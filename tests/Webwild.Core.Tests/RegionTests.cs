using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class RegionTests
{
    private static GameSimulation UnlockedSim()
    {
        var sim = TestWorld.NewSim();
        sim.Missions.Restore(
            3, 0,
            new[] { 2, 3, 3 },
            new[] { "M01", "M02", "M03" },
            Array.Empty<string>(),
            victoryAwarded: true);
        return sim;
    }

    private static void UnlockAndTravel(GameSimulation sim)
    {
        Assert.True(sim.TryTravelTo(RegionIds.ReedwaterBank, out var reason), reason);
    }

    [Fact]
    public void RegionIdsAndAnchors_AreStableUniqueAndScoped()
    {
        Assert.Equal(new[] { RegionIds.GardenClearing, RegionIds.ReedwaterBank },
            RegionCatalog.All.Select(region => region.Id));
        Assert.Equal(RegionCatalog.All.Count,
            RegionCatalog.All.Select(region => region.Id).Distinct(StringComparer.Ordinal).Count());

        var allAnchors = RegionCatalog.AllAnchors.ToList();
        Assert.Equal(allAnchors.Count,
            allAnchors.Select(anchor => anchor.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(allAnchors, anchor => Assert.True(RegionIds.IsKnown(anchor.Region)));
        Assert.All(RegionCatalog.Reedwater.Anchors, anchor =>
            Assert.Equal(RegionIds.ReedwaterBank, anchor.Region));
        Assert.All(RegionCatalog.Reedwater.InsectRoutes, route =>
            Assert.Equal(RegionIds.ReedwaterBank, route.Region));
        Assert.Equal(10, RegionCatalog.Reedwater.Anchors.Count);
        Assert.Equal(12, RegionCatalog.Garden.Anchors.Count);
    }

    [Fact]
    public void ReedwaterIsLockedUntilTheGardenStoryIsComplete()
    {
        var sim = TestWorld.NewSim();
        Assert.False(sim.ReedwaterUnlocked);
        Assert.False(sim.TryTravelTo(RegionIds.ReedwaterBank, out var reason));
        Assert.Contains("story", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RegionIds.GardenClearing, sim.ActiveRegionId);

        var unlocked = UnlockedSim();
        Assert.True(unlocked.ReedwaterUnlocked);
        Assert.True(unlocked.TryTravelTo(RegionIds.ReedwaterBank, out _));
        Assert.Equal(RegionIds.ReedwaterBank, unlocked.ActiveRegionId);
        Assert.Contains(RegionIds.ReedwaterBank, unlocked.VisitedRegions);
        Assert.True(unlocked.TryTravelTo(RegionIds.GardenClearing, out _));
    }

    [Fact]
    public void ReedwaterSupplyCache_IsGrantedExactlyOnce()
    {
        var sim = UnlockedSim();
        var food = sim.Ledger.Food;
        var silk = sim.Ledger.Silk;
        Assert.True(sim.TryTravelTo(RegionIds.ReedwaterBank, out _));
        Assert.Equal(food + 6, sim.Ledger.Food);
        Assert.Equal(silk + 24, sim.Ledger.Silk);
        Assert.True(sim.TryTravelTo(RegionIds.GardenClearing, out _));
        Assert.True(sim.TryTravelTo(RegionIds.ReedwaterBank, out _));
        Assert.Equal(food + 6, sim.Ledger.Food);
        Assert.Equal(silk + 24, sim.Ledger.Silk);

        var data = sim.ExportSave(new Vec3(10, 0.5, 8.5), 0, new SettingsData());
        var restored = GameSimulation.FromSave(data, TestWorld.RichTuning(), MissionsConfig.Defaults());
        Assert.True(restored.TryTravelTo(RegionIds.GardenClearing, out _));
        Assert.True(restored.TryTravelTo(RegionIds.ReedwaterBank, out _));
        Assert.Equal(food + 6, restored.Ledger.Food);
        Assert.Equal(silk + 24, restored.Ledger.Silk);
    }

    [Fact]
    public void ReedwaterSupplyCache_SurvivesLedgerHistoryEvictionAndSaveRoundTrip()
    {
        var sim = UnlockedSim();
        var food = sim.Ledger.Food;
        var silk = sim.Ledger.Silk;
        Assert.True(sim.TryTravelTo(RegionIds.ReedwaterBank, out var reason), reason);
        Assert.Equal(food + 6, sim.Ledger.Food);
        Assert.Equal(silk + 24, sim.Ledger.Silk);

        // Push the one-time grant key out of the bounded 512-key ledger
        // history. An immediate double-visit does not prove the fix.
        for (var i = 0; i < ResourceLedger.HistoryRetentionLimit + 90; i++)
            Assert.True(sim.Ledger.TryGrant(0, 0, $"history-filler-{i}"));
        Assert.False(sim.Ledger.HasProcessed($"grant:region_supply_{RegionIds.ReedwaterBank}"));

        for (var i = 0; i < 4; i++)
        {
            Assert.True(sim.TryTravelTo(RegionIds.GardenClearing, out reason), reason);
            Assert.True(sim.TryTravelTo(RegionIds.ReedwaterBank, out reason), reason);
        }
        Assert.Equal(food + 6, sim.Ledger.Food);
        Assert.Equal(silk + 24, sim.Ledger.Silk);
        Assert.Contains(RegionIds.ReedwaterBank, sim.ClaimedSupplyCaches);

        var data = sim.ExportSave(new Vec3(10, 0.5, 8.5), 0, new SettingsData());
        var restored = GameSimulation.FromSave(data, TestWorld.RichTuning(), MissionsConfig.Defaults());
        Assert.Contains(RegionIds.ReedwaterBank, restored.ClaimedSupplyCaches);
        Assert.False(restored.Ledger.HasProcessed(
            $"grant:region_supply_{RegionIds.ReedwaterBank}"));
        Assert.True(restored.TryTravelTo(RegionIds.GardenClearing, out reason), reason);
        Assert.True(restored.TryTravelTo(RegionIds.ReedwaterBank, out reason), reason);
        Assert.Equal(food + 6, restored.Ledger.Food);
        Assert.Equal(silk + 24, restored.Ledger.Silk);
        Assert.True(restored.Ledger.Food >= 0);
        Assert.True(restored.Ledger.Silk >= 0);
    }

    [Fact]
    public void ReedwaterBridgeSpansFitTheExistingBridgeRules()
    {
        var anchors = RegionCatalog.Reedwater.Anchors.ToDictionary(anchor => anchor.Id, StringComparer.Ordinal);
        foreach (var pair in new[]
                 {
                     (ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost),
                     (ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost),
                     (ReedwaterLayout.IslandBPost, ReedwaterLayout.FarPost)
                 })
        {
            var distance = Vec3.Distance(anchors[pair.Item1].Position, anchors[pair.Item2].Position);
            Assert.InRange(distance, 1.0, 6.0);
        }
    }

    [Fact]
    public void ReedwaterGroundGraph_HasNoDryRouteAcrossThePond()
    {
        var graph = new WorldGraph(ReedwaterLayout.CreateNodes(), ReedwaterLayout.CreateGroundEdges());
        Assert.Null(graph.FindRoute(ReedwaterLayout.NurseryNode, ReedwaterLayout.FirstDestinationNode));
        Assert.Null(graph.FindRoute(ReedwaterLayout.NurseryNode, ReedwaterLayout.FarA));
        Assert.NotNull(graph.FindRoute(ReedwaterLayout.NurseryNode, ReedwaterLayout.EasyA));
    }

    [Fact]
    public void TwoBridgesOpenR01_AndPhysicalArrivalAwardsCleverCrossing()
    {
        var sim = UnlockedSim();
        UnlockAndTravel(sim);

        Assert.True(sim.TryBuildWeb(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost,
            WebType.Bridge, out _, out var firstReason), firstReason.Reason);
        Assert.DoesNotContain("R01", sim.RegionalObjectives.CompletedObjectives);
        Assert.Null(sim.Graph.FindRoute(ReedwaterLayout.NurseryNode, ReedwaterLayout.FirstDestinationNode));

        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost,
            WebType.Bridge, out _, out var secondReason), secondReason.Reason);
        Assert.NotNull(sim.Graph.FindRoute(ReedwaterLayout.NurseryNode, ReedwaterLayout.FirstDestinationNode));
        Assert.Contains("R01", sim.RegionalObjectives.CompletedObjectives);
        Assert.Equal("R02", sim.RegionalObjectives.ActiveObjectiveId);
        Assert.Equal(2, sim.ReedwaterBridgeWebsBuilt);

        // Building the second bridge alone must not award the badge.
        Assert.False(sim.RegionalObjectives.MasteryAwarded);
        Assert.DoesNotContain(sim.Events, e => e.Type == "RegionalMasteryEarned");

        // Standing at the outpost is not arrival either.
        Assert.False(sim.TryAwardCleverCrossingOnArrival(ReedwaterLayout.NurseryPosition));
        Assert.False(sim.RegionalObjectives.MasteryAwarded);

        // Physically reaching the lily island does.
        Assert.True(sim.TryAwardCleverCrossingOnArrival(
            ReedwaterLayout.FirstDestinationPosition + new Vec3(1.2, 0, -0.8)));
        Assert.True(sim.RegionalObjectives.MasteryAwarded);
        Assert.Contains(sim.Events, e => e.Type == "RegionalMasteryEarned");
        Assert.False(sim.TryAwardCleverCrossingOnArrival(ReedwaterLayout.FirstDestinationPosition));
    }

    [Fact]
    public void CleverCrossing_IsNotAwardedWithThreeStandingBridges()
    {
        var sim = UnlockedSim();
        UnlockAndTravel(sim);
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandBPost, ReedwaterLayout.FarPost,
            WebType.Bridge, out _, out _));
        Assert.Equal(3, sim.ReedwaterBridgeCount);
        Assert.False(sim.TryAwardCleverCrossingOnArrival(ReedwaterLayout.FirstDestinationPosition));
        Assert.False(sim.RegionalObjectives.MasteryAwarded);
    }

    [Fact]
    public void CleverCrossing_SurvivesSaveAndReload()
    {
        var sim = UnlockedSim();
        UnlockAndTravel(sim);
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryAwardCleverCrossingOnArrival(ReedwaterLayout.FirstDestinationPosition));

        var data = sim.ExportSave(ReedwaterLayout.FirstDestinationPosition, 0, new SettingsData());
        var restored = GameSimulation.FromSave(data, TestWorld.RichTuning(), MissionsConfig.Defaults());
        Assert.True(restored.RegionalObjectives.MasteryAwarded);
        Assert.False(restored.TryAwardCleverCrossingOnArrival(ReedwaterLayout.FirstDestinationPosition));
    }

    [Fact]
    public void FarBankDelivery_CountsWhenTheHelperIsAlreadyOnTheFarBank()
    {
        var sim = UnlockedSim();
        UnlockAndTravel(sim);
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandBPost, ReedwaterLayout.FarPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryStartHatch(out var reason), reason);
        sim.AdvanceSeconds(5);
        var worker = Assert.Single(sim.Workers);
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.EasyA, ReedwaterLayout.EasyB,
            WebType.Capture, out _, out _));
        var guard = 0;
        while (!sim.RegionalObjectives.CompletedObjectives.Contains("R02") && guard++ < 80)
        {
            sim.AdvanceSeconds(1);
            foreach (var insect in sim.RegionInsects.Where(i => i.CanBeCollected).ToList())
                sim.TryCollectInsect(insect.Id, "player", out _, out _);
        }
        Assert.Contains("R02", sim.RegionalObjectives.CompletedObjectives);

        // Simulate a helper that already patrolled to the far bank: its current
        // collection leg uses ground edges only, not a bridge.
        sim.ReleaseWorkerTask(worker, "test setup", keepPosition: true);
        worker.CurrentNode = ReedwaterLayout.FarBankNode;
        worker.Position = ReedwaterLayout.FarBankPosition;
        worker.Activity = WorkerActivity.Resting;
        worker.PatrolCooldown = 0;
        worker.BridgeCrossings = 0;
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.FarA, ReedwaterLayout.FarB,
            WebType.Capture, out _, out _));

        var seconds = 0;
        while (!sim.RegionalObjectives.CompletedObjectives.Contains("R03") && seconds < 60)
        {
            sim.AdvanceSeconds(1);
            seconds++;
        }
        Assert.Contains("R03", sim.RegionalObjectives.CompletedObjectives);
        Assert.Contains(sim.Events, e => e.Type == "FarBankDelivery");
    }

    [Fact]
    public void ReversedBridgeSelectionOrder_BuildsTheSameRoute()
    {
        var sim = UnlockedSim();
        UnlockAndTravel(sim);
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandAPost, ReedwaterLayout.StartPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandBPost, ReedwaterLayout.IslandAPost,
            WebType.Bridge, out _, out _));
        Assert.NotNull(sim.Graph.FindRoute(ReedwaterLayout.NurseryNode, ReedwaterLayout.FirstDestinationNode));
        Assert.Contains("R01", sim.RegionalObjectives.CompletedObjectives);
    }

    [Fact]
    public void RemovingACriticalBridgeInvalidatesTheWorkerRoute()
    {
        var sim = UnlockedSim();
        UnlockAndTravel(sim);
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost,
            WebType.Bridge, out var second, out _));
        Assert.NotNull(sim.Graph.FindRoute(ReedwaterLayout.NurseryNode,
            ReedwaterLayout.FirstDestinationNode));
        Assert.True(sim.TryDismantleWeb(second!.Id, out _, out _));
        Assert.Null(sim.Graph.FindRoute(ReedwaterLayout.NurseryNode,
            ReedwaterLayout.FirstDestinationNode));
    }

    [Fact]
    public void ReedwaterFeedingObjective_RequiresThreeRegionalInsects()
    {
        var sim = UnlockedSim();
        UnlockAndTravel(sim);
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.EasyA, ReedwaterLayout.EasyB,
            WebType.Capture, out _, out _));

        var guard = 0;
        while (!sim.RegionalObjectives.CompletedObjectives.Contains("R02") && guard++ < 80)
        {
            sim.AdvanceSeconds(5);
            foreach (var insect in sim.RegionInsects.Where(i => i.CanBeCollected).ToList())
                sim.TryCollectInsect(insect.Id, "player", out _, out _);
        }

        Assert.Contains("R02", sim.RegionalObjectives.CompletedObjectives);
        Assert.Equal("R03", sim.RegionalObjectives.ActiveObjectiveId);
    }

    [Fact]
    public void WorkerCannotReachTheFarBankWithoutBridges()
    {
        var sim = UnlockedSim();
        UnlockAndTravel(sim);
        Assert.True(sim.TryStartHatch(out var reason), reason);
        sim.AdvanceSeconds(5);
        var worker = Assert.Single(sim.Workers);
        Assert.Equal(RegionIds.ReedwaterBank, worker.Region);
        Assert.Equal(ReedwaterLayout.NurseryNode, worker.CurrentNode);
        Assert.Null(sim.Graph.FindRoute(worker.CurrentNode, ReedwaterLayout.FarA));

        sim.AdvanceSeconds(30);
        Assert.DoesNotContain(sim.Events, e => e.Type == "FarBankDelivery");
        Assert.DoesNotContain(worker.PathNodes, node => node == ReedwaterLayout.FarA);
    }

    [Fact]
    public void WorkerCrossesThreeBridgesAndCompletesR03()
    {
        var sim = UnlockedSim();
        UnlockAndTravel(sim);

        // R01 route.
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost,
            WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandBPost, ReedwaterLayout.FarPost,
            WebType.Bridge, out _, out _));

        // R02: three catches at the easy reed flyway.
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.EasyA, ReedwaterLayout.EasyB,
            WebType.Capture, out _, out _));
        var guard = 0;
        while (!sim.RegionalObjectives.CompletedObjectives.Contains("R02") && guard++ < 80)
        {
            sim.AdvanceSeconds(5);
            foreach (var insect in sim.RegionInsects.Where(i => i.CanBeCollected).ToList())
                sim.TryCollectInsect(insect.Id, "player", out _, out _);
        }
        Assert.Equal("R03", sim.RegionalObjectives.ActiveObjectiveId);

        // Far-bank capture and a helper in Reedwater.
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.FarA, ReedwaterLayout.FarB,
            WebType.Capture, out _, out _));
        Assert.True(sim.TryStartHatch(out var hatchReason), hatchReason);
        sim.AdvanceSeconds(5);
        Assert.Single(sim.Workers);
        Assert.NotNull(sim.Graph.FindRoute(ReedwaterLayout.NurseryNode, ReedwaterLayout.FarA));

        guard = 0;
        while (!sim.RegionalObjectives.CompletedObjectives.Contains("R03") && guard++ < 120)
            sim.AdvanceSeconds(2);

        Assert.Contains("R03", sim.RegionalObjectives.CompletedObjectives);
        Assert.Contains(sim.Events, e => e.Type == "FarBankDelivery");
        Assert.Contains(sim.Events, e => e.Type == "WorkerUsedBridge" && e.Region == RegionIds.ReedwaterBank);
        Assert.Contains(sim.Events, e => e.Type == "RegionalObjectivesCompleted");
        Assert.True(sim.RegionalObjectives.AllComplete);
        Assert.Equal(2, sim.TotalSpiders);
    }

    [Fact]
    public void SavingDuringReedwaterHatch_KeepsTheHatchInItsRegion()
    {
        var sim = UnlockedSim();
        UnlockAndTravel(sim);
        Assert.True(sim.TryStartHatch(out var reason), reason);
        sim.AdvanceSeconds(1);
        var data = sim.ExportSave(new Vec3(10, 0.5, 8.5), 0, new SettingsData());
        Assert.Equal(RegionIds.ReedwaterBank, data.Counters.PendingHatchRegionId);

        var restored = GameSimulation.FromSave(data, TestWorld.RichTuning(), MissionsConfig.Defaults());
        Assert.NotNull(restored.PendingHatch);
        Assert.Equal(RegionIds.ReedwaterBank, restored.PendingHatch!.RegionId);
        restored.AdvanceSeconds(4);
        Assert.Single(restored.Workers);
        Assert.Equal(RegionIds.ReedwaterBank, restored.Workers[0].Region);
        Assert.Equal(ReedwaterLayout.NurseryNode, restored.Workers[0].CurrentNode);
    }

    [Fact]
    public void SavingInReedwater_PreservesRegionsObjectivesAndResources()
    {
        var sim = UnlockedSim();
        UnlockAndTravel(sim);
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost,
            WebType.Bridge, out var first, out _));
        Assert.True(sim.TryBuildWeb(ReedwaterLayout.IslandAPost, ReedwaterLayout.IslandBPost,
            WebType.Bridge, out var second, out _));
        sim.SetRegionPlayerPosition(RegionIds.ReedwaterBank, new Vec3(2.5, 0.6, 5.0), 1.1);
        sim.SetRegionPlayerPosition(RegionIds.GardenClearing, new Vec3(-9.0, 0.5, 2.0), -0.4);
        var data = sim.ExportSave(new Vec3(9.0, 0.6, 8.0), 0.8, new SettingsData());

        Assert.Equal(RegionIds.ReedwaterBank, data.ActiveRegionId);
        Assert.Equal(2, data.Webs.Count);
        Assert.All(data.Webs, web => Assert.Equal(RegionIds.ReedwaterBank, web.Region));
        Assert.Contains("R01", data.RegionalObjectives.Completed);
        Assert.Equal(2, data.RegionPlayers.Count);

        var restored = GameSimulation.FromSave(data, TestWorld.RichTuning(), MissionsConfig.Defaults());
        Assert.Equal(RegionIds.ReedwaterBank, restored.ActiveRegionId);
        Assert.Equal(sim.Ledger.Food, restored.Ledger.Food);
        Assert.Equal(sim.Ledger.Silk, restored.Ledger.Silk);
        Assert.Equal(2, restored.RegionWebs.Count);
        Assert.Contains("R01", restored.RegionalObjectives.CompletedObjectives);
        Assert.Equal("R02", restored.RegionalObjectives.ActiveObjectiveId);
        Assert.Equal(0.8, restored.GetRegionPlayerPosition(RegionIds.ReedwaterBank).RotationY, 3);
        Assert.NotNull(restored.Graph.FindRoute(ReedwaterLayout.NurseryNode,
            ReedwaterLayout.FirstDestinationNode));

        // Restoring and continuing cannot duplicate a reward.
        var foodBefore = restored.Ledger.Food;
        restored.RegionalObjectives.TryAwardMastery(2);
        Assert.True(restored.RegionalObjectives.MasteryAwarded);
        Assert.Equal(foodBefore, restored.Ledger.Food);
        Assert.False(restored.RegionalObjectives.TryAwardMastery(2));
    }
}
