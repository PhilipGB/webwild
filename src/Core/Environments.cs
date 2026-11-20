namespace Webwild.Core;

/// <summary>
/// The environments of generator-version-2 worlds. Version-1 worlds keep
/// their meadow/moss/bloom field untouched; every kind here is new terrain
/// that only exists when a save explicitly carries generator version 2.
/// </summary>
public enum EnvironmentKind
{
    Meadow,
    Forest,
    Woodland,
    Wetland,
    Shore,
    Hills,
    Cliff,
    Valley
}

/// <summary>Blended environment weights at one world point; sums to 1.</summary>
public readonly record struct EnvironmentWeights(
    double Meadow, double Forest, double Woodland, double Wetland,
    double Shore, double Hills, double Cliff, double Valley)
{
    public static readonly EnvironmentWeights PureMeadow = new(1, 0, 0, 0, 0, 0, 0, 0);

    public double Sum => Meadow + Forest + Woodland + Wetland +
                         Shore + Hills + Cliff + Valley;

    public EnvironmentKind Dominant
    {
        get
        {
            var best = Meadow;
            var kind = EnvironmentKind.Meadow;
            if (Forest > best) { best = Forest; kind = EnvironmentKind.Forest; }
            if (Woodland > best) { best = Woodland; kind = EnvironmentKind.Woodland; }
            if (Wetland > best) { best = Wetland; kind = EnvironmentKind.Wetland; }
            if (Shore > best) { best = Shore; kind = EnvironmentKind.Shore; }
            if (Hills > best) { best = Hills; kind = EnvironmentKind.Hills; }
            if (Cliff > best) { best = Cliff; kind = EnvironmentKind.Cliff; }
            if (Valley > best) { best = Valley; kind = EnvironmentKind.Valley; }
            return kind;
        }
    }

    public double WeightOf(EnvironmentKind kind) => kind switch
    {
        EnvironmentKind.Forest => Forest,
        EnvironmentKind.Woodland => Woodland,
        EnvironmentKind.Wetland => Wetland,
        EnvironmentKind.Shore => Shore,
        EnvironmentKind.Hills => Hills,
        EnvironmentKind.Cliff => Cliff,
        EnvironmentKind.Valley => Valley,
        _ => Meadow
    };

    public EnvironmentWeights WithWeight(EnvironmentKind kind, double weight) => kind switch
    {
        EnvironmentKind.Forest => new EnvironmentWeights(Meadow, weight, Woodland, Wetland, Shore, Hills, Cliff, Valley),
        EnvironmentKind.Woodland => new EnvironmentWeights(Meadow, Forest, weight, Wetland, Shore, Hills, Cliff, Valley),
        EnvironmentKind.Wetland => new EnvironmentWeights(Meadow, Forest, Woodland, weight, Shore, Hills, Cliff, Valley),
        EnvironmentKind.Shore => new EnvironmentWeights(Meadow, Forest, Woodland, Wetland, weight, Hills, Cliff, Valley),
        EnvironmentKind.Hills => new EnvironmentWeights(Meadow, Forest, Woodland, Wetland, Shore, weight, Cliff, Valley),
        EnvironmentKind.Cliff => new EnvironmentWeights(Meadow, Forest, Woodland, Wetland, Shore, Hills, weight, Valley),
        EnvironmentKind.Valley => new EnvironmentWeights(Meadow, Forest, Woodland, Wetland, Shore, Hills, Cliff, weight),
        _ => new EnvironmentWeights(weight, Forest, Woodland, Wetland, Shore, Hills, Cliff, Valley)
    };

    /// <summary>Blend one per-kind scalar (height offset, amplitude or a
    /// palette channel) across the weights.</summary>
    public double Blend(
        double meadow, double forest, double woodland, double wetland,
        double shore, double hills, double cliff, double valley) =>
        Meadow * meadow + Forest * forest + Woodland * woodland +
        Wetland * wetland + Shore * shore + Hills * hills +
        Cliff * cliff + Valley * valley;

    /// <summary>Averaged weights between four grid samples, used by the
    /// per-quad mesh colouring so environment boundaries blend smoothly.</summary>
    public static EnvironmentWeights Between(
        EnvironmentWeights a, EnvironmentWeights b,
        EnvironmentWeights c, EnvironmentWeights d) => new(
        (a.Meadow + b.Meadow + c.Meadow + d.Meadow) * 0.25,
        (a.Forest + b.Forest + c.Forest + d.Forest) * 0.25,
        (a.Woodland + b.Woodland + c.Woodland + d.Woodland) * 0.25,
        (a.Wetland + b.Wetland + c.Wetland + d.Wetland) * 0.25,
        (a.Shore + b.Shore + c.Shore + d.Shore) * 0.25,
        (a.Hills + b.Hills + c.Hills + d.Hills) * 0.25,
        (a.Cliff + b.Cliff + c.Cliff + d.Cliff) * 0.25,
        (a.Valley + b.Valley + c.Valley + d.Valley) * 0.25);
}

