using Godot;
using Webwild.Core;

namespace Webwild.Game;

/// <summary>
/// Hand-authored scenery for the single garden clearing. Every prop is placed
/// against the same coordinates used by the pure simulation.
/// </summary>
public static class GardenScenery
{
    public static readonly Color Grass = new("77A85A");
    public static readonly Color GrassLight = new("8DB86A");
    public static readonly Color Soil = new("8A6244");
    public static readonly Color SoilDark = new("6D4A34");
    public static readonly Color Stone = new("B7B3A4");

    public static Node3D Build(Node3D parent, bool titleBackdrop = false)
    {
        var world = new Node3D { Name = "GardenScenery" };
        parent.AddChild(world);

        AddEnvironment(world);
        AddGround(world);
        AddPath(world);
        AddLandmarks(world);
        AddFlora(world);
        AddBoundary(world);
        return world;
    }

    private static void AddEnvironment(Node3D parent)
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color("6FB9DD"),
                    SkyHorizonColor = new Color("D8F0EE"),
                    GroundHorizonColor = new Color("CDE4C0"),
                    GroundBottomColor = new Color("678B54"),
                    SunAngleMax = 24f,
                    SunCurve = 0.12f
                }
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightColor = new Color("FFF4DA"),
            AmbientLightEnergy = 0.45f,
            AmbientLightSkyContribution = 0.55f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 0.78f,
            FogEnabled = true,
            FogLightColor = new Color("DDEFE4"),
            FogDensity = 0.0016f,
            FogSkyAffect = 0.25f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.04f,
            AdjustmentContrast = 1.01f
        };
        parent.AddChild(new WorldEnvironment { Name = "GardenEnvironment", Environment = environment });

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-52, -38, 0),
            LightColor = new Color("FFF0CD"),
            LightEnergy = 0.9f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 70f
        };
        parent.AddChild(sun);

        var fill = new OmniLight3D
        {
            Name = "NurseryFill",
            Position = new Vector3(11f, 3.2f, 4f),
            LightColor = new Color("FFE6B8"),
            LightEnergy = 0.35f,
            OmniRange = 9f
        };
        parent.AddChild(fill);
    }

    private static void AddGround(Node3D parent)
    {
        var ground = new StaticBody3D
        {
            Name = "Ground",
            CollisionLayer = 1,
            CollisionMask = 0
        };
        var collision = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(38f, 0.6f, 28f) },
            Position = new Vector3(0, -0.3f, 0)
        };
        ground.AddChild(collision);

        var mesh = new MeshInstance3D
        {
            Name = "MeadowTop",
            Mesh = new PlaneMesh { Size = new Vector2(38f, 28f), SubdivideWidth = 2, SubdivideDepth = 2 },
            MaterialOverride = Flat(Grass, 0.95f),
            Position = new Vector3(0, 0.002f, 0)
        };
        ground.AddChild(mesh);
        parent.AddChild(ground);

        // Layered patches break up the large green plane without hiding anchors.
        var patchPositions = new[]
        {
            new Vector3(-13f, 0, 5f), new Vector3(-8f, 0, 8f), new Vector3(-4f, 0, -4f),
            new Vector3(2f, 0, 6f), new Vector3(8f, 0, 8f), new Vector3(13f, 0, -7f),
            new Vector3(5f, 0, -10f), new Vector3(-15f, 0, -9f), new Vector3(15f, 0, 6f)
        };
        for (var i = 0; i < patchPositions.Length; i++)
        {
            var patch = new MeshInstance3D
            {
                Name = $"GrassPatch{i}",
                Mesh = new CylinderMesh
                {
                    TopRadius = 2.1f + (i % 3) * 0.45f,
                    BottomRadius = 2.4f + (i % 3) * 0.45f,
                    Height = 0.025f,
                    RadialSegments = 16
                },
                MaterialOverride = Flat(i % 2 == 0 ? GrassLight : new Color("6E9D53"), 0.96f),
                Position = patchPositions[i] + new Vector3(0, 0.012f, 0)
            };
            parent.AddChild(patch);
        }
    }

    private static void AddPath(Node3D parent)
    {
        var path = new[]
        {
            new Vector3(-13.0f, 0, 7.4f), new Vector3(-11.8f, 0, 5.2f),
            new Vector3(-10.6f, 0, 3.2f), new Vector3(-9.7f, 0, 1.2f),
            new Vector3(-7.4f, 0, 0.8f), new Vector3(-5.2f, 0, 1.0f),
            new Vector3(-3.0f, 0, 1.2f), new Vector3(-0.2f, 0, 1.1f),
            new Vector3(2.6f, 0, 1.2f), new Vector3(5.0f, 0, 2.2f),
            new Vector3(7.5f, 0, 3.4f), new Vector3(10.0f, 0, 4.0f),
            new Vector3(12.0f, 0, 4.2f)
        };
        for (var i = 0; i < path.Length; i++)
        {
            var stone = Spawn(parent, "res://art/models/terrain/flat_stone.glb",
                path[i] + new Vector3(0, 0.035f, 0), 0.42f + (i % 3) * 0.05f, i * 37f, $"PathStone{i}");
            if (stone is not null) stone.Scale = new Vector3(stone.Scale.X * 1.4f, stone.Scale.Y, stone.Scale.Z);
        }

        // A soft soil line under the stones makes the route readable from afar.
        for (var i = 0; i < path.Length - 1; i++)
        {
            var a = path[i];
            var b = path[i + 1];
            var centre = (a + b) * 0.5f;
            var length = a.DistanceTo(b);
            var soil = new MeshInstance3D
            {
                Name = $"PathSoil{i}",
                Mesh = new BoxMesh { Size = new Vector3(0.95f, 0.018f, length + 0.25f) },
                MaterialOverride = Flat(i % 2 == 0 ? Soil : SoilDark, 1f),
                Position = centre + new Vector3(0, 0.018f, 0)
            };
            soil.LookAtFromPosition(soil.Position, b + new Vector3(0, 0.018f, 0), Vector3.Up);
            parent.AddChild(soil);
        }
    }

    private static void AddLandmarks(Node3D parent)
    {
        // Starter leaf and home sparkle.
        Spawn(parent, "res://art/models/flora/leaf_platform.glb", new Vector3(-13.6f, 0.04f, 6.7f), 1.25f, 18f, "HomeLeaf");
        Spawn(parent, "res://art/models/colony/cache_leaf.glb", new Vector3(-15.2f, 0.02f, 8.2f), 0.9f, -35f, "HomeCache");

        // Twin Twigs (first capture lane).
        Spawn(parent, "res://art/models/terrain/twig_fork.glb", new Vector3(-9.5f, 0, -1.4f), 1.05f, 8f, "TwinTwigA");
        Spawn(parent, "res://art/models/terrain/twig_fork.glb", new Vector3(-9.5f, 0, 1.6f), 1.05f, -12f, "TwinTwigB");
        Spawn(parent, "res://art/models/terrain/crossing_twigs.glb", new Vector3(-9.5f, 0.03f, 0.1f), 0.7f, 90f, "TwinCrossing");

        // Pebble gap.
        Spawn(parent, "res://art/models/terrain/pebble.glb", new Vector3(-3.0f, 0.02f, 1.2f), 1.35f, 24f, "GapWestPebble");
        Spawn(parent, "res://art/models/terrain/pebble.glb", new Vector3(3.0f, 0.02f, 1.2f), 1.35f, -18f, "GapEastPebble");
        Spawn(parent, "res://art/models/terrain/pebble_cluster.glb", new Vector3(0, 0.02f, 1.2f), 0.7f, 6f, "GapDip");
        var dip = new MeshInstance3D
        {
            Name = "GapShade",
            Mesh = new CylinderMesh { TopRadius = 2.1f, BottomRadius = 2.4f, Height = 0.02f, RadialSegments = 20 },
            MaterialOverride = Flat(new Color("6D8F55"), 1f),
            Position = new Vector3(0, 0.015f, 1.2f)
        };
        parent.AddChild(dip);

        // Flower lane.
        Spawn(parent, "res://art/models/flora/flower_pink.glb", new Vector3(2.0f, 0, -6.2f), 1.5f, 12f, "FlowerA");
        Spawn(parent, "res://art/models/flora/flower_purple.glb", new Vector3(6.2f, 0, -6.2f), 1.5f, -14f, "FlowerB");
        Spawn(parent, "res://art/models/flora/daisy.glb", new Vector3(4.1f, 0, -5.1f), 1.15f, 70f, "FlowerMiddle");

        // Wind edge.
        Spawn(parent, "res://art/models/flora/reed.glb", new Vector3(9.5f, 0, -8.0f), 1.45f, 10f, "WindReedA");
        Spawn(parent, "res://art/models/flora/reed.glb", new Vector3(13.5f, 0, -8.0f), 1.45f, -20f, "WindReedB");
        Spawn(parent, "res://art/models/flora/tall_grass.glb", new Vector3(16.8f, 0, -9.2f), 1.3f, 0, "WindGrassA");
        Spawn(parent, "res://art/models/flora/tall_grass.glb", new Vector3(7.0f, 0, -10.2f), 1.2f, 80f, "WindGrassB");

        // Sheltered log nook.
        var log = Spawn(parent, "res://art/models/terrain/fallen_log.glb", new Vector3(11.5f, 0.05f, -1.2f), 1.55f, 8f, "FallenLog");
        if (log is not null) log.Scale = new Vector3(log.Scale.X * 1.35f, log.Scale.Y, log.Scale.Z * 1.35f);
        Spawn(parent, "res://art/models/terrain/moss_rock.glb", new Vector3(8.4f, 0.02f, -2.1f), 1.2f, 3f, "LogRock");
        Spawn(parent, "res://art/models/flora/moss_patch.glb", new Vector3(10.0f, 0.03f, -3.1f), 1.0f, 20f, "LogMoss");

        // Leaf nursery.
        Spawn(parent, "res://art/models/colony/leaf_nursery.glb", new Vector3(12.0f, 0.05f, 4.0f), 1.6f, 8f, "LeafNursery");
        Spawn(parent, "res://art/models/colony/nursery_canopy.glb", new Vector3(12.0f, 1.15f, 4.0f), 1.35f, 8f, "NurseryCanopy");
        Spawn(parent, "res://art/models/colony/egg_cluster.glb", new Vector3(13.0f, 0.15f, 3.7f), 0.85f, 20f, "NurseryEggs");
        Spawn(parent, "res://art/models/colony/silk_cocoon.glb", new Vector3(11.1f, 0.12f, 4.6f), 0.7f, 0, "NurseryCocoon");

        // Tall stem and overlook.
        Spawn(parent, "res://art/models/flora/sapling.glb", new Vector3(-1.5f, 0, 9.0f), 1.5f, 22f, "ClimbSapling");
        var stem = new MeshInstance3D
        {
            Name = "ClimbStem",
            Mesh = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.18f, Height = 4.6f, RadialSegments = 10 },
            MaterialOverride = Flat(new Color("5E8E45"), 0.92f),
            Position = new Vector3(-1.5f, 2.3f, 9.0f)
        };
        parent.AddChild(stem);
        for (var i = 0; i < 4; i++)
        {
            Spawn(parent, "res://art/models/flora/broad_leaf.glb",
                new Vector3(-1.5f + (i % 2 == 0 ? 0.36f : -0.36f), 1.1f + i * 0.9f, 9.0f),
                0.62f, i * 70f, $"ClimbLeaf{i}");
        }
        Spawn(parent, "res://art/models/flora/leaf_platform.glb", new Vector3(-1.5f, 4.25f, 9.0f), 0.85f, 0, "OverlookLeaf");

        // The map board-like stone marker at spawn.
        Spawn(parent, "res://art/models/colony/explore_marker.glb", new Vector3(-12.3f, 0.05f, 7.7f), 0.75f, 0, "SpawnMarker");
    }

    private static void AddFlora(Node3D parent)
    {
        var rng = new Random(27011);
        var clover = new[]
        {
            new Vector3(-14.8f, 0, 2.5f), new Vector3(-12.4f, 0, -2.8f), new Vector3(-10.8f, 0, 5.8f),
            new Vector3(-7.2f, 0, 5.4f), new Vector3(-6.4f, 0, -4.8f), new Vector3(-2.0f, 0, 5.4f),
            new Vector3(1.8f, 0, 7.6f), new Vector3(5.2f, 0, 8.9f), new Vector3(8.8f, 0, 7.4f),
            new Vector3(14.6f, 0, 8.0f), new Vector3(15.1f, 0, -5.2f), new Vector3(5.8f, 0, -11.1f),
            new Vector3(-4.8f, 0, -10.4f), new Vector3(-15.2f, 0, -6.2f), new Vector3(16.2f, 0, 0.8f)
        };
        for (var i = 0; i < clover.Length; i++)
        {
            var path = i % 3 == 0 ? "res://art/models/flora/clover_tall.glb" : "res://art/models/flora/clover_patch.glb";
            Spawn(parent, path, clover[i], 0.65f + (i % 4) * 0.08f, i * 29f, $"Clover{i}");
        }

        var tufts = new[]
        {
            new Vector3(-16.8f, 0, 4.2f), new Vector3(-16.2f, 0, -2.6f), new Vector3(-12.8f, 0, 11.4f),
            new Vector3(-5.5f, 0, 11.0f), new Vector3(2.4f, 0, 11.8f), new Vector3(9.8f, 0, 11.1f),
            new Vector3(15.0f, 0, 1.2f), new Vector3(16.9f, 0, -3.6f), new Vector3(12.5f, 0, -12.0f),
            new Vector3(1.4f, 0, -12.3f), new Vector3(-8.4f, 0, -11.6f), new Vector3(-16.1f, 0, -10.2f)
        };
        for (var i = 0; i < tufts.Length; i++)
        {
            Spawn(parent, i % 2 == 0 ? "res://art/models/flora/grass_tuft.glb" : "res://art/models/flora/fern.glb",
                tufts[i], 0.75f + (i % 3) * 0.12f, i * 41f, $"Tuft{i}");
        }

        var mushrooms = new[]
        {
            new Vector3(-12.0f, 0, -6.4f), new Vector3(-11.1f, 0, -6.8f),
            new Vector3(6.7f, 0, 6.4f), new Vector3(7.3f, 0, 6.9f),
            new Vector3(14.2f, 0, -8.7f), new Vector3(-7.7f, 0, 9.6f)
        };
        for (var i = 0; i < mushrooms.Length; i++)
        {
            Spawn(parent, i % 2 == 0 ? "res://art/models/flora/mushroom.glb" : "res://art/models/flora/mushroom_blue.glb",
                mushrooms[i], 0.55f + (i % 2) * 0.12f, rng.Next(0, 360), $"Mushroom{i}");
        }

        var rocks = new[]
        {
            new Vector3(-15.4f, 0.02f, 0.2f), new Vector3(4.9f, 0.02f, -2.8f), new Vector3(13.9f, 0.02f, 10.0f)
        };
        for (var i = 0; i < rocks.Length; i++)
        {
            Spawn(parent, "res://art/models/terrain/moss_rock.glb", rocks[i], 0.9f + i * 0.15f, i * 63f, $"FeatureRock{i}");
        }

        foreach (var (position, model, scale, rotation) in new[]
                 {
                     (new Vector3(-15.7f, 0, 10.6f), "res://art/models/flora/sunflower.glb", 1.0f, 10f),
                     (new Vector3(16.5f, 0, 10.2f), "res://art/models/flora/sunflower.glb", 0.95f, -14f),
                     (new Vector3(-4.9f, 0, -9.3f), "res://art/models/flora/bush_berries.glb", 0.8f, 0f),
                     (new Vector3(8.2f, 0, -11.0f), "res://art/models/flora/bush.glb", 0.78f, 32f),
                     (new Vector3(-16.4f, 0, -5.0f), "res://art/models/flora/fern.glb", 1.0f, 0f),
                     (new Vector3(16.6f, 0, 4.7f), "res://art/models/flora/bush.glb", 0.85f, -20f)
                 })
        {
            Spawn(parent, model, position, scale, rotation, "FloraFeature");
        }
    }

    private static void AddBoundary(Node3D parent)
    {
        // Visible foliage sits in front of gentle collision hedges, so the child
        // sees why the garden ends instead of hitting an invisible wall.
        var boundary = new Node3D { Name = "BoundaryHedges" };
        parent.AddChild(boundary);

        AddHedgeWall(boundary, new Vector3(0, 0, -13.45f), new Vector3(38f, 3.2f, 0.8f));
        AddHedgeWall(boundary, new Vector3(0, 0, 13.45f), new Vector3(38f, 3.2f, 0.8f));
        AddHedgeWall(boundary, new Vector3(-18.45f, 0, 0), new Vector3(0.8f, 3.2f, 28f));
        AddHedgeWall(boundary, new Vector3(18.45f, 0, 0), new Vector3(0.8f, 3.2f, 28f));

        var spots = new List<Vector3>();
        for (var x = -18f; x <= 18f; x += 3f)
        {
            spots.Add(new Vector3(x, 0, -12.9f));
            spots.Add(new Vector3(x, 0, 12.9f));
        }
        for (var z = -11f; z <= 11f; z += 3f)
        {
            spots.Add(new Vector3(-17.9f, 0, z));
            spots.Add(new Vector3(17.9f, 0, z));
        }
        for (var i = 0; i < spots.Count; i++)
        {
            var model = (i % 5) switch
            {
                0 => "res://art/models/flora/bush.glb",
                1 => "res://art/models/flora/bush_berries.glb",
                2 => "res://art/models/flora/fern.glb",
                3 => "res://art/models/flora/tall_grass.glb",
                _ => "res://art/models/flora/sunflower.glb"
            };
            Spawn(boundary, model, spots[i], 0.72f + (i % 4) * 0.06f, i * 23f, $"Hedge{i}");
        }
    }

    private static void AddHedgeWall(Node3D parent, Vector3 position, Vector3 size)
    {
        var body = new StaticBody3D
        {
            Name = "HedgeCollision",
            Position = position,
            CollisionLayer = 1,
            CollisionMask = 0
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = size },
            Position = new Vector3(0, size.Y * 0.5f, 0)
        });
        parent.AddChild(body);
    }

    public static Node3D? Spawn(Node3D parent, string path, Vector3 position, float scale, float rotationYDegrees, string name)
    {
        if (!ResourceLoader.Exists(path)) return null;
        var packed = GD.Load<PackedScene>(path);
        if (packed is null) return null;
        var node = packed.Instantiate<Node3D>();
        node.Name = name;
        node.Position = position;
        node.RotationDegrees = new Vector3(0, rotationYDegrees, 0);
        node.Scale = Vector3.One * scale;
        parent.AddChild(node);
        ArtMaterials.ApplyVertexColors(node);
        return node;
    }

    public static StandardMaterial3D Flat(Color color, float roughness = 0.8f)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = roughness,
            Metallic = 0f
        };
    }

    public static Camera3D CreateTitleCamera(Node3D parent)
    {
        var camera = new Camera3D
        {
            Name = "TitleCamera",
            Current = true,
            Fov = 46f,
            Near = 0.1f,
            Far = 120f
        };
        parent.AddChild(camera);
        camera.LookAtFromPosition(new Vector3(-2.5f, 8.2f, 18.5f), new Vector3(-1.0f, 1.2f, 0f), Vector3.Up);
        return camera;
    }
}
