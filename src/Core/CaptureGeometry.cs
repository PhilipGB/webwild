namespace Webwild.Core;

public enum InsectPhase
{
    Flying,
    Trapped,
    Retrieved
}

public sealed class InsectState
{
    public required InsectRouteDefinition Definition { get; init; }
    public int Generation { get; set; }
    public InsectPhase Phase { get; set; } = InsectPhase.Flying;
    public Vec3 Position { get; set; }
    public Vec3 PreviousPosition { get; set; }
    public int WaypointIndex { get; set; }
    public string? TrappedWebId { get; set; }
    public long CapturedAtTick { get; set; }
    public long RespawnAtTick { get; set; }
    public string Id => Definition.Id;
    public bool IsVisible => Phase != InsectPhase.Retrieved;
    public bool CanBeCollected => Phase == InsectPhase.Trapped;
}

/// <summary>Geometry shared by capture rules and the visible capture volume.</summary>
public static class CaptureGeometry
{
    public sealed record PlaneArea(Vec3 A, Vec3 B, Vec3 Center, Vec3 Direction, Vec3 Normal, double HalfWidth, double HalfHeight);

    public static PlaneArea BuildArea(Vec3 a, Vec3 b, double captureRadius)
    {
        var dir = (b - a).Normalized();
        var normal = Vec3.Cross(dir, Vec3.Up).Normalized();
        if (normal.LengthSquared < 1e-9) normal = new Vec3(0, 0, 1);
        var center = (a + b) * 0.5;
        return new PlaneArea(a, b, center, dir, normal, (b - a).Length * 0.5 + 0.25, Math.Max(0.8, captureRadius));
    }

    /// <summary>Returns the first crossing along the insect's swept segment, if any.</summary>
    public static bool TryIntersect(PlaneArea area, Vec3 from, Vec3 to, out Vec3 hit, out double t)
    {
        hit = default;
        t = 0;
        var segment = to - from;
        var denom = Vec3.Dot(segment, area.Normal);
        if (Math.Abs(denom) < 1e-7) return false;
        var param = Vec3.Dot(area.Center - from, area.Normal) / denom;
        if (param < 0 || param > 1) return false;
        var point = from + segment * param;
        var local = point - area.Center;
        var lateral = Vec3.Dot(local, area.Direction);
        var vertical = local.Y;
        if (Math.Abs(lateral) > area.HalfWidth) return false;
        if (vertical < -0.55 || vertical > area.HalfHeight) return false;
        hit = point;
        t = param;
        return true;
    }
}
