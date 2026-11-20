using Godot;
using Webwild.Core;

namespace Webwild.Game;

/// <summary>One helper shown on the map: where it is, and where it is going
/// when it is travelling or hauling.</summary>
public sealed record MapWorker(
    string Id, string Name, Vec3 Position, Vec3? Destination,
    bool Travelling, bool Summoned);

/// <summary>
/// Child-friendly wilderness map: explored chunk cells tinted by environment,
/// river ribbons, visited landmarks, the spider's position, durable markers
/// and helper destinations. The view pans by dragging and zooms with the
/// wheel; when the home landmark is off-screen an edge arrow points home.
/// It is a pure presentation of Core data; the exploration record and save
/// payload stay in <see cref="ExplorationRecord"/> and the world manifest.
/// </summary>
public partial class MapView : Control
{
    private const float Padding = 12;
    private const long DefaultColumns = 84;
    private const float MinCellPixels = 2f;
    private const float MaxCellPixels = 46f;
    private const float DragThresholdPixels = 6f;

    private ExplorationRecord? _explored;
    private Vec3 _player;
    private IReadOnlyCollection<string> _visited = Array.Empty<string>();
    private WorldGenerator? _generator;
    private BiomeMap? _biomes;
    private IReadOnlyList<WorldMarker> _markers = Array.Empty<WorldMarker>();
    private IReadOnlyList<MapWorker> _workers = Array.Empty<MapWorker>();
    private readonly Dictionary<string, Vector2> _markerPixels = new(StringComparer.Ordinal);

    // View state survives refreshes; it starts centred on the player.
    private Vec3? _viewCentre;
    private float? _cellPixels;
    private bool _dragging;
    private Vector2 _pressPosition;
    private bool _pannedSincePress;
    private Vector3 _homeArrowDirection;
    private bool _homeArrowVisible;
    private double _homeArrowDistance;

    public event Action<string>? MarkerSelected;
    /// <summary>Raised when the player clicks empty map ground: the world
    /// adapter decides what a note marker at that point means.</summary>
    public event Action<Vec3>? NotePlacedRequested;

    /// <summary>Cells actually painted in the last draw (probe/diagnostic).</summary>
    public int LastDrawnCells { get; private set; }
    public int LastDrawnMarkers { get; private set; }
    public int LastDrawnWorkers { get; private set; }
    public bool HomeArrowVisible => _homeArrowVisible;
    public double HomeArrowDistance => _homeArrowDistance;
    public Vec3 ViewCentre => _viewCentre ?? _player;

    public void Configure(
        ExplorationRecord explored,
        Vec3 player,
        WorldGenerator generator,
        IReadOnlyCollection<string> visitedLandmarks,
        IReadOnlyCollection<WorldMarker>? markers = null,
        IReadOnlyList<MapWorker>? workers = null)
    {
        _explored = explored;
        _player = player;
        _generator = generator;
        _biomes = new BiomeMap(generator.Seed);
        _visited = visitedLandmarks;
        _markers = markers?.ToList() ?? new List<WorldMarker>();
        _workers = workers?.ToList() ?? new List<MapWorker>();
        _markerPixels.Clear();
        CustomMinimumSize = new Vector2(560, 280);
        MouseFilter = MouseFilterEnum.Stop;
        // Keep the player on the map: a fresh view centres on the spider, a
        // panned view stays where the player dragged it.
        if (_viewCentre is null) _viewCentre = player;
        if (_cellPixels is null)
        {
            var columns = Mathf.Min(DefaultColumns, Mathf.Max(12f, Size.X / 8f));
            _cellPixels = Mathf.Clamp(
                Mathf.Min((Size.X - 2 * Padding) / columns,
                    (Size.Y - 2 * Padding) / columns), MinCellPixels, MaxCellPixels);
        }
        QueueRedraw();
    }

    /// <summary>Recentre the view on the player without touching zoom
    /// (probe instrumentation and the "find me" behaviour).</summary>
    public void RecentreOnPlayer()
    {
        _viewCentre = _player;
        QueueRedraw();
    }

    private void Pan(Vector2 deltaPixels)
    {
        if (_cellPixels is not { } cellPixels || cellPixels <= 0) return;
        var worldPerPx = (float)WorldChunk.Size / cellPixels;
        var centre = _viewCentre ?? _player;
        _viewCentre = new Vec3(
            centre.X - deltaPixels.X * worldPerPx,
            centre.Y,
            centre.Z - deltaPixels.Y * worldPerPx);
        QueueRedraw();
    }

