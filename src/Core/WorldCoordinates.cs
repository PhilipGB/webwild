namespace Webwild.Core;

/// <summary>
/// Stable logical world coordinate system for the never-ending world.
///
/// Absolute world positions are unbounded doubles in metres. Chunk coordinates
/// are signed 64-bit integers, so the world is effectively unlimited without
/// ever storing an indefinitely growing float in a save file. Rendering and
/// physics always happen in a bounded engine-local floating-point space that is
/// derived by subtracting a rebase origin (see <see cref="WorldOrigin"/>).
/// </summary>
public static class WorldChunk
{
    /// <summary>Chunk edge length in metres. One chunk covers [0, Size) on both axes.</summary>
    public const double Size = 24.0;

    /// <summary>
    /// Terrain grid points per chunk side, including both edges. Adjacent chunks
    /// share their boundary height, so neighbouring meshes cannot form seams.
    /// </summary>
    public const int TerrainResolution = 13;

    /// <summary>Distance between terrain grid points (2.0 m at Size = 24).</summary>
    public const double TerrainSpacing = Size / (TerrainResolution - 1);
}

/// <summary>Signed 64-bit chunk identity. X grows east, Z grows south.</summary>
public readonly record struct ChunkCoordinate(long X, long Z) : IComparable<ChunkCoordinate>
{
    /// <summary>Largest coordinate magnitude accepted in saves and navigation.</summary>
    public const long MaxCoordinate = 1L << 40;

    public static readonly ChunkCoordinate Zero = new(0, 0);

    /// <summary>Chunk containing an absolute world point. Floor semantics keep
    /// negative coordinates symmetric: (-0.1, z) is chunk X = -1.</summary>
    public static ChunkCoordinate OfWorldPoint(double x, double z) => new(
        (long)Math.Floor(x / WorldChunk.Size),
        (long)Math.Floor(z / WorldChunk.Size));

    public static ChunkCoordinate OfWorldPoint(Vec3 point) => OfWorldPoint(point.X, point.Z);

    public ChunkCoordinate Offset(long dx, long dz) => new(X + dx, Z + dz);

    /// <summary>Chebyshev distance in chunks; the streaming radius metric.</summary>
    public long ChebyshevDistance(ChunkCoordinate other)
    {
        var dx = Math.Abs(X - other.X);
        var dz = Math.Abs(Z - other.Z);
        return Math.Max(dx, dz);
    }

    public Vec3 OriginWorld => new(X * WorldChunk.Size, 0, Z * WorldChunk.Size);

    public bool IsValid =>
        ChunkCoordinateExtensions.TryValidate(X) &&
        ChunkCoordinateExtensions.TryValidate(Z);

    /// <summary>Canonical, culture-stable identity for save files and maps.</summary>
    public string Id => $"{X}_{Z}";

    public int CompareTo(ChunkCoordinate other)
    {
        var byX = X.CompareTo(other.X);
        return byX != 0 ? byX : Z.CompareTo(other.Z);
    }

    public static bool TryParse(string text, out ChunkCoordinate coordinate)
    {
        coordinate = Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        if (!long.TryParse(parts[0], out var x) || !long.TryParse(parts[1], out var z)) return false;
        coordinate = new ChunkCoordinate(x, z);
        return coordinate.IsValid;
    }
}

/// <summary>
/// A world position stored as an integer chunk identity plus local doubles.
/// Local X/Z always lie in [0, ChunkSize); Y is an absolute height because it is
/// bounded in practice. Saving this shape means position precision can never
/// decay as the player explores far away.
/// </summary>
public readonly record struct WorldPos(long ChunkX, long ChunkZ, double LocalX, double Y, double LocalZ)
{
    public static WorldPos FromAbsolute(double x, double y, double z)
    {
        if (!IsFinite3(x, y, z)) return default;
        var chunk = ChunkCoordinate.OfWorldPoint(x, z);
        var localX = x - chunk.X * WorldChunk.Size;
        var localZ = z - chunk.Z * WorldChunk.Size;
        // Guard against rounding pushing locals marginally out of range.
        if (localX >= WorldChunk.Size) { localX = 0; chunk = chunk.Offset(1, 0); }
        if (localX < 0) { localX = WorldChunk.Size - double.Epsilon; chunk = chunk.Offset(-1, 0); }
        if (localZ >= WorldChunk.Size) { localZ = 0; chunk = chunk.Offset(0, 1); }
        if (localZ < 0) { localZ = WorldChunk.Size - double.Epsilon; chunk = chunk.Offset(0, -1); }
        return new WorldPos(chunk.X, chunk.Z, localX, y, localZ);
    }

    public static WorldPos FromAbsolute(Vec3 point) => FromAbsolute(point.X, point.Y, point.Z);

    public Vec3 Absolute => new(
        ChunkX * WorldChunk.Size + LocalX,
        Y,
        ChunkZ * WorldChunk.Size + LocalZ);

    public ChunkCoordinate Chunk => new(ChunkX, ChunkZ);

    public bool IsValid =>
        ChunkCoordinateExtensions.TryValidate(ChunkX) && ChunkCoordinateExtensions.TryValidate(ChunkZ) &&
        IsFinite3(LocalX, Y, LocalZ) &&
        LocalX >= 0 && LocalX < WorldChunk.Size &&
        LocalZ >= 0 && LocalZ < WorldChunk.Size &&
        Y is >= -16 and <= 512;

    private static bool IsFinite3(double a, double b, double c) =>
        double.IsFinite(a) && double.IsFinite(b) && double.IsFinite(c);
}

/// <summary>
/// Origin rebasing contract. Engine (float) coordinates are always
/// logical − origin, so a far-away location keeps full logical precision while
/// engine floats stay small. Adapters apply this when translating between the
/// simulation and Godot nodes.
/// </summary>
public readonly record struct WorldOrigin(Vec3 Absolute)
{
    public static WorldOrigin AtZero => new(Vec3.Zero);

    public Vec3 ToEngineSpace(Vec3 logical) => logical - Absolute;

    public Vec3 ToLogical(Vec3 engineSpace) => engineSpace + Absolute;

    /// <summary>Choose a new origin close to the anchor so engine-space
    /// coordinates stay within a comfortable float-precision band.</summary>
    public static WorldOrigin Near(Vec3 anchor) => new(new Vec3(
        RebaseCoordinate(anchor.X), anchor.Y, RebaseCoordinate(anchor.Z)));

    private static double RebaseCoordinate(double value)
    {
        // Snap to 64 m steps: preserves sub-metre precision while making the
        // rebase value itself representable in float32 for simple comparison.
        var snapped = Math.Floor(value / 64.0) * 64.0;
        return double.IsFinite(snapped) ? snapped : 0;
    }

    /// <summary>True when engine-space coordinates would lose more than about
    /// 1 mm of precision at 32-bit float, i.e. |value| beyond ~16k.</summary>
    public bool NeedsRebase(Vec3 logical, double engineLimit = 16_384)
    {
        var engine = ToEngineSpace(logical);
        return Math.Abs(engine.X) > engineLimit || Math.Abs(engine.Z) > engineLimit;
    }
}

public static class ChunkCoordinateExtensions
{
    /// <summary>Chunk-coordinate range check shared by records and saves.</summary>
    public static bool TryValidate(long value) => value is >= -ChunkCoordinate.MaxCoordinate and <= ChunkCoordinate.MaxCoordinate;
}
