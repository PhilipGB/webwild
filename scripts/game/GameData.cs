using Godot;
using Webwild.Core;

namespace Webwild.Game;

/// <summary>
/// Bridges the engine-free core configuration to Godot's res:// and user://
/// paths. All rules stay in Webwild.Core; this type only reads bytes.
/// </summary>
public static class GameData
{
    public const string Version = "0.1.2";

    public static string SavePath => ProjectSettings.GlobalizePath("user://webwild_save.json");
    public static string SettingsPath => ProjectSettings.GlobalizePath("user://webwild_settings.json");
    public static string ArtifactsPath => OS.HasFeature("editor")
        ? ProjectSettings.GlobalizePath("res://artifacts")
        : ProjectSettings.GlobalizePath("user://artifacts");

    public static Tuning LoadTuning()
    {
        try
        {
            var text = Godot.FileAccess.GetFileAsString("res://data/tuning.json");
            return string.IsNullOrWhiteSpace(text) ? new Tuning() : Tuning.Parse(text);
        }
        catch
        {
            return new Tuning();
        }
    }

    public static MissionsConfig LoadMissions()
    {
        try
        {
            var text = Godot.FileAccess.GetFileAsString("res://data/missions.json");
            return string.IsNullOrWhiteSpace(text) ? MissionsConfig.Defaults() : MissionsConfig.Parse(text);
        }
        catch
        {
            return MissionsConfig.Defaults();
        }
    }

    public static SaveService CreateSaveService() => new(SavePath);

    public static SettingsService CreateSettingsService() => new(SettingsPath);

    /// <summary>Derive low-threat weather from an immutable baseline.</summary>
    public static void ApplyDifficulty(Tuning tuning, Tuning baseline, bool lowThreat)
    {
        tuning.ApplyWeatherProfile(baseline.Weather, lowThreat);
    }

    public static void ApplyUiScale(SettingsData settings)
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        root.ContentScaleFactor = Mathf.Clamp(settings.UiScalePercent / 100f, 1f, 1.5f);
    }

    public static void ApplyFullscreen(SettingsData settings)
    {
        if (DisplayServer.GetName() == "headless") return;
        try
        {
            var window = ((SceneTree)Engine.GetMainLoop()).Root;
            window.Mode = settings.Fullscreen
                ? Window.ModeEnum.Fullscreen
                : Window.ModeEnum.Windowed;
        }
        catch
        {
            // Some window managers refuse mode changes; gameplay must continue.
        }
    }

    public static string MissionLabel(MissionTracker missions)
    {
        var mission = missions.ActiveMission;
        return mission is null ? "Garden complete" : $"{mission.Id} · {mission.Title}";
    }

    public static string FormatTime(double seconds)
    {
        var total = Math.Max(0, (int)seconds);
        return $"{total / 60:00}:{total % 60:00}";
    }
}
