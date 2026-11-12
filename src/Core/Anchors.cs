namespace Webwild.Core;

public enum WebType
{
    Capture,
    Bridge
}

public sealed class AnchorDefinition
{
    public required string Id { get; init; }
    public Vec3 Position { get; init; }
    public Vec3 Normal { get; init; } = Vec3.Up;
    public string Surface { get; init; } = "twig";
    public string Group { get; init; } = "garden";
    public int Capacity { get; init; } = 2;
    public bool Active { get; init; } = true;
    public bool Sheltered { get; init; }
    public double Exposure { get; init; } = 0.5;
    public string Label { get; init; } = "Twig";

    /// <summary>
    /// Optional authored point on solid ground where a bridge approach ramp
    /// starts. Use it where inferring "away from the other anchor" would be
    /// ambiguous, such as anchors that sit on the lip of a ravine.
    /// </summary>
    public Vec3? BridgeGroundPoint { get; init; }
}

public sealed class WebState
{
    public required string Id { get; init; }
    public WebType Type { get; init; }
    public required string AnchorA { get; init; }
    public required string AnchorB { get; init; }
    public int Durability { get; set; }
    public int MaxDurability { get; init; } = 100;
    public double Exposure { get; init; }
    public bool Sheltered { get; init; }
    public bool IsTutorial { get; init; }
    public long ConstructedAtTick { get; init; }
    public bool CapturesEnabled => Durability > 0;

    public bool Connects(string a, string b) =>
        (AnchorA == a && AnchorB == b) || (AnchorA == b && AnchorB == a);
}

public enum PlacementCode
{
    Valid,
    SameAnchor,
    UnknownAnchor,
    InactiveAnchor,
    TooClose,
    TooFar,
    AlreadyConnected,
    NoCapacity,
    NotEnoughSilk,
    TooSteep,
    InvalidOrientation
}

public sealed record PlacementResult(PlacementCode Code, bool IsValid, string Reason, int SilkCost)
{
    public static PlacementResult Ok(int cost) => new(PlacementCode.Valid, true, "Ready to build", cost);
    public static PlacementResult Fail(PlacementCode code, string reason, int cost = 0) =>
        new(code, false, reason, cost);
}

/// <summary>Pure anchor-pair validation shared by preview and build.</summary>
public sealed class PlacementRules
{
    private readonly IReadOnlyDictionary<string, AnchorDefinition> _anchors;
    private readonly IReadOnlyDictionary<string, WebState> _webs;
    private readonly Tuning _tuning;

    public PlacementRules(
        IReadOnlyDictionary<string, AnchorDefinition> anchors,
        IReadOnlyDictionary<string, WebState> webs,
        Tuning tuning)
    {
        _anchors = anchors;
        _webs = webs;
        _tuning = tuning;
    }

    public PlacementResult Validate(string anchorA, string anchorB, WebType type, int availableSilk, string? ignoreWebId = null)
    {
        var cost = type == WebType.Capture ? _tuning.Webs.Capture.SilkCost : _tuning.Webs.Bridge.SilkCost;
        if (string.IsNullOrWhiteSpace(anchorA) || string.IsNullOrWhiteSpace(anchorB))
            return PlacementResult.Fail(PlacementCode.UnknownAnchor, "Choose two anchors", cost);
        if (anchorA == anchorB)
            return PlacementResult.Fail(PlacementCode.SameAnchor, "Choose two different anchors", cost);
        if (!_anchors.TryGetValue(anchorA, out var a) || !_anchors.TryGetValue(anchorB, out var b))
            return PlacementResult.Fail(PlacementCode.UnknownAnchor, "That anchor is not part of this garden", cost);
        if (!a.Active || !b.Active)
            return PlacementResult.Fail(PlacementCode.InactiveAnchor, "That anchor is resting", cost);

        var distance = Vec3.Distance(a.Position, b.Position);
        var min = type == WebType.Capture ? _tuning.Webs.Capture.MinAnchorDistance : _tuning.Webs.Bridge.MinAnchorDistance;
        var max = type == WebType.Capture ? _tuning.Webs.Capture.MaxAnchorDistance : _tuning.Webs.Bridge.MaxAnchorDistance;
        if (distance < min)
            return PlacementResult.Fail(PlacementCode.TooClose, "Those anchors are too close", cost);
        if (distance > max)
            return PlacementResult.Fail(PlacementCode.TooFar, "Too far apart", cost);

        var existing = _webs.Values.FirstOrDefault(w =>
            w.Id != ignoreWebId && w.Connects(anchorA, anchorB));
        if (existing is not null)
            return PlacementResult.Fail(PlacementCode.AlreadyConnected,
                existing.Type == type ? "Already connected" : "Those anchors already hold a web", cost);

        var countA = _webs.Values.Count(w => w.Id != ignoreWebId && (w.AnchorA == anchorA || w.AnchorB == anchorA));
        var countB = _webs.Values.Count(w => w.Id != ignoreWebId && (w.AnchorA == anchorB || w.AnchorB == anchorB));
        if (countA >= a.Capacity || countB >= b.Capacity)
            return PlacementResult.Fail(PlacementCode.NoCapacity, "That anchor is full", cost);

        if (type == WebType.Bridge)
        {
            var dy = Math.Abs(b.Position.Y - a.Position.Y);
            var horiz = (b.Position - a.Position).HorizontalLength;
            var slope = Math.Atan2(dy, Math.Max(0.001, horiz)) * 180.0 / Math.PI;
            if (slope > _tuning.Webs.Bridge.MaxInclineDegrees)
                return PlacementResult.Fail(PlacementCode.TooSteep, "Too steep for a bridge", cost);
        }
        else
        {
            // Capture webs need a roughly upright plane; both anchors should be near the fly lanes.
            if (a.Position.Y < 0.2 || b.Position.Y < 0.2)
                return PlacementResult.Fail(PlacementCode.InvalidOrientation, "That web would sit on the ground", cost);
        }

        if (availableSilk < cost)
            return PlacementResult.Fail(PlacementCode.NotEnoughSilk, $"Need {cost} silk", cost);

        return PlacementResult.Ok(cost);
    }

    public static double ComputeExposure(AnchorDefinition a, AnchorDefinition b) =>
        Math.Clamp((a.Exposure + b.Exposure) / 2.0, 0, 1);

    public static string MakeWebId(WebType type, string a, string b, int sequence) =>
        $"{(type == WebType.Capture ? "capture" : "bridge")}_{Math.Min(StringComparer.Ordinal.Compare(a, b) <= 0 ? 0 : 1, 1)}_{sequence}";
}

public sealed class GroundEdge
{
    public required string A { get; init; }
    public required string B { get; init; }
    public string Kind { get; init; } = "ground";
    public bool UsesBridge => Kind == "bridge";
    public string? WebId { get; init; }
}
