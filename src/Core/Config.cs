using System.Text.Json;
using System.Text.Json.Serialization;

namespace Webwild.Core;

public sealed class WorldBounds
{
    public double X { get; set; } = 38;
    public double Z { get; set; } = 28;
}

public sealed class InitialResources
{
    public int Food { get; set; } = 5;
    public int Silk { get; set; } = 20;
    public int SpidersIncludingPlayer { get; set; } = 1;
}

public sealed class CaptureWebTuning
{
    public int SilkCost { get; set; } = 6;
    public double MinAnchorDistance { get; set; } = 1.3;
    public double MaxAnchorDistance { get; set; } = 7.0;
    public int Durability { get; set; } = 100;
    public double CaptureRadius { get; set; } = 1.3;
}

public sealed class BridgeWebTuning
{
    public int SilkCost { get; set; } = 5;
    public double MinAnchorDistance { get; set; } = 1.0;
    public double MaxAnchorDistance { get; set; } = 6.0;
    public int Durability { get; set; } = 100;
    public double MaxInclineDegrees { get; set; } = 25;
}

public sealed class WebTuning
{
    public CaptureWebTuning Capture { get; set; } = new();
    public BridgeWebTuning Bridge { get; set; } = new();
    public double DismantleRefundFraction { get; set; } = 0.6;
    public double TutorialRefundFraction { get; set; } = 1.0;
    public int RepairSilkCost { get; set; } = 2;
}

public sealed class InsectTuning
{
    public int FoodYield { get; set; } = 2;
    public double SpeedWorldUnitsPerSecond { get; set; } = 2.2;
    public double RespawnSeconds { get; set; } = 22;
}

public sealed class NurseryTuning
{
    public int HatchFoodCost { get; set; } = 4;
    public int HatchSilkCost { get; set; } = 4;
    public double HatchTimeSeconds { get; set; } = 3;
}

public sealed class SilkProductionTuning
{
    public double SilkPerFedWorkerEverySeconds { get; set; } = 18;
    public int FoodCostPerSilkBatch { get; set; } = 1;
}

public sealed class WeatherTuning
{
    public double WarningSeconds { get; set; } = 20;
    public int ExposedWebDamage { get; set; } = 50;
    public int ShelteredWebDamage { get; set; } = 5;
}

public sealed class RecoveryTuning
{
    public int EmergencyStarterWebRepairUses { get; set; } = 3;
    public double HintSecondsBeforeEscalation { get; set; } = 45;
}

/// <summary>Balance values loaded from data/tuning.json, with safe defaults.</summary>
public sealed class Tuning
{
    public int SchemaVersion { get; set; } = 1;
    public int Seed { get; set; } = 27011;
    public double FixedSimulationTickSeconds { get; set; } = 0.2;
    public WorldBounds WorldBoundsUnits { get; set; } = new();
    public InitialResources Initial { get; set; } = new();
    public int MaxSpidersIncludingPlayer { get; set; } = 5;
    public WebTuning Webs { get; set; } = new();
    public Dictionary<string, InsectTuning> Insects { get; set; } = new()
    {
        ["fly"] = new InsectTuning(),
        ["moth"] = new InsectTuning { FoodYield = 3, SpeedWorldUnitsPerSecond = 1.5, RespawnSeconds = 34 }
    };
    public NurseryTuning Nursery { get; set; } = new();
    public SilkProductionTuning SilkProduction { get; set; } = new();
    public WeatherTuning Weather { get; set; } = new();
    public RecoveryTuning Recovery { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Tuning Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return Parse(json);
        }
        catch (Exception)
        {
            return new Tuning();
        }
    }

    public static Tuning Parse(string json)
    {
        var value = JsonSerializer.Deserialize<Tuning>(json, JsonOptions) ?? new Tuning();
        value.Normalise();
        return value;
    }

    public Tuning Clone()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        return Parse(json);
    }

    /// <summary>
    /// Derive gentle-weather values from an immutable baseline so turning the
    /// option off restores the normal storm exactly.
    /// </summary>
    public void ApplyWeatherProfile(WeatherTuning baseline, bool gentle)
    {
        baseline ??= Weather;
        Weather.ExposedWebDamage = gentle ? Math.Min(baseline.ExposedWebDamage, 25) : baseline.ExposedWebDamage;
        Weather.ShelteredWebDamage = gentle ? 0 : baseline.ShelteredWebDamage;
        Weather.WarningSeconds = gentle ? Math.Max(baseline.WarningSeconds, 30) : baseline.WarningSeconds;
    }

    public void Normalise()
    {
        Seed = Math.Max(0, Seed);
        FixedSimulationTickSeconds = Math.Clamp(FixedSimulationTickSeconds, 0.05, 1.0);
        MaxSpidersIncludingPlayer = Math.Clamp(MaxSpidersIncludingPlayer, 1, 5);
        Initial.Food = Math.Max(0, Initial.Food);
        Initial.Silk = Math.Max(0, Initial.Silk);
        WorldBoundsUnits.X = Math.Max(10, WorldBoundsUnits.X);
        WorldBoundsUnits.Z = Math.Max(10, WorldBoundsUnits.Z);
        Webs ??= new WebTuning();
        Webs.Capture ??= new CaptureWebTuning();
        Webs.Bridge ??= new BridgeWebTuning();
        Webs.Capture.SilkCost = Math.Max(0, Webs.Capture.SilkCost);
        Webs.Bridge.SilkCost = Math.Max(0, Webs.Bridge.SilkCost);
        Webs.Capture.MaxAnchorDistance = Math.Max(Webs.Capture.MinAnchorDistance, Webs.Capture.MaxAnchorDistance);
        Webs.Bridge.MaxAnchorDistance = Math.Max(Webs.Bridge.MinAnchorDistance, Webs.Bridge.MaxAnchorDistance);
        Nursery ??= new NurseryTuning();
        Weather ??= new WeatherTuning();
        Recovery ??= new RecoveryTuning();
        SilkProduction ??= new SilkProductionTuning();
        SilkProduction.SilkPerFedWorkerEverySeconds = Math.Max(0, SilkProduction.SilkPerFedWorkerEverySeconds);
        SilkProduction.FoodCostPerSilkBatch = Math.Max(0, SilkProduction.FoodCostPerSilkBatch);
        Insects ??= new Dictionary<string, InsectTuning>();
        if (!Insects.ContainsKey("fly")) Insects["fly"] = new InsectTuning();
        if (!Insects.ContainsKey("moth")) Insects["moth"] = new InsectTuning { FoodYield = 3, SpeedWorldUnitsPerSecond = 1.5, RespawnSeconds = 34 };
        Weather.WarningSeconds = Math.Clamp(Weather.WarningSeconds, 5, 120);
        Weather.ExposedWebDamage = Math.Clamp(Weather.ExposedWebDamage, 0, 100);
        Weather.ShelteredWebDamage = Math.Clamp(Weather.ShelteredWebDamage, 0, 100);
    }
}

