namespace Webwild.Core;

/// <summary>
/// The live save path for the never-ending world. It owns both storage
/// generations:
///
///  - <see cref="WorldSaveStore"/> writes the schema-4 world manifest
///    (<c>world/world.json</c>) plus per-chunk changes. This is the path every
///    new save uses from v0.3.0 P2 onward.
///  - <see cref="SaveService"/> remains readable as the legacy schema-1–3 file
///    (<c>webwild_save.json</c>). A legacy save is migrated in memory and
///    immediately persisted as a manifest on first load; the legacy file is
///    left untouched as a rollback copy.
///
/// New-Garden archiving moves the current manifest to a durable
/// <c>world.previous.json</c>. Per-chunk records are archived with the chunk
/// store when chunk-owned changes exist (P6); until then the manifest is the
/// whole durable payload.
/// </summary>
public sealed class WorldSaveService
{
    public const string ArchiveFileName = "world.previous.json";

    private readonly ISaveFileSystem _fs;
    private readonly WorldSaveStore _world;
    private readonly SaveService _legacy;

    public string Directory { get; }
    public string ArchivePath { get; }
    public string LegacyPath => _legacy.MainPath;
    public WorldSaveStore WorldStore => _world;

    public WorldSaveService(string worldDirectory, string legacySavePath, ISaveFileSystem? fs = null)
    {
        _fs = fs ?? new DefaultSaveFileSystem();
        Directory = worldDirectory;
        _world = new WorldSaveStore(_fs, worldDirectory);
        _legacy = new SaveService(legacySavePath, fs: _fs);
        ArchivePath = Path.Combine(worldDirectory, ArchiveFileName);
    }

    /// <summary>Seed for a brand-new world. Probes force the historical fixed
    /// seed so headless runs stay deterministic; normal play gets a fresh one.</summary>
    public static int NewGameSeed(bool deterministic) =>
        deterministic ? 20260214 : Random.Shared.Next(1, int.MaxValue);

    /// <summary>
    /// Load the newest save. Manifests win; a legacy file is only consulted
    /// when no manifest exists at all, so a corrupt manifest can never
    /// silently resurrect older legacy progress.
    /// </summary>
    public LoadResult Load()
    {
        var world = _world.LoadManifest();
        if (world.Success && world.Manifest is not null)
        {
            try
            {
                var game = WorldSaveMigrator.ExtractGame(world.Manifest);
                return LoadResult.Ok(game, world.RecoveredFromBackup);
            }
            catch (Exception ex)
            {
                return new LoadResult(false, null, false,
                    "Couldn't load this save. The world manifest is inconsistent.", ex.Message);
            }
        }

        if (_world.HasManifest)
            return new LoadResult(false, null, false, world.Message, world.Error);

        var legacy = _legacy.Load();
        if (legacy.Success && legacy.Data is not null)
        {
            // Adopt the manifest write path now. A failed migration write must
            // not block the player: the legacy file is still intact.
            try
            {
                _world.SaveManifest(WorldSaveMigrator.ToManifest(legacy.Data));
            }
            catch
            {
                // Loading continues from the legacy bytes; the next save retries.
            }
            return LoadResult.Ok(legacy.Data, legacy.RecoveredFromBackup);
        }
        return new LoadResult(false, null, false, legacy.Message, legacy.Error);
    }

    /// <summary>
    /// Save the colony payload as a schema-4 manifest. <paramref name="data"/>'s
    /// player position is landmark-local (the established simulation contract);
    /// the manifest converts it to a stable world position through the constant
    /// landmark origin.
    /// </summary>
    public SaveResult Save(SaveGameData data)
    {
        if (data is null) return SaveResult.Fail("Couldn't save. The game state is missing.");
        try
        {
            return _world.SaveManifest(WorldSaveMigrator.ToManifest(data));
        }
        catch (Exception ex)
        {
            return SaveResult.Fail("Couldn't save. Try again", ex.Message);
        }
    }

    public bool HasAnySave => _world.HasManifest || _legacy.HasAnySave;

