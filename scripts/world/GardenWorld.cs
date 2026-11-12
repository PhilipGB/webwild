using Godot;

namespace Webwild.Game;

/// <summary>
/// Presentation-only root for the authored garden. Geometry and collision stay in
/// the scene; this ensures imported vertex-colour models also render correctly in
/// exported builds, where instance-level material overrides may be omitted.
/// </summary>
public partial class GardenWorld : Node3D
{
    public override void _Ready() => ArtMaterials.ApplyToModelInstances(this);
}
