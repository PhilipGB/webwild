using Godot;
using Webwild.Core;

namespace Webwild.Game;

public partial class SpiderPlayer : CharacterBody3D
{
    [Export] public float WalkSpeed { get; set; } = 5.4f;
    [Export] public float JumpVelocity { get; set; } = 6.4f;
    /// <summary>The bigger, gap-crossing leap: hold the jump key while rising
    /// to sustain the jump into this stronger launch.</summary>
    [Export] public float LeapVelocity { get; set; } = 8.6f;
    [Export] public float Gravity { get; set; } = 19f;
    [Export] public float ClimbSpeed { get; set; } = 2.5f;

    public bool InputEnabled { get; set; } = true;
    public bool PhysicsPaused { get; set; }
    /// <summary>Hint-only signal set by the world: an authored climb is close
    /// (used for the prompt label). Movement never reads it — climbing is
    /// decided by the surface the spider actually touches.</summary>
    public bool ClimbAvailable { get; set; }
    public Vector3 ClimbPoint { get; set; }
    public Vector3 FallResetPosition { get; set; } = new(-13.5f, 1.0f, 8.5f);
    public bool IsClimbing { get; private set; }
    /// <summary>Normal of the face currently being climbed (diagnostics).</summary>
    public Vector3 ClimbNormal { get; private set; } = Vector3.Back;
    public float FacingYaw => _visual.Rotation.Y;
    public Camera3D Camera { get; private set; } = null!;
    public Node3D CameraPivot { get; private set; } = null!;
    public SpringArm3D SpringArm { get; private set; } = null!;

    private Node3D _visual = null!;
    private Vector3 _visualHome;
    private float _coyote;
    private float _jumpBuffer;
    private float _leapHold;
    private float _climbCooldown;
    private float _yaw;
    private float _bobTime;
    private float _defaultZoom = 10f;
    private bool _wasOnFloor;
    private MeshInstance3D _shadow = null!;
    private float _landSquash;
    private Vector3 _lastSafePosition;

    // A face steeper than this (normal nearly horizontal) is a climbable
    // wall; anything gentler is already walkable ground. Just under the
    // walkable floor limit (cos 55°), so every non-walkable face qualifies.
    private const float ClimbFaceNormalY = 0.56f;
    private const float ClimbProbeDistance = 0.95f;
    private const float LeapReattachCooldown = 0.28f;

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
        // A child should never need a jump to climb a bridge joint. The walkable
        // surface is continuous, and these tolerances absorb tiny mesh steps.
        FloorMaxAngle = Mathf.DegToRad(55);
        FloorSnapLength = 0.4f;
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
        if (IsClimbing && !InputEnabled)
        {
            // A climb never survives a mode that disables movement; drop
            // safely onto whatever is below rather than freezing mid-wall.
            IsClimbing = false;
        }
        if (!InputEnabled)
        {
            Velocity = new Vector3(0, Velocity.Y - Gravity * dt, 0);
            if (IsOnFloor()) Velocity = new Vector3(0, 0, 0);
            MoveAndSlide();
            AnimateVisual(dt, Vector2.Zero, false);
            UpdateShadow();
            return;
        }

        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var jumpPressed = Input.IsActionJustPressed("jump");
        _climbCooldown = Mathf.Max(0, _climbCooldown - dt);

        if (jumpPressed) _jumpBuffer = 0.16f;
        else _jumpBuffer = Mathf.Max(0, _jumpBuffer - dt);

        if (IsOnFloor()) _coyote = 0.12f;
        else _coyote = Mathf.Max(0, _coyote - dt);