public sealed class StepFilter
{
    public string? Type { get; set; }
    [JsonPropertyName("webType")] public string? WebType { get; set; }
}

public sealed class MissionStepDefinition
{
    public string Event { get; set; } = "";
    public StepFilter? Filter { get; set; }
    public int Count { get; set; } = 1;
}

public sealed class MissionDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<MissionStepDefinition> Steps { get; set; } = new();
    public string Hint { get; set; } = "";
}

public sealed class OptionalMasteryDefinition
{
    public string Id { get; set; } = "O01";
    public string Title { get; set; } = "Clever Weaver";
    public string Description { get; set; } = "Collect 3 insects with 2 or fewer capture webs.";
    public string Reward { get; set; } = "Clever Weaver decorative badge";
    public bool RequiredForVictory { get; set; }
}

public sealed class MissionsConfig
{
    public int SchemaVersion { get; set; } = 1;
    public List<MissionDefinition> Missions { get; set; } = new();
    public OptionalMasteryDefinition? OptionalMastery { get; set; }

    public static MissionsConfig Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return Defaults();
        }
    }

    public static MissionsConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<MissionsConfig>(json, Tuning.JsonOptions) ?? Defaults();
        if (config.Missions.Count == 0) config = Defaults();
        return config;
    }

    public static MissionsConfig Defaults() => new()
    {
        Missions = new List<MissionDefinition>
        {
            new()
            {
                Id = "M01", Title = "First Threads",
                Hint = "Choose the glowing twigs and catch a fly.",
                Steps = new List<MissionStepDefinition>
                {
                    new() { Event = "WebBuilt", Filter = new StepFilter { Type = "capture" } },
                    new() { Event = "FoodCollected" }
                }
            },
            new()
            {
                Id = "M02", Title = "Tiny Team",
                Hint = "Build a shortcut and hatch a helper.",
                Steps = new List<MissionStepDefinition>
                {
                    new() { Event = "WebBuilt", Filter = new StepFilter { Type = "bridge" } },
                    new() { Event = "SpiderHatched" },
                    new() { Event = "WorkerUsedBridge" }
                }
            },
            new()
            {
                Id = "M03", Title = "Storm-Smart",
                Hint = "Protect a web, then collect food after the wind.",
                Steps = new List<MissionStepDefinition>
                {
                    new() { Event = "WindEventCompleted" },
                    new() { Event = "WebRestoredOrSheltered" },
                    new() { Event = "FoodCollectedAfterWind" }
                }
            }
        },
        OptionalMastery = new OptionalMasteryDefinition()
    };
}
