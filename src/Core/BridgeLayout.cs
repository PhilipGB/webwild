namespace Webwild.Core;

/// <summary>
/// Engine-free placement plan for the modular bridge deck. The Godot view turns
/// these placements into authored plank/fixture instances, while collision uses
/// the same sag contract in <see cref="BridgeGeometry"/>. Keeping this pure means
/// spacing, sag and orientation are covered by deterministic unit tests.
/// </summary>
public static class BridgeLayout
{
    public const double PlankSpacing = 0.225;
    public const double DeckHalfWidth = 0.34;
    public const double RailLateral = 0.70;
    public const double RailHeight = 0.36;
    public const double SupportInset = 0.55;

    public static BridgeDeckPlan Build(
        Vec3 anchorA,
        Vec3 anchorB,
        Vec3? authoredGroundA,
        Vec3? authoredGroundB,
        string bridgeId,
        double plankSpacing = PlankSpacing,
        bool resolveGround = true)
    {
        var approach = resolveGround || authoredGroundA is null || authoredGroundB is null
            ? BridgeGeometry.ComputeApproachPoints(
                anchorA, anchorB, authoredGroundA, authoredGroundB)
            : new BridgeGeometry.ApproachPoints(authoredGroundA.Value, authoredGroundB.Value);
        var span = Vec3.Distance(anchorA, anchorB);
        var sag = BridgeGeometry.ComputeSag(span);
        var horizontalSpan = new Vec3(anchorB.X - anchorA.X, 0, anchorB.Z - anchorA.Z).HorizontalLength;
        if (span < 0.02 || horizontalSpan < BridgeGeometry.MinimumHorizontalSpan || !anchorA.IsFinite || !anchorB.IsFinite)
        {
            var fallback = MakeFrame(anchorA, new Vec3(1, 0, 0));
            return new BridgeDeckPlan(
                new[] { anchorA },
                new[] { new BridgePlankPlacement(0, 0, fallback, 0, 0, false) },
                Array.Empty<BridgeFrame>(),
                Array.Empty<BridgeFrame>(),
                new[] { fallback },
                span,
                sag,
                true);
        }

        var points = new List<Vec3>();
        var rampAEnd = 0.0;
        AppendSegment(points, approach.A, anchorA, 0.12, ref rampAEnd);
        var mainStartDistance = rampAEnd;
        AppendSpan(points, anchorA, anchorB, sag, 40);
        var mainEnd = TotalLength(points);
        var totalEnd = mainEnd;
        AppendSegment(points, anchorB, approach.B, 0.12, ref totalEnd);

        var cumulative = CumulativeDistances(points);
        var total = cumulative.Count == 0 ? 0 : cumulative[^1];
        if (total < 0.02)
        {
            var fallback = MakeFrame(anchorA, new Vec3(1, 0, 0));
            return new BridgeDeckPlan(
                new[] { anchorA },
                new[] { new BridgePlankPlacement(0, 0, fallback, 0, 0, false) },
                Array.Empty<BridgeFrame>(),
                Array.Empty<BridgeFrame>(),
                new[] { fallback },
                span,
                sag,
                true);
        }

        var spacing = Math.Clamp(plankSpacing, 0.12, 0.60);
        var count = Math.Max(1, (int)Math.Floor(total / spacing));
        var planks = new List<BridgePlankPlacement>(count);
        var hash = StableHash(bridgeId);
        for (var i = 0; i < count; i++)
        {
            var distance = spacing * (i + 0.5);
            if (distance > total) break;
            var sample = Sample(points, cumulative, distance);
            var frame = MakeFrame(sample.Position, sample.Tangent);
            var lateral = (((i * 37 + hash) % 7) - 3) * 0.0025;
            var yaw = (((i * 23 + hash) % 9) - 4) * 0.0035;
            planks.Add(new BridgePlankPlacement(
                i, i % 3, frame, lateral, yaw,
                distance <= mainStartDistance || distance >= mainEnd));
        }

        var railFrames = new List<BridgeFrame>();
        const int railSamples = 22;
        var mainLength = Math.Max(0.001, mainEnd - mainStartDistance);
        for (var i = 0; i <= railSamples; i++)
        {
            var t = i / (double)railSamples;
            var distance = mainStartDistance + mainLength * t;
            var sample = Sample(points, cumulative, distance);
            railFrames.Add(MakeFrame(sample.Position, sample.Tangent));
        }

        var supports = new List<BridgeFrame>();
        foreach (var (t, side) in new[] { (0.10, -1.0), (0.90, -1.0), (0.30, 1.0), (0.70, 1.0) })
        {
            var sample = Sample(points, cumulative, mainStartDistance + mainLength * t);
            var frame = MakeFrame(sample.Position, sample.Tangent);
            supports.Add(frame with
            {
                Position = frame.Position + frame.Across * (SupportInset * side)
            });
        }

        var ties = new List<BridgeFrame>();
        var tieCount = Math.Max(1, (int)Math.Floor(mainLength / 1.5));
        for (var i = 1; i <= tieCount; i++)
        {
            var t = i / (double)(tieCount + 1);
            var sample = Sample(points, cumulative, mainStartDistance + mainLength * t);
            ties.Add(MakeFrame(sample.Position, sample.Tangent));
        }

        return new BridgeDeckPlan(
            points,
            planks,
            supports,
            ties,
            railFrames,
            span,
            sag,
            false);
    }

