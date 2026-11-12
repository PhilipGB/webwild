namespace Webwild.Core;

/// <summary>
/// Shared bridge walkway geometry. The sagging deck itself is authored by the
/// Godot view; this helper decides where each walkable approach meets solid
/// ground. Authored bank points win over inference so a reversed anchor
/// selection or an angled span cannot put a ramp over the ravine.
/// </summary>
public static class BridgeGeometry
{
    public const double DefaultRampLength = 1.2;
    public const double GroundLevel = 0.0;
    public const double MinimumHorizontalSpan = 0.05;

    /// <summary>
    /// Usable half-width of a bridge walkway. The authored planks are narrow, but
    /// the walkable silk deck is deliberately generous: a child steers with a
    /// camera-relative stick, not pixel precision. 1.5 m keeps a 0.72 m capsule
    /// clear of the edges (0.39 m each side) while staying visually supported by
    /// the generated silk mat.
    /// </summary>
    public const double WalkwayHalfWidth = 0.75;

    public readonly record struct ApproachPoints(Vec3 A, Vec3 B);

    /// <summary>
    /// A walkable triangle surface for a whole bridge: left ramp, sagging deck
    /// and right ramp. A surface has no box end caps, so a capsule can never
    /// catch on a lip where two collision boxes would meet.
    /// </summary>
    public sealed record WalkwaySurface(
        IReadOnlyList<Vec3> Vertices,
        IReadOnlyList<int> Indices,
        IReadOnlyList<Vec3> Centerline);

    /// <summary>
    /// Shared sag contract for bridge collision and visuals. Keeping this in one
    /// place prevents a decorative deck from drifting away from the walkable
    /// surface on long or steep spans.
    /// </summary>
    public static double ComputeSag(double spanLength) =>
        Math.Clamp(spanLength / 16.0, 0.08, 0.42);

    public static Vec3 EvaluateSpan(Vec3 a, Vec3 b, double t, double? sagOverride = null)
    {
        var clamped = Math.Clamp(t, 0, 1);
        var point = Vec3.Lerp(a, b, clamped);
        var sag = Math.Clamp(sagOverride ?? ComputeSag(Vec3.Distance(a, b)), 0, 2);
        return new Vec3(point.X, point.Y - Math.Sin(clamped * Math.PI) * sag, point.Z);
    }

    /// <summary>
    /// Return the ground-side end of the approach ramp for each anchor. The
    /// ramp continues the span direction, so it never doubles back under the
    /// deck (which would pinch the walkable surface at a shared island post).
    /// An authored bank point is honoured when it points away from the other
    /// anchor within 45 degrees; otherwise the span-aligned fallback wins.
    /// </summary>
    public static ApproachPoints ComputeApproachPoints(
        Vec3 anchorA,
        Vec3 anchorB,
        Vec3? authoredGroundA = null,
        Vec3? authoredGroundB = null,
        double rampLength = DefaultRampLength)
    {
        var span = new Vec3(anchorB.X - anchorA.X, 0, anchorB.Z - anchorA.Z);
        var horizontal = span.HorizontalLength;
        var awayFromB = new Vec3(0, 0, 1);
        var awayFromA = new Vec3(0, 0, -1);
        if (horizontal > MinimumHorizontalSpan)
        {
            var direction = span / horizontal;
            awayFromB = new Vec3(-direction.X, 0, -direction.Z);
            awayFromA = new Vec3(direction.X, 0, direction.Z);
        }

        var length = Math.Max(0.01, rampLength);
        var groundA = ResolveGround(anchorA, awayFromB, authoredGroundA, length);
        var groundB = ResolveGround(anchorB, awayFromA, authoredGroundB, length);
        return new ApproachPoints(groundA, groundB);
    }

    private static Vec3 ResolveGround(
        Vec3 anchor, Vec3 awayFromOther, Vec3? authored, double rampLength)
    {
        if (authored is { } ground)
        {
            var delta = new Vec3(ground.X - anchor.X, 0, ground.Z - anchor.Z);
            if (delta.HorizontalLength <= 1e-4) return ground;
            // A bank-level authored point sits most of the anchor height below
            // the deck. It is a safety anchor (it keeps a ravine ramp on solid
            // ground) and always wins, in world or bridge-local coordinates.
            // An elevated island landing is only useful if it continues away
            // from the deck; otherwise the span-aligned point avoids a hairpin
            // pinch at a shared post.
            if (anchor.Y - ground.Y >= 0.5) return ground;
            // 45 degrees.
            if (Vec3.Dot(delta.Normalized(), awayFromOther) >= 0.7071) return ground;
        }
        return new Vec3(
            anchor.X + awayFromOther.X * rampLength,
            GroundLevel,
            anchor.Z + awayFromOther.Z * rampLength);
    }

