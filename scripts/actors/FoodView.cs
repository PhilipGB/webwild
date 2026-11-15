using Godot;
using Webwild.Core;

namespace Webwild.Game;

/// <summary>
/// Wild food presentation: the generated bush/mushroom/seed-head model, a warm
/// glow while ripe, a small sprout while regrowing, and a nearby prompt. All
/// availability comes from the simulation; the view never owns game state.
/// </summary>
public partial class FoodView : Node3D
{
    private static readonly Dictionary<string, PackedScene?> SceneCache = new(StringComparer.Ordinal);
    private static Texture2D? _glowTexture;
    private static StandardMaterial3D? _sproutMaterial;

    private Node3D _visual = null!;
    private Node3D _glow = null!;
    private MeshInstance3D _sprout = null!;
    private Label3D _prompt = null!;
    private bool _available = true;
    private bool _nearby;
    private string _lastText = "";
    private int _lastSeconds = -1;
    private float _time;

    public string FoodId => Source.Id;
    public bool ProbeVisible => _visual.Visible;
    public string ProbePrompt => _prompt.Text;
    public FoodSourceDefinition Source { get; private set; } = null!;

    public void Configure(FoodSourceDefinition source)
    {
        Source = source;
        var model = source.Kind switch
        {
            "mushrooms" => "mushroom",
            "seeds" => "sunflower",
            _ => "bush_berries"
        };
        var path = $"res://art/models/flora/{model}.glb";
        if (!SceneCache.TryGetValue(path, out var scene))
        {
            scene = GD.Load<PackedScene>(path);
            SceneCache[path] = scene;
        }
        if (scene is null)
            throw new InvalidOperationException($"Food model '{path}' is missing.");
        _visual = scene.Instantiate<Node3D>();
        _visual.Scale = Vector3.One * (source.Kind switch
        {
            "mushrooms" => 0.7f,
            "seeds" => 0.5f,
            _ => 0.48f
        });
        ArtMaterials.ApplyVertexColors(_visual);
        AddChild(_visual);

        _glow = new Node3D { Position = new Vector3(0,
            source.Kind == "mushrooms" ? 0.22f : 0.4f, 0) };
        var sprite = new Sprite3D
        {
            Texture = GlowTexture(),
            PixelSize = 0.005f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(1f, 0.93f, 0.58f, 0.38f),
            NoDepthTest = true
        };
        sprite.Scale = new Vector3(0.9f, 0.9f, 1);
        _glow.AddChild(sprite);
        AddChild(_glow);

        _sprout = new MeshInstance3D
        {
            Name = "RegrowingSprout",
            Position = new Vector3(0, 0.09f, 0),
            Mesh = new CylinderMesh
            {
                TopRadius = 0.03f,
                BottomRadius = 0.08f,
                Height = 0.2f,
                RadialSegments = 5
            },
            MaterialOverride = SproutMaterial(),
            Visible = false
        };
        AddChild(_sprout);

        _prompt = new Label3D
        {
            Text = "",
            FontSize = 29,
            PixelSize = 0.007f,
            Modulate = UiKit.Cream,
            OutlineModulate = UiKit.Pine,
            OutlineSize = 9,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = new Vector3(0,
                source.Kind == "mushrooms" ? 0.55f : 0.9f, 0),
            Visible = false
        };
        AddChild(_prompt);
        Position = new Vector3((float)source.Position.X, (float)source.Position.Y, (float)source.Position.Z);
    }

    public void UpdateView(double delta, bool reducedMotion,
        bool available, double secondsRemaining, bool nearby)
    {
        _time += (float)delta;
        if (available != _available)
        {
            _available = available;
            _visual.Visible = available;
            _glow.Visible = available;
            _sprout.Visible = !available;
            _lastText = "";
        }
        if (_glow.Visible && !reducedMotion)
        {
            var pulse = 1f + Mathf.Sin(_time * 3.4f) * 0.08f;
            _glow.Scale = new Vector3(pulse, pulse, 1);
        }

        var seconds = available ? 0 : Math.Max(1, (int)Math.Ceiling(secondsRemaining));
        if (nearby != _nearby || seconds != _lastSeconds || _lastText.Length == 0)
        {
            _nearby = nearby;
            _lastSeconds = seconds;
            _prompt.Visible = nearby;
            if (nearby)
            {
                _lastText = available
                    ? $"E · forage {Source.Label}"
                    : $"{Cap(Source.Label)} regrow in {seconds}s";
                _prompt.Text = _lastText;
            }
        }
    }

    private static string Cap(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static StandardMaterial3D SproutMaterial()
    {
        if (_sproutMaterial is not null) return _sproutMaterial;
        _sproutMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.42f, 0.65f, 0.34f),
            Roughness = 0.9f,
            VertexColorUseAsAlbedo = false
        };
        return _sproutMaterial;
    }

    private static Texture2D GlowTexture()
    {
        if (_glowTexture is not null) return _glowTexture;
        var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
        {
            var dx = (x - 15.5f) / 15.5f;
            var dy = (y - 15.5f) / 15.5f;
            var alpha = Mathf.Clamp(1 - Mathf.Sqrt(dx * dx + dy * dy), 0, 1);
            image.SetPixel(x, y, new Color(1, 1, 1, alpha * alpha));
        }
        _glowTexture = ImageTexture.CreateFromImage(image);
        return _glowTexture;
    }
}
