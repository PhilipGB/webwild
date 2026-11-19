namespace Webwild.Core;

/// <summary>Player-built silk crossings over generated rivers: planning,
/// construction, dismantling and explicit route invalidation. Kept beside the
/// foraging partial so colony construction stays reviewable and the region
/// core in <see cref="GameSimulation.cs"/> stays focused.</summary>
public sealed partial class GameSimulation
{
    private const int MaxRiverBridges = 40;

    public IReadOnlyCollection<PlayerRiverBridge> RiverBridges => _riverBridges.Values;
    public int RiverBridgesBuilt { get; private set; }

    /// <summary>Plan a crossing for a world point without changing any state.
    /// The plan is derived live from the river field, the navigator and the
    /// spacing policy, so it always reflects the world the player sees.</summary>
    public RiverBuildPlan PlanRiverBridge(Vec3 worldPoint) =>
        RiverBridgePlanner.Plan(
            _navigator.Generator, _navigator, Tuning.RiverBridges,
            _riverBridges.Values, worldPoint);

    /// <summary>
    /// Commit an accepted plan. The site is re-planned from the world itself
    /// before anything is charged, so a stale plan, a second player action or
    /// a replayed call can never raise a duplicate deck.
    /// </summary>
    public bool TryBuildRiverBridge(RiverBuildPlan plan, out PlayerRiverBridge? bridge,
        out string reason)
    {
        bridge = null;
        reason = "";
        if (plan is null || plan.Site is null)
        {
            reason = "There is no crossing to build here";
            return false;
        }
        var site = plan.Site;
        // Re-plan from the bank the site describes; the fresh plan is the
        // authority, not the copy the caller carried around.
        var query = new Vec3(site.X, 0, site.CenterZ - site.HalfWidth - 1.0);
        var fresh = PlanRiverBridge(query);
        if (!fresh.Acceptable || fresh.Site is null)
        {
            reason = fresh.Reason is { Length: > 0 } ? fresh.Reason
                : "A bridge already crosses the water here";
            return false;
        }
        if (Math.Abs(fresh.Site.X - site.X) > 0.01 ||
            Math.Abs(fresh.Site.CenterZ - site.CenterZ) > 0.01 ||
            Math.Abs(fresh.Site.HalfSpan - site.HalfSpan) > 0.01)
        {
            reason = "That crossing has moved with the water — plan it again";
            return false;
        }
        if (_riverBridges.Count >= MaxRiverBridges)
        {
            reason = "The colony cannot maintain more river bridges";
            return false;
        }
        var cost = Tuning.RiverBridges.SilkCost;
        if (!Ledger.TrySpend(0, cost, NextKey("build_riverbridge")))
        {
            reason = "Need more silk";
            return false;
        }

        bridge = new PlayerRiverBridge(
            $"riverbridge_{++_riverBridgeSequence:000}",
            site.RiverIndex, site.X, site.CenterZ, site.HalfWidth, site.WaterLevel,
            site.HalfSpan, site.NorthGroundY, site.SouthGroundY, TickCount);
        _riverBridges[bridge.Id] = bridge;
        _navigator.SetPlayerBridges(_riverBridges.Values.ToList());
        RiverBridgesBuilt++;
        Emit(new GameplayEvent(NextKey($"river_bridge_{bridge.Id}"), "RiverBridgeBuilt",
            TickCount, bridge.Id, "bridge", Detail: bridge.Id, Amount: cost,
            Region: ActiveRegionId));

        // Helpers waiting on a blocked route get an immediate retry so the new
        // crossing is felt in play rather than after the full cooldown.
        foreach (var worker in _workers.Where(w => w.RouteBlocked))
            worker.TaskRetryCooldown = Math.Min(worker.TaskRetryCooldown, 0.5);
        return true;
    }

