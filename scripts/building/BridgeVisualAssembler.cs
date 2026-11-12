using Godot;
using Webwild.Core;

namespace Webwild.Game;

public enum BridgeVisualMode
{
    Constructed,
    PreviewValid,
    PreviewInvalid
}

/// <summary>
/// Builds the visible modular bridge from the supplied authored plank, fixture,
/// knot and panel scenes plus generated silk curves. It deliberately owns no
/// collision: <see cref="WebVisual"/> keeps the existing walkable deck shapes.
/// The placement plan is pure core code so spacing/sag/orientation are testable.
/// </summary>
public partial class BridgeVisualAssembler : Node3D
{
    private static readonly Dictionary<string, PackedScene?> SceneCache = new(StringComparer.Ordinal);
    private static StandardMaterial3D? _ropeMaterial;
    private StandardMaterial3D? _damageOverlayBase;
    private StandardMaterial3D? _ghostMaterial;

    private readonly List<MeshInstance3D> _meshes = new();
    private Node3D _partsRoot = null!;
    private MeshInstance3D? _railMesh;
    private BridgeDeckPlan? _plan;
    private BridgeVisualMode _mode;
    private int _plankCount;
    private int _panelCount;
    private int _detailCount;
    private int _railTriangles;
    private int _variantMask;
    private float _damageRatio = 1;
    private Vector3 _localAnchorA;
    private Vector3 _localAnchorB;
    private Vector3? _localGroundA;
    private Vector3? _localGroundB;

    public BridgeDeckPlan? Plan => _plan;

    public int PlankCount => _plankCount;
    public int PanelCount => _panelCount;
    public int DetailCount => _detailCount;
    public int RailTriangleCount => _railTriangles;
    public int DamageOverlayCount => _meshes.Count(mesh => mesh.MaterialOverlay is not null);
    public int UnstyledMeshCount => _meshes.Count(mesh => mesh.MaterialOverride is null);

