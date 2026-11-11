namespace Webwild.Core;

public enum WeatherPhase
{
    Calm,
    Warning,
    Aftermath
}

public sealed class PendingHatch
{
    public double Remaining { get; set; }
    public string Id { get; init; } = "";
}

/// <summary>
/// The authoritative engine-free game simulation. Godot adapters only translate
/// input and render this state; tests drive exactly the same code path.
/// </summary>
public sealed class GameSimulation
{
    private readonly Dictionary<string, AnchorDefinition> _anchors;
    private readonly Dictionary<string, WebState> _webs = new(StringComparer.Ordinal);
    private readonly List<InsectState> _insects = new();
    private readonly List<WorkerState> _workers = new();
    private readonly List<GameplayEvent> _events = new();
    private readonly HashSet<string> _issuedEventKeys = new(StringComparer.Ordinal);
    private int _webSequence;
    private int _workerSequence;
    private int _transactionSequence;
    private double _silkAccumulator;
    private double _weatherRemaining;
    private double _aftermathRemaining;
    private bool _windStarted;
    private bool _windCompleted;
    private bool _masteryAwarded;
    private bool _victory;
    private readonly Random _rng;

    public Tuning Tuning { get; }
    public MissionsConfig MissionsConfig { get; }
    public ResourceLedger Ledger { get; }
    public MissionTracker Missions { get; }
    public WorldGraph Graph { get; }
    public WeatherPhase WeatherPhase { get; private set; } = WeatherPhase.Calm;
    public double WeatherRemaining => _weatherRemaining;
    public PendingHatch? PendingHatch { get; private set; }
    public long TickCount { get; private set; }
    public double ElapsedSeconds { get; private set; }
    public int CaptureWebsBuilt { get; private set; }
    public int BridgeWebsBuilt { get; private set; }
    public int InsectsCollected { get; private set; }
    public int RepairsCompleted { get; private set; }
    public int EmergencyRecoveryUses { get; private set; }
    public bool WindStarted => _windStarted;
    public bool WindCompleted => _windCompleted;
    public bool MasteryAwarded => _masteryAwarded;
    public bool Victory => _victory;
    public bool IsVictory => _victory;
    public IReadOnlyCollection<WebState> Webs => _webs.Values;
    public IReadOnlyList<InsectState> Insects => _insects;
    public IReadOnlyList<WorkerState> Workers => _workers;
    public IReadOnlyDictionary<string, AnchorDefinition> Anchors => _anchors;
    public IReadOnlyList<GameplayEvent> Events => _events;
    public int TotalSpiders => 1 + _workers.Count + (PendingHatch is not null ? 1 : 0);
    public int MaxSpiders => Tuning.MaxSpidersIncludingPlayer;
    public int EmergencyUsesRemaining => Math.Max(0, Tuning.Recovery.EmergencyStarterWebRepairUses - EmergencyRecoveryUses);

    public GameSimulation(
        Tuning? tuning = null,
        MissionsConfig? missions = null,
        IEnumerable<AnchorDefinition>? anchors = null)
    {
        Tuning = tuning ?? new Tuning();
        Tuning.Normalise();
        MissionsConfig = missions ?? MissionsConfig.Defaults();
        Missions = new MissionTracker(MissionsConfig);
        Ledger = new ResourceLedger(Tuning.Initial.Food, Tuning.Initial.Silk);
        _anchors = (anchors ?? WorldLayout.CreateAnchors()).ToDictionary(a => a.Id, StringComparer.Ordinal);
        Graph = new WorldGraph(WorldLayout.CreateNodes(), WorldLayout.CreateGroundEdges());
        _rng = new Random(Tuning.Seed);

        foreach (var route in WorldLayout.CreateInsectRoutes())
        {
            var insect = new InsectState
            {
                Definition = route,
                WaypointIndex = 0,
                Position = route.Waypoints[0],
                PreviousPosition = route.Waypoints[0]
            };
            AdvanceInsectTo(insect, route.StartProgress * TotalRouteLength(route));
            _insects.Add(insect);
        }

        Emit(new GameplayEvent(NextKey("game_start"), "GameStarted", TickCount));
    }

    private string NextKey(string prefix) => $"{prefix}:{TickCount}:{++_transactionSequence}";
    private string NextWebId(WebType type) => $"{(type == WebType.Capture ? "capture" : "bridge")}_{++_webSequence:000}";

    public AnchorDefinition? GetAnchor(string id) => _anchors.GetValueOrDefault(id);
    public WebState? GetWeb(string id) => _webs.GetValueOrDefault(id);
    public InsectState? GetInsect(string id) => _insects.FirstOrDefault(i => i.Id == id);
    public WorkerState? GetWorker(string id) => _workers.FirstOrDefault(w => w.Id == id);
    public WebState? GetWebAtAnchor(string anchorId) =>
        _webs.Values.FirstOrDefault(w => w.AnchorA == anchorId || w.AnchorB == anchorId);

    public IReadOnlyList<GameplayEvent> DrainEvents()
    {
        var copy = _events.ToList();
        _events.Clear();
        return copy;
    }

    public PlacementResult Preview(string anchorA, string anchorB, WebType type)
    {
        var rules = new PlacementRules(_anchors, _webs, Tuning);
        return rules.Validate(anchorA, anchorB, type, Ledger.Silk);
    }

