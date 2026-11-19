using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>H3 contracts: planning a silk crossing over a generated river,
/// the spacing policy, exactly-once economy, the shared crossing model used
/// by worker navigation, explicit route invalidation and save persistence.</summary>
public class PlayerRiverBridgeTests
{
    private static readonly int Seed = new Tuning().Seed;

    private static WorldGenerator Generator() => new(Seed);

    /// <summary>A query point standing on the north bank of river 0 at a
    /// given x, one metre past the waterline.</summary>
    private static Vec3 BankQuery(double x)
    {
        var centerZ = RiverMap.CenterAt(0, x);
        var halfWidth = RiverMap.HalfWidthAt(0, x);
        return new Vec3(x, 0, centerZ - halfWidth - 1.0);
    }

    private static RiverBuildPlan PlanAt(GameSimulation sim, double x) =>
        sim.PlanRiverBridge(BankQuery(x));

    private static (PlayerRiverBridge Bridge, GameSimulation Sim) BuiltBridge(
        GameSimulation sim, double x)
    {
        var plan = PlanAt(sim, x);
        Assert.True(plan.Acceptable, $"plan at x={x} refused: {plan.Reason}");
        Assert.True(sim.TryBuildRiverBridge(plan, out var bridge, out var reason), reason);
        return (bridge!, sim);
    }

    [Fact]
    public void Plan_AcceptsMidRiverSite_WithGroundedGeometry()
    {
        var sim = new GameSimulation();
        AssertPlanAcceptsMidRiverSite(sim);
    }

    /// <summary>The runtime probe plays the fixed seed 20260214 world; its
    /// build site must be validated by the same deterministic contract.</summary>
    [Fact]
    public void Plan_AcceptsMidRiverSite_InTheProbeWorld()
    {
        var sim = new GameSimulation(new Tuning { Seed = 20260214 });
        AssertPlanAcceptsMidRiverSite(sim);
    }

    /// <summary>The probe's guidance segment stands at x=4 beside the
    /// generated crossing at x=0; its refusal is part of the contract.</summary>
    [Fact]
    public void Plan_RefusesBesideAGeneratedCrossing_InTheProbeWorld()
    {
        var sim = new GameSimulation(new Tuning { Seed = 20260214 });
        var plan = PlanAt(sim, 4);
        Assert.False(plan.Acceptable);
        Assert.NotNull(plan.Nearby);
        Assert.False(plan.Nearby!.IsPlayerBuilt);
    }

    /// <summary>The probe's second build sits at x=125, where the nearest
    /// existing crossing demands an unreasonable detour.</summary>
    [Fact]
    public void Plan_AcceptsASecondDistantSite_InTheProbeWorld()
    {
        var sim = new GameSimulation(new Tuning { Seed = 20260214 });
        var plan = PlanAt(sim, 125);
        Assert.True(plan.Acceptable, plan.Reason);
        Assert.Null(plan.Nearby);
    }

    private static void AssertPlanAcceptsMidRiverSite(GameSimulation sim)
    {
        var plan = PlanAt(sim, 75);
        Assert.True(plan.Acceptable, plan.Reason);
        Assert.Equal(sim.Tuning.RiverBridges.SilkCost, plan.SilkCost);
        Assert.Null(plan.Nearby);
        var site = plan.Site!;
        Assert.Equal(75, site.X, 2);
        var generator = Generator();
        var centerZ = RiverMap.CenterAt(0, site.X);
        // The deck actually spans the water and lands on both banks.
        Assert.True(site.NorthZ <= centerZ - site.HalfWidth + 0.5);
        Assert.True(site.SouthZ >= centerZ + site.HalfWidth - 0.5);
        Assert.True(site.NorthGroundY > site.WaterLevel + 0.25);
        Assert.True(site.SouthGroundY > site.WaterLevel + 0.25);
        Assert.All(site.DeckPoints(), point => Assert.True(point.IsFinite));
        // The sagging deck stays above the water along its whole length.
        Assert.All(site.DeckPoints(),
            point => Assert.True(point.Y > site.WaterLevel + 0.15));
    }