        // ---- Surface-aware climbing ----
        // Real collision queries decide whether the spider can climb: the
        // face must be steep, and it must be generated version-2 terrain or
        // an explicitly climbable authored surface. Walkable slopes, hedges
        // and the ravine walls stay ordinary ground.
        if (IsClimbing)
        {
            TickClimb(dt, input, jumpPressed);
            AnimateVisual(dt, input, true);
            UpdateShadow();
            return;
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
            _leapHold = 0.08f;
        }
        else if (_leapHold > 0 && Input.IsActionPressed("jump") && Velocity.Y > 0)
        {
            // Holding the key while rising sustains the jump into the
            // bigger, gap-crossing leap. A quick tap keeps the small hop,
            // so nothing about the leap needs frame-perfect timing.
            Velocity = new Vector3(Velocity.X, Mathf.Max(Velocity.Y, LeapVelocity), Velocity.Z);
            _leapHold -= dt;
        }
        else _leapHold = 0;
        Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * dt, Velocity.Z);

        var wasFloor = IsOnFloor();
        MoveAndSlide();
        if (!wasFloor && IsOnFloor()) _landSquash = 1;
        _landSquash = Mathf.Max(0, _landSquash - dt * 4.5f);

        // Pushing into a climbable face attaches the spider to it — from the
        // ground, mid-jump or mid-fall alike. The cooldown stops a leap from
        // re-sticking to the wall it just left.
        TryAttachToClimbableFace(direction);

        if (GlobalPosition.Y < -20)
        {
            GlobalPosition = FallResetPosition;
            Velocity = Vector3.Zero;
        }
        // The procedural meadow legitimately dips below y = -3, so "safe" is
        // recorded whenever the character body is actually grounded.
        if (IsOnFloor()) _lastSafePosition = GlobalPosition;
        AnimateVisual(dt, input, false);
        UpdateShadow();
    }

    /// <summary>Is this surface something a spider can cling to? Steeper than
    /// the walkable band, and either generated version-2 terrain (its cliff
    /// faces are made to be climbed) or an authored surface explicitly marked
    /// as climbable.</summary>
    private static bool SurfaceIsClimbable(GodotObject collider, Vector3 normal)
    {
        if (normal.Y > ClimbFaceNormalY) return false;
        if (collider is not Node node) return false;
        if (node.IsInGroup("climbable")) return true;
        return node.HasMeta("generator_version") &&
               node.GetMeta("generator_version").AsInt32() >= 2;
    }

    private void TryAttachToClimbableFace(Vector3 moveDirection)
    {
        if (IsClimbing || _climbCooldown > 0) return;
        if (moveDirection.LengthSquared() < 0.05f) return;
        // Every contact from the last slide, not just the last one: the
        // floor usually resolves after the wall and would hide it.
        var count = GetSlideCollisionCount();
        for (var i = 0; i < count; i++)
        {
            var slide = GetSlideCollision(i);
            if (slide?.GetCollider() is not { } collider) continue;
            var normal = slide.GetNormal();
            if (!SurfaceIsClimbable(collider, normal)) continue;
            // Only attach when the spider is actually pushing into the face.
            if (moveDirection.Dot(-normal) < 0.35f) continue;
            AttachToFace(normal);
            return;
        }
    }

    private void AttachToFace(Vector3 normal)
    {
        IsClimbing = true;
        ClimbNormal = normal.Normalized();
        Velocity = Vector3.Zero;
    }

    /// <summary>One physics step while climbing: re-probe the face, walk
    /// up, down and sideways along it, mount the top edge when the face runs
    /// out, and leap off on demand.</summary>
    private void TickClimb(float dt, Vector2 input, bool jumpPressed)
    {
        var space = GetWorld3D().DirectSpaceState;

        // Where is the face now? Re-probe from the body toward the wall.
        var from = GlobalPosition + new Vector3(0, 0.35f, 0);
        var into = -ClimbNormal;
        var faceQuery = PhysicsRayQueryParameters3D.Create(
            from, from + into * ClimbProbeDistance, 1);
        faceQuery.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var faceHit = space.IntersectRay(faceQuery);

        if (faceHit.Count == 0)
        {
            // The face ended: mount whatever lies over the lip when the
            // player is pushing up or into the wall, otherwise let go.
            var pushingUp = input.Y < -0.15f;
            if (pushingUp && TryMountTopEdge(space))
            {
                IsClimbing = false;
                _climbCooldown = LeapReattachCooldown;
                AnimateVisual(dt, input, false);
                UpdateShadow();
                return;
            }
            // Nothing to hold: let go and fall.
            IsClimbing = false;
            _climbCooldown = LeapReattachCooldown;
            Velocity = new Vector3(0, -1f, 0);
            AnimateVisual(dt, input, false);
            UpdateShadow();
            return;
        }

        ClimbNormal = faceHit["normal"].AsVector3().Normalized();

        // Movement along the face: W/S climb vertically, A/D shuffle along
        // the wall's tangent, and a gentle push keeps the body in contact.
        var vertical = -input.Y * ClimbSpeed;
        var tangent = ClimbNormal.Cross(Vector3.Up).Normalized();
        if (tangent.LengthSquared() < 0.01f) tangent = Vector3.Right;
        var lateral = input.X * ClimbSpeed * 0.9f;
        Velocity = tangent * lateral + -ClimbNormal * 1.4f;
        Velocity = new Vector3(Velocity.X, vertical, Velocity.Z);

        // Reaching the bottom of a face onto walkable ground ends the climb.
        if (input.Y > 0.25f && GroundIsCloseBelow(space))
        {
            IsClimbing = false;
            _climbCooldown = 0.1f;
            Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
            AnimateVisual(dt, input, false);
            UpdateShadow();
            return;
        }

        if (jumpPressed)
        {
            // Leap away from the wall: outward plus a healthy rise.
            IsClimbing = false;
            _climbCooldown = LeapReattachCooldown;
            Velocity = ClimbNormal * 3.6f + new Vector3(0, JumpVelocity * 0.75f, 0);
            AnimateVisual(dt, input, false);
            UpdateShadow();
            return;
        }

        MoveAndSlide();
    }

    /// <summary>Mount the ground over a lip: probe forward-and-down from just
    /// above the spider; walkable ground there receives a small hop. The
    /// generous probe means mounting never needs precise positioning.</summary>
    private bool TryMountTopEdge(PhysicsDirectSpaceState3D space)
    {
        var over = GlobalPosition + new Vector3(0, 0.85f, 0) - ClimbNormal * 0.9f;
        var query = PhysicsRayQueryParameters3D.Create(
            over, over + new Vector3(0, -1.6f, 0), 1);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var hit = space.IntersectRay(query);
        if (hit.Count == 0) return false;
        if (hit["normal"].AsVector3().Y < 0.6f) return false;
        // A small hop over the edge: up and forward onto the shelf.
        Velocity = -ClimbNormal * 3.4f + new Vector3(0, 4.6f, 0);
        MoveAndSlide();
        return true;
    }

    private bool GroundIsCloseBelow(PhysicsDirectSpaceState3D space)
    {
        var from = GlobalPosition + new Vector3(0, 0.2f, 0);
        var query = PhysicsRayQueryParameters3D.Create(
            from, from + new Vector3(0, -0.7f, 0), 1);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var hit = space.IntersectRay(query);
        return hit.Count > 0 && hit["normal"].AsVector3().Y > 0.6f;
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
        _shadow.GlobalRotation = Vector3.Zero;
    }

    private void UpdateShadow()
    {
        // The shadow used to assume flat ground at y = 0. The procedural world
        // slopes, so cast straight down from the spider and rest the shadow on
        // whatever surface is under it; hide it when airborne over a drop.
        var space = GetWorld3D().DirectSpaceState;
        var from = GlobalPosition + new Vector3(0, 0.5f, 0);
        var query = PhysicsRayQueryParameters3D.Create(from, from + new Vector3(0, -30, 0), 1);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var hit = space.IntersectRay(query);
        if (hit.Count > 0 && hit["position"].AsVector3().Y > GlobalPosition.Y - 3.5f)
        {
            _shadow.Visible = true;
            _shadow.GlobalPosition = hit["position"].AsVector3() + new Vector3(0, 0.02f, 0);
        }
        else
        {
            _shadow.Visible = false;
        }
    }

    public Vec3 ToCorePosition() => new(GlobalPosition.X, GlobalPosition.Y, GlobalPosition.Z);
}
