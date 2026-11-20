namespace Webwild.Core;

/// <summary>One compact region of the durable exploration record, serialised
/// as a base64 bitset of 64×64 chunk bits (512 bytes).</summary>
public sealed class ExplorationRegionSave
{
    public long RegionX { get; set; }
    public long RegionZ { get; set; }
    /// <summary>Base64 of 512 bytes, one bit per chunk in the region, row
    /// major: bit (localX + localZ*64).</summary>
    public string Bits { get; set; } = "";
}

/// <summary>
/// Durable record of chunks the player has seen.
///
/// The world is endless, so the record is region-partitioned: chunks are
/// grouped into 64×64 regions (1536 m squares) and each explored region
/// stores one compact bitset — 4096 chunks in 512 bytes instead of
/// thousands of id strings. A region is created only when the player
/// actually sees ground inside it, so storage grows with the width of the
/// journey, not with the number of visited chunks, and no per-chunk
/// eviction can ever make an old discovery un-explore itself.
///
/// The record is bounded by <see cref="MaxRegions"/> so the manifest can
/// never exceed its budget; the bound covers roughly 900 km of trail and
/// evicts whole regions oldest-first — far beyond any real play session,
/// and documented honestly rather than silently.
/// </summary>
public sealed class ExplorationRecord
{
    /// <summary>Chunks per region side (64×64 chunks = 1536 m squares).</summary>
    public const int RegionChunks = 64;

    /// <summary>Regions whose bits are stored: ~432 KB of manifest, about
    /// 900 km of straight-line trail — effectively unbounded for play.</summary>
    public const int MaxRegions = 600;

    private const int RegionBytes = RegionChunks * RegionChunks / 8; // 512

    private readonly Dictionary<(long X, long Z), ulong[]> _regions = new();
    private readonly Queue<(long X, long Z)> _regionArrival = new();
    private long _visitedBits;

    /// <summary>Total explored chunks.</summary>
    public long Count => _visitedBits;

    /// <summary>Number of stored regions.</summary>
    public int RegionCount => _regions.Count;

    public bool Contains(ChunkCoordinate coordinate)
    {
        if (!_regions.TryGetValue(RegionOf(coordinate), out var bits)) return false;
        var (localX, localZ) = LocalOf(coordinate);
        return (bits[localZ] & (1UL << localX)) != 0;
    }

    /// <summary>Record one chunk. Returns true only for a first visit.</summary>
    public bool Visit(ChunkCoordinate coordinate)
    {
        var key = RegionOf(coordinate);
        if (!_regions.TryGetValue(key, out var bits))
        {
            bits = new ulong[RegionChunks];
            _regions[key] = bits;
            _regionArrival.Enqueue(key);
            // The bound protects the manifest budget. It is a whole-region
            // bound reached only after ~900 km of trail; play never gets
            // here, but a malicious or scripted save could, so the oldest
            // region is retired rather than the save being silently grown.
            while (_regions.Count > MaxRegions && _regionArrival.Count > 0)
            {
                var oldest = _regionArrival.Dequeue();
                if (!_regions.TryGetValue(oldest, out var oldBits)) continue;
                _visitedBits -= CountBits(oldBits);
                _regions.Remove(oldest);
            }
        }
        var (localX, localZ) = LocalOf(coordinate);
        var mask = 1UL << localX;
        if ((bits[localZ] & mask) != 0) return false;
        bits[localZ] |= mask;
        _visitedBits++;
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
        _regions.Clear();
        _regionArrival.Clear();
        _visitedBits = 0;
    }

    /// <summary>Import the legacy id-list form (pre-H5 manifests). Invalid
    /// IDs are ignored.</summary>
    public void Restore(IEnumerable<string>? ids)
    {
        Clear();
        if (ids is null) return;
        foreach (var id in ids)
            if (ChunkCoordinate.TryParse(id ?? "", out var coordinate))
                Visit(coordinate);
    }

    /// <summary>Restore the compact region form written since H5.</summary>
    public void RestoreRegions(IEnumerable<ExplorationRegionSave>? regions)
    {
        Clear();
        if (regions is null) return;
        foreach (var region in regions)
        {
            if (region is null) continue;
            var bits = DecodeRegion(region.Bits);
            if (bits is null) continue;
            var key = (region.RegionX, region.RegionZ);
            if (_regions.ContainsKey(key)) continue;
            _regions[key] = bits;
            _regionArrival.Enqueue(key);
            _visitedBits += CountBits(bits);
        }
    }