    public int VariantCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < 3; i++)
                if ((_variantMask & (1 << i)) != 0) count++;
            return count;
        }
    }

    public bool HasAllPlankVariants => (_variantMask & 0b111) == 0b111;
    public Color PreviewColor => _ghostMaterial?.AlbedoColor ?? Colors.Transparent;

    public string ProbeSignature =>
        $"planks={_plankCount};variants={_variantMask};variantCount={VariantCount};" +
        $"panels={_panelCount};details={_detailCount};rails={_railTriangles};ribbon=0;" +
        $"degenerate={(_plan?.IsDegenerate == true ? 1 : 0)};mode={_mode}";

    public void Configure(
        string bridgeId,
        Vector3 anchorA,
        Vector3 anchorB,
        Vector3? groundA,
        Vector3? groundB,
        BridgeVisualMode mode,
        float damageRatio = 1f)
    {
        _mode = mode;
        _damageRatio = Mathf.Clamp(damageRatio, 0, 1);
        _localAnchorA = anchorA;
        _localAnchorB = anchorB;
        _localGroundA = groundA;
        _localGroundB = groundB;
        _plankCount = _panelCount = _detailCount = _railTriangles = 0;
        _variantMask = 0;
        _meshes.Clear();
        foreach (var child in GetChildren()) child.QueueFree();

        _partsRoot = new Node3D { Name = "Parts" };
        AddChild(_partsRoot);

        _plan = BridgeLayout.Build(
            ToCore(anchorA), ToCore(anchorB),
            groundA is { } ga ? ToCore(ga) : null,
            groundB is { } gb ? ToCore(gb) : null,
            bridgeId,
            resolveGround: groundA is null || groundB is null);

        BuildPlanks();
        BuildSilkDeckMat(groundA, groundB);
        BuildUnderlay();
        BuildSilkRails();
        if (mode == BridgeVisualMode.Constructed) BuildDetails();
        else ApplyGhostMaterial();
        ApplyDamage(_damageRatio);
    }

    public void SetDamageRatio(float ratio)
    {
        _damageRatio = Mathf.Clamp(ratio, 0, 1);
        ApplyDamage(_damageRatio);
    }

    public void SetPreviewColor(bool valid)
    {
        if (_mode == BridgeVisualMode.Constructed) return;
        _mode = valid ? BridgeVisualMode.PreviewValid : BridgeVisualMode.PreviewInvalid;
        ApplyGhostMaterial();
    }

    private void BuildSilkDeckMat(Vector3? groundA, Vector3? groundB)
    {
        // The collision strip is 1.5 m wide so a child can walk it comfortably.
        // Show the same width as a woven silk mat so the walkable deck is real,
        // not an invisible tolerance around narrow planks.
        // The caller passes ground points that were already resolved in world
        // space. Re-resolving local coordinates would lose the ground height, so
        // use them directly when both are present.
        var approach = groundA is { } ga && groundB is { } gb
            ? new BridgeGeometry.ApproachPoints(ToCore(ga), ToCore(gb))
            : BridgeGeometry.ComputeApproachPoints(
                ToCore(_localAnchorA), ToCore(_localAnchorB));
        var surface = BridgeGeometry.BuildWalkwaySurface(
            ToCore(_localAnchorA), ToCore(_localAnchorB), approach.A, approach.B);
        if (surface.Vertices.Count == 0 || surface.Indices.Count == 0) return;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var cream = new Color(0.94f, 0.95f, 0.86f);
        var sage = new Color(0.85f, 0.91f, 0.83f);
        // One colour per quad gives a subtle woven stripe without textures.
        for (var i = 0; i + 2 < surface.Indices.Count; i += 6)
        {
            var colour = (i / 6) % 2 == 0 ? cream : sage;
            for (var k = 0; k < 6 && i + k < surface.Indices.Count; k++)
            {
                var point = ToVector(surface.Vertices[surface.Indices[i + k]]);
                st.SetColor(colour);
                st.AddVertex(point - Vector3.Up * 0.045f);
            }
        }
        st.GenerateNormals();
        var mesh = st.Commit();
        var mat = new MeshInstance3D
        {
            Name = "SilkDeckMat",
            Mesh = mesh,
            MaterialOverride = RopeMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        _partsRoot.AddChild(mat);
        _meshes.Add(mat);
    }

    private void BuildPlanks()
    {        if (_plan is null) return;
        var variants = new[] { "BarkPlankA", "BarkPlankB", "BarkPlankC" };
        foreach (var placement in _plan.Planks)
        {
            var scene = LoadPart(variants[placement.VariantIndex]);
            if (scene is null) continue;
            var part = scene.Instantiate<Node3D>();
            part.Name = $"Plank{placement.Index:00}_{variants[placement.VariantIndex]}";
            var frame = placement.Frame;
            var basis = FrameBasis(frame, placement.YawOffset);
            var position = frame.Position + frame.Across * placement.LateralOffset;
            part.Transform = new Transform3D(basis, ToVector(position));
            _partsRoot.AddChild(part);
            if (_mode == BridgeVisualMode.Constructed)
                ArtMaterials.ApplyVertexColors(part);
            RegisterMeshes(part);
            _plankCount++;
            _variantMask |= 1 << placement.VariantIndex;
        }
    }

    private void BuildUnderlay()
    {
        if (_plan is null) return;
        var panels = LoadPart("WovenSilkPanel");
        if (panels is null) return;
        for (var i = 0; i + 1 < _plan.Planks.Count; i += 3)
        {
            var first = _plan.Planks[i];
            var second = _plan.Planks[i + 1];
            var midpoint = (first.Frame.Position + second.Frame.Position) * 0.5f;
            var midFrame = first.Frame;
            var panel = panels.Instantiate<Node3D>();
            panel.Name = $"SilkPanel{i:00}";
            var basis = FrameBasis(midFrame, second.YawOffset * 0.5);
            panel.Transform = new Transform3D(basis, ToVector(midpoint - midFrame.Up * 0.085));
            _partsRoot.AddChild(panel);
            if (_mode == BridgeVisualMode.Constructed)
                ArtMaterials.ApplyVertexColors(panel);
            RegisterMeshes(panel);
            _panelCount++;
        }
    }

    private void BuildSilkRails()
    {
        if (_plan is null || _plan.RailFrames.Count < 2) return;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        foreach (var side in new[] { -1f, 1f })
        {
            var upper = new List<Vector3>();
            var lower = new List<Vector3>();
            foreach (var frame in _plan.RailFrames)
            {
                var deck = ToVector(frame.Position);
                var across = ToVector(frame.Across);
                var up = ToVector(frame.Up);
                upper.Add(deck + across * ((float)BridgeLayout.RailLateral * side) + up * (float)BridgeLayout.RailHeight);
                lower.Add(deck + across * ((float)BridgeLayout.RailLateral * side) + up * 0.16f);
            }
            AddTube(st, upper, 0.016f,
                new Color(0.94f, 0.95f, 0.86f), new Color(0.86f, 0.93f, 0.90f));
            AddTube(st, lower, 0.009f,
                new Color(0.84f, 0.91f, 0.90f), new Color(0.93f, 0.94f, 0.85f));

            for (var i = 0; i < _plan.RailFrames.Count; i += 4)
            {
                var frame = _plan.RailFrames[i];
                var deck = ToVector(frame.Position);
                var across = ToVector(frame.Across);
                var up = ToVector(frame.Up);
                var bottom = deck + across * ((float)BridgeLayout.RailLateral * side);
                var top = bottom + up * (float)BridgeLayout.RailHeight;
                AddTube(st, new List<Vector3> { bottom, top }, 0.007f,
                    new Color(0.88f, 0.94f, 0.89f), new Color(0.78f, 0.88f, 0.88f));
            }
        }

        st.GenerateNormals();
        var mesh = st.Commit();
        _railTriangles = mesh.GetFaces().Length / 3;
        _railMesh = new MeshInstance3D
        {
            Name = "SilkRails",
            Mesh = mesh,
            MaterialOverride = RopeMaterial
        };
        _partsRoot.AddChild(_railMesh);
        _meshes.Add(_railMesh);
    }

    private void BuildDetails()
    {
        if (_plan is null) return;

        var support = LoadPart("SilkWrappedSupport");
        foreach (var frame in _plan.Supports)
            InstantiateDetail(support, "SilkSupport", frame, 0);

        if (_plan.RailFrames.Count >= 6)
        {
            var eyelet = LoadPart("WoodenEyelet");
            foreach (var index in new[] { 0, _plan.RailFrames.Count - 1 })
            {
                var frame = _plan.RailFrames[index];
                foreach (var side in new[] { -1f, 1f })
                {
                    var at = frame with
                    {
                        Position = frame.Position + frame.Across * ((float)BridgeLayout.DeckHalfWidth * side)
                    };
                    InstantiateDetail(eyelet, "SilkEyelet", at, 0);
                }
            }

            var fork = LoadPart("ForkedRailFixture");
            foreach (var (index, side) in new[]
                     {
                         (5, -1f), (_plan.RailFrames.Count - 6, 1f)
                     })
            {
                var frame = _plan.RailFrames[Math.Clamp(index, 0, _plan.RailFrames.Count - 1)];
                var at = frame with
                {
                    Position = frame.Position + frame.Across * ((float)BridgeLayout.RailLateral * side)
                };
                InstantiateDetail(fork, "RailFork", at, 0);
            }

            var knot = LoadPart("SilkCinchKnot");
            foreach (var (index, side) in new[] { (0, -1f), (_plan.RailFrames.Count - 1, 1f) })
            {
                var frame = _plan.RailFrames[index];
                var at = frame with
                {
                    Position = frame.Position + frame.Across * ((float)BridgeLayout.RailLateral * side) +
                               frame.Up * (float)BridgeLayout.RailHeight
                };
                InstantiateDetail(knot, "RailCinch", at, 0.15f);
            }

            var hitch = LoadPart("SilkDoubleHitch");
            foreach (var (index, side) in new[] { (_plan.RailFrames.Count / 3, 1f), (_plan.RailFrames.Count * 2 / 3, -1f) })
            {
                var frame = _plan.RailFrames[index];
                var at = frame with
                {
                    Position = frame.Position + frame.Across * ((float)BridgeLayout.RailLateral * side) +
                               frame.Up * 0.24f
                };
                InstantiateDetail(hitch, "RailHitch", at, 0.4f);
            }
        }

        var lashing = LoadPart("SilkCrossLashing");
        foreach (var frame in _plan.Ties)
        {
            var at = frame with { Position = frame.Position + frame.Up * 0.035f };
            InstantiateDetail(lashing, "CrossLashing", at, 0.3f);
        }
    }

    private void InstantiateDetail(PackedScene? scene, string name, BridgeFrame frame, float yaw)
    {
        if (scene is null) return;
        var part = scene.Instantiate<Node3D>();
        part.Name = $"{name}{_detailCount:00}";
        part.Transform = new Transform3D(FrameBasis(frame, yaw), ToVector(frame.Position));
        _partsRoot.AddChild(part);
        ArtMaterials.ApplyVertexColors(part);
        RegisterMeshes(part);
        _detailCount++;
    }

    private void ApplyGhostMaterial()
    {
        var color = _mode == BridgeVisualMode.PreviewInvalid ? UiKit.Coral : UiKit.FreshLeaf;
        _ghostMaterial = WebMeshFactory.SilkMaterial(new Color(color.R, color.G, color.B, 0.52f), true);
        foreach (var mesh in _meshes)
        {
            mesh.MaterialOverride = _ghostMaterial;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }

    private void ApplyDamage(float ratio)
    {
        if (_meshes.Count == 0) return;
        var alpha = ratio <= 0.01f
            ? 0.42f
            : Mathf.Clamp((1f - ratio) * 0.30f, 0f, 0.30f);
        if (alpha <= 0.001f)
        {
            foreach (var mesh in _meshes) mesh.MaterialOverlay = null;
            return;
        }
        _damageOverlayBase ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.32f, 0.30f, 0.3f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        _damageOverlayBase.AlbedoColor = new Color(0.92f, 0.32f, 0.30f, alpha);
        foreach (var mesh in _meshes) mesh.MaterialOverlay = _damageOverlayBase;
    }

    private void RegisterMeshes(Node node)
    {
        if (node is MeshInstance3D mesh) _meshes.Add(mesh);
        foreach (var child in node.GetChildren()) RegisterMeshes(child);
    }

    private static Basis FrameBasis(BridgeFrame frame, double yaw)
    {
        var along = ToVector(frame.Along);
        var up = ToVector(frame.Up);
        var across = ToVector(frame.Across);
        if (Math.Abs(yaw) > 1e-6)
        {
            var rotation = new Basis(up, (float)yaw);
            along = rotation * along;
            across = rotation * across;
        }
        return new Basis(along, up, across);
    }

    private static StandardMaterial3D RopeMaterial
    {
        get
        {
            if (_ropeMaterial is not null) return _ropeMaterial;
            _ropeMaterial = new StandardMaterial3D
            {
                AlbedoColor = Colors.White,
                VertexColorUseAsAlbedo = true,
                Roughness = 0.38f,
                Metallic = 0.03f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                EmissionEnabled = true,
                Emission = new Color(0.72f, 0.83f, 0.78f),
                EmissionEnergyMultiplier = 0.12f
            };
            return _ropeMaterial;
        }
    }

    private static PackedScene? LoadPart(string name)
    {
        if (SceneCache.TryGetValue(name, out var cached)) return cached;
        var scene = GD.Load<PackedScene>($"res://scenes/building/bridge/parts/{name}.tscn");
        if (scene is null) GD.PushError($"Bridge visual part is missing: {name}");
        SceneCache[name] = scene;
        return scene;
    }

    private static void AddTube(
        SurfaceTool st, IReadOnlyList<Vector3> points, float radius, Color first, Color second)
    {
        if (points.Count < 2) return;
        const int sides = 6;
        var rings = new List<Vector3[]>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var previous = points[Math.Max(0, i - 1)];
            var next = points[Math.Min(points.Count - 1, i + 1)];
            var tangent = (next - previous).Normalized();
            if (tangent.LengthSquared() < 1e-8f) tangent = Vector3.Forward;
            var side = tangent.Cross(Vector3.Up);
            if (side.LengthSquared() < 1e-6f) side = tangent.Cross(Vector3.Right);
            side = side.Normalized();
            var up = tangent.Cross(side).Normalized();
            var ring = new Vector3[sides];
            for (var k = 0; k < sides; k++)
            {
                var angle = Mathf.Tau * k / sides;
                ring[k] = points[i] + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
            }
            rings.Add(ring);
        }

        for (var i = 0; i < rings.Count - 1; i++)
        {
            var color = i % 2 == 0 ? first : second;
            for (var k = 0; k < sides; k++)
            {
                var nextK = (k + 1) % sides;
                AddTriangle(st, rings[i][k], rings[i + 1][k], rings[i + 1][nextK], color);
                AddTriangle(st, rings[i][k], rings[i + 1][nextK], rings[i][nextK], color);
            }
        }
    }

    private static void AddTriangle(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        st.SetColor(color);
        st.AddVertex(a);
        st.AddVertex(b);
        st.AddVertex(c);
    }

    private static Vec3 ToCore(Vector3 value) => new(value.X, value.Y, value.Z);
    private static Vector3 ToVector(Vec3 value) => new((float)value.X, (float)value.Y, (float)value.Z);
}