    [Fact]
    public void Plan_IsDeterministic_AtTheSameSpot()
    {
        var sim = new GameSimulation();
        var first = PlanAt(sim, 75);
        var second = PlanAt(sim, 75);
        Assert.Equal(first.Site, second.Site);
        Assert.Equal(first.SilkCost, second.SilkCost);
    }

    [Fact]
    public void Plan_Refuses_WhereThereIsNoWaterOrTooFarFromTheBank()
    {
        var sim = new GameSimulation();
        var inGarden = sim.PlanRiverBridge(new Vec3(0, 0, 0));
        Assert.False(inGarden.Acceptable);
        Assert.Equal("There is no water to cross here", inGarden.Reason);

        var centerZ = RiverMap.CenterAt(0, -75);
        var farAway = sim.PlanRiverBridge(new Vec3(-75, 0, centerZ - 60));
        Assert.False(farAway.Acceptable);
        Assert.Equal("Move closer to the water's edge to plan a crossing", farAway.Reason);
    }

    [Fact]
    public void Plan_Refuses_WithinShoreSpacingOfAGeneratedCrossing()
    {
        var sim = new GameSimulation();
        var plan = PlanAt(sim, 1);
        Assert.False(plan.Acceptable);
        Assert.Equal("A bridge already crosses the water here — use it instead", plan.Reason);
        Assert.NotNull(plan.Nearby);
        Assert.False(plan.Nearby!.IsPlayerBuilt);
        Assert.True(plan.Nearby.RouteDistance > plan.Nearby.DirectDistance);
        Assert.True(plan.Nearby.Ratio > 0);
    }

    [Fact]
    public void Plan_Accepts_WhenTheNearestCrossingNeedsAnUnreasonableDetour()
    {
        var sim = new GameSimulation();
        var plan = PlanAt(sim, 75);
        Assert.True(plan.Acceptable, plan.Reason);
        Assert.Null(plan.Nearby);
    }

    [Fact]
    public void Plan_DetourRatio_IsConfigurable()
    {
        // At 20 m from an existing crossing the real detour is more than the
        // default 1.5×, so the default accepts a new bridge there.
        var defaultSim = new GameSimulation();
        var defaultPlan = PlanAt(defaultSim, 20);
        Assert.True(defaultPlan.Acceptable, defaultPlan.Reason);

        // Raising the tolerated detour turns the same site into guidance.
        var tuning = new Tuning { RiverBridges = new RiverBridgeTuning { MaxDetourRatio = 20 } };
        var generous = new GameSimulation(tuning);
        var generousPlan = PlanAt(generous, 20);
        Assert.False(generousPlan.Acceptable);
        Assert.Equal("A nearby bridge already crosses the water — use it instead",
            generousPlan.Reason);
        Assert.NotNull(generousPlan.Nearby);
        Assert.True(generousPlan.Nearby!.Ratio <= 20);
        Assert.True(generousPlan.Nearby.Ratio > 1.5);
    }

    [Fact]
    public void Plan_Refuses_WithinShoreSpacingOfAPlayerBridge()
    {
        var sim = new GameSimulation();
        BuiltBridge(sim, 75);
        var plan = PlanAt(sim, 76);
        Assert.False(plan.Acceptable);
        Assert.Equal("A bridge already crosses the water here — use it instead", plan.Reason);
        Assert.NotNull(plan.Nearby);
        Assert.True(plan.Nearby!.IsPlayerBuilt);
    }

