using Godot;
using Webwild.Core;

namespace Webwild.Game;

public sealed record SceneValidationCheck(string Name, bool Pass, string Detail);

/// <summary>
/// Contract between the authored Godot world and the engine-free simulation.
/// The scene is authoritative for presentation; <see cref="WorldLayout"/> is
/// authoritative for gameplay coordinates. Every startup validates that the two
/// still agree and that mandatory collision, scenes and model resources exist.
/// </summary>
public static class SceneContentValidator
{
    public const float PositionTolerance = 0.02f;

    private static readonly (string Name, string Path)[] Landmarks =
    {
        ("HomeLeaf", "res://scenes/world/landmarks/HomeLeaf.tscn"),
        ("TwinTwigs", "res://scenes/world/landmarks/TwinTwigs.tscn"),
        ("PebbleGap", "res://scenes/world/landmarks/PebbleGap.tscn"),
        ("FlowerLane", "res://scenes/world/landmarks/FlowerLane.tscn"),
        ("LeafNursery", "res://scenes/world/landmarks/LeafNursery.tscn"),
        ("ClimbableStem", "res://scenes/world/landmarks/ClimbableStem.tscn"),
        ("FallenLog", "res://scenes/world/landmarks/FallenLog.tscn"),
        ("WindEdge", "res://scenes/world/landmarks/WindEdge.tscn")
    };

    private static readonly (string Name, string Path)[] WorldGroups =
    {
        ("Environment", "res://scenes/world/GardenEnvironment.tscn"),
        ("Terrain", "res://scenes/world/GardenTerrain.tscn"),
        ("Paths", "res://scenes/world/GardenPaths.tscn"),
        ("Landmarks", "res://scenes/world/GardenLandmarks.tscn"),
        ("Flora", "res://scenes/world/GardenFlora.tscn"),
        ("Boundaries", "res://scenes/world/GardenBoundaries.tscn")
    };

    private static readonly (string Name, string Path)[] ReedwaterGroups =
    {
        ("Environment", "res://scenes/world/regions/ReedwaterBank/ReedwaterEnvironment.tscn"),
        ("Terrain", "res://scenes/world/regions/ReedwaterBank/ReedwaterTerrain.tscn"),
        ("Paths", "res://scenes/world/regions/ReedwaterBank/ReedwaterPaths.tscn"),
        ("Landmarks", "res://scenes/world/regions/ReedwaterBank/ReedwaterLandmarks.tscn"),
        ("Flora", "res://scenes/world/regions/ReedwaterBank/ReedwaterFlora.tscn"),
        ("Boundaries", "res://scenes/world/regions/ReedwaterBank/ReedwaterBoundaries.tscn")
    };

    private static readonly (string Name, string Path)[] ReedwaterLandmarks =
    {
        ("StartBank", "res://scenes/world/regions/ReedwaterBank/landmarks/StartBank.tscn"),
        ("IslandA", "res://scenes/world/regions/ReedwaterBank/landmarks/IslandA.tscn"),
        ("IslandB", "res://scenes/world/regions/ReedwaterBank/landmarks/IslandB.tscn"),
        ("FarBank", "res://scenes/world/regions/ReedwaterBank/landmarks/FarBank.tscn"),
        ("Outpost", "res://scenes/world/regions/ReedwaterBank/landmarks/Outpost.tscn")
    };

    private static readonly string[] BridgeParts =
    {
        "BarkPlankA", "BarkPlankB", "BarkPlankC",
        "SilkCinchKnot", "SilkDoubleHitch", "WoodenEyelet", "ForkedRailFixture",
        "SilkWrappedSupport", "SilkCrossLashing", "BraidedSilkSegment", "WovenSilkPanel"
    };

