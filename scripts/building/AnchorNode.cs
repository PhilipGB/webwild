using Godot;
using Webwild.Core;

namespace Webwild.Game;

public partial class AnchorNode : Area3D
{
    /// <summary>
    /// Stable gameplay id authored on the scene instance. It links this visual
    /// socket to the engine-free anchor definition and is validated at startup.
    /// </summary>
    [Export] public string AnchorId { get; set; } = "";

    public AnchorDefinition Definition { get; private set; } = null!;
    public bool IsSelected { get; private set; }
    public bool IsHovered { get; private set; }
    public bool IsSuggested { get; private set; }
    public bool PreviewValid { get; private set; } = true;

    private Sprite3D _glow = null!;
    private Label3D _label = null!;
    private bool _buildMode;
    private float _time;
    private float _playerDistance = 999f;
    private static Texture2D? _radial;

    public override void _Ready()
    {
        // Exported builds can drop imported-child overrides from the authored
        // socket scene; keep the ring palette identical to the editor.
        ArtMaterials.ApplyToModelInstances(this);
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
        // The authored scene transform is authoritative for presentation. The
        // startup validator guarantees it still matches the simulation position,
        // so a moved socket fails loudly instead of being silently snapped back.
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

    /// <summary>Distance from the player, used to keep distant labels readable.</summary>
    public void SetPlayerDistance(float distance)
    {
        _playerDistance = distance;
        if (_buildMode) UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (_glow is null) return;
        var recommended = IsSuggested;
        _glow.Visible = _buildMode || IsSelected || IsSuggested;
        // Every valid socket used to carry a label, which turned overlapping
        // Reedwater sockets into an unreadable cluster. Only the recommended,
        // selected or hovered socket shows a label at a distance; nearby sockets
        // still identify themselves.
        _label.Visible = _buildMode && (recommended || IsSelected || IsHovered || _playerDistance <= 5.5f);
        if (_label.Visible)
        {
            _label.Text = (recommended ? "★ " : "") + (Definition?.Label ?? "Anchor");
            _label.FontSize = recommended || IsSelected ? 30 : 24;
        }
        if (!_buildMode && !IsSelected && !IsSuggested) return;
        var color = IsSelected ? UiKit.Teal
            : IsHovered ? (PreviewValid ? UiKit.FreshLeaf : UiKit.Coral)
            : recommended ? UiKit.Gold
            : new Color(UiKit.Cream, 0.85f);
        var alpha = IsSelected || IsHovered || recommended ? 0.95f : 0.24f;
        _glow.Modulate = new Color(color.R, color.G, color.B, alpha);
        _label.Modulate = new Color(color.R, color.G, color.B,
            IsSelected || IsHovered || recommended ? 1f : 0.62f);
    }

    public override void _Process(double delta)
    {
        if (!_buildMode && !IsSelected && !IsSuggested) return;
        _time += (float)delta;
        var pulse = 1.0f + Mathf.Sin(_time * 5.2f) * 0.12f;
        var scale = IsSelected ? 2.15f
            : IsHovered ? 1.9f
            : IsSuggested ? 1.75f
            : 1.05f;
        _glow.Scale = new Vector3(scale * pulse, scale * pulse, 1);
        _glow.Position = new Vector3(0, 0.15f + Mathf.Sin(_time * 2.6f) * 0.06f, 0);
    }

    public string ProbeVisualState =>
        $"glow={_glow?.Visible == true};label={_label?.Visible == true};" +
        $"alpha={_glow?.Modulate.A ?? 0:0.00};suggested={IsSuggested};selected={IsSelected};" +
        $"hovered={IsHovered};distance={_playerDistance:0.0}";

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
