namespace Webwild.Core;

/// <summary>Serializable world position: integer chunk identity plus local doubles.</summary>
public sealed class WorldPosSave
{
    public long ChunkX { get; set; }
    public long ChunkZ { get; set; }
    public double LocalX { get; set; }
    public double Y { get; set; }
    public double LocalZ { get; set; }

    public static WorldPosSave FromPos(WorldPos pos) => new()
    {
        ChunkX = pos.ChunkX, ChunkZ = pos.ChunkZ,
        LocalX = pos.LocalX, Y = pos.Y, LocalZ = pos.LocalZ
    };

    public WorldPos ToPos() => new(ChunkX, ChunkZ, LocalX, Y, LocalZ);

    public Vec3 Absolute => ToPos().Absolute;

    public bool IsValid => ToPos().IsValid;
}

/// <summary>Which landmark a remembered transform belongs to, and the transform.</summary>
public sealed class LandmarkVisitSave
{
    public string LandmarkId { get; set; } = LandmarkCatalog.Garden;
    /// <summary>Player transform in landmark-local coordinates, exactly as older
    /// RegionPlayerSave entries stored them.</summary>
    public double LocalX { get; set; }
    public double LocalY { get; set; }
    public double LocalZ { get; set; }
    public double RotationY { get; set; }
}

/// <summary>
/// Player-made changes inside one chunk: the only chunk data that is ever
/// written to disk. Baseline terrain is regenerated from the seed and is never
/// saved, so save size grows only with actual player impact.
/// </summary>
public sealed class ChunkChangeSave
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public long ChunkX { get; set; }
    public long ChunkZ { get; set; }

    /// <summary>Webs whose centre lies in this chunk. Web payloads (anchors,
    /// durability) live in the manifest game payload; the chunk record is a
    /// fast reconstruction index for streaming.</summary>
    public List<string> BuiltWebIds { get; set; } = new();

    /// <summary>Procedural food sources whose state differs from regenerated
    /// baseline state (depleted or modified), keyed by stable world identity.</summary>
    public List<FoodSourceStateSave> FoodSources { get; set; } = new();

    /// <summary>Player-placed map markers inside this chunk.</summary>
    public List<MapMarkerSave> Markers { get; set; } = new();

    public string UpdatedUtc { get; set; } = "";
}

/// <summary>Availability state of one procedural food source.</summary>
public sealed class FoodSourceStateSave
{
    /// <summary>Stable world identity, e.g. "food:<chunk>:<ordinal>:<kind>".</summary>
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    /// <summary>Food source family, e.g. "berries", "seeds".</summary>
    public string Kind { get; set; } = "";
    /// <summary>Regeneration cycle: absolute sim tick when it becomes available
    /// again, or 0 while untouched/available.</summary>
    public long AvailableAtTick { get; set; }
}

/// <summary>A player-placed exploration marker.</summary>
public sealed class MapMarkerSave
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    /// <summary>Marker meaning: "food", "home", "note".</summary>
    public string Kind { get; set; } = "note";
    public string Label { get; set; } = "";
}

/// <summary>
/// The small, versioned world manifest. It carries the world identity, the
/// player and colony progression, and indexes per-chunk change files. Legacy
/// saves (schema 1–3) are migrated into this shape by
/// <see cref="WorldSaveMigrator"/>; the embedded game payload keeps its own
/// schema-3 semantics so the existing simulation, migration chain and fixtures
/// remain untouched.
/// </summary>
public sealed class WorldManifestSave
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string SaveId { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public int Seed { get; set; }

    public long Tick { get; set; }
    public double ElapsedSeconds { get; set; }

    /// <summary>Landmark id the player currently occupies.</summary>
    public string ActiveLandmarkId { get; set; } = LandmarkCatalog.Garden;

    /// <summary>Player position in stable world coordinates.</summary>
    public WorldPosSave Player { get; set; } = new();

    public double PlayerRotationY { get; set; }

    /// <summary>Landmarks visited, each carrying the remembered local transform
    /// from the legacy per-region save model.</summary>
    public List<LandmarkVisitSave> LandmarkVisits { get; set; } = new();

    /// <summary>Exploration record: chunk ids whose terrain has been seen.</summary>
    public List<string> ExploredChunks { get; set; } = new();

    /// <summary>Colony and story payload, still stored in the established
    /// SaveGameData shape (webs, workers, missions, counters, weather, settings).</summary>
    public SaveGameData Game { get; set; } = new();
}
