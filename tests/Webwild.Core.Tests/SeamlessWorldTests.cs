using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>
/// P2 seamless-world contracts: landmark zones, generator footprints around
/// both landmarks, walking-based landmark activation and the live manifest
/// save path (including legacy adoption and archiving).
/// </summary>
public class SeamlessWorldTests
{
    private const int Seed = 20260214;

    [Fact]
    public void LandmarkZones_CoverBothAuthoredPockets()
    {
        Assert.Equal(LandmarkCatalog.Garden, LandmarkCatalog.LandmarkAt(0, 0));
        Assert.Equal(LandmarkCatalog.Garden, LandmarkCatalog.LandmarkAt(-24, 24));
        Assert.Null(LandmarkCatalog.LandmarkAt(25, 0));
        Assert.Null(LandmarkCatalog.LandmarkAt(50, 0));

        Assert.Equal(LandmarkCatalog.Reedwater, LandmarkCatalog.LandmarkAt(96, 0));
        Assert.Equal(LandmarkCatalog.Reedwater,
            LandmarkCatalog.LandmarkAt(96 - 19.6, -16.6));
        Assert.Null(LandmarkCatalog.LandmarkAt(96, 17));
        Assert.Null(LandmarkCatalog.LandmarkAt(120, 0));
    }

    [Fact]
    public void Generator_SkipsBothLandmarkFootprints()
    {
        var generator = new WorldGenerator(Seed);
        foreach (var chunk in new[]
                 {
                     new ChunkCoordinate(-1, -1), new ChunkCoordinate(0, 0),
                     new ChunkCoordinate(3, -1), new ChunkCoordinate(4, 0)
                 })
        {
            Assert.True(WorldGenerator.IsAuthoredChunk(chunk));
            Assert.Null(generator.GenerateChunk(chunk));
        }
        Assert.False(WorldGenerator.IsAuthoredChunk(new ChunkCoordinate(2, 0)));
        Assert.False(WorldGenerator.IsAuthoredChunk(new ChunkCoordinate(5, 0)));
    }

    [Fact]
    public void Generator_BlendsToAuthoredLevelAroundReedwater()
    {
        var generator = new WorldGenerator(Seed);
        var rect = LandmarkCatalog.ReedwaterFootprint;

        Assert.Equal(WorldGenerator.AuthoredGroundHeight,
            generator.HeightAt(96, 0), 12);
        Assert.Equal(WorldGenerator.AuthoredGroundHeight,
            generator.HeightAt(rect.MaxX, rect.MinZ), 12);

        // Moving outwards the blend must rise continuously from the authored
        // level towards the raw noise without a step at the footprint edge.
        var previous = generator.HeightAt(rect.MaxX, 0);
        Assert.Equal(WorldGenerator.AuthoredGroundHeight, previous, 12);
        for (var step = 0.5; step <= WorldGenerator.AuthoredBlendMetres + 4; step += 0.5)
        {
            var height = generator.HeightAt(rect.MaxX + step, 0);
            Assert.True(Math.Abs(height - previous) < 0.35,
                $"height jumped at +{step} m: {previous:0.###} -> {height:0.###}");
            previous = height;
        }

        // Beyond the blend ring the raw sampler is authoritative.
        var far = generator.HeightAt(rect.MaxX + WorldGenerator.AuthoredBlendMetres + 5, 0);
        var raw = generator.BaseHeightAt(
            rect.MaxX + WorldGenerator.AuthoredBlendMetres + 5, 0);
        Assert.Equal(raw, far, 12);
    }

    [Fact]
    public void WalkingIntoLandmark_ActivatesItWithoutMovingHelpers()
    {
        var sim = TestWorld.NewSim();
        Assert.True(sim.TryStartHatch(out _));
        sim.AdvanceSeconds(4);
        var worker = Assert.Single(sim.Workers);
        Assert.Equal(RegionIds.GardenClearing, sim.ActiveRegionId);

        Assert.True(sim.EnterLandmark(RegionIds.ReedwaterBank));
        Assert.Equal(RegionIds.ReedwaterBank, sim.ActiveRegionId);
        Assert.Contains(RegionIds.ReedwaterBank, sim.VisitedRegions);
        // F2: entering a landmark changes the story frame, not the colony.
        // The helper keeps its own home, task and physical position.
        Assert.Equal(RegionIds.GardenClearing, worker.Region);
        Assert.Equal(RegionIds.GardenClearing, worker.HomeRegion);
        Assert.False(worker.Travelling);
        var gardenNursery = LandmarkCatalog.ToWorld(RegionIds.GardenClearing,
            WorldLayout.NurseryPosition).Absolute;
        Assert.InRange(Vec3.Distance(worker.WorldPosition, gardenNursery), 0, 3);
        Assert.Contains(RegionIds.ReedwaterBank, sim.ClaimedSupplyCaches);

        // The supply cache is exactly once, even across repeated zone changes.
        Assert.False(sim.EnterLandmark(RegionIds.ReedwaterBank));
        Assert.True(sim.EnterLandmark(RegionIds.GardenClearing));
        Assert.True(sim.EnterLandmark(RegionIds.ReedwaterBank));
        Assert.Single(sim.ClaimedSupplyCaches);
    }

