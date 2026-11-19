namespace Webwild.Core;

/// <summary>
/// One player-built silk bridge over a generated river. Like a web it is a
/// colony construction with a stable id; like a generated crossing its
/// geometry is derived from the river at build time and stored so the deck,
/// its collision and its worker navigation edges rebuild identically after
/// any chunk unload, reload or save.
/// </summary>
public sealed record PlayerRiverBridge(
    string Id,
    int RiverIndex,
    double X,
    double CenterZ,
    double HalfWidth,
    double WaterLevel,
    double HalfSpan,
    double NorthGroundY,
    double SouthGroundY,
    long ConstructedAtTick)
{
    /// <summary>Posts stand this far above each bank; the silk deck meets
    /// them and the ramps climb the last stretch to the ground.</summary>
    public const double AnchorRise = 0.5;

    /// <summary>Bank ramp length, matching the authored bridge default.</summary>
    public const double RampOutset = 1.2;

    public double NorthZ => CenterZ - HalfSpan;
    public double SouthZ => CenterZ + HalfSpan;

    /// <summary>Deck end on the north bank, expressed in logical world
    /// coordinates. Shared by the preview, the built view and the worker
    /// deck points so all three agree on where the silk meets the posts.</summary>
    public Vec3 NorthAnchor => new(X, NorthGroundY + AnchorRise, NorthZ);

    /// <summary>Deck end on the south bank.</summary>
    public Vec3 SouthAnchor => new(X, SouthGroundY + AnchorRise, SouthZ);

    /// <summary>Solid ground at the foot of the north ramp.</summary>
    public Vec3 NorthGround => new(X, NorthGroundY, NorthZ - RampOutset);

    /// <summary>Solid ground at the foot of the south ramp.</summary>
    public Vec3 SouthGround => new(X, SouthGroundY, SouthZ + RampOutset);

    /// <summary>Horizontal span between the two posts.</summary>
    public double SpanLength => HalfSpan * 2.0;

    /// <summary>True when a world point lies on this bridge's deck strip:
    /// within the walkable width in X and the ramp-to-ramp reach in Z.</summary>
    public bool Covers(double x, double z) =>
        Math.Abs(x - X) <= 1.25 &&
        z >= NorthZ - RampOutset - 0.4 &&
        z <= SouthZ + RampOutset + 0.4;

    /// <summary>Deck centreline the worker follows: up the north ramp, the
    /// sagging silk span between the posts, and down the south ramp. Matches
    /// the collision surface built from <see cref="BridgeGeometry"/> so a
    /// crossing worker never floats above or sinks through the visible deck.</summary>
    public List<Vec3> DeckPoints()
    {
        var points = new List<Vec3>();
        var north = NorthAnchor;
        var south = SouthAnchor;
        var sag = BridgeGeometry.ComputeSag(SpanLength);
        var rampSteps = 2;
        for (var i = 1; i <= rampSteps; i++)
        {
            var t = i / (double)rampSteps;
            points.Add(new Vec3(
                NorthGround.X + (north.X - NorthGround.X) * t,
                NorthGround.Y + (north.Y - NorthGround.Y) * t,
                NorthGround.Z + (north.Z - NorthGround.Z) * t));
        }
        var deckSteps = Math.Max(2, (int)Math.Ceiling(SpanLength / 2.0));
        for (var i = 1; i <= deckSteps; i++)
        {
            var t = i / (double)deckSteps;
            points.Add(BridgeGeometry.EvaluateSpan(north, south, t, sag));
        }
        for (var i = 1; i <= rampSteps; i++)
        {
            var t = i / (double)rampSteps;
            points.Add(new Vec3(
                south.X + (SouthGround.X - south.X) * t,
                south.Y + (SouthGround.Y - south.Y) * t,
                south.Z + (SouthGround.Z - south.Z) * t));
        }
        return points;
    }
}

/// <summary>An existing crossing the spacing policy wants the player to reuse
/// instead of building a second bridge.</summary>
public sealed record NearbyCrossing(
    string Id,
    bool IsPlayerBuilt,
    double X,
    double CenterZ,
    double HalfSpan,
    double RouteDistance,
    double DirectDistance,
    double Ratio)
{
    /// <summary>Bank-end approach point on the side the follower stands on, so
    /// guidance leads onto the deck instead of into the water.</summary>
    public double ApproachZ(bool northSide) =>
        CenterZ + (northSide ? -1.0 : 1.0) * (HalfSpan + 1.5);

    public string Describe() =>
        $"bridge at x={X:0.#} · route {RouteDistance:0.#} m · detour ×{Ratio:0.0}";
}

