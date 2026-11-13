namespace Webwild.Core;

/// <summary>Engine-free remembered player transform for one region.</summary>
public sealed record PlayerRegionPosition(Vec3 Position, double RotationY);

public static class RegionIds
{
    public const string GardenClearing = "garden_clearing";
    public const string ReedwaterBank = "reedwater_bank";

    public static bool IsKnown(string? id) => id is GardenClearing or ReedwaterBank;
}

/// <summary>
/// Engine-free description of a playable region: its authored gameplay nodes,
/// anchor sockets, traversal graph and insect routes. Scene paths intentionally
/// live in the Godot adapter, not in this library.
/// </summary>
public sealed class RegionDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string NurseryLabel { get; init; }
    public bool UnlockedByDefault { get; init; }
    public required Vec3 PlayerSpawn { get; init; }
    public required Vec3 ArrivalPoint { get; init; }
    public required Vec3 TravelPoint { get; init; }
    public required string NurseryNode { get; init; }
    public required Vec3 NurseryPosition { get; init; }
    public string FirstDestinationNode { get; init; } = "";
    public string FarBankNode { get; init; } = "";
    public required IReadOnlyList<AnchorDefinition> Anchors { get; init; }
    public required IReadOnlyList<WorldNodeDefinition> Nodes { get; init; }
    public required IReadOnlyList<GroundEdge> Edges { get; init; }
    public required IReadOnlyList<InsectRouteDefinition> InsectRoutes { get; init; }

    public AnchorDefinition? GetAnchor(string id) =>
        Anchors.FirstOrDefault(anchor => anchor.Id == id);
}

/// <summary>
/// Explicit registry for all playable regions. Adding a third region means adding
/// one definition and one scene set; the shared simulation and save model already
/// carry a region id on webs, workers, insects and player positions.
/// </summary>
public static class RegionCatalog
{
    private static readonly Lazy<RegionDefinition> LazyGarden = new(CreateGarden);
    private static readonly Lazy<RegionDefinition> LazyReedwater = new(ReedwaterLayout.CreateRegion);
    private static readonly Lazy<IReadOnlyList<RegionDefinition>> LazyAll =
        new(() => new[] { Garden, Reedwater });

    public static RegionDefinition Garden => LazyGarden.Value;
    public static RegionDefinition Reedwater => LazyReedwater.Value;
    public static IReadOnlyList<RegionDefinition> All => LazyAll.Value;

    public static RegionDefinition? Get(string? id) =>
        All.FirstOrDefault(region => region.Id == id);

    public static RegionDefinition GetRequired(string id) =>
        Get(id) ?? throw new InvalidOperationException($"Unknown region '{id}'.");

    public static AnchorDefinition? FindAnchor(string id) =>
        All.SelectMany(region => region.Anchors).FirstOrDefault(anchor => anchor.Id == id);

    public static string RegionOfAnchor(string id) =>
        FindAnchor(id)?.Region ?? "";

    public static IEnumerable<WorldNodeDefinition> AllNodes => All.SelectMany(region => region.Nodes);
    public static IEnumerable<AnchorDefinition> AllAnchors => All.SelectMany(region => region.Anchors);
    public static IEnumerable<GroundEdge> AllEdges => All.SelectMany(region => region.Edges);

    private static RegionDefinition CreateGarden()
    {
        var anchors = WorldLayout.CreateAnchors();
        var nodes = WorldLayout.CreateNodes();
        return new RegionDefinition
        {
            Id = RegionIds.GardenClearing,
            DisplayName = "Garden Clearing",
            Description = "The sunny home clearing with its ravine, flower lane and leaf nursery.",
            NurseryLabel = "Leaf Nursery",
            UnlockedByDefault = true,
            PlayerSpawn = WorldLayout.PlayerSpawn,
            ArrivalPoint = WorldLayout.TravelArrivalPoint,
            TravelPoint = WorldLayout.TravelPoint,
            NurseryNode = WorldLayout.NurseryNode,
            NurseryPosition = WorldLayout.NurseryNodePosition,
            Anchors = anchors,
            Nodes = nodes,
            Edges = WorldLayout.CreateGroundEdges(),
            InsectRoutes = WorldLayout.CreateInsectRoutes()
        };
    }
}

