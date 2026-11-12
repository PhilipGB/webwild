using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>
/// Real v0.1.x save fixtures. These files contain only schema-1 fields; loading
/// them must run the explicit migration rather than relying on an untested JSON
/// default.
/// </summary>
public class SaveMigrationTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scenes", "world", "GardenClearing.tscn")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the Webwild repository root.");
    }

    private static SaveGameData LoadFixture(string name)
    {
        var path = Path.Combine(Root, "tests", "fixtures", name);
        Assert.True(File.Exists(path), $"Legacy fixture missing: {path}");
        var data = SaveValidator.Deserialize(File.ReadAllBytes(path), out var error);
        Assert.NotNull(data);
        Assert.True(string.IsNullOrEmpty(error), error);
        return data!;
    }

    [Theory]
    [InlineData("save_v0_1_0.json", 0, 0)]
    [InlineData("save_v0_1_1.json", 1, 1)]
    [InlineData("save_v0_1_2.json", 2, 2)]
    public void LegacySaves_MigrateToGardenDefaults(string fixture, int completedMissions, int webs)
    {
        var data = LoadFixture(fixture);
        Assert.Equal(SaveGameData.CurrentSchemaVersion, data.SchemaVersion);
        Assert.Equal(RegionIds.GardenClearing, data.ActiveRegionId);
        Assert.Contains(RegionIds.GardenClearing, data.VisitedRegions);
        Assert.Contains(data.RegionPlayers, entry =>
            entry.Region == RegionIds.GardenClearing);
        Assert.All(data.Webs, web => Assert.Equal(RegionIds.GardenClearing, web.Region));
        Assert.All(data.Workers, worker => Assert.Equal(RegionIds.GardenClearing, worker.Region));
        Assert.All(data.Insects, insect => Assert.Equal(RegionIds.GardenClearing, insect.Region));
        Assert.Equal(completedMissions, data.Missions.Completed.Count);
        Assert.Equal(webs, data.Webs.Count);
        Assert.NotNull(data.RegionalObjectives);
        Assert.Empty(data.RegionalObjectives.Completed);

        var sim = GameSimulation.FromSave(data, TestWorld.RichTuning(), MissionsConfig.Defaults());
        Assert.Equal(RegionIds.GardenClearing, sim.ActiveRegionId);
        Assert.Equal(data.Food, sim.Ledger.Food);
        Assert.Equal(data.Silk, sim.Ledger.Silk);
        Assert.Equal(data.Webs.Count, sim.RegionWebs.Count);
        Assert.Equal(data.Workers.Count, sim.RegionWorkers.Count);
        Assert.Equal(completedMissions, sim.Missions.CompletedMissions.Count);
    }

    [Fact]
    public void P1Schema2ReedwaterSave_SeedsDurableSupplyClaimOnMigration()
    {
        var data = LoadFixture("save_v0_2_0_p1_reedwater.json");
        Assert.Equal(SaveGameData.CurrentSchemaVersion, data.SchemaVersion);
        Assert.Equal(RegionIds.ReedwaterBank, data.ActiveRegionId);
        Assert.Contains(RegionIds.ReedwaterBank, data.VisitedRegions);
        Assert.Contains(RegionIds.ReedwaterBank, data.Counters.ClaimedSupplyCaches);

        var sim = GameSimulation.FromSave(data, TestWorld.RichTuning(), MissionsConfig.Defaults());
        var food = sim.Ledger.Food;
        var silk = sim.Ledger.Silk;
        Assert.True(sim.TryTravelTo(RegionIds.GardenClearing, out var reason), reason);
        Assert.True(sim.TryTravelTo(RegionIds.ReedwaterBank, out reason), reason);
        Assert.Equal(food, sim.Ledger.Food);
        Assert.Equal(silk, sim.Ledger.Silk);
        Assert.Contains(RegionIds.ReedwaterBank, sim.ClaimedSupplyCaches);
    }

    [Fact]
    public void P1Schema2GardenSave_MigratesWithoutInventingAClaim()
    {
        var data = LoadFixture("save_v0_2_0_p1.json");
        Assert.Equal(SaveGameData.CurrentSchemaVersion, data.SchemaVersion);
        Assert.Equal(RegionIds.GardenClearing, data.ActiveRegionId);
        Assert.DoesNotContain(RegionIds.ReedwaterBank, data.VisitedRegions);
        Assert.Empty(data.Counters.ClaimedSupplyCaches);
        Assert.Equal(5, data.Food);
        Assert.Equal(13, data.Silk);
        var sim = GameSimulation.FromSave(data, TestWorld.RichTuning(), MissionsConfig.Defaults());
        Assert.Equal(5, sim.Ledger.Food);
        Assert.Equal(13, sim.Ledger.Silk);
    }

    [Fact]
    public void LegacyGameplayState_IsNotRewrittenOrDuplicated()
    {        var data = LoadFixture("save_v0_1_2.json");
        Assert.Equal(11, data.Food);
        Assert.Equal(8, data.Silk);
        Assert.Equal(2, data.Missions.Completed.Count);
        Assert.Single(data.Workers);
        Assert.Contains(data.Webs, web => web.Id == "bridge_002" && web.Type == "bridge");

        var sim = GameSimulation.FromSave(data,
            TestWorld.RichTuning(t => t.SilkProduction.SilkPerFedWorkerEverySeconds = 0),
            MissionsConfig.Defaults());
        Assert.Equal("M03", sim.Missions.ActiveMissionId);
        Assert.Single(sim.Workers);
        Assert.Equal("Pip", sim.Workers[0].Name);
        Assert.NotNull(sim.Graph.FindRoute(WorldLayout.NurseryNode, WorldLayout.TwigA));
        var foodBefore = sim.Ledger.Food;
        sim.AdvanceSeconds(10);
        // The existing ledger history prevents a replayed reward from being granted.
        Assert.False(sim.Ledger.TryGrantFood(2, "food:fly_twins:0"));
        Assert.Equal(foodBefore, sim.Ledger.Food);
    }

    [Fact]
    public void UnsupportedSchema_IsRejectedClearly()
    {
        var path = Path.Combine(Root, "tests", "fixtures", "save_v0_1_1.json");
        var json = File.ReadAllText(path).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 77");
        var data = SaveValidator.Deserialize(System.Text.Encoding.UTF8.GetBytes(json), out var error);
        Assert.Null(data);
        Assert.Contains("77", error);
    }
}
