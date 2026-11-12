using Godot;
using Webwild.Core;

namespace Webwild.Game;

public partial class SpiderPlayer : CharacterBody3D
{
    [Export] public float WalkSpeed { get; set; } = 5.4f;
    [Export] public float JumpVelocity { get; set; } = 6.4f;
    [Export] public float Gravity { get; set; } = 19f;
    [Export] public float ClimbSpeed { get; set; } = 2.5f;

    public bool InputEnabled { get; set; } = true;
    public bool PhysicsPaused { get; set; }
    public bool ClimbAvailable { get; set; }
    public Vector3 ClimbPoint { get; set; }
    public Vector3 FallResetPosition { get; set; } = new(-13.5f, 1.0f, 8.5f);
    public bool IsClimbing { get; private set; }
    public float FacingYaw => _visual.Rotation.Y;
    public Camera3D Camera { get; private set; } = null!;
    public Node3D CameraPivot { get; private set; } = null!;
    public SpringArm3D SpringArm { get; private set; } = null!;

    private Node3D _visual = null!;
    private Vector3 _visualHome;
    private float _coyote;
    private float _jumpBuffer;
    private float _yaw;
    private float _bobTime;
    private float _defaultZoom = 10f;
    private bool _wasOnFloor;
    private MeshInstance3D _shadow = null!;
    private float _landSquash;
    private Vector3 _lastSafePosition;

    public override void _Ready()
    {
        _visual = GetNode<Node3D>("Visual");
        _visualHome = _visual.Position;
        ArtMaterials.ApplyVertexColors(_visual);
        CameraPivot = GetNode<Node3D>("CameraPivot");
        SpringArm = GetNode<SpringArm3D>("CameraPivot/SpringArm3D");
        Camera = GetNode<Camera3D>("CameraPivot/SpringArm3D/Camera3D");
        _yaw = CameraPivot.Rotation.Y;
        _defaultZoom = SpringArm.SpringLength;
        _lastSafePosition = GlobalPosition;
        BuildShadow();
    }