/// <summary>
/// The answer to "may I build a silk bridge here?". An accepted plan carries
/// the resolved site and cost; a refused plan carries a player-readable reason
/// and, when the spacing policy found one, the nearby crossing to use instead.
/// </summary>
public sealed record RiverBuildPlan(
    bool Acceptable,
    string Reason,
    PlayerRiverBridge? Site,
    int SilkCost,
    NearbyCrossing? Nearby)
{
    public static RiverBuildPlan Accept(PlayerRiverBridge site, int cost) =>
        new(true, "", site, cost, null);

    public static RiverBuildPlan Refuse(string reason, NearbyCrossing? nearby = null) =>
        new(false, reason, null, 0, nearby);
}

/// <summary>
/// Pure planning for player-built river crossings. Everything is derived from
/// the deterministic river field and the same worker navigator the colony
/// uses, so the planner, the route search and the visible world can never
/// disagree about where the water is or which crossing is usable.
/// </summary>
public static class RiverBridgePlanner
{
    /// <summary>Sites snap to this grid so a player standing still always
    /// plans the identical crossing and previews never flicker.</summary>
    public const double SiteSnapMetres = 0.25;

    /// <summary>Authored landmarks keep a clearance around them, matching the
    /// generated-crossing suppression in <see cref="RiverMap.BridgeAt"/>.</summary>
    public const double AuthoredClearance = 24;

    /// <summary>Plan a crossing for a player standing near a river bank.
    /// Never mutates any state; the caller decides whether to build.</summary>
    public static RiverBuildPlan Plan(
        WorldGenerator generator,
        WorkerNavigator navigator,
        RiverBridgeTuning tuning,
        IReadOnlyCollection<PlayerRiverBridge> existing,
        Vec3 query)
    {
        if (!query.IsFinite)
            return RiverBuildPlan.Refuse("There is no crossing to plan here");

        var river = generator.RiverAt(query.X, query.Z);
        if (!river.HasRiver)
            return RiverBuildPlan.Refuse("There is no water to cross here");

        // The player must be at the water's edge, not deep in the channel and
        // not admiring the river from a distance.
        if (river.Distance <= river.HalfWidth - 0.5)
            return RiverBuildPlan.Refuse("Wade back onto the bank to plan a crossing");
        if (river.Distance > river.HalfWidth + RiverMap.BankTaper + tuning.BankApproachMetres)
            return RiverBuildPlan.Refuse("Move closer to the water's edge to plan a crossing");

        var x = Math.Round(query.X / SiteSnapMetres) * SiteSnapMetres;
        var centerZ = RiverMap.CenterAt(river.Index, x);
        if (WorldGenerator.AuthoredRects.Any(rect =>
                rect.DistanceOutside(x, centerZ) < AuthoredClearance))
            return RiverBuildPlan.Refuse("The garden paths are too close to bridge here");

        var halfWidth = RiverMap.HalfWidthAt(river.Index, x);
        var waterLevel = generator.Rivers.WaterLevelAt(river.Index, x);
        var halfSpan = halfWidth + RiverMap.BankTaper + 0.8;
        var northZ = centerZ - halfSpan;
        var southZ = centerZ + halfSpan;
        var northY = generator.HeightAt(x, northZ);
        var southY = generator.HeightAt(x, southZ);

        var candidate = new PlayerRiverBridge(
            "", river.Index, x, centerZ, halfWidth, waterLevel, halfSpan,
            northY, southY, 0);

        // Grounded, stable banks: the deck must start above the water and the
        // two bank heights must be within the same incline budget as an
        // authored bridge.
        if (northY < waterLevel + tuning.MinBankClearance ||
            southY < waterLevel + tuning.MinBankClearance)
            return RiverBuildPlan.Refuse("The banks are too low above the water here");
        var incline = Math.Atan2(Math.Abs(southY - northY), candidate.SpanLength) * 180.0 / Math.PI;
        if (incline > tuning.MaxInclineDegrees)
            return RiverBuildPlan.Refuse("The banks are too steep to bridge here");
        var sag = BridgeGeometry.ComputeSag(candidate.SpanLength);
        if (Math.Min(northY, southY) + PlayerRiverBridge.AnchorRise - sag <
            waterLevel + tuning.MinBankClearance)
            return RiverBuildPlan.Refuse("The banks are too low above the water here");

        // Spacing policy: existing usable crossings on this river first.
        var crossings = ExistingCrossings(generator, existing, river.Index, x,
            tuning.GuidanceSearchMetres);

        // Hard rule: a deck within the shore spacing shares bank ramps with an
        // existing bridge; refuse and point at it.
        var overlapping = crossings
            .Where(c => Math.Abs(c.X - x) < tuning.MinShoreSpacingMetres)
            .OrderBy(c => Math.Abs(c.X - x))
            .ToList();
        if (overlapping.Count > 0)
            return RiverBuildPlan.Refuse(
                "A bridge already crosses the water here — use it instead",
                DescribeCrossing(generator, navigator, tuning, candidate, overlapping[0]));

        // Detour rule: when the nearest existing crossing makes the journey
        // for a small enough detour, prefer it over a new bridge.
        var northBank = new Vec3(x, northY + 0.3, northZ);
        var southBank = new Vec3(x, southY + 0.3, southZ);
        var direct = candidate.SpanLength + 2 * tuning.ApproachMetres;
        NearbyCrossing? guidance = null;
        foreach (var crossing in crossings
                     .OrderBy(c => Math.Abs(c.X - x)))
        {
            var nearby = DescribeCrossing(generator, navigator, tuning, candidate,
                crossing, northBank, southBank, direct);
            if (nearby is null) continue;
            if (nearby.Ratio <= tuning.MaxDetourRatio)
            {
                guidance = nearby;
                break;
            }
        }
        if (guidance is not null)
            return RiverBuildPlan.Refuse(
                "A nearby bridge already crosses the water — use it instead",
                guidance);

        return RiverBuildPlan.Accept(candidate, tuning.SilkCost);
    }

