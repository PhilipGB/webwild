namespace Webwild.Core;

/// <summary>Wild-food worker tasks and persistent map markers. Kept separate
/// from the region/colony core so the task lifecycle stays reviewable and the
/// presentation only translates clicks and renders the authoritative state.</summary>
public sealed partial class GameSimulation
{
    private const double WorkerWalkSpeed = 1.9;
    private const int MaxMapMarkers = 200;
    private const int MaxWorldPathPoints = 2_000;

    public IReadOnlyList<WorldMarker> MapMarkers => _markers;

    /// <summary>Mark one discovered wild source on the map. The id is stable,
    /// so marking twice is refused rather than duplicating the marker.</summary>
    public bool MarkFoodSource(FoodSourceDefinition source, out string reason)
    {
        reason = "";
        if (source is null || string.IsNullOrWhiteSpace(source.Id))
        {
            reason = "That food is not here";
            return false;
        }
        var id = "food:" + source.Id;
        if (_markers.Any(marker => marker.Id == id))
        {
            reason = $"{Cap(source.Label)} is already marked";
            return false;
        }
        if (_markers.Count >= MaxMapMarkers)
        {
            reason = "The map already has too many markers";
            return false;
        }
        _markers.Add(new WorldMarker(id, WorldMarkerKind.Food, source.Position,
            source.Id, source.Label));
        Emit(new GameplayEvent(NextKey("map_marker"), "MapMarkerAdded", TickCount, id,
            Detail: source.Id, Region: ActiveRegionId));
        return true;
    }

    public bool AddMapMarker(string kind, Vec3 position, string label, out string reason)
    {
        reason = "";
        if (!WorldMarkerKind.IsKnown(kind))
        {
            reason = "That marker kind is unknown";
            return false;
        }
        if (!position.IsFinite)
        {
            reason = "That marker position is invalid";
            return false;
        }
        if (_markers.Count >= MaxMapMarkers)
        {
            reason = "The map already has too many markers";
            return false;
        }
        var id = $"note:{Guid.NewGuid():N}";
        _markers.Add(new WorldMarker(id, kind, position, null,
            string.IsNullOrWhiteSpace(label) ? "Marker" : label[..Math.Min(60, label.Length)]));
        Emit(new GameplayEvent(NextKey("map_marker"), "MapMarkerAdded", TickCount, id,
            Detail: kind, Region: ActiveRegionId));
        return true;
    }

    public bool RemoveMapMarker(string markerId)
    {
        var index = _markers.FindIndex(marker => marker.Id == markerId);
        if (index < 0) return false;
        _markers.RemoveAt(index);
        Emit(new GameplayEvent(NextKey("map_marker_removed"), "MapMarkerRemoved", TickCount,
            markerId, Region: ActiveRegionId));
        return true;
    }

    /// <summary>Assign one available named helper to collect a wild source and
    /// carry it to the helper's home nursery. Nothing is credited until the
    /// helper physically arrives and delivers it.</summary>
    public bool TryAssignFoodGather(string workerId, string foodId, out string reason)
    {
        reason = "";
        var worker = GetWorker(workerId);
        if (worker is null)
        {
            reason = "That helper is not in the colony";
            return false;
        }
        if (worker.Summoned || worker.Travelling)
        {
            reason = $"{worker.Name} is travelling; release or wait for it first";
            return false;
        }
        var source = _navigator.Generator.FoodSourceById(foodId);
        if (source is null)
        {
            reason = "That food is not part of this world";
            return false;
        }
        ReleaseWorkerTask(worker, "Foraging", keepPosition: true);
        worker.TargetFoodId = source.Id;
        worker.FoodSourcePosition = source.Position;
        worker.FoodPhase = WorkerFoodPhase.ToSource;
        worker.CarriedFood = 0;
        worker.FoodDeliveryId = "";
        worker.WorldPath.Clear();
        worker.OutboundPath.Clear();
        worker.WorldPathIndex = 0;
        worker.WorldPathUsesBridge = false;
        worker.TaskRetryCooldown = 0;
        worker.Activity = WorkerActivity.Gathering;
        Emit(new GameplayEvent(NextKey($"worker_forage_{worker.Id}"), "WorkerForageAssigned",
            TickCount, worker.Id, Detail: source.Kind, Region: worker.Region));
        if (!IsFoodAvailable(source.Id))
        {
            worker.FoodPhase = WorkerFoodPhase.WaitingForRegrowth;
            worker.Activity = WorkerActivity.Waiting;
            reason = $"{Cap(source.Label)} will regrow in " +
                     $"{Math.Max(1, (int)Math.Ceiling(FoodSecondsRemaining(source.Id)))}s";
            return true;
        }
        return StartFoodLegToSource(worker, source, out reason);
    }