    [Fact]
    public void Build_ChargesOnce_RegistersBridge_AndRefusesDuplicates()
    {
        var sim = new GameSimulation();
        var silkBefore = sim.Ledger.Silk;
        var (bridge, _) = BuiltBridge(sim, 75);
        Assert.Equal("riverbridge_001", bridge.Id);
        Assert.Equal(silkBefore - sim.Tuning.RiverBridges.SilkCost, sim.Ledger.Silk);
        Assert.Single(sim.RiverBridges);
        Assert.Equal(1, sim.RiverBridgesBuilt);
        Assert.Contains(sim.Events, e => e.Type == "RiverBridgeBuilt" && e.SubjectId == bridge.Id);

        // The same site cannot be built twice: the fresh re-plan refuses it.
        var replay = sim.TryBuildRiverBridge(
            RiverBuildPlan.Accept(bridge, sim.Tuning.RiverBridges.SilkCost),
            out _, out var reason);
        Assert.False(replay);
        Assert.Equal("A bridge already crosses the water here — use it instead", reason);
        Assert.Equal(silkBefore - sim.Tuning.RiverBridges.SilkCost, sim.Ledger.Silk);
    }

    [Fact]
    public void Build_Refuses_WithoutEnoughSilk()
    {
        var sim = new GameSimulation();
        sim.Ledger.Restore(5, 0);
        var plan = PlanAt(sim, 75);
        Assert.True(plan.Acceptable, plan.Reason);
        Assert.False(sim.TryBuildRiverBridge(plan, out _, out var reason));
        Assert.Equal("Need more silk", reason);
        Assert.Empty(sim.RiverBridges);
    }

    [Fact]
    public void Navigator_RoutesAcrossThePlayerBridge_OnceRegistered()
    {
        var generator = Generator();
        var sim = new GameSimulation();
        var (bridge, _) = BuiltBridge(sim, 75);
        var north = bridge.NorthGround;
        var south = bridge.SouthGround;

        // Without the player bridge the same banks route around the river via
        // a generated crossing far along the shore. Only the two ramp-foot
        // endpoints themselves touch the future bridge strip.
        var before = new WorkerNavigator(Seed);
        var beforeRoute = before.FindRoute(north, south);
        Assert.NotNull(beforeRoute);
        Assert.True(beforeRoute!.UsesBridge);
        Assert.DoesNotContain(beforeRoute.Points.Skip(1).Take(beforeRoute.Points.Count - 2),
            point => bridge.Covers(point.X, point.Z));

        // With the bridge registered the direct silk crossing wins.
        var after = new WorkerNavigator(Seed);
        after.SetPlayerBridges(new[] { bridge });
        var afterRoute = after.FindRoute(north, south);
        Assert.NotNull(afterRoute);
        Assert.True(afterRoute!.UsesBridge);
        Assert.Contains(afterRoute.Points, point => bridge.Covers(point.X, point.Z));
        Assert.True(afterRoute.Distance < beforeRoute.Distance * 0.5);

        // The deck itself is a dry route: ramps, sag and all.
        var deck = new List<Vec3> { bridge.NorthGround };
        deck.AddRange(bridge.DeckPoints());
        deck.Add(bridge.SouthGround);
        Assert.True(after.IsRouteDry(deck));
        Assert.False(before.IsRouteDry(deck));
    }