    public bool TryBuildWeb(string anchorA, string anchorB, WebType type, out WebState? web, out PlacementResult result)
    {
        web = null;
        result = Preview(anchorA, anchorB, type);
        if (!result.IsValid) return false;

        var transaction = NextKey($"build_{type}");
        if (!Ledger.TrySpend(0, result.SilkCost, transaction))
        {
            result = PlacementResult.Fail(PlacementCode.NotEnoughSilk, "Need more silk", result.SilkCost);
            return false;
        }

        var a = _anchors[anchorA];
        var b = _anchors[anchorB];
        var sheltered = a.Sheltered && b.Sheltered;
        var maxDurability = type == WebType.Capture ? Tuning.Webs.Capture.Durability : Tuning.Webs.Bridge.Durability;
        var isTutorial = type == WebType.Capture && CaptureWebsBuilt == 0;
        web = new WebState
        {
            Id = NextWebId(type),
            Type = type,
            AnchorA = anchorA,
            AnchorB = anchorB,
            Durability = maxDurability,
            MaxDurability = maxDurability,
            Exposure = PlacementRules.ComputeExposure(a, b),
            Sheltered = sheltered,
            IsTutorial = isTutorial,
            ConstructedAtTick = TickCount
        };
        _webs[web.Id] = web;
        if (type == WebType.Capture) CaptureWebsBuilt++;
        else
        {
            BridgeWebsBuilt++;
            Graph.AddBridge(web.Id, anchorA, anchorB);
        }

        Emit(new GameplayEvent(NextKey($"web_built_{web.Id}"), "WebBuilt", TickCount,
            web.Id, type == WebType.Capture ? "capture" : "bridge", _windCompleted, Detail: web.Id));

        // A newly built shelter after the storm is a valid answer to M03.
        if (_windCompleted && type == WebType.Capture && sheltered)
        {
            Emit(new GameplayEvent(NextKey($"sheltered_{web.Id}"), "WebRestoredOrSheltered", TickCount,
                web.Id, "capture", true, Detail: "sheltered"));
        }

        CheckMastery();
        return true;
    }

    public bool TryDismantleWeb(string webId, out int silkRefund, out string reason)
    {
        silkRefund = 0;
        reason = "";
        if (!_webs.TryGetValue(webId, out var web))
        {
            reason = "That web is already gone";
            return false;
        }
        if (web.Type == WebType.Bridge &&
            _workers.Any(w => w.IsMoving && w.PathEdges.Any(e => e.WebId == webId)))
        {
            reason = "A helpful spider is crossing that bridge";
            return false;
        }

        var cost = web.Type == WebType.Capture ? Tuning.Webs.Capture.SilkCost : Tuning.Webs.Bridge.SilkCost;
        var fraction = web.IsTutorial ? Tuning.Webs.TutorialRefundFraction : Tuning.Webs.DismantleRefundFraction;
        silkRefund = (int)Math.Floor(cost * Math.Clamp(fraction, 0, 1));
        _webs.Remove(webId);
        if (web.Type == WebType.Bridge) Graph.RemoveBridge(webId);

        // Release prey and workers targeting this web.
        foreach (var insect in _insects.Where(i => i.TrappedWebId == webId && i.Phase == InsectPhase.Trapped))
        {
            insect.Phase = InsectPhase.Flying;
            insect.TrappedWebId = null;
            insect.WaypointIndex = Math.Clamp(insect.WaypointIndex, 0, insect.Definition.Waypoints.Count - 1);
        }
        foreach (var worker in _workers.Where(w => w.TargetWebId == webId || w.PathEdges.Any(e => e.WebId == webId)))
            ReleaseWorkerTask(worker, "Web removed");

        if (silkRefund > 0) Ledger.TryGrantSilk(silkRefund, NextKey($"dismantle_{webId}"));
        Emit(new GameplayEvent(NextKey($"web_removed_{webId}"), "WebDismantled", TickCount,
            webId, web.Type == WebType.Capture ? "capture" : "bridge", Detail: reason));
        return true;
    }

    public bool TryDismantleNearest(Vec3 position, double radius, out int silkRefund, out string reason)
    {
        silkRefund = 0;
        reason = "Move closer to a web";
        var web = _webs.Values
            .Select(w => (Web: w, Distance: Vec3.Distance(WebCenter(w), position)))
            .Where(x => x.Distance <= radius)
            .OrderBy(x => x.Distance)
            .Select(x => x.Web)
            .FirstOrDefault();
        if (web is null) return false;
        return TryDismantleWeb(web.Id, out silkRefund, out reason);
    }

    public bool TryRepairWeb(string webId, bool byWorker, out string reason)
    {
        reason = "";
        if (!_webs.TryGetValue(webId, out var web))
        {
            reason = "That web is gone";
            return false;
        }
        if (web.Durability >= web.MaxDurability)
        {
            reason = "That web is already strong";
            return false;
        }
        if (!Ledger.TrySpend(0, Tuning.Webs.RepairSilkCost, NextKey($"repair_{webId}")))
        {
            reason = $"Need {Tuning.Webs.RepairSilkCost} silk to repair";
            return false;
        }
        web.Durability = web.MaxDurability;
        RepairsCompleted++;
        Emit(new GameplayEvent(NextKey($"web_repaired_{webId}"), "WebRepaired", TickCount,
            webId, web.Type == WebType.Capture ? "capture" : "bridge", _windCompleted,
            Detail: byWorker ? "worker" : "player"));
        if (_windCompleted)
        {
            Emit(new GameplayEvent(NextKey($"restored_{webId}"), "WebRestoredOrSheltered", TickCount,
                webId, web.Type == WebType.Capture ? "capture" : "bridge", true, Detail: "repaired"));
        }
        return true;
    }

    public bool TryRepairNearest(Vec3 position, double radius, out string reason)
    {
        reason = "Move closer to a damaged web";
        var candidate = _webs.Values
            .Where(w => w.Durability < w.MaxDurability)
            .Select(w => (Web: w, Distance: Vec3.Distance(WebCenter(w), position)))
            .Where(x => x.Distance <= radius)
            .OrderBy(x => x.Distance)
            .FirstOrDefault();
        if (candidate.Web is null) return false;
        return TryRepairWeb(candidate.Web.Id, false, out reason);
    }

