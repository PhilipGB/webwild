namespace Webwild.Core;

/// <summary>
/// Deterministic procedural terrain generator for the never-ending world.
///
/// Every output is a pure function of (seed, chunk coordinate): the same chunk
/// regenerates identically regardless of exploration order, load order or
/// process lifetime. Sampling is coordinate-based, so neighbouring chunks agree
/// at shared boundaries without special-casing.
///
/// P1 baseline: a single grassland terrain type. The authored landmarks each
/// occupy a fixed footprint of chunks (<see cref="AuthoredFootprint"/>); terrain
/// there is never generated, and heights blend smoothly from the authored
/// ground level (y = 0) out into gentle rolling grassland. Two landmarks exist
/// from v0.3.0 P2: the Garden Clearing at the origin and Reedwater Bank at
/// (96, 0), with procedural meadow connecting them.
/// </summary>
public sealed class WorldGenerator
{
    private readonly TerrainSampler _sampler;
    private readonly int _seed;

    /// <summary>Chunks covered by the authored landscape. The authored scenes
    /// supply ground here; the generator skips these chunks so the two
    /// surfaces can never overlap or fight.</summary>
    public static IReadOnlySet<ChunkCoordinate> AuthoredFootprint { get; } =
        FootprintOf(LandmarkCatalog.GardenFootprint, LandmarkCatalog.ReedwaterFootprint);

    /// <summary>World-space rectangle of the garden's authored footprint,
    /// chunk aligned.</summary>
    public const double AuthoredMinX = -1 * WorldChunk.Size;
    public const double AuthoredMaxX = 1 * WorldChunk.Size;
    public const double AuthoredMinZ = -1 * WorldChunk.Size;
    public const double AuthoredMaxZ = 1 * WorldChunk.Size;

    /// <summary>Chunk-aligned world rects owned by authored landmarks.</summary>
    public static readonly IReadOnlyList<WorldRect> AuthoredRects = new[]
    {
        LandmarkCatalog.GardenFootprint,
        LandmarkCatalog.ReedwaterFootprint
    };

    /// <summary>Width in metres of the blend ring outside each authored rect
    /// where generated heights ramp from the authored level into terrain noise.</summary>
    public const double AuthoredBlendMetres = 10.0;

    /// <summary>The ground height the authored terrain sits at.</summary>
    public const double AuthoredGroundHeight = 0.0;

    public WorldGenerator(int seed)
    {
        _seed = seed;
        _sampler = new TerrainSampler(seed);
    }

    private static HashSet<ChunkCoordinate> FootprintOf(params WorldRect[] rects)
    {
        var chunks = new HashSet<ChunkCoordinate>();
        foreach (var rect in rects)
        {
            var minX = (long)Math.Floor(rect.MinX / WorldChunk.Size);
            var maxX = (long)Math.Floor((rect.MaxX - 1e-9) / WorldChunk.Size);
            var minZ = (long)Math.Floor(rect.MinZ / WorldChunk.Size);
            var maxZ = (long)Math.Floor((rect.MaxZ - 1e-9) / WorldChunk.Size);
            for (var x = minX; x <= maxX; x++)
            for (var z = minZ; z <= maxZ; z++)
                chunks.Add(new ChunkCoordinate(x, z));
        }
        return chunks;
    }

    public static bool IsAuthoredChunk(ChunkCoordinate coordinate) =>
        AuthoredFootprint.Contains(coordinate);

    /// <summary>
    /// Terrain height at an absolute world position: authored level inside
    /// either landmark footprint, smoothstep blend across that footprint's ring,
    /// then pure noise. A point near two footprints uses the strongest
    /// authored influence (the smallest blend factor), so overlapping rings
    /// could never create a seam.
    /// </summary>
    public double HeightAt(double x, double z)
    {
        var raw = _sampler.HeightAt(x, z);
        var t = 1.0;
        foreach (var rect in AuthoredRects)
        {
            var outside = rect.DistanceOutside(x, z);
            if (outside <= 0) return AuthoredGroundHeight;
            if (outside >= AuthoredBlendMetres) continue;
            t = Math.Min(t, Smooth(outside / AuthoredBlendMetres));
        }
        return AuthoredGroundHeight + (raw - AuthoredGroundHeight) * t;
    }

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Generate one chunk: height grid plus deterministic props.
    /// Authored-footprint chunks are never generated (they are hand-made).</summary>
    public ChunkData? GenerateChunk(ChunkCoordinate coordinate)
    {
        if (IsAuthoredChunk(coordinate)) return null;

        var resolution = WorldChunk.TerrainResolution;
        var heights = new double[resolution, resolution];
        var originX = coordinate.X * WorldChunk.Size;
        var originZ = coordinate.Z * WorldChunk.Size;
        for (var ix = 0; ix < resolution; ix++)
        for (var iz = 0; iz < resolution; iz++)
            heights[ix, iz] = HeightAt(
                originX + ix * WorldChunk.TerrainSpacing,
                originZ + iz * WorldChunk.TerrainSpacing);

        return new ChunkData
        {
            Coordinate = coordinate,
            Heights = heights,
            Props = GenerateProps(coordinate, originX, originZ, heights)
        };
    }

