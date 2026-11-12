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

    public static StandardMaterial3D VertexColorMaterial => _vertexColorMaterial ??= new StandardMaterial3D
    {
        AlbedoColor = Colors.White,
        VertexColorUseAsAlbedo = true,
        Roughness = 0.78f,
        Metallic = 0.02f
    };

    public static void ApplyVertexColors(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is MeshInstance3D mesh && mesh.Mesh is not null)
                mesh.MaterialOverride = VertexColorMaterial;
            ApplyVertexColors(child);
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
                ApplyVertexColors(child);
            ApplyToModelInstances(child);
        }
    }
}
