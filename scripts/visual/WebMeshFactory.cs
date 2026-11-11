using Godot;

namespace Webwild.Game;

public static class WebMeshFactory
{
    public static ArrayMesh BuildCapture(float halfWidth, float height, Color color)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(color);

        var spokes = 12;
        for (var i = 0; i < spokes; i++)
        {
            var angle = Mathf.Tau * i / spokes;
            var outer = new Vector3(Mathf.Cos(angle) * halfWidth, Mathf.Sin(angle) * height, 0);
            AddPrism(st, Vector3.Zero, outer, 0.018f);
        }
        foreach (var fraction in new[] { 0.42f, 0.7f, 1.0f })
        {
            var previous = Vector3.Zero;
            var segments = 28;
            for (var i = 0; i <= segments; i++)
            {
                var angle = Mathf.Tau * i / segments;
                var point = new Vector3(Mathf.Cos(angle) * halfWidth * fraction, Mathf.Sin(angle) * height * fraction, 0);
                if (i > 0) AddPrism(st, previous, point, 0.016f);
                previous = point;
            }
        }
        // A few dew threads make the web read as silk rather than wire.
        for (var i = 0; i < 5; i++)
        {
            var x = -halfWidth + halfWidth * 2 * (i + 1) / 6f;
            AddPrism(st, new Vector3(x, -height * 0.55f, 0), new Vector3(x * 0.35f, height * 0.8f, 0), 0.012f);
        }

        st.GenerateNormals();
        return st.Commit();
    }

    public static ArrayMesh BuildBridge(Vector3 a, Vector3 b, Color color)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(color);
        var center = (a + b) * 0.5f;
        var length = a.DistanceTo(b);
        var sag = Mathf.Clamp(length / 16f, 0.08f, 0.42f);
        var samples = 18;
        var halfWidth = 0.22f;
        Vector3 Point(float t)
        {
            var p = a.Lerp(b, t);
            p.Y -= Mathf.Sin(t * Mathf.Pi) * sag;
            return p - center;
        }
        var previous = Point(0);
        for (var i = 1; i <= samples; i++)
        {
            var t = i / (float)samples;
            var point = Point(t);
            var tangent = (point - previous).Normalized();
            var side = tangent.Cross(Vector3.Up).Normalized() * halfWidth;
            // Ribbon surface (double-sided via two triangles, cull disabled).
            st.AddVertex(previous - side);
            st.AddVertex(previous + side);
            st.AddVertex(point + side);
            st.AddVertex(previous - side);
            st.AddVertex(point + side);
            st.AddVertex(point - side);
            previous = point;
        }
        for (var i = 0; i <= samples; i++)
        {
            var t = i / (float)samples;
            var point = Point(t);
            var next = Point(Mathf.Min(1, t + 0.02f));
            var tangent = (next - point).Normalized();
            var side = tangent.Cross(Vector3.Up).Normalized() * halfWidth;
            if (i > 0)
            {
                var prevT = (i - 1) / (float)samples;
                var prev = Point(prevT);
                AddPrism(st, prev - side, point - side, 0.022f);
                AddPrism(st, prev + side, point + side, 0.022f);
            }
        }
        st.GenerateNormals();
        return st.Commit();
    }

    private static void AddPrism(SurfaceTool st, Vector3 a, Vector3 b, float radius)
    {
        var dir = (b - a).Normalized();
        if (dir.LengthSquared() < 1e-6f) return;
        var sideA = dir.Cross(Vector3.Forward).Normalized();
        if (sideA.LengthSquared() < 1e-5f) sideA = dir.Cross(Vector3.Right).Normalized();
        var sideB = dir.Cross(sideA).Normalized();
        var offsets = new[]
        {
            sideA * radius, sideB * radius, -sideA * radius, -sideB * radius
        };
        for (var i = 0; i < 4; i++)
        {
            var o1 = offsets[i];
            var o2 = offsets[(i + 1) % 4];
            st.AddVertex(a + o1);
            st.AddVertex(a + o2);
            st.AddVertex(b + o2);
            st.AddVertex(a + o1);
            st.AddVertex(b + o2);
            st.AddVertex(b + o1);
        }
    }

    public static StandardMaterial3D SilkMaterial(Color color, bool ghost = false)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Metallic = 0.05f,
            Roughness = 0.28f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = ghost ? 0.2f : 0.55f,
            NoDepthTest = false
        };
        material.AlbedoColor = new Color(color.R, color.G, color.B, ghost ? 0.45f : color.A);
        return material;
    }
}
