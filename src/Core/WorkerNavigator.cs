namespace Webwild.Core;

/// <summary>One navigable worker route in stable world coordinates.</summary>
public sealed record WorkerRoute(
    IReadOnlyList<Vec3> Points, bool UsesBridge, double Distance);

/// <summary>
/// Hierarchical worker navigation for the continuous world.
///
/// The world layer searches a coarse set of generated river crossings and
/// landmark banks; the local layer fills the chosen legs with terrain-followed
/// points. It deliberately never searches surface triangles and never returns a
/// route through deep water: if no dry way exists, the worker waits.
/// </summary>
public sealed class WorkerNavigator
{
    private const int MaxCrossingCandidates = 24;
    private const double CrossingCorridorMetres = 420;
    private const double PointSpacing = 6.0;

    private readonly WorldGenerator _generator;
    private IReadOnlyList<PlayerRiverBridge> _playerBridges = Array.Empty<PlayerRiverBridge>();

    public WorkerNavigator(int seed, int generatorVersion = 1) =>
        _generator = new WorldGenerator(seed, generatorVersion);

    public WorldGenerator Generator => _generator;

    /// <summary>The player's built crossings, shared with
    /// <see cref="GameSimulation"/>. One traversal model serves generated
    /// bridges and silk bridges alike, so a worker crosses exactly what the
    /// player sees.</summary>
    public void SetPlayerBridges(IReadOnlyList<PlayerRiverBridge> bridges) =>
        _playerBridges = bridges ?? Array.Empty<PlayerRiverBridge>();

    /// <summary>One usable river crossing in the corridor graph: either a
    /// generated landmark bridge or a player-built silk crossing.</summary>
    private readonly record struct Crossing(
        RiverBridge? Generated, PlayerRiverBridge? Player)
    {
        public double X => Generated?.X ?? Player!.X;
        public double CenterZ => Generated?.CenterZ ?? Player!.CenterZ;
    }

