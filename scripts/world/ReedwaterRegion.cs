using Godot;

namespace Webwild.Game;

/// <summary>Root of the authored Reedwater Bank region scene.</summary>
public partial class ReedwaterRegion : Node3D
{
    public string RegionId => Webwild.Core.ReedwaterLayout.RegionId;

    public override void _Ready() => ArtMaterials.ApplyToModelInstances(this);
}