    public bool HasArchive
    {
        get
        {
            if (!_fs.FileExists(ArchivePath)) return false;
            try
            {
                return WorldSaveMigrator.FromBytes(_fs.ReadAllBytes(ArchivePath), out _, out _) is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Save the current session and then archive it as one safe New-Garden
    /// preparation step. A failed save or archive returns false and leaves the
    /// current garden live; only a verified archive permits replacement.
    /// </summary>
    public bool PrepareNewGarden(SaveGameData current, out string message)
    {
        var saved = Save(current);
        if (!saved.Success)
        {
            message = "Couldn't save the current garden before starting a new one. " + saved.Message;
            return false;
        }
        return ArchiveExisting(out message);
    }

    /// <summary>
    /// Keep the current garden as a durable previous-garden archive before a
    /// confirmed New Garden replaces it. The manifest and every chunk record
    /// are staged and verified before publication; live data is only retired
    /// after the archive is complete. Returns false when the existing garden
    /// could not be kept, so the caller can refuse to discard anything.
    /// </summary>
    public bool ArchiveExisting(out string message)
    {
        message = "";
        WorldManifestSave? manifest = null;

        var world = _world.LoadManifest();
        if (world.Success && world.Manifest is not null)
        {
            manifest = world.Manifest;
        }
        else if (!_world.HasManifest)
        {
            var legacy = _legacy.Load();
            if (legacy.Success && legacy.Data is not null)
            {
                try { manifest = WorldSaveMigrator.ToManifest(legacy.Data); }
                catch (Exception ex)
                {
                    message = "Couldn't keep the previous garden. " + ex.Message;
                    return false;
                }
            }
        }

        if (manifest is null)
        {
            message = "There is no saved garden to keep.";
            return false;
        }

        var staging = Path.Combine(Directory, "archive-staging-" + Guid.NewGuid().ToString("N"));
        var chunkStaging = Path.Combine(staging, "chunks");
        string? tempManifest = null;
        try
        {
            if (!_fs.DirectoryExists(staging)) _fs.CreateDirectory(staging);

            // Stage and verify the manifest bytes first.
            tempManifest = Path.Combine(staging, ArchiveFileName);
            _fs.WriteAllBytes(tempManifest, WorldSaveMigrator.ToBytes(manifest));
            var check = WorldSaveMigrator.FromBytes(_fs.ReadAllBytes(tempManifest), out var error, out _);
            if (check is null || check.Seed != manifest.Seed || check.SaveId != manifest.SaveId)
            {
                message = "Couldn't verify the previous garden archive. " + error;
                return false;
            }

            // Copy and verify every chunk record while stamping the world seed.
            var chunks = _world.StageChunkArchive(chunkStaging, manifest.Seed);
            if (!chunks.Success)
            {
                message = chunks.Message + " " + chunks.Error;
                return false;
            }

            // Publish the manifest and chunks as one transaction. The old
            // archive is moved aside first; any failure restores it exactly,
            // and the live garden is never touched until both are verified.
            var archiveChunks = Path.Combine(Directory, "previous-chunks");
            var hadManifest = _fs.FileExists(ArchivePath);
            var hadChunks = _fs.DirectoryExists(archiveChunks);
            string? manifestBackup = null;
            string? chunksBackup = null;
            var manifestPublished = false;
            var chunksInstalled = false;
            try
            {
                if (hadManifest)
                {
                    var backup = ArchivePath + ".backup-" + Guid.NewGuid().ToString("N");
                    _fs.Move(ArchivePath, backup, true);
                    manifestBackup = backup;
                }
                if (hadChunks)
                {
                    var backup = archiveChunks + ".backup-" + Guid.NewGuid().ToString("N");
                    _fs.MoveDirectory(archiveChunks, backup);
                    chunksBackup = backup;
                }

                _fs.Move(tempManifest, ArchivePath, true);
                tempManifest = null;
                manifestPublished = true;
                var installed = _world.InstallStagedChunkArchive(chunkStaging, archiveChunks);
                if (!installed.Success)
                    throw new IOException(installed.Message + " " + installed.Error);
                chunksInstalled = true;

                // Verify the published archive before retiring live records.
                if (!HasArchive)
                    throw new IOException("The published previous garden archive could not be verified.");
            }
            catch (Exception publishError)
            {
                try
                {
                    if (manifestPublished && _fs.FileExists(ArchivePath)) _fs.Delete(ArchivePath);
                }
                catch { }
                try
                {
                    if (manifestBackup is not null)
                    {
                        _fs.Move(manifestBackup, ArchivePath, true);
                        manifestBackup = null;
                    }
                }
                catch { }
                try
                {
                    if (chunksInstalled || chunksBackup is not null || !hadChunks)
                    {
                        if (_fs.DirectoryExists(archiveChunks)) _fs.DeleteDirectory(archiveChunks);
                    }
                }
                catch { }
                try
                {
                    if (chunksBackup is not null)
                    {
                        _fs.MoveDirectory(chunksBackup, archiveChunks);
                        chunksBackup = null;
                    }
                }
                catch { }
                message = "Couldn't keep the previous garden. " + publishError.Message;
                return false;
            }
            try { if (manifestBackup is not null && _fs.FileExists(manifestBackup)) _fs.Delete(manifestBackup); }
            catch { }
            try { if (chunksBackup is not null && _fs.DirectoryExists(chunksBackup)) _fs.DeleteDirectory(chunksBackup); }
            catch { }
            var retired = Path.Combine(Directory, "chunks.previous-" + Guid.NewGuid().ToString("N"));
            var retire = _world.RetireLiveChunkChanges(retired);
            if (!retire.Success)
            {
                // The archive is complete and the old live garden is untouched;
                // refusing the replacement is the safe outcome.
                message = retire.Message + " " + retire.Error +
                          " Your previous garden is safe in the archive; the current garden is unchanged.";
                return false;
            }
            message = "Your previous garden is safe in the garden archive.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Couldn't keep the previous garden. " + ex.Message;
            return false;
        }
        finally
        {
            if (tempManifest is not null)
            {
                try { if (_fs.FileExists(tempManifest)) _fs.Delete(tempManifest); } catch { }
            }
            try
            {
                if (_fs.DirectoryExists(staging)) _fs.DeleteDirectory(staging);
            }
            catch { }
        }
    }

    /// <summary>
    /// Restore the previous garden manifest and its archived chunk records.
    /// Chunks are staged and verified, then swapped atomically; the live save
    /// is only replaced after the restore payload is complete.
    /// </summary>
    public LoadResult RestoreArchive()
    {
        if (!_fs.FileExists(ArchivePath))
            return LoadResult.Fail("No previous garden is saved.");
        WorldManifestSave? manifest;
        try
        {
            manifest = WorldSaveMigrator.FromBytes(
                _fs.ReadAllBytes(ArchivePath), out var error, out _);
            if (manifest is null || !WorldSaveMigrator.Validate(manifest, out var manifestError))
            {
                return new LoadResult(false, null, false,
                    "The previous garden could not be read.", error ?? "The archive is invalid.");
            }
        }
        catch (Exception ex)
        {
            return new LoadResult(false, null, false,
                "The previous garden could not be read.", ex.Message);
        }

        var staging = Path.Combine(Directory, "restore-staging-" + Guid.NewGuid().ToString("N"));
        var archivedChunks = Path.Combine(Directory, "previous-chunks");
        try
        {
            if (!_fs.DirectoryExists(staging)) _fs.CreateDirectory(staging);
            foreach (var coordinate in _world.ListChunkRecordsIn(archivedChunks))
            {
                var source = Path.Combine(archivedChunks, $"chunk_{coordinate.Id}.json");
                try
                {
                    if (_fs.Length(source) > WorldSaveStore.MaxChunkRecordBytes) continue;
                    var record = System.Text.Json.JsonSerializer.Deserialize<ChunkChangeSave>(
                        _fs.ReadAllBytes(source), SaveValidator.JsonOptions);
                    if (record is null || !WorldSaveStore.ValidateChunkRecord(record, out _)) continue;
                    // Ignore records from another world generation. Stamped
                    // archives always match; pre-stamp records with seed 0 are
                    // adopted only when the archive has no stamps.
                    if (record.WorldSeed != 0 && record.WorldSeed != manifest.Seed) continue;
                    record.WorldSeed = manifest.Seed;
                    _fs.WriteAllBytes(Path.Combine(staging, $"chunk_{coordinate.Id}.json"),
                        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                            record, SaveValidator.JsonOptions));
                }
                catch
                {
                    // A single unreadable archived chunk must not make the whole
                    // previous garden unusable; baseline content regenerates.
                }
            }

            var chunksDirectory = Path.Combine(Directory, "chunks");
            var previousLive = Path.Combine(Directory, "chunks.pre-restore-" + Guid.NewGuid().ToString("N"));
            var hadLive = _fs.DirectoryExists(chunksDirectory);
            if (hadLive) _fs.MoveDirectory(chunksDirectory, previousLive);
            try
            {
                _fs.MoveDirectory(staging, chunksDirectory);
            }
            catch
            {
                if (hadLive && !_fs.DirectoryExists(chunksDirectory))
                    _fs.MoveDirectory(previousLive, chunksDirectory);
                throw;
            }

            var save = _world.SaveManifest(manifest);
            if (!save.Success)
            {
                // Put the new garden's live chunk directory back; its manifest
                // was never replaced, so the session is still coherent.
                try
                {
                    var failedRestore = chunksDirectory + ".failed-" + Guid.NewGuid().ToString("N");
                    _fs.MoveDirectory(chunksDirectory, failedRestore);
                    if (hadLive) _fs.MoveDirectory(previousLive, chunksDirectory);
                    _fs.DeleteDirectory(failedRestore);
                }
                catch { }
                return new LoadResult(false, null, false,
                    "The previous garden could not be restored.", save.Error ?? save.Message);
            }
            if (hadLive)
            {
                try { _fs.DeleteDirectory(previousLive); } catch { }
            }
            return LoadResult.Ok(WorldSaveMigrator.ExtractGame(manifest), false);
        }
        catch (Exception ex)
        {
            return new LoadResult(false, null, false,
                "The previous garden could not be restored.", ex.Message);
        }
        finally
        {
            try
            {
                if (_fs.DirectoryExists(staging)) _fs.DeleteDirectory(staging);
            }
            catch { }
        }
    }

    /// <summary>Read the previous garden without changing the live world.</summary>
    public LoadResult LoadArchive()
    {
        if (!_fs.FileExists(ArchivePath))
            return LoadResult.Fail("No previous garden is saved.");
        try
        {
            var manifest = WorldSaveMigrator.FromBytes(
                _fs.ReadAllBytes(ArchivePath), out var error, out _);
            if (manifest is null)
                return new LoadResult(false, null, false,
                    "The previous garden could not be read.", error);
            return LoadResult.Ok(WorldSaveMigrator.ExtractGame(manifest), false);
        }
        catch (Exception ex)
        {
            return new LoadResult(false, null, false,
                "The previous garden could not be read.", ex.Message);
        }
    }}
