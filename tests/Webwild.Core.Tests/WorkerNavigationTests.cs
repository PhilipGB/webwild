using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>P7 contracts: coarse dry world routes through real river crossings,
/// physical worker journeys and save/load continuation.</summary>
public class WorkerNavigationTests
{
    private const int Seed = 20260214;

    private static void UnlockStory(GameSimulation sim) =>
        sim.Missions.Restore(3, 0, new[] { 2, 3, 3 },
            new[] { "M01", "M02", "M03" }, Array.Empty<string>(), victoryAwarded: true);

    private static void AdvanceUntilArrived(GameSimulation sim, WorkerState worker)
    {
        for (var i = 0; i < 2500 && worker.Travelling; i++) sim.AdvanceSeconds(0.2);
        Assert.False(worker.Travelling, "worker journey did not arrive");
    }

    private static WorkerState HatchedWorker(GameSimulation sim)
    {
        Assert.True(sim.TryStartHatch(out var reason), reason);
        sim.AdvanceSeconds(4);
        return Assert.Single(sim.Workers);
    }

    [Fact]
    public void DirectLandmarkRoute_IsDryAndDoesNotNeedABridge()
    {
        var nav = new WorkerNavigator(Seed);
        var start = LandmarkCatalog.ToWorld(RegionIds.GardenClearing,
            WorldLayout.NurseryPosition).Absolute;
        var target = LandmarkCatalog.ToWorld(RegionIds.ReedwaterBank,
            RegionCatalog.Reedwater.NurseryPosition).Absolute;
        var route = nav.FindRoute(start, target);
        Assert.NotNull(route);
        Assert.False(route!.UsesBridge);
        Assert.True(route.Points.Count > 4);
        Assert.All(route.Points, point => Assert.True(point.IsFinite));
        Assert.True(route.Distance > 80);
        var generator = nav.Generator;
        foreach (var point in route.Points)
            Assert.False(generator.RiverAt(point.X, point.Z).IsHazard &&
                         point.Y < generator.RiverAt(point.X, point.Z).WaterLevel + 0.2);
    }

    [Fact]
    public void LandmarkJourneys_PassThroughTheHedgeGates()
    {
        var nav = new WorkerNavigator(Seed);
        var start = LandmarkCatalog.ToWorld(RegionIds.GardenClearing,
            WorldLayout.NurseryPosition).Absolute;
        var target = LandmarkCatalog.ToWorld(RegionIds.ReedwaterBank,
            RegionCatalog.Reedwater.NurseryPosition).Absolute;
        var route = nav.FindRoute(start, target);
        Assert.NotNull(route);
        Assert.Contains(route!.Points, point =>
            Math.Abs(point.X - 24) < 1.5 && point.Z is > 4.5 and < 12.5);
        Assert.Contains(route.Points, point =>
            Math.Abs(point.X - 76.4) < 1.5 && point.Z is > 2.5 and < 9.5);
    }

    [Fact]
    public void RiverRoute_UsesTheGeneratedBridgeAndStaysAboveWater()
    {
        var nav = new WorkerNavigator(Seed);
        var generator = nav.Generator;
        var start = new Vec3(0, generator.HeightAt(0, 75) + 0.3, 75);
        var target = LandmarkCatalog.ToWorld(RegionIds.GardenClearing,
            WorldLayout.NurseryPosition).Absolute;
        var route = nav.FindRoute(start, target);
        Assert.NotNull(route);
        Assert.True(route!.UsesBridge, "a dry route from the south bank must use a bridge");
        var bridge = generator.Rivers.BridgeAt(0, 0, generator);
        Assert.NotNull(bridge);
        var water = bridge!.WaterLevel;
        Assert.Contains(route.Points, point => point.Y > water + 0.4);
        Assert.All(route.Points, point => Assert.True(point.IsFinite));
        for (var i = 1; i < route.Points.Count; i++)
            Assert.True(Vec3.Distance(route.Points[i - 1], route.Points[i]) < 10.0,
                $"route jumped {Vec3.Distance(route.Points[i - 1], route.Points[i]):0.0} m");
    }

    [Fact]
    public void WetTarget_HasNoSafeRoute()
    {
        var nav = new WorkerNavigator(Seed);
        var center = RiverMap.CenterAt(0, 0);
        var target = new Vec3(0, nav.Generator.Rivers.WaterLevelAt(0, 0), center);
        Assert.Null(nav.FindRoute(new Vec3(0, 1, 8), target));
    }

