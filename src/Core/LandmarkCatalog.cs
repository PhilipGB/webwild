namespace Webwild.Core;

/// <summary>
/// Authored landmarks are fixed, hand-made environments embedded in the
/// continuous world. Each landmark owns a local coordinate frame (the existing
/// gameplay coordinates) placed at a constant world origin, so legacy region
/// coordinates convert to world positions deterministically.
///
/// The procedural world grows around these anchors. Landmark identifiers, not
/// the old finite region catalogue, anchor story logic from v0.3 onward; the
/// identifiers intentionally reuse the existing region ids so saves, missions
/// and objective references stay stable.
/// </summary>
public static class LandmarkCatalog
{
    public const string Garden = RegionIds.GardenClearing;
    public const string Reedwater = RegionIds.ReedwaterBank;

    /// <summary>
    /// Constant world origins for authored landmarks.
    ///  - Garden: the home clearing sits at the world origin, so every legacy
    ///    garden coordinate is already a world coordinate.
    ///  - Reedwater: placed east of the garden's east-hedge passage (travel
    ///    point x = 16.1), across two to three chunks of connecting terrain.
    /// These values are part of the save contract once a world manifest exists;
    /// changing them would move every authored landmark in old saves.
    /// </summary>
    public static IReadOnlyDictionary<string, Vec3> Origins { get; } =
        new Dictionary<string, Vec3>(StringComparer.Ordinal)
        {
            [Garden] = new Vec3(0, 0, 0),
            [Reedwater] = new Vec3(96, 0, 0)
        };

    public static bool IsKnown(string? id) => id is Garden or Reedwater;

    public static Vec3 OriginOf(string landmarkId) =>
        Origins.TryGetValue(landmarkId, out var origin) ? origin : Vec3.Zero;

    /// <summary>Convert a landmark-local gameplay position into a world position.</summary>
    public static WorldPos ToWorld(string landmarkId, Vec3 local) =>
        WorldPos.FromAbsolute(OriginOf(landmarkId) + local);

    /// <summary>Convert a world position back into landmark-local coordinates.</summary>
    public static Vec3 ToLocal(string landmarkId, Vec3 world) => world - OriginOf(landmarkId);
}
