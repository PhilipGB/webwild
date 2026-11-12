using Godot;
using Webwild.Core;

namespace Webwild.Game;

public partial class WorkerView : Node3D
{
    private WorkerState _state = null!;
    private Node3D _visual = null!;
    private Label3D _nameplate = null!;
    private Sprite3D _activityGlow = null!;
    private float _flash;
    private float _time;
    private float _spawn;
    private Vector3 _lastPosition;

    public string WorkerId => _state.Id;

    public void Configure(WorkerState state)
    {
        _state = state;
        var file = state.Colour switch
        {
            "violet" => "spider_worker_violet",
            "green" => "spider_worker_green",
            "amber" => "spider_worker_orange",
            _ => "spider_worker_orange"
        };
        var scene = GD.Load<PackedScene>($"res://art/models/creatures/{file}.glb");
        _visual = scene?.Instantiate<Node3D>() ?? new Node3D();
        _visual.Scale = new Vector3(0.46f, 0.46f, 0.46f);
        AddChild(_visual);
        ArtMaterials.ApplyVertexColors(_visual);

        _nameplate = new Label3D
        {
            Text = $"{state.Name} · {state.DisplayState}",
            FontSize = 26,
            PixelSize = 0.006f,
            Modulate = UiKit.Cream,
            OutlineModulate = UiKit.Pine,
            OutlineSize = 7,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = new Vector3(0, 0.72f, 0),
            Visible = true
        };
        AddChild(_nameplate);

        _activityGlow = new Sprite3D
        {
            Texture = AnchorNode.RadialTexture,
            PixelSize = 0.013f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(1, 0.88f, 0.4f, 0),
            NoDepthTest = false,
            Visible = false,
            Position = new Vector3(0, 0.55f, 0)
        };
        AddChild(_activityGlow);

        Position = new Vector3((float)state.Position.X, 0.2f, (float)state.Position.Z);
        _lastPosition = Position;
        Scale = Vector3.Zero;
    }

    public void UpdateView(double delta, bool reducedMotion, bool near)
    {
        _time += (float)delta;
        _spawn = Mathf.Min(1, _spawn + (float)delta * 3.5f);
        Scale = Vector3.One * Mathf.Ease(_spawn, 0.4f) * (reducedMotion ? 1 : 1.0f);

        var onBridge = IsOnBridge();
        var target = new Vector3((float)_state.Position.X, onBridge ? 0.62f : 0.16f, (float)_state.Position.Z);
        Position = Position.Lerp(target, Mathf.Min(1, (float)delta * 12));
        if (!reducedMotion)
        {
            Position += new Vector3(0, Mathf.Sin(_time * 12) * 0.025f, 0);
        }
        var velocity = target - _lastPosition;
        if (velocity.LengthSquared() > 0.00002f)
        {
            var yaw = Mathf.Atan2(-velocity.X, -velocity.Z);
            _visual.Rotation = new Vector3(0, Mathf.LerpAngle(_visual.Rotation.Y, yaw, Mathf.Min(1, (float)delta * 12)), 0);
        }
        _lastPosition = target;
        _nameplate.Text = $"{_state.Name} · {_state.DisplayState}";
        _nameplate.Visible = near || _state.IsMoving;

        // A restrained gold halo marks a helper that is actively collecting,
        // repairing or delivering, and flashes when it completes a collection.
        _flash = Mathf.Max(0, _flash - (float)delta);
        var working = _state.Activity is WorkerActivity.Collecting or WorkerActivity.Repairing;
        var glow = Mathf.Max(working ? 0.45f + Mathf.Sin(_time * 4.2f) * 0.12f : 0f,
            Mathf.Min(1f, _flash * 1.6f));
        _activityGlow.Visible = glow > 0.03f;
        _activityGlow.Modulate = new Color(1, 0.88f, 0.4f, glow * 0.8f);
        _activityGlow.Scale = Vector3.One * (1.1f + Mathf.Sin(_time * 5f) * 0.12f);
    }

    public void FlashActivity() => _flash = 3.0f;

    public bool ProbeActivityGlowVisible =>
        _activityGlow.Visible || _flash > 0f ||
        _state.Activity is WorkerActivity.Collecting or WorkerActivity.Repairing;

    private bool IsOnBridge()
    {
        if (!_state.IsMoving) return false;
        var index = Mathf.Clamp(_state.PathIndex, 0, Mathf.Max(0, _state.PathEdges.Count - 1));
        return _state.PathEdges.Count > 0 && _state.PathEdges[index].UsesBridge;
    }

    public void Celebrate()
    {
        _spawn = 0;
    }
}
