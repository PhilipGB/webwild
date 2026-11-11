namespace Webwild.Core;

public sealed class WorldNodeDefinition
{
    public required string Id { get; init; }
    public Vec3 Position { get; init; }
    public string Label { get; init; } = "";
}

public sealed record RoutePlan(IReadOnlyList<string> Nodes, IReadOnlyList<GroundEdge> Edges, double Cost)
{
    public bool UsesBridge => Edges.Any(e => e.UsesBridge);
    public string Target => Nodes.Count == 0 ? "" : Nodes[^1];
}

/// <summary>Small explicit navigation graph for workers and mission helpers.</summary>
public sealed class WorldGraph
{
    private readonly Dictionary<string, WorldNodeDefinition> _nodes = new(StringComparer.Ordinal);
    private readonly List<GroundEdge> _edges = new();

    public WorldGraph(IEnumerable<WorldNodeDefinition> nodes, IEnumerable<GroundEdge> groundEdges)
    {
        foreach (var node in nodes) _nodes[node.Id] = node;
        _edges.AddRange(groundEdges);
    }

    public IReadOnlyDictionary<string, WorldNodeDefinition> Nodes => _nodes;
    public IReadOnlyList<GroundEdge> Edges => _edges;

    public bool TryGetNode(string id, out WorldNodeDefinition node) => _nodes.TryGetValue(id, out node!);

    public void AddBridge(string webId, string anchorA, string anchorB)
    {
        RemoveBridge(webId);
        _edges.Add(new GroundEdge { A = anchorA, B = anchorB, Kind = "bridge", WebId = webId });
    }

    public void RemoveBridge(string webId)
    {
        _edges.RemoveAll(e => e.WebId == webId && e.Kind == "bridge");
    }

    public void AddEdge(GroundEdge edge) => _edges.Add(edge);

    /// <summary>Dijkstra over the tiny explicit graph. Missing nodes make the route unavailable.</summary>
    public RoutePlan? FindRoute(string start, string target)
    {
        if (!_nodes.ContainsKey(start) || !_nodes.ContainsKey(target)) return null;
        if (start == target) return new RoutePlan(new[] { start }, Array.Empty<GroundEdge>(), 0);

        var dist = new Dictionary<string, double>(StringComparer.Ordinal) { [start] = 0 };
        var prev = new Dictionary<string, (string Node, GroundEdge Edge)>(StringComparer.Ordinal);
        var open = new PriorityQueue<string, double>();
        open.Enqueue(start, 0);
        var closed = new HashSet<string>(StringComparer.Ordinal);

        while (open.TryDequeue(out var current, out var currentCost))
        {
            if (!closed.Add(current)) continue;
            if (current == target) break;

            foreach (var edge in _edges)
            {
                var next = edge.A == current ? edge.B : edge.B == current ? edge.A : null;
                if (next is null || !_nodes.ContainsKey(next)) continue;
                if (closed.Contains(next)) continue;
                var step = Vec3.Distance(_nodes[current].Position, _nodes[next].Position);
                var tentative = currentCost + step;
                if (!dist.TryGetValue(next, out var known) || tentative < known - 1e-9)
                {
                    dist[next] = tentative;
                    prev[next] = (current, edge);
                    open.Enqueue(next, tentative);
                }
            }
        }

        if (!dist.ContainsKey(target)) return null;
        var nodes = new List<string>();
        var edges = new List<GroundEdge>();
        var cursor = target;
        nodes.Add(cursor);
        while (cursor != start)
        {
            var (p, edge) = prev[cursor];
            edges.Add(edge);
            nodes.Add(p);
            cursor = p;
        }
        nodes.Reverse();
        edges.Reverse();
        return new RoutePlan(nodes, edges, dist[target]);
    }

    public bool IsReachable(string start, string target) => FindRoute(start, target) is not null;
}