    public static IReadOnlyList<SceneValidationCheck> Validate(Node root)
    {
        var checks = new List<SceneValidationCheck>();
        void Check(string name, bool pass, string detail = "") =>
            checks.Add(new SceneValidationCheck(name, pass, detail));

        var scenePath = root.SceneFilePath ?? "";
        Check("clearing-scene",
            root is GardenClearing && scenePath.EndsWith("scenes/world/GardenClearing.tscn", StringComparison.Ordinal),
            $"scene={scenePath}");

        var world = root.GetNodeOrNull<Node3D>("World");
        Check("world-scene-authored",
            world is not null && (world.SceneFilePath ?? "").EndsWith("scenes/world/GardenWorld.tscn", StringComparison.Ordinal),
            world is null ? "missing World node" : $"scene={world.SceneFilePath}");

        var groupFailures = new List<string>();
        foreach (var (name, path) in WorldGroups)
        {
            var group = world?.GetNodeOrNull<Node3D>(name);
            if (group is null || !(group.SceneFilePath ?? "").EndsWith(Path.GetFileName(path), StringComparison.Ordinal))
                groupFailures.Add($"{name}={(group?.SceneFilePath ?? "missing")}");
        }
        Check("world-group-instances", groupFailures.Count == 0, string.Join(", ", groupFailures));

        var landmarks = world?.GetNodeOrNull<Node3D>("Landmarks");
        var landmarkChildren = landmarks?.GetChildren().OfType<Node3D>().ToList() ?? new List<Node3D>();
        var landmarkFailures = new List<string>();
        if (landmarkChildren.Count != Landmarks.Length)
            landmarkFailures.Add($"count={landmarkChildren.Count}");
        if (landmarkChildren.Select(child => child.Name.ToString()).Distinct().Count() != landmarkChildren.Count)
            landmarkFailures.Add("duplicate instance names");
        foreach (var (name, path) in Landmarks)
        {
            var landmark = landmarks?.GetNodeOrNull<Node3D>(name);
            if (landmark is null || !(landmark.SceneFilePath ?? "").EndsWith(Path.GetFileName(path), StringComparison.Ordinal))
                landmarkFailures.Add($"{name}={(landmark?.SceneFilePath ?? "missing")}");
        }
        Check("landmark-scenes", landmarkFailures.Count == 0, string.Join(", ", landmarkFailures));

        var anchorsRoot = root.GetNodeOrNull<Node3D>("Anchors");
        var anchorNodes = anchorsRoot?.GetChildren().OfType<AnchorNode>().ToList() ?? new List<AnchorNode>();
        var definitions = WorldLayout.CreateAnchors().ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        var ids = anchorNodes.Select(anchor => anchor.AnchorId).ToList();
        Check("anchor-count", anchorNodes.Count == 12 && definitions.Count == 12,
            $"scene={anchorNodes.Count} simulation={definitions.Count}");
        Check("anchor-ids-unique",
            ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Count() == ids.Count &&
            ids.All(definitions.ContainsKey),
            string.Join(",", ids));

        var transformFailures = new List<string>();
        foreach (var anchor in anchorNodes)
        {
            if (!definitions.TryGetValue(anchor.AnchorId, out var definition)) continue;
            if (!Near(anchor.GlobalPosition, definition.Position))
                transformFailures.Add($"{anchor.AnchorId}={anchor.GlobalPosition} expected {ToVector3(definition.Position)}");
        }
        Check("anchor-transforms-match-simulation", transformFailures.Count == 0,
            string.Join("; ", transformFailures));

        var markerFailures = new List<string>();
        var markerCount = 0;
        foreach (var definition in definitions.Values)
        {
            var matches = landmarkChildren
                .SelectMany(child => child.GetChildren().OfType<Marker3D>())
                .Where(marker => marker.Name.ToString() == $"Anchor_{definition.Id}")
                .ToList();
            markerCount += matches.Count;
            if (matches.Count != 1)
            {
                markerFailures.Add($"{definition.Id} markers={matches.Count}");
                continue;
            }
            var marker = matches[0];
            if (!Near(marker.GlobalPosition, definition.Position))
                markerFailures.Add($"{definition.Id} marker={marker.GlobalPosition} expected {ToVector3(definition.Position)}");
            var socket = anchorNodes.FirstOrDefault(anchor => anchor.AnchorId == definition.Id);
            if (socket is null || marker.GlobalPosition.DistanceTo(socket.GlobalPosition) > PositionTolerance)
                markerFailures.Add($"{definition.Id} marker/socket mismatch");
        }
        Check("landmark-anchor-markers", markerFailures.Count == 0 && markerCount == 12,
            $"{markerCount} markers; {string.Join("; ", markerFailures)}");

        Check("nursery-marker",
            MarkerMatches(landmarks, "LeafNursery", "NurseryPoint", WorldLayout.NurseryNodePosition),
            MarkerDetail(landmarks, "LeafNursery", "NurseryPoint"));
        Check("climb-markers",
            MarkerMatches(landmarks, "ClimbableStem", "ClimbBase", WorldLayout.ClimbBase) &&
            MarkerMatches(landmarks, "ClimbableStem", "ClimbTop", WorldLayout.ClimbTop),
            $"{MarkerDetail(landmarks, "ClimbableStem", "ClimbBase")}; {MarkerDetail(landmarks, "ClimbableStem", "ClimbTop")}");
        Check("bridge-bank-markers",
            MarkerMatches(landmarks, "PebbleGap", "BridgeGroundWest", WorldLayout.GapWestGround) &&
            MarkerMatches(landmarks, "PebbleGap", "BridgeGroundEast", WorldLayout.GapEastGround),
            $"{MarkerDetail(landmarks, "PebbleGap", "BridgeGroundWest")}; {MarkerDetail(landmarks, "PebbleGap", "BridgeGroundEast")}");

        var terrain = world?.GetNodeOrNull<Node3D>("Terrain");
        var west = terrain?.GetNodeOrNull<StaticBody3D>("WestMeadow");
        var east = terrain?.GetNodeOrNull<StaticBody3D>("EastMeadow");
        var westBox = BoxShapeOf(west);
        var eastBox = BoxShapeOf(east);
        var ravineOk = westBox is not null && eastBox is not null &&
                       Math.Abs(west!.GlobalPosition.X + westBox.Size.X * 0.5f + WorldLayout.RavineHalfWidth) <= PositionTolerance &&
                       Math.Abs(east!.GlobalPosition.X - eastBox.Size.X * 0.5f - WorldLayout.RavineHalfWidth) <= PositionTolerance;
        Check("ravine-geometry", ravineOk,
            $"westEdge={(westBox is null ? "?" : west!.GlobalPosition.X + westBox.Size.X * 0.5f):0.00} " +
            $"eastEdge={(eastBox is null ? "?" : east!.GlobalPosition.X - eastBox.Size.X * 0.5f):0.00}");

        Check("ground-collision-contract",
            west is not null && east is not null &&
            west.CollisionLayer == 1 && west.CollisionMask == 0 &&
            east.CollisionLayer == 1 && east.CollisionMask == 0 &&
            HasShape(west) && HasShape(east),
            $"west L{west?.CollisionLayer}/M{west?.CollisionMask} east L{east?.CollisionLayer}/M{east?.CollisionMask}");

        var boundaries = world?.GetNodeOrNull<Node3D>("Boundaries");
        var hedgeBodies = boundaries?.GetChildren().OfType<StaticBody3D>().ToList() ?? new List<StaticBody3D>();
        Check("boundary-collision",
            hedgeBodies.Count == 4 && hedgeBodies.All(body =>
                body.CollisionLayer == 1 && body.CollisionMask == 0 && HasShape(body)),
            $"bodies={hedgeBodies.Count}");

        Check("anchor-collision-contract",
            anchorNodes.Count == 12 && anchorNodes.All(anchor =>
                anchor.CollisionLayer == 4 && anchor.CollisionMask == 0 &&
                anchor.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape is SphereShape3D),
            $"anchors={anchorNodes.Count}");
        Check("anchor-marker-models",
            anchorNodes.All(anchor =>
                anchor.GetNodeOrNull<Node3D>("MarkerRing")?.SceneFilePath?.EndsWith("web_anchor_ring.glb", StringComparison.Ordinal) == true &&
                HasBakedModelMaterials(anchor.GetNode("MarkerRing"))),
            "visible socket rings authored in AnchorSocket.tscn");

        var player = root.GetNodeOrNull<SpiderPlayer>("Player");
        var spawn = ToVector3(WorldLayout.PlayerSpawn);
        var spawnOk = player is not null &&
                      Math.Abs(player.GlobalPosition.X - spawn.X) <= PositionTolerance &&
                      Math.Abs(player.GlobalPosition.Z - spawn.Z) <= PositionTolerance &&
                      Math.Abs(player.GlobalPosition.Y - spawn.Y) <= 0.6f;
        Check("player-contract",
            spawnOk && player!.CollisionLayer == 2 && player.CollisionMask == 1,
            player is null ? "missing Player" : $"pos={player.GlobalPosition} layer={player.CollisionLayer} mask={player.CollisionMask}");

        Check("runtime-roots",
            root.GetNodeOrNull<Node3D>("Webs") is not null &&
            root.GetNodeOrNull<Node3D>("Insects") is not null &&
            root.GetNodeOrNull<Node3D>("Workers") is not null &&
            root.GetNodeOrNull<Node3D>("WebPreview") is not null &&
            root.GetNodeOrNull<Node>("UI") is not null,
            "Webs, Insects, Workers, WebPreview and UI");

        var environment = world?.GetNodeOrNull<Node3D>("Environment");
        Check("environment-authored",
            environment?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment")?.Environment is not null &&
            environment.GetNodeOrNull<DirectionalLight3D>("Sun") is { ShadowEnabled: true } &&
            environment.GetNodeOrNull<OmniLight3D>("NurseryFill") is not null,
            "WorldEnvironment, shadowed Sun and NurseryFill");

        var modelFailures = new List<string>();
        var modelCount = 0;
        var meshCount = 0;
        foreach (var node in Descendants(world))
        {
            var path = node.SceneFilePath ?? "";
            if (!path.StartsWith("res://art/models/", StringComparison.Ordinal)) continue;
            modelCount++;
            if (!ResourceLoader.Exists(path))
                modelFailures.Add($"{node.Name}: resource missing");
            if (!HasBakedModelMaterials(node))
                modelFailures.Add($"{node.Name}: mesh/material missing");
        }
        foreach (var mesh in Descendants(world).OfType<MeshInstance3D>())
        {
            meshCount++;
            if (mesh.Mesh is null) modelFailures.Add($"{mesh.Name}: null mesh");
        }
        var vertexMaterial = GD.Load<Material>("res://art/materials/vertex_color_material.tres");
        Check("vertex-material-resource", vertexMaterial is not null,
            vertexMaterial is null ? "resource did not load" : vertexMaterial.GetType().Name);
        Check("model-resources", modelFailures.Count == 0,
            $"models={modelCount} meshes={meshCount} {string.Join("; ", modelFailures.Take(6))}");
        Check("model-material-coverage", modelCount > 0 && modelFailures.Count == 0,
            $"vertex-colour material resolved on {modelCount} model instances");

        Check("no-procedural-builder",
            !Godot.FileAccess.FileExists("res://scripts/world/GardenScenery.cs") &&
            Type.GetType("Webwild.Game.GardenScenery, Webwild") is null &&
            root.FindChildren("GardenScenery", "", true, false).Count == 0,
            "GardenScenery type and file are gone");

        Check("scene-loads",
            GD.Load<PackedScene>("res://scenes/world/GardenClearing.tscn") is not null &&
            GD.Load<PackedScene>("res://scenes/boot/TitleBackdrop.tscn") is not null,
            "GardenClearing and TitleBackdrop load");

        Check("garden-passage-authored",
            root.GetNodeOrNull<Node3D>("Passage") is { } passage &&
            (passage.SceneFilePath ?? "").EndsWith("GardenPassage.tscn", StringComparison.Ordinal),
            $"passage={root.GetNodeOrNull<Node3D>("Passage")?.SceneFilePath ?? "missing"}");
        Check("garden-travel-markers",
            MarkerMatches(root.GetNodeOrNull<Node3D>("TravelPoints"), "", "ToReedwaterBank", WorldLayout.TravelPoint) &&
            MarkerMatches(root.GetNodeOrNull<Node3D>("TravelPoints"), "", "FromReedwaterBank", WorldLayout.TravelArrivalPoint),
            $"{MarkerDetail(root.GetNodeOrNull<Node3D>("TravelPoints"), "", "ToReedwaterBank")}; " +
            $"{MarkerDetail(root.GetNodeOrNull<Node3D>("TravelPoints"), "", "FromReedwaterBank")}");
        Check("region-registry",
            RegionIds.IsKnown(RegionIds.GardenClearing) && RegionIds.IsKnown(RegionIds.ReedwaterBank) &&
            RegionCatalog.All.Select(region => region.Id).Distinct(StringComparer.Ordinal).Count() ==
            RegionCatalog.All.Count,
            string.Join(",", RegionCatalog.All.Select(region => region.Id)));

        ValidateBridgeAssets(Check);
        return checks;
    }

