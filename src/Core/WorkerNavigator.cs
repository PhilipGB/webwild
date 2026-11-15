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

    public WorkerNavigator(int seed) => _generator = new WorldGenerator(seed);

    public WorldGenerator Generator => _generator;

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
            var cost = 0.0;
            var cursor = positions[a];
            foreach (var point in via)
            {
                cost += Vec3.Distance(cursor, point);
                cursor = point;
            }
            nodes.Add((a, b, via, bridge));
        }

        var bridgePairs = new List<(int North, int South, RiverBridge Bridge)>();
        foreach (var bridge in bridges)
        {
            var north = new Vec3(bridge.X, BankY(bridge.X, bridge.CenterZ - bridge.HalfSpan - 1.5),
                bridge.CenterZ - bridge.HalfSpan - 1.5);
            var south = new Vec3(bridge.X, BankY(bridge.X, bridge.CenterZ + bridge.HalfSpan + 1.5),
                bridge.CenterZ + bridge.HalfSpan + 1.5);
            var northNode = positions.Count;
            positions.Add(north);
            var southNode = positions.Count;
            positions.Add(south);
            bridgePairs.Add((northNode, southNode, bridge));
        }

        var northCount = bridgePairs.Count;
        for (var i = 0; i < northCount; i++)
        {
            var (northNode, southNode, bridge) = bridgePairs[i];
            var north = positions[northNode];
            var south = positions[southNode];
            var deckTop = RiverMap.DeckTop(bridge) + 0.06;
            var deckPoints = new List<Vec3>();
            var deckSteps = Math.Max(1, (int)Math.Ceiling(bridge.HalfSpan * 2 / 3.0));
            for (var step = 0; step <= deckSteps; step++)
            {
                var z = bridge.CenterZ - bridge.HalfSpan +
                        step * (bridge.HalfSpan * 2 / deckSteps);
                deckPoints.Add(new Vec3(bridge.X, deckTop, z));
            }
            deckPoints.Add(south);
            AddEdge(northNode, southNode, deckPoints.ToArray(), bridge: true);
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

    /// <summary>Sampled terrain-followed points, or null if any point is wet.</summary>
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
            if (IsWet(x, z)) return null;
            var y = i == steps ? to.Y : i == 1 && from.Y > 0 ? from.Y : BankY(x, z);
            points[i - 1] = new Vec3(x, y, z);
        }
        return points;
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

    private List<RiverBridge> CandidateBridges(Vec3 start, Vec3 target)
    {
        var minX = Math.Min(start.X, target.X) - CrossingCorridorMetres;
        var maxX = Math.Max(start.X, target.X) + CrossingCorridorMetres;
        var minZ = Math.Min(start.Z, target.Z) - CrossingCorridorMetres;
        var maxZ = Math.Max(start.Z, target.Z) + CrossingCorridorMetres;
        var firstRiver = (int)Math.Floor((minZ - RiverMap.Offset) / RiverMap.Spacing) - 1;
        var lastRiver = (int)Math.Ceiling((maxZ - RiverMap.Offset) / RiverMap.Spacing) + 1;
        var firstCrossing = (long)Math.Floor(minX / RiverMap.CrossingSpacing);
        var lastCrossing = (long)Math.Ceiling(maxX / RiverMap.CrossingSpacing);
        var candidates = new List<(RiverBridge Bridge, double Distance)>();
        for (var river = firstRiver; river <= lastRiver; river++)
        for (var crossing = firstCrossing; crossing <= lastCrossing; crossing++)
        {
            var bridge = _generator.Rivers.BridgeAt(river, (int)crossing, _generator);
            if (bridge is null) continue;
            var distance = HorizontalDistanceToSegment(
                new Vec3(bridge.X, 0, bridge.CenterZ), start, target);
            if (distance > CrossingCorridorMetres) continue;
            candidates.Add((bridge, distance));
        }
        return candidates
            .OrderBy(candidate => candidate.Distance)
            .Take(MaxCrossingCandidates)
            .Select(candidate => candidate.Bridge)
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
