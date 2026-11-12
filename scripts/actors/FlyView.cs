using Godot;
using Webwild.Core;

namespace Webwild.Game;

public partial class FlyView : Node3D
{
    private InsectState _state = null!;
    private Node3D _visual = null!;
    private Label3D _prompt = null!;
    private Node3D _glow = null!;
    private float _time;
    private Vector3 _lastPosition;

    public string InsectId => _state.Id;

    public void Configure(InsectState state)
    {
        _state = state;
        var isReedwaterModel = state.Definition.Model == "dragonfly";
        var modelPath = isReedwaterModel
            ? "res://art/models/reedwater/dragonfly.glb"
            : $"res://art/models/creatures/{state.Definition.Model}.glb";
        var scene = GD.Load<PackedScene>(modelPath);
        if (scene is null)
            throw new InvalidOperationException($"Insect model '{modelPath}' is missing.");
        _visual = scene.Instantiate<Node3D>();
        _visual.Scale = new Vector3(0.5f, 0.5f, 0.5f);
        AddChild(_visual);
        ArtMaterials.ApplyVertexColors(_visual, isReedwaterModel);

        _glow = new Node3D { Position = new Vector3(0, 0.18f, 0) };
        var sprite = new Sprite3D
        {
            Texture = AnchorGlowTexture(),
            PixelSize = 0.005f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(1, 0.95f, 0.6f, 0.4f),
            NoDepthTest = true
        };
        sprite.Scale = new Vector3(0.7f, 0.7f, 1);
        _glow.AddChild(sprite);
        AddChild(_glow);

        _prompt = new Label3D
        {
            Text = "E  collect",
            FontSize = 30,
            PixelSize = 0.007f,
            Modulate = UiKit.Cream,
            OutlineModulate = UiKit.Pine,
            OutlineSize = 9,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = new Vector3(0, 0.75f, 0),
            Visible = false
        };
        AddChild(_prompt);
        Position = new Vector3((float)state.Position.X, (float)state.Position.Y, (float)state.Position.Z);
        _lastPosition = Position;
    }

    private static Texture2D? _glowTexture;
    private static Texture2D AnchorGlowTexture()
    {
        if (_glowTexture is not null) return _glowTexture;
        var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
        {
            var dx = (x - 15.5f) / 15.5f;
            var dy = (y - 15.5f) / 15.5f;
            var a = Mathf.Clamp(1 - Mathf.Sqrt(dx * dx + dy * dy), 0, 1);
            image.SetPixel(x, y, new Color(1, 1, 1, a * a));
        }
        _glowTexture = ImageTexture.CreateFromImage(image);
        return _glowTexture;
    }

    public void SetPromptVisible(bool visible) => _prompt.Visible = visible && _state.Phase == InsectPhase.Trapped;

    public void UpdateView(double delta, bool reducedMotion)
    {
        _time += (float)delta;
        if (_state.Phase == InsectPhase.Retrieved)
        {
            Visible = false;
            return;
        }
        Visible = true;
        var target = new Vector3((float)_state.Position.X, (float)_state.Position.Y, (float)_state.Position.Z);
        if (_state.Phase == InsectPhase.Trapped)
        {
            var tremble = reducedMotion ? 0f : Mathf.Sin(_time * 30) * 0.025f;
            Position = target + new Vector3(tremble, 0, 0);
            _visual.Rotation = new Vector3(0, _visual.Rotation.Y + (reducedMotion ? 0 : Mathf.Sin(_time * 20) * 0.008f), 0);
        }
        else
        {
            Position = Position.Lerp(target, Mathf.Min(1, (float)delta * 20));
            var velocity = target - _lastPosition;
            if (velocity.LengthSquared() > 0.0001f)
            {
                var yaw = Mathf.Atan2(-velocity.X, -velocity.Z);
                _visual.Rotation = new Vector3(0, Mathf.LerpAngle(_visual.Rotation.Y, yaw, Mathf.Min(1, (float)delta * 8)), 0);
            }
            var flutter = reducedMotion ? 0f : Mathf.Sin(_time * 24) * 0.12f;
            _visual.Scale = new Vector3(0.5f, 0.5f + flutter, 0.5f);
            _lastPosition = target;
        }
        _glow.Visible = _state.Phase == InsectPhase.Flying;
    }
}
