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

        return checks;
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

    private static bool MarkerMatches(Node3D? landmarks, string landmark, string marker, Vec3 expected)
    {
        var node = landmarks?.GetNodeOrNull<Marker3D>($"{landmark}/{marker}");
        return node is not null && Near(node.GlobalPosition, expected);
    }

    private static string MarkerDetail(Node3D? landmarks, string landmark, string marker)
    {
        var node = landmarks?.GetNodeOrNull<Marker3D>($"{landmark}/{marker}");
        return node is null ? $"{landmark}/{marker}=missing" : $"{landmark}/{marker}={node.GlobalPosition}";
    }

    private static bool Near(Vector3 actual, Vec3 expected) =>
        actual.DistanceTo(ToVector3(expected)) <= PositionTolerance;

    private static Vector3 ToVector3(Vec3 value) => new((float)value.X, (float)value.Y, (float)value.Z);
}
