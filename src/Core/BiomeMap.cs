namespace Webwild.Core;

/// <summary>The three baseline biomes of the procedural meadow.</summary>
public enum BiomeKind
{
    Meadow,
    Moss,
    Bloom
}

/// <summary>
/// Continuous biome mix at one world position. Weights always sum to 1, so a
/// border is a soft gradient rather than a hard line: colour, height and prop
/// palettes can blend without special-casing.
/// </summary>
public readonly record struct BiomeWeights(double Meadow, double Moss, double Bloom)
{
    public static readonly BiomeWeights PureMeadow = new(1, 0, 0);

    public BiomeKind Dominant =>
        Moss >= Meadow && Moss >= Bloom ? BiomeKind.Moss :
        Bloom >= Meadow && Bloom >= Moss ? BiomeKind.Bloom : BiomeKind.Meadow;

    /// <summary>Blend the three weights with the given per-biome values.</summary>
    public double Blend(double meadow, double moss, double bloom) =>
        Meadow * meadow + Moss * moss + Bloom * bloom;
}

/// <summary>
/// Deterministic biome field. Two low-frequency noise channels select mossy
/// hollows and flowered rises; the remaining weight is open meadow. Sampling is
/// a pure function of (seed, absolute x, z), so biome borders always agree
/// across chunk edges and after any reload.
/// </summary>
public sealed class BiomeMap
{
    private readonly TerrainSampler _sampler;

    public BiomeMap(int seed) => _sampler = new TerrainSampler(seed);

    public BiomeWeights WeightsAt(double x, double z)
    {
        var mossNoise = _sampler.Noise(x, z, 190, 11);
        var bloomNoise = _sampler.Noise(x, z, 230, 12);
        var moss = SmoothStep(0.46, 0.60, mossNoise);
        var bloom = SmoothStep(0.48, 0.62, bloomNoise) * (1 - moss);
        var meadow = 1 - moss - bloom;
        if (meadow < 0)
        {
            var total = moss + bloom;
            moss /= total;
            bloom /= total;
            meadow = 0;
        }
        return new BiomeWeights(meadow, moss, bloom);
    }

    /// <summary>Height offset in metres: mossy ground sinks gently, flowered
    /// rises lift; the range is deliberately small so slopes stay walkable.</summary>
    public static double HeightOffset(BiomeWeights weights) =>
        weights.Blend(0, -0.55, 0.85);

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>
/// Shared terrain palette so procedural chunks, landmark aprons and any future
/// generated ground agree on colour. Values are linear-space RGB triples in the
/// same stylised range as the authored garden meadow.
/// </summary>
public static class TerrainPalette
{
    public static readonly Vec3 Meadow = new(0.466667, 0.658824, 0.352941);
    public static readonly Vec3 Moss = new(0.298039, 0.478431, 0.321569);
    public static readonly Vec3 Bloom = new(0.639216, 0.729412, 0.364706);

    public static Vec3 ColorOf(BiomeWeights weights) => new(
        weights.Blend(Meadow.X, Moss.X, Bloom.X),
        weights.Blend(Meadow.Y, Moss.Y, Bloom.Y),
        weights.Blend(Meadow.Z, Moss.Z, Bloom.Z));

    /// <summary>Deterministic per-quad brightness in [0.92, 1.08].</summary>
    public static double QuadTint(int seed, double worldX, double worldZ)
    {
        var qx = (long)Math.Floor(worldX / 2.0);
        var qz = (long)Math.Floor(worldZ / 2.0);
        var h = DeterministicHash.Combine(
            DeterministicHash.Hash((long)seed),
            DeterministicHash.Combine(DeterministicHash.Hash(qx), DeterministicHash.Hash(qz)));
        return 0.92 + DeterministicHash.Unit(h) * 0.16;
    }
}
