namespace Webwild.Core;

/// <summary>
/// Engine-free description of one generated chunk: its sampled terrain surface
/// and deterministic prop placements. This is the regeneratable baseline; only
/// player-made changes to a chunk are persisted (see <see cref="ChunkChangeSave"/>).
/// </summary>
public sealed class ChunkData
{
    public required ChunkCoordinate Coordinate { get; init; }

    /// <summary>Height at terrain grid point [x, z], both in [0, Resolution).</summary>
    public double[,] Heights { get; init; } = new double[0, 0];

    /// <summary>Deterministic props for this chunk, sorted stably.</summary>
    public IReadOnlyList<PropPlacement> Props { get; init; } = Array.Empty<PropPlacement>();

    /// <summary>
    /// Stable content hash: two chunks are interchangeable only when this value
    /// and the coordinate match. Used by streaming, tests and regeneration checks.
    /// </summary>
    public ulong ContentHash()
    {
        var h = DeterministicHash.Hash(Coordinate.X);
        h = DeterministicHash.Combine(h, DeterministicHash.Hash(Coordinate.Z));
        h = DeterministicHash.Combine(h, DeterministicHash.Hash(Heights.GetLength(0)));
        h = DeterministicHash.Combine(h, DeterministicHash.Hash(Heights.GetLength(1)));
        for (var x = 0; x < Heights.GetLength(0); x++)
        for (var z = 0; z < Heights.GetLength(1); z++)
            h = DeterministicHash.Combine(h, DeterministicHash.Hash(Heights[x, z]));
        foreach (var prop in Props)
        {
            h = DeterministicHash.Combine(h, DeterministicHash.Hash(prop.Kind));
            h = DeterministicHash.Combine(h, DeterministicHash.Hash(prop.Position.X));
            h = DeterministicHash.Combine(h, DeterministicHash.Hash(prop.Position.Y));
            h = DeterministicHash.Combine(h, DeterministicHash.Hash(prop.Position.Z));
            h = DeterministicHash.Combine(h, DeterministicHash.Hash(prop.RotationDegrees));
            h = DeterministicHash.Combine(h, DeterministicHash.Hash(prop.Scale));
        }
        return h;
    }

    /// <summary>Height at an absolute world position via bilinear sampling of the grid.</summary>
    public double HeightAtWorld(double worldX, double worldZ)
    {
        var localX = worldX - Coordinate.X * WorldChunk.Size;
        var localZ = worldZ - Coordinate.Z * WorldChunk.Size;
        var gx = localX / WorldChunk.TerrainSpacing;
        var gz = localZ / WorldChunk.TerrainSpacing;
        var ix = (int)Math.Clamp(Math.Floor(gx), 0, Heights.GetLength(0) - 2);
        var iz = (int)Math.Clamp(Math.Floor(gz), 0, Heights.GetLength(1) - 2);
        var tx = Math.Clamp(gx - ix, 0, 1);
        var tz = Math.Clamp(gz - iz, 0, 1);
        var h00 = Heights[ix, iz];
        var h10 = Heights[ix + 1, iz];
        var h01 = Heights[ix, iz + 1];
        var h11 = Heights[ix + 1, iz + 1];
        return (h00 * (1 - tx) + h10 * tx) * (1 - tz) + (h01 * (1 - tx) + h11 * tx) * tz;
    }
}

/// <summary>One deterministic decoration or gameplay prop inside a chunk.</summary>
public sealed record PropPlacement(string Kind, Vec3 Position, double RotationDegrees, double Scale);
