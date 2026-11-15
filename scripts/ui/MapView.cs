using Godot;
using Webwild.Core;

namespace Webwild.Game;

/// <summary>
/// Child-friendly wilderness map: explored chunk cells tinted by biome, river
/// ribbons, visited landmarks and the spider's current position. It is a pure
/// presentation of Core data; the exploration record and save payload stay in
/// <see cref="ExplorationRecord"/> and the world manifest.
/// </summary>
public partial class MapView : Control
{
    private const float Padding = 12;
    private const long MaxColumns = 84;
    private List<ChunkCoordinate> _explored = new();
    private HashSet<ChunkCoordinate> _exploredSet = new();
    private Vec3 _player;
    private IReadOnlyCollection<string> _visited = Array.Empty<string>();
    private WorldGenerator? _generator;
    private BiomeMap? _biomes;

    /// <summary>Cells actually painted in the last draw (probe/diagnostic).</summary>
    public int LastDrawnCells { get; private set; }

    public void Configure(
        IReadOnlyCollection<ChunkCoordinate> explored,
        Vec3 player,
        WorldGenerator generator,
        IReadOnlyCollection<string> visitedLandmarks)
    {
        _explored = explored.ToList();
        _exploredSet = _explored.ToHashSet();
        _player = player;
        _generator = generator;
        _biomes = new BiomeMap(generator.Seed);
        _visited = visitedLandmarks;
        CustomMinimumSize = new Vector2(560, 280);
        MouseFilter = MouseFilterEnum.Ignore;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.063f, 0.118f, 0.086f));
        var font = ThemeDB.FallbackFont;
        if (_generator is null || _biomes is null)
        {
            DrawString(font, new Vector2(16, 26), "The map is not ready yet.",
                HorizontalAlignment.Left, -1, 14, new Color(0.94f, 0.95f, 0.86f));
            return;
        }

        var playerChunk = ChunkCoordinate.OfWorldPoint(_player.X, _player.Z);
        long minX = playerChunk.X, maxX = playerChunk.X;
        long minZ = playerChunk.Z, maxZ = playerChunk.Z;
        foreach (var coordinate in _explored)
        {
            minX = Math.Min(minX, coordinate.X);
            maxX = Math.Max(maxX, coordinate.X);
            minZ = Math.Min(minZ, coordinate.Z);
            maxZ = Math.Max(maxZ, coordinate.Z);
        }

        // A long trail should stay readable: show a window around the player
        // rather than shrinking thousands of chunks into invisible pixels.
        var cropped = false;
        if (maxX - minX + 1 > MaxColumns)
        {
            cropped = true;
            minX = playerChunk.X - MaxColumns / 2;
            maxX = minX + MaxColumns - 1;
        }
        if (maxZ - minZ + 1 > MaxColumns)
        {
            cropped = true;
            minZ = playerChunk.Z - MaxColumns / 2;
            maxZ = minZ + MaxColumns - 1;
        }

        var worldMinX = minX * WorldChunk.Size;
        var worldMinZ = minZ * WorldChunk.Size;
        var spanX = (maxX - minX + 1) * WorldChunk.Size;
        var spanZ = (maxZ - minZ + 1) * WorldChunk.Size;
        var scale = Mathf.Min(
            (size.X - 2 * Padding) / (float)spanX,
            (size.Y - 2 * Padding) / (float)spanZ);
        var cell = Mathf.Max(1f, (float)(WorldChunk.Size * scale));
        var mapWidth = (float)spanX * scale;
        var mapHeight = (float)spanZ * scale;
        var origin = new Vector2(
            Padding + (size.X - 2 * Padding - mapWidth) * 0.5f,
            Padding + (size.Y - 2 * Padding - mapHeight) * 0.5f);

        Vector2 ToPx(double worldX, double worldZ) => origin + new Vector2(
            (float)((worldX - worldMinX) * scale),
            (float)((worldZ - worldMinZ) * scale));

        DrawRect(new Rect2(origin, new Vector2(mapWidth, mapHeight)),
            new Color(0.082f, 0.153f, 0.106f));

        LastDrawnCells = 0;
        foreach (var coordinate in _explored)
        {
            if (coordinate.X < minX || coordinate.X > maxX ||
                coordinate.Z < minZ || coordinate.Z > maxZ) continue;
            var centreX = coordinate.X * WorldChunk.Size + WorldChunk.Size * 0.5;
            var centreZ = coordinate.Z * WorldChunk.Size + WorldChunk.Size * 0.5;
            var weights = _biomes.WeightsAt(centreX, centreZ);
            var rgb = TerrainPalette.ColorOf(weights);
            var color = new Color((float)rgb.X, (float)rgb.Y, (float)rgb.Z);
            var luminance = 0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B;
            if (luminance > 0.55f) color = color.Darkened(0.12f);
            var cellSize = Mathf.Max(1f, cell - 0.35f);
            DrawRect(new Rect2(ToPx(coordinate.X * WorldChunk.Size,
                coordinate.Z * WorldChunk.Size), new Vector2(cellSize, cellSize)), color);
            LastDrawnCells++;
        }

        // Rivers are an overlay so they stay visible where ground is explored.
        DrawRivers(ToPx, worldMinX, worldMinZ, spanX, spanZ, minZ, maxZ);

        DrawLandmark("Garden", new Vec3(0, 0, 0), ToPx, minX, maxX, minZ, maxZ, font);
        if (_visited.Contains(LandmarkCatalog.Reedwater))
            DrawLandmark("Reedwater", LandmarkCatalog.OriginOf(LandmarkCatalog.Reedwater),
                ToPx, minX, maxX, minZ, maxZ, font);

        var player = ToPx(_player.X, _player.Z);
        DrawCircle(player, 7.5f, new Color(0.06f, 0.1f, 0.08f, 0.95f));
        DrawCircle(player, 5.5f, new Color(0.35f, 0.86f, 0.78f));
        DrawCircle(player, 2.2f, new Color(0.95f, 0.98f, 0.9f));

        DrawString(font, new Vector2(10, 18), "N ↑",
            HorizontalAlignment.Left, -1, 13, new Color(0.9f, 0.94f, 0.82f, 0.9f));
        var note = cropped ? "Most recent explored area" : "All explored ground";
        DrawString(font, new Vector2(10, size.Y - 8), note,
            HorizontalAlignment.Left, -1, 12, new Color(0.82f, 0.88f, 0.76f, 0.8f));
    }

    private void DrawRivers(Func<double, double, Vector2> toPx,
        double worldMinX, double worldMinZ, double spanX, double spanZ,
        long minZ, long maxZ)
    {
        if (_generator is null) return;
        var rivers = _generator.Rivers;
        var mapMinZ = worldMinZ;
        var mapMaxZ = worldMinZ + spanZ;
        var first = (int)Math.Floor((mapMinZ - RiverMap.Spacing) / RiverMap.Spacing);
        var last = (int)Math.Ceiling((mapMaxZ + RiverMap.Spacing) / RiverMap.Spacing);
        var step = Math.Max(4.0, spanX / 80.0);
        var colour = new Color(0.24f, 0.55f, 0.61f, 0.72f);
        for (var index = first; index <= last; index++)
        {
            var north = new List<Vector2>();
            var south = new List<Vector2>();
            void Flush()
            {
                if (north.Count < 2)
                {
                    north.Clear();
                    south.Clear();
                    return;
                }
                south.Reverse();
                var polygon = new Vector2[north.Count + south.Count];
                north.CopyTo(polygon);
                south.CopyTo(polygon, north.Count);
                DrawColoredPolygon(polygon, colour);
                north.Clear();
                south.Clear();
            }

            for (var x = worldMinX; x <= worldMinX + spanX + step * 0.5; x += step)
            {
                var centre = RiverMap.CenterAt(index, x);
                var half = RiverMap.HalfWidthAt(index, x);
                var northZ = centre - half;
                var southZ = centre + half;
                // Only rivers through explored ground, fully inside the map;
                // a fragment at the edge must never paint over the modal.
                if (northZ < mapMinZ || southZ > mapMaxZ - 1.5 ||
                    !_exploredSet.Contains(ChunkCoordinate.OfWorldPoint(x, centre)))
                {
                    Flush();
                    continue;
                }
                north.Add(toPx(x, northZ));
                south.Add(toPx(x, southZ));
            }
            Flush();
        }
    }

    private void DrawLandmark(string label, Vec3 world,
        Func<double, double, Vector2> toPx,
        long minX, long maxX, long minZ, long maxZ, Font font)
    {
        var chunk = ChunkCoordinate.OfWorldPoint(world.X, world.Z);
        if (chunk.X < minX || chunk.X > maxX || chunk.Z < minZ || chunk.Z > maxZ) return;
        var point = toPx(world.X, world.Z);
        DrawCircle(point, 8.5f, new Color(0.08f, 0.12f, 0.08f, 0.9f));
        DrawCircle(point, 6.0f, new Color(0.96f, 0.78f, 0.31f));
        DrawCircle(point, 2.8f, new Color(0.33f, 0.25f, 0.12f));
        DrawString(font, point + new Vector2(10, 4), label,
            HorizontalAlignment.Left, -1, 12, new Color(0.97f, 0.93f, 0.76f));
    }
}
