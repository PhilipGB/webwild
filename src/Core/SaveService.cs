using System.Text.Json;

namespace Webwild.Core;

public interface ISaveFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    byte[] ReadAllBytes(string path);
    void WriteAllBytes(string path, byte[] bytes);
    void Move(string source, string destination, bool overwrite);
    void Copy(string source, string destination, bool overwrite);
    void Delete(string path);
    long Length(string path);
}

public sealed class DefaultSaveFileSystem : ISaveFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
    public void Move(string source, string destination, bool overwrite) => File.Move(source, destination, overwrite);
    public void Copy(string source, string destination, bool overwrite) => File.Copy(source, destination, overwrite);
    public void Delete(string path) => File.Delete(path);
    public long Length(string path) => new FileInfo(path).Length;
}

public static class SaveValidator
{
    public const int MaxFileBytes = 512_000;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static bool Validate(SaveGameData? data, out string error, IReadOnlySet<string>? allowedAnchors = null)
    {
        error = "";
        if (data is null)
        {
            error = "The save file is empty.";
            return false;
        }
        if (!SaveMigrator.Migrate(data, out error)) return false;
        if (!RegionIds.IsKnown(data.ActiveRegionId))
        {
            error = "The save names an unknown active region.";
            return false;
        }
        if (data.ActiveRegionId == RegionIds.ReedwaterBank && data.Missions.Completed.Count < 3)
        {
            error = "The save is inside Reedwater Bank before the garden story is complete.";
            return false;
        }
        if (data.VisitedRegions is null || data.VisitedRegions.Any(id => !RegionIds.IsKnown(id)))
        {
            error = "The save contains an unknown visited region.";
            return false;
        }
        if (data.RegionPlayers is null || data.RegionPlayers.Any(entry =>
                !RegionIds.IsKnown(entry.Region) ||
                !IsFinite(entry.X, entry.Y, entry.Z) ||
                Math.Abs(entry.X) > 60 || Math.Abs(entry.Z) > 60 || entry.Y < -3 || entry.Y > 40))
        {
            error = "A region player position is invalid.";
            return false;
        }
        if (data.RegionPlayers.Select(entry => entry.Region).Distinct(StringComparer.Ordinal).Count() !=
            data.RegionPlayers.Count)
        {
            error = "A region player position is duplicated.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(data.SaveId) || data.SaveId.Length > 80)
        {
            error = "The save ID is missing or invalid.";
            return false;
        }
        if (data.Food < 0 || data.Food > 9999 || data.Silk < 0 || data.Silk > 9999)
        {
            error = "Resources are outside the allowed range.";
            return false;
        }
        if (data.Tick < 0 || data.Seed < 0 || !double.IsFinite(data.ElapsedSeconds) || data.ElapsedSeconds < 0 || data.ElapsedSeconds > 100_000_000)
        {
            error = "Simulation timing is invalid.";
            return false;
        }
        if (!IsFinite(data.Player.X, data.Player.Y, data.Player.Z) ||
            Math.Abs(data.Player.X) > 60 || Math.Abs(data.Player.Z) > 60 ||
            data.Player.Y < -3 || data.Player.Y > 40)
        {
            error = "Player position is invalid.";
            return false;
        }
        if (data.Webs.Count > 50)
        {
            error = "Too many webs in this save.";
            return false;
        }
        Dictionary<string, string?> anchorRegions = allowedAnchors is null
            ? RegionCatalog.AllAnchors.ToDictionary(anchor => anchor.Id, anchor => (string?)anchor.Region, StringComparer.Ordinal)
            : allowedAnchors.ToDictionary(id => id, _ => (string?)null, StringComparer.Ordinal);
        var webIds = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var web in data.Webs)
        {
            if (string.IsNullOrWhiteSpace(web.Id) || !webIds.Add(web.Id))
            {
                error = "A web ID is missing or duplicated.";
                return false;
            }
            if (web.Type is not ("capture" or "bridge"))
            {
                error = "A web has an unknown type.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(web.Region) || !RegionIds.IsKnown(web.Region))
            {
                error = "A web names an unknown region.";
                return false;
            }
            if (!anchorRegions.TryGetValue(web.AnchorA, out var regionA) ||
                !anchorRegions.TryGetValue(web.AnchorB, out var regionB) ||
                web.AnchorA == web.AnchorB)
            {
                error = "A web refers to an unknown or identical anchor.";
                return false;
            }
            if (allowedAnchors is null && (regionA != web.Region || regionB != web.Region))
            {
                error = "A web spans two regions or the wrong region.";
                return false;
            }
            if (web.Durability < 0 || web.MaxDurability <= 0 || web.Durability > web.MaxDurability || web.MaxDurability > 1000)
            {
                error = "A web durability value is invalid.";
                return false;
            }
            if (!double.IsFinite(web.Exposure) || web.Exposure < 0 || web.Exposure > 1)
            {
                error = "A web exposure value is invalid.";
                return false;
            }
            var pair = string.CompareOrdinal(web.AnchorA, web.AnchorB) < 0
                ? web.AnchorA + "|" + web.AnchorB
                : web.AnchorB + "|" + web.AnchorA;
            if (!pairs.Add(pair))
            {
                error = "Two webs occupy the same anchors.";
                return false;
            }
        }
        if (data.Workers.Count > 4 || data.Workers.Count + 1 > 5)
        {
            error = "The save contains too many spiders.";
            return false;
        }
        var workerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var worker in data.Workers)
        {
            if (string.IsNullOrWhiteSpace(worker.Id) || !workerIds.Add(worker.Id))
            {
                error = "A worker ID is missing or duplicated.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(worker.Region) || !RegionIds.IsKnown(worker.Region))
            {
                error = "A worker names an unknown region.";
                return false;
            }
            if (!IsFinite(worker.X, worker.Y, worker.Z) || Math.Abs(worker.X) > 60 || Math.Abs(worker.Z) > 60 || worker.Y < -3 || worker.Y > 40)
            {
                error = "A worker position is invalid.";
                return false;
            }
            if (!Enum.TryParse<WorkerActivity>(worker.Activity, true, out _))
            {
                error = "A worker task state is unknown.";
                return false;
            }
        }
        var routeRegions = RegionCatalog.All
            .SelectMany(region => region.InsectRoutes.Select(route => new { route.Id, Region = region.Id }))
            .ToDictionary(entry => entry.Id, entry => entry.Region, StringComparer.Ordinal);
        foreach (var insect in data.Insects)
        {
            if (string.IsNullOrWhiteSpace(insect.Region) || !RegionIds.IsKnown(insect.Region))
            {
                error = "An insect names an unknown region.";
                return false;
            }
            if (!routeRegions.TryGetValue(insect.Id, out var routeRegion) || routeRegion != insect.Region)
            {
                error = "An insect does not match an authored regional route.";
                return false;
            }
            if (!IsFinite(insect.X, insect.Y, insect.Z))
            {
                error = "An insect position is invalid.";
                return false;
            }
            if (!Enum.TryParse<InsectPhase>(insect.Phase, true, out _))
            {
                error = "An insect state is unknown.";
                return false;
            }
            if (insect.TrappedWebId is not null && !webIds.Contains(insect.TrappedWebId))
            {
                error = "A trapped insect refers to a missing web.";
                return false;
            }
        }
        if (data.Missions.ActiveMissionIndex < 0 || data.Missions.ActiveMissionIndex > 3)
        {
            error = "Mission progress is invalid.";
            return false;
        }
        if (data.Missions.Progress.Count > 3 || data.Missions.Completed.Count > 3 || data.Missions.SeenEvents.Count > 2000)
        {
            error = "Mission progress is out of bounds.";
            return false;
        }
        if (data.Missions.SeenEvents.Any(string.IsNullOrWhiteSpace) ||
            data.Counters.LedgerKeys.Count > 2000 ||
            data.Counters.IssuedEventKeys.Count > 2000 ||
            data.Counters.OpenedRegionRoutes.Count > 20)
        {
            error = "Event history is invalid.";
            return false;
        }
        if (data.Counters.ClaimedSupplyCaches.Count > 20 ||
            data.Counters.ClaimedSupplyCaches.Any(string.IsNullOrWhiteSpace) ||
            data.Counters.ClaimedSupplyCaches.Any(id => !RegionIds.IsKnown(id)) ||
            data.Counters.ClaimedSupplyCaches.Distinct(StringComparer.Ordinal).Count() !=
            data.Counters.ClaimedSupplyCaches.Count)
        {
            error = "Supply cache history is invalid.";
            return false;
        }
        var regional = data.RegionalObjectives ?? new RegionalObjectiveSave();
        if (regional.ActiveObjectiveIndex < 0 || regional.ActiveObjectiveIndex > 3 ||
            regional.Progress.Count > 3 || regional.Completed.Count > 3 ||
            regional.SeenEvents.Count > 2000 ||
            regional.SeenEvents.Any(string.IsNullOrWhiteSpace))
        {
            error = "Regional objective progress is invalid.";
            return false;
        }
        if (data.Counters.WebSequence < 0 || data.Counters.WorkerSequence < 0 ||
            data.Counters.CaptureWebsBuilt < 0 || data.Counters.InsectsCollected < 0 ||
            data.Counters.ReedwaterCaptureWebsBuilt < 0 || data.Counters.ReedwaterBridgeWebsBuilt < 0 ||
            data.Counters.EmergencyUses < 0 || data.Counters.EmergencyUses > 3)
        {
            error = "Game counters are invalid.";
            return false;
        }
        if (!double.IsFinite(data.Counters.SilkAccumulator) || data.Counters.SilkAccumulator < 0 ||
            !double.IsFinite(data.Counters.PendingHatchRemaining) || data.Counters.PendingHatchRemaining < 0 || data.Counters.PendingHatchRemaining > 60 ||
            (!string.IsNullOrWhiteSpace(data.Counters.PendingHatchRegionId) &&
             !RegionIds.IsKnown(data.Counters.PendingHatchRegionId)))
        {
            error = "Simulation accumulators are invalid.";
            return false;
        }
        if (data.Weather.Phase is not ("Calm" or "Warning" or "Aftermath") ||
            !double.IsFinite(data.Weather.Remaining) || data.Weather.Remaining < 0 || data.Weather.Remaining > 200 ||
            !double.IsFinite(data.Weather.AftermathRemaining) || data.Weather.AftermathRemaining < 0)
        {
            error = "Weather state is invalid.";
            return false;
        }
        data.Settings?.Normalise();
        return true;
    }

    private static bool IsFinite(params double[] values) => values.All(double.IsFinite);

    public static SaveGameData? Deserialize(byte[] bytes, out string error)
    {
        error = "";
        if (bytes.Length == 0)
        {
            error = "The save file is empty.";
            return null;
        }
        if (bytes.Length > MaxFileBytes)
        {
            error = "The save file is unexpectedly large.";
            return null;
        }
        try
        {
            using var stream = new MemoryStream(bytes);
            var data = JsonSerializer.Deserialize<SaveGameData>(stream, JsonOptions);
            if (data is null)
            {
                error = "The save file is empty.";
                return null;
            }
            if (!SaveMigrator.Migrate(data, out error)) return null;
            if (!Validate(data, out error)) return null;
            return data;
        }
        catch (Exception ex)
        {
            error = "The save file is not valid JSON: " + ex.Message;
            return null;
        }
    }
}

/// <summary>Versioned saves with validation, healthy-backup rotation and explicit outcomes.</summary>
public sealed class SaveService
{
    private readonly ISaveFileSystem _fs;
    private readonly int _maxBytes;

