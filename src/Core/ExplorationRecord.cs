namespace Webwild.Core;

/// <summary>
/// Bounded record of chunks the player has seen.
///
/// The map needs a durable exploration history, but an endless world would
/// otherwise grow the save forever. Entries are retained in first-visit order
/// up to <see cref="MaxChunks"/>; the oldest are evicted first. IDs are stable
/// chunk coordinates and are written to the schema-4 manifest, never into the
/// embedded colony payload.
/// </summary>
public sealed class ExplorationRecord
{
    /// <summary>Enough for roughly 6.9 km² of explored ground, well inside the
    /// manifest budget even with thousands of player-made records.</summary>
    public const int MaxChunks = 12_000;

    private readonly HashSet<ChunkCoordinate> _chunks = new();
    private readonly Queue<ChunkCoordinate> _arrival = new();

    public int Count => _chunks.Count;
    public bool Contains(ChunkCoordinate coordinate) => _chunks.Contains(coordinate);

    /// <summary>Record one chunk. Returns true only for a first visit.</summary>
    public bool Visit(ChunkCoordinate coordinate)
    {
        if (!_chunks.Add(coordinate)) return false;
        _arrival.Enqueue(coordinate);
        while (_chunks.Count > MaxChunks && _arrival.Count > 0)
            _chunks.Remove(_arrival.Dequeue());
        return true;
    }

    /// <summary>Record a square around a centre chunk (radius 1 = 3×3).</summary>
    public int VisitArea(ChunkCoordinate centre, int radius)
    {
        var added = 0;
        for (var x = centre.X - radius; x <= centre.X + radius; x++)
        for (var z = centre.Z - radius; z <= centre.Z + radius; z++)
            if (Visit(new ChunkCoordinate(x, z))) added++;
        return added;
    }

    public void Clear()
    {
        _chunks.Clear();
        _arrival.Clear();
    }

    /// <summary>Restore a saved record. Invalid IDs are ignored; the cap holds.</summary>
    public void Restore(IEnumerable<string>? ids)
    {
        Clear();
        if (ids is null) return;
        foreach (var id in ids)
            if (ChunkCoordinate.TryParse(id ?? "", out var coordinate))
                Visit(coordinate);
    }

    /// <summary>Sorted stable IDs for the world manifest.</summary>
    public List<string> ToIds() => _chunks
        .OrderBy(coordinate => coordinate.X)
        .ThenBy(coordinate => coordinate.Z)
        .Select(coordinate => coordinate.Id)
        .ToList();

    /// <summary>Snapshot for the map presentation.</summary>
    public List<ChunkCoordinate> Snapshot() => _chunks
        .OrderBy(coordinate => coordinate.X)
        .ThenBy(coordinate => coordinate.Z)
        .ToList();
}