    private bool StartFoodLegToSource(WorkerState worker, FoodSourceDefinition source,
        out string reason)
    {
        reason = "";
        var route = _navigator.FindRoute(worker.WorldPosition, source.Position);
        if (route is null || route.Points.Count < 2)
        {
            worker.FoodPhase = WorkerFoodPhase.Blocked;
            worker.Activity = WorkerActivity.Waiting;
            worker.RouteBlocked = true;
            worker.TaskRetryCooldown = 4;
            reason = $"{worker.Name} has no safe route to the food yet";
            Emit(new GameplayEvent(NextKey($"worker_food_blocked_{worker.Id}"),
                "WorkerFoodBlocked", TickCount, worker.Id, Detail: source.Id, Region: worker.Region));
            return true;
        }
        worker.FoodSourcePosition = source.Position;
        worker.FoodPhase = WorkerFoodPhase.ToSource;
        worker.Activity = WorkerActivity.Gathering;
        worker.RouteBlocked = false;
        worker.TaskRetryCooldown = 0;
        worker.WorldPathUsesBridge = route.UsesBridge;
        SetWorldPath(worker, route.Points);
        return true;
    }

    private void ReplanFoodTask(WorkerState worker)
    {
        if (worker.TargetFoodId is null) return;
        var source = _navigator.Generator.FoodSourceById(worker.TargetFoodId);
        if (source is null)
        {
            ClearFoodTask(worker);
            worker.Activity = WorkerActivity.Resting;
            return;
        }
        if (!IsFoodAvailable(source.Id))
        {
            worker.FoodPhase = WorkerFoodPhase.WaitingForRegrowth;
            worker.Activity = WorkerActivity.Waiting;
            return;
        }
        if (worker.CarriedFood > 0)
        {
            StartFoodReturn(worker);
            return;
        }
        StartFoodLegToSource(worker, source, out _);
    }

    private void TickFoodTask(WorkerState worker, double dt)
    {
        switch (worker.FoodPhase)
        {
            case WorkerFoodPhase.WaitingForRegrowth:
                worker.Activity = WorkerActivity.Waiting;
                if (worker.TargetFoodId is not null && IsFoodAvailable(worker.TargetFoodId))
                    ReplanFoodTask(worker);
                return;
            case WorkerFoodPhase.Blocked:
                worker.Activity = WorkerActivity.Waiting;
                worker.TaskRetryCooldown -= dt;
                if (worker.TaskRetryCooldown <= 0)
                {
                    worker.TaskRetryCooldown = 4;
                    ReplanFoodTask(worker);
                }
                return;
            case WorkerFoodPhase.ToSource:
                if (worker.WorldPath.Count == 0)
                {
                    ReplanFoodTask(worker);
                    return;
                }
                if (AdvanceWorldPath(worker, dt)) CollectFoodAtSource(worker);
                return;
            case WorkerFoodPhase.ToHome:
                if (worker.WorldPath.Count == 0)
                {
                    StartFoodReturn(worker);
                    return;
                }
                if (AdvanceWorldPath(worker, dt)) DeliverFoodAtHome(worker);
                return;
            default:
                ClearFoodTask(worker);
                return;
        }
    }

    private void SetWorldPath(WorkerState worker, IReadOnlyList<Vec3> points)
    {
        worker.WorldPath = points.ToList();
        worker.WorldPathIndex = worker.WorldPath.Count > 1 ? 1 : worker.WorldPath.Count;
    }

    private bool AdvanceWorldPath(WorkerState worker, double dt)
    {
        if (worker.WorldPath.Count == 0) return true;
        if (worker.WorldPathIndex <= 0) worker.WorldPathIndex = 1;
        var remaining = WorkerWalkSpeed * dt;
        while (remaining > 0 && worker.WorldPathIndex < worker.WorldPath.Count)
        {
            var next = worker.WorldPath[worker.WorldPathIndex];
            var current = worker.WorldPosition;
            var delta = next - current;
            var distance = delta.Length;
            if (distance <= remaining + 0.01)
            {
                worker.Position = LandmarkCatalog.ToLocal(worker.Region, next);
                worker.WorldPathIndex++;
                remaining -= distance;
                continue;
            }
            var step = delta / distance * remaining;
            worker.Position = LandmarkCatalog.ToLocal(worker.Region, current + step);
            remaining = 0;
        }
        return worker.WorldPathIndex >= worker.WorldPath.Count;
    }