    private void BuildShadow()
    {
        var mesh = new CylinderMesh { TopRadius = 0.58f, BottomRadius = 0.62f, Height = 0.015f, RadialSegments = 20 };
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.06f, 0.12f, 0.09f, 0.28f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = false
        };
        _shadow = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = new Vector3(0, -0.42f, 0) };
        AddChild(_shadow);
    }

    public void ApplyOrbit(float relativeX, double sensitivity)
    {
        _yaw -= relativeX * 0.006f * (float)sensitivity;
        CameraPivot.Rotation = new Vector3(0, Mathf.Wrap(_yaw, -Mathf.Pi, Mathf.Pi), 0);
    }

    public void Zoom(double wheelDelta)
    {
        SpringArm.SpringLength = Mathf.Clamp(SpringArm.SpringLength - (float)wheelDelta * 0.8f, 5.5f, 15f);
    }

    public void ResetCamera()
    {
        _yaw = 0.35f;
        CameraPivot.Rotation = new Vector3(0, _yaw, 0);
        SpringArm.SpringLength = _defaultZoom;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (PhysicsPaused) return;
        var dt = (float)delta;
        if (!InputEnabled)
        {
            Velocity = new Vector3(0, Velocity.Y - Gravity * dt, 0);
            if (IsOnFloor()) Velocity = new Vector3(0, 0, 0);
            MoveAndSlide();
            return;
        }

        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var jumpPressed = Input.IsActionJustPressed("jump");

        if (jumpPressed) _jumpBuffer = 0.16f;
        else _jumpBuffer = Mathf.Max(0, _jumpBuffer - dt);

        if (IsOnFloor()) _coyote = 0.12f;
        else _coyote = Mathf.Max(0, _coyote - dt);

        // Assisted climb: only inside an authored volume, never arbitrary wall climbing.
        if (ClimbAvailable)
        {
            var wantsUp = input.Y < -0.25f;
            var wantsDown = input.Y > 0.25f;
            if (!IsClimbing)
            {
                if ((wantsUp && GlobalPosition.Y < 4.25f) ||
                    (wantsDown && GlobalPosition.Y > 1.0f && GlobalPosition.Y < 4.8f))
                {
                    IsClimbing = true;
                    CollisionMask = 0;
                }
            }
            if (IsClimbing)
            {
                var xz = new Vector3(ClimbPoint.X, GlobalPosition.Y, ClimbPoint.Z);
                GlobalPosition = new Vector3(
                    Mathf.Lerp(GlobalPosition.X, xz.X, Mathf.Min(1, dt * 6)),
                    GlobalPosition.Y,
                    Mathf.Lerp(GlobalPosition.Z, xz.Z, Mathf.Min(1, dt * 6)));
                var vertical = wantsUp ? ClimbSpeed : wantsDown ? -ClimbSpeed : 0;
                Velocity = new Vector3(0, vertical, 0);
                if (GlobalPosition.Y >= 4.35f && wantsUp)
                {
                    GlobalPosition = new Vector3(ClimbPoint.X, 4.58f, ClimbPoint.Z);
                    Velocity = new Vector3(0, 0.8f, 0);
                    IsClimbing = false;
                    CollisionMask = 1;
                }
                else if (GlobalPosition.Y <= 0.25f && wantsDown)
                {
                    IsClimbing = false;
                    CollisionMask = 1;
                }
                if (jumpPressed)
                {
                    IsClimbing = false;
                    CollisionMask = 1;
                    Velocity = new Vector3(0, JumpVelocity * 0.7f, 0);
                }
                MoveAndSlide();
                AnimateVisual(dt, input, true);
                UpdateShadow();
                return;
            }
        }
        else if (IsClimbing)
        {
            IsClimbing = false;
            CollisionMask = 1;
        }

        // Camera-relative ground movement.
        var basis = CameraPivot.GlobalTransform.Basis;
        var forward = -basis.Z;
        forward.Y = 0;
        forward = forward.Normalized();
        var right = basis.X;
        right.Y = 0;
        right = right.Normalized();
        var direction = (forward * -input.Y + right * input.X);
        if (direction.LengthSquared() > 1) direction = direction.Normalized();
        var horizontal = direction * WalkSpeed;
        Velocity = new Vector3(horizontal.X, Velocity.Y, horizontal.Z);

        if (_jumpBuffer > 0 && _coyote > 0)
        {
            Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
            _jumpBuffer = 0;
            _coyote = 0;
        }
        Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * dt, Velocity.Z);

        var wasFloor = IsOnFloor();
        MoveAndSlide();
        if (!wasFloor && IsOnFloor()) _landSquash = 1;
        _landSquash = Mathf.Max(0, _landSquash - dt * 4.5f);

        if (GlobalPosition.Y < -4)
        {
            GlobalPosition = FallResetPosition;
            Velocity = Vector3.Zero;
        }
        if (GlobalPosition.Y > -1) _lastSafePosition = GlobalPosition;
        AnimateVisual(dt, input, false);
        UpdateShadow();
    }

    private void AnimateVisual(float dt, Vector2 input, bool climbing)
    {
        var moving = input.LengthSquared() > 0.05f || climbing;
        _bobTime += dt * (moving ? 11f : 3f);
        var bob = moving ? Mathf.Sin(_bobTime) * 0.035f : Mathf.Sin(_bobTime) * 0.012f;
        var targetScale = new Vector3(1, 1 - _landSquash * 0.16f, 1 + _landSquash * 0.08f);
        _visual.Position = _visualHome + new Vector3(0, bob, 0);
        _visual.Scale = _visual.Scale.Lerp(targetScale, Mathf.Min(1, dt * 10));
        _visual.Rotation = new Vector3(
            climbing ? Mathf.DegToRad(-18) : 0,
            _visual.Rotation.Y,
            moving ? Mathf.Sin(_bobTime * 0.5f) * 0.035f : 0);

        if (input.LengthSquared() > 0.02f && !climbing)
        {
            var basis = CameraPivot.GlobalTransform.Basis;
            var forward = -basis.Z;
            forward.Y = 0;
            var right = basis.X;
            right.Y = 0;
            var direction = (forward.Normalized() * -input.Y + right.Normalized() * input.X).Normalized();
            var targetYaw = Mathf.Atan2(-direction.X, -direction.Z);
            _visual.Rotation = new Vector3(_visual.Rotation.X,
                Mathf.LerpAngle(_visual.Rotation.Y, targetYaw, Mathf.Min(1, dt * 10)), _visual.Rotation.Z);
        }
        _shadow.Position = new Vector3(0, -GlobalPosition.Y + 0.02f, 0);
        _shadow.GlobalRotation = Vector3.Zero;
    }

    private void UpdateShadow()
    {
        _shadow.GlobalPosition = new Vector3(GlobalPosition.X, 0.02f, GlobalPosition.Z);
    }

    public Vec3 ToCorePosition() => new(GlobalPosition.X, GlobalPosition.Y, GlobalPosition.Z);
}