    [Fact]
    public void Journey_ReroutesSafely_WhenTheBridgeIsDismantled()
    {
        var sim = new GameSimulation();
        Assert.True(sim.TryStartHatch(out var hatchReason), hatchReason);
        sim.AdvanceSeconds(4);
        var worker = sim.Workers[^1];
        var (bridge, _) = BuiltBridge(sim, 75);

        // A helper south of the river walks home across the new bridge.
        var centerZ = RiverMap.CenterAt(0, bridge.X);
        var start = new Vec3(bridge.X, 0, centerZ + 20);
        Assert.True(sim.BeginWorkerJourneyForTest(worker.Id, RegionIds.GardenClearing, start));
        Assert.Contains(worker.JourneyRoute, point => bridge.Covers(point.X, point.Z));
        sim.AdvanceSeconds(0.2);
        Assert.False(bridge.Covers(worker.WorldPosition.X, worker.WorldPosition.Z));

        var silkBefore = sim.Ledger.Silk;
        Assert.True(sim.TryDismantleRiverBridge(bridge.Id, out var refund, out var reason),
            reason);
        Assert.Equal(
            (int)Math.Floor(sim.Tuning.RiverBridges.SilkCost *
                            sim.Tuning.RiverBridges.DismantleRefundFraction), refund);
        Assert.Equal(silkBefore + refund, sim.Ledger.Silk);

        // The dependent route is invalidated: the helper either walks a fresh
        // dry route around the river or waits visibly, never the old deck.
        Assert.True(worker.Travelling || worker.RouteBlocked);
        Assert.DoesNotContain(worker.JourneyRoute, point => bridge.Covers(point.X, point.Z));
        var stillMoving = worker.Travelling && !worker.RouteBlocked;
        if (stillMoving)
            Assert.All(worker.JourneyRoute, point =>
                Assert.False(generatorIsWetUnder(point)));
        Assert.Empty(sim.RiverBridges);

        bool generatorIsWetUnder(Vec3 point)
        {
            var river = Generator().RiverAt(point.X, point.Z);
            return river.HasRiver && river.IsHazard && point.Y < river.WaterLevel + 0.2;
        }
    }

    [Fact]
    public void Dismantle_RefusesWhileAHelperIsOnTheDeck()
    {
        var sim = new GameSimulation();
        Assert.True(sim.TryStartHatch(out var hatchReason), hatchReason);
        sim.AdvanceSeconds(4);
        var worker = sim.Workers[^1];
        var (bridge, _) = BuiltBridge(sim, 75);

        // The helper is placed mid-deck and travelling across.
        var deckMid = bridge.DeckPoints()[bridge.DeckPoints().Count / 2];
        Assert.True(bridge.Covers(deckMid.X, deckMid.Z));
        Assert.True(sim.BeginWorkerJourneyForTest(worker.Id, RegionIds.GardenClearing, deckMid));
        Assert.False(sim.TryDismantleRiverBridge(bridge.Id, out _, out var reason));
        Assert.Equal("A helpful spider is crossing that bridge", reason);
        Assert.Single(sim.RiverBridges);
    }

    [Fact]
    public void Dismantle_RefusesWhileThePlayerStandsOnIt_AndOnlyOnce()
    {
        var sim = new GameSimulation();
        var (bridge, _) = BuiltBridge(sim, 75);
        var deckMid = bridge.DeckPoints()[bridge.DeckPoints().Count / 2];
        sim.SetPlayerWorldPosition(deckMid);
        Assert.False(sim.TryDismantleRiverBridge(bridge.Id, out _, out var reason));
        Assert.Equal("Step off the bridge before dismantling it", reason);

        sim.SetPlayerWorldPosition(new Vec3(bridge.X + 30, 0, bridge.CenterZ + 30));
        var silkBefore = sim.Ledger.Silk;
        Assert.True(sim.TryDismantleRiverBridge(bridge.Id, out var refund, out _));
        Assert.Equal(silkBefore + refund, sim.Ledger.Silk);
        Assert.False(sim.TryDismantleRiverBridge(bridge.Id, out _, out var secondReason));
        Assert.Equal("That bridge is already gone", secondReason);
        Assert.Equal(silkBefore + refund, sim.Ledger.Silk);
    }

    [Fact]
    public void RiverBridges_RoundTripThroughSave_RestoringGeometryAndSequence()
    {
        var sim = new GameSimulation();
        var (bridge, _) = BuiltBridge(sim, 75);
        var data = sim.ExportSave(new Vec3(0, 0, 0), 0, new SettingsData());
        Assert.Single(data.RiverBridges);

        var reloaded = GameSimulation.FromSave(data, sim.Tuning, sim.MissionsConfig);
        var restored = Assert.Single(reloaded.RiverBridges);
        Assert.Equal(bridge, restored);
        Assert.Equal(1, reloaded.RiverBridgesBuilt);
        Assert.Equal(sim.Ledger.Silk, reloaded.Ledger.Silk);

        // A second bridge after reload continues the saved id sequence.
        var plan = PlanAt(reloaded, 125);
        Assert.True(plan.Acceptable, plan.Reason);
        Assert.True(reloaded.TryBuildRiverBridge(plan, out var second, out var reason), reason);
        Assert.Equal("riverbridge_002", second!.Id);

        // The restored bridge is navigable in the reloaded world.
        var navigator = new WorkerNavigator(Seed);
        navigator.SetPlayerBridges(reloaded.RiverBridges.ToList());
        var route = navigator.FindRoute(restored.NorthGround, restored.SouthGround);
        Assert.NotNull(route);
        Assert.True(route!.UsesBridge);
    }