    /// <summary>
    /// Build the walkable surface along the same centerline the modular planks
    /// use (ground A → anchor A → sagging span → anchor B → ground B). Each of
    /// the three straight pieces keeps its own across direction, so the strip
    /// cannot fold into itself where an island approach changes heading.
    /// </summary>
    public static WalkwaySurface BuildWalkwaySurface(
        Vec3 anchorA,
        Vec3 anchorB,
        Vec3 groundA,
        Vec3 groundB,
        double halfWidth = WalkwayHalfWidth)
    {
        var width = Math.Max(0.3, halfWidth);
        var vertices = new List<Vec3>();
        var indices = new List<int>();
        var centerline = new List<Vec3>();

        // Three independent straight strips. A single folded ribbon would
        // self-intersect where an island approach changes heading, so each strip
        // keeps its own across direction. Their end edges are coincident at the
        // anchor, which keeps the joint flat and walkable.
        var rampA = SampleLine(groundA, anchorA, 0.18);
        var deck = SampleSpan(anchorA, anchorB, 28);
        var rampB = SampleLine(anchorB, groundB, 0.18);
        AppendStrip(vertices, indices, rampA, width);
        AppendStrip(vertices, indices, deck, width);
        AppendStrip(vertices, indices, rampB, width);
        foreach (var point in rampA) AddDistinct(centerline, point);
        foreach (var point in deck) AddDistinct(centerline, point);
        foreach (var point in rampB) AddDistinct(centerline, point);

        return new WalkwaySurface(vertices, indices, centerline);
    }

    /// <summary>Horizontal unit vector across the span, or +X when degenerate.</summary>
    public static Vec3 AcrossFor(Vec3 anchorA, Vec3 anchorB)
    {
        var span = new Vec3(anchorB.X - anchorA.X, 0, anchorB.Z - anchorA.Z);
        if (span.HorizontalLength <= MinimumHorizontalSpan) return new Vec3(1, 0, 0);
        var direction = span / span.HorizontalLength;
        return new Vec3(-direction.Z, 0, direction.X).Normalized();
    }

    private static List<Vec3> SampleLine(Vec3 from, Vec3 to, double step)
    {
        var points = new List<Vec3>();
        var length = Vec3.Distance(from, to);
        var count = Math.Max(1, (int)Math.Ceiling(length / Math.Max(0.04, step)));
        for (var i = 0; i <= count; i++)
            AddDistinct(points, Vec3.Lerp(from, to, i / (double)count));
        return points;
    }

    private static List<Vec3> SampleSpan(Vec3 anchorA, Vec3 anchorB, int samples)
    {
        var points = new List<Vec3>(samples + 1);
        var sag = ComputeSag(Vec3.Distance(anchorA, anchorB));
        for (var i = 0; i <= samples; i++)
            AddDistinct(points, EvaluateSpan(anchorA, anchorB, i / (double)samples, sag));
        return points;
    }

    private static void AppendStrip(
        List<Vec3> vertices, List<int> indices, List<Vec3> points, double halfWidth)
    {
        if (points.Count < 2) return;
        var across = AcrossFor(points[0], points[^1]);
        var start = vertices.Count;
        foreach (var point in points)
        {
            vertices.Add(new Vec3(
                point.X + across.X * halfWidth, point.Y, point.Z + across.Z * halfWidth));
            vertices.Add(new Vec3(
                point.X - across.X * halfWidth, point.Y, point.Z - across.Z * halfWidth));
        }

        for (var i = 0; i + 1 < points.Count; i++)
        {
            var left = start + i * 2;
            var right = left + 1;
            var nextLeft = left + 2;
            var nextRight = right + 2;
            // Counter-clockwise from above: (left, next-left, right) then
            // (right, next-left, next-right).
            indices.Add(left);
            indices.Add(nextLeft);
            indices.Add(right);
            indices.Add(right);
            indices.Add(nextLeft);
            indices.Add(nextRight);
        }
    }

    private static void AddDistinct(List<Vec3> points, Vec3 point)
    {
        if (!point.IsFinite) return;
        if (points.Count > 0 && Vec3.Distance(points[^1], point) < 1e-5) return;
        points.Add(point);
    }
}