    private void CollectFoodAtSource(WorkerState worker)
    {
        var source = _navigator.Generator.FoodSourceById(worker.TargetFoodId);
        if (source is null)
        {
            ClearFoodTask(worker);
            worker.Activity = WorkerActivity.Resting;
            return;
        }
        if (!TryDepleteFood(source, out var amount))
        {
            worker.FoodPhase = WorkerFoodPhase.WaitingForRegrowth;
            worker.Activity = WorkerActivity.Waiting;
            return;
        }
        worker.CarriedFood = amount;
        worker.FoodDeliveryId = $"worker_food:{worker.Id}:{source.Id}:{TickCount}";
        worker.OutboundPath = worker.WorldPath.ToList();
        Emit(new GameplayEvent(NextKey($"worker_food_collected_{worker.Id}"),
            "WorkerFoodCollected", TickCount, worker.Id, Detail: source.Kind,
            Amount: amount, Region: worker.Region));
        StartFoodReturn(worker);
    }

    private bool TryDepleteFood(FoodSourceDefinition source, out int amount)
    {
        amount = 0;
        if (!IsFoodAvailable(source.Id)) return false;
        _foodStates[source.Id] = new FoodSourceState(source.Id, source.Chunk, source.Kind,
            source.Position, TickCount + FoodRegenerationTicks);
        amount = source.Amount;
        return true;
    }

    private void StartFoodReturn(WorkerState worker)
    {
        var home = HomeWorldOf(worker);
        worker.CarriedFood = Math.Max(0, worker.CarriedFood);
        var preferred = worker.OutboundPath.Count > 1
            ? worker.OutboundPath.AsEnumerable().Reverse().ToList()
            : new List<Vec3>();
        if (preferred.Count > 1 &&
            Vec3.Distance(preferred[^1], home) < 12 &&
            _navigator.IsRouteDry(preferred))
        {
            SetWorldPath(worker, preferred);
        }
        else
        {
            var route = _navigator.FindRoute(worker.WorldPosition, home);
            if (route is null || route.Points.Count < 2)
            {
                worker.FoodPhase = WorkerFoodPhase.Blocked;
                worker.Activity = WorkerActivity.Waiting;
                worker.RouteBlocked = true;
                worker.TaskRetryCooldown = 4;
                Emit(new GameplayEvent(NextKey($"worker_food_blocked_{worker.Id}"),
                    "WorkerFoodBlocked", TickCount, worker.Id, Detail: "home", Region: worker.Region));
                return;
            }
            SetWorldPath(worker, route.Points);
            worker.WorldPathUsesBridge = route.UsesBridge;
        }
        worker.FoodPhase = WorkerFoodPhase.ToHome;
        worker.Activity = WorkerActivity.Hauling;
        worker.RouteBlocked = false;
    }

    private Vec3 HomeWorldOf(WorkerState worker)
    {
        var home = RegionCatalog.Get(worker.HomeRegion) ?? RegionCatalog.Garden;
        return LandmarkCatalog.ToWorld(home.Id, home.NurseryPosition).Absolute;
    }

    private void DeliverFoodAtHome(WorkerState worker)
    {
        if (worker.CarriedFood <= 0 || string.IsNullOrWhiteSpace(worker.FoodDeliveryId))
        {
            ClearFoodTask(worker);
            worker.Activity = WorkerActivity.Resting;
            worker.PatrolCooldown = 1;
            return;
        }
        var key = "grant:" + worker.FoodDeliveryId;
        if (!Ledger.HasProcessed(key))
        {
            if (!Ledger.TryGrantFood(worker.CarriedFood, worker.FoodDeliveryId))
            {
                worker.FoodPhase = WorkerFoodPhase.Blocked;
                worker.TaskRetryCooldown = 2;
                return;
            }
            Emit(new GameplayEvent(NextKey($"worker_food_delivered_{worker.Id}"),
                "WorkerFoodDelivered", TickCount, worker.Id, Detail: "food",
                Amount: worker.CarriedFood, Region: worker.Region));
        }
        worker.CarriedFood = 0;
        worker.FoodDeliveryId = "";
        ClearFoodTask(worker);
        worker.Activity = WorkerActivity.Resting;
        worker.PatrolCooldown = 1;
        worker.RouteBlocked = false;
    }

    private void ClearFoodTask(WorkerState worker)
    {
        worker.TargetFoodId = null;
        worker.FoodPhase = WorkerFoodPhase.None;
        worker.FoodSourcePosition = Vec3.Zero;
        worker.WorldPath.Clear();
        worker.OutboundPath.Clear();
        worker.WorldPathIndex = 0;
        worker.WorldPathUsesBridge = false;
        worker.TaskRetryCooldown = 0;
    }

    /// <summary>Drop a food task safely, keeping carried food and its delivery
    /// identity so a later release home can still deliver exactly once.</summary>
    public void ReleaseFoodTask(WorkerState worker, string reason)
    {
        if (worker.CarriedFood > 0)
        {
            worker.FoodPhase = WorkerFoodPhase.ToHome;
            worker.Activity = WorkerActivity.Hauling;
            StartFoodReturn(worker);
            return;
        }
        ClearFoodTask(worker);
        ReleaseWorkerTask(worker, reason, keepPosition: true);
    }
}
