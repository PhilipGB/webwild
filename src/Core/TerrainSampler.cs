namespace Webwild.Core;

/// <summary>
/// Deterministic 64-bit hashing and seeded value noise used by the world
/// generator. Every function is a pure function of its arguments: the same seed
/// and world position always produce the same value, independent of exploration
/// order, chunk load order, thread scheduling or save/reload.
/// </summary>
public static class DeterministicHash
{
    /// <summary>SplitMix64 finaliser. Fast, well-mixed, overflow is intended.</summary>
    public static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    public static ulong Combine(ulong a, ulong b)
    {
        var h = a ^ Mix(b + 0x9E3779B97F4A7C15UL + (b << 6) + (b >> 2));
        return Mix(h);
    }

    public static ulong Hash(double value) => Mix((ulong)BitConverter.DoubleToUInt64Bits(value));

    public static ulong Hash(long value) => Mix((ulong)value);

    public static ulong Hash(string value)
    {
        var h = 0x243F6A8885A308D3UL;
        foreach (var ch in value)
            h = Mix(h ^ ch);
        return Mix(h);
    }

    /// <summary>Map a hash to [0, 1) with 53 usable bits.</summary>
    public static double Unit(ulong hash) => (hash >> 11) * (1.0 / (1UL << 53));

    /// <summary>Seed a per-chunk or per-feature random stream from the world seed.</summary>
    public static Random RandomFor(long seed, params long[] coordinates)
    {
        var h = Hash(seed);
        foreach (var coordinate in coordinates) h = Combine(h, Hash(coordinate));
        return new Random(unchecked((int)h ^ (int)(h >> 32)));
    }
}

/// <summary>
/// Smooth seeded value noise sampled by absolute world coordinates. Sampling is
/// coordinate-based rather than chunk-based, so terrain is automatically
/// continuous across chunk boundaries and identical whenever regenerated.
/// </summary>
public sealed class TerrainSampler
{
    private readonly int _seed;

    public TerrainSampler(int seed) => _seed = seed;

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private double LatticeValue(int latticeX, int latticeZ, int octave)
    {
        var h = DeterministicHash.Hash(_seed);
        h = DeterministicHash.Combine(h, DeterministicHash.Hash((long)latticeX));
        h = DeterministicHash.Combine(h, DeterministicHash.Hash((long)latticeZ));
        h = DeterministicHash.Combine(h, DeterministicHash.Hash((long)octave));
        return DeterministicHash.Unit(h);
    }

    /// <summary>One octave of value noise at a given metre-scale frequency.</summary>
    public double Noise(double x, double z, double wavelengthMetres, int octave = 0)
    {
        if (wavelengthMetres <= 0) return 0.5;
        var fx = x / wavelengthMetres;
        var fz = z / wavelengthMetres;
        var x0 = (int)Math.Floor(fx);
        var z0 = (int)Math.Floor(fz);
        var tx = Smooth(fx - x0);
        var tz = Smooth(fz - z0);
        var v00 = LatticeValue(x0, z0, octave);
        var v10 = LatticeValue(x0 + 1, z0, octave);
        var v01 = LatticeValue(x0, z0 + 1, octave);
        var v11 = LatticeValue(x0 + 1, z0 + 1, octave);
        return (v00 * (1 - tx) + v10 * tx) * (1 - tz) + (v01 * (1 - tx) + v11 * tx) * tz;
    }

    /// <summary>
    /// Baseline terrain height in metres at an absolute world position.
    /// Layered octaves: broad hills, medium rolls and small roughness.
    /// Range is deliberately gentle for the initial grassland phase.
    /// </summary>
    public double HeightAt(double x, double z)
    {
        var broad = Noise(x, z, 96, 0) - 0.5;
        var medium = Noise(x, z, 27, 1) - 0.5;
        var rough = Noise(x, z, 8, 2) - 0.5;
        return broad * 5.2 + medium * 1.6 + rough * 0.45;
    }
}