    public string MainPath { get; }
    public string BackupPath { get; }

    public SaveService(string mainPath, string? backupPath = null, ISaveFileSystem? fs = null, int maxBytes = SaveValidator.MaxFileBytes)
    {
        MainPath = mainPath;
        BackupPath = backupPath ?? mainPath + ".bak";
        _fs = fs ?? new DefaultSaveFileSystem();
        _maxBytes = maxBytes;
    }

    public SaveResult Save(SaveGameData data)
    {
        if (string.IsNullOrWhiteSpace(data.SaveId)) data.SaveId = Guid.NewGuid().ToString("N");
        data.UpdatedUtc = DateTime.UtcNow;
        if (!SaveValidator.Validate(data, out var validationError))
            return SaveResult.Fail("Couldn't save. " + validationError, validationError);

        byte[] bytes;
        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(data, SaveValidator.JsonOptions);
        }
        catch (Exception ex)
        {
            return SaveResult.Fail("Couldn't save. Try again", ex.Message);
        }
        if (bytes.Length > _maxBytes)
            return SaveResult.Fail("Couldn't save: the save is too large.");

        string? temp = null;
        try
        {
            var directory = Path.GetDirectoryName(MainPath);
            if (!string.IsNullOrWhiteSpace(directory) && !_fs.DirectoryExists(directory)) _fs.CreateDirectory(directory);

            temp = MainPath + ".tmp-" + Guid.NewGuid().ToString("N");
            _fs.WriteAllBytes(temp, bytes);

            // Validate the bytes before touching healthy data.
            var check = SaveValidator.Deserialize(_fs.ReadAllBytes(temp), out var verifyError);
            if (check is null)
            {
                _fs.Delete(temp);
                return SaveResult.Fail("Couldn't save. Try again", verifyError);
            }

            if (_fs.FileExists(MainPath))
            {
                var existing = ReadRaw(MainPath, out _);
                if (existing is not null)
                {
                    try { _fs.Copy(MainPath, BackupPath, true); }
                    catch { /* A backup copy failure must not destroy the existing main. */ }
                }
            }

            _fs.Move(temp, MainPath, true);
            temp = null;
            return SaveResult.Ok(MainPath);
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

    public LoadResult Load()
    {
        if (_fs.FileExists(MainPath))
        {
            var main = ReadRaw(MainPath, out var mainError);
            if (main is not null)
                return LoadResult.Ok(main, false);
            mainError ??= "The main save is invalid.";
            if (_fs.FileExists(BackupPath))
            {
                var backup = ReadRaw(BackupPath, out var backupError);
                if (backup is not null)
                    return LoadResult.Ok(backup, true);
                return LoadResult.Fail("Couldn't load this save. The main and backup files are both invalid.",
                    $"main: {mainError}; backup: {backupError}");
            }
            return LoadResult.Fail("Couldn't load this save. Your file was left untouched.", mainError);
        }

        if (_fs.FileExists(BackupPath))
        {
            var backup = ReadRaw(BackupPath, out var backupError);
            if (backup is not null) return LoadResult.Ok(backup, true);
            return LoadResult.Fail("Couldn't load this save. The backup is invalid.", backupError);
        }
        return LoadResult.Fail("No saved game yet");
    }

    public bool HasAnySave => _fs.FileExists(MainPath) || _fs.FileExists(BackupPath);

    private SaveGameData? ReadRaw(string path, out string? error)
    {
        error = null;
        try
        {
            if (_fs.Length(path) > _maxBytes)
            {
                error = "The save file is unexpectedly large.";
                return null;
            }
            var data = SaveValidator.Deserialize(_fs.ReadAllBytes(path), out var message);
            if (data is null) error = message;
            return data;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}

public sealed class SettingsService
{
    private readonly ISaveFileSystem _fs;
    private readonly string _path;
    private readonly string _backup;

    public SettingsService(string path, ISaveFileSystem? fs = null)
    {
        _path = path;
        _backup = path + ".bak";
        _fs = fs ?? new DefaultSaveFileSystem();
    }

    public SettingsData Load()
    {
        foreach (var path in new[] { _path, _backup })
        {
            try
            {
                if (!_fs.FileExists(path)) continue;
                if (_fs.Length(path) > 32_000) continue;
                var json = System.Text.Encoding.UTF8.GetString(_fs.ReadAllBytes(path));
                var settings = JsonSerializer.Deserialize<SettingsData>(json, Tuning.JsonOptions);
                if (settings is null) continue;
                settings.Normalise();
                return settings;
            }
            catch
            {
                // Fall through to defaults; settings must never block play.
            }
        }
        return new SettingsData();
    }

    public SaveResult Save(SettingsData settings)
    {
        settings.Normalise();
        string? temp = null;
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir) && !_fs.DirectoryExists(dir)) _fs.CreateDirectory(dir);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, Tuning.JsonOptions);
            temp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            _fs.WriteAllBytes(temp, bytes);
            if (_fs.FileExists(_path)) _fs.Copy(_path, _backup, true);
            _fs.Move(temp, _path, true);
            temp = null;
            return SaveResult.Ok(_path);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup. A cleanup failure must never hide the real
            // reason the settings write failed, and the message must never claim
            // the save succeeded.
            if (temp is not null)
            {
                try
                {
                    if (_fs.FileExists(temp)) _fs.Delete(temp);
                }
                catch
                {
                    // The original exception remains the reported cause.
                }
            }
            return SaveResult.Fail("Couldn't save settings", ex.Message);
        }
    }
}
