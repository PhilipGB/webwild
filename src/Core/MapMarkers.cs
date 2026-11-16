namespace Webwild.Core;

/// <summary>A player-created marker shown on the wilderness map. Food markers
/// reference a deterministic source id so the map can assign a helper later,
/// even when the source's chunk is not currently presented.</summary>
public sealed record WorldMarker(
    string Id, string Kind, Vec3 Position, string? FoodId, string Label);