/// <summary>
/// The generator-version-2 environment field. Kinds come from a few broad,
/// smooth noise channels and a river-corridor override, so every point is a
/// pure function of (seed, x, z) and neighbouring points blend continuously.
/// </summary>
public sealed class EnvironmentMap
{
    private readonly TerrainSampler _sampler;
    private readonly RiverMap _rivers;

    public EnvironmentMap(int seed, RiverMap rivers)
    {
        _sampler = new TerrainSampler(seed);
        _rivers = rivers;
    }

    /// <summary>Height offsets per kind in metres: gentle for the grassy
    /// kinds, flattened wet ground, raised rocky hills and cliff terraces,
    /// soft valley basins.</summary>
    public static double HeightOffsetOf(EnvironmentKind kind) => kind switch
    {
        EnvironmentKind.Forest => -0.35,
        EnvironmentKind.Woodland => -0.15,
        EnvironmentKind.Wetland => -0.85,
        EnvironmentKind.Shore => -0.55,
        EnvironmentKind.Hills => 1.5,
        EnvironmentKind.Cliff => 2.3,
        EnvironmentKind.Valley => -1.2,
        _ => 0.0
    };

    /// <summary>Amplitude multipliers per kind: wetlands and valleys calm
    /// the terrain, hills swell it, cliffs swell and terrace it.</summary>
    public static double AmplitudeOf(EnvironmentKind kind) => kind switch
    {
        EnvironmentKind.Forest => 0.9,
        EnvironmentKind.Woodland => 0.95,
        EnvironmentKind.Wetland => 0.4,
        EnvironmentKind.Shore => 0.5,
        EnvironmentKind.Hills => 1.75,
        EnvironmentKind.Cliff => 2.1,
        EnvironmentKind.Valley => 0.55,
        _ => 1.0
    };

    public static double HeightOffset(EnvironmentWeights weights) => weights.Blend(
        HeightOffsetOf(EnvironmentKind.Meadow),
        HeightOffsetOf(EnvironmentKind.Forest),
        HeightOffsetOf(EnvironmentKind.Woodland),
        HeightOffsetOf(EnvironmentKind.Wetland),
        HeightOffsetOf(EnvironmentKind.Shore),
        HeightOffsetOf(EnvironmentKind.Hills),
        HeightOffsetOf(EnvironmentKind.Cliff),
        HeightOffsetOf(EnvironmentKind.Valley));

    public static double Amplitude(EnvironmentWeights weights) => weights.Blend(
        AmplitudeOf(EnvironmentKind.Meadow),
        AmplitudeOf(EnvironmentKind.Forest),
        AmplitudeOf(EnvironmentKind.Woodland),
        AmplitudeOf(EnvironmentKind.Wetland),
        AmplitudeOf(EnvironmentKind.Shore),
        AmplitudeOf(EnvironmentKind.Hills),
        AmplitudeOf(EnvironmentKind.Cliff),
        AmplitudeOf(EnvironmentKind.Valley));

    /// <summary>
    /// Cliff faces: inside cliff regions a short-wavelength noise channel is
    /// squeezed through a smoothstep, which compresses its transitions into
    /// real steep faces (≈55–75°) between flat plateaus. A pure function of
    /// the noise, so faces regenerate identically and the terrain stays
    /// continuous — no seams, no quantisation jumps. The quadratic weight
    /// falloff keeps faces inside cliff country and out of the meadow and
    /// wetland on the other side of a boundary.
    /// </summary>
    public double CliffFacesAt(double x, double z, double cliffWeight)
    {
        if (cliffWeight <= 0.02) return 0;
        var w = Math.Clamp(cliffWeight, 0, 1);
        var face = SmoothStep(0.33, 0.67, _sampler.Noise(x, z, 9, 27));
        return face * 5.0 * Smooth(w) * w;
    }