/// <summary>
/// Reedwater Bank is a marsh beside a shallow pond. Its local coordinates are
/// authored directly in metres, exactly like the original clearing.
/// </summary>
public static class ReedwaterLayout
{
    public const string RegionId = RegionIds.ReedwaterBank;

    public const string StartPost = "rw_start_post";
    public const string IslandAPost = "rw_island_a_post";
    public const string IslandBPost = "rw_island_b_post";
    public const string FarPost = "rw_far_post";
    public const string EasyA = "rw_reed_a";
    public const string EasyB = "rw_reed_b";
    public const string MidA = "rw_lily_a";
    public const string MidB = "rw_lily_b";
    public const string FarA = "rw_far_web_a";
    public const string FarB = "rw_far_web_b";

    public const string NurseryNode = "rw_nursery";
    public const string ArrivalNode = "rw_arrival";
    public const string FirstDestinationNode = "rw_first_destination";
    public const string FarBankNode = "rw_far_bank";

    public const string StartBankZone = "start_bank";
    public const string IslandAZone = "island_a";
    public const string IslandBZone = "island_b";
    public const string FarBankZone = "far_bank";

    public static readonly Vec3 PlayerSpawn = new(10.0, 0.55, 8.5);
    public static readonly Vec3 ArrivalPoint = new(10.0, 0.55, 8.5);
    public static readonly Vec3 TravelPoint = new(12.0, 0, 8.5);
    public static readonly Vec3 NurseryPosition = new(11.0, 0.1, 5.8);
    public static readonly Vec3 FirstDestinationPosition = new(-3.4, 0.15, 1.6);
    public static readonly Vec3 FarBankPosition = new(-10.5, 0.1, -3.6);

    // The lily island uses the authored MossIslandMedium cylinder: 1.65 m
    // radius scaled to 0.95 (1.5675 m) with its walkable top at about 0.13 m.
    // The arrival footprint stays inside that rim so a bridge deck, a lily pad
    // or open water beside the island cannot count as standing on it.
    public const double FirstDestinationWalkableRadius = 1.45;
    public const double FirstDestinationSurfaceY = 0.15;
    public const double FirstDestinationSurfaceTolerance = 0.2;

    /// <summary>
    /// Engine-free standing check for the lily island. The Godot adapter also
    /// supplies <c>IsOnFloor</c>, so a player falling past the island or standing
    /// on a bridge above it is never treated as an arrival.
    /// </summary>
    public static bool IsOnFirstDestinationIsland(Vec3 position)
    {
        var dx = position.X - FirstDestinationPosition.X;
        var dz = position.Z - FirstDestinationPosition.Z;
        if (Math.Sqrt(dx * dx + dz * dz) > FirstDestinationWalkableRadius) return false;
        return Math.Abs(position.Y - FirstDestinationSurfaceY) <= FirstDestinationSurfaceTolerance;
    }

    public static readonly Vec3 StartPostGround = new(7.6, 0.0, 7.6);
    public static readonly Vec3 IslandAPostGround = new(2.2, 0.15, 6.4);
    public static readonly Vec3 IslandBPostGround = new(-2.7, 0.15, 2.7);
    public static readonly Vec3 FarPostGround = new(-8.8, 0.0, -1.9);

