using Godot;
using Webwild.Core;

namespace Webwild.Game;

public partial class WebVisual : Node3D
{
    public string WebId { get; private set; } = "";
    public WebType Type { get; private set; }

    private WebState _state = null!;
    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;
    private Label3D _repairLabel = null!;
    private StaticBody3D? _bridgeBody;
    private BridgeVisualAssembler? _bridgeVisual;
    private Vector3 _center;
    private int _lastDurability = -1;
    private readonly List<Vector3> _rampGroundPoints = new();

    public IReadOnlyList<Vector3> RampGroundPoints => _rampGroundPoints;
    public int WalkwayCollisionShapeCount => _bridgeBody?.GetChildCount() ?? 0;
    public string ProbeBridgeVisualSignature => _bridgeVisual?.ProbeSignature ?? "none";
    public int ProbeBridgePlankCount => _bridgeVisual?.PlankCount ?? 0;
    public int ProbeBridgeDetailCount => _bridgeVisual?.DetailCount ?? 0;
    public int ProbeBridgeRailTriangles => _bridgeVisual?.RailTriangleCount ?? 0;
    public int ProbeBridgeDamageOverlayCount => _bridgeVisual?.DamageOverlayCount ?? 0;
    public int ProbeBridgeUnstyledMeshCount => _bridgeVisual?.UnstyledMeshCount ?? -1;
    public bool ProbeBridgeUsesAllPlankVariants => _bridgeVisual?.HasAllPlankVariants == true;

    public void Configure(WebState state, Vector3 a, Vector3 b)
    {
        WebId = state.Id;
        Type = state.Type;
        _state = state;
        _center = (a + b) * 0.5f;
        Position = _center;

        if (Type == WebType.Capture)
        {
            var color = UiKit.Dew;
            _material = WebMeshFactory.SilkMaterial(color);
            var dir = (b - a).Normalized();
            var normal = dir.Cross(Vector3.Up).Normalized();
            if (normal.LengthSquared() < 0.01f) normal = Vector3.Back;
            var yAxis = normal.Cross(dir).Normalized();
            if (yAxis.Y < 0) yAxis = -yAxis;
            var xAxis = dir;
            var basis = new Basis(xAxis, yAxis, normal);
            Transform = new Transform3D(basis, _center);
            var halfWidth = Mathf.Max(0.65f, a.DistanceTo(b) * 0.5f + 0.2f);
            var height = Mathf.Max(0.95f, (float)state.MaxDurability / 100f * 1.25f);
            var mesh = WebMeshFactory.BuildCapture(halfWidth, height, color);
            _mesh = new MeshInstance3D { Mesh = mesh, MaterialOverride = _material, Name = "CaptureSilk" };
            AddChild(_mesh);
            AddGlints(halfWidth, height);
        }
        else
        {
            var approach = BridgeGeometry.ComputeApproachPoints(
                ToCoreVec3(a),
                ToCoreVec3(b),
                RegionCatalog.FindAnchor(state.AnchorA)?.BridgeGroundPoint,
                RegionCatalog.FindAnchor(state.AnchorB)?.BridgeGroundPoint);
            var groundA = ToVector3(approach.A);
            var groundB = ToVector3(approach.B);
            _bridgeVisual = new BridgeVisualAssembler { Name = "ModularBridge" };
            AddChild(_bridgeVisual);
            _bridgeVisual.Configure(
                state.Id, a - _center, b - _center,
                groundA - _center, groundB - _center,
                BridgeVisualMode.Constructed,
                state.MaxDurability <= 0 ? 1f : (float)state.Durability / state.MaxDurability);
            BuildBridgeCollision(a, b, approach);
        }

        _repairLabel = new Label3D
        {
            Text = "Repair · E",
            FontSize = 30,
            PixelSize = 0.007f,
            Modulate = UiKit.Coral,
            OutlineModulate = UiKit.Pine,
            OutlineSize = 8,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = new Vector3(0, 1.15f, 0),
            Visible = false
        };
        AddChild(_repairLabel);
        UpdateState(state);
    }

