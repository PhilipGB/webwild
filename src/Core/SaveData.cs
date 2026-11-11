using System.Text.Json;

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

public sealed class WorkerSave
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Colour { get; set; } = "orange";
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
}

public sealed class InsectSave
{
    public string Id { get; set; } = "";
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

public sealed class SimCountersSave
{
    public int WebSequence { get; set; }
    public int WorkerSequence { get; set; }
    public int TransactionSequence { get; set; }
    public int CaptureWebsBuilt { get; set; }
    public int BridgeWebsBuilt { get; set; }
    public int InsectsCollected { get; set; }
    public int RepairsCompleted { get; set; }
    public int EmergencyUses { get; set; }
    public bool MasteryAwarded { get; set; }
    public bool Victory { get; set; }
    public double SilkAccumulator { get; set; }
    public double PendingHatchRemaining { get; set; }
    public string? PendingHatchId { get; set; }
    public List<string> LedgerKeys { get; set; } = new();
    public List<string> IssuedEventKeys { get; set; } = new();
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
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string SaveId { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public int Seed { get; set; }
    public long Tick { get; set; }
    public double ElapsedSeconds { get; set; }
    public PlayerSave Player { get; set; } = new();
    public int Food { get; set; }
    public int Silk { get; set; }
    public List<WebSave> Webs { get; set; } = new();
    public List<WorkerSave> Workers { get; set; } = new();
    public List<InsectSave> Insects { get; set; } = new();
    public MissionSave Missions { get; set; } = new();
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