    public static List<AnchorDefinition> CreateAnchors() => new()
    {
        new AnchorDefinition
        {
            Id = StartPost, Region = RegionId, Zone = StartBankZone,
            Position = new Vec3(6.6, 0.55, 7.0), Normal = new Vec3(-1, 0, 0),
            Surface = "wood", Group = "pond_bridge", Capacity = 3, Exposure = 0.35, Sheltered = true,
            Label = "Reed post", BridgeGroundPoint = StartPostGround
        },
        new AnchorDefinition
        {
            Id = IslandAPost, Region = RegionId, Zone = IslandAZone,
            Position = new Vec3(1.4, 0.55, 5.6), Normal = new Vec3(-1, 0, 0),
            Surface = "moss", Group = "pond_bridge", Capacity = 3, Exposure = 0.35, Sheltered = true,
            Label = "Mossy island post", BridgeGroundPoint = IslandAPostGround
        },
        new AnchorDefinition
        {
            Id = IslandBPost, Region = RegionId, Zone = IslandBZone,
            Position = new Vec3(-3.0, 0.55, 1.8), Normal = new Vec3(-1, 0, 0),
            Surface = "root", Group = "pond_bridge", Capacity = 3, Exposure = 0.4, Sheltered = true,
            Label = "Root island post", BridgeGroundPoint = IslandBPostGround
        },
        new AnchorDefinition
        {
            Id = FarPost, Region = RegionId, Zone = FarBankZone,
            Position = new Vec3(-7.4, 0.55, -1.0), Normal = new Vec3(-1, 0, 0),
            Surface = "wood", Group = "pond_bridge", Capacity = 3, Exposure = 0.35, Sheltered = true,
            Label = "Far bank post", BridgeGroundPoint = FarPostGround
        },
        new AnchorDefinition
        {
            Id = EasyA, Region = RegionId, Zone = StartBankZone,
            Position = new Vec3(10.0, 1.35, 10.2), Normal = new Vec3(0, 0, -1),
            Surface = "reed", Group = "reed_flyway", Capacity = 3, Exposure = 0.5,
            Label = "Reed stem"
        },
        new AnchorDefinition
        {
            Id = EasyB, Region = RegionId, Zone = StartBankZone,
            Position = new Vec3(12.4, 1.35, 10.2), Normal = new Vec3(0, 0, -1),
            Surface = "reed", Group = "reed_flyway", Capacity = 3, Exposure = 0.5,
            Label = "Reed stem"
        },
        new AnchorDefinition
        {
            Id = MidA, Region = RegionId, Zone = IslandBZone,
            Position = new Vec3(-4.8, 1.35, 1.8), Normal = new Vec3(0, 0, -1),
            Surface = "root", Group = "lily_flyway", Capacity = 3, Exposure = 0.65,
            Label = "Lily island root"
        },
        new AnchorDefinition
        {
            Id = MidB, Region = RegionId, Zone = IslandBZone,
            Position = new Vec3(-2.0, 1.35, 1.8), Normal = new Vec3(0, 0, -1),
            Surface = "root", Group = "lily_flyway", Capacity = 3, Exposure = 0.65,
            Label = "Lily island root"
        },
        new AnchorDefinition
        {
            Id = FarA, Region = RegionId, Zone = FarBankZone,
            Position = new Vec3(-12.8, 1.35, -3.6), Normal = new Vec3(0, 0, -1),
            Surface = "root", Group = "far_flyway", Capacity = 3, Exposure = 0.7,
            Label = "Far reed root"
        },
        new AnchorDefinition
        {
            Id = FarB, Region = RegionId, Zone = FarBankZone,
            Position = new Vec3(-10.0, 1.35, -3.6), Normal = new Vec3(0, 0, -1),
            Surface = "root", Group = "far_flyway", Capacity = 3, Exposure = 0.7,
            Label = "Far reed root"
        }
    };

    public static List<WorldNodeDefinition> CreateNodes()
    {
        var nodes = CreateAnchors()
            .Select(anchor => new WorldNodeDefinition
            {
                Id = anchor.Id, Position = anchor.Position, Label = anchor.Label, Region = RegionId
            })
            .ToList();
        nodes.Add(new WorldNodeDefinition
        {
            Id = NurseryNode, Position = NurseryPosition, Label = "Reedwater outpost", Region = RegionId
        });
        nodes.Add(new WorldNodeDefinition
        {
            Id = ArrivalNode, Position = ArrivalPoint, Label = "Reedwater arrival", Region = RegionId
        });
        nodes.Add(new WorldNodeDefinition
        {
            Id = FirstDestinationNode, Position = FirstDestinationPosition,
            Label = "Lily island", Region = RegionId
        });
        nodes.Add(new WorldNodeDefinition
        {
            Id = FarBankNode, Position = FarBankPosition, Label = "Far bank", Region = RegionId
        });
        return nodes;
    }

