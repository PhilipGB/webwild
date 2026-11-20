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
    /// <summary>Current world-generation algorithm. Version 1 is the
    /// v0.3.0/v0.3.1 meadow/moss/bloom world; version 2 adds the eight
    /// environments with cliffs. Saves carry the version their terrain was
    /// generated with, so loading an old garden regenerates byte-identical
    /// terrain instead of silently rewriting it.</summary>
    public const int CurrentGeneratorVersion = 2;

    private readonly TerrainSampler _sampler;
    private readonly BiomeMap _biomes;
    private readonly EnvironmentMap? _environments;
    private readonly RiverMap _rivers;
    private readonly int _seed;
    private readonly int _version;

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

    /// <summary>Seed of this world; drives terrain, biomes and props.</summary>
    public int Seed => _seed;

    /// <summary>Generation algorithm version this generator produces.</summary>
    public int GeneratorVersion => _version;

    public WorldGenerator(int seed, int generatorVersion = 1)
    {
        _seed = seed;
        _version = Math.Clamp(generatorVersion, 1, CurrentGeneratorVersion);
        _sampler = new TerrainSampler(seed);
        _biomes = new BiomeMap(seed);
        _rivers = new RiverMap(seed, _version);
        _environments = _version >= 2 ? new EnvironmentMap(seed, _rivers) : null;
    }

    /// <summary>Deterministic river field for water, carving and crossings.</summary>
    public RiverMap Rivers => _rivers;

    /// <summary>The version-2 environment field, or null on version-1
    /// worlds (which keep their meadow/moss/bloom terrain).</summary>
    public EnvironmentMap? Environments => _environments;

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
        var raw = BaseHeightAt(x, z);
        var t = 1.0;
        foreach (var rect in AuthoredRects)
        {
            var outside = rect.DistanceOutside(x, z);
            if (outside <= 0) return AuthoredGroundHeight;
            if (outside >= AuthoredBlendMetres) continue;
            t = Math.Min(t, Smooth(outside / AuthoredBlendMetres));
        }
        var height = AuthoredGroundHeight + (raw - AuthoredGroundHeight) * t;
        // Rivers carve generated land only; authored footprints are never
        // touched (their ground is supplied by the hand-made scenes).
        if (t < 1.0) return height;
        return height - _rivers.CarveDepth(x, z);
    }

    /// <summary>Nearest river sample, excluding authored landmark ground.</summary>
    public RiverSample RiverAt(double x, double z)
    {
        if (AuthoredRects.Any(rect => rect.Contains(x, z))) return RiverSample.None;
        return _rivers.Sample(x, z);
    }

    /// <summary>Dominant environment or biome name at a world point
    /// (diagnostics, probes and the zone banner).</summary>
    public string BiomeNameAt(double x, double z)
    {
        if (_environments is { } environments)
            return EnvironmentMap.KindName(environments.WeightsAt(x, z).Dominant);
        return _biomes.WeightsAt(x, z).Dominant switch
        {
            BiomeKind.Moss => "moss",
            BiomeKind.Bloom => "bloom",
            _ => "meadow"
        };
    }

    /// <summary>Weight of one named environment (version 2) or biome
    /// (version 1) at a world point in [0, 1].</summary>
    public double BiomeWeightAt(double x, double z, string biome)
    {
        if (_environments is { } environments)
        {
            var weights = environments.WeightsAt(x, z);
            return biome switch
            {
                "forest" => weights.Forest,
                "woodland" => weights.Woodland,
                "wetland" => weights.Wetland,
                "shore" => weights.Shore,
                "hills" => weights.Hills,
                "cliff" => weights.Cliff,
                "valley" => weights.Valley,
                _ => weights.Meadow
            };
        }
        var legacy = _biomes.WeightsAt(x, z);
        return biome switch
        {
            "moss" => legacy.Moss,
            "bloom" => legacy.Bloom,
            "meadow" => legacy.Meadow,
            _ => 0
        };
    }

    /// <summary>Noise plus environment/biome height before landmark
    /// blending. Exposed for tests and for tools that need the generated
    /// baseline at a point.</summary>
    public double BaseHeightAt(double x, double z)
    {
        if (_environments is { } environments)
        {
            var weights = environments.WeightsAt(x, z);
            return _sampler.HeightAt(x, z) * EnvironmentMap.Amplitude(weights) +
                   EnvironmentMap.HeightOffset(weights) +
                   environments.CliffFacesAt(x, z, weights.Cliff);
        }
        var biome = _biomes.WeightsAt(x, z);
        return _sampler.HeightAt(x, z) + BiomeMap.HeightOffset(biome);
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
        var biomes = new BiomeWeights[resolution, resolution];
        EnvironmentWeights[,]? environments = _environments is null ? null
            : new EnvironmentWeights[resolution, resolution];
        var originX = coordinate.X * WorldChunk.Size;
        var originZ = coordinate.Z * WorldChunk.Size;
        for (var ix = 0; ix < resolution; ix++)
        for (var iz = 0; iz < resolution; iz++)
        {
            var x = originX + ix * WorldChunk.TerrainSpacing;
            var z = originZ + iz * WorldChunk.TerrainSpacing;
            heights[ix, iz] = HeightAt(x, z);
            biomes[ix, iz] = _biomes.WeightsAt(x, z);
            if (environments is not null)
                environments[ix, iz] = AuthoredEdgeEnvironment(x, z);
        }

        return new ChunkData
        {
            Coordinate = coordinate,
            Heights = heights,
            Biomes = biomes,
            Environments = environments,
            Props = GenerateProps(coordinate, originX, originZ, heights),
            WaterQuads = _rivers.WaterQuadsForChunk(coordinate),
            Bridges = _rivers.BridgesForChunk(coordinate, this),
            FoodSources = GenerateFoodSources(coordinate)
        };
    }

    /// <summary>Environment weights with a forced meadow margin around the
    /// authored landmarks, so the hand-made scenes always meet familiar
    /// grass instead of rock or sand at their blend ring. The strongest
    /// landmark influence wins, so a point between two landmarks is meadow
    /// whenever either of them is close.</summary>
    /// <summary>Environment weights at a world point including the authored
    /// meadow margin — the same value the chunk grid stores.</summary>
    public EnvironmentWeights EnvironmentAt(double x, double z) =>
        _environments is null
            ? EnvironmentWeights.PureMeadow
            : AuthoredEdgeEnvironment(x, z);

    private EnvironmentWeights AuthoredEdgeEnvironment(double x, double z)
    {
        var closest = double.PositiveInfinity;
        foreach (var rect in AuthoredRects)
            closest = Math.Min(closest, rect.DistanceOutside(x, z));
        var field = _environments!.WeightsAt(x, z);
        if (closest > AuthoredBlendMetres + 4)
            return field;
        if (closest <= AuthoredBlendMetres + 1)
            return EnvironmentWeights.PureMeadow;
        var t = Smooth((closest - AuthoredBlendMetres - 1) / 3.0);
        return BlendTowardsMeadow(field, t);
    }

    private static EnvironmentWeights BlendTowardsMeadow(EnvironmentWeights weights, double t)
    {
        if (t >= 1) return weights;
        return new EnvironmentWeights(
            weights.Meadow + (1 - weights.Meadow) * t,
            weights.Forest * (1 - t), weights.Woodland * (1 - t),
            weights.Wetland * (1 - t), weights.Shore * (1 - t),
            weights.Hills * (1 - t), weights.Cliff * (1 - t),
            weights.Valley * (1 - t));
    }

    /// <summary>
    /// Deterministic wild food: a few berries, mushrooms or seed heads on dry
    /// generated land. IDs are stable across unload/reload and carry the owning
    /// chunk, so state can be stored in that chunk's small change record.
    /// </summary>
    /// <summary>Resolve one deterministic food definition from its stable id,
    /// including chunks that are not currently loaded by the presentation.</summary>
    public FoodSourceDefinition? FoodSourceById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var parts = id.Split(':');
        if (parts.Length < 4 || parts[0] != "food") return null;
        if (!ChunkCoordinate.TryParse(parts[1], out var coordinate)) return null;
        var data = GenerateChunk(coordinate);
        return data?.FoodSources.FirstOrDefault(source => source.Id == id);
    }

    public List<FoodSourceDefinition> GenerateFoodSources(ChunkCoordinate coordinate)
    {
        var sources = new List<FoodSourceDefinition>();
        if (IsAuthoredChunk(coordinate)) return sources;
        var originX = coordinate.X * WorldChunk.Size;
        var originZ = coordinate.Z * WorldChunk.Size;
        const int slots = 3;
        for (var slot = 0; slot < slots; slot++)
        {
            var stream = DeterministicHash.RandomFor(_seed, coordinate.X, coordinate.Z, 100 + slot);
            if (stream.NextDouble() < 0.42) continue;
            var worldX = originX + 2.0 + stream.NextDouble() * (WorldChunk.Size - 4.0);
            var worldZ = originZ + 2.0 + stream.NextDouble() * (WorldChunk.Size - 4.0);
            var river = RiverAt(worldX, worldZ);
            if (river.HasRiver && river.Distance < river.HalfWidth + 1.5) continue;

            var biome = _biomes.WeightsAt(worldX, worldZ);
            var roll = stream.NextDouble();
            string kind;
            int amount;
            if (_environments is { } environments)
            {
                var environment = environments.WeightsAt(worldX, worldZ);
                kind = environment.Dominant switch
                {
                    EnvironmentKind.Forest => roll < 0.66 ? "mushrooms" : "berries",
                    EnvironmentKind.Woodland => roll < 0.60 ? "berries" : "mushrooms",
                    EnvironmentKind.Wetland => roll < 0.70 ? "seeds" : "mushrooms",
                    EnvironmentKind.Shore => roll < 0.62 ? "seeds" : "berries",
                    EnvironmentKind.Hills => roll < 0.58 ? "berries" : "seeds",
                    EnvironmentKind.Cliff => roll < 0.66 ? "seeds" : "berries",
                    EnvironmentKind.Valley => roll < 0.55 ? "berries" : "seeds",
                    _ => roll < 0.55 ? "seeds" : "berries"
                };
            }
            else
            {
                kind = biome.Dominant switch
                {
                    BiomeKind.Moss => roll < 0.62 ? "mushrooms" : "berries",
                    BiomeKind.Bloom => roll < 0.70 ? "berries" : "seeds",
                    _ => roll < 0.55 ? "seeds" : "berries"
                };
            }
            var label = kind switch
            {
                "mushrooms" => "mushrooms",
                "seeds" => "sunflower seeds",
                _ => "berries"
            };
            amount = kind == "berries" ? 2 : 1;
            sources.Add(new FoodSourceDefinition(
                $"food:{coordinate.Id}:{slot}:{kind}",
                coordinate, kind, label, amount,
                new Vec3(worldX, HeightAt(worldX, worldZ) + 0.02, worldZ)));
        }
        sources.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return sources;
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

        const int slots = 40;
        for (var slot = 0; slot < slots; slot++)
        {
            var stream = DeterministicHash.RandomFor(_seed, coordinate.X, coordinate.Z, slot);
            var localX = stream.NextDouble() * (WorldChunk.Size - 2.0) + 1.0;
            var localZ = stream.NextDouble() * (WorldChunk.Size - 2.0) + 1.0;
            var worldX = originX + localX;
            var worldZ = originZ + localZ;

            // A broad density mask creates open lawns and thicker patches.
            var density = _sampler.Noise(worldX, worldZ, 31, 3);
            // Dense woodland feels fuller; rocky and wet kinds stay sparse.
            var densityFloor = _environments is null
                ? 0.16
                : DensityFloorOf(_environments.WeightsAt(worldX, worldZ).Dominant);
            if (density < densityFloor) continue;

            // No dressing in the channel: the carved bed is visible through
            // the water and props must not float on it.
            if (_rivers.Sample(worldX, worldZ).IsWater) continue;
            var kind = _environments is { } environments
                ? PickEnvironmentKind(stream, environments.WeightsAt(worldX, worldZ))
                : PickKind(stream, _biomes.WeightsAt(worldX, worldZ));
            if (kind is null) continue;
            // Cliffs dress only their flat shelves; a prop pinned to a steep
            // face would hang off the rock.
            if (_environments is not null && SlopeTooSteepForProps(worldX, worldZ))
                continue;
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

    private bool SlopeTooSteepForProps(double x, double z)
    {
        const double step = 0.5;
        var h = HeightAt(x, z);
        var dx = HeightAt(x + step, z) - HeightAt(x - step, z);
        var dz = HeightAt(x, z + step) - HeightAt(x, z - step);
        return Math.Sqrt(dx * dx + dz * dz) / (2 * step) > 0.8;
    }

    private static double DensityFloorOf(EnvironmentKind kind) => kind switch
    {
        EnvironmentKind.Forest => 0.04,
        EnvironmentKind.Woodland => 0.10,
        EnvironmentKind.Wetland => 0.12,
        EnvironmentKind.Shore => 0.14,
        EnvironmentKind.Hills => 0.22,
        EnvironmentKind.Cliff => 0.34,
        EnvironmentKind.Valley => 0.10,
        _ => 0.16
    };

    /// <summary>Version-2 dressing: each environment picks from its own
    /// prop vocabulary, using only the supplied in-repo asset packs.</summary>
    private static string? PickEnvironmentKind(Random stream, EnvironmentWeights weights)
    {
        var roll = stream.NextDouble();
        switch (weights.Dominant)
        {
            case EnvironmentKind.Forest:
                if (roll < 0.16) return "fern";
                if (roll < 0.30) return "leaf_pile";
                if (roll < 0.42) return "fallen_leaf";
                if (roll < 0.54) return "mushroom";
                if (roll < 0.64) return "mushroom_blue";
                if (roll < 0.76) return "broad_leaf";
                if (roll < 0.88) return "sapling";
                return "bush";
            case EnvironmentKind.Woodland:
                if (roll < 0.22) return "bush";
                if (roll < 0.36) return "grass";
                if (roll < 0.50) return "fern";
                if (roll < 0.62) return "clover";
                if (roll < 0.74) return "clover_tall";
                if (roll < 0.86) return "sapling";
                return "tallgrass";
            case EnvironmentKind.Wetland:
                if (roll < 0.40) return "reed";
                if (roll < 0.72) return "marsh_grass";
                if (roll < 0.90) return "cattail";
                return "mushroom_blue";
            case EnvironmentKind.Shore:
                if (roll < 0.28) return "pebble";
                if (roll < 0.48) return "pebble_cluster";
                if (roll < 0.66) return "flat_stone";
                if (roll < 0.80) return "soil_mound";
                return "small_log";
            case EnvironmentKind.Hills:
                if (roll < 0.34) return "boulder";
                if (roll < 0.58) return "moss_rock";
                if (roll < 0.76) return "pebble_cluster";
                if (roll < 0.90) return "grass";
                return "soil_mound";
            case EnvironmentKind.Cliff:
                if (roll < 0.44) return "moss_rock";
                if (roll < 0.80) return "boulder";
                return "pebble_cluster";
            case EnvironmentKind.Valley:
                if (roll < 0.26) return "clover";
                if (roll < 0.42) return "daisy";
                if (roll < 0.58) return "grass";
                if (roll < 0.72)
                {
                    var flower = stream.Next(3);
                    return flower switch
                    {
                        0 => "flower_yellow",
                        1 => "flower_pink",
                        _ => "flower_purple"
                    };
                }
                if (roll < 0.86) return "clover_tall";
                return "fern";
            default:
                // Open meadow keeps the authored garden's balanced dressing.
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
    }

    private static string? PickKind(Random stream, BiomeWeights biome)
    {
        var roll = stream.NextDouble();
        switch (biome.Dominant)
        {
            case BiomeKind.Moss:
                if (roll < 0.30) return "fern";
                if (roll < 0.48) return "moss_patch";
                if (roll < 0.60) return "mushroom";
                if (roll < 0.72) return "clover";
                if (roll < 0.86) return "grass";
                if (roll < 0.94) return "flat_stone";
                return "small_log";
            case BiomeKind.Bloom:
                if (roll < 0.42)
                {
                    return stream.Next(3) switch
                    {
                        0 => "flower_yellow",
                        1 => "flower_pink",
                        _ => "flower_purple"
                    };
                }
                if (roll < 0.60) return "tallgrass";
                if (roll < 0.72) return "grass";
                if (roll < 0.84) return "clover";
                if (roll < 0.91) return "fern";
                if (roll < 0.97) return "pebble";
                return "mushroom";
            default:
                // Open meadow keeps the authored garden's balanced dressing.
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
    }

    /// <summary>Sink props slightly so bases meet the ground without floating.</summary>
    private static double PropSink(string kind) => kind switch
    {
        "pebble" or "flat_stone" or "small_log" or "moss_patch" or "boulder" or
        "moss_rock" or "pebble_cluster" or "soil_mound" or "cattail" => -0.05,
        _ => -0.03
    };

    private static double PropScale(string kind, Random stream)
    {
        var baseScale = kind switch
        {
            "grass" => 0.9,
            "tallgrass" => 1.0,
            "clover" => 0.85,
            "clover_tall" => 1.05,
            "fern" => 0.8,
            "pebble" => 0.9,
            "pebble_cluster" => 1.1,
            "flat_stone" => 1.1,
            "mushroom" => 0.85,
            "mushroom_blue" => 0.9,
            "moss_patch" => 1.2,
            "small_log" => 0.8,
            "boulder" => 1.15,
            "moss_rock" => 1.05,
            "soil_mound" => 1.0,
            "bush" => 1.1,
            "broad_leaf" => 0.95,
            "leaf_pile" => 1.0,
            "fallen_leaf" => 0.85,
            "daisy" => 0.9,
            "reed" => 1.1,
            "marsh_grass" => 1.0,
            "cattail" => 1.2,
            "sapling" => 1.25,
            _ => 0.9
        };
        return baseScale * (0.8 + stream.NextDouble() * 0.5);
    }
}
