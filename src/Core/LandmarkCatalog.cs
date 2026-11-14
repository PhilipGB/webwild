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
    /// Chunk-aligned world rectangles owned by authored landmarks. Terrain is
    /// never generated inside these rects (the authored scenes supply the
    /// ground) and generated heights blend to the authored level across a ring
    /// outside each edge. The garden rect covers the four chunks around the
    /// origin; Reedwater's rect covers the four chunks around its origin at
    /// (96, 0). Both are part of the streaming and save contract.
    /// </summary>
    public static readonly WorldRect GardenFootprint =
        new(-WorldChunk.Size, -WorldChunk.Size, WorldChunk.Size, WorldChunk.Size);

    public static readonly WorldRect ReedwaterFootprint =
        new(96 - WorldChunk.Size, -WorldChunk.Size, 96 + WorldChunk.Size, WorldChunk.Size);

    /// <summary>
    /// Walkable heart of each landmark inside its world-space walls. Crossing
    /// into one of these rects by ordinary walking activates that landmark
    /// (missions, helpers and interaction anchors); no teleport is involved.
    /// </summary>
    public static readonly WorldRect GardenPlay =
        new(-24, -24, 24, 24);

    public static readonly WorldRect ReedwaterPlay =
        new(96 - 19.6, -16.6, 96 + 19.6, 16.6);

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

    /// <summary>
    /// The authored landmark whose walkable heart contains this world point,
    /// or null in the procedural wilderness between landmarks. Boundary points
    /// count as inside so a spider that reaches the hedge line activates the
    /// landmark before stepping visibly past it.
    /// </summary>
    public static string? LandmarkAt(double worldX, double worldZ)
    {
        if (GardenPlay.Contains(worldX, worldZ)) return Garden;
        if (ReedwaterPlay.Contains(worldX, worldZ)) return Reedwater;
        return null;
    }

    public static string? LandmarkAt(Vec3 world) => LandmarkAt(world.X, world.Z);

    public static Vec3 OriginOf(string landmarkId) =>
        Origins.TryGetValue(landmarkId, out var origin) ? origin : Vec3.Zero;

    /// <summary>Convert a landmark-local gameplay position into a world position.</summary>
    public static WorldPos ToWorld(string landmarkId, Vec3 local) =>
        WorldPos.FromAbsolute(OriginOf(landmarkId) + local);

    /// <summary>Convert a world position back into landmark-local coordinates.</summary>
    public static Vec3 ToLocal(string landmarkId, Vec3 world) => world - OriginOf(landmarkId);
}