    public bool TryCollectInsect(string insectId, string collector, out int food, out string reason)
    {
        food = 0;
        reason = "";
        var insect = GetInsect(insectId);
        if (insect is null)
        {
            reason = "That insect is gone";
            return false;
        }
        if (insect.Phase != InsectPhase.Trapped)
        {
            reason = "That insect is not caught";
            return false;
        }
        var key = $"food:{insect.Id}:{insect.Generation}";
        if (!Ledger.TryGrantFood(insect.Definition.FoodYield, key))
        {
            reason = "Already collected";
            return false;
        }
        food = insect.Definition.FoodYield;
        insect.Phase = InsectPhase.Retrieved;
        insect.RespawnAtTick = TickCount + (long)Math.Ceiling(insect.Definition.RespawnSeconds / Tuning.FixedSimulationTickSeconds);
        InsectsCollected++;
        var webId = insect.TrappedWebId;
        insect.TrappedWebId = null;

        Emit(new GameplayEvent(NextKey($"food_collected_{insect.Id}_{insect.Generation}"), "FoodCollected",
            TickCount, insect.Id, null, _windCompleted, food, collector));
        if (_windCompleted)
        {
            Emit(new GameplayEvent(NextKey($"food_after_wind_{insect.Id}_{insect.Generation}"), "FoodCollectedAfterWind",
                TickCount, insect.Id, null, true, food, collector));
        }
        foreach (var w in _workers.Where(w => w.TargetInsectId == insect.Id)) ReleaseWorkerTask(w, "Prey collected");
        CheckMastery();
        return true;
    }

    public bool TryCollectNearest(Vec3 position, double radius, out InsectState? insect, out int food, out string reason)
    {
        insect = _insects
            .Where(i => i.CanBeCollected)
            .Select(i => (Insect: i, Distance: Vec3.Distance(i.Position, position)))
            .Where(x => x.Distance <= radius)
            .OrderBy(x => x.Distance)
            .Select(x => x.Insect)
            .FirstOrDefault();
        food = 0;
        if (insect is null)
        {
            reason = "No caught insect nearby";
            return false;
        }
        return TryCollectInsect(insect.Id, "player", out food, out reason);
    }

    public (bool CanHatch, string Reason) CanHatch()
    {
        if (PendingHatch is not null) return (false, "A new helper is already hatching");
        if (TotalSpiders >= MaxSpiders) return (false, "The colony is full (5 spiders)");
        if (Ledger.Food < Tuning.Nursery.HatchFoodCost || Ledger.Silk < Tuning.Nursery.HatchSilkCost)
            return (false, $"Need {Tuning.Nursery.HatchFoodCost} food and {Tuning.Nursery.HatchSilkCost} silk");
        return (true, "Ready to hatch");
    }

    public bool TryStartHatch(out string reason)
    {
        var check = CanHatch();
        reason = check.Reason;
        if (!check.CanHatch) return false;
        var costs = $"{Tuning.Nursery.HatchFoodCost}/{Tuning.Nursery.HatchSilkCost}";
        if (!Ledger.TrySpend(Tuning.Nursery.HatchFoodCost, Tuning.Nursery.HatchSilkCost, NextKey($"hatch_start_{costs}")))
        {
            reason = "Could not spend hatch resources";
            return false;
        }
        PendingHatch = new PendingHatch
        {
            Remaining = Math.Max(0.1, Tuning.Nursery.HatchTimeSeconds),
            Id = $"hatch_{++_workerSequence}"
        };
        Emit(new GameplayEvent(NextKey("hatch_started"), "HatchStarted", TickCount,
            PendingHatch.Id, Amount: (int)Math.Ceiling(PendingHatch.Remaining)));
        return true;
    }

    private void CompleteHatch()
    {
        if (PendingHatch is null) return;
        var index = _workers.Count + 1;
        var colours = new[] { "orange", "violet", "green", "amber" };
        var names = new[] { "Pip", "Bramble", "Juniper", "Clover" };
        var worker = new WorkerState
        {
            Id = $"worker_{index}",
            Name = names[Math.Clamp(index - 1, 0, names.Length - 1)],
            Colour = colours[Math.Clamp(index - 1, 0, colours.Length - 1)],
            Position = new Vec3(12.0, 0.1, 4.0),
            CurrentNode = WorldLayout.NurseryNode,
            Activity = WorkerActivity.Resting,
            PatrolCooldown = 2.0
        };
        _workers.Add(worker);
        PendingHatch = null;
        Emit(new GameplayEvent(NextKey($"spider_hatched_{worker.Id}"), "SpiderHatched", TickCount,
            worker.Id, Detail: worker.Name));
    }

    public bool CanUseEmergencyRecovery =>
        Ledger.Silk < 2 && EmergencyUsesRemaining > 0;

    /// <summary>Limited, clearly labelled rescue for a child who has spent all silk.</summary>
    public bool TryUseEmergencyRecovery(out int silkGranted, out string reason)
    {
        silkGranted = 0;
        reason = "";
        if (Ledger.Silk >= 2)
        {
            reason = "You still have silk";
            return false;
        }
        if (EmergencyUsesRemaining <= 0)
        {
            reason = "The nursery rescue has already been used three times";
            return false;
        }
        silkGranted = 4;
        if (!Ledger.TryGrantSilk(silkGranted, NextKey("emergency_silk")))
        {
            reason = "Could not add rescue silk";
            return false;
        }
        EmergencyRecoveryUses++;
        Emit(new GameplayEvent(NextKey("recovery_used"), "RecoveryUsed", TickCount,
            Amount: silkGranted, Detail: "Nursery rescue silk"));
        return true;
    }

