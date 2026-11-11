namespace Webwild.Core;

public sealed class InsectRouteDefinition
{
    public required string Id { get; init; }
    public string Kind { get; init; } = "fly";
    public string Model { get; init; } = "fly";
    public int FoodYield { get; init; } = 2;
    public double Speed { get; init; } = 2.2;
    public double RespawnSeconds { get; init; } = 22;
    public required IReadOnlyList<Vec3> Waypoints { get; init; }
    public double StartProgress { get; init; }
}

/// <summary>
/// Hand-authored garden layout. Both the pure simulation and the Godot scene use
/// these positions so anchors can never drift between rules and visible props.
/// </summary>
public static class WorldLayout
{
    public const string NurseryNode = "nursery_home";
    public const string SpawnNode = "home_leaf";
    public const string GapWest = "gap_west";
    public const string GapEast = "gap_east";
    public const string TwigA = "twig_west_a";
    public const string TwigB = "twig_west_b";
    public const string FlowerA = "flower_lane_a";
    public const string FlowerB = "flower_lane_b";
    public const string WindA = "wind_edge_a";
    public const string WindB = "wind_edge_b";
    public const string ShelterA = "log_shelter_a";
    public const string ShelterB = "log_shelter_b";
    public const string StemTop = "stem_overlook";

    public static readonly Vec3 PlayerSpawn = new(-13.5, 0.45, 8.5);
    public static readonly Vec3 NurseryPosition = new(12.0, 0, 4.0);
    public static readonly Vec3 ClimbBase = new(-5.0, 0, 9.0);
    public static readonly Vec3 ClimbTop = new(-5.0, 4.4, 9.0);
    public const float RavineHalfWidth = 3.0f;
    public const float RavineDepth = 3.2f;

    public static List<AnchorDefinition> CreateAnchors() => new()
    {
        new AnchorDefinition
        {
            Id = SpawnNode, Position = new Vec3(-13.5, 1.0, 6.5), Normal = new Vec3(0, 0, 1),
            Surface = "leaf", Group = "home", Capacity = 2, Exposure = 0.25, Sheltered = true,
            Label = "Home leaf"
        },
        new AnchorDefinition
        {
            Id = TwigA, Position = new Vec3(-9.5, 1.25, -1.4), Normal = new Vec3(1, 0, 0),
            Surface = "twig", Group = "twins", Capacity = 3, Exposure = 0.55,
            Label = "Twin twig"
        },
        new AnchorDefinition
        {
            Id = TwigB, Position = new Vec3(-9.5, 1.25, 1.6), Normal = new Vec3(1, 0, 0),
            Surface = "twig", Group = "twins", Capacity = 3, Exposure = 0.55,
            Label = "Twin twig"
        },
        new AnchorDefinition
        {
            Id = GapWest, Position = new Vec3(-3.0, 0.55, 1.2), Normal = new Vec3(-1, 0, 0),
            Surface = "pebble", Group = "gap", Capacity = 2, Exposure = 0.2, Sheltered = true,
            Label = "Pebble post"
        },
        new AnchorDefinition
        {
            Id = GapEast, Position = new Vec3(3.0, 0.55, 1.2), Normal = new Vec3(1, 0, 0),
            Surface = "pebble", Group = "gap", Capacity = 2, Exposure = 0.2, Sheltered = true,
            Label = "Pebble post"
        },
        new AnchorDefinition
        {
            Id = FlowerA, Position = new Vec3(4.4, 1.35, -6.2), Normal = new Vec3(0, 0, -1),
            Surface = "flower", Group = "flowers", Capacity = 3, Exposure = 0.45,
            Label = "Flower stem"
        },
        new AnchorDefinition
        {
            Id = FlowerB, Position = new Vec3(6.2, 1.35, -6.2), Normal = new Vec3(0, 0, -1),
            Surface = "flower", Group = "flowers", Capacity = 3, Exposure = 0.45,
            Label = "Flower stem"
        },
        new AnchorDefinition
        {
            Id = WindA, Position = new Vec3(9.5, 1.35, -8.0), Normal = new Vec3(0, 0, -1),
            Surface = "twig", Group = "wind_edge", Capacity = 3, Exposure = 0.95,
            Label = "Windy twig"
        },
        new AnchorDefinition
        {
            Id = WindB, Position = new Vec3(13.5, 1.35, -8.0), Normal = new Vec3(0, 0, -1),
            Surface = "twig", Group = "wind_edge", Capacity = 3, Exposure = 0.95,
            Label = "Windy twig"
        },
        new AnchorDefinition
        {
            Id = ShelterA, Position = new Vec3(9.8, 1.0, -3.0), Normal = new Vec3(0, 0, 1),
            Surface = "bark", Group = "log_shelter", Capacity = 3, Exposure = 0.12, Sheltered = true,
            Label = "Log nook"
        },
        new AnchorDefinition
        {
            Id = ShelterB, Position = new Vec3(13.2, 1.0, -3.0), Normal = new Vec3(0, 0, 1),
            Surface = "bark", Group = "log_shelter", Capacity = 3, Exposure = 0.12, Sheltered = true,
            Label = "Log nook"
        },
        new AnchorDefinition
        {
            Id = StemTop, Position = new Vec3(-5.0, 4.55, 9.0), Normal = new Vec3(0, 0, 1),
            Surface = "leaf", Group = "overlook", Capacity = 2, Exposure = 0.7,
            Label = "Overlook leaf"
        }
    };

