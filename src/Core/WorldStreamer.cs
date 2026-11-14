namespace Webwild.Core;

/// <summary>One streaming update: which chunks to build next and which to retire.</summary>
public sealed record ChunkStreamPlan(
    IReadOnlyList<ChunkCoordinate> ToLoad,
    IReadOnlyList<ChunkCoordinate> ToUnload)
{
    public static readonly ChunkStreamPlan Empty = new(Array.Empty<ChunkCoordinate>(), Array.Empty<ChunkCoordinate>());
}

/// <summary>
/// Engine-free chunk lifecycle for the never-ending world.
///
/// The streamer keeps a bounded active set: everything within
/// <see cref="LoadRadius"/> chunks of the player's chunk, ordered deterministically
/// by distance, plus a retention band (<see cref="UnloadRadius"/>) so short
/// backtracks never thrash loads and unloads. Authored-footprint chunks are
/// never streamed because hand-made terrain covers them.
///
/// Godot adapters translate the plans into scene nodes; they must never let the
/// active set grow without bound, and the same chunk must rebuild identically
/// after any unload (guaranteed by the deterministic generator).
/// </summary>
public sealed class WorldStreamer
{
    public int LoadRadius { get; }
    public int UnloadRadius { get; }

    private readonly Dictionary<ChunkCoordinate, byte> _active = new();
    private ChunkCoordinate _centre = ChunkCoordinate.Zero;

    public WorldStreamer(int loadRadius = 3, int unloadRadius = 5)
    {
        if (loadRadius < 1) throw new ArgumentOutOfRangeException(nameof(loadRadius));
        if (unloadRadius <= loadRadius) throw new ArgumentOutOfRangeException(nameof(unloadRadius),
            "The unload radius must exceed the load radius so backtracking cannot thrash.");
        LoadRadius = loadRadius;
        UnloadRadius = unloadRadius;
    }

    public IReadOnlyDictionary<ChunkCoordinate, byte> Active => _active;
    public ChunkCoordinate Centre => _centre;
    public int ActiveCount => _active.Count;

    /// <summary>True when the chunk is part of the active streaming set.</summary>
    public bool IsLoaded(ChunkCoordinate coordinate) => _active.ContainsKey(coordinate);

    /// <summary>
    /// Recompute plans for a new player chunk centre. Chunks inside the
    /// authored footprint are never requested. ToLoad is sorted nearest-first
    /// with a deterministic tiebreak so adapters can budget work per frame.
    /// </summary>
    public ChunkStreamPlan Update(ChunkCoordinate centre)
    {
        _centre = centre;
        var toLoad = new List<ChunkCoordinate>();
        for (var dx = -LoadRadius; dx <= LoadRadius; dx++)
        for (var dz = -LoadRadius; dz <= LoadRadius; dz++)
        {
            var candidate = centre.Offset(dx, dz);
            if (WorldGenerator.IsAuthoredChunk(candidate)) continue;
            if (_active.ContainsKey(candidate)) continue;
            toLoad.Add(candidate);
        }
        toLoad.Sort((a, b) =>
        {
            var byDistance = a.ChebyshevDistance(centre).CompareTo(b.ChebyshevDistance(centre));
            return byDistance != 0 ? byDistance : a.CompareTo(b);
        });

        var toUnload = new List<ChunkCoordinate>();
        foreach (var active in _active.Keys)
        {
            if (active.ChebyshevDistance(centre) > UnloadRadius) toUnload.Add(active);
        }
        toUnload.Sort();

        foreach (var coordinate in toLoad) _active[coordinate] = 1;
        foreach (var coordinate in toUnload) _active.Remove(coordinate);

        return new ChunkStreamPlan(toLoad, toUnload);
    }

    /// <summary>
    /// Required chunks for a centre, useful for tests and "is the ground ready"
    /// checks: everything within the load radius that is not authored.
    /// </summary>
    public IEnumerable<ChunkCoordinate> RequiredFor(ChunkCoordinate centre)
    {
        for (var dx = -LoadRadius; dx <= LoadRadius; dx++)
        for (var dz = -LoadRadius; dz <= LoadRadius; dz++)
        {
            var candidate = centre.Offset(dx, dz);
            if (!WorldGenerator.IsAuthoredChunk(candidate)) yield return candidate;
        }
    }
}
