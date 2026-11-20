using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>P5 exploration contracts: bounded map history, stable IDs, manifest
/// round-tripping without bloating the embedded colony payload.</summary>
public class ExplorationTests
{
    private const int Seed = 20260214;

    [Fact]
    public void VisitArea_RecordsStableIds()
    {
        var record = new ExplorationRecord();
        Assert.Equal(9, record.VisitArea(new ChunkCoordinate(2, -3), 1));
        Assert.Equal(9, record.Count);
        Assert.True(record.Contains(new ChunkCoordinate(2, -3)));
        Assert.True(record.Contains(new ChunkCoordinate(3, -2)));
        Assert.False(record.Contains(new ChunkCoordinate(4, 0)));

        // Revisiting is idempotent and never creates a duplicate.
        Assert.Equal(0, record.VisitArea(new ChunkCoordinate(2, -3), 1));
        var ids = record.ToIds();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The H5 contract: exploration is durable. Well past the old
    /// 12,000-chunk eviction cap, the oldest discoveries stay recorded.</summary>
    [Fact]
    public void Record_IsDurable_WellBeyondTheLegacyCap()
    {
        var record = new ExplorationRecord();
        const int legacyCap = 12_000;
        for (var i = 0; i < legacyCap + 8_000; i++)
            record.Visit(new ChunkCoordinate(i, 0));
        Assert.Equal(legacyCap + 8_000, record.Count);
        Assert.True(record.Contains(new ChunkCoordinate(0, 0)),
            "the oldest discovery was evicted");
        Assert.True(record.Contains(new ChunkCoordinate(legacyCap + 7_999, 0)));

        // A dense block is compact as well as durable: 10,000 chunks of a
        // 100×100 square live in a handful of region bitsets.
        var dense = new ExplorationRecord();
        for (var x = 0; x < 100; x++)
        for (var z = 0; z < 100; z++)
            dense.Visit(new ChunkCoordinate(x, z));
        Assert.Equal(10_000, dense.Count);
        Assert.True(dense.RegionCount <= 9,
            $"a dense square spans {dense.RegionCount} regions, not a compact few");
    }

    [Fact]
    public void Regions_RoundTripAndIgnoreInvalidPayloads()
    {
        var source = new ExplorationRecord();
        // Negative and distant coordinates cross region seams deliberately.
        for (var x = -70; x < 70; x += 3)
        for (var z = -70; z < 70; z += 5)
            source.Visit(new ChunkCoordinate(x * 137, z * 91));
        var snapshot = source.SnapshotRegions();
        Assert.True(snapshot.Count > 1);

        var restored = new ExplorationRecord();
        var payloads = snapshot.ToList();
        payloads.Add(null!);
        payloads.Add(new ExplorationRegionSave
        {
            RegionX = 999, RegionZ = 999, Bits = "not base64!"
        });
        restored.RestoreRegions(payloads);
        Assert.Equal(source.Count, restored.Count);
        Assert.Equal(source.ToIds(), restored.ToIds());
        Assert.Equal(snapshot.Count, restored.RegionCount);
    }

    [Fact]
    public void Regions_RespectTheDocumentedBound()
    {
        var record = new ExplorationRecord();
        for (var region = 0; region < ExplorationRecord.MaxRegions + 3; region++)
            record.Visit(new ChunkCoordinate(region * ExplorationRecord.RegionChunks, 0));
        Assert.Equal(ExplorationRecord.MaxRegions, record.RegionCount);
        // The bound evicts whole regions oldest-first, far beyond real play;
        // the honest contract is that the bound exists and holds.
        Assert.False(record.Contains(new ChunkCoordinate(0, 0)));
        Assert.True(record.Contains(new ChunkCoordinate(
            ExplorationRecord.MaxRegions * ExplorationRecord.RegionChunks, 0)));
    }

    [Fact]
    public void Restore_RoundTripsAndIgnoresInvalidIds()
    {
        var source = new ExplorationRecord();
        source.VisitArea(new ChunkCoordinate(-2, 4), 1);
        var ids = source.ToIds();

        var restored = new ExplorationRecord();
        restored.Restore(ids.Append("not-a-chunk").Append(""));
        Assert.Equal(source.Count, restored.Count);
        Assert.Equal(ids, restored.ToIds());
        Assert.True(restored.Contains(new ChunkCoordinate(-2, 4)));
    }

    /// <summary>A dense history must stay comfortably inside the manifest
    /// budget: full regions cost ~700 bytes each, not thousands of ids.</summary>
    [Fact]
    public void ManifestGrowth_StaysReasonableForDenseHistories()
    {
        var record = new ExplorationRecord();
        for (var region = 0; region < 40; region++)
        for (var x = 0; x < 4; x++)
        for (var z = 0; z < 4; z++)
            record.Visit(new ChunkCoordinate(
                region * ExplorationRecord.RegionChunks + x * 16, z * 16));
        // 40 regions, each holding 16 chunk bits.
        Assert.Equal(40, record.RegionCount);

        var manifest = new WorldManifestSave
        {
            ExplorationRegions = record.SnapshotRegions()
        };
        var bytes = System.Text.Json.JsonSerializer.Serialize(manifest);
        Assert.True(bytes.Length < 60_000,
            $"40-region manifest is {bytes.Length} bytes; growth is not reasonable");
        Assert.True(bytes.Length < WorldSaveStore.MaxManifestBytes,
            "the manifest exceeds its own budget");
    }

    [Fact]
    public void ChunksInView_WindowsWithoutEnumeratingTheWorld()
    {
        var record = new ExplorationRecord();
        // A few hundred regions around the origin, well inside the bound.
        for (var x = -150; x <= 150; x += 7)
        for (var z = -150; z <= 150; z += 7)
            record.Visit(new ChunkCoordinate(x, z));
        Assert.True(record.RegionCount > 1);
        var inView = record.ChunksInView(-30, -30, 30, 30).ToList();
        Assert.Equal(record.ToIds().Where(id =>
        {
            var parts = id.Split('_');
            var x = long.Parse(parts[0]);
            var z = long.Parse(parts[1]);
            return x >= -30 && x <= 30 && z >= -30 && z <= 30;
        }).Count(), inView.Count);
        Assert.All(inView, coordinate => Assert.True(record.Contains(coordinate)));
    }

    [Fact]
    public void WorldSave_RoundTripsExplorationOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "webwild-p5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new WorldSaveService(
                Path.Combine(root, "world"), Path.Combine(root, "webwild_save.json"));
            var sim = TestWorld.NewSim();
            var data = sim.ExportSave(new Vec3(10, 0.4, 6), 0, new SettingsData());
            data.Seed = Seed;
            data.ExploredChunks = new List<string> { "0_0", "1_0", "-1_-1" };
            var saved = service.Save(data);
            Assert.True(saved.Success, saved.Error);

