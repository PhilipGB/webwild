using Godot;
using Webwild.Core;

namespace Webwild.Game;

/// <summary>
/// Godot adapter for the deterministic garden simulation. It owns presentation,
/// input translation and save wiring; every gameplay rule lives in Webwild.Core.
/// </summary>
public partial class GardenClearing : Node3D
{
    public event Action? QuitToMenuRequested;
    public event Action<SettingsData>? SettingsChanged;

    public SettingsData Settings { get; private set; } = new();
    public GameSimulation Simulation => _sim;
    public SpiderPlayer Player => _player!;
    public IReadOnlyList<GameplayEvent> EventLog => _eventLog;

    /// <summary>Procedural chunk streaming state for probes and diagnostics.</summary>
    public ChunkStreamer? ChunkStream => _chunks;

    private GameSimulation _sim = null!;
    private Tuning _baselineTuning = null!;
    private WorldSaveService _saves = null!;
    private SpiderPlayer _player = null!;
    private GameUI _ui = null!;
    private AudioDirector _audio = null!;
    private SettingsDialog _settingsDialog = null!;
    private CanvasLayer _settingsLayer = null!;

    private Node3D _websRoot = null!;
    private Node3D _insectsRoot = null!;
    private Node3D _workersRoot = null!;
    private Node3D _previewRoot = null!;
    private Node3D? _preview;
    private CpuParticles3D _windParticles = null!;
    private Label3D _nurseryLabel = null!;
    private Label3D _climbLabel = null!;
    private Label3D _farBankTrailLabel = null!;
    private StaticBody3D? _brambleGate;
    private MeshInstance3D? _brambleGateMesh;
    private bool _reedwaterPathToastShown;
    private string? _zone;
    private string? _zoneBiome;
    private readonly ExplorationRecord _explored = new();
    private ChunkCoordinate? _lastExploredChunk;
    private readonly Dictionary<string, FoodSourceDefinition> _foodDefinitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FoodView> _foodViews = new(StringComparer.Ordinal);
    private readonly HashSet<ChunkCoordinate> _foodLoadedChunks = new();
    private readonly HashSet<ChunkCoordinate> _foodKnownChunks = new();

    private string _activeRegionId = RegionIds.GardenClearing;
    private Node3D _gardenWorld = null!;
    private Node3D _gardenAnchors = null!;
    private Node3D _gardenPassage = null!;
    private Node3D _regionsHost = null!;
    private Node3D _chunkHost = null!;
    private ChunkStreamer? _chunks;
    /// <summary>Seed of the live world; restored from the save manifest and
    /// regenerated per new garden. Probe runs force the historical fixed seed.</summary>
    private int _worldSeed = WorldSaveService.NewGameSeed(deterministic: false);
    /// <summary>Presentation-only offset: logical world = engine position + this.
    /// Rebased near the player so float32 physics never leaves comfortable
    /// precision, while saves and simulation keep stable logical coordinates.</summary>
    private Vec3 _originOffset = Vec3.Zero;
    private const double RebaseThreshold = 16_000;
    private const double RebaseGrid = 64;
    private readonly Dictionary<Node3D, string> _hostRegions = new();
    private readonly Dictionary<string, Node3D> _regionRoots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _regionAnchorRoots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _viewHosts = new(StringComparer.Ordinal);
    private Vector3 _regionSafePosition;
    private bool _waterToastShown;

    private readonly Dictionary<string, AnchorNode> _anchors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WebVisual> _webViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FlyView> _insectViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkerView> _workerViews = new(StringComparer.Ordinal);
    private readonly List<GameplayEvent> _eventLog = new();

    private WebType? _tool;
    private string? _anchorA;
    private string? _pendingAnchorB;
    private AnchorNode? _hoverAnchor;
    private string _previewSignature = "";
    private double _accumulator;
    private double _autosaveTimer;
    private double _hudTimer;
    private double _masteryCheckTimer;
    private bool _victoryShown;
    private bool _started;
    private bool _probeMode;
    private bool _bridgeCrossToastShown;
    private bool _fallToastShown;
    private bool _interiorBuilt;
    private string _lastContext = "";

    /// <summary>Set by the scene-validation entry point so it can inspect the
    /// instantiated tree without building gameplay UI or entities.</summary>
    public bool ValidationOnly { get; set; }

    public void Configure(SettingsData settings, WorldSaveService saves, bool probeMode = false)
    {
        Settings = settings;
        Settings.Normalise();
        _saves = saves;
        _probeMode = probeMode;
    }

    public override void _Ready()
    {
        if (ValidationOnly) return;
        _saves ??= GameData.CreateWorldSaveService();
        BuildInterior();
        GameData.ApplyUiScale(Settings);
        _ui.SetTutorialPrompts(Settings.TutorialPrompts);
    }

    private void BuildInterior()
    {
        if (_interiorBuilt) return;
        _interiorBuilt = true;

        // The persistent garden is authored in GardenWorld.tscn and the anchor
        // sockets, player and runtime roots are authored in GardenClearing.tscn.
        // Missing or drifted content is a startup error, never a procedural
        // fallback that would hide an incomplete scene.
        SceneContentValidator.ValidateOrThrow(this);

        _gardenWorld = GetRequired<Node3D>("World");
        _gardenAnchors = GetRequired<Node3D>("Anchors");
        _gardenPassage = GetRequired<Node3D>("Passage");
        _regionsHost = GetRequired<Node3D>("Regions");
        _websRoot = GetRequired<Node3D>("Webs");
        _insectsRoot = GetRequired<Node3D>("Insects");
        _workersRoot = GetRequired<Node3D>("Workers");
        _previewRoot = GetRequired<Node3D>("WebPreview");

        BindAllAnchors();
        BuildMarkers();
        BuildWind();
        BindPlayer();
        BuildInterface();

        // The procedural world is a runtime-only sibling of the authored world.
        _chunkHost = new Node3D { Name = "ProceduralChunks" };
        AddChild(_chunkHost);

        // From P2 the world is seamless: Reedwater Bank is instantiated at its
        // constant world origin and stays resident, so the connecting meadow is
        // ordinary walkable ground rather than a teleport destination.
        var reedwater = EnsureRegionScene(RegionIds.ReedwaterBank);
        reedwater.Visible = true;
        SetAnchorRegionActive(_regionAnchorRoots[RegionIds.ReedwaterBank], true);
        _brambleGate = reedwater.GetNodeOrNull<StaticBody3D>(
            "World/Boundaries/WestBankWall/BrambleGate");
        _brambleGateMesh = reedwater.GetNodeOrNull<MeshInstance3D>(
            "World/Boundaries/WestBankWall/BrambleGate/BrambleGateMesh");
    }

    private T GetRequired<T>(string path) where T : Node =>
        GetNodeOrNull<T>(path) ?? throw new InvalidOperationException(
            $"GardenClearing.tscn is missing mandatory node '{path}' ({typeof(T).Name}).");

    private void BindAllAnchors()
    {
        _anchors.Clear();
        // External landmark scenes are part of the continuous world; they must
        // exist before their anchor sockets can be bound.
        foreach (var entry in RegionSceneCatalog.All.Where(entry => !entry.IsBuiltIntoSession))
            EnsureRegionScene(entry.RegionId);
        BindAnchorRoot(_gardenAnchors, RegionIds.GardenClearing);
        foreach (var (regionId, root) in _regionAnchorRoots)
            BindAnchorRoot(root, regionId);
    }

    private void BindAnchorRoot(Node3D root, string regionId)
    {
        var region = RegionCatalog.GetRequired(regionId);
        var definitions = region.Anchors.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        var before = _anchors.Count;
        foreach (var child in root.GetChildren())
        {
            if (child is not AnchorNode node) continue;
            if (string.IsNullOrWhiteSpace(node.AnchorId) || !definitions.TryGetValue(node.AnchorId, out var definition))
                throw new InvalidOperationException(
                    $"Region '{regionId}' socket '{node.Name}' has unknown anchor_id '{node.AnchorId}'.");
            if (_anchors.ContainsKey(node.AnchorId))
                throw new InvalidOperationException($"Duplicate authored anchor socket '{node.AnchorId}'.");
            node.Configure(definition);
            _anchors[node.AnchorId] = node;
        }
        if (_anchors.Count - before != definitions.Count)
            throw new InvalidOperationException(
                $"Region '{regionId}' authors {_anchors.Count - before} anchor sockets but the simulation defines {definitions.Count}.");
    }

    private void BuildMarkers()
    {
        _nurseryLabel = new Label3D
        {
            Text = "Leaf Nursery · E",
            FontSize = 34,
            PixelSize = 0.007f,
            Modulate = UiKit.Cream,
            OutlineModulate = UiKit.Pine,
            OutlineSize = 9,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = ToVector3(WorldLayout.NurseryPosition) + new Vector3(0, 2.25f, 0),
            Visible = false
        };
        AddChild(_nurseryLabel);

        _climbLabel = new Label3D
        {
            Text = "Hold W to climb",
            FontSize = 30,
            PixelSize = 0.006f,
            Modulate = UiKit.Cream,
            OutlineModulate = UiKit.Pine,
            OutlineSize = 8,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = new Vector3(-1.5f, 2.0f, 9.0f),
            Visible = false
        };
        AddChild(_climbLabel);

        _farBankTrailLabel = new Label3D
        {
            Text = "E · Reed trail to the outpost",
            FontSize = 28,
            PixelSize = 0.006f,
            Modulate = UiKit.Gold,
            OutlineModulate = UiKit.Pine,
            OutlineSize = 8,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = new Vector3(86.4f, 1.7f, -2.8f),
            Visible = false
        };
        AddChild(_farBankTrailLabel);
    }

