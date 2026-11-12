using System.Globalization;
using System.Text.RegularExpressions;
using Webwild.Core;
using Xunit;

namespace Webwild.Core.Tests;

/// <summary>
/// Deterministic scene/content contract tests. These read the real authored
/// .tscn files and compare anchor coordinates with the engine-free simulation
/// definition, so the scene cannot silently drift from WorldLayout.
/// </summary>
public class SceneAuthoringTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scenes", "world", "GardenClearing.tscn")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the Webwild repository root.");
    }

    private sealed record SceneNode(string Name, string Type, string? Parent, string Header)
    {
        public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);
    }

    private static List<SceneNode> ParseScene(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        Assert.True(File.Exists(path), $"Authored scene missing: {relativePath}");
        var nodes = new List<SceneNode>();
        SceneNode? current = null;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[node ", StringComparison.Ordinal))
            {
                current = new SceneNode(
                    Attribute(line, "name"),
                    Attribute(line, "type"),
                    line.Contains("parent=\"", StringComparison.Ordinal) ? Attribute(line, "parent") : null,
                    line);
                nodes.Add(current);
                continue;
            }
            if (line.StartsWith('['))
            {
                current = null;
                continue;
            }
            if (current is null || !line.Contains('=')) continue;
            var split = line.IndexOf('=');
            var value = line[(split + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            current.Properties[line[..split].Trim()] = value;
        }
        return nodes;
    }

    private static string Attribute(string header, string name)
    {
        var match = Regex.Match(header, $"{Regex.Escape(name)}=\"([^\"]*)\"");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static (double X, double Y, double Z) ParseVector(string value)
    {
        var match = Regex.Match(value, @"Vector3\(([^)]*)\)");
        Assert.True(match.Success, $"Expected Vector3, got '{value}'");
        var parts = match.Groups[1].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal(3, parts.Length);
        return (parts[0], parts[1], parts[2]);
    }

    private static (double X, double Y, double Z) Position(SceneNode node) =>
        node.Properties.TryGetValue("position", out var value) ? ParseVector(value) : (0, 0, 0);

    private static string ReadScene(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath));

    [Fact]
    public void AuthoredAnchorSockets_AreUnique_AndMatchSimulationCoordinates()
    {
        var blocks = ParseScene("scenes/world/GardenClearing.tscn")
            .Where(node => node.Parent == "Anchors")
            .ToList();

        Assert.Equal(12, blocks.Count);
        var ids = blocks.Select(block => block.Properties["AnchorId"]).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        var definitions = WorldLayout.CreateAnchors().ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        Assert.Equal(definitions.Keys.OrderBy(id => id), ids.OrderBy(id => id));
        Assert.All(blocks, block =>
        {
            var id = block.Properties["AnchorId"];
            var expected = definitions[id].Position;
            var actual = Position(block);
            Assert.Equal(expected.X, actual.X, 3);
            Assert.Equal(expected.Y, actual.Y, 3);
            Assert.Equal(expected.Z, actual.Z, 3);
        });
    }

    [Fact]
    public void LandmarkAnchorMarkers_MatchSimulationCoordinates()
    {
        var landmarks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HomeLeaf"] = "scenes/world/landmarks/HomeLeaf.tscn",
            ["TwinTwigs"] = "scenes/world/landmarks/TwinTwigs.tscn",
            ["PebbleGap"] = "scenes/world/landmarks/PebbleGap.tscn",
            ["FlowerLane"] = "scenes/world/landmarks/FlowerLane.tscn",
            ["LeafNursery"] = "scenes/world/landmarks/LeafNursery.tscn",
            ["ClimbableStem"] = "scenes/world/landmarks/ClimbableStem.tscn",
            ["FallenLog"] = "scenes/world/landmarks/FallenLog.tscn",
            ["WindEdge"] = "scenes/world/landmarks/WindEdge.tscn"
        };

        var found = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.Ordinal);
        foreach (var (name, relativePath) in landmarks)
        {
            var nodes = ParseScene(relativePath);
            var root = Assert.Single(nodes, node => node.Parent is null);
            var origin = Position(root);
            foreach (var marker in nodes.Where(node => node.Type == "Marker3D" && node.Name.StartsWith("Anchor_", StringComparison.Ordinal)))
            {
                var local = Position(marker);
                found[marker.Name["Anchor_".Length..]] = (origin.X + local.X, origin.Y + local.Y, origin.Z + local.Z);
            }
        }

        var definitions = WorldLayout.CreateAnchors();
        Assert.Equal(definitions.Count, found.Count);
        Assert.All(definitions, definition =>
        {
            Assert.True(found.TryGetValue(definition.Id, out var actual), $"Missing marker for {definition.Id}");
            Assert.Equal(definition.Position.X, actual.X, 3);
            Assert.Equal(definition.Position.Y, actual.Y, 3);
            Assert.Equal(definition.Position.Z, actual.Z, 3);
        });
        Assert.Equal(landmarks.Count, Directory.GetFiles(Path.Combine(Root, "scenes/world/landmarks"), "*.tscn").Length);
    }

    [Fact]
    public void NurseryClimbAndBridgeMarkers_MatchSimulationCoordinates()
    {
        AssertLocalMarker("scenes/world/landmarks/LeafNursery.tscn", "NurseryPoint", WorldLayout.NurseryNodePosition);
        AssertLocalMarker("scenes/world/landmarks/ClimbableStem.tscn", "ClimbBase", WorldLayout.ClimbBase);
        AssertLocalMarker("scenes/world/landmarks/ClimbableStem.tscn", "ClimbTop", WorldLayout.ClimbTop);
        AssertLocalMarker("scenes/world/landmarks/PebbleGap.tscn", "BridgeGroundWest", WorldLayout.GapWestGround);
        AssertLocalMarker("scenes/world/landmarks/PebbleGap.tscn", "BridgeGroundEast", WorldLayout.GapEastGround);
    }

    private static void AssertLocalMarker(string scenePath, string markerName, Vec3 expected)
    {
        var nodes = ParseScene(scenePath);
        var root = Assert.Single(nodes, node => node.Parent is null);
        var marker = Assert.Single(nodes, node => node.Name == markerName && node.Type == "Marker3D");
        var origin = Position(root);
        var local = Position(marker);
        Assert.Equal(expected.X, origin.X + local.X, 3);
        Assert.Equal(expected.Y, origin.Y + local.Y, 3);
        Assert.Equal(expected.Z, origin.Z + local.Z, 3);
    }

    [Fact]
    public void GardenClearing_IsComposedFromAuthoredSceneInstances()
    {
        var clearing = ParseScene("scenes/world/GardenClearing.tscn");
        var clearingText = ReadScene("scenes/world/GardenClearing.tscn");
        Assert.Single(clearing, node => node.Parent is null);
        Assert.Contains("path=\"res://scripts/world/GardenClearing.cs\"", clearingText);

        foreach (var required in new[] { "World", "Anchors", "Player", "Webs", "Insects", "Workers", "WebPreview", "UI" })
            Assert.Contains(clearing, node => node.Name == required);

        Assert.Contains("path=\"res://scenes/world/GardenWorld.tscn\"", clearingText);
        Assert.Contains("path=\"res://scenes/actors/spider/SpiderPlayer.tscn\"", clearingText);

        var worldText = ReadScene("scenes/world/GardenWorld.tscn");
        foreach (var group in new[] { "Environment", "Terrain", "Paths", "Landmarks", "Flora", "Boundaries" })
        {
            Assert.Contains($"path=\"res://scenes/world/Garden{group}.tscn\"", worldText);
            Assert.Contains(ParseScene("scenes/world/GardenWorld.tscn"), node => node.Name == group);
        }
    }

    [Fact]
    public void AuthoredScenes_HaveEditableCollisionShapes()
    {
        var terrain = ParseScene("scenes/world/GardenTerrain.tscn");
        foreach (var ground in new[] { "WestMeadow", "EastMeadow" })
        {
            var body = Assert.Single(terrain, node => node.Name == ground && node.Type == "StaticBody3D");
            Assert.Equal("1", body.Properties["collision_layer"]);
            Assert.Equal("0", body.Properties["collision_mask"]);
            Assert.Contains(terrain, node => node.Parent == ground && node.Type == "CollisionShape3D" &&
                node.Properties.ContainsKey("shape"));
        }

        var boundaries = ParseScene("scenes/world/GardenBoundaries.tscn");
        var walls = boundaries.Where(node => node.Type == "StaticBody3D").ToList();
        Assert.Equal(4, walls.Count);
        Assert.All(walls, wall =>
        {
            Assert.Equal("1", wall.Properties["collision_layer"]);
            Assert.Equal("0", wall.Properties["collision_mask"]);
            Assert.Contains(boundaries, node => node.Parent == wall.Name && node.Type == "CollisionShape3D");
        });

        var socket = ParseScene("scenes/building/webs/AnchorSocket.tscn");
        var anchor = Assert.Single(socket, node => node.Type == "Area3D");
        Assert.Equal("4", anchor.Properties["collision_layer"]);
        Assert.Equal("0", anchor.Properties["collision_mask"]);
        Assert.Contains(socket, node => node.Name == "CollisionShape3D" && node.Type == "CollisionShape3D");
    }

    [Fact]
    public void ReedwaterAnchors_AreUnique_AndMatchSimulationCoordinates()
    {
        var blocks = ParseScene("scenes/world/regions/ReedwaterBank/ReedwaterBank.tscn")
            .Where(node => node.Parent == "Anchors")
            .ToList();
        Assert.Equal(10, blocks.Count);
        var ids = blocks.Select(block => block.Properties["AnchorId"]).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        var definitions = ReedwaterLayout.CreateAnchors()
            .ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        Assert.Equal(definitions.Keys.OrderBy(id => id), ids.OrderBy(id => id));
        Assert.All(blocks, block =>
        {
            var id = block.Properties["AnchorId"];
            var expected = definitions[id].Position;
            var actual = Position(block);
            Assert.Equal(expected.X, actual.X, 3);
            Assert.Equal(expected.Y, actual.Y, 3);
            Assert.Equal(expected.Z, actual.Z, 3);
        });
    }

    [Fact]
    public void ReedwaterMarkersAndBridgeBanks_MatchSimulationCoordinates()
    {
        var landmarks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["StartBank"] = "scenes/world/regions/ReedwaterBank/landmarks/StartBank.tscn",
            ["IslandA"] = "scenes/world/regions/ReedwaterBank/landmarks/IslandA.tscn",
            ["IslandB"] = "scenes/world/regions/ReedwaterBank/landmarks/IslandB.tscn",
            ["FarBank"] = "scenes/world/regions/ReedwaterBank/landmarks/FarBank.tscn",
            ["Outpost"] = "scenes/world/regions/ReedwaterBank/landmarks/Outpost.tscn"
        };

        var found = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.Ordinal);
        foreach (var relativePath in landmarks.Values)
        {
            var nodes = ParseScene(relativePath);
            var root = Assert.Single(nodes, node => node.Parent is null);
            var origin = Position(root);
            foreach (var marker in nodes.Where(node => node.Type == "Marker3D" &&
                         node.Name.StartsWith("Anchor_", StringComparison.Ordinal)))
            {
                var local = Position(marker);
                found[marker.Name["Anchor_".Length..]] = (origin.X + local.X, origin.Y + local.Y, origin.Z + local.Z);
            }
        }

        var definitions = ReedwaterLayout.CreateAnchors();
        Assert.Equal(definitions.Count, found.Count);
        Assert.All(definitions, definition =>
        {
            Assert.True(found.TryGetValue(definition.Id, out var actual), $"Missing marker for {definition.Id}");
            Assert.Equal(definition.Position.X, actual.X, 3);
            Assert.Equal(definition.Position.Y, actual.Y, 3);
            Assert.Equal(definition.Position.Z, actual.Z, 3);
        });

        AssertLocalMarker(landmarks["StartBank"], "BridgeGround_rw_start_post", ReedwaterLayout.StartPostGround);
        AssertLocalMarker(landmarks["IslandA"], "BridgeGround_rw_island_a_post", ReedwaterLayout.IslandAPostGround);
        AssertLocalMarker(landmarks["IslandB"], "BridgeGround_rw_island_b_post", ReedwaterLayout.IslandBPostGround);
        AssertLocalMarker(landmarks["FarBank"], "BridgeGround_rw_far_post", ReedwaterLayout.FarPostGround);
        AssertLocalMarker(landmarks["Outpost"], "NurseryPoint", ReedwaterLayout.NurseryPosition);
        AssertLocalMarker(landmarks["IslandB"], "FirstDestinationPoint", ReedwaterLayout.FirstDestinationPosition);
        AssertLocalMarker(landmarks["FarBank"], "FarBankPoint", ReedwaterLayout.FarBankPosition);
        Assert.Equal(5, Directory.GetFiles(Path.Combine(Root,
            "scenes/world/regions/ReedwaterBank/landmarks"), "*.tscn").Length);
    }

    [Fact]
    public void Reedwater_IsAuthoredWithWaterCollisionAndNoProceduralFallback()
    {
        var bank = ParseScene("scenes/world/regions/ReedwaterBank/ReedwaterBank.tscn");
        var bankText = ReadScene("scenes/world/regions/ReedwaterBank/ReedwaterBank.tscn");
        foreach (var required in new[] { "World", "Anchors", "TravelPoints" })
            Assert.Contains(bank, node => node.Name == required);
        Assert.Contains("path=\"res://scenes/world/regions/ReedwaterBank/ReedwaterWorld.tscn\"", bankText);

        var terrain = ParseScene("scenes/world/regions/ReedwaterBank/ReedwaterTerrain.tscn");
        foreach (var bankName in new[] { "StartBank", "FarBank" })
        {
            var body = Assert.Single(terrain, node => node.Name == bankName && node.Type == "StaticBody3D");
            Assert.Equal("1", body.Properties["collision_layer"]);
            Assert.Equal("0", body.Properties["collision_mask"]);
            Assert.Contains(terrain, node => node.Parent == bankName && node.Type == "CollisionShape3D");
        }
        var water = terrain.Where(node => node.Type == "Area3D" && node.Name.Contains("Water", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, water.Count);
        Assert.All(water, area =>
        {
            Assert.Equal("8", area.Properties["collision_layer"]);
            Assert.Equal("2", area.Properties["collision_mask"]);
            Assert.Contains(terrain, node => node.Parent == area.Name && node.Type == "CollisionShape3D");
        });

        Assert.False(File.Exists(Path.Combine(Root, "scripts/world/ReedwaterScenery.cs")));
        var main = File.ReadAllText(Path.Combine(Root, "scripts/game/Main.cs"));
        Assert.DoesNotContain("new GardenClearing()", main);
        Assert.Contains("GardenClearing.tscn is missing", main);
    }

    [Fact]
    public void RegionCatalog_HasStableIdsAndTransitionDestinations()
    {
        Assert.Equal(new[] { RegionIds.GardenClearing, RegionIds.ReedwaterBank },
            RegionCatalog.All.Select(region => region.Id));
        Assert.All(RegionCatalog.All, region =>
        {
            Assert.True(RegionIds.IsKnown(region.Id));
            Assert.False(string.IsNullOrWhiteSpace(region.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(region.NurseryNode));
            Assert.NotNull(region.GetAnchor(region.Anchors.First().Id));
        });
        Assert.True(RegionCatalog.Reedwater.FirstDestinationNode.Length > 0);
        Assert.True(RegionCatalog.Reedwater.FarBankNode.Length > 0);
        var allAnchorIds = RegionCatalog.AllAnchors.Select(anchor => anchor.Id).ToList();
        Assert.Equal(allAnchorIds.Count, allAnchorIds.Distinct(StringComparer.Ordinal).Count());
        var allNodeIds = RegionCatalog.AllNodes.Select(node => node.Id).ToList();
        Assert.Equal(allNodeIds.Count, allNodeIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AllReferencedScenesAndModelsExist_AndNoProceduralBuilderRemains()
    {
        Assert.False(File.Exists(Path.Combine(Root, "scripts/world/GardenScenery.cs")));

        var sceneFiles = Directory.GetFiles(Path.Combine(Root, "scenes"), "*.tscn", SearchOption.AllDirectories);
        foreach (var scene in sceneFiles)
        {
            var text = File.ReadAllText(scene);
            foreach (Match match in Regex.Matches(text, "path=\"(res://[^\"]+)\""))
            {
                var resourcePath = match.Groups[1].Value;
                var local = Path.Combine(Root, resourcePath["res://".Length..]);
                Assert.True(File.Exists(local) || Directory.Exists(local) || File.Exists(local + ".import"),
                    $"{scene} references missing resource {resourcePath}");
            }
        }

        foreach (var script in Directory.GetFiles(Path.Combine(Root, "scripts"), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(script);
            foreach (var call in new[] { "GardenScenery.Build(", "GardenScenery.Spawn(", "GardenScenery.CreateTitleCamera(" })
                Assert.False(text.Contains(call, StringComparison.Ordinal),
                    $"{script} still calls the procedural garden builder via {call}");
        }
    }
}