    /// <summary>Existing crossings on one river within a search distance
    /// along the shore: generated landmarks plus the player's own silk.</summary>
    public static List<ExistingCrossing> ExistingCrossings(
        WorldGenerator generator,
        IReadOnlyCollection<PlayerRiverBridge> existing,
        int riverIndex,
        double x,
        double searchMetres)
    {
        var crossings = new List<ExistingCrossing>();
        var first = (long)Math.Floor((x - searchMetres) / RiverMap.CrossingSpacing) - 1;
        var last = (long)Math.Ceiling((x + searchMetres) / RiverMap.CrossingSpacing) + 1;
        for (var n = first; n <= last; n++)
        {
            var bridge = generator.Rivers.BridgeAt(riverIndex, (int)n, generator);
            if (bridge is null) continue;
            if (Math.Abs(bridge.X - x) > searchMetres) continue;
            crossings.Add(new ExistingCrossing(bridge.Id, false, bridge.X, bridge.CenterZ,
                bridge.HalfSpan));
        }
        foreach (var player in existing)
        {
            if (player.RiverIndex != riverIndex) continue;
            if (Math.Abs(player.X - x) > searchMetres) continue;
            crossings.Add(new ExistingCrossing(player.Id, true, player.X, player.CenterZ,
                player.HalfSpan));
        }
        return crossings;
    }

    private static NearbyCrossing? DescribeCrossing(
        WorldGenerator generator,
        WorkerNavigator navigator,
        RiverBridgeTuning tuning,
        PlayerRiverBridge site,
        ExistingCrossing crossing,
        Vec3? northBankOverride = null,
        Vec3? southBankOverride = null,
        double directOverride = 0)
    {
        var northBank = northBankOverride ??
                        new Vec3(site.X, site.NorthGroundY + 0.3, site.NorthZ);
        var southBank = southBankOverride ??
                        new Vec3(site.X, site.SouthGroundY + 0.3, site.SouthZ);
        var direct = directOverride > 0
            ? directOverride
            : site.SpanLength + 2 * tuning.ApproachMetres;
        var route = navigator.FindRoute(northBank, southBank);
        if (route is null) return null;
        return new NearbyCrossing(
            crossing.Id, crossing.IsPlayerBuilt, crossing.X, crossing.CenterZ,
            crossing.HalfSpan, route.Distance, direct, route.Distance / direct);
    }
}

/// <summary>One existing crossing on a river, from either source.</summary>
public sealed record ExistingCrossing(
    string Id, bool IsPlayerBuilt, double X, double CenterZ, double HalfSpan);
