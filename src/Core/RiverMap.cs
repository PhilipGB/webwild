namespace Webwild.Core;

/// <summary>
/// One procedural river sample at an absolute world point.
///
/// Rivers are broad, gently meandering east-west bands. The nearest band is
/// selected by index, so every query is local and O(1): the same point always
/// resolves to the same river regardless of chunk load order, exploration
/// history or reload.
/// </summary>
public readonly record struct RiverSample(
    bool HasRiver,
    int Index,
    double CenterZ,
    double HalfWidth,
    double WaterLevel,
    double Distance)
{
    public static readonly RiverSample None = new(false, 0, 0, 0, 0, double.PositiveInfinity);

    /// <summary>True when the point lies over the visual water surface.</summary>
    public bool IsWater => HasRiver && Distance <= HalfWidth + RiverMap.VisualMargin;

    /// <summary>True when the point is deep enough to be a real hazard.</summary>
    public bool IsHazard => HasRiver && Distance <= HalfWidth - RiverMap.HazardInset;
}

/// <summary>
/// One 2 m river-water quad. Each corner owns its level because a stream
/// follows the terrain downhill; a single flat level would flood a low bank.
/// </summary>
public readonly record struct RiverWaterQuad(
    double CenterX, double CenterZ,
    double Level00, double Level10, double Level01, double Level11,
    int RiverIndex);

/// <summary>
/// A generated crossing where an old low bridge spans one river. Crossings are
/// stable functions of the world seed: the same crossing regenerates with the
/// same identity and geometry after any unload/reload.
/// </summary>
public sealed record RiverBridge(
    string Id,
    int RiverIndex,
    int CrossingIndex,
    double X,
    double CenterZ,
    double HalfWidth,
    double WaterLevel,
    double HalfSpan,
    double NorthGroundY,
    double SouthGroundY)
{
    public bool Equals(RiverBridge? other) =>
        other is not null && Id == other.Id &&
        X.Equals(other.X) && CenterZ.Equals(other.CenterZ) &&
        HalfSpan.Equals(other.HalfSpan) &&
        NorthGroundY.Equals(other.NorthGroundY) &&
        SouthGroundY.Equals(other.SouthGroundY);

    public override int GetHashCode() =>
        HashCode.Combine(Id, X, CenterZ, HalfSpan, NorthGroundY, SouthGroundY);
}

/// <summary>
/// Deterministic river field for the never-ending world.
///
/// A river band is placed every <see cref="Spacing"/> metres in Z and meanders
/// gently with X. Terrain is carved into a shallow channel inside
/// <see cref="HalfWidth"/> and blends back to the bank over
/// <see cref="BankTaper"/> metres. Water follows the sampled bank height, so a
/// stream visibly descends with the landscape instead of forming an impossible
/// flat plane across hills.
///
/// Crossings repeat every <see cref="CrossingSpacing"/> metres along X. They are
/// modelled as low, solid footbridges: the crossing geometry is presentation
/// plus collision, while the river sample remains the single source of truth
/// for water and hazard decisions.
/// </summary>
public sealed class RiverMap
{
    public const double Spacing = 260.0;
    public const double Offset = 62.0;
    public const double BaseHalfWidth = 3.6;
    public const double BankTaper = 4.5;
    /// <summary>Depth at the visual water edge; the centre is deeper still.</summary>
    public const double EdgeDepth = 1.2;
    public const double CentreExtraDepth = 0.5;
    /// <summary>Water sits this far below the sampled bank height.</summary>
    public const double WaterDrop = 0.95;
    /// <summary>Water quads may extend this far past the nominal waterline.</summary>
    public const double VisualMargin = 0.15;
    /// <summary>Hazard begins this far inside the nominal waterline.</summary>
    public const double HazardInset = 0.2;
    /// <summary>Crossings repeat at these X intervals.</summary>
    public const double CrossingSpacing = 150.0;

    private readonly TerrainSampler _sampler;
    private readonly BiomeMap _biomes;
    private readonly int _seed;

    public RiverMap(int seed)
    {
        _seed = seed;
        _sampler = new TerrainSampler(seed);
        _biomes = new BiomeMap(seed);
    }