    public static List<WorldNodeDefinition> CreateNodes()
    {
        var nodes = CreateAnchors()
            .Select(a => new WorldNodeDefinition { Id = a.Id, Position = a.Position, Label = a.Label })
            .ToList();
        nodes.Add(new WorldNodeDefinition
        {
            Id = NurseryNode, Position = new Vec3(12.0, 0.1, 4.0), Label = "Leaf nursery"
        });
        return nodes;
    }

    public static List<GroundEdge> CreateGroundEdges() => new()
    {
        new() { A = SpawnNode, B = TwigA },
        new() { A = TwigA, B = TwigB },
        new() { A = TwigB, B = GapWest },
        new() { A = GapWest, B = StemTop, Kind = "climb" },
        new() { A = GapEast, B = FlowerA },
        new() { A = FlowerA, B = FlowerB },
        new() { A = GapEast, B = NurseryNode },
        new() { A = NurseryNode, B = ShelterA },
        new() { A = ShelterA, B = ShelterB },
        new() { A = ShelterB, B = WindA },
        new() { A = WindA, B = WindB },
        new() { A = FlowerB, B = WindA }
    };

    public static IReadOnlyList<InsectRouteDefinition> CreateInsectRoutes() => new List<InsectRouteDefinition>
    {
        // Route 1 crosses the Twin Twigs capture plane at x=-9.5, z≈0.1.
        new()
        {
            Id = "fly_twins", Kind = "fly", Model = "fly", FoodYield = 2, Speed = 2.3,
            RespawnSeconds = 20, StartProgress = 0.08,
            Waypoints = new[]
            {
                new Vec3(-15.0, 2.0, 0.1), new Vec3(-7.0, 2.0, 0.1),
                new Vec3(-6.2, 2.5, 3.2), new Vec3(-12.0, 2.4, 4.2),
                new Vec3(-15.0, 2.0, 0.1)
            }
        },
        // Route 2 crosses the Flower Lane plane at z=-6.2 and the log-shelter plane at z=-3.
        new()
        {
            Id = "moth_flowers", Kind = "moth", Model = "moth", FoodYield = 3, Speed = 1.6,
            RespawnSeconds = 30, StartProgress = 0.35,
            Waypoints = new[]
            {
                new Vec3(11.0, 1.8, -6.2), new Vec3(1.0, 1.8, -6.2),
                new Vec3(1.0, 1.9, 0.0), new Vec3(11.0, 1.8, -3.0),
                new Vec3(11.0, 1.8, -6.2)
            }
        },
        // Route 3 crosses the exposed Wind Edge plane at z=-8.0.
        new()
        {
            Id = "fly_wind", Kind = "fly", Model = "fly", FoodYield = 2, Speed = 2.1,
            RespawnSeconds = 22, StartProgress = 0.55,
            Waypoints = new[]
            {
                new Vec3(15.5, 1.9, -8.0), new Vec3(7.5, 1.9, -8.0),
                new Vec3(7.5, 2.3, -11.0), new Vec3(15.5, 2.2, -11.0),
                new Vec3(15.5, 1.9, -8.0)
            }
        },
        // Route 4 crosses the sheltered log nook at z=-3, x≈11.
        new()
        {
            Id = "fly_shelter", Kind = "fly", Model = "fly", FoodYield = 2, Speed = 2.0,
            RespawnSeconds = 24, StartProgress = 0.7,
            Waypoints = new[]
            {
                new Vec3(7.8, 1.7, -3.0), new Vec3(15.4, 1.7, -3.0),
                new Vec3(15.4, 2.0, -0.5), new Vec3(7.8, 2.0, -0.5),
                new Vec3(7.8, 1.7, -3.0)
            }
        }
    };

    public static string SuggestedTutorialPair => $"{TwigA} + {TwigB}";
    public static string SuggestedBridgePair => $"{GapWest} + {GapEast}";
}