    private void Zoom(float factor)
    {
        var cellPixels = _cellPixels ?? 12f;
        _cellPixels = Mathf.Clamp(cellPixels * factor, MinCellPixels, MaxCellPixels);
        QueueRedraw();
    }

    /// <summary>Probe instrumentation: the same pan the drag gesture performs.</summary>
    public void ProbePan(Vector2 deltaPixels)
    {
        _viewCentre ??= _player;
        _cellPixels ??= 12f;
        Pan(deltaPixels);
    }

    /// <summary>Probe instrumentation: the same zoom the wheel performs.</summary>
    public void ProbeZoom(float factor) => Zoom(factor);

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.063f, 0.118f, 0.086f));
        var font = ThemeDB.FallbackFont;
        if (_generator is null || _biomes is null || _explored is null)
        {
            DrawString(font, new Vector2(16, 26), "The map is not ready yet.",
                HorizontalAlignment.Left, -1, 14, new Color(0.94f, 0.95f, 0.86f));
            return;
        }

        var centre = _viewCentre ?? _player;
        var cellPixels = _cellPixels ?? 12f;
        var columns = Mathf.FloorToInt((size.X - 2 * Padding) / cellPixels);
        var rows = Mathf.FloorToInt((size.Y - 2 * Padding) / cellPixels);
        var worldMinX = centre.X - columns * WorldChunk.Size * 0.5;
        var worldMinZ = centre.Z - rows * WorldChunk.Size * 0.5;
        var spanX = columns * WorldChunk.Size;
        var spanZ = rows * WorldChunk.Size;

        Vector2 ToPx(double worldX, double worldZ) => new(
            Padding + (float)((worldX - worldMinX) / WorldChunk.Size * cellPixels),
            Padding + (float)((worldZ - worldMinZ) / WorldChunk.Size * cellPixels));

        DrawRect(new Rect2(Padding, Padding,
                (float)(spanX / WorldChunk.Size * cellPixels),
                (float)(spanZ / WorldChunk.Size * cellPixels)),
            new Color(0.082f, 0.153f, 0.106f));

        // Window the record directly: asking per cell in view keeps the
        // draw cheap no matter how much of the world has been explored.
        var minChunkX = (long)Math.Floor(worldMinX / WorldChunk.Size);
        var minChunkZ = (long)Math.Floor(worldMinZ / WorldChunk.Size);
        var maxChunkX = minChunkX + columns;
        var maxChunkZ = minChunkZ + rows;
        LastDrawnCells = 0;
        for (var chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
        for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
        {
            var coordinate = new ChunkCoordinate(chunkX, chunkZ);
            if (!_explored.Contains(coordinate)) continue;
            var cellCentreX = chunkX * WorldChunk.Size + WorldChunk.Size * 0.5;
            var cellCentreZ = chunkZ * WorldChunk.Size + WorldChunk.Size * 0.5;
            var rgb = _generator.Environments is not null
                ? EnvironmentPalette.ColorOf(_generator.EnvironmentAt(cellCentreX, cellCentreZ))
                : TerrainPalette.ColorOf(_biomes.WeightsAt(cellCentreX, cellCentreZ));
            var color = new Color((float)rgb.X, (float)rgb.Y, (float)rgb.Z);
            var luminance = 0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B;
            if (luminance > 0.55f) color = color.Darkened(0.12f);
            var cellSize = Mathf.Max(1f, cellPixels - 0.35f);
            DrawRect(new Rect2(ToPx(chunkX * WorldChunk.Size,
                chunkZ * WorldChunk.Size), new Vector2(cellSize, cellSize)), color);
            LastDrawnCells++;
        }

        DrawRivers(ToPx, worldMinX, worldMinZ, spanX, spanZ, minChunkX, minChunkZ,
            maxChunkX, maxChunkZ);
        DrawLandmark("Garden", new Vec3(0, 0, 0), ToPx, minChunkX, maxChunkX, minChunkZ,
            maxChunkZ, font);
        if (_visited.Contains(LandmarkCatalog.Reedwater))
            DrawLandmark("Reedwater", LandmarkCatalog.OriginOf(LandmarkCatalog.Reedwater),
                ToPx, minChunkX, maxChunkX, minChunkZ, maxChunkZ, font);
        DrawWorkers(ToPx, minChunkX, maxChunkX, minChunkZ, maxChunkZ);
        DrawMarkers(ToPx, minChunkX, maxChunkX, minChunkZ, maxChunkZ, font);

        var player = ToPx(_player.X, _player.Z);
        DrawCircle(player, 7.5f, new Color(0.06f, 0.1f, 0.08f, 0.95f));
        DrawCircle(player, 5.5f, new Color(0.35f, 0.86f, 0.78f));
        DrawCircle(player, 2.2f, new Color(0.95f, 0.98f, 0.9f));

        DrawHomeArrow(ToPx, font);

        DrawString(font, new Vector2(10, 18), "N ↑",
            HorizontalAlignment.Left, -1, 13, new Color(0.9f, 0.94f, 0.82f, 0.9f));
        DrawString(font, new Vector2(10, size.Y - 8),
            "Drag to pan · wheel to zoom · click ground to drop a note",
            HorizontalAlignment.Left, -1, 12, new Color(0.82f, 0.88f, 0.76f, 0.8f));
    }

    private void DrawHomeArrow(Func<double, double, Vector2> toPx, Font font)
    {
        _homeArrowVisible = false;
        if (_generator is null) return;
        var home = toPx(0, 0);
        var mapRect = new Rect2(Padding, Padding,
            Size.X - 2 * Padding, Size.Y - 2 * Padding);
        if (mapRect.HasPoint(home)) return;
        // The landmark itself is on the map; the arrow only points to it
        // from off-screen, so a wandering spider can always find home.
        var centre = mapRect.GetCenter();
        var direction = (home - centre).Normalized();
        var edge = centre + direction *
            (Math.Abs(direction.X) > Math.Abs(direction.Y)
                ? mapRect.Size.X * 0.5f / Math.Max(0.2f, Math.Abs(direction.X))
                : mapRect.Size.Y * 0.5f / Math.Max(0.2f, Math.Abs(direction.Y)));
        edge = new Vector2(
            Mathf.Clamp(edge.X, Padding + 14, Size.X - Padding - 14),
            Mathf.Clamp(edge.Y, Padding + 14, Size.Y - Padding - 14));
        var colour = new Color(0.96f, 0.78f, 0.31f, 0.95f);
        var angle = Mathf.Atan2(direction.Y, direction.X);
        var tip = edge;
        var left = edge - new Vector2(Mathf.Cos(angle - 0.5f), Mathf.Sin(angle - 0.5f)) * 14f;
        var right = edge - new Vector2(Mathf.Cos(angle + 0.5f), Mathf.Sin(angle + 0.5f)) * 14f;
        DrawColoredPolygon(new[] { tip, left, right }, colour);
        _homeArrowDirection = new Vector3(direction.X, 0, direction.Y);
        var viewCentre = _viewCentre ?? _player;
        _homeArrowDistance = Math.Sqrt(
            viewCentre.X * viewCentre.X + viewCentre.Z * viewCentre.Z);
        DrawString(font, edge + new Vector2(14, 4),
            $"Home · {_homeArrowDistance:0.0} km",
            HorizontalAlignment.Left, -1, 12, colour);
        _homeArrowVisible = true;
    }

    private void DrawWorkers(Func<double, double, Vector2> toPx,
        long minChunkX, long maxChunkX, long minChunkZ, long maxChunkZ)
    {
        LastDrawnWorkers = 0;
        foreach (var worker in _workers)
        {
            var chunk = ChunkCoordinate.OfWorldPoint(worker.Position.X, worker.Position.Z);
            if (chunk.X < minChunkX || chunk.X > maxChunkX ||
                chunk.Z < minChunkZ || chunk.Z > maxChunkZ) continue;
            var point = toPx(worker.Position.X, worker.Position.Z);
            var bodyColour = worker.Summoned
                ? new Color(0.72f, 0.87f, 0.98f)
                : new Color(0.96f, 0.65f, 0.24f);
            DrawCircle(point, 6.4f, new Color(0.09f, 0.12f, 0.08f, 0.9f));
            DrawCircle(point, 4.2f, bodyColour);
            LastDrawnWorkers++;
            if (worker.Destination is { } destination)
            {
                var target = toPx(destination.X, destination.Z);
                DrawLine(point, target,
                    new Color(0.96f, 0.65f, 0.24f, 0.55f), 1.6f);
                DrawCircle(target, 3.4f, new Color(0.09f, 0.12f, 0.08f, 0.8f));
                DrawCircle(target, 2.2f, bodyColour);
            }
        }
    }

    private void DrawMarkers(Func<double, double, Vector2> toPx,
        long minX, long maxX, long minZ, long maxZ, Font font)
    {
        LastDrawnMarkers = 0;
        _markerPixels.Clear();
        foreach (var marker in _markers)
        {
            var chunk = ChunkCoordinate.OfWorldPoint(marker.Position.X, marker.Position.Z);
            if (chunk.X < minX || chunk.X > maxX || chunk.Z < minZ || chunk.Z > maxZ) continue;
            var point = toPx(marker.Position.X, marker.Position.Z);
            _markerPixels[marker.Id] = point;
            LastDrawnMarkers++;
            if (marker.Kind == WorldMarkerKind.Food)
            {
                DrawCircle(point, 7.5f, new Color(0.09f, 0.12f, 0.08f, 0.95f));
                DrawCircle(point, 5.2f, new Color(0.96f, 0.65f, 0.24f));
                DrawCircle(point + new Vector2(-1.5f, -1.5f), 1.5f,
                    new Color(1f, 0.9f, 0.65f));
            }
            else
            {
                var colour = marker.Kind == WorldMarkerKind.Home
                    ? new Color(0.36f, 0.77f, 0.91f)
                    : new Color(0.78f, 0.86f, 0.94f);
                DrawColoredPolygon(new[]
                {
                    point + new Vector2(0, -7), point + new Vector2(7, 0),
                    point + new Vector2(0, 7), point + new Vector2(-7, 0)
                }, colour);
            }
            var label = marker.Kind == WorldMarkerKind.Food
                ? (marker.Label.Length > 0 ? marker.Label : "food")
                : marker.Label;
            if (!string.IsNullOrWhiteSpace(label) && label.Length <= 22)
                DrawString(font, point + new Vector2(10, 4), label,
                    HorizontalAlignment.Left, -1, 11, new Color(0.97f, 0.93f, 0.76f));
        }
    }

    public Rect2 MarkerGlobalRect(string markerId) =>
        _markerPixels.TryGetValue(markerId, out var point)
            ? new Rect2(GlobalPosition + point - new Vector2(16, 16), new Vector2(32, 32))
            : new Rect2();

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                _dragging = true;
                _pannedSincePress = false;
                _pressPosition = press.Position;
                AcceptEvent();
                return;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                _dragging = false;
                if (_pannedSincePress) { AcceptEvent(); return; }
                HandleClick(_pressPosition);
                AcceptEvent();
                return;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Zoom(1.3f);
                AcceptEvent();
                return;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Zoom(1f / 1.3f);
                AcceptEvent();
                return;
            case InputEventMouseMotion motion:
                if (!_dragging) return;
                var delta = motion.Relative;
                if (!_pannedSincePress &&
                    _pressPosition.DistanceTo(_pressPosition + delta) >= DragThresholdPixels)
                    _pannedSincePress = true;
                if (_pannedSincePress) Pan(delta);
                AcceptEvent();
                return;
        }
    }

    /// <summary>Press-release without a drag: select a marker when one is
    /// close, otherwise drop a note marker on the clicked ground.</summary>
    private void HandleClick(Vector2 position)
    {
        if (_cellPixels is not { } cellPixels || cellPixels <= 0) return;
        string? best = null;
        var bestDistance = 16.0f;
        foreach (var (id, point) in _markerPixels)
        {
            var distance = point.DistanceTo(position);
            if (distance > bestDistance) continue;
            best = id;
            bestDistance = distance;
        }
        if (best is not null)
        {
            MarkerSelected?.Invoke(best);
            return;
        }
        var centre = _viewCentre ?? _player;
        var columns = Mathf.FloorToInt((Size.X - 2 * Padding) / cellPixels);
        var rows = Mathf.FloorToInt((Size.Y - 2 * Padding) / cellPixels);
        var worldMinX = centre.X - columns * WorldChunk.Size * 0.5;
        var worldMinZ = centre.Z - rows * WorldChunk.Size * 0.5;
        var worldX = worldMinX + (position.X - Padding) / cellPixels * WorldChunk.Size;
        var worldZ = worldMinZ + (position.Y - Padding) / cellPixels * WorldChunk.Size;
        NotePlacedRequested?.Invoke(new Vec3(worldX, 0, worldZ));
    }

    private void DrawRivers(Func<double, double, Vector2> toPx,
        double worldMinX, double worldMinZ, double spanX, double spanZ,
        long minChunkX, long minChunkZ, long maxChunkX, long maxChunkZ)
    {
        if (_generator is null || _explored is null) return;
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
                var riverChunk = ChunkCoordinate.OfWorldPoint(x, centre);
                if (northZ < mapMinZ || southZ > mapMaxZ - 1.5 ||
                    riverChunk.X < minChunkX || riverChunk.X > maxChunkX ||
                    !_explored.Contains(riverChunk))
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
