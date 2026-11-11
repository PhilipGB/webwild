using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class WorkerAndMissionTests
{
    private static GameSimulation ReadyWorkerScenario(out InsectState trapped)
    {
        var sim = TestWorld.NewSim();
        Assert.True(sim.TryBuildWeb(WorldLayout.GapWest, WorldLayout.GapEast, WebType.Bridge, out _, out _));
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out _, out _));
        sim.AdvanceSeconds(12);
        trapped = sim.Insects.First(i => i.Phase == InsectPhase.Trapped);

        Assert.True(sim.TryStartHatch(out var hatchReason), hatchReason);
        sim.AdvanceSeconds(4);
        Assert.Single(sim.Workers);
        return sim;
    }

    [Fact]
    public void HatchedWorker_CrossesBridgeAndCollectsPrey()
    {
        var sim = ReadyWorkerScenario(out var trapped);
        var foodBefore = sim.Ledger.Food;
        sim.AdvanceSeconds(40);

        Assert.Contains(sim.Events, e => e.Type == "WorkerUsedBridge");
        Assert.True(trapped.Generation > 0 || trapped.Phase == InsectPhase.Retrieved);
        Assert.True(sim.Ledger.Food >= foodBefore + trapped.Definition.FoodYield);
        Assert.Contains(sim.Workers, w => w.CollectCount >= 1);
        Assert.Contains(sim.Events, e => e.Type == "FoodCollected" && e.Detail == sim.Workers[0].Id);
    }

    [Fact]
    public void WorkerDoesNotTeleport_WithoutBridge()
    {
        var sim = TestWorld.NewSim();
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out _, out _));
        sim.AdvanceSeconds(12);
        var trapped = sim.Insects.First(i => i.Phase == InsectPhase.Trapped);
        Assert.True(sim.TryStartHatch(out _));
        sim.AdvanceSeconds(30);

        var worker = Assert.Single(sim.Workers);
        Assert.Equal(InsectPhase.Trapped, trapped.Phase);
        Assert.DoesNotContain(sim.Events, e => e.Type == "FoodCollected" && e.Detail == worker.Id);
        Assert.Null(worker.TargetInsectId);
        // The worker may explore only nodes on the nursery side of the gap.
        Assert.DoesNotContain(WorldLayout.TwigA, worker.PathNodes);
    }

    [Fact]
    public void RemovingBridge_InvalidatesRouteImmediately()
    {
        var sim = TestWorld.NewSim();
        Assert.True(sim.TryBuildWeb(WorldLayout.GapWest, WorldLayout.GapEast, WebType.Bridge, out var bridge, out _));
        Assert.NotNull(sim.Graph.FindRoute(WorldLayout.NurseryNode, WorldLayout.TwigA));
        Assert.True(sim.TryDismantleWeb(bridge!.Id, out _, out _));
        Assert.Null(sim.Graph.FindRoute(WorldLayout.NurseryNode, WorldLayout.TwigA));
    }

    [Fact]
    public void Hatching_StopsAtFiveSpidersIncludingPlayer()
    {
        var sim = TestWorld.NewSim();
        for (var i = 0; i < 4; i++)
        {
            Assert.True(sim.TryStartHatch(out var reason), reason);
            sim.AdvanceSeconds(4);
        }
        Assert.Equal(4, sim.Workers.Count);
        Assert.Equal(5, sim.TotalSpiders);
        Assert.False(sim.CanHatch().CanHatch);
        Assert.Contains("full", sim.CanHatch().Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(sim.TryStartHatch(out _));
    }

    [Fact]
    public void HatchSpendsOnce_AndDisabledReasonExplainsCost()
    {
        var sim = TestWorld.NewSim(t => { t.Initial.Food = 3; t.Initial.Silk = 3; });
        Assert.False(sim.TryStartHatch(out var reason));
        Assert.Contains("4 food and 4 silk", reason);
        Assert.Equal(3, sim.Ledger.Food);

        var rich = TestWorld.NewSim();
        var beforeFood = rich.Ledger.Food;
        var beforeSilk = rich.Ledger.Silk;
        Assert.True(rich.TryStartHatch(out _));
        Assert.Equal(beforeFood - 4, rich.Ledger.Food);
        Assert.Equal(beforeSilk - 4, rich.Ledger.Silk);
        Assert.False(rich.TryStartHatch(out _));
        Assert.Equal(beforeFood - 4, rich.Ledger.Food);
    }

    [Fact]
    public void Missions_AreSequential_AndEventIdempotent()
    {
        var sim = TestWorld.NewSim();
        Assert.Equal("M01", sim.Missions.ActiveMissionId);

        // A bridge event cannot satisfy M01 or skip ahead to M02.
        Assert.True(sim.TryBuildWeb(WorldLayout.GapWest, WorldLayout.GapEast, WebType.Bridge, out _, out _));
        Assert.Equal("M01", sim.Missions.ActiveMissionId);
        Assert.Equal(0, sim.Missions.StepProgress);

        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out _, out _));
        Assert.Equal(1, sim.Missions.StepIndex);
        Assert.Equal(1, sim.Missions.CompletedStepCount);
        Assert.True(sim.TryBuildWeb(WorldLayout.FlowerA, WorldLayout.FlowerB, WebType.Capture, out _, out _));
        Assert.Equal(1, sim.Missions.StepIndex); // duplicate step event ignored

        var duplicate = new GameplayEvent("duplicate-test", "WebBuilt", 1, "x", "capture");
        sim.Missions.Record(duplicate);
        sim.Missions.Record(duplicate);
        Assert.Equal(1, sim.Missions.StepIndex);
    }

    [Fact]
    public void FullStoryMissionFlow_ReachesVictory()
    {
        var sim = TestWorld.NewSim(t => t.Weather.WarningSeconds = 5);

        // M01
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out var capture, out _));
        sim.AdvanceSeconds(12);
        var first = sim.Insects.First(i => i.Phase == InsectPhase.Trapped);
        Assert.True(sim.TryCollectInsect(first.Id, "player", out _, out _));
        Assert.Equal("M02", sim.Missions.ActiveMissionId);

        // M02
        Assert.True(sim.TryBuildWeb(WorldLayout.GapWest, WorldLayout.GapEast, WebType.Bridge, out _, out _));
        Assert.True(sim.TryStartHatch(out _));
        sim.AdvanceSeconds(40);
        Assert.Contains(sim.Events, e => e.Type == "WorkerUsedBridge");
        Assert.Equal("M03", sim.Missions.ActiveMissionId);
        Assert.True(sim.WindStarted);

        // M03: wind, repair, collect afterwards.
        sim.AdvanceSeconds(10);
        Assert.True(sim.WindCompleted);
        Assert.Contains(sim.Events, e => e.Type == "WindEventCompleted");
        if (capture!.Durability >= capture.MaxDurability)
        {
            // A worker may have repaired the web autonomously; that is also valid recovery.
            Assert.Contains(sim.Events, e => e.Type == "WebRestoredOrSheltered");
        }
        else
        {
            Assert.True(sim.TryRepairWeb(capture.Id, false, out var repairReason), repairReason);
            Assert.Contains(sim.Events, e => e.Type == "WebRestoredOrSheltered");
        }

        for (var i = 0; i < 6 && !sim.Victory; i++)
        {
            sim.AdvanceSeconds(10);
            foreach (var caught in sim.Insects.Where(x => x.CanBeCollected).ToList())
                sim.TryCollectInsect(caught.Id, "player", out _, out _);
        }
        Assert.True(sim.Victory);
        Assert.Contains(sim.Events, e => e.Type == "Victory");
        Assert.Equal(3, sim.Missions.CompletedMissions.Count);

        // Continued play does not regrant victory.
        var victoryCount = sim.Events.Count(e => e.Type == "Victory");
        sim.AdvanceSeconds(10);
        Assert.Equal(victoryCount, sim.Events.Count(e => e.Type == "Victory"));
    }

    [Fact]
    public void LateMissionEvents_DoNotCountForCompletedMissions()
    {
        var sim = TestWorld.NewSim();
        sim.AdvanceSeconds(2);
        sim.Missions.Record(new GameplayEvent("late", "FoodCollected", 1));
        Assert.NotEqual("M03", sim.Missions.ActiveMissionId);
        Assert.Empty(sim.Missions.CompletedMissions);
    }

    [Fact]
    public void Mastery_RequiresThreeInsectsWithTwoOrFewerWebs()
    {
        var sim = TestWorld.NewSim();
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out _, out _));
        Assert.True(sim.TryBuildWeb(WorldLayout.FlowerA, WorldLayout.FlowerB, WebType.Capture, out _, out _));
        var guard = 0;
        while (sim.InsectsCollected < 3 && guard++ < 40)
        {
            sim.AdvanceSeconds(10);
            foreach (var insect in sim.Insects.Where(i => i.CanBeCollected).ToList())
                sim.TryCollectInsect(insect.Id, "player", out _, out _);
        }
        Assert.Equal(3, sim.InsectsCollected);
        Assert.Equal(2, sim.CaptureWebsBuilt);
        Assert.True(sim.MasteryAwarded);
        Assert.Contains(sim.Events, e => e.Type == "MasteryEarned");
    }
}