    [Fact]
    public void RiverBridges_LegacySaveWithoutTheField_LoadsEmpty()
    {
        var sim = new GameSimulation();
        var (bridge, _) = BuiltBridge(sim, 75);
        var data = sim.ExportSave(new Vec3(0, 0, 0), 0, new SettingsData());
        data.RiverBridges = new List<RiverBridgeSave>();
        data.Counters.RiverBridgesBuilt = 0;
        data.Counters.RiverBridgeSequence = 0;
        var reloaded = GameSimulation.FromSave(data, sim.Tuning, sim.MissionsConfig);
        Assert.Empty(reloaded.RiverBridges);
        Assert.Equal(0, reloaded.RiverBridgesBuilt);
    }

    [Fact]
    public void SaveValidator_RejectsBrokenRiverBridgeRecords()
    {
        var sim = new GameSimulation();
        var (bridge, _) = BuiltBridge(sim, 75);
        var data = sim.ExportSave(new Vec3(0, 0, 0), 0, new SettingsData());

        var cloned = CloneSave(data);
        for (var i = 0; i < 41; i++)
            cloned.RiverBridges.Add(new RiverBridgeSave { Id = $"x{i}" });
        Assert.False(SaveValidator.Validate(cloned, out var tooMany), tooMany);
        Assert.Equal("Too many river bridges in this save.", tooMany);

        cloned = CloneSave(data);
        cloned.RiverBridges[0].HalfSpan = 0;
        Assert.False(SaveValidator.Validate(cloned, out var span), span);
        Assert.Equal("A river bridge span is invalid.", span);

        cloned = CloneSave(data);
        cloned.RiverBridges[0].NorthGroundY = double.NaN;
        Assert.False(SaveValidator.Validate(cloned, out var geometry), geometry);
        Assert.Equal("A river bridge has invalid geometry.", geometry);

        cloned = CloneSave(data);
        cloned.RiverBridges.Add(cloned.RiverBridges[0]);
        Assert.False(SaveValidator.Validate(cloned, out var duplicate), duplicate);
        Assert.Equal("A river bridge ID is missing or duplicated.", duplicate);

        cloned = CloneSave(data);
        cloned.RiverBridges[0].Id = "   ";
        Assert.False(SaveValidator.Validate(cloned, out var blank), blank);

        // A valid record still passes.
        Assert.True(SaveValidator.Validate(CloneSave(data), out var valid), valid);
    }

    [Fact]
    public void SpacingPolicy_LeavesGeneratedCrossingsAsLandmarks()
    {
        // The compatibility decision: generated wooden crossings stay exactly
        // where the seed puts them; player silk bridges are additive and only
        // spacing-gated. The generator output is unchanged by building.
        var generator = Generator();
        var before = generator.Rivers.BridgeAt(0, 0, generator);
        var sim = new GameSimulation();
        BuiltBridge(sim, 75);
        var after = new WorldGenerator(Seed).Rivers.BridgeAt(0, 0, generator);
        Assert.Equal(before, after);
        Assert.NotNull(after);
    }