    [Fact]
    public void EnteringLandmark_StartsAPhysicalJourney_InsteadOfTeleporting()
    {
        var sim = TestWorld.NewSim();
        var worker = HatchedWorker(sim);
        var gardenNursery = LandmarkCatalog.ToWorld(RegionIds.GardenClearing,
            WorldLayout.NurseryPosition).Absolute;
        var reedwaterNursery = LandmarkCatalog.ToWorld(RegionIds.ReedwaterBank,
            RegionCatalog.Reedwater.NurseryPosition).Absolute;
        Assert.InRange(Vec3.Distance(worker.WorldPosition, gardenNursery), 0, 2);

        Assert.True(sim.EnterLandmark(RegionIds.ReedwaterBank));
        Assert.True(worker.Travelling);
        Assert.Equal(RegionIds.ReedwaterBank, worker.Region);
        Assert.True(worker.JourneyRoute.Count > 2);
        Assert.True(Vec3.Distance(worker.WorldPosition, gardenNursery) < 3,
            "the worker must still be at the garden when the journey starts");
        Assert.True(Vec3.Distance(worker.WorldPosition, reedwaterNursery) > 50);
        Assert.Empty(sim.RegionWorkers);

        sim.AdvanceSeconds(5);
        Assert.True(worker.Travelling);
        Assert.True(Vec3.Distance(worker.WorldPosition, gardenNursery) > 5);

        AdvanceUntilArrived(sim, worker);
        Assert.Equal(RegionIds.ReedwaterBank, worker.Region);
        Assert.Equal(RegionCatalog.Reedwater.NurseryPosition, worker.Position);
        Assert.InRange(Vec3.Distance(worker.WorldPosition, reedwaterNursery), 0, 1);
        Assert.Single(sim.RegionWorkers);
    }

    [Fact]
    public void WorkerJourney_ContinuesAfterSaveAndLoad()
    {
        var sim = TestWorld.NewSim();
        UnlockStory(sim);
        var worker = HatchedWorker(sim);
        Assert.True(sim.EnterLandmark(RegionIds.ReedwaterBank));
        sim.AdvanceSeconds(8);
        var savedWorld = worker.WorldPosition;
        var data = sim.ExportSave(new Vec3(0, 0.55, 8.5), 0, new SettingsData());
        data.Seed = sim.Tuning.Seed;
        Assert.True(data.Workers[0].Travelling);

        var tuning = TestWorld.RichTuning(t => t.Seed = sim.Tuning.Seed);
        var restored = GameSimulation.FromSave(data, tuning, MissionsConfig.Defaults());
        var restoredWorker = Assert.Single(restored.Workers);
        Assert.True(restoredWorker.Travelling);
        Assert.InRange(Vec3.Distance(restoredWorker.WorldPosition, savedWorld), 0, 1);

        AdvanceUntilArrived(restored, restoredWorker);
        Assert.Equal(RegionIds.ReedwaterBank, restoredWorker.Region);
        var nursery = LandmarkCatalog.ToWorld(RegionIds.ReedwaterBank,
            RegionCatalog.Reedwater.NurseryPosition).Absolute;
        Assert.InRange(Vec3.Distance(restoredWorker.WorldPosition, nursery), 0, 1);
    }

    [Fact]
    public void ReturningMidJourney_PrefersTheReverseRoute()
    {
        var sim = TestWorld.NewSim();
        var worker = HatchedWorker(sim);
        Assert.True(sim.EnterLandmark(RegionIds.ReedwaterBank));
        sim.AdvanceSeconds(6);
        var travelled = worker.JourneyIndex;
        Assert.True(travelled > 1);

        Assert.True(sim.EnterLandmark(RegionIds.GardenClearing));
        Assert.True(worker.Travelling);
        Assert.Equal(RegionIds.GardenClearing, worker.JourneyTargetRegion);
        Assert.Equal(RegionIds.GardenClearing, worker.JourneyFromRegion);
        Assert.True(worker.JourneyRoute.Count >= travelled);
        var gardenNursery = LandmarkCatalog.ToWorld(RegionIds.GardenClearing,
            WorldLayout.NurseryPosition).Absolute;
        AdvanceUntilArrived(sim, worker);
        Assert.Equal(RegionIds.GardenClearing, worker.Region);
        Assert.InRange(Vec3.Distance(worker.WorldPosition, gardenNursery), 0, 1);
    }