    /// <summary>Environment weights at a world point.</summary>
    public EnvironmentWeights WeightsAt(double x, double z)
    {
        // Channel edges are deliberately wide: every height contribution
        // (offset, amplitude, faces) scales with these weights, so a narrow
        // edge would turn an environment boundary into a steep ramp.
        var forest = SmoothStep(0.44, 0.58, _sampler.Noise(x, z, 170, 21));
        var woodland = SmoothStep(0.46, 0.60, _sampler.Noise(x, z, 240, 22)) * (1 - forest);
        var rocky = SmoothStep(0.46, 0.70, _sampler.Noise(x, z, 300, 23));
        var cliff = SmoothStep(0.45, 0.79, _sampler.Noise(x, z, 95, 24)) * rocky;
        var hills = rocky * (1 - cliff);
        var valley = SmoothStep(0.52, 0.66, _sampler.Noise(x, z, 330, 25)) * (1 - rocky);
        var wetland = SmoothStep(0.42, 0.70, _sampler.Noise(x, z, 120, 26)) * (1 - rocky) * (1 - valley);
        var shore = 0.0;

        // Rivers override their corridor with a fading influence so the
        // override itself is continuous: banks stay gentle, the water edge
        // becomes shore, the flattening around it becomes wetland. Shape only
        // — computing a water level here would recurse into the heights.
        var river = _rivers.SampleShape(x, z);
        if (river.HasRiver)
        {
            var bankDistance = river.Distance - river.HalfWidth;
            if (bankDistance < 18)
            {
                var corridor = 1 - SmoothStep(4.0, 18.0, bankDistance);
                if (corridor > 0)
                {
                    shore = (1 - SmoothStep(2.5, 7.0, bankDistance + 3.0)) * corridor;
                    var wet = SmoothStep(4.0, 9.5, bankDistance + 3.0) * corridor;
                    wetland = wetland * (1 - corridor) + wet;
                    // Rocky kinds fade out entirely inside the corridor so a
                    // river bank is always a gentle walkable edge.
                    cliff *= 1 - corridor;
                    hills *= 1 - corridor;
                    forest *= 1 - corridor * 0.6;
                    woodland *= 1 - corridor * 0.6;
                    valley *= 1 - corridor * 0.6;
                }
            }
        }

        var meadow = Math.Max(0, 1 - forest - woodland - hills - cliff - valley - wetland - shore);
        if (meadow < 0) meadow = 0;
        var total = meadow + forest + woodland + hills + cliff + valley + wetland + shore;
        if (total <= 0.0001) return EnvironmentWeights.PureMeadow;
        return new EnvironmentWeights(
            meadow / total, forest / total, woodland / total, wetland / total,
            shore / total, hills / total, cliff / total, valley / total);
    }

    public string NameAt(double x, double z) => KindName(WeightsAt(x, z).Dominant);

    public static string KindName(EnvironmentKind kind) => kind switch
    {
        EnvironmentKind.Forest => "forest",
        EnvironmentKind.Woodland => "woodland",
        EnvironmentKind.Wetland => "wetland",
        EnvironmentKind.Shore => "shore",
        EnvironmentKind.Hills => "hills",
        EnvironmentKind.Cliff => "cliff",
        EnvironmentKind.Valley => "valley",
        _ => "meadow"
    };

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>Palette for the eight environments, in Core beside the v1
/// terrain palette so the mesh, the map and tests share one source of truth.
/// Colours follow the authored art-direction hex palette.</summary>
public static class EnvironmentPalette
{
    public static readonly Vec3 Meadow = new(0.466667, 0.658824, 0.352941);
    public static readonly Vec3 Forest = new(0.196078, 0.380392, 0.239216);
    public static readonly Vec3 Woodland = new(0.337255, 0.509804, 0.301961);
    public static readonly Vec3 Wetland = new(0.352941, 0.545098, 0.439216);
    public static readonly Vec3 Shore = new(0.803922, 0.725490, 0.501961);
    public static readonly Vec3 Hills = new(0.545098, 0.552941, 0.368627);
    public static readonly Vec3 Cliff = new(0.509804, 0.486275, 0.439216);
    public static readonly Vec3 Valley = new(0.607843, 0.686275, 0.380392);

    public static Vec3 ColorOf(EnvironmentWeights weights) => new(
        weights.Blend(Meadow.X, Forest.X, Woodland.X, Wetland.X, Shore.X, Hills.X, Cliff.X, Valley.X),
        weights.Blend(Meadow.Y, Forest.Y, Woodland.Y, Wetland.Y, Shore.Y, Hills.Y, Cliff.Y, Valley.Y),
        weights.Blend(Meadow.Z, Forest.Z, Woodland.Z, Wetland.Z, Shore.Z, Hills.Z, Cliff.Z, Valley.Z));
}
