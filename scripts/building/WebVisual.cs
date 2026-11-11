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
    private Vector3 _center;
    private int _lastDurability = -1;

    public void Configure(WebState state, Vector3 a, Vector3 b)
    {
        WebId = state.Id;
        Type = state.Type;
        _state = state;
        _center = (a + b) * 0.5f;
        Position = _center;
        var color = Type == WebType.Capture ? UiKit.Dew : UiKit.Gold;
        _material = WebMeshFactory.SilkMaterial(color);

        if (Type == WebType.Capture)
        {
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
            var mesh = WebMeshFactory.BuildBridge(a, b, color);
            _mesh = new MeshInstance3D { Mesh = mesh, MaterialOverride = _material, Name = "BridgeSilk" };
            AddChild(_mesh);
            BuildBridgeCollision(a, b);
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

    private void BuildBridgeCollision(Vector3 a, Vector3 b)
    {
        var dir = (b - a).Normalized();
        var length = a.DistanceTo(b);
        var z = dir;
        var x = Vector3.Up.Cross(z).Normalized();
        var y = z.Cross(x).Normalized();
        var basis = new Basis(x, y, z);
        _bridgeBody = new StaticBody3D
        {
            CollisionLayer = 1,
            CollisionMask = 0,
            Transform = new Transform3D(basis, new Vector3(0, -0.04f, 0))
        };
        var shape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.75f, 0.16f, length) }
        };
        _bridgeBody.AddChild(shape);
        AddChild(_bridgeBody);
    }

    public void UpdateState(WebState state)
    {
        _state = state;
        if (_lastDurability == state.Durability) return;
        _lastDurability = state.Durability;
        var ratio = state.MaxDurability <= 0 ? 0 : (float)state.Durability / state.MaxDurability;
        var baseColor = Type == WebType.Capture ? UiKit.Dew : UiKit.Gold;
        var color = ratio > 0.7f ? baseColor : ratio > 0 ? UiKit.Gold : UiKit.Coral;
        if (ratio <= 0.35f) color = UiKit.Coral;
        _material.AlbedoColor = new Color(color.R, color.G, color.B, ratio <= 0 ? 0.6f : 0.92f);
        _material.Emission = color;
        _material.EmissionEnergyMultiplier = ratio <= 0 ? 0.1f : 0.55f;
        if (_repairLabel is not null) _repairLabel.Visible = state.Durability < state.MaxDurability;
        if (_mesh is not null && Type == WebType.Capture)
        {
            _mesh.Scale = ratio <= 0 ? new Vector3(1, 0.8f, 1) : Vector3.One;
        }
    }

    public void SetNearby(bool nearby)
    {
        if (_repairLabel is not null) _repairLabel.Visible = _state.Durability < _state.MaxDurability && nearby;
    }

    public void SetPreviewColor(bool valid)
    {
        var color = valid ? UiKit.FreshLeaf : UiKit.Coral;
        _material.AlbedoColor = new Color(color.R, color.G, color.B, 0.42f);
        _material.Emission = color;
    }

    public static Node3D CreatePreview(WebType type, Vector3 a, Vector3 b, bool valid = true)
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
            node.Position = (a + b) * 0.5f;
            node.AddChild(new MeshInstance3D
            {
                Mesh = WebMeshFactory.BuildBridge(a, b, color), MaterialOverride = material
            });
        }
        return node;
    }
}