    private void AddGlints(float halfWidth, float height)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = UiKit.White,
            EmissionEnabled = true,
            Emission = UiKit.Dew,
            EmissionEnergyMultiplier = 0.8f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        var sphere = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 6, Rings = 4 };
        for (var i = 0; i < 10; i++)
        {
            var angle = i * 2.399f;
            var radius = 0.35f + 0.55f * (i % 3) / 2f;
            var glint = new MeshInstance3D
            {
                Mesh = sphere,
                MaterialOverride = material,
                Position = new Vector3(Mathf.Cos(angle) * halfWidth * radius, Mathf.Sin(angle) * height * radius, 0.02f)
            };
            AddChild(glint);
        }
    }

    private void BuildBridgeCollision(
        Vector3 a, Vector3 b, BridgeGeometry.ApproachPoints approach)
    {
        var aLocal = a - _center;
        var bLocal = b - _center;
        _bridgeBody = new StaticBody3D
        {
            Name = "BridgeWalkway",
            CollisionLayer = 1,
            CollisionMask = 0
        };
        AddChild(_bridgeBody);

        var samples = 7;
        var previous = aLocal;
        for (var i = 1; i <= samples; i++)
        {
            var point = ToVector3(BridgeGeometry.EvaluateSpan(
                ToCoreVec3(aLocal), ToCoreVec3(bLocal), i / (double)samples));
            AddWalkwayCollision(previous, point);
            previous = point;
        }

        // Walkable approaches connect the bridge anchors to solid ground on both
        // banks. The shared core helper uses authored bank points for the ravine
        // posts, so this is correct whichever anchor the player selected first.
        var leftGround = ToVector3(approach.A) - _center;
        var rightGround = ToVector3(approach.B) - _center;
        _rampGroundPoints.Clear();
        _rampGroundPoints.Add(ToVector3(approach.A));
        _rampGroundPoints.Add(ToVector3(approach.B));
        AddWalkwayCollision(leftGround, aLocal);
        AddWalkwayCollision(bLocal, rightGround);
    }

    private static Vec3 ToCoreVec3(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3 ToVector3(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private void AddWalkwayCollision(Vector3 from, Vector3 to)
    {
        var delta = to - from;
        var length = delta.Length();
        if (length < 0.01f) return;
        var z = delta.Normalized();
        var x = Vector3.Up.Cross(z);
        if (x.LengthSquared() < 0.0001f) x = Vector3.Right;
        x = x.Normalized();
        var y = z.Cross(x).Normalized();
        var basis = new Basis(x, y, z);
        var centre = (from + to) * 0.5f - y * 0.05f;

        _bridgeBody!.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.9f, 0.1f, length + 0.08f) },
            Transform = new Transform3D(basis, centre)
        });
    }

    public void UpdateState(WebState state)
    {
        _state = state;
        if (_lastDurability == state.Durability) return;
        _lastDurability = state.Durability;
        var ratio = state.MaxDurability <= 0 ? 0 : (float)state.Durability / state.MaxDurability;
        if (Type == WebType.Bridge)
        {
            _bridgeVisual?.SetDamageRatio(ratio);
        }
        else
        {
            var baseColor = UiKit.Dew;
            var color = ratio > 0.7f ? baseColor : ratio > 0 ? UiKit.Gold : UiKit.Coral;
            if (ratio <= 0.35f) color = UiKit.Coral;
            _material.AlbedoColor = new Color(color.R, color.G, color.B, ratio <= 0 ? 0.6f : 0.92f);
            _material.Emission = color;
            _material.EmissionEnergyMultiplier = ratio <= 0 ? 0.1f : 0.55f;
            if (_mesh is not null)
                _mesh.Scale = ratio <= 0 ? new Vector3(1, 0.8f, 1) : Vector3.One;
        }
        if (_repairLabel is not null) _repairLabel.Visible = state.Durability < state.MaxDurability;
    }

    public void SetNearby(bool nearby)
    {
        if (_repairLabel is not null) _repairLabel.Visible = _state.Durability < _state.MaxDurability && nearby;
    }

    public void SetPreviewColor(bool valid)
    {
        if (Type == WebType.Bridge)
        {
            _bridgeVisual?.SetPreviewColor(valid);
            return;
        }
        var color = valid ? UiKit.FreshLeaf : UiKit.Coral;
        _material.AlbedoColor = new Color(color.R, color.G, color.B, 0.42f);
        _material.Emission = color;
    }

    public static Node3D CreatePreview(
        WebType type, Vector3 a, Vector3 b, bool valid = true,
        Vector3? groundA = null, Vector3? groundB = null)
    {
        var color = valid ? UiKit.FreshLeaf : UiKit.Coral;
        var material = WebMeshFactory.SilkMaterial(new Color(color.R, color.G, color.B, 0.45f), true);
        var node = new Node3D { Name = "WebPreview" };
        if (type == WebType.Capture)
        {
            var dir = (b - a).Normalized();
            var normal = dir.Cross(Vector3.Up).Normalized();
            if (normal.LengthSquared() < 0.01f) normal = Vector3.Back;
            var yAxis = normal.Cross(dir).Normalized();
            if (yAxis.Y < 0) yAxis = -yAxis;
            node.Transform = new Transform3D(new Basis(dir, yAxis, normal), (a + b) * 0.5f);
            var halfWidth = Mathf.Max(0.65f, a.DistanceTo(b) * 0.5f + 0.2f);
            var mesh = new MeshInstance3D
            {
                Mesh = WebMeshFactory.BuildCapture(halfWidth, 1.25f, color), MaterialOverride = material
            };
            node.AddChild(mesh);
        }
        else
        {
            var centre = (a + b) * 0.5f;
            node.Position = centre;
            var assembler = new BridgeVisualAssembler { Name = "BridgePreview" };
            node.AddChild(assembler);
            assembler.Configure(
                "preview", a - centre, b - centre,
                groundA is { } ga ? ga - centre : null,
                groundB is { } gb ? gb - centre : null,
                valid ? BridgeVisualMode.PreviewValid : BridgeVisualMode.PreviewInvalid);
        }
        return node;
    }
}
