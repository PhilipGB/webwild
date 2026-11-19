using Godot;
using Webwild.Core;

namespace Webwild.Game;

/// <summary>
/// Built view for one player-made river crossing: the modular silk bridge
/// plus one continuous walkway collision surface. The deck is placed from the
/// simulation's stored geometry, so it rebuilds identically after any chunk
/// unload, reload or save.
/// </summary>
public sealed partial class RiverBridgeVisual : Node3D
{
    private BridgeVisualAssembler? _bridgeVisual;
    private StaticBody3D? _body;
    private MeshInstance3D? _beacon;
    private double _beaconPulse;

    public string BridgeId { get; private set; } = "";

    public int ProbeWalkwayShapeCount =>
        _body?.GetChildren().OfType<CollisionShape3D>().Count() ?? 0;
    public int ProbeDetailCount => _bridgeVisual?.DetailCount ?? 0;
    public bool ProbeBeaconVisible => _beacon is { Visible: true };

    public void Configure(PlayerRiverBridge bridge, bool reducedMotion)
    {
        BridgeId = bridge.Id;
        Name = "RiverBridge_" + bridge.Id;
        var a = ToVector3(bridge.NorthAnchor);
        var b = ToVector3(bridge.SouthAnchor);
        var center = (a + b) * 0.5f;
        Position = center;

        _bridgeVisual = new BridgeVisualAssembler { Name = "ModularBridge" };
        AddChild(_bridgeVisual);
        _bridgeVisual.Configure(
            bridge.Id, a - center, b - center,
            ToVector3(bridge.NorthGround) - center,
            ToVector3(bridge.SouthGround) - center,
            BridgeVisualMode.Constructed, 1f, reducedMotion);

        // One continuous triangle surface for ramp + deck + ramp, exactly as
        // the authored bridges build theirs: no end caps to wedge the player.
        _body = new StaticBody3D
        {
            Name = "BridgeWalkway",
            CollisionLayer = 1,
            CollisionMask = 0
        };
        _body.SetMeta("river_bridge_id", bridge.Id);
        _body.AddToGroup("player_river_bridge");
        AddChild(_body);
        var surface = BridgeGeometry.BuildWalkwaySurface(
            bridge.NorthAnchor, bridge.SouthAnchor, bridge.NorthGround, bridge.SouthGround);
        var centerLogical = new Vec3(center.X, center.Y, center.Z);
        var faces = new Vector3[surface.Indices.Count];
        for (var i = 0; i < surface.Indices.Count; i++)
        {
            var local = surface.Vertices[surface.Indices[i]] - centerLogical;
            faces[i] = ToVector3(local);
        }
        var shape = new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true };
        _body.AddChild(new CollisionShape3D
        {
            Name = "WalkwaySurface",
            Shape = shape,
            Transform = Transform3D.Identity
        });
    }

    /// <summary>Pulsing marker over the deck while the player follows
    /// nearby-bridge guidance. Purely presentational.</summary>
    public void SetBeaconVisible(bool visible)
    {
        if (visible && _beacon is null)
        {
            var material = new StandardMaterial3D
            {
                AlbedoColor = UiKit.White,
                EmissionEnabled = true,
                Emission = UiKit.Gold,
                EmissionEnergyMultiplier = 1.4f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha
            };
            _beacon = new MeshInstance3D
            {
                Name = "GuidanceBeacon",
                Mesh = new SphereMesh { Radius = 0.34f, Height = 0.68f, RadialSegments = 10, Rings = 6 },
                MaterialOverride = material,
                Position = new Vector3(0, 1.5f, 0)
            };
            AddChild(_beacon);
        }
        if (_beacon is not null) _beacon.Visible = visible;
    }

    public override void _Process(double delta)
    {
        if (_beacon is not { Visible: true }) return;
        _beaconPulse += delta;
        var pulse = 1.0 + 0.25 * Math.Sin(_beaconPulse * 4.0);
        _beacon.Scale = Vector3.One * (float)pulse;
        if (_beacon.GetActiveMaterial(0) is StandardMaterial3D material)
            material.EmissionEnergyMultiplier = (float)(1.1 + 0.7 * Math.Sin(_beaconPulse * 4.0));
    }

    private static Vector3 ToVector3(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