    [Fact]
    public void CrossingLifecycle_WithstandsLongDeterministicStress()
    {
        // Extended scenario: two crossings, three helpers with tasks, tens of
        // thousands of ticks, repeated save/load round trips, a broken and a
        // restored crossing, and journeys over each in turn.
        var sim = new GameSimulation();
        var (first, _) = BuiltBridge(sim, 75);
        var secondPlan = PlanAt(sim, 125);
        Assert.True(secondPlan.Acceptable, secondPlan.Reason);
        Assert.True(sim.TryBuildRiverBridge(secondPlan, out var second, out var reason), reason);

        for (var helper = 0; helper < 3; helper++)
        {
            sim.Ledger.Restore(50, 50);
            Assert.True(sim.TryStartHatch(out var hatchReason), hatchReason);
            sim.AdvanceSeconds(4);
        }
        Assert.Equal(3, sim.Workers.Count);
        // Metabolism would drain the pantry over thousands of ticks; the
        // economy assertion below isolates the crossing lifecycle itself.
        sim.Tuning.SilkProduction.SilkPerFedWorkerEverySeconds = 0;

        var navigator = new WorkerNavigator(Seed);
        var silkAtStart = sim.Ledger.Silk;
        var foodAtStart = sim.Ledger.Food;

        for (var round = 0; round < 3; round++)
        {
            // Helpers keep working across the crossings while the world runs.
            foreach (var worker in sim.Workers)
            {
                var start = new Vec3(first.X, 0, first.CenterZ + 25);
                if (worker.Travelling) continue;
                Assert.True(sim.BeginWorkerJourneyForTest(worker.Id, RegionIds.GardenClearing, start));
            }
            sim.AdvanceSeconds(2400);
            Assert.Equal(2, sim.RiverBridges.Count);
            Assert.All(sim.RiverBridges, bridge => Assert.True(
                Math.Abs(bridge.X - first.X) < 0.01 || Math.Abs(bridge.X - second!.X) < 0.01));

            // Repeated save/load keeps identity, sequence and geometry stable.
            var data = sim.ExportSave(new Vec3(0, 0, 0), 0, new SettingsData());
            var reloaded = GameSimulation.FromSave(data, sim.Tuning, sim.MissionsConfig);
            Assert.Equal(2, reloaded.RiverBridges.Count);
            Assert.Equal(first, reloaded.RiverBridges.First(bridge => bridge.Id == first.Id));
            Assert.Equal(second!, reloaded.RiverBridges.First(bridge => bridge.Id == second!.Id));

            // A broken crossing is explicit: routes survive or wait, and the
            // deck can be rebuilt at the same site afterwards.
            Assert.True(sim.TryDismantleRiverBridge(second!.Id, out var refund, out var dismantleReason),
                dismantleReason);
            Assert.Single(sim.RiverBridges);
            var replan = PlanAt(sim, 125);
            Assert.True(replan.Acceptable, replan.Reason);
            Assert.True(sim.TryBuildRiverBridge(replan, out var rebuilt, out var rebuildReason),
                rebuildReason);
            Assert.Equal(second!.RiverIndex, rebuilt!.RiverIndex);
            Assert.Equal(second!.X, rebuilt!.X, 2);
            Assert.Equal(2, sim.RiverBridges.Count);
            sim = reloaded;
        }

        // The ledger is bounded and honest across the whole run: only hatches,
        // builds and refunds moved it.
        Assert.True(sim.Ledger.Food >= foodAtStart, $"food={sim.Ledger.Food}");
        Assert.InRange(sim.Ledger.Silk, 0, 60);
        Assert.Equal(2, sim.RiverBridges.Count);

        // The final registry is still navigable end to end.
        navigator.SetPlayerBridges(sim.RiverBridges.ToList());
        foreach (var bridge in sim.RiverBridges)
        {
            var route = navigator.FindRoute(bridge.NorthGround, bridge.SouthGround);
            Assert.NotNull(route);
            Assert.True(route!.UsesBridge);
        }
    }

    private static SaveGameData CloneSave(SaveGameData data)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        return System.Text.Json.JsonSerializer.Deserialize<SaveGameData>(json)!;
    }
}
