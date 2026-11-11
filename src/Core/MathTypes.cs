namespace Webwild.Core;

/// <summary>Engine-free 3D vector used by the deterministic simulation.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 Zero => new(0, 0, 0);
    public static Vec3 Up => new(0, 1, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);
    public double HorizontalLength => Math.Sqrt(X * X + Z * Z);

    public Vec3 Normalized()
    {
        var l = Length;
        return l < 1e-9 ? Zero : this / l;
    }

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public static double Distance(Vec3 a, Vec3 b) => (a - b).Length;

    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

    public bool IsFinite =>
        double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
}