    public int Seed => _seed;

    /// <summary>River centreline Z for a band at a given X.</summary>
    public static double CenterAt(int index, double x) =>
        index * Spacing + Offset +
        18.0 * Math.Sin(x / 70.0 + index * 1.7) +
        7.0 * Math.Sin(x / 31.0 + index * 0.9);

    public static double HalfWidthAt(int index, double x) =>
        BaseHalfWidth + 0.5 * Math.Sin(x / 47.0 + index * 2.3);

    public double BaseHeightAt(double x, double z) =>
        _sampler.HeightAt(x, z) + BiomeMap.HeightOffset(_biomes.WeightsAt(x, z));

    /// <summary>Nearest river band to a world point.</summary>
    public RiverSample Sample(double x, double z)
    {
        var approximate = (int)Math.Round((z - Offset) / Spacing);
        RiverSample best = RiverSample.None;
        var bestDistance = double.PositiveInfinity;
        for (var index = approximate - 1; index <= approximate + 1; index++)
        {
            var center = CenterAt(index, x);
            var distance = Math.Abs(z - center);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            var halfWidth = HalfWidthAt(index, x);
            // Water follows the local ground, so the stream descends with the
            // landscape instead of flooding a lower bank.
            var level = BaseHeightAt(x, z) - WaterDrop;
            best = new RiverSample(true, index, center, halfWidth, level, distance);
        }
        return best;
    }

    /// <summary>Depth the terrain is lowered by at a point (0 outside a band).</summary>
    public double CarveDepth(double x, double z)
    {
        var sample = Sample(x, z);
        if (!sample.HasRiver) return 0;
        var d = sample.Distance;
        if (d <= sample.HalfWidth)
        {
            var t = sample.HalfWidth <= 1e-9 ? 0 : d / sample.HalfWidth;
            return EdgeDepth + CentreExtraDepth * (1 - t);
        }
        var outside = d - sample.HalfWidth;
        if (outside >= BankTaper) return 0;
        var blend = 1 - Smooth(outside / BankTaper);
        return EdgeDepth * blend;
    }

    /// <summary>
    /// Nearest dry bank point for save recovery. Phase-generated rivers can
    /// appear under a save written before that phase; loading must never strand
    /// the player in deep water.
    /// </summary>
    public Vec3 SafeBankNear(double x, double z)
    {
        var sample = Sample(x, z);
        if (!sample.HasRiver || !sample.IsHazard)
            return new Vec3(x, BaseHeightAt(x, z), z);
        var side = z >= sample.CenterZ ? 1.0 : -1.0;
        var safeZ = sample.CenterZ +
                    side * (sample.HalfWidth + BankTaper + 1.5);
        return new Vec3(x, BaseHeightAt(x, safeZ), safeZ);
    }

    /// <summary>
    /// One crossing by river and X index, independent of chunk ownership. Used
    /// by chunk generation and by the worker world navigator, so a journey
    /// crosses exactly the bridge the player sees.
    /// </summary>
    public RiverBridge? BridgeAt(int riverIndex, int crossingIndex, WorldGenerator generator)
    {
        var x = crossingIndex * CrossingSpacing;
        var centerZ = CenterAt(riverIndex, x);
        if (WorldGenerator.AuthoredRects.Any(rect =>
                rect.DistanceOutside(x, centerZ) < 24)) return null;
        var halfWidth = HalfWidthAt(riverIndex, x);
        var halfSpan = halfWidth + BankTaper + 0.8;
        var northZ = centerZ - halfSpan;
        var southZ = centerZ + halfSpan;
        var northY = generator.HeightAt(x, northZ);
        var southY = generator.HeightAt(x, southZ);
        var level = WaterLevelAt(riverIndex, x);
        return new RiverBridge(
            $"river_{riverIndex}_{crossingIndex}", riverIndex, crossingIndex, x, centerZ,
            halfWidth, level, halfSpan, northY, southY);
    }

