using System.Text.Json;
using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class SaveTests : IDisposable
{
    private readonly string _dir;

    public SaveTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "webwild-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private (SaveService Service, string Main, string Backup) NewService(ISaveFileSystem? fs = null)
    {
        var main = Path.Combine(_dir, "slot1.json");
        var backup = main + ".bak";
        return (new SaveService(main, backup, fs), main, backup);
    }

    private static SaveGameData FreshSave(GameSimulation sim, int tick = 0)
    {
        var data = sim.ExportSave(new Vec3(-13, 0.5, 8), 0, new SettingsData());
        data.Tick = tick;
        return data;
    }

    [Fact]
    public void SaveRoundtrip_PreservesResourcesWebsAndMissionState()
    {
        var sim = TestWorld.NewSim();
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out var web, out _));
        sim.AdvanceSeconds(12);
        var insect = sim.Insects.First(i => i.CanBeCollected);
        Assert.True(sim.TryCollectInsect(insect.Id, "player", out _, out _));

        var (service, main, _) = NewService();
        var save = sim.ExportSave(new Vec3(-8, 0.5, 0), 1.2, new SettingsData { ReducedMotion = true });
        var result = service.Save(save);
        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(main));

        var loaded = service.Load();
        Assert.True(loaded.Success, loaded.Error);
        Assert.False(loaded.RecoveredFromBackup);
        Assert.Equal(sim.Ledger.Food, loaded.Data!.Food);
        Assert.Equal(sim.Ledger.Silk, loaded.Data.Silk);
        Assert.Single(loaded.Data.Webs);
        Assert.Equal(web!.Id, loaded.Data.Webs[0].Id);
        Assert.Equal("M01", loaded.Data.Missions.Completed.Single());
        Assert.Equal(1, loaded.Data.Missions.ActiveMissionIndex);
        Assert.True(loaded.Data.Settings.ReducedMotion);

        var restored = GameSimulation.FromSave(loaded.Data, TestWorld.RichTuning(), MissionsConfig.Defaults());
        Assert.Equal(sim.Ledger.Food, restored.Ledger.Food);
        Assert.Single(restored.Webs);
        Assert.Equal("M02", restored.Missions.ActiveMissionId);
        Assert.Equal(sim.TickCount, restored.TickCount);
    }

    [Fact]
    public void CorruptMain_RecoversHealthyBackup_AndReportsClearly()
    {
        var (service, main, backup) = NewService();
        var sim = TestWorld.NewSim();
        var first = sim.ExportSave(Vec3.Zero, 0, new SettingsData());
        first.Tick = 11;
        Assert.True(service.Save(first).Success);

        var second = sim.ExportSave(Vec3.Zero, 0, new SettingsData());
        second.Tick = 22;
        Assert.True(service.Save(second).Success);
        Assert.True(File.Exists(backup));

        File.WriteAllText(main, "{ this is not valid json ]");
        var loaded = service.Load();
        Assert.True(loaded.Success, loaded.Error);
        Assert.True(loaded.RecoveredFromBackup);
        Assert.Contains("Recovered", loaded.Message);
        Assert.Equal(11, loaded.Data!.Tick);
        Assert.True(File.Exists(main)); // corrupt artifact left untouched, not silently deleted
    }

    [Fact]
    public void InvalidSchema_IsRejectedWithNoFalseSuccess()
    {
        var (service, main, _) = NewService();
        var data = FreshSave(TestWorld.NewSim());
        var json = JsonSerializer.Serialize(data, SaveValidator.JsonOptions);
        json = json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");
        File.WriteAllText(main, json);

        var loaded = service.Load();
        Assert.False(loaded.Success);
        Assert.Null(loaded.Data);
        Assert.Contains("Couldn't load", loaded.Message);
        Assert.Contains("99", loaded.Error ?? "");
    }

    [Fact]
    public void FailedWrite_LeavesOldSaveIntact_AndNeverSaysSaved()
    {
        var (good, main, _) = NewService();
        var original = FreshSave(TestWorld.NewSim());
        original.Tick = 7;
        Assert.True(good.Save(original).Success);

        var failing = new FailingSaveFileSystem { FailWrites = true };
        var (bad, _, _) = NewService(failing);
        var update = FreshSave(TestWorld.NewSim());
        update.Tick = 999;
        var result = bad.Save(update);
        Assert.False(result.Success);
        Assert.NotEqual("Saved", result.Message);
        Assert.Contains("Couldn't save", result.Message);

        var reloaded = good.Load();
        Assert.True(reloaded.Success);
        Assert.Equal(7, reloaded.Data!.Tick);
        Assert.False(File.Exists(main + ".tmp"));
    }

    [Fact]
    public void Validation_RejectsNegativeResourcesNaNAndTooManySpiders()
    {
        var data = FreshSave(TestWorld.NewSim());
        data.Food = -1;
        Assert.False(SaveValidator.Validate(data, out var error));
        Assert.Contains("Resources", error);

        data = FreshSave(TestWorld.NewSim());
        data.Player.X = double.NaN;
        Assert.False(SaveValidator.Validate(data, out error));
        Assert.Contains("position", error, StringComparison.OrdinalIgnoreCase);

        data = FreshSave(TestWorld.NewSim());
        for (var i = 0; i < 5; i++)
            data.Workers.Add(new WorkerSave { Id = "w" + i, X = 1, Y = 1, Z = 1, Activity = "Resting" });
        Assert.False(SaveValidator.Validate(data, out error));
        Assert.Contains("spiders", error);

        data = FreshSave(TestWorld.NewSim());
        data.Webs.Add(new WebSave { Id = "dup", Type = "capture", AnchorA = WorldLayout.TwigA, AnchorB = WorldLayout.TwigB, Durability = 100 });
        data.Webs.Add(new WebSave { Id = "dup", Type = "capture", AnchorA = WorldLayout.FlowerA, AnchorB = WorldLayout.FlowerB, Durability = 100 });
        Assert.False(SaveValidator.Validate(data, out error));
        Assert.Contains("duplicated", error);
    }

    [Fact]
    public void RestoreAfterM02_PreservesWorkerRouteAndNoDuplicateRewards()
    {
        var sim = TestWorld.NewSim();
        Assert.True(sim.TryBuildWeb(WorldLayout.GapWest, WorldLayout.GapEast, WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out _, out _));
        sim.AdvanceSeconds(12);
        sim.TryStartHatch(out _);
        sim.AdvanceSeconds(10);
        var worker = Assert.Single(sim.Workers);
        var data = sim.ExportSave(Vec3.Zero, 0, new SettingsData());

        var restored = GameSimulation.FromSave(data, TestWorld.RichTuning(), MissionsConfig.Defaults());
        Assert.Single(restored.Workers);
        var restoredWorker = restored.Workers[0];
        Assert.Equal(worker.PathNodes.Count, restoredWorker.PathNodes.Count);
        Assert.Equal(worker.CurrentNode, restoredWorker.CurrentNode);
        var foodBefore = restored.Ledger.Food;
        var trapped = restored.Insects.FirstOrDefault(i => i.CanBeCollected);
        if (trapped is not null)
        {
            Assert.True(restored.TryCollectInsect(trapped.Id, "player", out _, out _));
            Assert.Equal(foodBefore + trapped.Definition.FoodYield, restored.Ledger.Food);
            Assert.False(restored.TryCollectInsect(trapped.Id, "player", out _, out _));
        }
    }

    [Fact]
    public void SavingDuringHatch_RestoresPendingHatch()
    {
        var sim = TestWorld.NewSim();
        Assert.True(sim.TryStartHatch(out _));
        sim.AdvanceSeconds(1);
        var data = sim.ExportSave(Vec3.Zero, 0, new SettingsData());
        Assert.True(SaveValidator.Validate(data, out var error), error);
        var restored = GameSimulation.FromSave(data, TestWorld.RichTuning(), MissionsConfig.Defaults());
        Assert.NotNull(restored.PendingHatch);
        restored.AdvanceSeconds(4);
        Assert.Single(restored.Workers);
    }

    [Fact]
    public void Settings_RoundtripThroughAtomicFile()
    {
        var path = Path.Combine(_dir, "settings.json");
        var service = new SettingsService(path);
        var result = service.Save(new SettingsData
        {
            MasterVolume = 0.4, MusicVolume = 0.2, SfxVolume = 0.6,
            UiScalePercent = 125, ReducedMotion = true, LowThreat = true,
            Fullscreen = true, CameraSensitivity = 1.4
        });
        Assert.True(result.Success, result.Error);
        var loaded = service.Load();
        Assert.Equal(0.4, loaded.MasterVolume, 3);
        Assert.Equal(125, loaded.UiScalePercent);
        Assert.True(loaded.ReducedMotion);
        Assert.True(loaded.LowThreat);
        Assert.True(loaded.Fullscreen);
    }

    private sealed class FailingSaveFileSystem : ISaveFileSystem
    {
        private readonly DefaultSaveFileSystem _inner = new();
        public bool FailWrites { get; set; }
        public bool FileExists(string path) => _inner.FileExists(path);
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);
        public void WriteAllBytes(string path, byte[] bytes)
        {
            if (FailWrites) throw new IOException("Simulated read-only storage");
            _inner.WriteAllBytes(path, bytes);
        }
        public void Move(string source, string destination, bool overwrite) => _inner.Move(source, destination, overwrite);
        public void Copy(string source, string destination, bool overwrite) => _inner.Copy(source, destination, overwrite);
        public void Delete(string path) => _inner.Delete(path);
        public long Length(string path) => _inner.Length(path);
    }
}
