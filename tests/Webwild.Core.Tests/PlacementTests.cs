using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

public class PlacementTests
{
    [Fact]
    public void TutorialPair_IsValidAndChargeMatchesPreview()
    {
        var sim = TestWorld.NewSim();
        var before = sim.Ledger.Silk;
        var preview = sim.Preview(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture);
        Assert.True(preview.IsValid);
        Assert.Equal(6, preview.SilkCost);

        Assert.True(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out var web, out _));
        Assert.NotNull(web);
        Assert.Equal(before - 6, sim.Ledger.Silk);
        Assert.Equal(6, before - sim.Ledger.Silk);
        Assert.Equal(100, web!.Durability);
        Assert.True(web.IsTutorial);
    }

    [Fact]
    public void SameAnchor_IsRejectedWithoutCharge()
    {
        var sim = TestWorld.NewSim();
        var before = sim.Ledger.Silk;
        var preview = sim.Preview(WorldLayout.TwigA, WorldLayout.TwigA, WebType.Capture);
        Assert.False(preview.IsValid);
        Assert.Equal(PlacementCode.SameAnchor, preview.Code);
        Assert.Contains("different", preview.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigA, WebType.Capture, out _, out _));
        Assert.Equal(before, sim.Ledger.Silk);
    }

    [Fact]
    public void DuplicatePair_IsRejectedWithoutSecondCharge()
    {
        var sim = TestWorld.BuildTutorialWeb(TestWorld.NewSim());
        var silkAfterFirst = sim.Ledger.Silk;
        var preview = sim.Preview(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture);
        Assert.False(preview.IsValid);
        Assert.Equal(PlacementCode.AlreadyConnected, preview.Code);
        Assert.False(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out _, out _));
        Assert.Equal(silkAfterFirst, sim.Ledger.Silk);
    }

    [Fact]
    public void TooFar_IsRejectedWithoutCharge()
    {
        var anchors = WorldLayout.CreateAnchors();
        anchors.Add(new AnchorDefinition
        {
            Id = "far_test", Position = new Vec3(20, 1.4, 12), Exposure = 0.5, Label = "Far"
        });
        var sim = new GameSimulation(TestWorld.RichTuning(), MissionsConfig.Defaults(), anchors);
        var before = sim.Ledger.Silk;
        var preview = sim.Preview(WorldLayout.TwigA, "far_test", WebType.Capture);
        Assert.False(preview.IsValid);
        Assert.Equal(PlacementCode.TooFar, preview.Code);
        Assert.False(sim.TryBuildWeb(WorldLayout.TwigA, "far_test", WebType.Capture, out _, out _));
        Assert.Equal(before, sim.Ledger.Silk);
    }

    [Fact]
    public void LowSilk_ExplainsTrueCostAndDoesNotCharge()
    {
        var sim = TestWorld.NewSim(t => t.Initial.Silk = 2);
        var preview = sim.Preview(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture);
        Assert.False(preview.IsValid);
        Assert.Equal(PlacementCode.NotEnoughSilk, preview.Code);
        Assert.Contains("6 silk", preview.Reason);
        Assert.False(sim.TryBuildWeb(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture, out _, out _));
        Assert.Equal(2, sim.Ledger.Silk);
    }

    [Fact]
    public void AnchorCapacity_IsEnforced()
    {
        var anchors = new List<AnchorDefinition>
        {
            new() { Id = "a", Position = new Vec3(0, 1.3, 0), Capacity = 1, Exposure = 0.5 },
            new() { Id = "b", Position = new Vec3(2, 1.3, 0), Capacity = 2, Exposure = 0.5 },
            new() { Id = "c", Position = new Vec3(0, 1.3, 2), Capacity = 2, Exposure = 0.5 }
        };
        var sim = new GameSimulation(TestWorld.RichTuning(), MissionsConfig.Defaults(), anchors);
        Assert.True(sim.TryBuildWeb("a", "b", WebType.Capture, out _, out _));
        var preview = sim.Preview("a", "c", WebType.Capture);
        Assert.False(preview.IsValid);
        Assert.Equal(PlacementCode.NoCapacity, preview.Code);
    }

    [Fact]
    public void InactiveAnchor_IsRejected()
    {
        var anchors = WorldLayout.CreateAnchors();
        anchors.Add(new AnchorDefinition
        {
            Id = "sleeping", Position = new Vec3(-8, 1.2, 3), Active = false, Exposure = 0.3
        });
        var sim = new GameSimulation(TestWorld.RichTuning(), MissionsConfig.Defaults(), anchors);
        var preview = sim.Preview(WorldLayout.TwigA, "sleeping", WebType.Capture);
        Assert.False(preview.IsValid);
        Assert.Equal(PlacementCode.InactiveAnchor, preview.Code);
    }

    [Fact]
    public void SteepBridge_IsRejected()
    {
        var anchors = new List<AnchorDefinition>
        {
            new() { Id = "low", Position = new Vec3(0, 0.5, 0), Exposure = 0.2 },
            new() { Id = "high", Position = new Vec3(2, 3.5, 0), Exposure = 0.2 }
        };
        var sim = new GameSimulation(TestWorld.RichTuning(), MissionsConfig.Defaults(), anchors);
        var preview = sim.Preview("low", "high", WebType.Bridge);
        Assert.False(preview.IsValid);
        Assert.Equal(PlacementCode.TooSteep, preview.Code);
        Assert.False(sim.TryBuildWeb("low", "high", WebType.Bridge, out _, out _));
    }

    [Fact]
    public void BridgePair_IsValidAndChangesWorkerGraph()
    {
        var sim = TestWorld.NewSim();
        var routeBefore = sim.Graph.FindRoute(WorldLayout.NurseryNode, WorldLayout.TwigA);
        Assert.Null(routeBefore);

        Assert.True(sim.TryBuildWeb(WorldLayout.GapWest, WorldLayout.GapEast, WebType.Bridge, out var bridge, out var result), result.Reason);
        var routeAfter = sim.Graph.FindRoute(WorldLayout.NurseryNode, WorldLayout.TwigA);
        Assert.NotNull(routeAfter);
        Assert.True(routeAfter!.UsesBridge);

        Assert.True(sim.TryDismantleWeb(bridge!.Id, out var refund, out _));
        Assert.Equal(3, refund);
        Assert.Null(sim.Graph.FindRoute(WorldLayout.NurseryNode, WorldLayout.TwigA));
    }

    [Fact]
    public void TutorialWebDismantle_RefundsInFull_AndNormalWebPartially()
    {
        var sim = TestWorld.BuildTutorialWeb(TestWorld.NewSim());
        var first = sim.Webs.Single();
        var silkBefore = sim.Ledger.Silk;
        Assert.True(sim.TryDismantleWeb(first.Id, out var refund, out _));
        Assert.Equal(6, refund);
        Assert.Equal(silkBefore + 6, sim.Ledger.Silk);

        Assert.True(sim.TryBuildWeb(WorldLayout.FlowerA, WorldLayout.FlowerB, WebType.Capture, out var second, out _));
        Assert.False(second!.IsTutorial);
        silkBefore = sim.Ledger.Silk;
        Assert.True(sim.TryDismantleWeb(second.Id, out refund, out _));
        Assert.Equal(3, refund);
        Assert.Equal(silkBefore + 3, sim.Ledger.Silk);
    }

    [Fact]
    public void PreviewAndCancel_NeverChargeSilk()
    {
        var sim = TestWorld.NewSim();
        var before = sim.Ledger.Silk;
        for (var i = 0; i < 20; i++)
        {
            sim.Preview(WorldLayout.TwigA, WorldLayout.TwigB, WebType.Capture);
            sim.Preview(WorldLayout.WindA, WorldLayout.SpawnNode, WebType.Capture);
        }
        Assert.Equal(before, sim.Ledger.Silk);
    }
}