    [Fact]
    public void SummonWorkers_WalkToThePlayerAndWait()
    {
        var sim = TestWorld.NewSim();
        var worker = HatchedWorker(sim);
        var player = new Vec3(300, 0.5, -120);
        Assert.True(sim.TrySummonWorkers(player, RegionIds.GardenClearing, out var reason), reason);
        Assert.True(worker.Travelling);
        Assert.True(worker.Summoned);
        Assert.True(Vec3.Distance(worker.WorldPosition, player) > 200,
            "summoning must not teleport the helper");
        Assert.InRange(Vec3.Distance(worker.JourneyDestination, player), 0, 4);

        AdvanceUntilArrived(sim, worker);
        Assert.False(worker.Travelling);
        Assert.True(worker.Summoned);
        Assert.Equal(RegionIds.GardenClearing, worker.Region);
        Assert.Equal("", worker.CurrentNode);
        Assert.Equal(WorkerActivity.Waiting, worker.Activity);
        Assert.InRange(Vec3.Distance(worker.WorldPosition, player), 0, 5);
    }

    [Fact]
    public void SummonedHelpers_SaveAndLoadTheirWildPosition()
    {
        var sim = TestWorld.NewSim();
        var worker = HatchedWorker(sim);
        var player = new Vec3(300, 0.5, -120);
        Assert.True(sim.TrySummonWorkers(player, RegionIds.GardenClearing, out var reason), reason);
        AdvanceUntilArrived(sim, worker);
        var data = sim.ExportSave(new Vec3(0, 0.55, 8.5), 0, new SettingsData());
        data.Seed = sim.Tuning.Seed;
        Assert.True(data.Workers[0].Summoned);
        Assert.True(Math.Abs(data.Workers[0].X) > 60,
            "the summoned helper is far outside the authored garden");

        var tuning = TestWorld.RichTuning(t => t.Seed = sim.Tuning.Seed);
        var restored = GameSimulation.FromSave(data, tuning, MissionsConfig.Defaults());
        var restoredWorker = Assert.Single(restored.Workers);
        Assert.True(restoredWorker.Summoned);
        Assert.Equal("", restoredWorker.CurrentNode);
        Assert.InRange(Vec3.Distance(restoredWorker.WorldPosition, player), 0, 6);
    }

    [Fact]
    public void SummonWithoutHelpers_ExplainsWhatToDo()
    {
        var sim = TestWorld.NewSim();
        Assert.False(sim.TrySummonWorkers(new Vec3(20, 0.5, 20),
            RegionIds.GardenClearing, out var reason));
        Assert.Contains("Hatch", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SummonedWorkerPositions_AreValidatedAsWildPositions()
    {
        var sim = TestWorld.NewSim();
        var worker = HatchedWorker(sim);
        var player = new Vec3(300, 0.5, -120);
        Assert.True(sim.TrySummonWorkers(player, RegionIds.GardenClearing, out _));
        AdvanceUntilArrived(sim, worker);
        var data = sim.ExportSave(new Vec3(0, 0.55, 8.5), 0, new SettingsData());
        data.Seed = sim.Tuning.Seed;
        Assert.True(SaveValidator.Validate(data, out var error), error);
        data.Workers[0].Summoned = false;
        Assert.False(SaveValidator.Validate(data, out _));
    }

    [Fact]
    public void JourneyFromTheSouthBank_CrossesTheVisibleBridge()
    {
        var sim = TestWorld.NewSim();
        var worker = HatchedWorker(sim);
        var generator = new WorldGenerator(sim.Tuning.Seed);
        var southBank = new Vec3(0, generator.HeightAt(0, 75) + 0.3, 75);
        Assert.True(sim.BeginWorkerJourneyForTest(worker.Id, RegionIds.GardenClearing, southBank));
        Assert.True(worker.Travelling);
        Assert.True(worker.JourneyUsesBridge);

        AdvanceUntilArrived(sim, worker);
        Assert.Equal(RegionIds.GardenClearing, worker.Region);
        Assert.InRange(Vec3.Distance(worker.WorldPosition,
            LandmarkCatalog.ToWorld(RegionIds.GardenClearing, WorldLayout.NurseryPosition).Absolute), 0, 1);
    }
}