    /// <summary>Check that an existing route is still walkable. Bridge-deck
    /// points are allowed above water; ground points inside a river are not.
    /// Every segment is sampled internally — even short ones — so a densely
    /// pointed deck path cannot hide a removed crossing between its points.</summary>
    public bool IsRouteDry(IReadOnlyList<Vec3> points)
    {
        if (points.Count < 2) return false;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            if (!a.IsFinite || !b.IsFinite) return false;
            var distance = Vec3.Distance(a, b);
            var steps = Math.Max(2, (int)Math.Ceiling(distance / 3.0));
            for (var step = 1; step < steps; step++)
            {
                var t = step / (double)steps;
                var x = a.X + (b.X - a.X) * t;
                var y = a.Y + (b.Y - a.Y) * t;
                var z = a.Z + (b.Z - a.Z) * t;
                if (!IsWet(x, z)) continue;
                if (!IsBridgeDeck(x, y, z)) return false;
            }
        }
        return true;
    }

    private bool IsBridgeDeck(double x, double y, double z)
    {
        var river = _generator.RiverAt(x, z);
        if (!river.HasRiver) return false;
        var crossing = (long)Math.Round(x / RiverMap.CrossingSpacing);
        for (var n = crossing - 1; n <= crossing + 1; n++)
        {
            var bridge = _generator.Rivers.BridgeAt(river.Index, (int)n, _generator);
            if (bridge is null) continue;
            if (Math.Abs(x - bridge.X) > 1.9) continue;
            if (Math.Abs(z - bridge.CenterZ) > bridge.HalfSpan + 1.5) continue;
            if (y < bridge.WaterLevel + 0.2) continue;
            return true;
        }
        // Player-built silk crossings use the same waterline rule at the
        // narrower walkway width, so a deck is only traversable where the
        // player could physically stand on it.
        foreach (var bridge in _playerBridges)
        {
            if (bridge.RiverIndex != river.Index) continue;
            if (!bridge.Covers(x, z)) continue;
            if (y < bridge.WaterLevel + 0.2) continue;
            return true;
        }
        return false;
    }

    public WorkerRoute? FindRoute(Vec3 start, Vec3 target)
    {
        if (!start.IsFinite || !target.IsFinite) return null;
        if (Vec3.Distance(start, target) < 0.01)
            return new WorkerRoute(new[] { start }, false, 0);

        var startLandmark = LandmarkCatalog.LandmarkAt(start.X, start.Z);
        var targetLandmark = LandmarkCatalog.LandmarkAt(target.X, target.Z);
        var waypoints = new List<Vec3>();
        if (startLandmark is not null && startLandmark != targetLandmark)
        {
            if (GateFor(startLandmark, target) is { } exitGate)
                waypoints.Add(WithGroundY(exitGate));
        }
        if (targetLandmark is not null && startLandmark != targetLandmark)
        {
            if (GateFor(targetLandmark, start) is { } entryGate &&
                (waypoints.Count == 0 || Vec3.Distance(waypoints[^1], entryGate) > 2))
                waypoints.Add(WithGroundY(entryGate));
        }
        if (waypoints.Count == 0) return FindLeg(start, target);

        var points = new List<Vec3> { start };
        var usesBridge = false;
        var distance = 0.0;
        var cursor = start;
        waypoints.Add(target);
        foreach (var waypoint in waypoints)
        {
            var leg = FindLeg(cursor, waypoint);
            if (leg is null) return null;
            for (var i = 1; i < leg.Points.Count; i++) points.Add(leg.Points[i]);
            usesBridge |= leg.UsesBridge;
            distance += leg.Distance;
            cursor = waypoint;
        }
        points = Simplify(points);
        return new WorkerRoute(points, usesBridge, distance);
    }

    private WorkerRoute? FindLeg(Vec3 start, Vec3 target)
    {
        var direct = Build(start, target);
        if (direct is not null) return direct;

        var bridges = CandidateBridges(start, target);
        if (bridges.Count == 0) return null;

        // Nodes: 0 = start, 1 = target, then two bank nodes per crossing.
        var positions = new List<Vec3> { start, target };
        var nodes = new List<(int A, int B, Vec3[] Via, bool Bridge)>();
        void AddEdge(int a, int b, Vec3[] via, bool bridge)
        {
            if (via.Length == 0) return;
            nodes.Add((a, b, via, bridge));
        }

        var bridgePairs = new List<(int North, int South, Vec3[] Via)>();
        foreach (var crossing in bridges)
        {
            Vec3 north;
            Vec3 south;
            Vec3[] via;
            if (crossing.Generated is { } generated)
            {
                north = new Vec3(generated.X,
                    BankY(generated.X, generated.CenterZ - generated.HalfSpan - 1.5),
                    generated.CenterZ - generated.HalfSpan - 1.5);
                south = new Vec3(generated.X,
                    BankY(generated.X, generated.CenterZ + generated.HalfSpan + 1.5),
                    generated.CenterZ + generated.HalfSpan + 1.5);
                var deckTop = RiverMap.DeckTop(generated) + 0.06;
                var deckPoints = new List<Vec3>();
                var deckSteps = Math.Max(1, (int)Math.Ceiling(generated.HalfSpan * 2 / 3.0));
                for (var step = 0; step <= deckSteps; step++)
                {
                    var z = generated.CenterZ - generated.HalfSpan +
                            step * (generated.HalfSpan * 2 / deckSteps);
                    deckPoints.Add(new Vec3(generated.X, deckTop, z));
                }
                deckPoints.Add(south);
                via = deckPoints.ToArray();
            }
            else
            {
                var player = crossing.Player!;
                // The silk crossing's own geometry: bank ramps and the
                // sagging deck, exactly where the walkway collision lives.
                north = player.NorthGround;
                south = player.SouthGround;
                var deck = player.DeckPoints();
                deck.Add(south);
                via = deck.ToArray();
            }
            var northNode = positions.Count;
            positions.Add(north);
            var southNode = positions.Count;
            positions.Add(south);
            bridgePairs.Add((northNode, southNode, via));
        }

        var northCount = bridgePairs.Count;
        for (var i = 0; i < northCount; i++)
        {
            var (northNode, southNode, via) = bridgePairs[i];
            var north = positions[northNode];
            var south = positions[southNode];
            AddEdge(northNode, southNode, via, bridge: true);
            if (DryPath(start, north) is { } startToNorth)
                AddEdge(0, northNode, startToNorth, false);
            if (DryPath(north, target) is { } northToTarget)
                AddEdge(northNode, 1, northToTarget, false);
            if (DryPath(start, south) is { } startToSouth)
                AddEdge(0, southNode, startToSouth, false);
            if (DryPath(south, target) is { } southToTarget)
                AddEdge(southNode, 1, southToTarget, false);
        }

        // Dijkstra over the tiny corridor graph.
        var distances = new double[positions.Count];
        var previous = new (int Node, int Edge)[positions.Count];
        Array.Fill(distances, double.PositiveInfinity);
        Array.Fill(previous, (-1, -1));
        distances[0] = 0;
        var open = new PriorityQueue<int, double>();
        open.Enqueue(0, 0);
        while (open.TryDequeue(out var current, out var currentCost))
        {
            if (currentCost > distances[current] + 1e-9) continue;
            if (current == 1) break;
            foreach (var (index, edge) in nodes.Select((edge, index) => (index, edge)))
            {
                if (edge.A != current && edge.B != current) continue;
                var next = edge.A == current ? edge.B : edge.A;
                var via = edge.A == current ? edge.Via : edge.Via.Reverse().ToArray();
                var cost = 0.0;
                var cursor = positions[current];
                foreach (var point in via)
                {
                    cost += Vec3.Distance(cursor, point);
                    cursor = point;
                }
                var tentative = currentCost + cost;
                if (tentative >= distances[next] - 1e-9) continue;
                distances[next] = tentative;
                previous[next] = (current, index);
                open.Enqueue(next, tentative);
            }
        }
        if (!double.IsFinite(distances[1])) return null;

        var segments = new List<(int Node, int Edge)>();
        var walk = 1;
        while (walk != 0 && previous[walk].Node >= 0)
        {
            segments.Add(previous[walk]);
            walk = previous[walk].Node;
        }
        segments.Reverse();

        var points = new List<Vec3> { start };
        var usesBridge = false;
        foreach (var (fromNode, edgeIndex) in segments)
        {
            var edge = nodes[edgeIndex];
            var via = edge.A == fromNode ? edge.Via : edge.Via.Reverse().ToArray();
            points.AddRange(via);
            usesBridge |= edge.Bridge;
        }
        points.Add(target);
        points = Simplify(points);
        if (points.Count < 2) return null;
        return new WorkerRoute(points, usesBridge, RouteDistance(points));
    }

    /// <summary>Direct dry route, or null when deep water blocks the line.</summary>
    public WorkerRoute? FindDryDirectRoute(Vec3 start, Vec3 target) =>
        Build(start, target);

    private WorkerRoute? Build(Vec3 start, Vec3 target)
    {
        var via = DryPath(start, target);
        if (via is null) return null;
        var points = new List<Vec3> { start };
        points.AddRange(via);
        points = Simplify(points);
        return points.Count < 2 ? null : new WorkerRoute(points, false, RouteDistance(points));
    }

    /// <summary>Sampled terrain-followed points, or null if any point is wet
    /// or blocked. Version-2 worlds also refuse steep cliff faces: a face the
    /// player must climb is not ground a helper can walk.</summary>
    private Vec3[]? DryPath(Vec3 from, Vec3 to)
    {
        var distance = Vec3.Distance(from, to);
        var steps = Math.Max(1, (int)Math.Ceiling(distance / PointSpacing));
        var points = new Vec3[steps];
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (double)steps;
            var x = from.X + (to.X - from.X) * t;
            var z = from.Z + (to.Z - from.Z) * t;
            if (IsBlockedForWalking(x, z)) return null;
            var y = i == steps ? to.Y : i == 1 && from.Y > 0 ? from.Y : BankY(x, z);
            points[i - 1] = new Vec3(x, y, z);
        }
        return points;
    }

    /// <summary>True where a helper cannot walk: deep water anywhere, and on
    /// version-2 cliff faces too steep to stand on.</summary>
    private bool IsBlockedForWalking(double x, double z)
    {
        if (IsWet(x, z)) return true;
        if (_generator.GeneratorVersion < 2) return false;
        const double step = 1.0;
        var hx = _generator.HeightAt(x + step, z) - _generator.HeightAt(x - step, z);
        var hz = _generator.HeightAt(x, z + step) - _generator.HeightAt(x, z - step);
        return Math.Sqrt(hx * hx + hz * hz) / (2 * step) > 0.9;
    }

    private bool IsWet(double x, double z)
    {
        var river = _generator.RiverAt(x, z);
        return river.HasRiver && river.Distance <= river.HalfWidth + 1.0;
    }

    private double BankY(double x, double z) => _generator.HeightAt(x, z) + 0.3;

    /// <summary>World points through the authored hedge gates, so helpers never
    /// walk through a wall.</summary>
    private static Vec3? GateFor(string landmarkId, Vec3 other)
    {
        if (landmarkId == LandmarkCatalog.Garden)
        {
            var gates = new[]
            {
                new Vec3(24, 0, 8.5),   // east hedge gate
                new Vec3(-24, 0, 8.5),  // west hedge gate
                new Vec3(0, 0, -24),    // north hedge gate
                new Vec3(0, 0, 24)      // south hedge gate
            };
            return gates.OrderBy(gate =>
                (gate.X - other.X) * (gate.X - other.X) +
                (gate.Z - other.Z) * (gate.Z - other.Z)).First();
        }
        if (landmarkId == LandmarkCatalog.Reedwater)
            return new Vec3(96 - 19.6, 0, 6.0); // Reedwater's west bramble gate
        return null;
    }

    private Vec3 WithGroundY(Vec3 point) =>
        new(point.X, BankY(point.X, point.Z), point.Z);

    private List<Crossing> CandidateBridges(Vec3 start, Vec3 target)
    {
        var minX = Math.Min(start.X, target.X) - CrossingCorridorMetres;
        var maxX = Math.Max(start.X, target.X) + CrossingCorridorMetres;
        var minZ = Math.Min(start.Z, target.Z) - CrossingCorridorMetres;
        var maxZ = Math.Max(start.Z, target.Z) + CrossingCorridorMetres;
        var firstRiver = (int)Math.Floor((minZ - RiverMap.Offset) / RiverMap.Spacing) - 1;
        var lastRiver = (int)Math.Ceiling((maxZ - RiverMap.Offset) / RiverMap.Spacing) + 1;
        var firstCrossing = (long)Math.Floor(minX / RiverMap.CrossingSpacing);
        var lastCrossing = (long)Math.Ceiling(maxX / RiverMap.CrossingSpacing);
        var candidates = new List<(Crossing Crossing, double Distance)>();
        for (var river = firstRiver; river <= lastRiver; river++)
        for (var crossing = firstCrossing; crossing <= lastCrossing; crossing++)
        {
            var bridge = _generator.Rivers.BridgeAt(river, (int)crossing, _generator);
            if (bridge is null) continue;
            var distance = HorizontalDistanceToSegment(
                new Vec3(bridge.X, 0, bridge.CenterZ), start, target);
            if (distance > CrossingCorridorMetres) continue;
            candidates.Add((new Crossing(bridge, null), distance));
        }
        // Player-built silk crossings join the same corridor graph with the
        // same distance rule; the Dijkstra below decides which crossing the
        // journey actually uses.
        foreach (var bridge in _playerBridges)
        {
            if (bridge.RiverIndex < firstRiver || bridge.RiverIndex > lastRiver) continue;
            if (bridge.X < minX || bridge.X > maxX) continue;
            var distance = HorizontalDistanceToSegment(
                new Vec3(bridge.X, 0, bridge.CenterZ), start, target);
            if (distance > CrossingCorridorMetres) continue;
            candidates.Add((new Crossing(null, bridge), distance));
        }
        return candidates
            .OrderBy(candidate => candidate.Distance)
            .Take(MaxCrossingCandidates)
            .Select(candidate => candidate.Crossing)
            .ToList();
    }

    private static double HorizontalDistanceToSegment(Vec3 point, Vec3 a, Vec3 b)
    {
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        var lengthSquared = dx * dx + dz * dz;
        if (lengthSquared < 1e-9) return Math.Sqrt(
            (point.X - a.X) * (point.X - a.X) + (point.Z - a.Z) * (point.Z - a.Z));
        var t = Math.Clamp(((point.X - a.X) * dx + (point.Z - a.Z) * dz) / lengthSquared, 0, 1);
        var x = a.X + dx * t;
        var z = a.Z + dz * t;
        return Math.Sqrt((point.X - x) * (point.X - x) + (point.Z - z) * (point.Z - z));
    }

    private static List<Vec3> Simplify(List<Vec3> points)
    {
        var result = new List<Vec3>(points.Count);
        foreach (var point in points)
        {
            if (result.Count == 0 || Vec3.Distance(result[^1], point) > 0.02)
                result.Add(point);
        }
        return result;
    }

    private static double RouteDistance(IReadOnlyList<Vec3> points)
    {
        var distance = 0.0;
        for (var i = 1; i < points.Count; i++) distance += Vec3.Distance(points[i - 1], points[i]);
        return distance;
    }
}