            var manifest = service.WorldStore.LoadManifest().Manifest;
            Assert.NotNull(manifest);
            Assert.Equal(new[] { "0_0", "1_0", "-1_-1" }, manifest!.ExploredChunks);

            var loaded = service.Load();
            Assert.True(loaded.Success, loaded.Message);
            Assert.Equal(manifest.ExploredChunks, loaded.Data!.ExploredChunks);

            // The runtime-only list is JsonIgnore'd: the manifest stores the
            // record exactly once, not again inside the embedded game payload.
            var json = File.ReadAllText(service.WorldStore.ManifestPath);
            Assert.Equal(1, CountOccurrences(json, "\"exploredChunks\""));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>The H5 compact form: a durable record saves as region
    /// bitsets, reloads exactly, keeps the legacy list empty, and stays
    /// inside the manifest budget.</summary>
    [Fact]
    public void WorldSave_RoundTripsExplorationRegions()
    {
        var root = Path.Combine(Path.GetTempPath(), "webwild-h5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new WorldSaveService(
                Path.Combine(root, "world"), Path.Combine(root, "webwild_save.json"));
            var sim = TestWorld.NewSim();
            var data = sim.ExportSave(new Vec3(10, 0.4, 6), 0, new SettingsData());
            data.Seed = Seed;
            var explored = new ExplorationRecord();
            // Cross region seams deliberately, but stay inside the bound —
            // real histories never approach it.
            for (var x = -70; x < 70; x += 2)
            for (var z = -70; z < 70; z += 3)
                explored.Visit(new ChunkCoordinate(x * 3, z * 5));
            data.ExploredChunks = new List<string>();
            data.ExplorationRegions = explored.SnapshotRegions();
            var saved = service.Save(data);
            Assert.True(saved.Success, saved.Error);

            var manifest = service.WorldStore.LoadManifest().Manifest;
            Assert.NotNull(manifest);
            Assert.Empty(manifest!.ExploredChunks);
            Assert.Equal(explored.RegionCount, manifest.ExplorationRegions.Count);

            var loaded = service.Load();
            Assert.True(loaded.Success, loaded.Message);
            var restored = new ExplorationRecord();
            restored.RestoreRegions(loaded.Data!.ExplorationRegions);
            Assert.Equal(explored.ToIds(), restored.ToIds());
            Assert.True(restored.Contains(new ChunkCoordinate(-70 * 3, -70 * 5)));

            var json = File.ReadAllText(service.WorldStore.ManifestPath);
            Assert.Equal(1, CountOccurrences(json, "\"exploredChunks\""));
            Assert.True(json.Length < 400_000, $"manifest is {json.Length} bytes");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ManifestValidation_RejectsBrokenRegionRecords()
    {
        var good = new ExplorationRecord();
        good.VisitArea(new ChunkCoordinate(3, 3), 1);
        var baseManifest = WorldSaveMigrator.ToManifest(
            TestWorld.NewSim().ExportSave(new Vec3(0, 0, 0), 0, new SettingsData()));

        var manifest = WorldSaveMigrator.ToManifest(
            TestWorld.NewSim().ExportSave(new Vec3(0, 0, 0), 0, new SettingsData()));
        manifest.ExplorationRegions = good.SnapshotRegions();
        manifest.ExploredChunks = new List<string> { "0_0" };
        Assert.False(WorldSaveMigrator.Validate(manifest, out var bothFormats));
        Assert.Equal("The exploration record uses two formats at once.", bothFormats);

        manifest = WorldSaveMigrator.ToManifest(
            TestWorld.NewSim().ExportSave(new Vec3(0, 0, 0), 0, new SettingsData()));
        manifest.ExplorationRegions = new List<ExplorationRegionSave>();
        for (var i = 0; i <= ExplorationRecord.MaxRegions; i++)
            manifest.ExplorationRegions.Add(new ExplorationRegionSave
            {
                RegionX = i, RegionZ = 0,
                Bits = Convert.ToBase64String(new byte[512])
            });
        Assert.False(WorldSaveMigrator.Validate(manifest, out var tooMany));
        Assert.Equal("The exploration record has too many regions.", tooMany);

        manifest = WorldSaveMigrator.ToManifest(
            TestWorld.NewSim().ExportSave(new Vec3(0, 0, 0), 0, new SettingsData()));
        manifest.ExplorationRegions = new List<ExplorationRegionSave>
        {
            new() { RegionX = 1, RegionZ = 1, Bits = Convert.ToBase64String(new byte[64]) }
        };
        Assert.False(WorldSaveMigrator.Validate(manifest, out var wrongSize));
        Assert.Equal("The exploration record has a region of the wrong size.", wrongSize);

        manifest = WorldSaveMigrator.ToManifest(
            TestWorld.NewSim().ExportSave(new Vec3(0, 0, 0), 0, new SettingsData()));
        manifest.ExplorationRegions = good.SnapshotRegions();
        manifest.ExplorationRegions.Add(manifest.ExplorationRegions[0]);
        Assert.False(WorldSaveMigrator.Validate(manifest, out var duplicated));
        Assert.Equal("The exploration record has an invalid region.", duplicated);

        Assert.True(WorldSaveMigrator.Validate(baseManifest, out var baselineValid),
            baselineValid);
    }

    [Fact]
    public void FreshSave_DoesNotMarkUnvisitedLandmarks()
    {
        var sim = TestWorld.NewSim();
        var data = sim.ExportSave(new Vec3(10, 0.4, 6), 0, new SettingsData());
        data.Seed = Seed;
        // Core starts with a remembered position for every known landmark, but
        // only the garden has actually been visited.
        Assert.Contains(data.RegionPlayers, entry => entry.Region == RegionIds.ReedwaterBank);
        var manifest = WorldSaveMigrator.ToManifest(data);
        var visit = Assert.Single(manifest.LandmarkVisits);
        Assert.Equal(LandmarkCatalog.Garden, visit.LandmarkId);
        var extracted = WorldSaveMigrator.ExtractGame(manifest);
        Assert.Equal(new[] { RegionIds.GardenClearing }, extracted.VisitedRegions);

        // Actually entering Reedwater records the second visit.
        sim.EnterLandmark(RegionIds.ReedwaterBank);
        var travelled = sim.ExportSave(new Vec3(10, 0.4, 6), 0, new SettingsData());
        travelled.Seed = Seed;
        var travelledManifest = WorldSaveMigrator.ToManifest(travelled);
        Assert.Equal(2, travelledManifest.LandmarkVisits.Count);
        Assert.Contains(travelledManifest.LandmarkVisits,
            entry => entry.LandmarkId == LandmarkCatalog.Reedwater);
    }

    [Fact]
    public void ManifestValidation_RejectsBadExplorationRecords()
    {
        var sim = TestWorld.NewSim();
        var data = sim.ExportSave(new Vec3(10, 0.4, 6), 0, new SettingsData());
        data.Seed = Seed;
        var manifest = WorldSaveMigrator.ToManifest(data);

        manifest.ExploredChunks.Add("0_0");
        Assert.True(WorldSaveMigrator.Validate(manifest, out var okError), okError);

        manifest.ExploredChunks.Add("0_0");
        Assert.False(WorldSaveMigrator.Validate(manifest, out var duplicateError));
        Assert.Contains("exploration", duplicateError, StringComparison.OrdinalIgnoreCase);

        manifest.ExploredChunks.Clear();
        manifest.ExploredChunks.Add("not-a-chunk");
        Assert.False(WorldSaveMigrator.Validate(manifest, out var badError));
        Assert.Contains("exploration", badError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyMigration_StartsWithEmptyExploration()
    {
        var sim = TestWorld.NewSim();
        var data = sim.ExportSave(new Vec3(10, 0.4, 6), 0, new SettingsData());
        data.Seed = Seed;
        var manifest = WorldSaveMigrator.ToManifest(data);
        Assert.Empty(manifest.ExploredChunks);
        var extracted = WorldSaveMigrator.ExtractGame(manifest);
        Assert.Empty(extracted.ExploredChunks);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