    private void BuildWind()
    {
        var leafMesh = new BoxMesh { Size = new Vector3(0.09f, 0.02f, 0.2f) };
        leafMesh.Material = new StandardMaterial3D
        {
            AlbedoColor = new Color("B9D98C"),
            Roughness = 0.9f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        _windParticles = new CpuParticles3D
        {
            Name = "WindLeaves",
            Amount = 54,
            Lifetime = 2.4,
            Emitting = false,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(18, 1.5f, 13),
            Direction = new Vector3(1, 0.15f, 0),
            Spread = 26,
            Gravity = new Vector3(0, -0.8f, 0),
            InitialVelocityMin = 3.5f,
            InitialVelocityMax = 8f,
            ScaleAmountMin = 0.55f,
            ScaleAmountMax = 1.2f,
            Position = new Vector3(0, 1.8f, 0),
            Mesh = leafMesh
        };
        AddChild(_windParticles);
    }

    private void BindPlayer()
    {
        _player = GetRequired<SpiderPlayer>("Player");
        _player.InputEnabled = false;
    }

    private void BuildInterface()
    {
        _ui = new GameUI { Name = "GameUI" };
        GetRequired<Node>("UI").AddChild(_ui);
        _ui.BuildToolRequested += tool => SetTool(tool);
        _ui.CancelRequested += ClearBuild;
        _ui.InteractRequested += TryInteract;
        _ui.DismantleRequested += TryDismantleNearest;
        _ui.PauseRequested += TogglePause;
        _ui.SaveRequested += () => SaveGame(true);
        _ui.QuitToMenuRequested += SaveAndQuit;
        _ui.SettingsRequested += OpenSettings;
        _ui.NewGameRequested += RestartNewGame;
        _ui.HatchRequested += TryHatch;
        _ui.RecoveryRequested += TryRecovery;
        _ui.ContinuePlayingRequested += () => _ui.SetStatus(_sim.GetActiveRegionHint());
        _ui.BuildConfirmed += ConfirmBuild;
        _ui.BuildCancelled += ClearBuild;
        _ui.MapRequested += OpenMap;
        _ui.ReturnHomeRequested += ReturnHome;
        _ui.SummonRequested += SummonHelpers;

        _audio = new AudioDirector { Name = "AudioDirector" };
        AddChild(_audio);

        _settingsLayer = new CanvasLayer { Name = "SettingsLayer", Layer = 30 };
        AddChild(_settingsLayer);
        _settingsDialog = new SettingsDialog();
        _settingsLayer.AddChild(_settingsDialog);
        _settingsDialog.Configure(Settings);
        _settingsDialog.Changed += OnSettingsChanged;
    }

    public void StartNew()
    {
        BuildInterior();
        _started = true;
        _eventLog.Clear();
        _victoryShown = false;
        _bridgeCrossToastShown = false;
        _autosaveTimer = 0;
        _baselineTuning = GameData.LoadTuning();
        var tuning = _baselineTuning.Clone();
        GameData.ApplyDifficulty(tuning, _baselineTuning, Settings.LowThreat);
        // One seed per world: it drives both the colony simulation and the
        // procedural terrain, and the manifest persists it for every reload.
        _worldSeed = WorldSaveService.NewGameSeed(_probeMode);
        tuning.Seed = _worldSeed;
        _sim = new GameSimulation(tuning, GameData.LoadMissions());
        _originOffset = Vec3.Zero;
        UpdateWorldTransforms();
        RebuildChunkStream();
        ActivateRegion(RegionIds.GardenClearing);
        ResetPresentation();
        _player.GlobalPosition = ToWorld(RegionIds.GardenClearing, WorldLayout.PlayerSpawn);
        _regionSafePosition = _player.GlobalPosition;
        _player.FallResetPosition = ToWorld(RegionIds.GardenClearing, WorldLayout.PlayerSpawn);
        _player.ResetCamera();
        _player.InputEnabled = true;
        _explored.Clear();
        _lastExploredChunk = null;
        TrackExploration();
        _ui.ShowToast("Welcome to the garden. Use WASD to explore.", UiKit.Pine, 4.5f);
        _ui.SetStatus(_sim.GetActiveRegionHint());
        RefreshAll();
        ProcessEvents();
    }

    public void StartFromSave(SaveGameData data)
    {
        BuildInterior();
        _started = true;
        _eventLog.Clear();
        _victoryShown = data.Counters.Victory;
        _bridgeCrossToastShown = false;
        _autosaveTimer = 0;
        if (data.Settings is not null)
        {
            CopySettings(data.Settings, Settings);
            Settings.Normalise();
            GameData.ApplyUiScale(Settings);
            _ui.SetTutorialPrompts(Settings.TutorialPrompts);
            _settingsDialog.Configure(Settings);
            SettingsChanged?.Invoke(Settings);
        }
        var tuning = GameData.LoadTuning();
        _baselineTuning = tuning.Clone();
        // The saved seed is the world seed contract: terrain must regenerate
        // byte-identically from it, in every session.
        _worldSeed = data.Seed > 0 ? data.Seed : WorldSaveService.NewGameSeed(deterministic: true);
        tuning.Seed = _worldSeed;
        GameData.ApplyDifficulty(tuning, _baselineTuning, Settings.LowThreat);
        _sim = GameSimulation.FromSave(data, tuning, GameData.LoadMissions());
        var region = _sim.ActiveRegion;
        var savedPlayer = _sim.GetRegionPlayerPosition(region.Id);
        var savedWorld = LandmarkCatalog.ToWorld(region.Id, savedPlayer.Position).Absolute;
        _originOffset = SnapOrigin(savedWorld);
        UpdateWorldTransforms();
        RebuildChunkStream();
        ActivateRegion(_sim.ActiveRegionId);
        ResetPresentation();
        var spawnWorld = ClampSavedPlayerPosition(savedWorld, region);
        // Rivers are regenerated from the seed, so a save from an earlier build
        // can now sit in deep water. Recover to the nearest dry bank and update
        // the remembered position so the next save is already safe.
        if (_chunks is not null)
        {
            var river = _chunks.Generator.RiverAt(spawnWorld.X, spawnWorld.Z);
            if (river.IsHazard && spawnWorld.Y < river.WaterLevel + 0.3)
            {
                var bank = _chunks.Generator.Rivers.SafeBankNear(spawnWorld.X, spawnWorld.Z);
                spawnWorld = new Vec3(bank.X, bank.Y + 0.25, bank.Z);
                _sim.SetRegionPlayerPosition(region.Id,
                    LandmarkCatalog.ToLocal(region.Id, spawnWorld),
                    savedPlayer.RotationY);
                _ui.ShowToast("You scrambled up the riverbank.", UiKit.Coral, 3.5f);
            }
        }
        _player.GlobalPosition = ToEngine(spawnWorld);
        _regionSafePosition = _player.GlobalPosition;
        _player.FallResetPosition = ToWorld(region.Id, region.PlayerSpawn);
        _player.CameraPivot.Rotation = new Vector3(0, (float)savedPlayer.RotationY, 0);
        _player.InputEnabled = true;
        _explored.Restore(data.ExploredChunks);
        _lastExploredChunk = null;
        TrackExploration();
        _ui.ShowToast($"Welcome back to {region.DisplayName}.", UiKit.Pine, 4.0f);
        _ui.SetStatus(_sim.GetActiveRegionHint());
        RefreshAll();
        ProcessEvents();
    }

    private static Vec3 ClampSavedPlayerPosition(Vec3 worldPosition, RegionDefinition region)
    {
        if (!worldPosition.IsFinite || worldPosition.Y < -20)
            return LandmarkCatalog.ToWorld(region.Id, region.PlayerSpawn).Absolute;
        return worldPosition;
    }

    /// <summary>Start streaming a fresh generated baseline for this world seed.</summary>
    private void RebuildChunkStream()
    {
        ClearFoodViews();
        if (_chunks is not null)
        {
            foreach (var node in _chunks.Nodes.Values) node.QueueFree();
            _chunks = null;
        }
        _chunks = new ChunkStreamer(new WorldGenerator(_worldSeed), _chunkHost);
        AddLandmarkAprons();
    }

    /// <summary>
    /// The authored Reedwater footprint is chunk-aligned, but its box-ground
    /// skirts are uniformly lit and met procedural grass with a hard tint line.
    /// Cover each skirt with the same per-quad colours the generator uses and
    /// hide the box tops; collision remains authored. The apron lives under the
    /// streaming host so origin rebasing moves it with the rest of the world.
    /// </summary>
    private void AddLandmarkAprons()
    {
        var reedwater = _regionRoots.TryGetValue(RegionIds.ReedwaterBank, out var root) ? root : null;
        if (reedwater is null) return;
        var skirtMeshes = new[]
        {
            "World/Terrain/LandingGate/LandingGateMesh",
            "World/Terrain/BankSkirtWest/BankSkirtWestMesh",
            "World/Terrain/BankSkirtEast/BankSkirtEastMesh",
            "World/Terrain/BankSkirtNorth/BankSkirtNorthMesh",
            "World/Terrain/BankSkirtSouth/BankSkirtSouthMesh"
        };
        foreach (var path in skirtMeshes)
            if (reedwater.GetNodeOrNull<MeshInstance3D>(path) is { } mesh) mesh.Visible = false;

        var rects = new[]
        {
            new WorldRect(72, -16.6, 76.4, 16.6),
            new WorldRect(115.6, -16.6, 120, 16.6),
            new WorldRect(72, -24, 120, -16.6),
            new WorldRect(72, 16.6, 120, 24),
            new WorldRect(76.4, 4, 77, 8)
        };
        for (var i = 0; i < rects.Length; i++)
            _chunkHost.AddChild(ChunkTerrainBuilder.BuildApron(
                _worldSeed, rects[i], $"ReedwaterApron{i}"));
    }

    /// <summary>
    /// Bind the colony's story frame to a landmark. In the seamless world this
    /// never rearranges the scene: every landmark stays visible, collidable and
    /// streaming-aware. Activation only rebinds labels, anchors and mission
    /// context as the player physically crosses a landmark boundary.
    /// </summary>
    private void ActivateRegion(string regionId)
    {
        var region = RegionCatalog.GetRequired(regionId);
        _activeRegionId = regionId;
        _gardenWorld.Visible = true;
        _gardenAnchors.Visible = true;
        _gardenPassage.Visible = true;
        SetAnchorRegionActive(_gardenAnchors, true);
        foreach (var (_, root) in _regionAnchorRoots)
            SetAnchorRegionActive(root, true);
        UpdateRegionLabels(region);
        _waterToastShown = false;
    }

    private Node3D EnsureRegionScene(string regionId)
    {
        if (_regionRoots.TryGetValue(regionId, out var cached)) return cached;
        var entry = RegionSceneCatalog.Get(regionId);
        if (entry is null || entry.IsBuiltIntoSession)
            throw new InvalidOperationException($"No external scene is registered for region '{regionId}'.");
        var packed = GD.Load<PackedScene>(entry.ScenePath);
        if (packed is null)
            throw new InvalidOperationException(
                $"{entry.ScenePath} is missing. A mandatory region scene is never replaced procedurally.");
        var root = packed.Instantiate<Node3D>();
        if (root is null)
            throw new InvalidOperationException($"{entry.ScenePath} did not instantiate.");
        root.Name = regionId;
        _regionsHost.AddChild(root);
        var anchors = root.GetNodeOrNull<Node3D>("Anchors")
            ?? throw new InvalidOperationException($"{entry.ScenePath} is missing mandatory node 'Anchors'.");
        _regionRoots[regionId] = root;
        _regionAnchorRoots[regionId] = anchors;
        if (regionId == RegionIds.ReedwaterBank)
            SceneContentValidator.ValidateReedwaterOrThrow(root);
        // Authored landmarks are embedded at their constant world origins, so
        // their scene-local gameplay coordinates never leak into world space.
        // Validation runs first so it sees the authored local frame.
        root.Position = ToVector3(LandmarkCatalog.OriginOf(regionId));
        return root;
    }

    private static void SetAnchorRegionActive(Node3D root, bool active)
    {
        foreach (var node in root.GetChildren().OfType<AnchorNode>())
        {
            node.CollisionLayer = active ? 4u : 0u;
            node.ProcessMode = active ? Node.ProcessModeEnum.Inherit : Node.ProcessModeEnum.Disabled;
        }
    }

    private void UpdateRegionLabels(RegionDefinition region)
    {
        _nurseryLabel.Text = $"{region.NurseryLabel} · E";
        _nurseryLabel.Position = ToWorld(region.Id, region.NurseryPosition) + new Vector3(0, 2.25f, 0);
        _climbLabel.Visible = false;
        _farBankTrailLabel.Visible = region.Id == RegionIds.ReedwaterBank &&
            _sim?.RegionalObjectives.CompletedObjectives.Contains("R03") == true;
    }

    private void ResetPresentation()
    {
        foreach (var view in _webViews.Values) view.QueueFree();
        _webViews.Clear();
        foreach (var view in _insectViews.Values) view.QueueFree();
        _insectViews.Clear();
        foreach (var view in _workerViews.Values) view.QueueFree();
        _workerViews.Clear();
        _ui.CloseModal();
        ClearBuild();
        _ui.UpdateHud(_sim, Settings);
    }

    private void RefreshAll()
    {
        SyncWebViews();
        SyncInsectViews();
        SyncWorkerViews();
        _ui.UpdateHud(_sim, Settings);
    }

    public override void _Process(double delta)
    {
        if (!_started || _sim is null) return;
        var paused = _ui.PausesSimulation || _settingsDialog.Visible;
        if (_player is not null) _player.PhysicsPaused = paused || _ui.BlocksWorldInput;
        if (!paused)
        {
            var step = Math.Max(0.05, _sim.Tuning.FixedSimulationTickSeconds);
            _accumulator += delta;
            var guard = 0;
            while (_accumulator >= step && guard++ < 20)
            {
                _sim.Tick(step);
                _accumulator -= step;
            }
            ProcessEvents();
        }

        var dt = (float)delta;
        // One continuous world: stream around the player wherever they are,
        // even while a paused menu is open, so the load ring never lags a
        // teleport or a save restore. Rebase first so logical streaming stays
        // independent of float precision.
        UpdateOriginRebase();
        _chunks?.Update(ToVector3(PlayerWorld), true);
        SyncFoodViews();
        UpdateLandmarkZone();
        TrackExploration();
        UpdatePlayerContext();
        UpdateViews(dt);
        UpdateSuggestions();
        UpdateContext();
        UpdatePreview();

        _masteryCheckTimer += delta;
        if (_masteryCheckTimer >= 0.2)
        {
            _masteryCheckTimer = 0;
            if (_activeRegionId == RegionIds.ReedwaterBank && _player is not null &&
                _player.IsOnFloor() && !_player.IsClimbing)
                _sim.TryAwardCleverCrossingOnArrival(PlayerLocal, true);
        }

        UpdateWaterHazard();

        if (_player is not null && _player.GlobalPosition.Y < -3.1f && !_fallToastShown)
        {
            // Only the authored pocket has a hazard dip; the procedural meadow
            // legitimately rolls below -3, so the toast must not fire there.
            var authored = Math.Abs(_player.GlobalPosition.X) <= 24f && Math.Abs(_player.GlobalPosition.Z) <= 24f;
            if (_activeRegionId != RegionIds.GardenClearing || authored)
            {
                _fallToastShown = true;
                _ui.ShowToast(_activeRegionId == RegionIds.GardenClearing
                    ? "Whoops! The ravine sends you back to the home leaf."
                    : "Whoops! The bank sends you back to your safe spot.", UiKit.Coral, 4f);
            }
        }
        else if (_player is not null && _player.GlobalPosition.Y > -1f)
        {
            _fallToastShown = false;
        }

        _hudTimer += delta;
        if (_hudTimer >= 0.1)
        {
            _hudTimer = 0;
            _ui.UpdateHud(_sim, Settings);
        }

        if (!paused && !_ui.BlocksWorldInput && _player is not null)
        {
            _player.InputEnabled = _tool is null;
            _autosaveTimer += delta;
            if (_autosaveTimer >= 30)
            {
                _autosaveTimer = 0;
                SaveGame(false, true);
            }
        }
        else if (_player is not null)
        {
            _player.InputEnabled = false;
        }
    }

    private void UpdatePlayerContext()
    {
        if (_player is null) return;
        if (_activeRegionId != RegionIds.GardenClearing)
        {
            _player.ClimbAvailable = false;
            return;
        }
        var position = _player.GlobalPosition;
        var dx = position.X - (float)WorldLayout.ClimbBase.X;
        var dz = position.Z - (float)WorldLayout.ClimbBase.Z;
        _player.ClimbAvailable = dx * dx + dz * dz < 3.2f * 3.2f;
        _player.ClimbPoint = new Vector3((float)WorldLayout.ClimbBase.X, 0, (float)WorldLayout.ClimbBase.Z);
    }

    /// <summary>
    /// Record the chunks the spider can currently see. A 3×3 window keeps the
    /// map readable for a walking trail without ballooning the save; the record
    /// is bounded and first-visit order is stable.
    /// </summary>
    private void TrackExploration()
    {
        if (_player is null) return;
        var world = PlayerWorld;
        var coordinate = ChunkCoordinate.OfWorldPoint(world.X, world.Z);
        if (_lastExploredChunk == coordinate) return;
        _lastExploredChunk = coordinate;
        _explored.VisitArea(coordinate, 1);
    }

    private void SummonHelpers()
    {
        if (_sim is null) return;
        if (_sim.TrySummonWorkers(PlayerWorld, _sim.ActiveRegionId, out var reason))
            _ui.ShowToast("Helpers are on their way. They will walk to you.", UiKit.TealDark, 4f);
        else
            _ui.ShowToast(reason, UiKit.Coral, 3.5f);
        _ui.RefreshOpenPanels(_sim);
    }

    private void OpenMap()
    {
        if (_sim is null || _player is null || _chunks is null) return;
        if (_ui.IsModalOpen) return;
        ClearBuild();
        _ui.OpenMap(_explored.Snapshot(), PlayerWorld, _chunks.Generator, _sim.VisitedRegions);
    }

    /// <summary>
    /// Return the colony to the garden spawn from anywhere. This is a rescue
    /// and navigation aid, not a story shortcut: missions, webs and resources
    /// are untouched, and the trip is saved like any other walk.
    /// </summary>
    public void ReturnHome()
    {
        if (_sim is null || _player is null) return;
        if (_sim.ActiveRegionId != RegionIds.GardenClearing)
            _sim.EnterLandmark(RegionIds.GardenClearing);
        var spawn = LandmarkCatalog.ToWorld(RegionIds.GardenClearing,
            WorldLayout.PlayerSpawn).Absolute;
        ApplyOrigin(SnapOrigin(spawn));
        ActivateRegion(RegionIds.GardenClearing);
        ResetPresentation();
        _player.GlobalPosition = ToEngine(spawn);
        _player.Velocity = Vector3.Zero;
        _player.ResetCamera();
        _regionSafePosition = _player.GlobalPosition;
        _player.FallResetPosition = _player.GlobalPosition;
        _zone = LandmarkCatalog.Garden;
        _zoneBiome = null;
        _waterToastShown = false;
        _explored.VisitArea(ChunkCoordinate.OfWorldPoint(spawn.X, spawn.Z), 1);
        _ui.SetExploration(null, null);
        _ui.CloseModal();
        _ui.UpdateHud(_sim, Settings);
        _ui.SetStatus(_sim.GetActiveRegionHint());
        _ui.ShowToast("You returned to the Garden Clearing.", UiKit.TealDark, 3.5f);
        RefreshAll();
        ProcessEvents();
        SaveGame(false, true);
    }


    /// <summary>
    /// Create/destroy wild food views as chunks stream and import each newly
    /// loaded chunk's persisted depletion state before its views appear. Food
    /// definitions regenerate from the seed; only depletion is stored.
    /// </summary>
    private void SyncFoodViews()
    {
        if (_sim is null || _chunks is null) return;
        foreach (var coordinate in _foodLoadedChunks
                     .Where(coordinate => !_chunks.Nodes.ContainsKey(coordinate)).ToList())
        {
            foreach (var source in _foodDefinitions.Values
                         .Where(source => source.Chunk == coordinate).ToList())
            {
                if (_foodViews.Remove(source.Id, out var view)) view.QueueFree();
                _foodDefinitions.Remove(source.Id);
            }
            _foodLoadedChunks.Remove(coordinate);
        }

        foreach (var coordinate in _chunks.Nodes.Keys)
        {
            if (!_foodLoadedChunks.Add(coordinate)) continue;
            var data = _chunks.DataFor(coordinate);
            if (data is null) continue;
            var record = _saves.WorldStore.LoadChunkChanges(coordinate, out _);
            if (record is not null &&
                (record.WorldSeed == 0 || record.WorldSeed == _worldSeed))
                _sim.ImportFoodStates(coordinate, record.FoodSources);
            _foodKnownChunks.Add(coordinate);
            foreach (var source in data.FoodSources)
            {
                var view = new FoodView { Name = "Food_" + source.Id };
                view.Configure(source);
                _chunkHost.AddChild(view);
                _foodDefinitions[source.Id] = source;
                _foodViews[source.Id] = view;
            }
        }
    }

    private void UpdateFoodViews(float delta)
    {
        if (_sim is null) return;
        foreach (var (id, view) in _foodViews)
        {
            view.UpdateView(delta, Settings.ReducedMotion,
                _sim.IsFoodAvailable(id),
                _sim.FoodSecondsRemaining(id),
                Vector3DistanceToPlayer(view.GlobalPosition, 3.2f));
        }
    }

    private void ClearFoodViews()
    {
        foreach (var view in _foodViews.Values) view.QueueFree();
        _foodViews.Clear();
        _foodDefinitions.Clear();
        _foodLoadedChunks.Clear();
        _foodKnownChunks.Clear();
    }

    private FoodSourceDefinition? NearestFood(Vec3 world, double radius, bool availableOnly)
    {
        FoodSourceDefinition? best = null;
        var bestDistance = radius;
        foreach (var source in _foodDefinitions.Values)
        {
            var distance = Vec3.Distance(source.Position, world);
            if (distance > bestDistance) continue;
            if (availableOnly && _sim is not null && !_sim.IsFoodAvailable(source.Id)) continue;
            best = source;
            bestDistance = distance;
        }
        return best;
    }

    private string? FoodPrompt()
    {
        if (_sim is null) return null;
        var available = NearestFood(PlayerWorld, 3.2, availableOnly: true);
        if (available is not null) return $"E · Forage {available.Label}";
        var depleted = NearestFood(PlayerWorld, 3.2, availableOnly: false);
        if (depleted is not null)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(_sim.FoodSecondsRemaining(depleted.Id)));
            return $"{Cap(depleted.Label)} regrow in {seconds}s";
        }
        return null;
    }

    private bool TryForageNearest(Vec3 world, double radius,
        out FoodSourceDefinition? source, out int food, out string reason)
    {
        source = null;
        food = 0;
        reason = "No wild food nearby";
        if (_sim is null) return false;
        var best = NearestFood(world, radius, availableOnly: true);
        if (best is null)
        {
            var depleted = NearestFood(world, radius, availableOnly: false);
            if (depleted is not null)
            {
                var seconds = Math.Max(1, (int)Math.Ceiling(_sim.FoodSecondsRemaining(depleted.Id)));
                reason = $"{Cap(depleted.Label)} regrow in {seconds}s";
            }
            return false;
        }
        if (!_sim.TryHarvestFood(best, out food, out reason)) return false;
        source = best;
        return true;
    }

    private static string Cap(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// Persist only depleted food states, grouped into each chunk's small
    /// change record. Records for chunks whose state was never loaded this
    /// session are left untouched; known chunks with no remaining changes are
    /// deleted so baseline content stays implicit.
    /// </summary>
    private bool SaveFoodChunkChanges(out string error)
    {
        error = "";
        if (_sim is null) return true;
        var depleted = _sim.DepletedFoodStates()
            .GroupBy(state => state.Chunk)
            .ToDictionary(group => group.Key,
                group => group.Select(state => state.Save).ToList());
        var records = new Dictionary<ChunkCoordinate, ChunkChangeSave>();
        foreach (var coordinate in _saves.WorldStore.ListChangedChunks())
        {
            var existing = _saves.WorldStore.LoadChunkChanges(coordinate, out _);
            if (existing is not null) records[coordinate] = existing;
        }
        var now = DateTime.UtcNow.ToString("O");
        foreach (var (coordinate, states) in depleted)
        {
            if (!records.TryGetValue(coordinate, out var record))
            {
                record = new ChunkChangeSave { ChunkX = coordinate.X, ChunkZ = coordinate.Z };
                records[coordinate] = record;
            }
            record.FoodSources = states;
            record.WorldSeed = _worldSeed;
            record.UpdatedUtc = now;
        }
        foreach (var (coordinate, record) in records)
        {
            if (!_foodKnownChunks.Contains(coordinate) && !depleted.ContainsKey(coordinate))
                continue;
            if (!depleted.ContainsKey(coordinate) && record.FoodSources.Count > 0)
            {
                record.FoodSources.Clear();
                record.UpdatedUtc = now;
            }
            if (record.FoodSources.Count == 0 &&
                record.BuiltWebIds.Count == 0 && record.Markers.Count == 0)
            {
                _saves.WorldStore.DeleteChunkChanges(coordinate);
                continue;
            }
            var result = _saves.WorldStore.SaveChunkChanges(record);
            if (!result.Success)
            {
                error = result.Message + (string.IsNullOrWhiteSpace(result.Error) ? "" : " " + result.Error);
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Crossing a landmark boundary on foot activates that landmark's story
    /// frame, helpers and HUD. There is no dialog and no teleport: the spider
    /// is already standing inside the landmark when this fires.
    /// </summary>
    private void UpdateLandmarkZone()
    {
        if (_sim is null || _player is null) return;
        var zone = LandmarkCatalog.LandmarkAt(PlayerWorld);
        if (zone != _zone)
        {
            _zone = zone;
            _zoneBiome = null;
            if (zone is not null) _ui.SetExploration(null, null);
        }
        if (zone is null && _sim is not null)
        {
            var biome = ProbeBiomeAt(PlayerWorld);
            if (biome != _zoneBiome)
            {
                _zoneBiome = biome;
                _ui.SetExploration("Wild Meadows",
                    $"Exploring the wild meadows · {biome} biome");
            }
        }
        if (zone is not null && _sim is not null && zone != _sim.ActiveRegionId && _sim.EnterLandmark(zone))
        {
            ActivateRegion(zone);
            _ui.UpdateHud(_sim, Settings);
            _ui.SetStatus(_sim.GetActiveRegionHint());
            _ui.ShowToast($"You reached {_sim.ActiveRegion.DisplayName}.", UiKit.TealDark, 3.5f);
            RefreshAll();
            ProcessEvents();
            SaveGame(false, true);
        }
        UpdateBrambleGate();
        if (_nurseryLabel is not null && _sim is not null)
            _nurseryLabel.Position = ToWorld(_sim.ActiveRegionId,
                _sim.ActiveRegion.NurseryPosition) + new Vector3(0, 2.25f, 0);
    }

    /// <summary>
    /// The Reedwater path is guarded by a bramble gate until the garden story
    /// is complete. The barrier is physical: the zone change itself is never
    /// refused, so unlocking is expressed by the world, not by a dialog.
    /// </summary>
    private void UpdateBrambleGate()
    {
        if (_brambleGate is null) return;
        var open = _sim?.ReedwaterUnlocked == true;
        _brambleGate.CollisionLayer = open ? 0u : 1u;
        if (_brambleGateMesh is not null) _brambleGateMesh.Visible = !open;
        if (open && !_reedwaterPathToastShown)
        {
            _reedwaterPathToastShown = true;
            _ui?.ShowToast("The bramble gate east of the garden has opened. Follow the meadows to Reedwater Bank.",
                UiKit.TealDark, 5f);
        }
    }

    private void UpdateViews(float delta)
    {
        if (_sim is null) return;
        foreach (var view in _webViews.Values)
        {
            var state = _sim.GetWeb(view.WebId);
            if (state is not null)
            {
                view.UpdateState(state);
                view.SetNearby(Vector3DistanceToPlayer(view.GlobalPosition, 3.2f));
            }
        }
        foreach (var insect in _sim.Insects)
        {
            if (!_insectViews.TryGetValue(insect.Id, out var view)) continue;
            view.UpdateView(delta, Settings.ReducedMotion);
            view.SetPromptVisible(Vector3DistanceToPlayer(
                ToWorld(insect.Definition.Region, insect.Position), 3.2f));
        }
        foreach (var worker in _sim.Workers)
        {
            if (!_workerViews.TryGetValue(worker.Id, out var view)) continue;
            // Off-screen helpers keep simulating logically but skip animation
            // and rendering entirely until they are near again. Measure from
            // the logical world position: a culled view's own transform is
            // stale until it is snapped back.
            var workerEngine = ToEngine(worker.WorldPosition);
            var near = Vector3DistanceToPlayer(workerEngine, 90f);
            if (view.Visible != near)
            {
                view.Visible = near;
                if (near) view.SnapToWorld();
            }
            if (!near) continue;
            view.UpdateView(delta, Settings.ReducedMotion,
                Vector3DistanceToPlayer(workerEngine, 4f));
        }

        var nearbyNursery = Vector3DistanceToPlayer(NurseryInteractionPoint, 3.2f);
        var nurseryStep = _activeRegionId == RegionIds.GardenClearing &&
                          _sim.Missions.ActiveMissionId == "M02" && _sim.Workers.Count == 0;
        _nurseryLabel.Visible = nearbyNursery || nurseryStep;
        _climbLabel.Visible = _activeRegionId == RegionIds.GardenClearing &&
                              _player.ClimbAvailable && !_player.IsClimbing;

        UpdateFoodViews(delta);

        var wind = _sim.WeatherPhase != WeatherPhase.Calm;
        _windParticles.Emitting = wind && !Settings.ReducedMotion && _activeRegionId == RegionIds.GardenClearing;
    }

    private void UpdateSuggestions()
    {
        if (_sim is null) return;
        foreach (var anchor in _anchors.Values)
        {
            anchor.SetSuggested(false);
            if (_player is not null)
                anchor.SetPlayerDistance(_player.GlobalPosition.DistanceTo(anchor.GlobalPosition));
        }

        if (_activeRegionId == RegionIds.ReedwaterBank)
        {
            UpdateReedwaterSuggestions();
            return;
        }

        var mission = _sim.Missions.ActiveMissionId;
        if (mission == "M01" && _sim.CaptureWebsBuilt == 0)
        {
            Suggest(WorldLayout.TwigA);
            Suggest(WorldLayout.TwigB);
        }
        else if (mission == "M02" && _sim.BridgeWebsBuilt == 0)
        {
            Suggest(WorldLayout.GapWest);
            Suggest(WorldLayout.GapEast);
        }
        else if (mission == "M03" && _sim.WindCompleted &&
                 !_sim.RegionWebs.Any(w => w is { Type: WebType.Capture, Sheltered: true }))
        {
            Suggest(WorldLayout.ShelterA);
            Suggest(WorldLayout.ShelterB);
        }
    }

    private void UpdateReedwaterSuggestions()
    {
        var objective = _sim.RegionalObjectives.ActiveObjective?.Id;
        if (objective == "R01")
        {
            if (!HasRegionBridge(ReedwaterLayout.StartPost, ReedwaterLayout.IslandAPost))
            {
                Suggest(ReedwaterLayout.StartPost);
                Suggest(ReedwaterLayout.IslandAPost);
            }
            else
            {
                Suggest(ReedwaterLayout.IslandAPost);
                Suggest(ReedwaterLayout.IslandBPost);
            }
        }
        else if (objective == "R02")
        {
            // Point at the closest viable flyway instead of always sending the
            // player back to the reed bank.
            var player = _sim.ActiveRegionId == RegionIds.ReedwaterBank
                ? PlayerLocal
                : ReedwaterLayout.ArrivalPoint;
            var lilyDistance = Math.Min(
                Vec3.Distance(player, _sim.GetAnchor(ReedwaterLayout.MidA)?.Position ?? ReedwaterLayout.ArrivalPoint),
                Vec3.Distance(player, _sim.GetAnchor(ReedwaterLayout.MidB)?.Position ?? ReedwaterLayout.ArrivalPoint));
            var reedDistance = Math.Min(
                Vec3.Distance(player, _sim.GetAnchor(ReedwaterLayout.EasyA)?.Position ?? ReedwaterLayout.ArrivalPoint),
                Vec3.Distance(player, _sim.GetAnchor(ReedwaterLayout.EasyB)?.Position ?? ReedwaterLayout.ArrivalPoint));
            if (lilyDistance < reedDistance)
            {
                Suggest(ReedwaterLayout.MidA);
                Suggest(ReedwaterLayout.MidB);
            }
            else
            {
                Suggest(ReedwaterLayout.EasyA);
                Suggest(ReedwaterLayout.EasyB);
            }
        }
        else if (objective == "R03")
        {
            if (!HasRegionBridge(ReedwaterLayout.IslandBPost, ReedwaterLayout.FarPost))
            {
                Suggest(ReedwaterLayout.IslandBPost);
                Suggest(ReedwaterLayout.FarPost);
            }
            else
            {
                Suggest(ReedwaterLayout.FarA);
                Suggest(ReedwaterLayout.FarB);
            }
        }
    }

    private bool HasRegionBridge(string a, string b) =>
        _sim.RegionWebs.Any(web => web.Type == WebType.Bridge && web.Connects(a, b));

    private void Suggest(string anchorId)
    {
        if (_anchors.TryGetValue(anchorId, out var anchor)) anchor.SetSuggested(true);
    }

    private void UpdateContext()
    {
        if (_sim is null || _player is null) return;
        string text;
        if (_tool is not null)
        {
            text = _pendingAnchorB is not null
                ? "Choose Build to spend silk, or Cancel"
                : _anchorA is null
                    ? "Click a glowing anchor · right-click cancels"
                    : "Click the second anchor · right-click cancels";
        }
        else if (_sim.ActiveRegionId == RegionIds.ReedwaterBank &&
                 _sim.RegionalObjectives.CompletedObjectives.Contains("R03") &&
                 Vector3DistanceToPlayer(
                     ToWorld(RegionIds.ReedwaterBank, new Vec3(-9.6, 0, -2.8)), 2.6f))
        {
            text = "E · Follow the reed trail back to the outpost";
        }
        else if (Vector3DistanceToPlayer(NurseryInteractionPoint, 2.8f))
        {
            text = $"E · {_sim.ActiveRegion.NurseryLabel} — hatch a helper";
        }
        else if (_sim.Insects.Any(i => i.CanBeCollected &&
                 Vector3DistanceToPlayer(ToWorld(i.Definition.Region, i.Position), 3.2f)))
        {
            text = "E · Collect the caught insect";
        }
        else if (_sim.Webs.Any(w => w.Durability < w.MaxDurability && Vector3DistanceToPlayer(WebCenter(w), 3.2f)))
        {
            text = "E · Repair web (2 silk)";
        }
        else if (FoodPrompt() is { } foodText)
        {
            text = foodText;
        }
        else if (_activeRegionId == RegionIds.GardenClearing && _player.ClimbAvailable)
        {
            text = "Hold W to climb the tall stem";
        }
        else
        {
            text = "";
        }
        if (text == _lastContext) return;
        _lastContext = text;
        _ui.SetContext(text);
    }

    private void UpdatePreview()
    {
        if (_sim is null)
        {
            RemovePreview();
            return;
        }
        if (_tool is null)
        {
            RemovePreview();
            return;
        }
        var targetId = _pendingAnchorB
            ?? (_hoverAnchor is not null && _hoverAnchor.Definition.Id != _anchorA
                ? _hoverAnchor.Definition.Id
                : null);
        if (_anchorA is null || targetId is null)
        {
            RemovePreview();
            if (_anchorA is null)
            {
                _ui.SetStatus(_hoverAnchor is null
                    ? "Choose the first glowing anchor."
                    : $"Target: {_hoverAnchor.Definition.Label} · click to choose");
            }
            else
            {
                _ui.SetStatus("Now choose the second glowing anchor.");
            }
            return;
        }

        var result = _sim.Preview(_anchorA, targetId, _tool.Value);
        if (_pendingAnchorB is null)
        {
            foreach (var anchor in _anchors.Values)
            {
                if (anchor.Definition.Id == targetId)
                    anchor.SetHovered(true, result.IsValid);
                else if (anchor.IsHovered)
                    anchor.SetHovered(false);
            }
        }
        var signature = $"{_tool}:{_anchorA}:{targetId}:{result.IsValid}";
        if (signature != _previewSignature)
        {
            _previewSignature = signature;
            RemovePreview();
            var a = _sim.GetAnchor(_anchorA);
            var b = _sim.GetAnchor(targetId);
            if (a is not null && b is not null)
            {
                var approach = BridgeGeometry.ComputeApproachPoints(
                    a.Position, b.Position, a.BridgeGroundPoint, b.BridgeGroundPoint);
                _preview = WebVisual.CreatePreview(
                    _tool.Value,
                    ToVector3(a.Position),
                    ToVector3(b.Position),
                    result.IsValid,
                    ToVector3(approach.A),
                    ToVector3(approach.B));
                _previewRoot.AddChild(_preview);
            }
        }
        if (_pendingAnchorB is not null)
        {
            _ui.SetStatus($"Review {_sim.GetAnchor(_anchorA)?.Label} → " +
                          $"{_sim.GetAnchor(_pendingAnchorB)?.Label} · {result.SilkCost} silk");
        }
        else
        {
            _ui.SetStatus(result.IsValid
                ? $"Click to review · {result.SilkCost} silk"
                : result.Reason);
        }
    }

    private void RemovePreview()
    {
        if (_preview is not null)
        {
            _preview.QueueFree();
            _preview = null;
        }
        _previewSignature = "";
    }

    private void ProcessEvents()
    {
        if (_sim is null) return;
        foreach (var evt in _sim.DrainEvents())
        {
            _eventLog.Add(evt);
            if (_eventLog.Count > 600) _eventLog.RemoveRange(0, 150);
            HandleEvent(evt);
        }
    }

    private string InsectName(string? insectId) =>
        _sim?.GetInsect(insectId ?? "")?.Definition.Kind ?? "insect";

    private void HandleEvent(GameplayEvent evt)
    {
        // Events from an inactive region keep simulating, but only the active
        // region's local feedback should interrupt the player.
        if (evt.Region is not null && evt.Region != _activeRegionId &&
            evt.Type is not ("MissionCompleted" or "Victory" or "MasteryEarned" or
                "RegionalObjectiveCompleted" or "RegionalMasteryEarned" or
                "RegionalObjectivesCompleted" or "RegionUnlocked" or "SilkProduced" or "RecoveryUsed"))
            return;

        switch (evt.Type)
        {
            case "WebBuilt":
                _audio.Play("build");
                _ui.ShowToast(evt.WebType == "capture" ? "Capture web spun!" : "Silk bridge built!", UiKit.TealDark);
                SyncWebViews();
                break;
            case "WebDismantled":
                _audio.Play("cancel");
                SyncWebViews();
                break;
            case "WebRepaired":
                _audio.Play("repair");
                _ui.ShowToast(evt.Detail == "worker" ? "A helper repaired a web." : "Web repaired!", UiKit.TealDark);
                SyncWebViews();
                break;
            case "WebRestoredOrSheltered":
                SyncWebViews();
                break;
            case "InsectTrapped":
                _audio.Play("catch");
                _ui.ShowToast($"A {InsectName(evt.SubjectId)} is caught! Walk close and press E.",
                    UiKit.Gold, 4f);
                break;
            case "FoodCollected":
                _audio.Play("collect");
                var collector = evt.Detail ?? "player";
                var insectName = InsectName(evt.SubjectId);
                if (collector == "player")
                {
                    _ui.ShowToast($"You collected a {insectName} · +{evt.Amount} food",
                        UiKit.LeafDark, 3.2f);
                }
                else
                {
                    var collectorWorker = _sim.GetWorker(collector);
                    var collectorName = collectorWorker?.Name ?? "A helper";
                    _ui.ShowToast($"{collectorName} collected a {insectName} · +{evt.Amount} food",
                        UiKit.LeafDark, 3.4f);
                    if (collectorWorker is not null &&
                        _workerViews.TryGetValue(collectorWorker.Id, out var collectingView))
                        collectingView.FlashActivity();
                }
                break;
            case "FarBankDelivery":
                var deliveryWorker = _sim.GetWorker(evt.SubjectId ?? "");
                var deliveryName = deliveryWorker?.Name ?? "A helper";
                if (_ui.ToastText.StartsWith(deliveryName) && _ui.ToastText.Contains("collected a"))
                    _ui.AppendToast(" from the far bank");
                else
                    _ui.ShowToast($"{deliveryName} brought far-bank prey home!", UiKit.Violet, 3.6f);
                if (deliveryWorker is not null &&
                    _workerViews.TryGetValue(deliveryWorker.Id, out var deliveryView))
                    deliveryView.FlashActivity();
                break;
            case "SpiderHatched":
                _audio.Play("hatch");
                _ui.ShowToast($"Welcome, {evt.Detail ?? "little helper"}!", UiKit.Violet, 4f);
                SyncWorkerViews();
                break;
            case "HatchStarted":
                _audio.Play("click");
                _ui.ShowToast("A tiny spider is hatching…", UiKit.TealDark, 3f);
                break;
            case "WorkerUsedBridge":
                if (!_bridgeCrossToastShown)
                {
                    _bridgeCrossToastShown = true;
                    _ui.ShowToast("Your helper crossed the silk bridge!", UiKit.TealDark, 4f);
                }
                break;
            case "WindWarning":
                _audio.Play("wind");
                _ui.ShowToast("Wind is coming! Protect your web.", UiKit.Coral, 5f);
                break;
            case "WindEventCompleted":
                _audio.Play("wind");
                _ui.ShowToast("The wind has passed. Repair a damaged web with E.", UiKit.Coral, 5f);
                SyncWebViews();
                break;
            case "SilkProduced":
            case "InsectRespawned":
            case "GameStarted":
                break;
            case "RecoveryUsed":
                _audio.Play("click");
                _ui.ShowToast("Nursery rescue added 4 silk.", UiKit.TealDark, 4f);
                break;
            case "RegionUnlocked":
                _audio.Play("success");
                _ui.ShowToast("A new path has opened by the east hedge: Reedwater Bank!", UiKit.Violet, 6f);
                break;
            case "RegionSupply":
                _audio.Play("collect");
                _ui.ShowToast("Found a reed-bank supply cache: +24 silk, +6 food.", UiKit.Gold, 5f);
                break;
            case "RegionChanged":
                break;
            case "RegionalObjectiveCompleted":
                _audio.Play("success");
                if (evt.SubjectId != "R03")
                    _ui.ShowToast($"Reedwater objective {evt.SubjectId} complete!", UiKit.LeafDark, 4.5f);
                _ui.SetStatus(_sim.GetActiveRegionHint());
                if (evt.SubjectId == "R03") _farBankTrailLabel.Visible = true;
                SaveGame(false, true);
                break;
            case "RegionalMasteryEarned":
                _audio.Play("success");
                _ui.ShowToast("★ Clever Crossing badge earned!", UiKit.Violet, 5f);
                break;
            case "RegionalObjectivesCompleted":
                _audio.Play("success");
                _victoryShown = true;
                SaveGame(false, true);
                if (_ui.IsModalOpen) _ui.CloseModal();
                _ui.ShowRegionComplete(_sim);
                break;
            case "MissionCompleted":
                _audio.Play("success");
                _ui.ShowToast("Mission complete!", UiKit.LeafDark, 4f);
                SaveGame(false, true);
                break;
            case "MasteryEarned":
                _audio.Play("success");
                _ui.ShowToast("★ Clever Weaver badge earned!", UiKit.Violet, 5f);
                break;
            case "Victory":
                _audio.Play("success");
                _victoryShown = true;
                SaveGame(false, true);
                if (_ui.IsModalOpen) _ui.CloseModal();
                _ui.ShowVictory(_sim);
                break;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_started || _sim is null) return;
        if (_settingsDialog.Visible) return;

        if (_pendingAnchorB is not null && @event.IsActionPressed("ui_accept"))
        {
            ConfirmBuild();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("ui_cancel") || @event.IsActionPressed("cancel"))
        {
            if (_ui.IsModalOpen)
            {
                _ui.CloseModal();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (_tool is not null)
            {
                ClearBuild();
                GetViewport().SetInputAsHandled();
                return;
            }
            TogglePause();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("map"))
        {
            if (_ui.IsMapOpen) _ui.CloseModal();
            else if (!_ui.IsModalOpen) OpenMap();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("colony"))
        {
            if (!_ui.IsModalOpen) _ui.ToggleColony();
            else if (_ui.IsModalOpen) _ui.CloseModal();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("build_capture"))
        {
            SetTool(WebType.Capture);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("build_bridge"))
        {
            SetTool(WebType.Bridge);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("interact"))
        {
            TryInteract();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("dismantle"))
        {
            TryDismantleNearest();
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed)
            {
                _player.Zoom(1);
                GetViewport().SetInputAsHandled();
            }
            else if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed)
            {
                _player.Zoom(-1);
                GetViewport().SetInputAsHandled();
            }
            else if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
            {
                if (_tool is not null)
                {
                    ClearBuild();
                    GetViewport().SetInputAsHandled();
                }
            }
            else if (mouse.ButtonIndex == MouseButton.Left && mouse.Pressed && !_ui.BlocksWorldInput)
            {
                var anchor = PickAnchor(mouse.Position);
                if (anchor is not null)
                {
                    OnAnchorClicked(anchor);
                    GetViewport().SetInputAsHandled();
                }
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (_tool is not null && _pendingAnchorB is null && !_ui.BlocksWorldInput)
            {
                var anchor = PickAnchor(motion.Position);
                if (anchor != _hoverAnchor)
                {
                    if (_hoverAnchor is not null) _hoverAnchor.SetHovered(false);
                    _hoverAnchor = anchor;
                    if (_hoverAnchor is not null) _hoverAnchor.SetHovered(true);
                    RemovePreview();
                }
            }
            else if (_tool is null && (motion.ButtonMask & MouseButtonMask.Right) != 0 && !_ui.BlocksWorldInput)
            {
                _player.ApplyOrbit(motion.Relative.X, Settings.CameraSensitivity);
            }
        }
    }

    private AnchorNode? PickAnchor(Vector2 screenPosition)
    {
        var camera = _player?.Camera;
        if (camera is null) return null;

        // Nearest projected socket within a forgiving radius. This is what makes
        // overlapping Reedwater sockets predictable: the closest label wins.
        AnchorNode? nearest = null;
        var nearestPixels = 30f;
        foreach (var anchor in _anchors.Values)
        {
            // A socket in the other landmark can never be a valid build target
            // from the active story frame, so it is not pickable from here.
            if (_sim?.GetAnchor(anchor.AnchorId)?.Region != _sim?.ActiveRegionId) continue;
            var world = anchor.GlobalPosition + new Vector3(0, 0.18f, 0);
            if (camera.IsPositionBehind(world)) continue;
            var screen = camera.UnprojectPosition(world);
            var pixels = screen.DistanceTo(screenPosition);
            if (pixels > nearestPixels) continue;
            if (IsAnchorOccluded(camera.GlobalPosition, world)) continue;
            nearest = anchor;
            nearestPixels = pixels;
        }

        var from = camera.ProjectRayOrigin(screenPosition);
        var to = from + camera.ProjectRayNormal(screenPosition) * 90f;
        using var query = PhysicsRayQueryParameters3D.Create(from, to, 5);
        query.CollideWithAreas = true;
        query.CollideWithBodies = true;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        // Screen-space nearest wins over a raw ray hit: overlapping socket
        // spheres can put a different anchor in front of the cursor.
        if (nearest is not null)
        {
            if (hit.Count > 0 && hit["collider"].AsGodotObject() is not AnchorNode)
            {
                var bodyDistance = from.DistanceTo((Vector3)hit["position"]);
                if (from.DistanceTo(nearest.GlobalPosition) > bodyDistance + 0.15f) return null;
            }
            return nearest;
        }

        if (hit.Count == 0) return null;
        return hit["collider"].AsGodotObject() is AnchorNode direct &&
               _anchors.ContainsKey(direct.Definition.Id)
            ? direct
            : null;
    }

    private bool IsAnchorOccluded(Vector3 from, Vector3 world)
    {
        using var query = PhysicsRayQueryParameters3D.Create(from, world, 1);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return false;
        return from.DistanceTo((Vector3)hit["position"]) < from.DistanceTo(world) - 0.2f;
    }

    private void OnAnchorClicked(AnchorNode anchor)
    {
        if (_sim is null) return;
        if (_ui.BlocksWorldInput) return;
        if (_tool is null)
        {
            var inferred = anchor.Definition.Group is "gap" or "pebble" or "pond_bridge"
                ? WebType.Bridge
                : WebType.Capture;
            if (anchor.Definition.Group == "log_shelter") inferred = WebType.Capture;
            SetTool(inferred, silent: true);
        }
        var id = anchor.Definition.Id;
        if (_pendingAnchorB is not null)
        {
            if (id == _anchorA)
            {
                _anchors[_anchorA!].SetSelected(false);
                _anchorA = null;
                _pendingAnchorB = null;
                _ui.HideBuildConfirmation();
                RemovePreview();
                _ui.SetStatus("Choose the first glowing anchor.");
                return;
            }
            StageBuildConfirmation(anchor);
            return;
        }
        if (_anchorA is null)
        {
            _anchorA = id;
            anchor.SetSelected(true);
            _hoverAnchor = anchor;
            RemovePreview();
            _ui.SetStatus($"First anchor: {anchor.Definition.Label}. Choose the second glowing anchor.");
            _audio.Play("click");
            return;
        }
        if (_anchorA == id)
        {
            _anchors[_anchorA].SetSelected(false);
            _anchorA = null;
            _ui.SetStatus("Choose the first glowing anchor.");
            return;
        }
        StageBuildConfirmation(anchor);
    }

    private void StageBuildConfirmation(AnchorNode anchor)
    {
        if (_sim is null || _tool is null || _anchorA is null) return;
        var result = _sim.Preview(_anchorA, anchor.Definition.Id, _tool.Value);
        if (!result.IsValid)
        {
            _audio.Play("cancel");
            _ui.SetStatus(result.Reason);
            _ui.ShowToast(result.Reason, UiKit.Coral, 3.4f);
            anchor.SetHovered(true, false);
            return;
        }
        _pendingAnchorB = anchor.Definition.Id;
        _hoverAnchor = anchor;
        foreach (var other in _anchors.Values)
            if (other != anchor) other.SetHovered(false);
        anchor.SetHovered(true, true);
        _previewSignature = "";
        UpdatePreview();
        _ui.ShowBuildConfirmation(
            _sim.GetAnchor(_anchorA)?.Label ?? _anchorA,
            _sim.GetAnchor(_pendingAnchorB)?.Label ?? _pendingAnchorB,
            result.SilkCost);
        _audio.Play("click");
    }

    private void ConfirmBuild()
    {
        if (_sim is null || _tool is null || _anchorA is null || _pendingAnchorB is null) return;
        var result = _sim.Preview(_anchorA, _pendingAnchorB, _tool.Value);
        if (!result.IsValid)
        {
            _audio.Play("cancel");
            _ui.HideBuildConfirmation();
            _pendingAnchorB = null;
            _ui.SetStatus(result.Reason);
            _ui.ShowToast(result.Reason, UiKit.Coral, 3.4f);
            return;
        }
        if (!_sim.TryBuildWeb(_anchorA, _pendingAnchorB, _tool.Value, out _, out _))
        {
            _audio.Play("cancel");
            _ui.SetStatus(result.Reason);
            return;
        }
        var tool = _tool.Value;
        ProcessEvents();
        ClearBuild();
        _ui.ShowToast(tool == WebType.Capture
            ? "Capture web built! Watch for a fly."
            : "Silk bridge built! Walk across when you are ready.", UiKit.TealDark, 4f);
        _autosaveTimer = 0;
        SaveGame(false, true);
    }

    private void SetTool(WebType? type, bool silent = false)
    {
        if (_sim is null) return;
        if (type is not null && _tool == type)
        {
            ClearBuild();
            return;
        }
        _tool = type;
        _anchorA = null;
        _pendingAnchorB = null;
        _hoverAnchor = null;
        RemovePreview();
        _ui.HideBuildConfirmation();
        foreach (var anchor in _anchors.Values)
        {
            // Only the active landmark's sockets glow: the other landmark is
            // resident but not a valid build frame from here.
            var sameLandmark = _sim?.GetAnchor(anchor.AnchorId)?.Region == _sim?.ActiveRegionId;
            anchor.SetBuildMode(type is not null && sameLandmark);
            anchor.SetSelected(false);
            anchor.SetHovered(false);
        }
        _player.InputEnabled = type is null;
        _ui.SetBuildMode(type, null, _sim!);
        if (type is not null && !silent)
        {
            _audio.Play("click");
            _ui.ShowToast(type == WebType.Capture
                ? "Click two glowing anchors to spin a capture web, then choose Build. Right-click cancels."
                : "Click two glowing anchors to build a silk bridge, then choose Build. Right-click cancels.", UiKit.TealDark, 4f);
        }
        if (type is null) _ui.SetStatus(_sim!.GetActiveRegionHint());
    }

    private void ClearBuild()
    {
        if (_tool is null && _anchorA is null && _pendingAnchorB is null) return;
        _tool = null;
        _anchorA = null;
        _pendingAnchorB = null;
        _hoverAnchor = null;
        RemovePreview();
        _ui.HideBuildConfirmation();
        foreach (var anchor in _anchors.Values)
        {
            anchor.SetBuildMode(false);
            anchor.SetSelected(false);
            anchor.SetHovered(false);
        }
        if (_player is not null) _player.InputEnabled = true;
        if (_ui is not null && _sim is not null) _ui.SetBuildMode(null, null, _sim);
    }

    private void TryInteract()
    {
        if (_sim is null || _ui.BlocksWorldInput) return;
        // After the far bank is reached, a short reed trail saves the long walk
        // back over three bridges. It is optional: the bridges still work.
        if (_sim.ActiveRegionId == RegionIds.ReedwaterBank &&
            _sim.RegionalObjectives.CompletedObjectives.Contains("R03") &&
            Vector3DistanceToPlayer(
                ToWorld(RegionIds.ReedwaterBank, new Vec3(-9.6, 0, -2.8)), 2.6f))
        {
            _audio.Play("click");
            _player.GlobalPosition = ToWorld(RegionIds.ReedwaterBank, new Vec3(10.2, 0.55, 6.8));
            _player.Velocity = Vector3.Zero;
            _regionSafePosition = _player.GlobalPosition;
            _ui.ShowToast("You followed the reed trail back to the outpost.", UiKit.TealDark, 3.5f);
            return;
        }
        var player = PlayerLocal;
        if (_sim.TryCollectNearest(player, 3.2, out _, out _, out _))
        {
            _audio.Play("collect");
            ProcessEvents();
            return;
        }
        if (_sim.TryRepairNearest(player, 3.2, out _))
        {
            _audio.Play("repair");
            ProcessEvents();
            return;
        }
        if (TryForageNearest(PlayerWorld, 3.2, out var foraged, out var foragedFood, out _))
        {
            _audio.Play("collect");
            _ui.ShowToast(
                $"Foraged {foraged!.Label} · +{foragedFood} food", UiKit.TealDark, 3f);
            ProcessEvents();
            return;
        }
        var nurseryDistance = _player.GlobalPosition.DistanceTo(NurseryInteractionPoint);
        if (nurseryDistance <= 2.8f)
        {
            _audio.Play("click");
            _ui.OpenNursery(_sim);
            return;
        }
        _ui.ShowToast("Move closer to a caught insect, a damaged web, wild food or the nursery.",
            UiKit.Coral, 4f);
    }

    private void TryDismantleNearest()
    {
        if (_sim is null || _ui.BlocksWorldInput) return;
        if (_sim.TryDismantleNearest(PlayerLocal, 3.2, out var silk, out var reason))
        {
            _audio.Play("cancel");
            _ui.ShowToast($"Web removed · +{silk} silk", UiKit.TealDark, 3f);
            SyncWebViews();
            ProcessEvents();
        }
        else
        {
            _ui.ShowToast(reason, UiKit.Coral, 3.2f);
        }
    }

    private void TryHatch()
    {
        if (_sim is null) return;
        if (_sim.TryStartHatch(out var reason))
        {
            _audio.Play("click");
            _ui.ShowToast("Hatching started — a new helper is on the way.", UiKit.Violet, 3.5f);
            ProcessEvents();
            _ui.RefreshOpenPanels(_sim);
        }
        else
        {
            _ui.ShowToast(reason, UiKit.Coral, 3.5f);
        }
    }

    private void TryRecovery()
    {
        if (_sim is null) return;
        if (_sim.TryUseEmergencyRecovery(out var silk, out var reason))
        {
            _audio.Play("click");
            _ui.ShowToast($"Nursery rescue: +{silk} silk", UiKit.TealDark, 3.5f);
            ProcessEvents();
            _ui.RefreshOpenPanels(_sim);
        }
        else
        {
            _ui.ShowToast(reason, UiKit.Coral, 3.5f);
        }
    }

    private void TogglePause()
    {
        if (_ui.IsModalOpen)
        {
            _ui.CloseModal();
            return;
        }
        _audio.Play("click");
        _ui.OpenPause();
    }

    private void OpenSettings()
    {
        _settingsDialog.Configure(Settings);
        _settingsDialog.Open();
    }

    private void OnSettingsChanged(SettingsData settings)
    {
        Settings.Normalise();
        _audio.ApplySettings(Settings);
        _ui.SetTutorialPrompts(Settings.TutorialPrompts);
        if (_sim is not null) _baselineTuning ??= GameData.LoadTuning();
        if (_sim is not null) GameData.ApplyDifficulty(_sim.Tuning, _baselineTuning, Settings.LowThreat);
        TrySaveSettings();
        SettingsChanged?.Invoke(Settings);
    }

    private void SaveAndQuit()
    {
        var saved = SaveGame(false, true);
        if (saved)
        {
            QuitToMenuRequested?.Invoke();
            return;
        }
        var message = string.IsNullOrWhiteSpace(LastSaveError)
            ? "Couldn't save. Your progress is still here — you can retry."
            : "Couldn't save: " + LastSaveError;
        _ui?.OpenSaveFailedQuitPrompt(message, SaveAndQuit,
            () => QuitToMenuRequested?.Invoke());
    }

    private void TrySaveSettings()
    {
        var result = GameData.CreateSettingsService().Save(Settings);
        if (result.Success)
        {
            _settingsDialog?.ClearPersistenceError();
            return;
        }
        _settingsDialog?.ShowPersistenceError(
            "Couldn't save settings. Check disk space or permissions, then retry.",
            TrySaveSettings);
        _ui?.ShowToast("Couldn't save settings. Open Settings to retry.", UiKit.Coral, 6f);
    }

    private bool TryBuildSaveData(out SaveGameData data, out string error)
    {
        data = new SaveGameData();
        error = "";
        if (_sim is null || _player is null)
        {
            error = "There is no garden to save yet.";
            return false;
        }
        data = _sim.ExportSave(PlayerLocal, _player.CameraPivot.Rotation.Y, Settings);
        data.ExploredChunks = _explored.ToIds();
        if (!SimulateSaveFailureForProbe && !SaveFoodChunkChanges(out var chunkError))
        {
            error = chunkError;
            return false;
        }
        return true;
    }

    public bool SaveGame(bool showToast, bool quiet = false)
    {
        if (!TryBuildSaveData(out var data, out var error))
        {
            LastSaveError = error;
            if (showToast) _ui.ShowSaveResult(false, error);
            else if (!quiet) _ui.ShowSaveResult(false, error);
            return false;
        }
        var result = SimulateSaveFailureForProbe
            ? SaveResult.Fail("Simulated save failure", "simulated")
            : _saves.Save(data);
        LastSaveError = result.Error ?? "";
        if (showToast) _ui.ShowSaveResult(result.Success, result.Message);
        else if (!result.Success || (!quiet && result.Success)) _ui.ShowSaveResult(result.Success, result.Message);
        return result.Success;
    }

    /// <summary>
    /// In-game New Garden follows the same safe workflow as the title screen:
    /// save the live garden, archive the manifest and every chunk modification,
    /// verify the archive, and only then replace the live world. Any failure
    /// keeps the current garden running and explains what happened.
    /// </summary>
    private void RestartNewGame()
    {
        _ui.CloseModal();
        if (_sim is null || _player is null)
        {
            StartNew();
            return;
        }
        if (!TryBuildSaveData(out var data, out var error))
        {
            _ui.ShowToast(error, UiKit.Coral, 5f);
            return;
        }
        if (!_saves.PrepareNewGarden(data, out var message))
        {
            _ui.ShowToast(message, UiKit.Coral, 6f);
            return;
        }
        StartNew();
        SaveGame(false, true);
        _ui.ShowToast(
            "A new garden has begun. Your previous garden is safe in the garden archive.",
            UiKit.TealDark, 5f);
    }

    public void ApplySettings(SettingsData settings)
    {
        CopySettings(settings, Settings);
        Settings.Normalise();
        GameData.ApplyUiScale(Settings);
        GameData.ApplyFullscreen(Settings);
        _ui?.SetTutorialPrompts(Settings.TutorialPrompts);
        _settingsDialog?.Configure(Settings);
        _audio?.ApplySettings(Settings);
        if (_sim is not null) _baselineTuning ??= GameData.LoadTuning();
        if (_sim is not null) GameData.ApplyDifficulty(_sim.Tuning, _baselineTuning, Settings.LowThreat);
    }

    private void SyncWebViews()
    {
        if (_sim is null) return;
        // Both landmarks render at once: a web built in Reedwater remains
        // visible while the spider walks back to the garden.
        foreach (var web in _sim.Webs)
        {
            if (_webViews.ContainsKey(web.Id)) continue;
            var a = _sim.GetAnchor(web.AnchorA);
            var b = _sim.GetAnchor(web.AnchorB);
            if (a is null || b is null) continue;
            var view = new WebVisual { Name = $"Web_{web.Id}" };
            ViewHost(_websRoot, web.Region).AddChild(view);
            view.Configure(web, ToVector3(a.Position), ToVector3(b.Position), Settings.ReducedMotion);
            _webViews[web.Id] = view;
        }
        var activeIds = _sim.Webs.Select(web => web.Id).ToHashSet(StringComparer.Ordinal);
        var stale = _webViews.Keys.Where(id => !activeIds.Contains(id)).ToList();
        foreach (var id in stale)
        {
            _webViews[id].QueueFree();
            _webViews.Remove(id);
        }
        foreach (var view in _webViews.Values)
        {
            var state = _sim.GetWeb(view.WebId);
            if (state is not null) view.UpdateState(state);
        }
    }

    private void SyncInsectViews()
    {
        if (_sim is null) return;
        foreach (var insect in _sim.Insects)
        {
            if (_insectViews.ContainsKey(insect.Id)) continue;
            var view = new FlyView { Name = $"Insect_{insect.Id}" };
            ViewHost(_insectsRoot, insect.Definition.Region).AddChild(view);
            view.Configure(insect);
            _insectViews[insect.Id] = view;
        }
    }

    private void SyncWorkerViews()
    {
        if (_sim is null) return;
        // All workers live in one world-space host: local workers use their
        // landmark origin through WorldPosition, travelling workers walk the
        // procedural world, and origin rebasing moves the host once.
        foreach (var worker in _sim.Workers)
        {
            if (_workerViews.ContainsKey(worker.Id)) continue;
            var view = new WorkerView { Name = $"Worker_{worker.Id}" };
            ViewHost(_workersRoot, RegionIds.GardenClearing).AddChild(view);
            view.Configure(worker);
            _workerViews[worker.Id] = view;
        }
        var activeIds = _sim.Workers.Select(worker => worker.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _workerViews.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            _workerViews[id].QueueFree();
            _workerViews.Remove(id);
        }
    }

    private Vector3 NurseryInteractionPoint =>
        _sim is not null
            ? ToWorld(_sim.ActiveRegionId, _sim.ActiveRegion.NurseryPosition) + new Vector3(0, 0.4f, 0)
            : ToWorld(RegionIds.GardenClearing, WorldLayout.NurseryPosition) + new Vector3(0, 0.4f, 0);

    /// <summary>
    /// The authored pond water bodies are real monitoring Area3Ds. Touching any
    /// of them sends the player back to the last safe bank position, so water
    /// cannot be crossed by walking or hopping around a bridge.
    /// </summary>
    private void UpdateWaterHazard()
    {
        if (_player is null || _sim is null) return;
        if (_activeRegionId != RegionIds.ReedwaterBank)
        {
            // Procedural rivers are pure world data, not physics volumes. The
            // player is in trouble when the horizontal hazard mask matches and
            // they are actually down at water height; crossing decks stay safe.
            var world = PlayerWorld;
            var river = _chunks?.Generator.RiverAt(world.X, world.Z) ?? RiverSample.None;
            var inDanger = river.IsHazard && world.Y < river.WaterLevel + 0.3;
            if (inDanger && !_player.IsClimbing)
            {
                _player.GlobalPosition = _regionSafePosition;
                _player.Velocity = Vector3.Zero;
                if (!_waterToastShown)
                {
                    _waterToastShown = true;
                    _ui.ShowToast("Splash! The river is too deep to wade. Seek an old silk crossing.",
                        UiKit.Coral, 4.5f);
                }
                return;
            }
            if (_player.IsOnFloor() && !_player.IsClimbing)
                _regionSafePosition = _player.GlobalPosition;
            // Re-arm the warning only well clear of the channel; otherwise a
            // player holding forward against the current would re-trigger it
            // every frame after each recovery.
            if (_waterToastShown &&
                (!river.HasRiver || river.Distance > river.HalfWidth + 3.0))
                _waterToastShown = false;
            return;
        }

        var space = GetWorld3D().DirectSpaceState;
        using var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = 0.2f },
            Transform = new Transform3D(Basis.Identity, _player.GlobalPosition + new Vector3(0, 0.25f, 0))
        };
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        query.CollisionMask = 8;
        var hits = space.IntersectShape(query, 8);
        var inWater = hits.Any(hit => hit["collider"].AsGodotObject() is Area3D area && area.IsInGroup("water"));
        // A tall water guard catches jumps over open water, but a player on a
        // real bridge deck or bank has solid ground just below and is safe.
        using var groundQuery = PhysicsRayQueryParameters3D.Create(
            _player.GlobalPosition + new Vector3(0, 0.05f, 0),
            _player.GlobalPosition + new Vector3(0, -1.0f, 0), 1);
        groundQuery.CollideWithAreas = false;
        groundQuery.CollideWithBodies = true;
        var hasGround = space.IntersectRay(groundQuery).Count > 0;
        if (inWater && !hasGround)
        {
            _player.GlobalPosition = _regionSafePosition;
            _player.Velocity = Vector3.Zero;
            if (!_waterToastShown)
            {
                _waterToastShown = true;
                _ui.ShowToast("Splash! The pond is too deep. Build a silk bridge to cross.", UiKit.Coral, 4.5f);
            }
            return;
        }

        if (_player.IsOnFloor() && !_player.IsClimbing)
            _regionSafePosition = _player.GlobalPosition;
        _waterToastShown = false;
    }

    private bool Vector3DistanceToPlayer(Vector3 point, float radius)
    {
        if (_player is null) return false;
        return _player.GlobalPosition.DistanceTo(point) <= radius;
    }

    private Vector3 WebCenter(WebState web)
    {
        var a = _sim.GetAnchor(web.AnchorA);
        var b = _sim.GetAnchor(web.AnchorB);
        if (a is null || b is null) return Vector3.Zero;
        var region = web.Region;
        return (ToWorld(region, a.Position) + ToWorld(region, b.Position)) * 0.5f;
    }

    public static Vector3 ToVector3(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>Landmark-local gameplay coordinates to engine space.</summary>
    private Vector3 ToWorld(string regionId, Vec3 local) =>
        ToEngine(LandmarkCatalog.OriginOf(regionId) + local);

    /// <summary>Stable logical world position of a landmark-local point.</summary>
    public static Vector3 LandmarkWorld(string regionId, Vec3 local) =>
        ToVector3(LandmarkCatalog.OriginOf(regionId) + local);

    /// <summary>Logical world to engine space.</summary>
    private Vector3 ToEngine(Vec3 world) => ToVector3(world - _originOffset);

    /// <summary>Engine position to logical world.</summary>
    private Vec3 ToLogical(Vector3 engine) =>
        new(engine.X + _originOffset.X, engine.Y + _originOffset.Y, engine.Z + _originOffset.Z);

    /// <summary>Engine world position to landmark-local simulation coordinates.</summary>
    private Vec3 ToLocal(Vec3 world, string? regionId = null) =>
        LandmarkCatalog.ToLocal(regionId ?? _activeRegionId, world);

    private Vec3 PlayerWorld => _player is null ? Vec3.Zero : ToLogical(_player.GlobalPosition);

    private static Vec3 SnapOrigin(Vec3 world) => new(
        Math.Round(world.X / RebaseGrid) * RebaseGrid, 0,
        Math.Round(world.Z / RebaseGrid) * RebaseGrid);

    /// <summary>Move the presentation origin near the player without changing
    /// any logical coordinate, save value or simulation state.</summary>
    private void UpdateOriginRebase()
    {
        if (_player is null) return;
        var engine = new Vec3(_player.GlobalPosition.X, _player.GlobalPosition.Y, _player.GlobalPosition.Z);
        if (Math.Abs(engine.X) < RebaseThreshold && Math.Abs(engine.Z) < RebaseThreshold) return;
        ApplyOrigin(SnapOrigin(engine + _originOffset));
    }

    private void ApplyOrigin(Vec3 target)
    {
        if (target == _originOffset) return;
        var delta = target - _originOffset;
        _originOffset = target;
        if (_player is not null)
        {
            _player.GlobalPosition -= ToVector3(delta);
            _player.FallResetPosition -= ToVector3(delta);
        }
        _regionSafePosition -= ToVector3(delta);
        RemovePreview();
        UpdateWorldTransforms();
    }

    /// <summary>Place every world-space node for the current logical origin.</summary>
    private void UpdateWorldTransforms()
    {
        var offset = ToVector3(_originOffset);
        if (_gardenWorld is not null) _gardenWorld.Position = -offset;
        if (_gardenAnchors is not null) _gardenAnchors.Position = -offset;
        if (_gardenPassage is not null) _gardenPassage.Position = -offset;
        if (_chunkHost is not null) _chunkHost.Position = -offset;
        if (_windParticles is not null)
            _windParticles.Position = new Vector3(0, 1.8f, 0) - offset;
        if (_climbLabel is not null)
            _climbLabel.Position = ToEngine(new Vec3(-1.5, 2.0, 9.0));
        if (_farBankTrailLabel is not null)
            _farBankTrailLabel.Position = ToEngine(LandmarkCatalog.OriginOf(RegionIds.ReedwaterBank) +
                                                   new Vec3(-9.6, 1.7, -2.8));
        if (_nurseryLabel is not null && _sim is not null)
            _nurseryLabel.Position = ToWorld(_sim.ActiveRegionId,
                _sim.ActiveRegion.NurseryPosition) + new Vector3(0, 2.25f, 0);
        foreach (var (regionId, root) in _regionRoots)
            root.Position = ToWorld(regionId, Vec3.Zero);
        foreach (var (host, regionId) in _hostRegions)
            host.Position = ToWorld(regionId, Vec3.Zero);
    }

    private Vec3 PlayerLocal => ToLocal(PlayerWorld);

    /// <summary>
    /// Presentation root for one landmark's webs, insects or workers. Positions
    /// inside the simulation are landmark-local; the host applies the constant
    /// world origin so both landmarks render in one continuous space.
    /// </summary>
    private Node3D ViewHost(Node3D root, string regionId)
    {
        var key = root.Name + ":" + regionId;
        if (_viewHosts.TryGetValue(key, out var host)) return host;
        host = new Node3D
        {
            Name = "Region_" + regionId,
            Position = ToWorld(regionId, Vec3.Zero)
        };
        root.AddChild(host);
        _viewHosts[key] = host;
        _hostRegions[host] = regionId;
        return host;
    }

    private static void CopySettings(SettingsData from, SettingsData to)
    {
        to.MasterVolume = from.MasterVolume;
        to.MusicVolume = from.MusicVolume;
        to.SfxVolume = from.SfxVolume;
        to.UiScalePercent = from.UiScalePercent;
        to.ReducedMotion = from.ReducedMotion;
        to.LowThreat = from.LowThreat;
        to.Fullscreen = from.Fullscreen;
        to.CameraSensitivity = from.CameraSensitivity;
        to.TutorialPrompts = from.TutorialPrompts;
    }

    // Runtime probe support: deliberately calls the same adapter methods as input.
    public void ProbeSetTool(WebType type) => SetTool(type, silent: true);

    public bool ProbeBuildWeb(string a, string b, WebType type)
    {
        if (_sim is null) return false;
        var success = _sim.TryBuildWeb(a, b, type, out _, out _);
        if (success)
        {
            ProcessEvents();
            SyncWebViews();
            SaveGame(false, true);
        }
        return success;
    }

    public void ProbeAdvance(double seconds)
    {
        if (_sim is null) return;
        _sim.AdvanceSeconds(seconds);
        ProcessEvents();
        SyncWebViews();
        SyncWorkerViews();
        _ui.RefreshOpenPanels(_sim);
    }

    public void ProbeTeleport(Vec3 world)
    {
        if (_player is null) return;
        // Teleports recentre the presentation origin on the destination so a
        // far jump never spends a frame outside comfortable float precision.
        ApplyOrigin(SnapOrigin(world));
        _player.GlobalPosition = ToEngine(world);
        _player.Velocity = Vector3.Zero;
    }

    /// <summary>Generated terrain height at a logical world point.</summary>
    public double ProbeGroundHeightAt(Vec3 world) =>
        _chunks?.Generator.HeightAt(world.X, world.Z) ?? 0;

    public void ProbeLookAt(Vec3 worldTarget)
    {
        if (_player is null) return;
        var target = ToEngine(worldTarget);
        var dx = target.X - _player.GlobalPosition.X;
        var dz = target.Z - _player.GlobalPosition.Z;
        var yaw = Mathf.Atan2(-dx, -dz);
        _player.CameraPivot.Rotation = new Vector3(0, yaw, 0);
    }

    public void ProbeInteract() => TryInteract();

    public bool ProbeHatch()
    {
        if (_sim is null) return false;
        var success = _sim.TryStartHatch(out _);
        if (success)
        {
            ProcessEvents();
            _ui.RefreshOpenPanels(_sim);
        }
        return success;
    }

    public bool ProbeRepairNearest()
    {
        if (_sim is null) return false;
        var success = _sim.TryRepairNearest(PlayerLocal, 4.5, out _);
        if (success) ProcessEvents();
        return success;
    }

    public bool ProbeSave() => SaveGame(false, true);

    public void ProbeOpenNursery()
    {
        if (_sim is not null) _ui.OpenNursery(_sim);
    }

    public string DebugUi => _ui.DebugModal();

    public void ProbeCloseModal() => _ui.CloseModal();

    public void ProbeClearBuild() => ClearBuild();

    public void ProbeOpenSettings() => OpenSettings();

    public void ProbeCloseSettings() => _settingsDialog.Close();

    public void ProbeShowSettingsError() =>
        _settingsDialog.ShowPersistenceError("Simulated settings write failure", () => { });

    public bool SettingsErrorVisible => _settingsDialog.PersistenceErrorVisible;

    public void ProbeOpenPause() => _ui.OpenPause();

    public void ProbeOpenHelp() => _ui.OpenHelp();

    public void ProbeToggleColony() => _ui.ToggleColony();

    public void ProbeDismantleNearest() => TryDismantleNearest();

    public IReadOnlyList<Vec3> ProbeBridgeRampGroundPoints(string webId)
    {
        if (!_webViews.TryGetValue(webId, out var view)) return Array.Empty<Vec3>();
        return view.RampGroundPoints
            .Select(v => new Vec3(v.X, v.Y, v.Z))
            .ToList();
    }

    public int ProbeBridgeWalkwayShapeCount(string webId) =>
        _webViews.TryGetValue(webId, out var view) ? view.WalkwayCollisionShapeCount : 0;

    public string ProbeBridgeVisualSignature(string webId) =>
        _webViews.TryGetValue(webId, out var view) ? view.ProbeBridgeVisualSignature : "none";

    public int ProbeBridgePlankCount(string webId) =>
        _webViews.TryGetValue(webId, out var view) ? view.ProbeBridgePlankCount : 0;

    public int ProbeBridgeDetailCount(string webId) =>
        _webViews.TryGetValue(webId, out var view) ? view.ProbeBridgeDetailCount : 0;

    public int ProbeBridgeRailTriangles(string webId) =>
        _webViews.TryGetValue(webId, out var view) ? view.ProbeBridgeRailTriangles : 0;

    public int ProbeBridgeDamageOverlayCount(string webId) =>
        _webViews.TryGetValue(webId, out var view) ? view.ProbeBridgeDamageOverlayCount : 0;

    public int ProbeBridgeUnstyledMeshCount(string webId) =>
        _webViews.TryGetValue(webId, out var view) ? view.ProbeBridgeUnstyledMeshCount : -1;

    public bool ProbeBridgeUsesAllPlankVariants(string webId) =>
        _webViews.TryGetValue(webId, out var view) && view.ProbeBridgeUsesAllPlankVariants;

    public bool ProbeBridgeConstructionActive(string webId) =>
        _webViews.TryGetValue(webId, out var view) && view.ProbeBridgeConstructionActive;
    public int ProbeBridgeConstructionPartCount(string webId) =>
        _webViews.TryGetValue(webId, out var view) ? view.ProbeBridgeConstructionPartCount : 0;
    public bool ProbeWorkerActivityGlowVisible(string workerId) =>
        _workerViews.TryGetValue(workerId, out var view) && view.ProbeActivityGlowVisible;
    public bool ProbeFarBankTrailVisible => _farBankTrailLabel.Visible;

    public bool ProbeToastVisible => _ui.ProbeToastVisible;
    public string ProbeToastText => _ui.ProbeToastText;
    public IReadOnlyList<string> ProbeToastHistory => _ui.ProbeToastHistory;
    public void ProbeClearToastHistory() => _ui.ProbeClearToastHistory();
    public Rect2 ProbeToastRect => _ui.ProbeToastRect;
    public Rect2 ProbeMissionRect => _ui.ProbeMissionRect;
    public Rect2 ProbeActionColumnRect => _ui.ProbeActionColumnRect;
    public Rect2 ProbeToolbarRect => _ui.ProbeToolbarRect;
    public Rect2 ProbeStatusRect => _ui.ProbeStatusRect;
    public void ProbeShowToast(string text) => _ui.ShowToast(text, UiKit.TealDark, 3.5f);

    public IReadOnlyList<SceneValidationCheck> ProbeSceneValidation()
    {
        var checks = SceneContentValidator.Validate(this).ToList();
        var packed = GD.Load<PackedScene>("res://scenes/world/regions/ReedwaterBank/ReedwaterBank.tscn");
        if (packed is null)
        {
            checks.Add(new SceneValidationCheck("reedwater-scene-loads", false,
                "ReedwaterBank.tscn is missing"));
            return checks;
        }
        var region = packed.Instantiate<ReedwaterRegion>();
        AddChild(region);
        checks.AddRange(SceneContentValidator.ValidateReedwater(region));
        region.QueueFree();
        return checks;
    }

    public void ProbeQuitToMenu() => SaveAndQuit();

    public bool ProbeTitleVisible => false;

    public string ActiveRegionId => _activeRegionId;
    public bool ProbeTravel(string regionId)
    {
        if (_sim is null || _player is null) return false;
        var ok = _sim.TryTravelTo(regionId, out _);
        if (!ok) return false;
        var region = _sim.ActiveRegion;
        var arrival = LandmarkCatalog.ToWorld(regionId, region.ArrivalPoint).Absolute;
        ApplyOrigin(SnapOrigin(arrival));
        ActivateRegion(regionId);
        ResetPresentation();
        _player.GlobalPosition = ToEngine(arrival);
        _player.Velocity = Vector3.Zero;
        _regionSafePosition = _player.GlobalPosition;
        _player.FallResetPosition = ToWorld(regionId, region.PlayerSpawn);
        _player.ResetCamera();
        _ui.ShowToast($"Welcome to {region.DisplayName}.", UiKit.TealDark, 4f);
        _ui.SetStatus(_sim.GetActiveRegionHint());
        RefreshAll();
        ProcessEvents();
        return true;
    }

    public bool ProbeInWater()
    {
        if (_player is null || _activeRegionId != RegionIds.ReedwaterBank) return false;
        return ProbeWaterVolumeAt(new Vec3(_player.GlobalPosition.X, _player.GlobalPosition.Y, _player.GlobalPosition.Z));
    }

    /// <summary>True when the probe sphere at a point intersects an authored water volume.</summary>
    public bool ProbeWaterVolumeAt(Vec3 world)
    {
        var value = ToEngine(world);
        var space = GetWorld3D().DirectSpaceState;
        using var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = 0.2f },
            Transform = new Transform3D(Basis.Identity, value + new Vector3(0, 0.25f, 0))
        };
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        query.CollisionMask = 8;
        return space.IntersectShape(query, 8)
            .Any(hit => hit["collider"].AsGodotObject() is Area3D area && area.IsInGroup("water"));
    }

    /// <summary>Same water/ground decision used by UpdateWaterHazard at an arbitrary point.</summary>
    public bool ProbeWaterHazardAt(Vec3 world)
    {
        if (ProbeRiverDangerAt(world)) return true;
        var value = ToEngine(world);
        var space = GetWorld3D().DirectSpaceState;
        using var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = 0.2f },
            Transform = new Transform3D(Basis.Identity, value + new Vector3(0, 0.25f, 0))
        };
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        query.CollisionMask = 8;
        var inWater = space.IntersectShape(query, 8)
            .Any(hit => hit["collider"].AsGodotObject() is Area3D area && area.IsInGroup("water"));
        if (!inWater) return false;
        using var groundQuery = PhysicsRayQueryParameters3D.Create(
            value + new Vector3(0, 0.05f, 0),
            value + new Vector3(0, -1.0f, 0), 1);
        groundQuery.CollideWithAreas = false;
        groundQuery.CollideWithBodies = true;
        return space.IntersectRay(groundQuery).Count == 0;
    }

    /// <summary>Nearest procedural river at a logical world point.</summary>
    public RiverSample ProbeRiverAt(Vec3 world) =>
        _chunks?.Generator.RiverAt(world.X, world.Z) ?? RiverSample.None;

    /// <summary>Horizontal river-hazard mask, independent of height.</summary>
    public bool ProbeRiverMaskAt(Vec3 world) => ProbeRiverAt(world).IsHazard;

    /// <summary>River hazard including the player's height.</summary>
    public bool ProbeRiverDangerAt(Vec3 world)
    {
        var river = ProbeRiverAt(world);
        return river.IsHazard && world.Y < river.WaterLevel + 0.3;
    }

    /// <summary>Centre of one generated crossing (for real-input probes).</summary>
    public Vec3 ProbeRiverCrossing(int riverIndex, int crossingIndex)
    {
        var x = crossingIndex * RiverMap.CrossingSpacing;
        var centerZ = RiverMap.CenterAt(riverIndex, x);
        var level = _chunks?.Generator.Rivers.WaterLevelAt(riverIndex, x) ?? 0;
        return new Vec3(x, level, centerZ);
    }

    /// <summary>Number of loaded generated bridge nodes.</summary>
    public int ProbeLoadedBridgeCount => _chunks is null ? 0 : _chunks.Nodes.Values
        .SelectMany(node => node.GetChildren())
        .Count(child => child.Name.ToString().StartsWith("RiverBridge_", StringComparison.Ordinal));

    /// <summary>River centre, surface and level at an arbitrary X on one band.</summary>
    public Vec3 ProbeRiverPoint(int riverIndex, double x)
    {
        var centerZ = RiverMap.CenterAt(riverIndex, x);
        var level = _chunks?.Generator.Rivers.WaterLevelAt(riverIndex, x) ?? 0;
        return new Vec3(x, level, centerZ);
    }

    /// <summary>True when the generated bridge for one crossing is resident.</summary>
    public bool ProbeRiverBridgeAt(int riverIndex, int crossingIndex)
    {
        var x = crossingIndex * RiverMap.CrossingSpacing;
        var centerZ = RiverMap.CenterAt(riverIndex, x);
        var coordinate = ChunkCoordinate.OfWorldPoint(x, centerZ);
        if (_chunks is null || !_chunks.Nodes.TryGetValue(coordinate, out var node)) return false;
        return node.GetNodeOrNull<Node3D>($"RiverBridge_river_{riverIndex}_{crossingIndex}") is not null;
    }

    /// <summary>Number of water quads in the loaded chunk ring.</summary>
    public int ProbeLoadedWaterQuadCount => _chunks is null ? 0 : _chunks.Nodes.Keys
        .Sum(coordinate => _chunks.Generator.GenerateChunk(coordinate)?.WaterQuads.Count ?? 0);

    /// <summary>
    /// Every rendered prop must sit inside its own chunk's world bounds. This
    /// catches coordinate-frame regressions (props are stored world-space in
    /// Core but are children of an origin-positioned chunk body).
    /// </summary>
    public bool ProbePropsInOwnChunks()
    {
        if (_chunks is null) return false;
        foreach (var (coordinate, node) in _chunks.Nodes)
        {
            var origin = coordinate.OriginWorld;
            foreach (var child in node.GetChildren())
            {
                if (child is not Node3D prop ||
                    !prop.Name.ToString().StartsWith("Prop_", StringComparison.Ordinal)) continue;
                var world = ToLogical(prop.GlobalPosition);
                if (world.X < origin.X - 0.01 || world.X > origin.X + WorldChunk.Size + 0.01 ||
                    world.Z < origin.Z - 0.01 || world.Z > origin.Z + WorldChunk.Size + 0.01)
                {
                    return false;
                }
            }
        }
        return true;
    }

    public void ProbeSetCameraZoom(float length)
    {
        if (_player is null) return;
        _player.SpringArm.SpringLength = Mathf.Clamp(length, 5.5f, 15f);
    }

    /// <summary>Probe framing: spring-arm pitch in degrees (negative looks down).</summary>
    public void ProbeSetCameraPitch(float degrees)
    {
        if (_player is null) return;
        _player.SpringArm.Rotation = new Vector3(
            Mathf.DegToRad(Mathf.Clamp(degrees, -80f, -5f)), 0, 0);
    }

    // Kept as a probe-compatible shim: the travel dialog no longer exists in
    // seamless play, so the modal can never open.
    public void ProbeOpenTravel() { }
    public bool TravelModalOpen => false;
    public bool RegionCompleteModalOpen => _ui.IsRegionCompleteOpen;
    public void ProbeSetRegionPlayer(Vec3 world)
    {
        ProbeSetSafePosition(world);
        ProbeTeleport(world);
    }

    public void ProbeSetSafePosition(Vec3 world) => _regionSafePosition = ToEngine(world);

    /// <summary>Anchors and insects of the active landmark's story frame. All
    /// landmarks remain resident in the seamless world (see
    /// <see cref="ResidentAnchorCount"/>), but interaction and HUD counts stay
    /// scoped to where the spider is.</summary>
    public int VisibleAnchorCount => _sim?.RegionAnchors.Count ?? _anchors.Count;
    public int VisibleWebCount => _webViews.Count;
    public int VisibleInsectCount => _sim?.RegionInsects.Count ?? _insectViews.Count;
    public int VisibleWorkerCount => _workerViews.Count;
    /// <summary>The bramble gate physically blocks the Reedwater path until
    /// the garden story is complete.</summary>
    public bool ProbeBrambleGateBlocking =>
        _brambleGate is not null && _brambleGate.CollisionLayer == 1 &&
        _brambleGateMesh?.Visible == true;

    /// <summary>The gate has been removed: collision off and mesh hidden.</summary>
    public bool ProbeBrambleGateOpen =>
        _brambleGate is not null && _brambleGate.CollisionLayer == 0 &&
        _brambleGateMesh?.Visible != true;

    public int ResidentAnchorCount => _anchors.Count;
    public int ResidentInsectCount => _insectViews.Count;
    public int ResidentWebCount => _webViews.Count;
    public bool VictoryModalOpen => _ui.IsVictoryOpen;
    public bool PauseModalOpen => _ui.IsPauseOpen;
    public int ProbeWorldSeed => _worldSeed;
    public string ProbeSaveId => _saves.Load().Data?.SaveId ?? "";
    public bool ProbeArchiveAvailable => _saves.HasArchive;
    public Rect2 ProbePauseNewGameRect => _ui.ProbePauseNewGameRect;
    public Rect2 ProbeConfirmNewGardenRect => _ui.ProbeConfirmNewGardenRect;
    public bool ProbeNewGardenConfirmVisible => _ui.ProbeNewGardenConfirmVisible;
    public void ProbeOpenNewGardenConfirm() => _ui.ProbeOpenNewGardenConfirm();
    public void ProbePressConfirmNewGarden() => _ui.ProbePressConfirmNewGarden();
    public bool HelpModalOpen => _ui.IsHelpOpen;
    public bool ColonyModalOpen => _ui.IsColonyOpen;
    public bool SaveFailedPromptOpen => _ui.IsSaveFailedOpen;
    public bool SimulateSaveFailureForProbe { get; set; }
    public bool PlayerPhysicsPaused => _player?.PhysicsPaused ?? false;
    public bool PlayerClimbing => _player?.IsClimbing ?? false;
    /// <summary>Stable logical world position (probes and bookkeeping).</summary>
    public Vector3 PlayerPosition => _player is null ? Vector3.Zero : ToVector3(PlayerWorld);
    public string ProbeJson { get; set; } = "";

    public Vector2 ProbeScreenPosition(Vec3 world) =>
        _player.Camera.UnprojectPosition(ToEngine(world));

    public int TravellingWorkerCount => _sim?.Workers.Count(worker => worker.Travelling) ?? 0;
    public Vec3 ProbeWorkerWorldPosition(string id) =>
        _sim?.Workers.FirstOrDefault(worker => worker.Id == id)?.WorldPosition ?? Vec3.Zero;
    public Vec3 ProbeWorkerJourneyDestination(string id) =>
        _sim?.Workers.FirstOrDefault(worker => worker.Id == id)?.JourneyDestination ?? Vec3.Zero;
    public bool ProbeWorkerSummoned(string id) =>
        _sim?.Workers.FirstOrDefault(worker => worker.Id == id)?.Summoned == true;
    public string ProbeWorkerCurrentNode(string id) =>
        _sim?.Workers.FirstOrDefault(worker => worker.Id == id)?.CurrentNode ?? "";
    public bool ProbeWorkerViewVisible(string id) =>
        _workerViews.TryGetValue(id, out var view) && view.Visible;
    public bool ProbeSummonHelpers() =>
        _sim?.TrySummonWorkers(PlayerWorld, _sim.ActiveRegionId, out _) == true;

    public bool ProbeWorkerTravelling(string id) =>
        _sim?.Workers.FirstOrDefault(worker => worker.Id == id)?.Travelling == true;
    public string ProbeWorkerJourneyTarget(string id) =>
        _sim?.Workers.FirstOrDefault(worker => worker.Id == id)?.JourneyTargetRegion ?? "";
    public bool ProbeWorkerRouteUsesBridge(string id) =>
        _sim?.Workers.FirstOrDefault(worker => worker.Id == id)?.JourneyUsesBridge == true;
    public bool ProbeWorkerBlocked(string id) =>
        _sim?.Workers.FirstOrDefault(worker => worker.Id == id)?.RouteBlocked == true;
    public string ProbeWorkerRegion(string id) =>
        _sim?.Workers.FirstOrDefault(worker => worker.Id == id)?.Region ?? "";
    public bool ProbeStartWorkerJourney(string id, string region, Vec3 world) =>
        _sim?.BeginWorkerJourneyForTest(id, region, world) == true;

    public int ProbeFoodViewCount => _foodViews.Count;
    public FoodSourceDefinition? ProbeNearestFood(Vec3 near, double radius) =>
        NearestFood(near, radius, availableOnly: false);
    public bool ProbeFoodAvailable(string id) => _sim?.IsFoodAvailable(id) ?? false;
    public double ProbeFoodSecondsRemaining(string id) =>
        _sim?.FoodSecondsRemaining(id) ?? 0;
    public bool ProbeForageNearest() =>
        TryForageNearest(PlayerWorld, 3.2, out _, out _, out _);
    public string ProbeFoodPrompt() => FoodPrompt() ?? "";
    public bool ProbeFoodViewVisible(string id) =>
        _foodViews.TryGetValue(id, out var view) && view.ProbeVisible;
    public string ProbeFoodViewPrompt(string id) =>
        _foodViews.TryGetValue(id, out var view) ? view.ProbePrompt : "";

    public int ExploredChunkCount => _explored.Count;
    public bool ProbeExploredContains(Vec3 world) =>
        _explored.Contains(ChunkCoordinate.OfWorldPoint(world.X, world.Z));
    public bool ProbeMapOpen => _ui.IsMapOpen;
    public int ProbeMapCells => _ui.ProbeMapCells;
    public Rect2 ProbeMapButtonRect => _ui.ProbeMapButtonRect;
    public Rect2 ProbeMapHomeRect => _ui.ProbeMapHomeRect;
    public void ProbeOpenMap() => OpenMap();
    public void ProbeCloseMap() => _ui.CloseModal();
    public bool ProbeEnterLandmark(string regionId)
    {
        if (_sim is null || _player is null) return false;
        var region = RegionCatalog.Get(regionId);
        if (region is null) return false;
        if (_sim.ActiveRegionId != regionId && !_sim.EnterLandmark(regionId)) return false;
        var world = LandmarkCatalog.ToWorld(regionId, region.NurseryPosition).Absolute;
        ApplyOrigin(SnapOrigin(world));
        ActivateRegion(regionId);
        ResetPresentation();
        _player.GlobalPosition = ToEngine(world + new Vec3(0, 0.6, 0));
        _player.Velocity = Vector3.Zero;
        _player.ResetCamera();
        _regionSafePosition = _player.GlobalPosition;
        _player.FallResetPosition = _player.GlobalPosition;
        _zone = regionId;
        _zoneBiome = null;
        _ui.SetExploration(null, null);
        _ui.UpdateHud(_sim, Settings);
        _ui.SetStatus(_sim.GetActiveRegionHint());
        RefreshAll();
        ProcessEvents();
        return true;
    }

    public void ProbeReturnHome() => ReturnHome();

    public int ProbeLoadedPropCount => _chunks is null ? 0 : _chunks.Nodes.Values
        .SelectMany(node => node.GetChildren())
        .Count(child => child.Name.ToString().StartsWith("Prop_", StringComparison.Ordinal));

    /// <summary>Dominant biome at a logical world point (probe/diagnostic).</summary>
    public string ProbeBiomeAt(Vec3 world) =>
        _chunks?.Generator.BiomeNameAt(world.X, world.Z) ?? "none";

    /// <summary>
    /// Find a representative point of one biome near a logical centre. The
    /// search prefers a deep core (weight ≥ 0.7) so evidence screenshots show
    /// the biome's own palette and props rather than a boundary blend, and it
    /// keeps a clear margin from authored landmark ground.
    /// </summary>
    public Vec3 ProbeFindBiome(string biome, Vec3 near, double radius)
    {
        if (_chunks is null) return near;
        var generator = _chunks.Generator;
        Vec3 best = near;
        var bestWeight = -1.0;
        for (var ring = 24.0; ring <= radius; ring += 12)
        {
            for (var angle = 0; angle < 360; angle += 12)
            {
                var rad = Mathf.DegToRad(angle);
                var x = near.X + Math.Cos(rad) * ring;
                var z = near.Z + Math.Sin(rad) * ring;
                // Generated-land probes only: authored landmark ground is a
                // different surface and may contain authored obstacles.
                if (WorldGenerator.AuthoredRects.Any(rect => rect.DistanceOutside(x, z) < 28)) continue;
                // Keep evidence walks on dry land and clear of the bank slope.
                var river = generator.RiverAt(x, z);
                if (river.HasRiver && river.Distance < river.HalfWidth + 6) continue;
                var weight = generator.BiomeWeightAt(x, z, biome);
                if (weight <= bestWeight) continue;
                bestWeight = weight;
                best = new Vec3(x, generator.HeightAt(x, z) + 0.6, z);
                if (weight >= 0.7) return best;
            }
        }
        return best;
    }

    /// <summary>Current presentation origin (logical minus engine).</summary>
    public Vector3 ProbeOriginOffset => ToVector3(_originOffset);

    /// <summary>Raw engine-space position; bounded by the rebase threshold.</summary>
    public Vector3 ProbeEnginePlayerPosition => _player?.GlobalPosition ?? Vector3.Zero;

    public bool ProbeClickAnchor(string anchorId)
    {
        if (!_anchors.TryGetValue(anchorId, out var anchor)) return false;
        OnAnchorClicked(anchor);
        return true;
    }

    public bool ProbeBuildConfirmVisible => _ui.BuildConfirmVisible;
    public string ProbeBuildConfirmText => _ui.ProbeBuildConfirmText;
    public string ProbeStatusText => _ui.ProbeStatusText;
    public Rect2 ProbeBuildConfirmRect => _ui.ProbeBuildConfirmRect;
    public bool ProbeNurseryProgressVisible => _ui.ProbeNurseryProgressVisible;
    public double ProbeNurseryProgressValue => _ui.ProbeNurseryProgressValue;
    public string ProbeNurseryStatusText => _ui.ProbeNurseryStatusText;
    public string ProbeNurseryCountText => _ui.ProbeNurseryCountText;

    public void ProbeConfirmBuild() => ConfirmBuild();
    public void ProbeCancelPendingBuild() => ClearBuild();
    public void ProbeShowBuildConfirmation() =>
        _ui.ShowBuildConfirmation("Reed post", "Mossy island post", 5);
    public void ProbeHideBuildConfirmation() => _ui.HideBuildConfirmation();

    public string ProbePickAnchorAt(Vector2 screenPosition) =>
        PickAnchor(screenPosition)?.Definition.Id ?? "";

    public string ProbeAnchorVisualState(string anchorId) =>
        _anchors.TryGetValue(anchorId, out var anchor) ? anchor.ProbeVisualState : "missing";

    public Vector2 ProbeAnchorScreenPosition(string anchorId) =>
        _anchors.TryGetValue(anchorId, out var anchor)
            ? _player.Camera.UnprojectPosition(anchor.GlobalPosition + new Vector3(0, 0.18f, 0))
            : Vector2.Zero;

    public bool ProbeAnchorLabelVisible(string anchorId) =>
        _anchors.TryGetValue(anchorId, out var anchor) && anchor.ProbeVisualState.Contains("label=True");

    /// <summary>Physics ground under the player, for asserting the real island surface in the probe.</summary>
    public string ProbeGroundColliderPath()
    {
        if (_player is null) return "";
        using var query = PhysicsRayQueryParameters3D.Create(
            _player.GlobalPosition + new Vector3(0, 0.5f, 0),
            _player.GlobalPosition + new Vector3(0, -2.5f, 0), 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return "";
        return (hit["collider"].AsGodotObject() as Node)?.GetPath().ToString() ?? "";
    }

    public string LastSaveError { get; private set; } = "";
}