    /// <summary>The compact regions for the world manifest, region-sorted
    /// so the payload is byte-stable for identical histories.</summary>
    public List<ExplorationRegionSave> SnapshotRegions()
    {
        var saves = new List<ExplorationRegionSave>();
        foreach (var key in _regions.Keys.OrderBy(key => key.X).ThenBy(key => key.Z))
        {
            var bits = _regions[key];
            var bytes = new byte[RegionBytes];
            for (var row = 0; row < RegionChunks; row++)
            {
                var word = bits[row];
                for (var b = 0; b < 8; b++)
                    bytes[row * 8 + b] = (byte)((word >> (8 * b)) & 0xFF);
            }
            saves.Add(new ExplorationRegionSave
            {
                RegionX = key.X,
                RegionZ = key.Z,
                Bits = Convert.ToBase64String(bytes)
            });
        }
        return saves;
    }

    /// <summary>Iterate the explored chunks of one region row band, used by
    /// the map to draw a window without enumerating the whole world.</summary>
    public IEnumerable<ChunkCoordinate> ChunksInView(long minChunkX, long minChunkZ,
        long maxChunkX, long maxChunkZ)
    {
        var minRX = minChunkX >> 6;
        var maxRX = maxChunkX >> 6;
        var minRZ = minChunkZ >> 6;
        var maxRZ = maxChunkZ >> 6;
        for (var rx = minRX; rx <= maxRX; rx++)
        for (var rz = minRZ; rz <= maxRZ; rz++)
        {
            if (!_regions.TryGetValue((rx, rz), out var bits)) continue;
            for (var localZ = 0; localZ < RegionChunks; localZ++)
            {
                var word = bits[localZ];
                if (word == 0) continue;
                var z = (rz << 6) + localZ;
                if (z < minChunkZ || z > maxChunkZ) continue;
                for (var localX = 0; localX < RegionChunks; localX++)
                {
                    if ((word & (1UL << localX)) == 0) continue;
                    var x = (rx << 6) + localX;
                    if (x < minChunkX || x > maxChunkX) continue;
                    yield return new ChunkCoordinate(x, z);
                }
            }
        }
    }

    /// <summary>Legacy sorted stable IDs, kept for migration tests and any
    /// tooling that still reads the pre-H5 form.</summary>
    public List<string> ToIds()
    {
        var ids = new List<string>((int)Math.Min(int.MaxValue, _visitedBits));
        foreach (var key in _regions.Keys.OrderBy(key => key.X).ThenBy(key => key.Z))
        {
            var bits = _regions[key];
            for (var localZ = 0; localZ < RegionChunks; localZ++)
            {
                var word = bits[localZ];
                if (word == 0) continue;
                for (var localX = 0; localX < RegionChunks; localX++)
                    if ((word & (1UL << localX)) != 0)
                        ids.Add(new ChunkCoordinate((key.X << 6) + localX,
                            (key.Z << 6) + localZ).Id);
            }
        }
        return ids;
    }

    private static (long X, long Z) RegionOf(ChunkCoordinate coordinate) =>
        (coordinate.X >> 6, coordinate.Z >> 6);

    private static (int LocalX, int LocalZ) LocalOf(ChunkCoordinate coordinate) =>
        ((int)(coordinate.X & (RegionChunks - 1)), (int)(coordinate.Z & (RegionChunks - 1)));

    private static ulong[]? DecodeRegion(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { return null; }
        if (bytes.Length != RegionBytes) return null;
        var bits = new ulong[RegionChunks];
        for (var row = 0; row < RegionChunks; row++)
        {
            ulong word = 0;
            for (var b = 0; b < 8; b++)
                word |= (ulong)bytes[row * 8 + b] << (8 * b);
            bits[row] = word;
        }
        return bits;
    }

    private static long CountBits(ulong[] bits)
    {
        long count = 0;
        foreach (var word in bits)
            count += System.Numerics.BitOperations.PopCount(word);
        return count;
    }
}