    /// <summary>
    /// Region-specific scene contract for the Reedwater Bank root. It deliberately
    /// runs the same shape of checks as the garden: authored group instances,
    /// landmark scenes, anchors and markers, bridge banks, water collision,
    /// required materials and no procedural fallback.
    /// </summary>
    public static IReadOnlyList<SceneValidationCheck> ValidateReedwater(Node root)
    {
        var checks = new List<SceneValidationCheck>();
        void Check(string name, bool pass, string detail = "") =>
            checks.Add(new SceneValidationCheck(name, pass, detail));

        var scenePath = root.SceneFilePath ?? "";
        Check("rw-bank-scene",
            root is ReedwaterRegion && scenePath.EndsWith("ReedwaterBank.tscn", StringComparison.Ordinal),
            $"scene={scenePath}");

        var world = root.GetNodeOrNull<Node3D>("World");
        Check("rw-world-scene-authored",
            world is not null && (world.SceneFilePath ?? "").EndsWith("ReedwaterWorld.tscn", StringComparison.Ordinal),
            world is null ? "missing World node" : $"scene={world.SceneFilePath}");

        var groupFailures = new List<string>();
        foreach (var (name, path) in ReedwaterGroups)
        {
            var group = world?.GetNodeOrNull<Node3D>(name);
            if (group is null || !(group.SceneFilePath ?? "").EndsWith(Path.GetFileName(path), StringComparison.Ordinal))
                groupFailures.Add($"{name}={(group?.SceneFilePath ?? "missing")}");
        }
        Check("rw-world-group-instances", groupFailures.Count == 0, string.Join(", ", groupFailures));

        var region = RegionCatalog.GetRequired(RegionIds.ReedwaterBank);
        var landmarks = world?.GetNodeOrNull<Node3D>("Landmarks");
        var landmarkChildren = landmarks?.GetChildren().OfType<Node3D>().ToList() ?? new List<Node3D>();
        var landmarkFailures = new List<string>();
        if (landmarkChildren.Count != ReedwaterLandmarks.Length)
            landmarkFailures.Add($"count={landmarkChildren.Count}");
        foreach (var (name, path) in ReedwaterLandmarks)
        {
            var landmark = landmarks?.GetNodeOrNull<Node3D>(name);
            if (landmark is null || !(landmark.SceneFilePath ?? "").EndsWith(Path.GetFileName(path), StringComparison.Ordinal))
                landmarkFailures.Add($"{name}={(landmark?.SceneFilePath ?? "missing")}");
        }
        Check("rw-landmark-scenes", landmarkFailures.Count == 0, string.Join(", ", landmarkFailures));

        var anchorsRoot = root.GetNodeOrNull<Node3D>("Anchors");
        var anchorNodes = anchorsRoot?.GetChildren().OfType<AnchorNode>().ToList() ?? new List<AnchorNode>();
        var definitions = region.Anchors.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        var ids = anchorNodes.Select(anchor => anchor.AnchorId).ToList();
        Check("rw-anchor-count", anchorNodes.Count == 10 && definitions.Count == 10,
            $"scene={anchorNodes.Count} simulation={definitions.Count}");
        Check("rw-anchor-ids-unique",
            ids.All(id => !string.IsNullOrWhiteSpace(id)) &&
            ids.Distinct(StringComparer.Ordinal).Count() == ids.Count &&
            ids.All(definitions.ContainsKey),
            string.Join(",", ids));

        var transformFailures = new List<string>();
        foreach (var anchor in anchorNodes)
        {
            if (!definitions.TryGetValue(anchor.AnchorId, out var definition)) continue;
            if (!Near(anchor.GlobalPosition, definition.Position))
                transformFailures.Add($"{anchor.AnchorId}={anchor.GlobalPosition} expected {ToVector3(definition.Position)}");
        }
        Check("rw-anchor-transforms-match-simulation", transformFailures.Count == 0,
            string.Join("; ", transformFailures));

        var markerFailures = new List<string>();
        var markerCount = 0;
        foreach (var definition in definitions.Values)
        {
            var matches = landmarkChildren
                .SelectMany(child => child.GetChildren().OfType<Marker3D>())
                .Where(marker => marker.Name.ToString() == $"Anchor_{definition.Id}")
                .ToList();
            markerCount += matches.Count;
            if (matches.Count != 1)
            {
                markerFailures.Add($"{definition.Id} markers={matches.Count}");
                continue;
            }
            if (!Near(matches[0].GlobalPosition, definition.Position))
                markerFailures.Add($"{definition.Id} marker={matches[0].GlobalPosition} expected {ToVector3(definition.Position)}");
        }
        Check("rw-landmark-anchor-markers", markerFailures.Count == 0 && markerCount == 10,
            $"{markerCount} markers; {string.Join("; ", markerFailures)}");

        Check("rw-nursery-marker",
            MarkerMatches(landmarks, "Outpost", "NurseryPoint", ReedwaterLayout.NurseryPosition),
            MarkerDetail(landmarks, "Outpost", "NurseryPoint"));
        Check("rw-first-destination-marker",
            MarkerMatches(landmarks, "IslandB", "FirstDestinationPoint", ReedwaterLayout.FirstDestinationPosition),
            MarkerDetail(landmarks, "IslandB", "FirstDestinationPoint"));
        Check("rw-far-bank-marker",
            MarkerMatches(landmarks, "FarBank", "FarBankPoint", ReedwaterLayout.FarBankPosition),
            MarkerDetail(landmarks, "FarBank", "FarBankPoint"));
        Check("rw-arrival-return-markers",
            MarkerMatches(root.GetNodeOrNull<Node3D>("TravelPoints"), "", "ArrivalPoint", ReedwaterLayout.ArrivalPoint) &&
            MarkerMatches(root.GetNodeOrNull<Node3D>("TravelPoints"), "", "TravelPoint", ReedwaterLayout.TravelPoint),
            $"{MarkerDetail(root.GetNodeOrNull<Node3D>("TravelPoints"), "", "ArrivalPoint")}; " +
            $"{MarkerDetail(root.GetNodeOrNull<Node3D>("TravelPoints"), "", "TravelPoint")}");

        Check("rw-bridge-bank-markers",
            MarkerMatches(landmarks, "StartBank", "BridgeGround_rw_start_post", ReedwaterLayout.StartPostGround) &&
            MarkerMatches(landmarks, "IslandA", "BridgeGround_rw_island_a_post", ReedwaterLayout.IslandAPostGround) &&
            MarkerMatches(landmarks, "IslandB", "BridgeGround_rw_island_b_post", ReedwaterLayout.IslandBPostGround) &&
            MarkerMatches(landmarks, "FarBank", "BridgeGround_rw_far_post", ReedwaterLayout.FarPostGround),
            $"{MarkerDetail(landmarks, "StartBank", "BridgeGround_rw_start_post")}; " +
            $"{MarkerDetail(landmarks, "IslandA", "BridgeGround_rw_island_a_post")}; " +
            $"{MarkerDetail(landmarks, "IslandB", "BridgeGround_rw_island_b_post")}; " +
            $"{MarkerDetail(landmarks, "FarBank", "BridgeGround_rw_far_post")}");

        var terrain = world?.GetNodeOrNull<Node3D>("Terrain");
        var startBank = terrain?.GetNodeOrNull<StaticBody3D>("StartBank");
        var farBank = terrain?.GetNodeOrNull<StaticBody3D>("FarBank");
        var bankOk = startBank is not null && farBank is not null &&
                     startBank.CollisionLayer == 1 && startBank.CollisionMask == 0 &&
                     farBank.CollisionLayer == 1 && farBank.CollisionMask == 0 &&
                     HasShape(startBank) && HasShape(farBank);
        Check("rw-bank-collision", bankOk,
            $"start L{startBank?.CollisionLayer}/M{startBank?.CollisionMask} far L{farBank?.CollisionLayer}/M{farBank?.CollisionMask}");

        var waterAreas = terrain?.GetChildren().OfType<Area3D>()
            .Where(area => area.IsInGroup("water")).ToList() ?? new List<Area3D>();
        Check("rw-water-collision",
            waterAreas.Count == 4 && waterAreas.All(area =>
                area.CollisionLayer == 8 && area.CollisionMask == 2 &&
                area.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape is BoxShape3D),
            $"water areas={waterAreas.Count}");

        var solidBodies = Descendants(world).OfType<StaticBody3D>().ToList();
        Check("rw-solid-collision-contract",
            solidBodies.Count >= 8 && solidBodies.All(body =>
                body.CollisionLayer == 1 && body.CollisionMask == 0 && HasShape(body)),
            $"bodies={solidBodies.Count}");

        var modelFailures = new List<string>();
        var modelCount = 0;
        var meshCount = 0;
        foreach (var node in Descendants(world))
        {
            var path = node.SceneFilePath ?? "";
            if (!path.StartsWith("res://art/models/", StringComparison.Ordinal)) continue;
            modelCount++;
            if (!ResourceLoader.Exists(path))
                modelFailures.Add($"{node.Name}: resource missing");
            if (!HasBakedModelMaterials(node))
                modelFailures.Add($"{node.Name}: mesh/material missing");
        }
        foreach (var mesh in Descendants(world).OfType<MeshInstance3D>())
        {
            meshCount++;
            if (mesh.Mesh is null) modelFailures.Add($"{mesh.Name}: null mesh");
        }
        var reedwaterMaterial = GD.Load<Material>("res://art/materials/reedwater_vertex_color_material.tres");
        Check("rw-material-resource", reedwaterMaterial is not null,
            reedwaterMaterial is null ? "resource did not load" : reedwaterMaterial.ResourcePath);
        Check("rw-model-resources", modelFailures.Count == 0,
            $"models={modelCount} meshes={meshCount} {string.Join("; ", modelFailures.Take(6))}");
        Check("rw-insect-routes",
            region.InsectRoutes.Count >= 3 &&
            region.InsectRoutes.All(route => route.Region == RegionIds.ReedwaterBank) &&
            region.InsectRoutes.Select(route => route.Id).Distinct(StringComparer.Ordinal).Count() ==
            region.InsectRoutes.Count,
            string.Join(",", region.InsectRoutes.Select(route => route.Id)));
        Check("rw-no-procedural-scenery",
            !Godot.FileAccess.FileExists("res://scripts/world/ReedwaterScenery.cs") &&
            root.FindChildren("ReedwaterScenery", "", true, false).Count == 0,
            "Reedwater scenery is authored, not generated in C#");

        return checks;
    }

