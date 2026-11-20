using System.Text.Json;
using System.Text.Json.Serialization;

namespace Webwild.Core;

public sealed class PlayerSave
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double RotationY { get; set; }
}

public sealed class WebSave
{
    public string Id { get; set; } = "";
    public string Region { get; set; } = RegionIds.GardenClearing;
    public string Type { get; set; } = "capture";
    public string AnchorA { get; set; } = "";
    public string AnchorB { get; set; } = "";
    public int Durability { get; set; }
    public int MaxDurability { get; set; } = 100;
    public double Exposure { get; set; }
    public bool Sheltered { get; set; }
    public bool IsTutorial { get; set; }
    public long ConstructedAtTick { get; set; }
}

/// <summary>One player-built silk crossing over a generated river. The full
/// deck geometry is stored so views, collision and worker navigation rebuild
/// identically after any reload; river state itself regenerates from the
/// seed and is never persisted.</summary>
public sealed class RiverBridgeSave
{
    public string Id { get; set; } = "";
    public int RiverIndex { get; set; }
    public double X { get; set; }
    public double CenterZ { get; set; }
    public double HalfWidth { get; set; }
    public double WaterLevel { get; set; }
    public double HalfSpan { get; set; }
    public double NorthGroundY { get; set; }
    public double SouthGroundY { get; set; }
    public long ConstructedAtTick { get; set; }
}

public sealed class WorkerSave
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Colour { get; set; } = "orange";
    public string Region { get; set; } = RegionIds.GardenClearing;
    /// <summary>Home landmark. Missing in v0.3.0 runner saves and filled with
    /// <see cref="Region"/> during import; never silently moves a helper.</summary>
    public string HomeRegion { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string CurrentNode { get; set; } = "";
    public string Activity { get; set; } = "Resting";
    public string? TargetInsectId { get; set; }
    public string? TargetWebId { get; set; }
    public string TargetNode { get; set; } = "";
    public List<string> PathNodes { get; set; } = new();
    public List<string> PathEdgeWebIds { get; set; } = new();
    public int PathIndex { get; set; }
    public double ArrivalDelay { get; set; }
    public double PatrolCooldown { get; set; }
    public int CollectCount { get; set; }
    public int RepairCount { get; set; }
    public int BridgeCrossings { get; set; }
    public bool Travelling { get; set; }
    public string JourneyTargetRegion { get; set; } = "";
    public string JourneyFromRegion { get; set; } = "";
    public double JourneyX { get; set; }
    public double JourneyY { get; set; }
    public double JourneyZ { get; set; }
    public double DestinationX { get; set; }
    public double DestinationY { get; set; }
    public double DestinationZ { get; set; }
    public bool Summoned { get; set; }
    public string? TargetFoodId { get; set; }
    public string FoodPhase { get; set; } = "None";
    public double FoodX { get; set; }
    public double FoodY { get; set; }
    public double FoodZ { get; set; }
    public int CarriedFood { get; set; }
    public string FoodDeliveryId { get; set; } = "";
    public List<WorldPosSave> WorldPath { get; set; } = new();
    public int WorldPathIndex { get; set; }
    public List<WorldPosSave> OutboundPath { get; set; } = new();
    public bool WorldPathUsesBridge { get; set; }
    public double TaskRetryCooldown { get; set; }
}

public sealed class InsectSave
{
    public string Id { get; set; } = "";
    public string Region { get; set; } = RegionIds.GardenClearing;
    public int Generation { get; set; }
    public string Phase { get; set; } = "Flying";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public int WaypointIndex { get; set; }
    public string? TrappedWebId { get; set; }
    public long CapturedAtTick { get; set; }
    public long RespawnAtTick { get; set; }
}

public sealed class MissionSave
{
    public int ActiveMissionIndex { get; set; }
    public int StepIndex { get; set; }
    public List<int> Progress { get; set; } = new();
    public List<string> Completed { get; set; } = new();
    public List<string> SeenEvents { get; set; } = new();
    public bool VictoryAwarded { get; set; }
}

public sealed class WeatherSave
{
    public string Phase { get; set; } = "Calm";
    public double Remaining { get; set; }
    public bool WindStarted { get; set; }
    public bool WindCompleted { get; set; }
    public double AftermathRemaining { get; set; }
}

