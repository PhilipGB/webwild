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

    public readonly record struct ApproachPoints(Vec3 A, Vec3 B);

    /// <summary>
    /// Return the ground-side end of the approach ramp for each anchor. When no
    /// authored bank point exists, the ramp runs away from the other anchor by
    /// <paramref name="rampLength"/>, which keeps it independent of selection
    /// order and works for spans on either the X or Z axis.
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
        var groundA = authoredGroundA ?? new Vec3(
            anchorA.X + awayFromB.X * length,
            GroundLevel,
            anchorA.Z + awayFromB.Z * length);
        var groundB = authoredGroundB ?? new Vec3(
            anchorB.X + awayFromA.X * length,
            GroundLevel,
            anchorB.Z + awayFromA.Z * length);
        return new ApproachPoints(groundA, groundB);
    }
}
