using Godot;
using Webwild.Core;

namespace Webwild.Game;

public partial class AnchorNode : Area3D
{
    public AnchorDefinition Definition { get; private set; } = null!;
    public bool IsSelected { get; private set; }
    public bool IsHovered { get; private set; }
    public bool IsSuggested { get; private set; }
    public bool PreviewValid { get; private set; } = true;

    private Sprite3D _glow = null!;
    private Label3D _label = null!;
    private bool _buildMode;
    private float _time;
    private static Texture2D? _radial;

    public override void _Ready()
    {
        CollisionLayer = 4;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = true;
        if (GetNodeOrNull<CollisionShape3D>("CollisionShape3D") is null)
        {
            var shape = new CollisionShape3D
            {
                Shape = new SphereShape3D { Radius = 0.75f },
                Position = new Vector3(0, 0, 0)
            };
            AddChild(shape);
        }

        _glow = new Sprite3D
        {
            Texture = GetRadialTexture(),
            PixelSize = 0.014f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(1, 1, 1, 0.9f),
            NoDepthTest = false,
            Visible = false,
            Position = new Vector3(0, 0.15f, 0)
        };
        _glow.Scale = new Vector3(1.5f, 1.5f, 1);
        AddChild(_glow);

        _label = new Label3D
        {
            Text = Definition?.Label ?? "Anchor",
            FontSize = 28,
            PixelSize = 0.006f,
            Modulate = UiKit.Cream,
            OutlineModulate = UiKit.Pine,
            OutlineSize = 8,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Visible = false,
            Position = new Vector3(0, -0.75f, 0)
        };
        AddChild(_label);
        UpdateVisuals();
    }

    public void Configure(AnchorDefinition definition)
    {
        Definition = definition;
        Position = new Vector3((float)definition.Position.X, (float)definition.Position.Y, (float)definition.Position.Z);
        if (_label is not null) _label.Text = definition.Label;
    }

    public void SetBuildMode(bool active)
    {
        _buildMode = active;
        UpdateVisuals();
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        UpdateVisuals();
    }

    public void SetSuggested(bool suggested)
    {
        IsSuggested = suggested;
        UpdateVisuals();
    }

    public void SetHovered(bool hovered, bool previewValid = true)
    {
        IsHovered = hovered;
        PreviewValid = previewValid;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (_glow is null) return;
        _glow.Visible = _buildMode || IsSelected || IsSuggested;
        _label.Visible = _buildMode;
        if (!_buildMode && !IsSelected && !IsSuggested) return;
        var color = IsSelected ? UiKit.Teal
            : IsHovered ? (PreviewValid ? UiKit.FreshLeaf : UiKit.Coral)
            : IsSuggested && !_buildMode ? UiKit.Gold
            : UiKit.Gold;
        _glow.Modulate = new Color(color.R, color.G, color.B, 0.95f);
        _label.Modulate = color;
    }

    public override void _Process(double delta)
    {
        if (!_buildMode && !IsSelected && !IsSuggested) return;
        _time += (float)delta;
        var pulse = 1.0f + Mathf.Sin(_time * 5.2f) * 0.12f;
        var scale = IsSelected ? 2.15f : IsHovered ? 1.9f : IsSuggested && !_buildMode ? 1.3f : 1.5f;
        _glow.Scale = new Vector3(scale * pulse, scale * pulse, 1);
        _glow.Position = new Vector3(0, 0.15f + Mathf.Sin(_time * 2.6f) * 0.06f, 0);
    }

    private static Texture2D GetRadialTexture()
    {
        if (_radial is not null) return _radial;
        var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
        {
            var dx = (x - 31.5f) / 31.5f;
            var dy = (y - 31.5f) / 31.5f;
            var d = Mathf.Sqrt(dx * dx + dy * dy);
            var alpha = Mathf.Clamp(1 - d, 0, 1);
            alpha = Mathf.Pow(alpha, 1.5f);
            var ring = Mathf.Exp(-Mathf.Pow((d - 0.62f) * 9f, 2)) * 0.7f;
            image.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp(alpha * 0.8f + ring, 0, 1)));
        }
        _radial = ImageTexture.CreateFromImage(image);
        return _radial;
    }
}
