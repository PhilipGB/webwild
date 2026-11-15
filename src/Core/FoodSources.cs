namespace Webwild.Core;

/// <summary>
/// One deterministic wild food source. Definitions are regenerated from
/// (seed, chunk) and never persisted; only a depleted source's availability
/// state is written, in that chunk's small change record.
/// </summary>
public sealed record FoodSourceDefinition(
    string Id,
    ChunkCoordinate Chunk,
    string Kind,
    string Label,
    int Amount,
    Vec3 Position);

/// <summary>
/// Serializable state for one harvested food source. <see cref="AvailableAtTick"/>
/// is an absolute simulation tick, so regeneration advances while chunks are
/// unloaded and never resets by walking away.
/// </summary>
public sealed record FoodSourceState(
    string Id,
    ChunkCoordinate Chunk,
    string Kind,
    Vec3 Position,
    long AvailableAtTick)
{
    public FoodSourceStateSave ToSave() => new()
    {
        Id = Id,
        X = Position.X,
        Y = Position.Y,
        Z = Position.Z,
        Kind = Kind,
        AvailableAtTick = AvailableAtTick
    };

    public static FoodSourceState FromSave(ChunkCoordinate chunk, FoodSourceStateSave save) =>
        new(save.Id, chunk, save.Kind,
            new Vec3(save.X, save.Y, save.Z), save.AvailableAtTick);
}

/// <summary>A depleted state paired with its owning chunk for save grouping.</summary>
public sealed record FoodStateSnapshot(ChunkCoordinate Chunk, FoodSourceStateSave Save);