    private static void ValidateBridgeAssets(Action<string, bool, string> check)
    {
        var failures = new List<string>();
        var instances = new List<Node3D>();
        var meshInstances = 0;
        foreach (var part in BridgeParts)
        {
            var path = $"res://scenes/building/bridge/parts/{part}.tscn";
            var scene = GD.Load<PackedScene>(path);
            if (scene is null)
            {
                failures.Add($"{part}: scene missing");
                continue;
            }
            var instance = scene.Instantiate<Node3D>();
            instances.Add(instance);
            var meshes = Descendants(instance).OfType<MeshInstance3D>().ToList();
            meshInstances += meshes.Count;
            // Imported material_override properties can be dropped from exported
            // PCKs for nested GLB children. BridgeVisualAssembler applies the
            // authored vertex-colour material at runtime; here we prove the
            // wrapper still supplies real meshes and no collision.
            if (meshes.Count == 0 || meshes.Any(mesh => mesh.Mesh is null))
                failures.Add($"{part}: mesh missing");
            if (Descendants(instance).Any(node => node is CollisionShape3D or StaticBody3D or Area3D))
                failures.Add($"{part}: unexpected collision");
        }
        check("bridge-parts-exist", failures.Count == 0,
            $"{instances.Count}/{BridgeParts.Length} parts; meshes={meshInstances}; {string.Join("; ", failures.Take(6))}");
        foreach (var instance in instances) instance.Free();

        var gallery = GD.Load<PackedScene>("res://scenes/building/bridge/BridgeAssetGallery.tscn");
        var galleryInstances = gallery?.Instantiate<Node3D>();
        var galleryMeshes = galleryInstances is null
            ? 0
            : Descendants(galleryInstances).OfType<MeshInstance3D>().Count();
        check("bridge-asset-gallery",
            galleryInstances is not null && galleryInstances.GetChildCount() == BridgeParts.Length &&
            galleryMeshes > 0,
            galleryInstances is null
                ? "gallery scene missing"
                : $"children={galleryInstances.GetChildCount()} meshes={galleryMeshes}");
        galleryInstances?.Free();

        var material = GD.Load<StandardMaterial3D>("res://art/materials/vertex_color_material.tres");
        check("bridge-vertex-colour-material",
            material is not null && material.VertexColorUseAsAlbedo,
            material is null ? "resource missing" : $"path={material.ResourcePath}");
        check("bridge-no-legacy-ribbon",
            !Godot.FileAccess.FileExists("res://scripts/visual/WebMeshFactory.cs") ||
            !Godot.FileAccess.GetFileAsString("res://scripts/visual/WebMeshFactory.cs")
                .Contains("BuildBridge(", StringComparison.Ordinal),
            "legacy flat ribbon builder is gone");
    }