    /// <summary>
    /// Deterministic prop placement: a fixed number of hash-driven candidate
    /// slots per chunk, filtered by kind weights and a decoration-density mask
    /// so meadows feel varied without becoming cluttered. Props are never
    /// placed inside the authored footprint rect.
    /// </summary>
    public List<PropPlacement> GenerateProps(
        ChunkCoordinate coordinate, double originX, double originZ, double[,] heights)
    {
        var props = new List<PropPlacement>();
        if (IsAuthoredChunk(coordinate)) return props;

        const int slots = 14;
        for (var slot = 0; slot < slots; slot++)
        {
            var stream = DeterministicHash.RandomFor(_seed, coordinate.X, coordinate.Z, slot);
            var localX = stream.NextDouble() * (WorldChunk.Size - 2.0) + 1.0;
            var localZ = stream.NextDouble() * (WorldChunk.Size - 2.0) + 1.0;
            var worldX = originX + localX;
            var worldZ = originZ + localZ;

            // A broad density mask creates open lawns and thicker patches.
            var density = _sampler.Noise(worldX, worldZ, 31, 3);
            if (density < 0.34) continue;

            var kind = PickKind(stream, worldX, worldZ);
            if (kind is null) continue;
            var position = new Vec3(
                worldX,
                HeightAt(worldX, worldZ) + PropSink(kind),
                worldZ);
            var rotation = stream.NextDouble() * 360.0;
            var scale = PropScale(kind, stream);
            props.Add(new PropPlacement(kind, position, rotation, scale));
        }
        // Canonical order: by kind then position, so the list is stable.
        props.Sort((a, b) =>
        {
            var byKind = string.CompareOrdinal(a.Kind, b.Kind);
            return byKind != 0
                ? byKind
                : a.Position.X != b.Position.X
                    ? a.Position.X.CompareTo(b.Position.X)
                    : a.Position.Z.CompareTo(b.Position.Z);
        });
        return props;
    }

    private string? PickKind(Random stream, double worldX, double worldZ)
    {
        var roll = stream.NextDouble();
        // Meadow dressing, matching the authored garden's flat-shaded flora.
        if (roll < 0.40) return "grass";
        if (roll < 0.58) return "clover";
        if (roll < 0.68) return "tallgrass";
        if (roll < 0.78)
        {
            var flower = stream.Next(3);
            return flower switch
            {
                0 => "flower_yellow",
                1 => "flower_pink",
                _ => "flower_purple"
            };
        }
        if (roll < 0.84) return "fern";
        if (roll < 0.90) return "pebble";
        if (roll < 0.94) return "flat_stone";
        if (roll < 0.97) return "mushroom";
        if (roll < 0.99) return "moss_patch";
        return "small_log";
    }

    /// <summary>Sink props slightly so bases meet the ground without floating.</summary>
    private static double PropSink(string kind) => kind switch
    {
        "pebble" or "flat_stone" or "small_log" or "moss_patch" => -0.05,
        _ => -0.03
    };

    private static double PropScale(string kind, Random stream)
    {
        var baseScale = kind switch
        {
            "grass" => 0.9,
            "tallgrass" => 1.0,
            "clover" => 0.85,
            "fern" => 0.8,
            "pebble" => 0.9,
            "flat_stone" => 1.1,
            "mushroom" => 0.85,
            "moss_patch" => 1.2,
            "small_log" => 0.8,
            _ => 0.9
        };
        return baseScale * (0.8 + stream.NextDouble() * 0.5);
    }
}
