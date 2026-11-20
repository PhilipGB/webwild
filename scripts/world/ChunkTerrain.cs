using Godot;
using Webwild.Core;

namespace Webwild.Game;

/// <summary>
/// Builds the Godot presentation for one generated chunk: a flat-shaded,
/// vertex-coloured terrain mesh with trimesh collision, plus deterministic
/// prop instances. Purely presentational — every rule comes from
/// <see cref="WorldGenerator"/>; this class only turns a <see cref="ChunkData"/>
/// into scene nodes. The same chunk therefore rebuilds identically after any
/// unload, which the runtime probe verifies via content hashes.
/// </summary>
public static class ChunkTerrainBuilder
{
    private static readonly Dictionary<string, PackedScene> PropScenes = new();

    /// <summary>Generated terrain material: vertex colours, double-sided so the
    /// winding order can never hide the ground.</summary>
    private static readonly StandardMaterial3D TerrainMaterial = new()
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.95f,
        Metallic = 0f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled
    };

    private static readonly StandardMaterial3D RiverWaterMaterial = new()
    {
        AlbedoColor = new Color(0.145f, 0.482f, 0.541f, 0.76f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.16f,
        Metallic = 0.04f,
        EmissionEnabled = true,
        Emission = new Color(0.235f, 0.639f, 0.647f),
        EmissionEnergyMultiplier = 0.22f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled
    };

    private static readonly StandardMaterial3D BridgeWoodMaterial = new()
    {
        AlbedoColor = new Color(0.478f, 0.345f, 0.216f),
        Roughness = 0.92f,
        Metallic = 0f
    };

    private static readonly StandardMaterial3D BridgeRopeMaterial = new()
    {
        AlbedoColor = new Color(0.788f, 0.878f, 0.769f),
        Roughness = 0.72f,
        Metallic = 0f
    };

    /// <summary>Map a generator prop kind to the supplied GLB that dresses it.</summary>
    private static string? PropScenePath(string kind) => kind switch
    {
        "grass" => "res://art/models/flora/grass_tuft.glb",
        "clover" => "res://art/models/flora/clover_patch.glb",
        "clover_tall" => "res://art/models/flora/clover_tall.glb",
        "tallgrass" => "res://art/models/flora/tall_grass.glb",
        "flower_yellow" => "res://art/models/flora/flower_yellow.glb",
        "flower_pink" => "res://art/models/flora/flower_pink.glb",
        "flower_purple" => "res://art/models/flora/flower_purple.glb",
        "daisy" => "res://art/models/flora/daisy.glb",
        "fern" => "res://art/models/flora/fern.glb",
        "mushroom" => "res://art/models/flora/mushroom.glb",
        "mushroom_blue" => "res://art/models/flora/mushroom_blue.glb",
        "moss_patch" => "res://art/models/flora/moss_patch.glb",
        "bush" => "res://art/models/flora/bush.glb",
        "broad_leaf" => "res://art/models/flora/broad_leaf.glb",
        "leaf_pile" => "res://art/models/flora/leaf_pile.glb",
        "fallen_leaf" => "res://art/models/flora/fallen_leaf.glb",
        "reed" => "res://art/models/flora/reed.glb",
        "sapling" => "res://art/models/flora/sapling.glb",
        "pebble" => "res://art/models/terrain/pebble.glb",
        "pebble_cluster" => "res://art/models/terrain/pebble_cluster.glb",
        "flat_stone" => "res://art/models/terrain/flat_stone.glb",
        "small_log" => "res://art/models/terrain/small_log.glb",
        "boulder" => "res://art/models/terrain/boulder.glb",
        "moss_rock" => "res://art/models/terrain/moss_rock.glb",
        "soil_mound" => "res://art/models/terrain/soil_mound.glb",
        "marsh_grass" => "res://art/models/reedwater/marsh_grass_tuft.glb",
        "cattail" => "res://art/models/reedwater/cattail_cluster.glb",
        _ => null
    };

    /// <summary>Build the full chunk node: terrain body plus prop dressing.</summary>
    public static StaticBody3D Build(ChunkData data, int seed, int generatorVersion = 1)
    {
        var body = new StaticBody3D
        {
            Name = $"Chunk_{data.Coordinate.Id}",
            Position = ToVector3(data.Coordinate.OriginWorld),
            CollisionLayer = 1,
            CollisionMask = 0
        };
        body.SetMeta("chunk_id", data.Coordinate.Id);
        body.SetMeta("content_hash", data.ContentHash().ToString("x16"));
        body.SetMeta("generator_version", generatorVersion);

        var mesh = BuildMesh(data, seed);
        var meshInstance = new MeshInstance3D
        {
            Name = "TerrainMesh",
            Mesh = mesh,
            MaterialOverride = TerrainMaterial
        };
        body.AddChild(meshInstance);

        var shape = new ConcavePolygonShape3D
        {
            Data = mesh.GetFaces(),
            BackfaceCollision = true
        };
        var collider = new CollisionShape3D { Name = "TerrainShape", Shape = shape };
        body.AddChild(collider);

        foreach (var prop in data.Props)
        {
            var path = PropScenePath(prop.Kind);
            if (path is null) continue;
            var packed = LoadPropScene(path);
            if (packed is null) continue;
            var instance = packed.Instantiate<Node3D>();
            if (instance is null) continue;
            instance.Name = $"Prop_{prop.Kind}_{prop.Position.X:0.##}_{prop.Position.Z:0.##}";
            // Core placements are world-space; the chunk body already carries
            // the chunk origin, so children must use chunk-local coordinates.
            instance.Position = ToVector3(prop.Position) - ToVector3(data.Coordinate.OriginWorld);
            instance.RotationDegrees = new Vector3(0, (float)prop.RotationDegrees, 0);
            instance.Scale = Vector3.One * (float)prop.Scale;
            ArtMaterials.ApplyVertexColors(instance);
            body.AddChild(instance);
        }

        if (data.WaterQuads.Count > 0)
            body.AddChild(BuildWater(data));
        foreach (var bridge in data.Bridges)
            AddBridge(body, data, bridge);

        return body;
    }

    /// <summary>Flat river-water surface, one quad per visual grid cell.</summary>
    private static MeshInstance3D BuildWater(ChunkData data)
    {
        var origin = data.Coordinate.OriginWorld;
        var half = WorldChunk.TerrainSpacing * 0.5;
        var vertices = new List<Vector3>(data.WaterQuads.Count * 4);
        var normals = new List<Vector3>(data.WaterQuads.Count * 4);
        var indices = new List<int>(data.WaterQuads.Count * 6);
        foreach (var quad in data.WaterQuads)
        {
            var cx = (float)(quad.CenterX - origin.X);
            var cz = (float)(quad.CenterZ - origin.Z);
            var baseIndex = vertices.Count;
            vertices.Add(new Vector3(cx - (float)half, (float)quad.Level00, cz - (float)half));
            vertices.Add(new Vector3(cx + (float)half, (float)quad.Level10, cz - (float)half));
            vertices.Add(new Vector3(cx - (float)half, (float)quad.Level01, cz + (float)half));
            vertices.Add(new Vector3(cx + (float)half, (float)quad.Level11, cz + (float)half));
            for (var i = 0; i < 4; i++) normals.Add(Vector3.Up);
            indices.AddRange(new[]
            {
                baseIndex, baseIndex + 2, baseIndex + 3,
                baseIndex, baseIndex + 3, baseIndex + 1
            });
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return new MeshInstance3D
        {
            Name = "RiverWater",
            Mesh = mesh,
            MaterialOverride = RiverWaterMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
    }

    /// <summary>
    /// One stable, low bank-to-bank crossing. The deck is a tilted solid box
    /// whose ends meet the sampled bank heights; rails and legs are visual.
    /// </summary>
    private static void AddBridge(StaticBody3D chunkBody, ChunkData data, RiverBridge bridge)
    {
        var origin = data.Coordinate.OriginWorld;
        var length = (float)(bridge.HalfSpan * 2.0);
        var slope = (bridge.SouthGroundY - bridge.NorthGroundY) / (bridge.HalfSpan * 2.0);
        // Meet both banks when possible, but never let a low bank put the deck
        // under the water: keep at least 0.45 m of clearance.
        var centerY = Math.Max(
            (bridge.NorthGroundY + bridge.SouthGroundY) * 0.5 - 0.12,
            bridge.WaterLevel + 0.37);
        var body = new StaticBody3D
        {
            Name = "RiverBridge_" + bridge.Id,
            Position = new Vector3(
                (float)(bridge.X - origin.X),
                (float)centerY,
                (float)(bridge.CenterZ - origin.Z)),
            Rotation = new Vector3((float)-Math.Atan(slope), 0, 0),
            CollisionLayer = 1,
            CollisionMask = 0
        };
        body.SetMeta("bridge_id", bridge.Id);
        body.SetMeta("river_index", bridge.RiverIndex);
        body.SetMeta("water_level", bridge.WaterLevel);
        body.AddToGroup("river_bridge");

        var deck = new MeshInstance3D
        {
            Name = "Deck",
            Mesh = new BoxMesh { Size = new Vector3(2.8f, 0.16f, length) },
            MaterialOverride = BridgeWoodMaterial
        };
        body.AddChild(deck);
        body.AddChild(new CollisionShape3D
        {
            Name = "DeckShape",
            Shape = new BoxShape3D { Size = new Vector3(2.8f, 0.16f, length) }
        });

        // Visible cross-planks and side beams break up the single-box deck.
        const int plankCount = 11;
        for (var i = 0; i < plankCount; i++)
        {
            var z = -length * 0.5f + (i + 0.5f) * (length / plankCount);
            body.AddChild(new MeshInstance3D
            {
                Name = $"Plank_{i}",
                Position = new Vector3(0, 0.095f, z),
                Mesh = new BoxMesh
                {
                    Size = new Vector3(2.9f, 0.05f, length / plankCount * 0.72f)
                },
                MaterialOverride = BridgeWoodMaterial
            });
        }
        for (var side = -1; side <= 1; side += 2)
        {
            body.AddChild(new MeshInstance3D
            {
                Name = side < 0 ? "BeamWest" : "BeamEast",
                Position = new Vector3(side * 1.34f, -0.02f, 0),
                Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.22f, length) },
                MaterialOverride = BridgeWoodMaterial
            });
        }

        // Woven-silk rails run the full span above low wooden posts.
        for (var side = -1; side <= 1; side += 2)
        {
            var x = side * 1.28f;
            body.AddChild(new MeshInstance3D
            {
                Name = side < 0 ? "RopeWest" : "RopeEast",
                Position = new Vector3(x, 0.52f, 0),
                Mesh = new BoxMesh { Size = new Vector3(0.09f, 0.09f, length * 0.98f) },
                MaterialOverride = BridgeRopeMaterial
            });
            foreach (var zSign in new[] { -1f, 1f })
            {
                body.AddChild(new MeshInstance3D
                {
                    Name = $"Post_{side}_{zSign}",
                    Position = new Vector3(x, 0.28f, zSign * (length * 0.5f - 0.55f)),
                    Mesh = new BoxMesh { Size = new Vector3(0.15f, 0.86f, 0.15f) },
                    MaterialOverride = BridgeWoodMaterial
                });
            }
            body.AddChild(new MeshInstance3D
            {
                Name = side < 0 ? "LegWest" : "LegEast",
                Position = new Vector3(x * 0.72f, -0.72f, 0),
                Mesh = new BoxMesh { Size = new Vector3(0.16f, 1.55f, 0.16f) },
                MaterialOverride = BridgeWoodMaterial
            });
        }

        chunkBody.AddChild(body);
    }

    /// <summary>
    /// Flat-shaded mesh: every quad is four unique vertices so per-quad normals
    /// and colours stay crisp, matching the authored garden's look.
    /// </summary>
    private static ArrayMesh BuildMesh(ChunkData data, int seed)
    {
        var resolution = WorldChunk.TerrainResolution;
        var spacing = WorldChunk.TerrainSpacing;
        var quads = (resolution - 1) * (resolution - 1);
        var positions = new Vector3[quads * 4];
        var normals = new Vector3[quads * 4];
        var colors = new Color[quads * 4];
        var indices = new int[quads * 6];
        var heights = data.Heights;
        var biomes = data.Biomes;
        var originX = data.Coordinate.X * WorldChunk.Size;
        var originZ = data.Coordinate.Z * WorldChunk.Size;
        var rivers = new RiverMap(seed);

        var vertex = 0;
        var index = 0;
        for (var ix = 0; ix < resolution - 1; ix++)
        for (var iz = 0; iz < resolution - 1; iz++)
        {
            var x0 = (float)(ix * spacing);
            var x1 = (float)((ix + 1) * spacing);
            var z0 = (float)(iz * spacing);
            var z1 = (float)((iz + 1) * spacing);
            var h00 = (float)heights[ix, iz];
            var h10 = (float)heights[ix + 1, iz];
            var h01 = (float)heights[ix, iz + 1];
            var h11 = (float)heights[ix + 1, iz + 1];

            positions[vertex] = new Vector3(x0, h00, z0);
            positions[vertex + 1] = new Vector3(x1, h10, z0);
            positions[vertex + 2] = new Vector3(x0, h01, z1);
            positions[vertex + 3] = new Vector3(x1, h11, z1);

            // Flat normal from the quad's own plane.
            var edge1 = positions[vertex + 2] - positions[vertex];
            var edge2 = positions[vertex + 1] - positions[vertex];
            var normal = edge1.Cross(edge2).Normalized();
            if (normal.Y < 0) normal = -normal;
            for (var i = 0; i < 4; i++) normals[vertex + i] = normal;

            var weights = BiomeWeightsBetween(biomes, ix, iz);
            var quadX = originX + (ix + 0.5) * spacing;
            var quadZ = originZ + (iz + 0.5) * spacing;
            var tint = (float)TerrainPalette.QuadTint(seed, quadX, quadZ);
            // Version-2 chunks paint their quads from the eight-environment
            // palette; version-1 keeps the meadow/moss/bloom blend unchanged.
            var rgb = data.Environments is { } environments
                ? EnvironmentPalette.ColorOf(EnvironmentWeightsBetween(environments, ix, iz))
                : TerrainPalette.ColorOf(weights);
            // Wet mud under and beside the water makes the channel read as a
            // carved streambed through the translucent surface.
            var river = rivers.Sample(quadX, quadZ);
            if (river.HasRiver)
            {
                // Mud is only tinted under cells that also receive a water
                // quad, so no dark slabs leak past the visible waterline.
                var bedEdge = river.HalfWidth + RiverMap.VisualMargin;
                var wetEdge = river.HalfWidth + 2.2;
                if (river.Distance <= bedEdge)
                    rgb = TerrainPalette.RiverBed;
                else if (river.Distance <= wetEdge)
                    rgb = TerrainPalette.Blend(TerrainPalette.RiverBank, rgb,
                        (river.Distance - bedEdge) / (wetEdge - bedEdge));
            }
            var color = new Color((float)rgb.X * tint, (float)rgb.Y * tint, (float)rgb.Z * tint);
            for (var i = 0; i < 4; i++) colors[vertex + i] = color;

            indices[index] = vertex;
            indices[index + 1] = vertex + 2;
            indices[index + 2] = vertex + 3;
            indices[index + 3] = vertex;
            indices[index + 4] = vertex + 3;
            indices[index + 5] = vertex + 1;
            vertex += 4;
            index += 6;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static BiomeWeights BiomeWeightsBetween(BiomeWeights[,] biomes, int ix, int iz)
    {
        if (biomes.GetLength(0) <= ix + 1 || biomes.GetLength(1) <= iz + 1)
            return BiomeWeights.PureMeadow;
        var a = biomes[ix, iz];
        var b = biomes[ix + 1, iz];
        var c = biomes[ix, iz + 1];
        var d = biomes[ix + 1, iz + 1];
        return new BiomeWeights(
            (a.Meadow + b.Meadow + c.Meadow + d.Meadow) * 0.25,
            (a.Moss + b.Moss + c.Moss + d.Moss) * 0.25,
            (a.Bloom + b.Bloom + c.Bloom + d.Bloom) * 0.25);
    }

    private static EnvironmentWeights EnvironmentWeightsBetween(
        EnvironmentWeights[,] environments, int ix, int iz)
    {
        if (environments.GetLength(0) <= ix + 1 || environments.GetLength(1) <= iz + 1)
            return EnvironmentWeights.PureMeadow;
        return EnvironmentWeights.Between(
            environments[ix, iz], environments[ix + 1, iz],
            environments[ix, iz + 1], environments[ix + 1, iz + 1]);
    }

    /// <summary>Flat, per-quad-tinted patch matching generated terrain colours.
    /// Used for the landmark skirts so authored ground meets procedural ground
    /// without a tint break. Purely visual; collision stays with the scene.</summary>
    public static MeshInstance3D BuildApron(WorldGenerator generator, WorldRect rect, string name)
    {
        var seed = generator.Seed;
        var environments = generator.Environments;
        var cell = WorldChunk.TerrainSpacing;
        var biomeMap = environments is null ? new BiomeMap(seed) : null;
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();
        var x = rect.MinX;
        while (x < rect.MaxX - 0.001)
        {
            var x1 = Math.Min(x + cell, rect.MaxX);
            var z = rect.MinZ;
            while (z < rect.MaxZ - 0.001)
            {
                var z1 = Math.Min(z + cell, rect.MaxZ);
                // The skirt paints with whatever the neighbouring generated
                // ground uses: environment palette on version-2 worlds, the
                // meadow/moss/bloom blend on version-1.
                var rgb = environments is not null
                    ? EnvironmentPalette.ColorOf(generator.EnvironmentAt((x + x1) * 0.5, (z + z1) * 0.5))
                    : TerrainPalette.ColorOf(biomeMap!.WeightsAt((x + x1) * 0.5, (z + z1) * 0.5));
                var tint = (float)TerrainPalette.QuadTint(seed, (x + x1) * 0.5, (z + z1) * 0.5);
                var color = new Color((float)rgb.X * tint, (float)rgb.Y * tint, (float)rgb.Z * tint);
                var baseIndex = positions.Count;
                positions.Add(new Vector3((float)x, 0.015f, (float)z));
                positions.Add(new Vector3((float)x1, 0.015f, (float)z));
                positions.Add(new Vector3((float)x, 0.015f, (float)z1));
                positions.Add(new Vector3((float)x1, 0.015f, (float)z1));
                for (var i = 0; i < 4; i++) { normals.Add(Vector3.Up); colors.Add(color); }
                indices.AddRange(new[] { baseIndex, baseIndex + 2, baseIndex + 3, baseIndex, baseIndex + 3, baseIndex + 1 });
                z = z1;
            }
            x = x1;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = TerrainMaterial
        };
    }

    private static PackedScene? LoadPropScene(string path)
    {
        if (PropScenes.TryGetValue(path, out var cached)) return cached;
        var packed = ResourceLoader.Load<PackedScene>(path);
        PropScenes[path] = packed!;
        return packed;
    }

    private static Vector3 ToVector3(Vec3 value) => new((float)value.X, (float)value.Y, (float)value.Z);
}

/// <summary>
/// Engine adapter for <see cref="WorldStreamer"/>. Driven from
/// GardenClearing._Process while the session is in the garden region: applies
/// the streaming plan with a small per-frame build budget, guarantees the
/// chunk under the spider always exists, and unloads retired chunks. When the
/// session leaves the garden (Reedwater Bank), streaming freezes and the host
/// is hidden because the two regions' local coordinates overlap.
/// </summary>
public sealed class ChunkStreamer
{
    /// <summary>Chunk mesh builds allowed per update; the plan order is
    /// nearest-first, so the ground under and around the spider arrives first.</summary>
    public const int BuildsPerFrame = 2;

    private readonly WorldGenerator _generator;
    private readonly WorldStreamer _streamer;
    private readonly Node3D _host;
    private readonly Dictionary<ChunkCoordinate, StaticBody3D> _nodes = new();
    private readonly Dictionary<ChunkCoordinate, ChunkData> _data = new();
    private readonly List<ChunkCoordinate> _pending = new();
    private bool _frozen;

    public ChunkStreamer(WorldGenerator generator, Node3D host)
    {
        _generator = generator;
        _streamer = new WorldStreamer(loadRadius: 3, unloadRadius: 5);
        _host = host;
    }

    public int LoadedCount => _nodes.Count;
    public WorldGenerator Generator => _generator;
    public int PendingCount => _pending.Count;
    public ChunkCoordinate Centre => _streamer.Centre;
    public IReadOnlyDictionary<ChunkCoordinate, StaticBody3D> Nodes => _nodes;

    public bool IsChunkLoaded(ChunkCoordinate coordinate) => _nodes.ContainsKey(coordinate);

    /// <summary>Generated data for a currently loaded chunk (food, props, water).</summary>
    public ChunkData? DataFor(ChunkCoordinate coordinate) =>
        _data.GetValueOrDefault(coordinate);

    /// <summary>Hide all chunk nodes (region switch) and take their collision
    /// out of the physics space. Hidden bodies still collide in Godot, and the
    /// other region's local coordinates overlap world space around the origin,
    /// so leftover garden chunks would wedge the player there.</summary>
    public void SetFrozen(bool frozen)
    {
        _frozen = frozen;
        _host.Visible = !frozen;
        foreach (var node in _nodes.Values)
            node.CollisionLayer = frozen ? 0u : 1u;
    }

    /// <summary>
    /// One streaming step. The chunk under the player is built synchronously so
    /// there is never a frame where the spider can fall through unloaded ground
    /// (teleports, saves restored far from the garden).
    /// </summary>
    public void Update(Vector3 playerPosition, bool active)
    {
        if (_frozen || !active) return;

        var centre = ChunkCoordinate.OfWorldPoint(playerPosition.X, playerPosition.Z);
        var plan = _streamer.Update(centre);

        foreach (var coordinate in plan.ToUnload)
        {
            if (_nodes.Remove(coordinate, out var node))
            {
                _data.Remove(coordinate);
                node.QueueFree();
            }
        }

        // The Core streamer registers planned chunks as active immediately, so
        // each plan only names new requirements. The build queue therefore has
        // to be persistent: it carries unfinished work across frames while the
        // per-frame budget pace stays constant.
        foreach (var coordinate in plan.ToLoad)
            if (!_pending.Contains(coordinate))
                _pending.Add(coordinate);
        _pending.RemoveAll(coordinate =>
            _nodes.ContainsKey(coordinate) ||
            WorldGenerator.IsAuthoredChunk(coordinate) ||
            coordinate.ChebyshevDistance(centre) > _streamer.UnloadRadius);
        _pending.Sort((a, b) =>
        {
            var byDistance = a.ChebyshevDistance(centre).CompareTo(b.ChebyshevDistance(centre));
            return byDistance != 0 ? byDistance : a.CompareTo(b);
        });

        var built = 0;
        // The player's own chunk comes first, synchronously, whenever missing.
        if (!WorldGenerator.IsAuthoredChunk(centre) && !_nodes.ContainsKey(centre))
        {
            Build(centre);
            _pending.Remove(centre);
            built++;
        }

        for (var i = 0; i < _pending.Count && built < BuildsPerFrame; i++)
        {
            Build(_pending[i]);
            _pending.RemoveAt(i);
            i--;
            built++;
        }
    }

    private void Build(ChunkCoordinate coordinate)
    {
        var data = _generator.GenerateChunk(coordinate);
        if (data is null) return; // Authored chunks are never generated.
        var node = ChunkTerrainBuilder.Build(data, _generator.Seed, _generator.GeneratorVersion);
        _nodes[coordinate] = node;
        _data[coordinate] = data;
        _host.AddChild(node);
    }
}