    private static void AppendSegment(
        List<Vec3> points, Vec3 from, Vec3 to, double step, ref double endDistance)
    {
        var length = Vec3.Distance(from, to);
        if (length < 1e-5)
        {
            AddPoint(points, from);
            endDistance = TotalLength(points);
            return;
        }
        var segments = Math.Max(1, (int)Math.Ceiling(length / Math.Max(0.04, step)));
        for (var i = 0; i <= segments; i++)
            AddPoint(points, Vec3.Lerp(from, to, i / (double)segments));
        endDistance = TotalLength(points);
    }

    private static void AppendSpan(List<Vec3> points, Vec3 a, Vec3 b, double sag, int samples)
    {
        for (var i = 0; i <= samples; i++)
            AddPoint(points, BridgeGeometry.EvaluateSpan(a, b, i / (double)samples, sag));
    }

    private static void AddPoint(List<Vec3> points, Vec3 point)
    {
        if (!point.IsFinite) return;
        if (points.Count > 0 && Vec3.Distance(points[^1], point) < 1e-5) return;
        points.Add(point);
    }

    private static double TotalLength(List<Vec3> points)
    {
        var total = 0.0;
        for (var i = 1; i < points.Count; i++) total += Vec3.Distance(points[i - 1], points[i]);
        return total;
    }

    private static List<double> CumulativeDistances(List<Vec3> points)
    {
        var result = new List<double>(points.Count);
        var total = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0) total += Vec3.Distance(points[i - 1], points[i]);
            result.Add(total);
        }
        return result;
    }

    private static (Vec3 Position, Vec3 Tangent) Sample(
        IReadOnlyList<Vec3> points, IReadOnlyList<double> cumulative, double distance)
    {
        if (points.Count == 0) return (Vec3.Zero, new Vec3(1, 0, 0));
        if (points.Count == 1) return (points[0], new Vec3(1, 0, 0));
        var clamped = Math.Clamp(distance, 0, cumulative[^1]);
        var index = 1;
        while (index < cumulative.Count - 1 && cumulative[index] < clamped) index++;
        var start = cumulative[index - 1];
        var length = cumulative[index] - start;
        var t = length < 1e-9 ? 0 : (clamped - start) / length;
        var position = Vec3.Lerp(points[index - 1], points[index], t);
        var tangent = (points[index] - points[index - 1]).Normalized();
        return (position, tangent);
    }

    private static BridgeFrame MakeFrame(Vec3 position, Vec3 tangent)
    {
        var along = tangent.Normalized();
        if (along.LengthSquared < 1e-9) along = new Vec3(1, 0, 0);
        var across = Vec3.Cross(along, Vec3.Up);
        if (across.LengthSquared < 1e-9)
            across = new Vec3(0, 0, 1);
        across = across.Normalized();
        var up = Vec3.Cross(across, along).Normalized();
        if (up.Y < 0)
        {
            across = -across;
            up = -up;
        }
        if (up.LengthSquared < 1e-9) up = Vec3.Up;
        return new BridgeFrame(position, along, up, across);
    }

    internal static int StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in value ?? "")
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return (int)(hash & 0x7FFFFFFF);
        }
    }
}

public readonly record struct BridgeFrame(Vec3 Position, Vec3 Along, Vec3 Up, Vec3 Across);

public readonly record struct BridgePlankPlacement(
    int Index,
    int VariantIndex,
    BridgeFrame Frame,
    double LateralOffset,
    double YawOffset,
    bool OnRamp);

public sealed record BridgeDeckPlan(
    IReadOnlyList<Vec3> Centerline,
    IReadOnlyList<BridgePlankPlacement> Planks,
    IReadOnlyList<BridgeFrame> Supports,
    IReadOnlyList<BridgeFrame> Ties,
    IReadOnlyList<BridgeFrame> RailFrames,
    double SpanLength,
    double Sag,
    bool IsDegenerate);
