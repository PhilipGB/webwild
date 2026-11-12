using Godot;

namespace Webwild.Game;

/// <summary>
/// The supplied low-poly GLBs store their palette in vertex colours and contain
/// no authored materials. Godot's default material ignores those colours, so
/// every imported visual gets a small shared material that uses them.
/// </summary>
public static class ArtMaterials
{
    private static StandardMaterial3D? _vertexColorMaterial;
    private static StandardMaterial3D? _reedwaterVertexColorMaterial;

    public static StandardMaterial3D VertexColorMaterial => _vertexColorMaterial ??= new StandardMaterial3D
    {
        AlbedoColor = Colors.White,
        VertexColorUseAsAlbedo = true,
        Roughness = 0.78f,
        Metallic = 0.02f
    };

    /// <summary>
    /// Reedwater foliage and dragonfly wings are thin, so the supplied material
    /// renders double-sided. Prefer the authored resource; fall back to an
    /// equivalent material if an exported build omits it.
    /// </summary>
    public static StandardMaterial3D ReedwaterVertexColorMaterial
    {
        get
        {
            if (_reedwaterVertexColorMaterial is not null) return _reedwaterVertexColorMaterial;
            var loaded = ResourceLoader.Load<StandardMaterial3D>(
                "res://art/materials/reedwater_vertex_color_material.tres");
            _reedwaterVertexColorMaterial = loaded ?? new StandardMaterial3D
            {
                AlbedoColor = Colors.White,
                VertexColorUseAsAlbedo = true,
                Roughness = 0.78f,
                Metallic = 0.02f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };
            return _reedwaterVertexColorMaterial;
        }
    }

    public static void ApplyVertexColors(Node root, bool reedwater = false)
    {
        var material = reedwater ? ReedwaterVertexColorMaterial : VertexColorMaterial;
        foreach (var child in root.GetChildren())
        {
            if (child is MeshInstance3D mesh && mesh.Mesh is not null)
                mesh.MaterialOverride = material;
            ApplyVertexColors(child, reedwater);
        }
    }

    /// <summary>
    /// Apply the vertex-colour material to imported model instances only. The
    /// authored scenes already carry editor-visible material overrides, but an
    /// exported build can drop instance property overrides on imported children.
    /// This narrow runtime pass keeps exported visuals identical without touching
    /// authored primitive materials or constructing any geometry.
    /// </summary>
    public static void ApplyToModelInstances(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            var scenePath = child.SceneFilePath ?? "";
            if (scenePath.StartsWith("res://art/models/", StringComparison.Ordinal))
                ApplyVertexColors(child, scenePath.StartsWith("res://art/models/reedwater/", StringComparison.Ordinal));
            ApplyToModelInstances(child);
        }
    }
}