    /// <summary>
    /// Solid-ground edges only. There is deliberately no edge between the start
    /// bank, either island, or the far bank; those links can only be created by
    /// silk bridges at runtime.
    /// </summary>
    public static List<GroundEdge> CreateGroundEdges() => new()
    {
        new() { A = ArrivalNode, B = NurseryNode },
        new() { A = NurseryNode, B = StartPost },
        new() { A = NurseryNode, B = EasyA },
        new() { A = NurseryNode, B = EasyB },
        new() { A = FirstDestinationNode, B = MidA },
        new() { A = FirstDestinationNode, B = MidB },
        new() { A = FirstDestinationNode, B = IslandBPost },
        new() { A = FarPost, B = FarA },
        new() { A = FarA, B = FarB },
        new() { A = FarB, B = FarBankNode }
    };

    public static IReadOnlyList<InsectRouteDefinition> CreateInsectRoutes() => new List<InsectRouteDefinition>
    {
        // Straightforward route: a slow fly loops across the reed posts on the
        // starting bank, so a first capture web is easy to place.
        new()
        {
            Id = "rw_reed_fly", Region = RegionId, Kind = "fly", Model = "fly",
            FoodYield = 2, Speed = 1.7, RespawnSeconds = 10, StartProgress = 0.15,
            Waypoints = new[]
            {
                new Vec3(11.2, 2.0, 13.0), new Vec3(11.2, 2.0, 8.0),
                new Vec3(8.0, 2.4, 7.0), new Vec3(14.0, 2.2, 7.0),
                new Vec3(11.2, 2.0, 13.0)
            }
        },
        // Harder route: a dragonfly skims the lily island and crosses a web
        // plane that requires the player to observe its banking turn.
        new()
        {
            Id = "rw_dragonfly_lilies", Region = RegionId, Kind = "dragonfly",
            Model = "dragonfly", FoodYield = 4, Speed = 2.8, RespawnSeconds = 10,
            StartProgress = 0.45,
            Waypoints = new[]
            {
                new Vec3(-3.4, 2.3, 4.5), new Vec3(-3.4, 2.3, -1.0),
                new Vec3(0.5, 2.8, -2.0), new Vec3(-7.2, 2.6, -2.0),
                new Vec3(-3.4, 2.3, 4.5)
            }
        },
        // Far-bank route used by the worker objective once all three bridges
        // connect the outpost to the far reeds.
        new()
        {
            Id = "rw_far_dragonfly", Region = RegionId, Kind = "dragonfly",
            Model = "dragonfly", FoodYield = 4, Speed = 2.4, RespawnSeconds = 10,
            StartProgress = 0.2,
            Waypoints = new[]
            {
                new Vec3(-11.4, 2.1, -1.0), new Vec3(-11.4, 2.1, -6.5),
                new Vec3(-14.0, 2.6, -7.0), new Vec3(-8.5, 2.4, -7.0),
                new Vec3(-11.4, 2.1, -1.0)
            }
        }
    };

    public static RegionDefinition CreateRegion() => new()
    {
        Id = RegionId,
        DisplayName = "Reedwater Bank",
        Description = "A quiet marsh beside a shallow pond, crossing with silk bridges and a reed-bed outpost.",
        NurseryLabel = "Reedwater Outpost",
        PlayerSpawn = PlayerSpawn,
        ArrivalPoint = ArrivalPoint,
        TravelPoint = TravelPoint,
        NurseryNode = NurseryNode,
        NurseryPosition = NurseryPosition,
        FirstDestinationNode = FirstDestinationNode,
        FarBankNode = FarBankNode,
        Anchors = CreateAnchors(),
        Nodes = CreateNodes(),
        Edges = CreateGroundEdges(),
        InsectRoutes = CreateInsectRoutes()
    };
}
