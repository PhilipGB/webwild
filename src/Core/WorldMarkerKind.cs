namespace Webwild.Core;

/// <summary>Stable marker kind names used by saves and the map.</summary>
public static class WorldMarkerKind
{
    public const string Food = "food";
    public const string Note = "note";
    public const string Home = "home";

    public static bool IsKnown(string? kind) =>
        kind is Food or Note or Home;
}
