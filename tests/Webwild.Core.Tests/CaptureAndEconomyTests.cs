using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class CaptureAndEconomyTests
{
    [Fact]
    public void CaptureGeometry_CrossingHits_MissDoesNot()
    {
        var area = CaptureGeometry.BuildArea(new Vec3(0, 1.3, -2), new Vec3(0, 1.3, 2), 1.3);
        Assert.True(CaptureGeometry.TryIntersect(area, new Vec3(-3, 1.8, 0.2), new Vec3(3, 1.8, 0.2), out var hit, out _));
        Assert.InRange(hit.X, -0.1, 0.1);

        Assert.False(CaptureGeometry.TryIntersect(area, new Vec3(-3, 1.8, 4.5), new Vec3(3, 1.8, 4.5), out _, out _));
        Assert.False(CaptureGeometry.TryIntersect(area, new Vec3(-3, 4.5, 0), new Vec3(3, 4.5, 0), out _, out _));
        Assert.False(CaptureGeometry.TryIntersect(area, new Vec3(0.5, 1.8, -3), new Vec3(0.5, 1.8, 3), out _, out _));
    }

    [Fact]
    public void FlyCrossesTutorialWeb_AndIsTrappedExactlyOnce()
    {
        var sim = TestWorld.BuildTutorialWeb(TestWorld.NewSim());
        sim.AdvanceSeconds(12);

        var caught = sim.Insects.Where(i => i.Phase == InsectPhase.Trapped).ToList();
        Assert.Single(caught);
        var trapEvents = sim.Events.Where(e => e.Type == "InsectTrapped").ToList();
        Assert.Single(trapEvents);
        Assert.Equal(caught[0].Id, trapEvents[0].SubjectId);
        Assert.True(caught[0].CanBeCollected);
    }

    [Fact]
    public void OutOfLaneWeb_CatchesNothing_WithoutIntersection()
    {
        var anchors = WorldLayout.CreateAnchors();
        anchors.Add(new AnchorDefinition { Id = "lane_miss_a", Position = new Vec3(0, 1.3, 6.0), Exposure = 0.4 });
        anchors.Add(new AnchorDefinition { Id = "lane_miss_b", Position = new Vec3(2, 1.3, 6.0), Exposure = 0.4 });
        var sim = new GameSimulation(TestWorld.RichTuning(), MissionsConfig.Defaults(), anchors);
        Assert.True(sim.TryBuildWeb("lane_miss_a", "lane_miss_b", WebType.Capture, out _, out _));

        sim.AdvanceSeconds(40);
        Assert.DoesNotContain(sim.Insects, i => i.Phase == InsectPhase.Trapped);
        Assert.DoesNotContain(sim.Events, e => e.Type == "InsectTrapped");
        Assert.Equal(100, sim.Ledger.Food);
    }

    [Fact]
    public void FoodIsGrantedOnce_PerCapture_AndCollectedPreyRespawns()
    {
        var sim = TestWorld.BuildTutorialWeb(TestWorld.NewSim());
        sim.AdvanceSeconds(12);
        var insect = sim.Insects.First(i => i.Phase == InsectPhase.Trapped);
        var before = sim.Ledger.Food;

        Assert.True(sim.TryCollectInsect(insect.Id, "player", out var food, out _));
        Assert.Equal(insect.Definition.FoodYield, food);
        Assert.Equal(before + food, sim.Ledger.Food);
        Assert.Equal(InsectPhase.Retrieved, insect.Phase);

        Assert.False(sim.TryCollectInsect(insect.Id, "player", out var secondFood, out _));
        Assert.Equal(0, secondFood);
        Assert.Equal(before + food, sim.Ledger.Food);
        Assert.Single(sim.Events, e => e.Type == "FoodCollected");

        // Respawn is deterministic and yields a fresh generation that can be caught again.
        sim.AdvanceSeconds(25);
        Assert.True(insect.Generation > 0);
        Assert.NotEqual(InsectPhase.Retrieved, insect.Phase);
    }

    [Fact]
    public void DestroyedWeb_DoesNotCatch()
    {
        var sim = TestWorld.BuildTutorialWeb(TestWorld.NewSim());
        var web = sim.Webs.Single();
        Assert.True(sim.TryDismantleWeb(web.Id, out _, out _));
        sim.AdvanceSeconds(30);
        Assert.DoesNotContain(sim.Insects, i => i.Phase == InsectPhase.Trapped);
    }

    [Fact]
    public void DamagedWeb_StopsCapturing_UntilRepaired()
    {
        var sim = TestWorld.NewSim(t =>
        {
            t.Weather.ExposedWebDamage = 100;
            t.Weather.WarningSeconds = 5;
        });
        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out var first, out _));
        Assert.True(sim.TryBuildWeb(WorldLayout.FlowerA, WorldLayout.FlowerB, WebType.Capture, out var second, out _));
        sim.StartWindWarning();
        sim.AdvanceSeconds(6);

        var zeroWeb = sim.Webs.First(w => w.Durability == 0);
        Assert.False(zeroWeb.CapturesEnabled);
        // The wind rescue keeps at least one capture route alive.
        Assert.Contains(sim.Webs, w => w.Type == WebType.Capture && w.CapturesEnabled);

        Assert.True(sim.TryRepairWeb(zeroWeb.Id, false, out var reason), reason);
        Assert.Equal(100, zeroWeb.Durability);
        Assert.True(zeroWeb.CapturesEnabled);
    }

    [Fact]
    public void ResourceLedger_RejectsNegativeBalancesAndDuplicateKeys()
    {
        var ledger = new ResourceLedger(5, 3);
        Assert.False(ledger.TrySpend(6, 0, "too-much"));
        Assert.False(ledger.TrySpend(-1, 0, "negative"));
        Assert.Equal(5, ledger.Food);
        Assert.True(ledger.TrySpend(2, 1, "ok"));
        Assert.False(ledger.TrySpend(2, 1, "ok"));
        Assert.Equal(3, ledger.Food);
        Assert.Equal(2, ledger.Silk);
        Assert.True(ledger.TryGrant(1, 2, "reward"));
        Assert.False(ledger.TryGrant(1, 2, "reward"));
        Assert.Equal(4, ledger.Food);
        Assert.Equal(4, ledger.Silk);
    }

    [Fact]
    public void EmergencyRecovery_IsLimitedAndOnlyAtLowSilk()
    {
        var sim = TestWorld.NewSim(t => t.Initial.Silk = 1);
        Assert.True(sim.TryUseEmergencyRecovery(out var silk, out _));
        Assert.Equal(4, silk);
        Assert.Equal(5, sim.Ledger.Silk);

        Assert.False(sim.TryUseEmergencyRecovery(out _, out _));

        var sim2 = TestWorld.NewSim(t => t.Initial.Silk = 0);
        Assert.True(sim2.TryUseEmergencyRecovery(out _, out _));
        Assert.True(sim2.Ledger.TrySpend(0, 4, "spend-rescue-one"));
        Assert.True(sim2.TryUseEmergencyRecovery(out _, out _));
        Assert.True(sim2.Ledger.TrySpend(0, 4, "spend-rescue-two"));
        Assert.True(sim2.TryUseEmergencyRecovery(out _, out _));
        Assert.True(sim2.Ledger.TrySpend(0, 4, "spend-rescue-three"));
        Assert.False(sim2.TryUseEmergencyRecovery(out _, out var reason));
        Assert.Contains("three", reason);
    }

    [Fact]
    public void SilkProduction_IsBoundedAndRequiresFood()
    {
        var sim = TestWorld.NewSim(t =>
        {
            t.Initial.Food = 1;
            t.Initial.Silk = 0;
            t.SilkProduction.SilkPerFedWorkerEverySeconds = 2;
        });
        sim.AdvanceSeconds(10);
        Assert.InRange(sim.Ledger.Silk, 1, 6);
    }
}
