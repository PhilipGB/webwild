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
    /// Keep the current garden as a durable previous-garden archive before a
    /// confirmed New Garden replaces it. Returns false when there is no valid
    /// save to keep, so the caller can refuse to discard anything.
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

        string? temp = null;
        try
        {
            var directory = Path.GetDirectoryName(ArchivePath);
            if (!string.IsNullOrWhiteSpace(directory) && !_fs.DirectoryExists(directory))
                _fs.CreateDirectory(directory);
            temp = ArchivePath + ".tmp-" + Guid.NewGuid().ToString("N");
            _fs.WriteAllBytes(temp, WorldSaveMigrator.ToBytes(manifest));
            var check = WorldSaveMigrator.FromBytes(_fs.ReadAllBytes(temp), out var error, out _);
            if (check is null)
            {
                _fs.Delete(temp);
                message = "Couldn't keep the previous garden. " + error;
                return false;
            }
            _fs.Move(temp, ArchivePath, true);
            temp = null;
            message = "Your previous garden is safe in the garden archive.";
            return true;
        }
        catch (Exception ex)
        {
            if (temp is not null)
            {
                try { if (_fs.FileExists(temp)) _fs.Delete(temp); } catch { }
            }
            message = "Couldn't keep the previous garden. " + ex.Message;
            return false;
        }
    }

    public LoadResult LoadArchive()
    {
        if (!_fs.FileExists(ArchivePath))
            return LoadResult.Fail("No previous garden is saved.");
        try
        {
            var manifest = WorldSaveMigrator.FromBytes(_fs.ReadAllBytes(ArchivePath), out var error, out _);
            if (manifest is null)
                return new LoadResult(false, null, false, "The previous garden could not be read.", error);
            return LoadResult.Ok(WorldSaveMigrator.ExtractGame(manifest), false);
        }
        catch (Exception ex)
        {
            return new LoadResult(false, null, false,
                "The previous garden could not be read.", ex.Message);
        }
    }
}
