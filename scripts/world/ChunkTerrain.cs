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
    /// <summary>The authored meadow top colour, so procedural grass reads as
    /// the same surface the garden uses; per-quad variation adds the low-poly
    /// patchwork feel.</summary>
    private static readonly Color MeadowGreen = new(0.466667f, 0.658824f, 0.352941f);

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

    /// <summary>Map a generator prop kind to the supplied GLB that dresses it.</summary>
    private static string? PropScenePath(string kind) => kind switch
    {
        "grass" => "res://art/models/flora/grass_tuft.glb",
        "clover" => "res://art/models/flora/clover_patch.glb",
        "tallgrass" => "res://art/models/flora/tall_grass.glb",
        "flower_yellow" => "res://art/models/flora/flower_yellow.glb",
        "flower_pink" => "res://art/models/flora/flower_pink.glb",
        "flower_purple" => "res://art/models/flora/flower_purple.glb",
        "fern" => "res://art/models/flora/fern.glb",
        "mushroom" => "res://art/models/flora/mushroom.glb",
        "moss_patch" => "res://art/models/flora/moss_patch.glb",
        "pebble" => "res://art/models/terrain/pebble.glb",
        "flat_stone" => "res://art/models/terrain/flat_stone.glb",
        "small_log" => "res://art/models/terrain/small_log.glb",
        _ => null
    };

    /// <summary>Build the full chunk node: terrain body plus prop dressing.</summary>
    public static StaticBody3D Build(ChunkData data)
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

        var mesh = BuildMesh(data);
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
            instance.Position = ToVector3(prop.Position);
            instance.RotationDegrees = new Vector3(0, (float)prop.RotationDegrees, 0);
            instance.Scale = Vector3.One * (float)prop.Scale;
            ArtMaterials.ApplyVertexColors(instance);
            body.AddChild(instance);
        }

        return body;
    }

    /// <summary>
    /// Flat-shaded mesh: every quad is four unique vertices so per-quad normals
    /// and colours stay crisp, matching the authored garden's look.
    /// </summary>
    private static ArrayMesh BuildMesh(ChunkData data)
    {
        var resolution = WorldChunk.TerrainResolution;
        var spacing = WorldChunk.TerrainSpacing;
        var quads = (resolution - 1) * (resolution - 1);
        var positions = new Vector3[quads * 4];
        var normals = new Vector3[quads * 4];
        var colors = new Color[quads * 4];
        var indices = new int[quads * 6];
        var heights = data.Heights;
        var originX = data.Coordinate.X * WorldChunk.Size;
        var originZ = data.Coordinate.Z * WorldChunk.Size;

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

            var tint = QuadTint(originX + ix * spacing, originZ + iz * spacing);
            var color = new Color(
                MeadowGreen.R * tint,
                MeadowGreen.G * tint,
                MeadowGreen.B * tint);
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

    /// <summary>Deterministic per-quad brightness variation in [0.92, 1.08].</summary>
    private static float QuadTint(double worldX, double worldZ)
    {
        var qx = (long)Math.Floor(worldX / 2.0);
        var qz = (long)Math.Floor(worldZ / 2.0);
        var h = DeterministicHash.Combine(
            DeterministicHash.Hash(qx), DeterministicHash.Hash(qz));
        return (float)(0.92 + DeterministicHash.Unit(h) * 0.16);
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
    private readonly List<ChunkCoordinate> _pending = new();
    private bool _frozen;

    public ChunkStreamer(WorldGenerator generator, Node3D host)
    {
        _generator = generator;
        _streamer = new WorldStreamer(loadRadius: 3, unloadRadius: 5);
        _host = host;
    }

    public int LoadedCount => _nodes.Count;
    public int PendingCount => _pending.Count;
    public ChunkCoordinate Centre => _streamer.Centre;
    public IReadOnlyDictionary<ChunkCoordinate, StaticBody3D> Nodes => _nodes;

    public bool IsChunkLoaded(ChunkCoordinate coordinate) => _nodes.ContainsKey(coordinate);

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
                node.QueueFree();
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
        var node = ChunkTerrainBuilder.Build(data);
        _nodes[coordinate] = node;
        _host.AddChild(node);
    }
}