public sealed class RegionPlayerSave
{
    public string Region { get; set; } = RegionIds.GardenClearing;
    /// <summary>Home landmark. Missing in v0.3.0 runner saves and filled with
    /// <see cref="Region"/> during import; never silently moves a helper.</summary>
    public string HomeRegion { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double RotationY { get; set; }
}

public sealed class RegionalObjectiveSave
{
    public int ActiveObjectiveIndex { get; set; }
    public int StepIndex { get; set; }
    public List<int> Progress { get; set; } = new();
    public List<string> Completed { get; set; } = new();
    public List<string> SeenEvents { get; set; } = new();
    public bool MasteryAwarded { get; set; }
}

public sealed class SimCountersSave
{
    public int WebSequence { get; set; }
    public int RiverBridgeSequence { get; set; }
    public int WorkerSequence { get; set; }
    public int TransactionSequence { get; set; }
    public int CaptureWebsBuilt { get; set; }
    public int BridgeWebsBuilt { get; set; }
    public int RiverBridgesBuilt { get; set; }
    public int ReedwaterCaptureWebsBuilt { get; set; }
    public int ReedwaterBridgeWebsBuilt { get; set; }
    public int InsectsCollected { get; set; }
    public int RepairsCompleted { get; set; }
    public int EmergencyUses { get; set; }
    public bool MasteryAwarded { get; set; }
    public bool Victory { get; set; }
    public double SilkAccumulator { get; set; }
    public double PendingHatchRemaining { get; set; }
    public string? PendingHatchId { get; set; }
    public string? PendingHatchRegionId { get; set; }
    public List<string> LedgerKeys { get; set; } = new();
    public List<string> IssuedEventKeys { get; set; } = new();
    public List<string> OpenedRegionRoutes { get; set; } = new();
    public List<string> ClaimedSupplyCaches { get; set; } = new();
}

public sealed class SettingsData
{
    public double MasterVolume { get; set; } = 0.85;
    public double MusicVolume { get; set; } = 0.55;
    public double SfxVolume { get; set; } = 0.8;
    public int UiScalePercent { get; set; } = 100;
    public bool ReducedMotion { get; set; }
    public bool LowThreat { get; set; }
    public bool Fullscreen { get; set; }
    public double CameraSensitivity { get; set; } = 1.0;
    public bool TutorialPrompts { get; set; } = true;

    public void Normalise()
    {
        MasterVolume = Math.Clamp(MasterVolume, 0, 1);
        MusicVolume = Math.Clamp(MusicVolume, 0, 1);
        SfxVolume = Math.Clamp(SfxVolume, 0, 1);
        UiScalePercent = UiScalePercent is 100 or 125 or 150 ? UiScalePercent : 100;
        CameraSensitivity = Math.Clamp(CameraSensitivity, 0.3, 2.0);
    }
}

public sealed class SaveGameData
{
    public const int CurrentSchemaVersion = 3;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string SaveId { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public int Seed { get; set; }
    public long Tick { get; set; }
    public double ElapsedSeconds { get; set; }
    public string ActiveRegionId { get; set; } = RegionIds.GardenClearing;
    /// <summary>World-generation algorithm this garden was generated with;
    /// 1 for all pre-H4 saves. The manifest mirrors it for load-time checks.</summary>
    public int GeneratorVersion { get; set; } = 1;
    public PlayerSave Player { get; set; } = new();
    public List<RegionPlayerSave> RegionPlayers { get; set; } = new();

    /// <summary>
    /// Runtime view of the schema-4 manifest's exploration record. It is not
    /// serialized inside the embedded colony payload; the manifest stores it
    /// once at the top level so save size stays bounded.
    /// </summary>
    [JsonIgnore]
    public List<string> ExploredChunks { get; set; } = new();

    /// <summary>Runtime view of the manifest's compact exploration regions;
    /// not serialized in the payload (same reason as above).</summary>
    [JsonIgnore]
    public List<ExplorationRegionSave> ExplorationRegions { get; set; } = new();
    public List<string> VisitedRegions { get; set; } = new() { RegionIds.GardenClearing };
    public int Food { get; set; }
    public int Silk { get; set; }
    public List<WebSave> Webs { get; set; } = new();
    public List<RiverBridgeSave> RiverBridges { get; set; } = new();
    public List<WorkerSave> Workers { get; set; } = new();
    public List<InsectSave> Insects { get; set; } = new();
    public List<MapMarkerSave> MapMarkers { get; set; } = new();
    public MissionSave Missions { get; set; } = new();
    public RegionalObjectiveSave RegionalObjectives { get; set; } = new();
    public WeatherSave Weather { get; set; } = new();
    public SimCountersSave Counters { get; set; } = new();
    public SettingsData Settings { get; set; } = new();
}

public sealed record SaveResult(bool Success, string Message, string? Path, string? Error = null)
{
    public static SaveResult Ok(string path) => new(true, "Saved", path);
    public static SaveResult Fail(string message, string? error = null) => new(false, message, null, error);
}

public sealed record LoadResult(bool Success, SaveGameData? Data, bool RecoveredFromBackup, string Message, string? Error = null)
{
    public static LoadResult Ok(SaveGameData data, bool recovered) =>
        new(true, data, recovered, recovered ? "Recovered your last good save" : "Save loaded");
    public static LoadResult Fail(string message, string? error = null) => new(false, null, false, message, error);
}