    public void Tick(double deltaSeconds)
    {
        var dt = Math.Clamp(deltaSeconds, 0, 0.5);
        if (dt <= 0) return;
        TickCount++;
        ElapsedSeconds += dt;

        ProduceSilk(dt);
        TickHatch(dt);
        TickInsects(dt);
        TickWorkers(dt);
        TickWeather(dt);
    }

    public void AdvanceSeconds(double seconds)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(seconds / Tuning.FixedSimulationTickSeconds));
        for (var i = 0; i < steps; i++) Tick(Tuning.FixedSimulationTickSeconds);
    }

    private void ProduceSilk(double dt)
    {
        if (Tuning.SilkProduction.SilkPerFedWorkerEverySeconds <= 0) return;
        if (Ledger.Food <= 0) return;
        _silkAccumulator += dt;
        while (_silkAccumulator >= Tuning.SilkProduction.SilkPerFedWorkerEverySeconds)
        {
            _silkAccumulator -= Tuning.SilkProduction.SilkPerFedWorkerEverySeconds;
            if (Ledger.Silk < 99 && Ledger.TryGrantSilk(1, NextKey("silk_production")))
                Emit(new GameplayEvent(NextKey("silk_produced"), "SilkProduced", TickCount, Amount: 1));
        }
    }

    private void TickHatch(double dt)
    {
        if (PendingHatch is null) return;
        PendingHatch.Remaining -= dt;
        if (PendingHatch.Remaining <= 0) CompleteHatch();
    }

    private void TickInsects(double dt)
    {
        foreach (var insect in _insects)
        {
            if (insect.Phase == InsectPhase.Retrieved)
            {
                if (TickCount >= insect.RespawnAtTick)
                {
                    insect.Generation++;
                    insect.Phase = InsectPhase.Flying;
                    insect.TrappedWebId = null;
                    insect.WaypointIndex = 0;
                    insect.Position = insect.Definition.Waypoints[0];
                    insect.PreviousPosition = insect.Position;
                    Emit(new GameplayEvent(NextKey($"respawn_{insect.Id}"), "InsectRespawned", TickCount, insect.Id));
                }
                continue;
            }
            if (insect.Phase == InsectPhase.Trapped) continue;

            var before = insect.Position;
            AdvanceInsect(insect, dt);
            var after = insect.Position;
            insect.PreviousPosition = before;
            if (TryCaptureAlongSegment(insect, before, after)) continue;
        }
    }

    private bool TryCaptureAlongSegment(InsectState insect, Vec3 from, Vec3 to)
    {
        var bestT = double.MaxValue;
        WebState? bestWeb = null;
        Vec3 bestHit = default;
        foreach (var web in _webs.Values.Where(w => w.Type == WebType.Capture && w.CapturesEnabled))
        {
            var a = GetAnchor(web.AnchorA);
            var b = GetAnchor(web.AnchorB);
            if (a is null || b is null) continue;
            var area = CaptureGeometry.BuildArea(a.Position, b.Position, Tuning.Webs.Capture.CaptureRadius);
            if (CaptureGeometry.TryIntersect(area, from, to, out var hit, out var t) && t < bestT)
            {
                bestT = t;
                bestWeb = web;
                bestHit = hit;
            }
        }
        if (bestWeb is null) return false;
        insect.Phase = InsectPhase.Trapped;
        insect.TrappedWebId = bestWeb.Id;
        insect.Position = bestHit;
        insect.PreviousPosition = bestHit;
        insect.CapturedAtTick = TickCount;
        Emit(new GameplayEvent(NextKey($"trapped_{insect.Id}_{bestWeb.Id}"), "InsectTrapped", TickCount,
            insect.Id, "capture", Detail: bestWeb.Id));
        return true;
    }

    private void AdvanceInsect(InsectState insect, double dt)
    {
        var remaining = insect.Definition.Speed * dt;
        var points = insect.Definition.Waypoints;
        var guard = 0;
        while (remaining > 1e-6 && guard++ < 8)
        {
            var nextIndex = (insect.WaypointIndex + 1) % points.Count;
            var target = points[nextIndex];
            var toTarget = target - insect.Position;
            var dist = toTarget.Length;
            if (dist < 1e-5)
            {
                insect.WaypointIndex = nextIndex;
                continue;
            }
            if (remaining >= dist)
            {
                insect.Position = target;
                insect.WaypointIndex = nextIndex;
                remaining -= dist;
            }
            else
            {
                insect.Position += toTarget.Normalized() * remaining;
                remaining = 0;
            }
        }
    }

    private void AdvanceInsectTo(InsectState insect, double distance)
    {
        var remaining = distance;
        var points = insect.Definition.Waypoints;
        var guard = 0;
        while (remaining > 1e-6 && guard++ < 100)
        {
            var nextIndex = (insect.WaypointIndex + 1) % points.Count;
            var target = points[nextIndex];
            var dist = Vec3.Distance(insect.Position, target);
            if (remaining >= dist)
            {
                insect.Position = target;
                insect.WaypointIndex = nextIndex;
                remaining -= dist;
            }
            else
            {
                insect.Position += (target - insect.Position).Normalized() * remaining;
                remaining = 0;
            }
        }
    }

    private static double TotalRouteLength(InsectRouteDefinition route)
    {
        var total = 0.0;
        for (var i = 0; i < route.Waypoints.Count; i++)
            total += Vec3.Distance(route.Waypoints[i], route.Waypoints[(i + 1) % route.Waypoints.Count]);
        return total;
    }

    private void TickWorkers(double dt)
    {
        foreach (var worker in _workers)
        {
            if (worker.IsMoving) MoveWorker(worker, dt);
            else
            {
                worker.PatrolCooldown -= dt;
                if (worker.PatrolCooldown > 0) continue;
                AssignWorkerTask(worker);
            }
        }
    }

    private void AssignWorkerTask(WorkerState worker)
    {
        // Priority 1: trapped prey that nobody else is collecting.
        foreach (var insect in _insects.Where(i => i.Phase == InsectPhase.Trapped))
        {
            if (_workers.Any(w => w != worker && w.TargetInsectId == insect.Id)) continue;
            var web = insect.TrappedWebId is null ? null : GetWeb(insect.TrappedWebId);
            if (web is null) continue;
            var route = BestRouteToWeb(worker.CurrentNode, web);
            if (route is null) continue;
            BeginRoute(worker, route, WorkerActivity.Collecting, insect.Id, web.Id);
            return;
        }

        // Priority 2: accessible damage, if we have repair silk.
        if (Ledger.Silk >= Tuning.Webs.RepairSilkCost)
        {
            foreach (var web in _webs.Values.Where(w => w.Durability < w.MaxDurability))
            {
                if (_workers.Any(w => w != worker && w.TargetWebId == web.Id && w.Activity == WorkerActivity.Repairing)) continue;
                var route = BestRouteToWeb(worker.CurrentNode, web);
                if (route is null) continue;
                BeginRoute(worker, route, WorkerActivity.Repairing, null, web.Id);
                return;
            }
        }

        // Priority 3: a short patrol, deliberately crossing a bridge when one exists.
        var destinations = Graph.Nodes.Keys.Where(id => id != worker.CurrentNode).ToList();
        var candidates = destinations
            .Select(id => (Id: id, Route: Graph.FindRoute(worker.CurrentNode, id)))
            .Where(x => x.Route is not null)
            .Select(x => (x.Id, Route: x.Route!))
            .ToList();
        var bridgeCandidates = candidates.Where(c => c.Route.UsesBridge).ToList();
        var pool = bridgeCandidates.Count > 0 && _rng.NextDouble() < 0.8 ? bridgeCandidates : candidates;
        if (pool.Count == 0)
        {
            worker.Activity = WorkerActivity.Waiting;
            worker.PatrolCooldown = 4;
            return;
        }
        var pick = pool[_rng.Next(pool.Count)];
        BeginRoute(worker, pick.Route, WorkerActivity.Patrolling, null, null);
    }

    private RoutePlan? BestRouteToWeb(string from, WebState web)
    {
        var a = Graph.FindRoute(from, web.AnchorA);
        var b = Graph.FindRoute(from, web.AnchorB);
        if (a is null) return b;
        if (b is null) return a;
        return a.Cost <= b.Cost ? a : b;
    }

    private void BeginRoute(WorkerState worker, RoutePlan route, WorkerActivity activity, string? insectId, string? webId)
    {
        if (route.Nodes.Count == 0) return;
        worker.Activity = activity;
        worker.TargetInsectId = insectId;
        worker.TargetWebId = webId;
        worker.TargetNode = route.Target;
        worker.PathNodes = route.Nodes.ToList();
        worker.PathEdges = route.Edges.ToList();
        worker.PathIndex = 0;
        worker.ArrivalDelay = 0;
        if (route.Nodes[0] != worker.CurrentNode)
        {
            // The worker has drifted; snap only to the nearest authored node at route start.
            worker.CurrentNode = route.Nodes[0];
            if (Graph.TryGetNode(worker.CurrentNode, out var node)) worker.Position = node.Position;
        }
    }

    private void MoveWorker(WorkerState worker, double dt)
    {
        if (worker.PathNodes.Count == 0 || worker.PathIndex >= worker.PathNodes.Count - 1)
        {
            FinishWorkerRoute(worker);
            return;
        }
        var nextNodeId = worker.PathNodes[worker.PathIndex + 1];
        if (!Graph.TryGetNode(nextNodeId, out var nextNode))
        {
            ReleaseWorkerTask(worker, "Route missing");
            return;
        }
        var step = 1.9 * dt;
        var to = nextNode.Position - worker.Position;
        var distance = to.Length;
        if (distance <= step + 0.02)
        {
            var previousNode = worker.PathNodes[worker.PathIndex];
            worker.Position = nextNode.Position;
            worker.PathIndex++;
            worker.CurrentNode = nextNodeId;
            EmitBridgeUseIfNeeded(worker, worker.PathIndex - 1, previousNode, nextNodeId);
            if (worker.PathIndex >= worker.PathNodes.Count - 1) FinishWorkerRoute(worker);
        }
        else
        {
            worker.Position += to.Normalized() * step;
        }
    }

    private void EmitBridgeUseIfNeeded(WorkerState worker, int edgeIndex, string fromNode, string toNode)
    {
        if (edgeIndex < 0 || edgeIndex >= worker.PathEdges.Count) return;
        var edge = worker.PathEdges[edgeIndex];
        if (!edge.UsesBridge) return;
        Emit(new GameplayEvent(NextKey($"worker_bridge_{worker.Id}"), "WorkerUsedBridge", TickCount,
            worker.Id, "bridge", _windCompleted, Detail: edge.WebId));
    }

    private void FinishWorkerRoute(WorkerState worker)
    {
        switch (worker.Activity)
        {
            case WorkerActivity.Collecting:
                if (worker.TargetInsectId is not null &&
                    TryCollectInsect(worker.TargetInsectId, worker.Id, out _, out _))
                {
                    worker.CollectCount++;
                }
                break;
            case WorkerActivity.Repairing:
                if (worker.TargetWebId is not null &&
                    TryRepairWeb(worker.TargetWebId, true, out _))
                {
                    worker.RepairCount++;
                }
                break;
        }
        ReleaseWorkerTask(worker, "Task complete", keepPosition: true);
        worker.PatrolCooldown = 2.5 + _rng.NextDouble() * 2;
    }

    public void ReleaseWorkerTask(WorkerState worker, string reason, bool keepPosition = false)
    {
        worker.Activity = WorkerActivity.Resting;
        worker.TargetInsectId = null;
        worker.TargetWebId = null;
        worker.PathNodes = new List<string>();
        worker.PathEdges = new List<GroundEdge>();
        worker.PathIndex = 0;
        worker.TargetNode = worker.CurrentNode;
        if (!keepPosition)
        {
            worker.PatrolCooldown = Math.Max(worker.PatrolCooldown, 1.0);
        }
    }

    public void StartWindWarning()
    {
        if (_windStarted) return;
        _windStarted = true;
        WeatherPhase = WeatherPhase.Warning;
        _weatherRemaining = Tuning.Weather.WarningSeconds;
        Emit(new GameplayEvent(NextKey("wind_warning"), "WindWarning", TickCount,
            Amount: (int)Math.Ceiling(_weatherRemaining)));
    }

    private void TickWeather(double dt)
    {
        if (WeatherPhase == WeatherPhase.Warning)
        {
            _weatherRemaining -= dt;
            if (_weatherRemaining <= 0) ApplyWindDamage();
        }
        else if (WeatherPhase == WeatherPhase.Aftermath)
        {
            _aftermathRemaining -= dt;
            if (_aftermathRemaining <= 0)
            {
                _aftermathRemaining = 0;
                WeatherPhase = WeatherPhase.Calm;
            }
        }
    }

    private void ApplyWindDamage()
    {
        WeatherPhase = WeatherPhase.Aftermath;
        _aftermathRemaining = 8;
        _weatherRemaining = 0;
        _windCompleted = true;

        var damaged = new List<string>();
        foreach (var web in _webs.Values.Where(w => w.Type == WebType.Capture))
        {
            var amount = web.Exposure >= 0.55
                ? Tuning.Weather.ExposedWebDamage
                : Tuning.Weather.ShelteredWebDamage;
            if (web.Sheltered) amount = Tuning.Weather.ShelteredWebDamage;
            var newDurability = Math.Max(0, web.Durability - amount);
            if (newDurability != web.Durability) damaged.Add(web.Id);
            web.Durability = newDurability;
        }

        // Never leave a child with no working capture route at all.
        if (_webs.Values.Any(w => w.Type == WebType.Capture) &&
            !_webs.Values.Any(w => w.Type == WebType.Capture && w.CapturesEnabled))
        {
            var rescue = _webs.Values
                .Where(w => w.Type == WebType.Capture)
                .OrderByDescending(w => w.IsTutorial)
                .First();
            rescue.Durability = Math.Min(rescue.MaxDurability, 20);
            if (!damaged.Contains(rescue.Id)) damaged.Add(rescue.Id);
        }

        Emit(new GameplayEvent(NextKey("wind_completed"), "WindEventCompleted", TickCount,
            Detail: string.Join(",", damaged)));

        foreach (var worker in _workers.Where(w => w.IsMoving))
        {
            // A gust pauses workers safely; no spider is ever lost or killed.
            ReleaseWorkerTask(worker, "Gust pause");
            worker.PatrolCooldown = 4;
        }
    }

    public void ForceWindNowForTest()
    {
        if (!_windStarted) StartWindWarning();
        _weatherRemaining = 0;
        TickWeather(0.01);
    }

    private void CheckMastery()
    {
        if (_masteryAwarded) return;
        if (InsectsCollected >= 3 && CaptureWebsBuilt <= 2)
        {
            _masteryAwarded = true;
            Emit(new GameplayEvent(NextKey("mastery"), "MasteryEarned", TickCount,
                MissionsConfig.OptionalMastery?.Id ?? "O01", Detail: "Clever Weaver"));
        }
    }

    private void Emit(GameplayEvent evt)
    {
        if (!_issuedEventKeys.Add(evt.Key)) return;
        _events.Add(evt);
        if (_events.Count > 300) _events.RemoveRange(0, 100);

        var result = Missions.Record(evt);
        if (!result.MissionCompleted || result.CompletedMissionId is null) return;

        Emit(new GameplayEvent(NextKey($"mission_done_{result.CompletedMissionId}"), "MissionCompleted",
            TickCount, result.CompletedMissionId, Detail: result.CompletedMissionId));

        if (result.CompletedMissionId == "M02" && !_windStarted) StartWindWarning();

        if (result.AllComplete && !_victory)
        {
            _victory = true;
            Missions.MarkVictoryAwarded();
            Emit(new GameplayEvent(NextKey("victory"), "Victory", TickCount, Detail: "Garden complete"));
        }
    }

    public SaveGameData ExportSave(Vec3 playerPosition, double playerRotationY, SettingsData settings)
    {
        return new SaveGameData
        {
            SchemaVersion = SaveGameData.CurrentSchemaVersion,
            SaveId = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            Seed = Tuning.Seed,
            Tick = TickCount,
            ElapsedSeconds = ElapsedSeconds,
            Player = new PlayerSave
            {
                X = playerPosition.X, Y = playerPosition.Y, Z = playerPosition.Z, RotationY = playerRotationY
            },
            Food = Ledger.Food,
            Silk = Ledger.Silk,
            Webs = _webs.Values.Select(w => new WebSave
            {
                Id = w.Id,
                Type = w.Type == WebType.Capture ? "capture" : "bridge",
                AnchorA = w.AnchorA,
                AnchorB = w.AnchorB,
                Durability = w.Durability,
                MaxDurability = w.MaxDurability,
                Exposure = w.Exposure,
                Sheltered = w.Sheltered,
                IsTutorial = w.IsTutorial,
                ConstructedAtTick = w.ConstructedAtTick
            }).ToList(),
            Workers = _workers.Select(w => new WorkerSave
            {
                Id = w.Id, Name = w.Name, Colour = w.Colour,
                X = w.Position.X, Y = w.Position.Y, Z = w.Position.Z,
                CurrentNode = w.CurrentNode,
                Activity = w.Activity.ToString(),
                TargetInsectId = w.TargetInsectId,
                TargetWebId = w.TargetWebId,
                TargetNode = w.TargetNode,
                PathNodes = w.PathNodes.ToList(),
                PathEdgeWebIds = w.PathEdges.Select(e => e.WebId ?? "").ToList(),
                PathIndex = w.PathIndex,
                ArrivalDelay = w.ArrivalDelay,
                PatrolCooldown = w.PatrolCooldown,
                CollectCount = w.CollectCount,
                RepairCount = w.RepairCount
            }).ToList(),
            Insects = _insects.Select(i => new InsectSave
            {
                Id = i.Id, Generation = i.Generation, Phase = i.Phase.ToString(),
                X = i.Position.X, Y = i.Position.Y, Z = i.Position.Z,
                WaypointIndex = i.WaypointIndex,
                TrappedWebId = i.TrappedWebId,
                CapturedAtTick = i.CapturedAtTick,
                RespawnAtTick = i.RespawnAtTick
            }).ToList(),
            Missions = new MissionSave
            {
                ActiveMissionIndex = Missions.ActiveMissionIndex,
                StepIndex = Missions.StepIndex,
                Progress = Missions.StepProgressList.ToList(),
                Completed = Missions.CompletedMissions.ToList(),
                SeenEvents = Missions.SeenEventKeys.ToList(),
                VictoryAwarded = Missions.VictoryAwarded
            },
            Weather = new WeatherSave
            {
                Phase = WeatherPhase.ToString(),
                Remaining = _weatherRemaining,
                WindStarted = _windStarted,
                WindCompleted = _windCompleted,
                AftermathRemaining = _aftermathRemaining
            },
            Counters = new SimCountersSave
            {
                WebSequence = _webSequence,
                WorkerSequence = _workerSequence,
                TransactionSequence = _transactionSequence,
                CaptureWebsBuilt = CaptureWebsBuilt,
                BridgeWebsBuilt = BridgeWebsBuilt,
                InsectsCollected = InsectsCollected,
                RepairsCompleted = RepairsCompleted,
                EmergencyUses = EmergencyRecoveryUses,
                MasteryAwarded = _masteryAwarded,
                Victory = _victory,
                SilkAccumulator = _silkAccumulator,
                PendingHatchRemaining = PendingHatch?.Remaining ?? 0,
                PendingHatchId = PendingHatch?.Id,
                LedgerKeys = Ledger.ProcessedKeys.ToList(),
                IssuedEventKeys = _issuedEventKeys.ToList()
            },
            Settings = settings
        };
    }

    public static GameSimulation FromSave(SaveGameData data, Tuning tuning, MissionsConfig missions)
    {
        if (!SaveValidator.Validate(data, out var error))
            throw new InvalidDataException(error);

        var sim = new GameSimulation(tuning, missions);
        sim._events.Clear();
        sim._issuedEventKeys.Clear();
        sim.Ledger.Restore(data.Food, data.Silk);
        sim.Ledger.RestoreProcessed(data.Counters.LedgerKeys);
        sim.TickCount = data.Tick;
        sim.ElapsedSeconds = data.ElapsedSeconds;

        sim._webs.Clear();
        foreach (var web in data.Webs)
        {
            var type = web.Type == "bridge" ? WebType.Bridge : WebType.Capture;
            var state = new WebState
            {
                Id = web.Id, Type = type, AnchorA = web.AnchorA, AnchorB = web.AnchorB,
                Durability = web.Durability, MaxDurability = web.MaxDurability,
                Exposure = web.Exposure, Sheltered = web.Sheltered, IsTutorial = web.IsTutorial,
                ConstructedAtTick = web.ConstructedAtTick
            };
            sim._webs[state.Id] = state;
            if (type == WebType.Bridge) sim.Graph.AddBridge(state.Id, state.AnchorA, state.AnchorB);
        }

        sim._workers.Clear();
        foreach (var saved in data.Workers)
        {
            var worker = new WorkerState
            {
                Id = saved.Id, Name = saved.Name, Colour = saved.Colour,
                Position = new Vec3(saved.X, saved.Y, saved.Z),
                CurrentNode = saved.CurrentNode,
                Activity = Enum.TryParse<WorkerActivity>(saved.Activity, true, out var activity) ? activity : WorkerActivity.Resting,
                TargetInsectId = saved.TargetInsectId,
                TargetWebId = saved.TargetWebId,
                TargetNode = saved.TargetNode,
                PathNodes = saved.PathNodes.ToList(),
                PathIndex = saved.PathIndex,
                ArrivalDelay = saved.ArrivalDelay,
                PatrolCooldown = saved.PatrolCooldown,
                CollectCount = saved.CollectCount,
                RepairCount = saved.RepairCount
            };
            worker.PathEdges = new List<GroundEdge>();
            for (var i = 0; i < worker.PathNodes.Count - 1; i++)
            {
                var a = worker.PathNodes[i];
                var b = worker.PathNodes[i + 1];
                var webId = i < saved.PathEdgeWebIds.Count ? saved.PathEdgeWebIds[i] : "";
                var edge = !string.IsNullOrWhiteSpace(webId)
                    ? new GroundEdge { A = a, B = b, Kind = "bridge", WebId = webId }
                    : new GroundEdge { A = a, B = b, Kind = "ground" };
                worker.PathEdges.Add(edge);
            }
            sim._workers.Add(worker);
        }

        sim._insects.Clear();
        foreach (var route in WorldLayout.CreateInsectRoutes())
        {
            var saved = data.Insects.FirstOrDefault(i => i.Id == route.Id);
            var insect = new InsectState
            {
                Definition = route,
                Generation = saved?.Generation ?? 0,
                Phase = saved is not null && Enum.TryParse<InsectPhase>(saved.Phase, true, out var phase) ? phase : InsectPhase.Flying,
                Position = saved is null ? route.Waypoints[0] : new Vec3(saved.X, saved.Y, saved.Z),
                PreviousPosition = saved is null ? route.Waypoints[0] : new Vec3(saved.X, saved.Y, saved.Z),
                WaypointIndex = saved?.WaypointIndex ?? 0,
                TrappedWebId = saved?.TrappedWebId,
                CapturedAtTick = saved?.CapturedAtTick ?? 0,
                RespawnAtTick = saved?.RespawnAtTick ?? 0
            };
            sim._insects.Add(insect);
        }

        sim.Missions.Restore(
            data.Missions.ActiveMissionIndex,
            data.Missions.StepIndex,
            data.Missions.Progress,
            data.Missions.Completed,
            data.Missions.SeenEvents,
            data.Missions.VictoryAwarded);

        sim.WeatherPhase = Enum.TryParse<WeatherPhase>(data.Weather.Phase, true, out var weather) ? weather : WeatherPhase.Calm;
        sim._weatherRemaining = data.Weather.Remaining;
        sim._windStarted = data.Weather.WindStarted;
        sim._windCompleted = data.Weather.WindCompleted;
        sim._aftermathRemaining = data.Weather.AftermathRemaining;
        sim._webSequence = data.Counters.WebSequence;
        sim._workerSequence = data.Counters.WorkerSequence;
        sim._transactionSequence = data.Counters.TransactionSequence;
        sim.CaptureWebsBuilt = data.Counters.CaptureWebsBuilt;
        sim.BridgeWebsBuilt = data.Counters.BridgeWebsBuilt;
        sim.InsectsCollected = data.Counters.InsectsCollected;
        sim.RepairsCompleted = data.Counters.RepairsCompleted;
        sim.EmergencyRecoveryUses = data.Counters.EmergencyUses;
        sim._masteryAwarded = data.Counters.MasteryAwarded;
        sim._victory = data.Counters.Victory;
        sim._silkAccumulator = data.Counters.SilkAccumulator;
        if (!string.IsNullOrWhiteSpace(data.Counters.PendingHatchId) && data.Counters.PendingHatchRemaining > 0)
        {
            sim.PendingHatch = new PendingHatch
            {
                Id = data.Counters.PendingHatchId,
                Remaining = data.Counters.PendingHatchRemaining
            };
        }
        foreach (var key in data.Counters.IssuedEventKeys) sim._issuedEventKeys.Add(key);
        return sim;
    }

    public string GetHint()
    {        if (_victory) return "Garden complete! Keep exploring, weaving and catching.";
        if (WeatherPhase == WeatherPhase.Warning)
            return $"Wind is coming in {(int)Math.Ceiling(_weatherRemaining)}s. Repair or shelter your web.";
        if (WeatherPhase == WeatherPhase.Aftermath && _webs.Values.Any(w => w.Type == WebType.Capture && w.Durability < w.MaxDurability))
            return "A web was damaged. Press E beside it to repair for 2 silk.";
        if (Missions.ActiveMissionId == "M01")
        {
            if (CaptureWebsBuilt == 0) return "Choose the two glowing twin twigs, then click them to build a capture web.";
            if (_insects.Any(i => i.CanBeCollected)) return "A fly is caught! Walk close and press E to collect food.";
            return "A fly is coming. Watch your web!";
        }
        if (Missions.ActiveMissionId == "M02")
        {
            if (BridgeWebsBuilt == 0) return "Select the two pebble posts and build a silk bridge (key 2).";
            if (Workers.Count == 0) return "Visit the leaf nursery and hatch a spider helper.";
            return "Your helper is exploring. See it cross the bridge!";
        }
        if (Missions.ActiveMissionId == "M03")
        {
            if (!_windCompleted) return "Wind is on its way. Keep an eye on exposed webs.";
            if (!_webs.Values.Any(w => w.Durability < w.MaxDurability) && !_webs.Values.Any(w => w is { Type: WebType.Capture, Sheltered: true }))
                return "Build a sheltered web near the log or repair a damaged one.";
            if (!_insects.Any(i => i.CanBeCollected)) return "Catch one more insect after the wind.";
            return "Collect the trapped insect to finish the story.";
        }
        return "Explore the garden. Your webs make a difference.";
    }

    public double WebCenterDistance(WebState web, Vec3 position)
    {
        var a = GetAnchor(web.AnchorA);
        var b = GetAnchor(web.AnchorB);
        if (a is null || b is null) return double.MaxValue;
        var center = (a.Position + b.Position) * 0.5;
        return Vec3.Distance(center, position);
    }

    private Vec3 WebCenter(WebState web)
    {
        var a = GetAnchor(web.AnchorA);
        var b = GetAnchor(web.AnchorB);
        return a is null || b is null ? Vec3.Zero : (a.Position + b.Position) * 0.5;
    }
}
