using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>P7/H1 contracts: dry world routes through real crossings, physical
/// journeys, helper independence and individual summon/follow/release.</summary>
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
        var before = sim.Workers.Count;
        Assert.True(sim.TryStartHatch(out var reason), reason);
        sim.AdvanceSeconds(4);
        Assert.Equal(before + 1, sim.Workers.Count);
        return sim.Workers[^1];
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
    public void EnteringLandmark_KeepsHelperTaskAndPhysicalPosition()
    {
        var sim = TestWorld.NewSim();
        Assert.True(sim.TryBuildWeb(WorldLayout.GapWest, WorldLayout.GapEast,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB,
            WebType.Capture, out _, out _));
        sim.AdvanceSeconds(12);
        var worker = HatchedWorker(sim);
        sim.AdvanceSeconds(1.2);
        var activity = worker.Activity;
        var insect = worker.TargetInsectId;
        var world = worker.WorldPosition;
        Assert.NotEqual(WorkerActivity.Resting, activity);

        Assert.True(sim.EnterLandmark(RegionIds.ReedwaterBank));
        Assert.Equal(RegionIds.GardenClearing, worker.Region);
        Assert.False(worker.Travelling);
        Assert.Equal(activity, worker.Activity);
        Assert.Equal(insect, worker.TargetInsectId);
        Assert.InRange(Vec3.Distance(world, worker.WorldPosition), 0, 0.01);

        sim.AdvanceSeconds(8);
        Assert.Equal(RegionIds.GardenClearing, worker.Region);
        Assert.False(worker.Travelling);
        Assert.True(worker.CollectCount >= 1 || worker.BridgeCrossings >= 1 ||
                    worker.Activity == WorkerActivity.Patrolling,
            $"helper stopped working: {worker.DisplayState}");
    }

    [Fact]
    public void IndividualSummon_MovesOnlyTheNamedHelperWithoutTeleporting()
    {
        var sim = TestWorld.NewSim();
        var first = HatchedWorker(sim);
        var second = HatchedWorker(sim);
        var player = new Vec3(60, 0.5, 8);
        var firstBefore = first.WorldPosition;
        var secondBefore = second.WorldPosition;

        Assert.True(sim.TrySummonWorker(first.Id, player, RegionIds.GardenClearing,
            out var reason), reason);
        Assert.True(first.Travelling);
        Assert.True(first.Summoned);
        Assert.False(second.Travelling);
        Assert.False(second.Summoned);
        Assert.True(Vec3.Distance(first.WorldPosition, firstBefore) < 3,
            "summoning must not teleport the helper");
        Assert.InRange(Vec3.Distance(second.WorldPosition, secondBefore), 0, 0.01);
        Assert.Contains(sim.Events, e => e.Type == "WorkerSummoned" && e.SubjectId == first.Id);
    }

    [Fact]
    public void SummonedHelper_FollowsThePlayerAfterArrival()
    {
        var sim = TestWorld.NewSim();
        var worker = HatchedWorker(sim);
        var player = new Vec3(120, 0.55, 6);
        sim.SetPlayerWorldPosition(player);
        Assert.True(sim.TrySummonWorker(worker.Id, player, RegionIds.ReedwaterBank, out _));
        AdvanceUntilArrived(sim, worker);
        Assert.True(worker.Summoned);
        var arrived = worker.WorldPosition;
        Assert.InRange(Vec3.Distance(arrived, player), 0, 5);

        var moved = player + new Vec3(18, 0, 4);
        sim.SetPlayerWorldPosition(moved);
        sim.AdvanceSeconds(0.2);
        Assert.True(worker.Travelling, "the helper should walk after the player moves");
        Assert.True(Vec3.Distance(worker.WorldPosition, arrived) < 6,
            "following must start from the arrival point, not teleport");
        AdvanceUntilArrived(sim, worker);
        Assert.InRange(Vec3.Distance(worker.WorldPosition, moved), 0, 6);
        Assert.True(worker.Summoned);
    }

    [Fact]
    public void SummoningAnotherHelper_ReleasesTheFirstPredictably()
    {
        var sim = TestWorld.NewSim();
        var first = HatchedWorker(sim);
        var second = HatchedWorker(sim);
        var player = new Vec3(80, 0.55, 6);
        Assert.True(sim.TrySummonWorker(first.Id, player, RegionIds.GardenClearing, out _));
        sim.AdvanceSeconds(3);
        Assert.True(sim.TrySummonWorker(second.Id, player, RegionIds.GardenClearing, out _));
        Assert.False(first.Summoned);
        Assert.True(second.Summoned);
        Assert.True(first.Travelling);
        Assert.Equal(RegionIds.GardenClearing, first.JourneyTargetRegion);
        Assert.Contains(sim.Events, e => e.Type == "WorkerReleased" && e.SubjectId == first.Id);
    }

    [Fact]
    public void ReleasedHelper_WalksBackHomeBeforeResumingDuties()
    {
        var sim = TestWorld.NewSim();
        var worker = HatchedWorker(sim);
        var player = new Vec3(120, 0.55, 6);
        sim.SetPlayerWorldPosition(player);
        Assert.True(sim.TrySummonWorker(worker.Id, player, RegionIds.ReedwaterBank, out _));
        AdvanceUntilArrived(sim, worker);
        Assert.True(sim.ReleaseWorkerSummon(worker.Id, out var reason), reason);
        Assert.False(worker.Summoned);
        Assert.True(worker.Travelling);
        Assert.Equal(RegionIds.GardenClearing, worker.JourneyTargetRegion);
        AdvanceUntilArrived(sim, worker);
        var home = LandmarkCatalog.ToWorld(RegionIds.GardenClearing,
            WorldLayout.NurseryPosition).Absolute;
        Assert.Equal(RegionIds.GardenClearing, worker.Region);
        Assert.InRange(Vec3.Distance(worker.WorldPosition, home), 0, 8);
    }

    [Fact]
    public void SummonedHelper_SurvivesReturnHomeAndSaveLoad()
    {
        var sim = TestWorld.NewSim();
        var worker = HatchedWorker(sim);
        var player = new Vec3(120, 0.55, 6);
        sim.SetPlayerWorldPosition(player);
        Assert.True(sim.TrySummonWorker(worker.Id, player, RegionIds.ReedwaterBank, out _));
        AdvanceUntilArrived(sim, worker);
        sim.SetPlayerWorldPosition(new Vec3(10, 0.55, 8.5));
        sim.AdvanceSeconds(0.3);
        Assert.True(worker.Travelling);

        var data = sim.ExportSave(new Vec3(10, 0.55, 8.5), 0, new SettingsData());
        data.Seed = sim.Tuning.Seed;
        Assert.True(SaveValidator.Validate(data, out var error), error);
        var restored = GameSimulation.FromSave(data, TestWorld.RichTuning(t => t.Seed = sim.Tuning.Seed),
            MissionsConfig.Defaults());
        var restoredWorker = Assert.Single(restored.Workers);
        Assert.True(restoredWorker.Summoned);
        Assert.True(restoredWorker.Travelling);
        Assert.Equal(RegionIds.GardenClearing, restoredWorker.HomeRegion);
    }

    [Fact]
    public void WorkerJourney_ContinuesAfterSaveAndLoad()
    {
        var sim = TestWorld.NewSim();
        UnlockStory(sim);
        var worker = HatchedWorker(sim);
        var target = LandmarkCatalog.ToWorld(RegionIds.ReedwaterBank,
            RegionCatalog.Reedwater.NurseryPosition).Absolute;
        sim.SetPlayerWorldPosition(target);
        Assert.True(sim.TrySummonWorker(worker.Id, target, RegionIds.ReedwaterBank, out _));
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
        Assert.InRange(Vec3.Distance(restoredWorker.WorldPosition, target), 0, 6);
    }

    [Fact]
    public void SummonWithoutHelpers_ExplainsWhatToDo()
    {
        var sim = TestWorld.NewSim();
        Assert.False(sim.TrySummonWorker("worker_1", new Vec3(20, 0.5, 20),
            RegionIds.GardenClearing, out var reason));
        Assert.Contains("colony", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SummonedWorkerPositions_AreValidatedAsWildPositions()
    {
        var sim = TestWorld.NewSim();
        var worker = HatchedWorker(sim);
        var player = new Vec3(300, 0.5, -120);
        sim.SetPlayerWorldPosition(player);
        Assert.True(sim.TrySummonWorker(worker.Id, player, RegionIds.GardenClearing, out _));
        AdvanceUntilArrived(sim, worker);
        var data = sim.ExportSave(new Vec3(0, 0.55, 8.5), 0, new SettingsData());
        data.Seed = sim.Tuning.Seed;
        Assert.True(SaveValidator.Validate(data, out var error), error);
        data.Workers[0].Summoned = false;
        data.Workers[0].Travelling = false;
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
            LandmarkCatalog.ToWorld(RegionIds.GardenClearing, WorldLayout.NurseryPosition).Absolute), 0, 8);
    }
}