    /// <summary>Dismantle one river bridge, refund part of the silk and
    /// explicitly invalidate every dependent worker route.</summary>
    public bool TryDismantleRiverBridge(string bridgeId, out int silkRefund, out string reason)
    {
        silkRefund = 0;
        reason = "";
        if (!_riverBridges.TryGetValue(bridgeId, out var bridge))
        {
            reason = "That bridge is already gone";
            return false;
        }

        // Nobody may remove the deck under a crossing spider, least of all
        // the player themselves.
        if (_playerWorldPosition is { } player && bridge.Covers(player.X, player.Z))
        {
            reason = "Step off the bridge before dismantling it";
            return false;
        }
        if (_workers.Any(w => IsWorkerOnBridge(w, bridge)))
        {
            reason = "A helpful spider is crossing that bridge";
            return false;
        }

        var cost = Tuning.RiverBridges.SilkCost;
        silkRefund = (int)Math.Floor(cost * Math.Clamp(Tuning.RiverBridges.DismantleRefundFraction, 0, 1));
        _riverBridges.Remove(bridgeId);
        _navigator.SetPlayerBridges(_riverBridges.Values.ToList());
        if (silkRefund > 0) Ledger.TryGrantSilk(silkRefund, NextKey($"dismantle_{bridgeId}"));

        InvalidateRoutesUsingBridge(bridge);

        Emit(new GameplayEvent(NextKey($"river_bridge_removed_{bridgeId}"), "RiverBridgeDismantled",
            TickCount, bridgeId, "bridge", Amount: silkRefund, Detail: reason,
            Region: ActiveRegionId));
        return true;
    }

    /// <summary>Nearest player-built bridge to a world point, for the
    /// dismantle interaction. Distance is measured to the walkable strip
    /// (ramps included), so standing anywhere on a long deck counts.</summary>
    public PlayerRiverBridge? NearestRiverBridge(Vec3 worldPoint, double radius)
    {
        PlayerRiverBridge? nearest = null;
        var best = radius;
        foreach (var bridge in _riverBridges.Values)
        {
            var distance = StripDistance(bridge, worldPoint);
            if (distance > best) continue;
            best = distance;
            nearest = bridge;
        }
        return nearest;
    }

    /// <summary>Horizontal distance from a point to a bridge's ramp-to-ramp
    /// strip; the deck runs along Z at the bridge's X.</summary>
    public static double StripDistance(PlayerRiverBridge bridge, Vec3 point)
    {
        var z0 = Math.Min(bridge.NorthZ - PlayerRiverBridge.RampOutset,
            bridge.SouthZ + PlayerRiverBridge.RampOutset);
        var z1 = Math.Max(bridge.NorthZ - PlayerRiverBridge.RampOutset,
            bridge.SouthZ + PlayerRiverBridge.RampOutset);
        var z = Math.Clamp(point.Z, z0, z1);
        var dx = point.X - bridge.X;
        var dz = point.Z - z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Explicit route invalidation when a crossing disappears. Workers whose
    /// current route crosses the removed deck re-plan immediately from where
    /// they stand; when no dry route exists any more they wait visibly and
    /// retry, exactly like any other obstruction.
    /// </summary>
    private void InvalidateRoutesUsingBridge(PlayerRiverBridge bridge)
    {
        foreach (var worker in _workers)
        {
            if (worker.Travelling)
            {
                var crosses = worker.JourneyRoute.Any(p => bridge.Covers(p.X, p.Z)) ||
                              bridge.Covers(worker.JourneyPosition.X, worker.JourneyPosition.Z);
                if (!crosses) continue;
                BeginJourney(worker, worker.JourneyTargetRegion, preferReverse: false,
                    destinationOverride: worker.JourneyDestination, summoned: worker.Summoned);
                continue;
            }
            var foraging = worker.FoodPhase is WorkerFoodPhase.ToSource or WorkerFoodPhase.ToHome;
            if (!foraging) continue;
            var pathCrosses = (worker.WorldPath.Count > 0 && worker.WorldPath.Any(p =>
                                   bridge.Covers(p.X, p.Z))) ||
                               bridge.Covers(worker.WorldPosition.X, worker.WorldPosition.Z);
            if (!pathCrosses) continue;
            ReplanFoodTask(worker);
        }
    }

    /// <summary>A helper is mid-crossing when it is walking a world route and
    /// currently stands on this bridge's deck strip.</summary>
    private bool IsWorkerOnBridge(WorkerState worker, PlayerRiverBridge bridge)
    {
        var active = worker.Travelling ||
                     worker.FoodPhase is WorkerFoodPhase.ToSource or WorkerFoodPhase.ToHome;
        return active && bridge.Covers(worker.WorldPosition.X, worker.WorldPosition.Z);
    }

    private void SyncRiverBridgesToNavigator() =>
        _navigator.SetPlayerBridges(_riverBridges.Values.ToList());
}
