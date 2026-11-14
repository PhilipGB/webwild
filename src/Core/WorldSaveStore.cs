namespace Webwild.Core;

/// <summary>
/// Durable, scalable world persistence.
///
/// Layout:
///   <c>&lt;dir&gt;/world.json</c>            the small world manifest (+ rotating .bak)
///   <c>&lt;dir&gt;/chunks/chunk_X_Z.json</c>  player-made changes for one chunk
///
/// All writes are atomic (temp file → validate → move) so an interrupted save
/// never damages healthy data, mirroring the established SaveService contract.
/// Leftover temp files from a crash are cleaned up on load.
/// </summary>
public sealed class WorldSaveStore
{
    public const int MaxManifestBytes = 512_000;
    public const int MaxChunkRecordBytes = 64_000;

    private readonly ISaveFileSystem _fs;
    private readonly string _directory;
    private readonly string _chunksDirectory;

    public string ManifestPath { get; }
    public string ManifestBackupPath { get; }

    public WorldSaveStore(ISaveFileSystem? fs = null, string? directory = null)
    {
        _fs = fs ?? new DefaultSaveFileSystem();
        _directory = directory ?? "world";
        _chunksDirectory = Path.Combine(_directory, "chunks");
        ManifestPath = Path.Combine(_directory, "world.json");
        ManifestBackupPath = ManifestPath + ".bak";
    }

    // ---------------------------------------------------------------- manifest

