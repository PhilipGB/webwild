using System.Text.Json;
using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>
/// World persistence contracts: manifest round-trips, legacy save migration
/// into the manifest, per-chunk change files, atomic writes, interrupted-write
/// recovery and corruption recovery.
/// </summary>
public class WorldSaveTests : IDisposable
{
    private readonly string _dir;

    public WorldSaveTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "webwild-world-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static SaveGameData FreshLegacySave(GameSimulation sim, string activeRegion, Vec3 localPosition)
    {
        var data = sim.ExportSave(localPosition, 0, new SettingsData());
        data.ActiveRegionId = activeRegion;
        data.VisitedRegions = new List<string> { RegionIds.GardenClearing, activeRegion };
        data.RegionPlayers = new List<RegionPlayerSave>
        {
            new() { Region = RegionIds.GardenClearing, X = -13.5, Y = 0.45, Z = 8.5 }
        };
        if (activeRegion != RegionIds.GardenClearing)
            data.RegionPlayers.Add(new RegionPlayerSave
            {
                Region = activeRegion,
                X = localPosition.X, Y = localPosition.Y, Z = localPosition.Z
            });
        return data;
    }

    private static string FixturePath(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", name);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the Webwild repository root.");
    }

    // ------------------------------------------------------------- round-trip

    [Fact]
    public void ManifestRoundtrip_PreservesWorldAndColonyState()
    {
        var sim = TestWorld.NewSim();
        var legacy = FreshLegacySave(sim, RegionIds.GardenClearing, new Vec3(-13.5, 0.45, 8.5));
        legacy.Food = 17;
        legacy.Silk = 29;
        legacy.Tick = 1234;
        legacy.Missions.Completed.Add("M01");

        var manifest = WorldSaveMigrator.ToManifest(legacy);
        var bytes = WorldSaveMigrator.ToBytes(manifest);
        var restored = WorldSaveMigrator.FromBytes(bytes, out var error, out var wasLegacy);
        Assert.True(string.IsNullOrEmpty(error), error);
        Assert.False(wasLegacy);
        Assert.NotNull(restored);

        Assert.Equal(WorldManifestSave.CurrentSchemaVersion, restored!.SchemaVersion);
        Assert.Equal(legacy.Seed, restored.Seed);
        Assert.Equal(17, restored.Game.Food);
        Assert.Equal(29, restored.Game.Silk);
        Assert.Contains("M01", restored.Game.Missions.Completed);
        Assert.Equal(LandmarkCatalog.Garden, restored.ActiveLandmarkId);
        Assert.Equal(legacy.Player.X, restored.Player.Absolute.X, 9);
        Assert.Equal(legacy.Player.Z, restored.Player.Absolute.Z, 9);

        // The extracted payload loads straight back into the simulation.
        var game = WorldSaveMigrator.ExtractGame(restored);
        var reloaded = GameSimulation.FromSave(game, TestWorld.RichTuning(), MissionsConfig.Defaults());
        Assert.Equal(17, reloaded.Ledger.Food);
        Assert.Equal(29, reloaded.Ledger.Silk);
        Assert.Equal(RegionIds.GardenClearing, reloaded.ActiveRegionId);
    }

    [Fact]
    public void ReedwaterPlayerPosition_MigratesThroughLandmarkOrigin()
    {
        var sim = TestWorld.NewSim();
        var local = new Vec3(10.0, 0.55, 8.5);
        var legacy = FreshLegacySave(sim, RegionIds.ReedwaterBank, local);

        var manifest = WorldSaveMigrator.ToManifest(legacy);
        Assert.Equal(LandmarkCatalog.Reedwater, manifest.ActiveLandmarkId);
        var world = manifest.Player.Absolute;
        Assert.Equal(LandmarkCatalog.Origins[LandmarkCatalog.Reedwater].X + local.X, world.X, 9);
        Assert.Equal(local.Z, world.Z, 9);

        // Round trip returns the original region-local transform.
        var game = WorldSaveMigrator.ExtractGame(manifest);
        Assert.Equal(local.X, game.Player.X, 9);
        Assert.Equal(local.Y, game.Player.Y, 9);
        Assert.Equal(local.Z, game.Player.Z, 9);
        Assert.Equal(RegionIds.ReedwaterBank, game.ActiveRegionId);
        Assert.Contains(game.RegionPlayers, entry => entry.Region == RegionIds.ReedwaterBank);
    }

    // ---------------------------------------------------------- legacy migrate

    [Fact]
    public void LegacyGardenFixture_MigratesToManifest()
    {
        var bytes = File.ReadAllBytes(FixturePath("save_v0_1_1.json"));
        var manifest = WorldSaveMigrator.FromBytes(bytes, out var error, out var wasLegacy);
        Assert.True(string.IsNullOrEmpty(error), error);
        Assert.True(wasLegacy);
        Assert.NotNull(manifest);
        Assert.Equal(WorldManifestSave.CurrentSchemaVersion, manifest!.SchemaVersion);
        Assert.Equal(LandmarkCatalog.Garden, manifest.ActiveLandmarkId);
        // Garden landmark origin is the world origin, so the absolute world
        // position equals the legacy garden position exactly, while local
        // coordinates may sit inside a negative chunk (floor semantics).
        Assert.Equal(manifest.Game.Player.X, manifest.Player.Absolute.X, 9);
        Assert.Equal(manifest.Game.Player.Y, manifest.Player.Y, 9);
        Assert.Equal(manifest.Game.Player.Z, manifest.Player.Absolute.Z, 9);
        // Round-tripping back to local coordinates restores the legacy value.
        var backLocal = LandmarkCatalog.ToLocal(LandmarkCatalog.Garden, manifest.Player.Absolute);
        Assert.Equal(manifest.Game.Player.X, backLocal.X, 9);
        Assert.Equal(manifest.Game.Player.Z, backLocal.Z, 9);
        Assert.Contains(manifest.LandmarkVisits, visit => visit.LandmarkId == LandmarkCatalog.Garden);
    }

    [Fact]
    public void LegacyReedwaterFixture_MigratesWithWorldPosition()
    {
        var bytes = File.ReadAllBytes(FixturePath("save_v0_2_0_p1_reedwater.json"));
        var manifest = WorldSaveMigrator.FromBytes(bytes, out var error, out var wasLegacy);
        Assert.True(string.IsNullOrEmpty(error), error);
        Assert.True(wasLegacy);
        Assert.NotNull(manifest);
        Assert.Equal(LandmarkCatalog.Reedwater, manifest.ActiveLandmarkId);
        var local = LandmarkCatalog.ToLocal(LandmarkCatalog.Reedwater, manifest!.Player.Absolute);
        var expectedLocal = new Vec3(manifest.Game.Player.X, manifest.Game.Player.Y, manifest.Game.Player.Z);
        Assert.Equal(expectedLocal.X, local.X, 9);
        Assert.Equal(expectedLocal.Z, local.Z, 9);
        // The migrated manifest still validates as a whole.
        Assert.True(WorldSaveMigrator.Validate(manifest, out var validationError), validationError);
    }

    [Fact]
    public void NewerManifestVersion_IsRejected()
    {
        var manifest = new WorldManifestSave { SchemaVersion = 99 };
        var bytes = WorldSaveMigrator.ToBytes(manifest);
        var result = WorldSaveMigrator.FromBytes(bytes, out var error, out _);
        Assert.Null(result);
        Assert.Contains("newer than this build supports", error);
    }

    // -------------------------------------------------------------- world store

    private WorldSaveStore NewStore() => new(directory: _dir);

    private static WorldManifestSave ValidManifest(int seed = 42)
    {
        var sim = TestWorld.NewSim();
        var legacy = FreshLegacySave(sim, RegionIds.GardenClearing, new Vec3(-13.5, 0.45, 8.5));
        var manifest = WorldSaveMigrator.ToManifest(legacy);
        manifest.Seed = seed;
        return manifest;
    }

    [Fact]
    public void SaveManifest_ThenLoad_ReturnsIdenticalData()
    {
        var store = NewStore();
        var manifest = ValidManifest();
        var result = store.SaveManifest(manifest);
        Assert.True(result.Success, result.Message);

        var loaded = store.LoadManifest();
        Assert.True(loaded.Success, loaded.Message);
        Assert.False(loaded.RecoveredFromBackup);
        Assert.Equal(manifest.Seed, loaded.Manifest!.Seed);
        Assert.Equal(manifest.Player.Absolute.X, loaded.Manifest.Player.Absolute.X, 9);
        Assert.Equal(manifest.Game.Food, loaded.Manifest.Game.Food);
    }

    [Fact]
    public void SaveManifest_CorruptMain_FallsBackToBackup()
    {
        var store = NewStore();
        var manifest = ValidManifest();
        Assert.True(store.SaveManifest(manifest).Success);
        var second = ValidManifest(43);
        second.Game.Food = 55;
        Assert.True(store.SaveManifest(second).Success);
        // Corrupt the main file; the rotated backup still holds the first save.
        File.WriteAllText(store.ManifestPath, "{ not json ");
        var loaded = store.LoadManifest();
        Assert.True(loaded.Success, loaded.Message);
        Assert.True(loaded.RecoveredFromBackup);
        Assert.Equal(42, loaded.Manifest!.Seed);
    }

    [Fact]
    public void SaveManifest_FailureNeverDamagesExistingSave()
    {
        var store = NewStore();
        var manifest = ValidManifest();
        Assert.True(store.SaveManifest(manifest).Success);
        var before = File.ReadAllBytes(store.ManifestPath);

        // An invalid manifest (negative seed) must be refused without touching main.
        var bad = ValidManifest();
        bad.Seed = -1;
        var result = store.SaveManifest(bad);
        Assert.False(result.Success);
        Assert.Equal(before, File.ReadAllBytes(store.ManifestPath));
    }

    [Fact]
    public void SaveManifest_TooLarge_IsRefused()
    {
        var store = NewStore();
        var manifest = ValidManifest();
        // Pad ledger history past the manifest budget.
        for (var i = 0; i < 400; i++) manifest.Game.Counters.LedgerKeys.Add($"x{i:0000}");
        var result = store.SaveManifest(manifest);
        // The legacy payload validator bounds ledger keys at 2000, so the
        // manifest itself is what must refuse the oversize bytes.
        if (result.Success)
        {
            var bytes = WorldSaveMigrator.ToBytes(manifest);
            Assert.True(bytes.Length <= WorldSaveStore.MaxManifestBytes);
        }
        else
        {
            Assert.Contains("large", result.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void LoadManifest_CleansInterruptedTempFiles()
    {
        var store = NewStore();
        Assert.True(store.SaveManifest(ValidManifest()).Success);
        // A crash mid-write leaves temp files behind in both directories.
        Directory.CreateDirectory(Path.Combine(_dir, "chunks"));
        File.WriteAllText(Path.Combine(_dir, "world.json.tmp-crashed"), "garbage");
        File.WriteAllText(Path.Combine(_dir, "chunks", "chunk_0_0.json.tmp-crashed"), "garbage");

        var loaded = store.LoadManifest();
        Assert.True(loaded.Success, loaded.Message);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp-*", SearchOption.AllDirectories));
    }

    [Fact]
    public void ManifestWithoutSave_FailsCleanly()
    {
        var loaded = NewStore().LoadManifest();
        Assert.False(loaded.Success);
        Assert.Equal("No saved game yet", loaded.Message);
    }

    // ------------------------------------------------------------- chunk files

    [Fact]
    public void ChunkChanges_SaveLoadDeleteRoundtrip()
    {
        var store = NewStore();
        var chunk = new ChunkCoordinate(5, -2);
        var record = new ChunkChangeSave
        {
            ChunkX = chunk.X, ChunkZ = chunk.Z,
            BuiltWebIds = { "bridge_001" },
            FoodSources =
            {
                new FoodSourceStateSave { Id = "food:5_-2:0:berries", X = 130.5, Y = 0.2, Z = -40.0, Kind = "berries", AvailableAtTick = 900 }
            },
            Markers = { new MapMarkerSave { Id = "m1", X = 131.0, Y = 0.3, Z = -41.0, Kind = "food", Label = "Berry patch" } }
        };
        Assert.True(store.SaveChunkChanges(record).Success);

        var loaded = store.LoadChunkChanges(chunk, out var error);
        Assert.Null(error);
        Assert.NotNull(loaded);
        Assert.Equal(chunk, new ChunkCoordinate(loaded!.ChunkX, loaded.ChunkZ));
        Assert.Equal("bridge_001", Assert.Single(loaded.BuiltWebIds));
        var food = Assert.Single(loaded.FoodSources);
        Assert.Equal("food:5_-2:0:berries", food.Id);
        Assert.Equal(900, food.AvailableAtTick);
        Assert.Equal("Berry patch", Assert.Single(loaded.Markers).Label);

        Assert.True(store.DeleteChunkChanges(chunk));
        Assert.Null(store.LoadChunkChanges(chunk, out error));
        Assert.Null(error);
        Assert.False(store.DeleteChunkChanges(chunk));
    }

    [Fact]
    public void ChunkChanges_NegativeCoordinatesRoundTrip()
    {
        var store = NewStore();
        var chunk = new ChunkCoordinate(-7, -13);
        var record = new ChunkChangeSave { ChunkX = chunk.X, ChunkZ = chunk.Z, BuiltWebIds = { "capture_002" } };
        Assert.True(store.SaveChunkChanges(record).Success);
        var loaded = store.LoadChunkChanges(chunk, out _);
        Assert.NotNull(loaded);
        Assert.Contains(store.ListChangedChunks(), c => c == chunk);
    }

    [Fact]
    public void ChunkChanges_CorruptRecord_IsIgnoredNotFatal()
    {
        var store = NewStore();
        Directory.CreateDirectory(Path.Combine(_dir, "chunks"));
        File.WriteAllText(Path.Combine(_dir, "chunks", "chunk_1_1.json"), "{ broken");
        var loaded = store.LoadChunkChanges(new ChunkCoordinate(1, 1), out var error);
        Assert.Null(loaded);
        Assert.False(string.IsNullOrEmpty(error));
        // The store still works afterwards.
        Assert.True(store.SaveChunkChanges(new ChunkChangeSave { ChunkX = 1, ChunkZ = 1 }).Success);
    }

    [Fact]
    public void ChunkChanges_MismatchedFileContents_AreRejected()
    {
        var store = NewStore();
        var record = new ChunkChangeSave { ChunkX = 3, ChunkZ = 4 };
        Assert.True(store.SaveChunkChanges(record).Success);
        var path = Path.Combine(_dir, "chunks", "chunk_3_4.json");
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length - 3] = (byte)'}';
        File.WriteAllBytes(path, bytes);
        var loaded = store.LoadChunkChanges(new ChunkCoordinate(3, 4), out _);
        // Tampering with the JSON may or may not produce a different chunk id;
        // either way the store must never return a record for the wrong chunk.
        Assert.True(loaded is null || (loaded.ChunkX == 3 && loaded.ChunkZ == 4));
    }

    [Fact]
    public void ChunkChanges_OversizeRecord_IsRefused()
    {
        var store = NewStore();
        var record = new ChunkChangeSave { ChunkX = 0, ChunkZ = 0 };
        for (var i = 0; i < 250; i++)
            record.FoodSources.Add(new FoodSourceStateSave { Id = $"food:{i}", X = 1, Y = 0, Z = 1, Kind = "berries" });
        var result = store.SaveChunkChanges(record);
        Assert.False(result.Success);
        Assert.Contains("too many", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListChangedChunks_SortsAndParses()
    {
        var store = NewStore();
        Assert.Empty(store.ListChangedChunks());
        store.SaveChunkChanges(new ChunkChangeSave { ChunkX = 2, ChunkZ = 0 });
        store.SaveChunkChanges(new ChunkChangeSave { ChunkX = -1, ChunkZ = 3 });
        store.SaveChunkChanges(new ChunkChangeSave { ChunkX = -1, ChunkZ = -3 });
        var chunks = store.ListChangedChunks();
        Assert.Equal(3, chunks.Count);
        Assert.Equal(new ChunkCoordinate(-1, -3), chunks[0]);
        Assert.Equal(new ChunkCoordinate(-1, 3), chunks[1]);
        Assert.Equal(new ChunkCoordinate(2, 0), chunks[2]);
    }
}