    /// <summary>Top of a generated bridge deck at its centre (visual/collision
    /// formula shared with the Godot builder).</summary>
    public static double DeckTop(RiverBridge bridge) =>
        Math.Max((bridge.NorthGroundY + bridge.SouthGroundY) * 0.5 - 0.12,
            bridge.WaterLevel + 0.37) + 0.08;

    /// <summary>Water level at an X position for one river band.</summary>
    public double WaterLevelAt(int index, double x) =>
        BaseHeightAt(x, CenterAt(index, x)) - WaterDrop;

    /// <summary>
    /// Build the water quads owned by one chunk. Ownership is exact: a quad is
    /// emitted only when its centre lies inside this chunk's X/Z bounds, so
    /// neighbouring chunks never draw the same water twice and boundaries met
    /// by sampling the same deterministic field.
    /// </summary>
    public List<RiverWaterQuad> WaterQuadsForChunk(ChunkCoordinate coordinate)
    {
        var quads = new List<RiverWaterQuad>();
        var originX = coordinate.X * WorldChunk.Size;
        var originZ = coordinate.Z * WorldChunk.Size;
        for (var ix = 0; ix < WorldChunk.TerrainResolution - 1; ix++)
        for (var iz = 0; iz < WorldChunk.TerrainResolution - 1; iz++)
        {
            var x = originX + (ix + 0.5) * WorldChunk.TerrainSpacing;
            var z = originZ + (iz + 0.5) * WorldChunk.TerrainSpacing;
            if (WorldGenerator.AuthoredRects.Any(rect => rect.Contains(x, z))) continue;
            var sample = Sample(x, z);
            if (!sample.IsWater) continue;
            var half = WorldChunk.TerrainSpacing * 0.5;
            var l00 = Sample(x - half, z - half).WaterLevel;
            var l10 = Sample(x + half, z - half).WaterLevel;
            var l01 = Sample(x - half, z + half).WaterLevel;
            var l11 = Sample(x + half, z + half).WaterLevel;
            quads.Add(new RiverWaterQuad(x, z, l00, l10, l01, l11, sample.Index));
        }
        quads.Sort((a, b) =>
        {
            var byZ = a.CenterZ.CompareTo(b.CenterZ);
            return byZ != 0 ? byZ : a.CenterX.CompareTo(b.CenterX);
        });
        return quads;
    }

    /// <summary>
    /// Crossings whose bridge centre belongs to this chunk. Ownership by
    /// centre means exactly one chunk owns each bridge; the deck may extend a
    /// few metres into a neighbouring chunk, exactly as authored bridges do.
    /// </summary>
    public List<RiverBridge> BridgesForChunk(ChunkCoordinate coordinate, WorldGenerator generator)
    {
        var bridges = new List<RiverBridge>();
        var originX = coordinate.X * WorldChunk.Size;
        var originZ = coordinate.Z * WorldChunk.Size;
        var first = (long)Math.Floor(originX / CrossingSpacing);
        var last = (long)Math.Floor((originX + WorldChunk.Size - 0.001) / CrossingSpacing);
        for (var n = first; n <= last; n++)
        {
            var x = n * CrossingSpacing;
            if (x < originX || x >= originX + WorldChunk.Size) continue;
            // The crossing belongs to the river nearest the chunk centre in Z.
            var approximate = (int)Math.Round((originZ + WorldChunk.Size * 0.5 - Offset) / Spacing);
            var bestIndex = approximate;
            var bestDistance = double.PositiveInfinity;
            for (var k = approximate - 1; k <= approximate + 1; k++)
            {
                var distance = Math.Abs(originZ + WorldChunk.Size * 0.5 - CenterAt(k, x));
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestIndex = k;
            }
            var centerZ = CenterAt(bestIndex, x);
            var ownerZ = (long)Math.Floor(centerZ / WorldChunk.Size);
            if (ownerZ != coordinate.Z) continue;
            if (WorldGenerator.AuthoredRects.Any(rect =>
                    rect.DistanceOutside(x, centerZ) < 24)) continue;

            var bridge = BridgeAt(bestIndex, (int)n, generator);
            if (bridge is not null) bridges.Add(bridge);
        }
        return bridges;
    }

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