    public SaveResult SaveManifest(WorldManifestSave manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.SaveId)) manifest.SaveId = Guid.NewGuid().ToString("N");
        manifest.UpdatedUtc = DateTime.UtcNow;
        if (!WorldSaveMigrator.Validate(manifest, out var validationError))
            return SaveResult.Fail("Couldn't save. " + validationError, validationError);

        byte[] bytes;
        try
        {
            bytes = WorldSaveMigrator.ToBytes(manifest);
        }
        catch (Exception ex)
        {
            return SaveResult.Fail("Couldn't save. Try again", ex.Message);
        }
        if (bytes.Length > MaxManifestBytes)
            return SaveResult.Fail("Couldn't save: the save is too large.");

        string? temp = null;
        try
        {
            EnsureDirectories();
            temp = ManifestPath + ".tmp-" + Guid.NewGuid().ToString("N");
            _fs.WriteAllBytes(temp, bytes);

            var check = WorldSaveMigrator.FromBytes(_fs.ReadAllBytes(temp), out var verifyError, out _);
            if (check is null)
            {
                _fs.Delete(temp);
                return SaveResult.Fail("Couldn't save. Try again", verifyError);
            }

            if (_fs.FileExists(ManifestPath))
            {
                try { _fs.Copy(ManifestPath, ManifestBackupPath, true); }
                catch { /* A failed backup copy must not destroy the existing main. */ }
            }

            _fs.Move(temp, ManifestPath, true);
            temp = null;
            return SaveResult.Ok(ManifestPath);
        }
        catch (Exception ex)
        {
            if (temp is not null)
            {
                try { if (_fs.FileExists(temp)) _fs.Delete(temp); } catch { }
            }
            return SaveResult.Fail("Couldn't save. Try again", ex.Message);
        }
    }

    public WorldLoadResult LoadManifest()
    {
        CleanTempFiles();
        if (_fs.FileExists(ManifestPath))
        {
            var main = ReadManifest(ManifestPath, out var mainError);
            if (main is not null) return WorldLoadResult.Ok(main, false);
            mainError ??= "The main save is invalid.";
            if (_fs.FileExists(ManifestBackupPath))
            {
                var backup = ReadManifest(ManifestBackupPath, out var backupError);
                if (backup is not null) return WorldLoadResult.Recovered(backup);
                return WorldLoadResult.Fail(
                    "Couldn't load this save. The main and backup files are both invalid.",
                    $"main: {mainError}; backup: {backupError}");
            }
            return WorldLoadResult.Fail("Couldn't load this save. Your file was left untouched.", mainError);
        }

        if (_fs.FileExists(ManifestBackupPath))
        {
            var backup = ReadManifest(ManifestBackupPath, out var backupError);
            if (backup is not null) return WorldLoadResult.Recovered(backup);
            return WorldLoadResult.Fail("Couldn't load this save. The backup is invalid.", backupError);
        }
        return WorldLoadResult.Fail("No saved game yet");
    }

    private WorldManifestSave? ReadManifest(string path, out string? error)
    {
        error = null;
        try
        {
            if (_fs.Length(path) > MaxManifestBytes)
            {
                error = "The save file is unexpectedly large.";
                return null;
            }
            var data = WorldSaveMigrator.FromBytes(_fs.ReadAllBytes(path), out var message, out _);
            if (data is null) error = message;
            return data;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    // ------------------------------------------------------------- chunk files

    private string ChunkPath(ChunkCoordinate coordinate) =>
        Path.Combine(_chunksDirectory, $"chunk_{coordinate.Id}.json");

    public SaveResult SaveChunkChanges(ChunkChangeSave record)
    {
        record ??= new ChunkChangeSave();
        record.SchemaVersion = ChunkChangeSave.CurrentSchemaVersion;
        if (!ValidateChunkRecord(record, out var error))
            return SaveResult.Fail("Couldn't save chunk. " + error, error);

        byte[] bytes;
        try
        {
            bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(record, SaveValidator.JsonOptions);
        }
        catch (Exception ex)
        {
            return SaveResult.Fail("Couldn't save chunk. Try again", ex.Message);
        }
        if (bytes.Length > MaxChunkRecordBytes)
            return SaveResult.Fail("Couldn't save chunk: too many changes in one chunk.");

        var path = ChunkPath(new ChunkCoordinate(record.ChunkX, record.ChunkZ));
        string? temp = null;
        try
        {
            EnsureDirectories();
            temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            _fs.WriteAllBytes(temp, bytes);

            var check = DeserializeChunkRecord(_fs.ReadAllBytes(temp), out var verifyError);
            if (check is null)
            {
                _fs.Delete(temp);
                return SaveResult.Fail("Couldn't save chunk. Try again", verifyError);
            }

            _fs.Move(temp, path, true);
            temp = null;
            return SaveResult.Ok(path);
        }
        catch (Exception ex)
        {
            if (temp is not null)
            {
                try { if (_fs.FileExists(temp)) _fs.Delete(temp); } catch { }
            }
            return SaveResult.Fail("Couldn't save chunk. Try again", ex.Message);
        }
    }

    /// <summary>Load one chunk's player-made changes, or null when the chunk is
    /// unmodified or its record is unreadable (the record is simply ignored:
    /// baseline terrain regenerates and the next save rewrites healthy data).</summary>
    public ChunkChangeSave? LoadChunkChanges(ChunkCoordinate coordinate, out string? error)
    {
        error = null;
        var path = ChunkPath(coordinate);
        if (!_fs.FileExists(path)) return null;
        try
        {
            if (_fs.Length(path) > MaxChunkRecordBytes)
            {
                error = "Chunk record is unexpectedly large.";
                return null;
            }
            var record = DeserializeChunkRecord(_fs.ReadAllBytes(path), out var message);
            if (record is null)
            {
                error = message;
                return null;
            }
            if (record.ChunkX != coordinate.X || record.ChunkZ != coordinate.Z)
            {
                error = "Chunk record does not match its file.";
                return null;
            }
            return record;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public bool DeleteChunkChanges(ChunkCoordinate coordinate)
    {
        var path = ChunkPath(coordinate);
        if (!_fs.FileExists(path)) return false;
        try { _fs.Delete(path); return true; }
        catch { return false; }
    }

    /// <summary>Chunk ids that currently have a persisted change record.</summary>
    public IReadOnlyList<ChunkCoordinate> ListChangedChunks()
    {
        if (!_fs.DirectoryExists(_chunksDirectory)) return Array.Empty<ChunkCoordinate>();
        var result = new List<ChunkCoordinate>();
        try
        {
            // ISaveFileSystem cannot enumerate; use the concrete file system when available.
            if (_fs is DefaultSaveFileSystem && Directory.Exists(_chunksDirectory))
            {
                foreach (var file in Directory.GetFiles(_chunksDirectory, "chunk_*.json"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (name.StartsWith("chunk_", StringComparison.Ordinal) &&
                        ChunkCoordinate.TryParse(name["chunk_".Length..], out var coordinate))
                        result.Add(coordinate);
                }
            }
        }
        catch
        {
            // Enumeration failures degrade to an empty index; chunk data is
            // still loadable by explicit coordinate.
        }
        result.Sort();
        return result;
    }

    private void CleanTempFiles()
    {
        if (_fs is not DefaultSaveFileSystem) return;
        try
        {
            if (Directory.Exists(_directory))
                foreach (var file in Directory.GetFiles(_directory, "*.tmp-*"))
                    File.Delete(file);
            if (Directory.Exists(_chunksDirectory))
                foreach (var file in Directory.GetFiles(_chunksDirectory, "*.tmp-*"))
                    File.Delete(file);
        }
        catch
        {
            // Temp cleanup is best effort; a leftover temp file is never read.
        }
    }

    private void EnsureDirectories()
    {
        if (!string.IsNullOrWhiteSpace(_directory) && !_fs.DirectoryExists(_directory))
            _fs.CreateDirectory(_directory);
        if (!_fs.DirectoryExists(_chunksDirectory)) _fs.CreateDirectory(_chunksDirectory);
    }

    // -------------------------------------------------------------- validation

    public static bool ValidateChunkRecord(ChunkChangeSave? record, out string error)
    {
        error = "";
        if (record is null)
        {
            error = "The chunk record is empty.";
            return false;
        }
        if (record.SchemaVersion != ChunkChangeSave.CurrentSchemaVersion)
        {
            error = $"Chunk record version {record.SchemaVersion} is not supported.";
            return false;
        }
        if (!ChunkCoordinateExtensions.TryValidate(record.ChunkX) ||
            !ChunkCoordinateExtensions.TryValidate(record.ChunkZ))
        {
            error = "The chunk record names an invalid chunk.";
            return false;
        }        if (record.BuiltWebIds is null || record.BuiltWebIds.Count > 60 ||
            record.BuiltWebIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 40) ||
            record.BuiltWebIds.Distinct(StringComparer.Ordinal).Count() != record.BuiltWebIds.Count)
        {
            error = "The chunk web index is invalid.";
            return false;
        }
        if (record.FoodSources is null || record.FoodSources.Count > 200)
        {
            error = "The chunk food record has too many entries.";
            return false;
        }
        foreach (var food in record.FoodSources)
        {
            if (food is null || string.IsNullOrWhiteSpace(food.Id) || food.Id.Length > 80 ||
                !double.IsFinite(food.X) || !double.IsFinite(food.Y) || !double.IsFinite(food.Z) ||
                Math.Abs(food.X) > 1e9 || Math.Abs(food.Z) > 1e9 ||
                string.IsNullOrWhiteSpace(food.Kind) || food.Kind.Length > 24)
            {
                error = "A chunk food entry is invalid.";
                return false;
            }
        }
        if (record.Markers is null || record.Markers.Count > 100)
        {
            error = "The chunk marker record is too large.";
            return false;
        }
        foreach (var marker in record.Markers)
        {
            if (marker is null || string.IsNullOrWhiteSpace(marker.Id) || marker.Id.Length > 60 ||
                !double.IsFinite(marker.X) || !double.IsFinite(marker.Y) || !double.IsFinite(marker.Z) ||
                Math.Abs(marker.X) > 1e9 || Math.Abs(marker.Z) > 1e9 ||
                (marker.Label?.Length ?? 0) > 60)
            {
                error = "A chunk marker entry is invalid.";
                return false;
            }
        }
        return true;
    }

    private static ChunkChangeSave? DeserializeChunkRecord(byte[] bytes, out string error)
    {
        error = "";
        if (bytes.Length == 0)
        {
            error = "The chunk record is empty.";
            return null;
        }
        try
        {
            var record = System.Text.Json.JsonSerializer.Deserialize<ChunkChangeSave>(bytes, SaveValidator.JsonOptions);
            if (record is null)
            {
                error = "The chunk record is empty.";
                return null;
            }
            if (!ValidateChunkRecord(record, out error)) return null;
            return record;
        }
        catch (Exception ex)
        {
            error = "The chunk record is not valid JSON: " + ex.Message;
            return null;
        }
    }
}

/// <summary>Load result carrying a world manifest instead of a legacy payload.</summary>
public sealed record WorldLoadResult(bool Success, WorldManifestSave? Manifest, bool RecoveredFromBackup, string Message, string? Error = null)
{
    public static WorldLoadResult Ok(WorldManifestSave manifest, bool recovered) =>
        new(true, manifest, recovered, recovered ? "Recovered your last good save" : "Save loaded");

    public static WorldLoadResult Recovered(WorldManifestSave manifest) => Ok(manifest, true);

    public static WorldLoadResult Fail(string message, string? error = null) =>
        new(false, null, false, message, error);
}