    [Fact]
    public void SaveValidator_AllowsSeamlessPositionsInBothFrames()
    {
        Assert.True(SaveValidator.PositionInBounds(RegionIds.GardenClearing, 120, 0.5, -96));
        Assert.True(SaveValidator.PositionInBounds(RegionIds.ReedwaterBank, -40, 0.5, 10));
        Assert.False(SaveValidator.PositionInBounds(RegionIds.ReedwaterBank, double.NaN, 0, 0));
        Assert.False(SaveValidator.PositionInBounds(RegionIds.GardenClearing, 0, -20, 0));
    }

    [Fact]
    public void WorldSaveService_SavesAndLoadsManifest()
    {
        using var fixture = new SaveFixture();
        var sim = TestWorld.NewSim();
        // A seamless-world save far outside the authored garden rect stays in
        // the garden's local frame until a landmark zone is entered.
        var data = sim.ExportSave(new Vec3(120, 0.5, -96), 1.25, new SettingsData());
        data.Seed = Seed;

        var saved = fixture.Service.Save(data);
        Assert.True(saved.Success, saved.Error);
        Assert.True(File.Exists(fixture.Service.WorldStore.ManifestPath));

        var loaded = fixture.Service.Load();
        Assert.True(loaded.Success, loaded.Message);
        Assert.NotNull(loaded.Data);
        Assert.Equal(120, loaded.Data!.Player.X, 6);
        Assert.Equal(-96, loaded.Data.Player.Z, 6);
    }

    [Fact]
    public void WorldSaveService_AdoptsLegacySaveAndKeepsLegacyFile()
    {
        using var fixture = new SaveFixture();
        var legacy = fixture.ServiceWorldlessLegacy();
        Assert.True(legacy.Save(NewLegacyData()).Success);

        var loaded = fixture.Service.Load();
        Assert.True(loaded.Success, loaded.Message);
        Assert.NotNull(loaded.Data);
        Assert.True(File.Exists(fixture.Service.WorldStore.ManifestPath),
            "legacy load must adopt the manifest write path");
        Assert.True(File.Exists(fixture.LegacyPath),
            "the legacy file is kept as a rollback copy");
    }

    [Fact]
    public void WorldSaveService_CorruptManifestNeverResurrectsLegacySave()
    {
        using var fixture = new SaveFixture();
        Assert.True(fixture.Legacy.Save(NewLegacyData()).Success);
        // A manifest exists but is unreadable, and no backup is available.
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.Service.WorldStore.ManifestPath)!);
        File.WriteAllText(fixture.Service.WorldStore.ManifestPath, "{ not json");

        var loaded = fixture.Service.Load();
        Assert.False(loaded.Success);
        Assert.Null(loaded.Data);
    }

    [Fact]
    public void WorldSaveService_ArchivesAndRestoresManifest()
    {
        using var fixture = new SaveFixture();
        var sim = TestWorld.NewSim();
        var data = sim.ExportSave(new Vec3(-13.5, 0.45, 8.5), 0, new SettingsData());
        data.Seed = Seed;
        Assert.True(fixture.Service.Save(data).Success);

        Assert.True(fixture.Service.ArchiveExisting(out var message), message);
        Assert.True(fixture.Service.HasArchive);
        var archive = fixture.Service.LoadArchive();
        Assert.True(archive.Success, archive.Message);
        Assert.Equal(data.Tick, archive.Data!.Tick);

        // A new game's save must not consume the archive.
        var fresh = sim.ExportSave(new Vec3(-13.5, 0.45, 8.5), 0, new SettingsData());
        fresh.Seed = Seed;
        Assert.True(fixture.Service.Save(fresh).Success);
        Assert.True(fixture.Service.HasArchive);
    }

    private static SaveGameData NewLegacyData()
    {
        var sim = TestWorld.NewSim();
        var data = sim.ExportSave(new Vec3(-13.5, 0.45, 8.5), 0, new SettingsData());
        data.Seed = Seed;
        return data;
    }

    /// <summary>Isolated on-disk save root for one test.</summary>
    private sealed class SaveFixture : IDisposable
    {
        public string Root { get; }
        public string LegacyPath { get; }
        public WorldSaveService Service { get; }
        public SaveService Legacy { get; }

        public SaveFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "webwild-p2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            LegacyPath = Path.Combine(Root, "webwild_save.json");
            Service = new WorldSaveService(Path.Combine(Root, "world"), LegacyPath);
            Legacy = new SaveService(LegacyPath);
        }

        public SaveService ServiceWorldlessLegacy() => Legacy;

        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
        }
    }
}