    public static void ValidateReedwaterOrThrow(Node root)
    {
        var failures = ValidateReedwater(root).Where(check => !check.Pass).ToList();
        if (failures.Count == 0) return;
        var lines = string.Join("\n", failures.Select(check => $" - {check.Name}: {check.Detail}"));
        throw new InvalidOperationException($"Authored Reedwater Bank scene validation failed:\n{lines}");
    }

    public static void ValidateOrThrow(Node root)
    {
        var failures = Validate(root).Where(check => !check.Pass).ToList();
        if (failures.Count == 0) return;
        var lines = string.Join("\n", failures.Select(check => $" - {check.Name}: {check.Detail}"));
        throw new InvalidOperationException($"Authored garden scene validation failed:\n{lines}");
    }

    private static BoxShape3D? BoxShapeOf(StaticBody3D? body) =>
        body?.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape as BoxShape3D;

    private static bool HasShape(StaticBody3D body) =>
        body.GetNodeOrNull<CollisionShape3D>("CollisionShape3D")?.Shape is not null;

    private static bool HasBakedModelMaterials(Node model)
    {
        var meshes = Descendants(model).OfType<MeshInstance3D>().ToList();
        return meshes.Count > 0 && meshes.All(mesh => mesh.Mesh is not null && mesh.MaterialOverride is not null);
    }

    private static IEnumerable<Node> Descendants(Node? node)
    {
        if (node is null) yield break;
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static bool MarkerMatches(Node3D? root, string owner, string marker, Vec3 expected)
    {
        var node = MarkerNode(root, owner, marker);
        return node is not null && Near(node.GlobalPosition, expected);
    }

    private static string MarkerDetail(Node3D? root, string owner, string marker)
    {
        var node = MarkerNode(root, owner, marker);
        var path = string.IsNullOrWhiteSpace(owner) ? marker : $"{owner}/{marker}";
        return node is null ? $"{path}=missing" : $"{path}={node.GlobalPosition}";
    }

    private static Marker3D? MarkerNode(Node3D? root, string owner, string marker) =>
        string.IsNullOrWhiteSpace(owner)
            ? root?.GetNodeOrNull<Marker3D>(marker)
            : root?.GetNodeOrNull<Marker3D>($"{owner}/{marker}");

    private static bool Near(Vector3 actual, Vec3 expected) =>
        actual.DistanceTo(ToVector3(expected)) <= PositionTolerance;

    private static Vector3 ToVector3(Vec3 value) => new((float)value.X, (float)value.Y, (float)value.Z);
}
