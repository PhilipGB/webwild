using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>P5 exploration contracts: bounded map history, stable IDs, manifest
/// round-tripping without bloating the embedded colony payload.</summary>
public class ExplorationTests
{
    private const int Seed = 20260214;

    [Fact]
    public void VisitArea_RecordsStableSortedIds()
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
        Assert.Equal(record.Snapshot().Select(coordinate => coordinate.Id), ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Record_IsBounded_AndEvictsOldestFirst()
    {
        var record = new ExplorationRecord();
        for (var i = 0; i < ExplorationRecord.MaxChunks + 5; i++)
            record.Visit(new ChunkCoordinate(i, 0));
        Assert.Equal(ExplorationRecord.MaxChunks, record.Count);
        Assert.False(record.Contains(new ChunkCoordinate(0, 0)));
        Assert.True(record.Contains(new ChunkCoordinate(ExplorationRecord.MaxChunks + 4, 0)));
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
