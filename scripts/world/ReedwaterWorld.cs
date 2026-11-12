using Godot;

namespace Webwild.Game;

/// <summary>
/// Presentation-only root for the authored Reedwater Bank. The scene owns all
/// persistent geometry and collision; this script only makes imported
/// vertex-colour models render correctly in exported builds.
/// </summary>
public partial class ReedwaterWorld : Node3D
{
    public override void _Ready() => ArtMaterials.ApplyToModelInstances(this);
}
