using Webwild.Core;

namespace Webwild.Game;

/// <summary>
/// Godot-side mapping from a stable core region id to its authored root scene.
/// New regions add one entry here; the session loader, validation command and
/// runtime probe can then treat them uniformly.
/// </summary>
public static class RegionSceneCatalog
{
    public sealed record Entry(string RegionId, string ScenePath, bool IsBuiltIntoSession);

    public static readonly Entry Garden = new(
        RegionIds.GardenClearing, "res://scenes/world/GardenClearing.tscn", true);

    public static readonly Entry Reedwater = new(
        RegionIds.ReedwaterBank,
        "res://scenes/world/regions/ReedwaterBank/ReedwaterBank.tscn", false);

    public static readonly IReadOnlyList<Entry> All = new[] { Garden, Reedwater };

    public static Entry? Get(string regionId) =>
        All.FirstOrDefault(entry => entry.RegionId == regionId);
}
