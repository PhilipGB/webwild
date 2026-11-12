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

    private GameSimulation _sim = null!;
    private Tuning _baselineTuning = null!;
    private SaveService _saves = null!;
    private SpiderPlayer _player = null!;
    private GameUI _ui = null!;
    private AudioDirector _audio = null!;
    private SettingsDialog _settingsDialog = null!;
    private CanvasLayer _settingsLayer = null!;

    private Node3D _anchorsRoot = null!;
    private Node3D _websRoot = null!;
    private Node3D _insectsRoot = null!;
    private Node3D _workersRoot = null!;
    private Node3D _previewRoot = null!;
    private Node3D? _preview;
    private CpuParticles3D _windParticles = null!;
    private Label3D _nurseryLabel = null!;
    private Label3D _climbLabel = null!;
    private Label3D _travelLabel = null!;

    private string _activeRegionId = RegionIds.GardenClearing;
    private Node3D _gardenWorld = null!;
    private Node3D _gardenAnchors = null!;
    private Node3D _gardenPassage = null!;
    private Node3D _regionsHost = null!;
    private readonly Dictionary<string, Node3D> _regionRoots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _regionAnchorRoots = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, (uint Layer, uint Mask)> _worldCollisionState = new();
    private readonly Dictionary<ulong, Godot.Environment?> _worldEnvironmentState = new();
    private Vector3 _regionSafePosition;
    private bool _waterToastShown;

    private readonly Dictionary<string, AnchorNode> _anchors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WebVisual> _webViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FlyView> _insectViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkerView> _workerViews = new(StringComparer.Ordinal);
    private readonly List<GameplayEvent> _eventLog = new();

    private WebType? _tool;
    private string? _anchorA;
    private AnchorNode? _hoverAnchor;
    private string _previewSignature = "";
    private double _accumulator;
    private double _autosaveTimer;
    private double _hudTimer;
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

    public void Configure(SettingsData settings, SaveService saves, bool probeMode = false)
    {
        Settings = settings;
        Settings.Normalise();
        _saves = saves;
        _probeMode = probeMode;
    }

    public override void _Ready()
    {
        if (ValidationOnly) return;
        _saves ??= GameData.CreateSaveService();
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
        _anchorsRoot = _gardenAnchors;
        _websRoot = GetRequired<Node3D>("Webs");
        _insectsRoot = GetRequired<Node3D>("Insects");
        _workersRoot = GetRequired<Node3D>("Workers");
        _previewRoot = GetRequired<Node3D>("WebPreview");

        BindAnchors(RegionIds.GardenClearing);
        BuildMarkers();
        BuildWind();
        BindPlayer();
        BuildInterface();
    }

    private T GetRequired<T>(string path) where T : Node =>
        GetNodeOrNull<T>(path) ?? throw new InvalidOperationException(
            $"GardenClearing.tscn is missing mandatory node '{path}' ({typeof(T).Name}).");

    private void BindAnchors(string regionId)
    {
        var region = RegionCatalog.GetRequired(regionId);
        var definitions = region.Anchors.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        _anchors.Clear();
        foreach (var child in _anchorsRoot.GetChildren())
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

        if (_anchors.Count != definitions.Count)
            throw new InvalidOperationException(
                $"Region '{regionId}' authors {_anchors.Count} anchor sockets but the simulation defines {definitions.Count}.");
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

        _travelLabel = new Label3D
        {
            Text = "E · Travel",
            FontSize = 30,
            PixelSize = 0.006f,
            Modulate = UiKit.Gold,
            OutlineModulate = UiKit.Pine,
            OutlineSize = 8,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Visible = false
        };
        AddChild(_travelLabel);
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
        _started = true;
        _eventLog.Clear();
        _victoryShown = false;
        _bridgeCrossToastShown = false;
        _autosaveTimer = 0;
        _baselineTuning = GameData.LoadTuning();
        var tuning = _baselineTuning.Clone();
        GameData.ApplyDifficulty(tuning, _baselineTuning, Settings.LowThreat);
        _sim = new GameSimulation(tuning, GameData.LoadMissions());
        ActivateRegion(RegionIds.GardenClearing);
        ResetPresentation();
        _player.GlobalPosition = ToVector3(WorldLayout.PlayerSpawn);
        _regionSafePosition = _player.GlobalPosition;
        _player.FallResetPosition = ToVector3(WorldLayout.PlayerSpawn);
        _player.ResetCamera();
        _player.InputEnabled = true;
        _ui.ShowToast("Welcome to the garden. Use WASD to explore.", UiKit.Pine, 4.5f);
        _ui.SetStatus(_sim.GetActiveRegionHint());
        RefreshAll();
        ProcessEvents();
    }

    public void StartFromSave(SaveGameData data)
    {
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
        GameData.ApplyDifficulty(tuning, _baselineTuning, Settings.LowThreat);
        _sim = GameSimulation.FromSave(data, tuning, GameData.LoadMissions());
        ActivateRegion(_sim.ActiveRegionId);
        ResetPresentation();
        var region = _sim.ActiveRegion;
        var savedPlayer = _sim.GetRegionPlayerPosition(region.Id);
        _player.GlobalPosition = ClampSavedPlayerPosition(savedPlayer.Position, region);
        _regionSafePosition = _player.GlobalPosition;
        _player.FallResetPosition = ToVector3(region.PlayerSpawn);
        _player.CameraPivot.Rotation = new Vector3(0, (float)savedPlayer.RotationY, 0);
        _player.InputEnabled = true;
        _ui.ShowToast($"Welcome back to {region.DisplayName}.", UiKit.Pine, 4.0f);
        _ui.SetStatus(_sim.GetActiveRegionHint());
        RefreshAll();
        ProcessEvents();
    }

    private static Vector3 ClampSavedPlayerPosition(Vec3 position, RegionDefinition region)
    {
        var value = ToVector3(position);
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || value.Y < -3f)
            return ToVector3(region.PlayerSpawn);
        return value;
    }

    /// <summary>
    /// Switch the authored region presentation without duplicating the player,
    /// UI, audio, simulation or runtime roots. The simulation keeps all regional
    /// construction; only views are rebuilt for the active region.
    /// </summary>
    private void ActivateRegion(string regionId)
    {
        var region = RegionCatalog.GetRequired(regionId);
        if (regionId == _activeRegionId && _anchorsRoot is not null)
        {
            BindAnchors(regionId);
            UpdateRegionLabels(region);
            return;
        }

        if (regionId == RegionIds.GardenClearing)
        {
            _gardenWorld.Visible = true;
            _gardenAnchors.Visible = true;
            _gardenPassage.Visible = true;
            SetWorldCollisionActive(_gardenWorld, true);
            SetAnchorRegionActive(_gardenAnchors, true);
            foreach (var (id, root) in _regionRoots)
            {
                root.Visible = false;
                if (root.GetNodeOrNull<Node3D>("World") is { } regionWorld)
                    SetWorldCollisionActive(regionWorld, false);
                if (_regionAnchorRoots.TryGetValue(id, out var anchors))
                    SetAnchorRegionActive(anchors, false);
            }
            _anchorsRoot = _gardenAnchors;
        }
        else
        {
            var regionRoot = EnsureRegionScene(regionId);
            _gardenWorld.Visible = false;
            _gardenAnchors.Visible = false;
            _gardenPassage.Visible = false;
            SetWorldCollisionActive(_gardenWorld, false);
            SetAnchorRegionActive(_gardenAnchors, false);
            foreach (var (id, other) in _regionRoots)
            {
                if (id == regionId) continue;
                other.Visible = false;
                if (other.GetNodeOrNull<Node3D>("World") is { } otherWorld)
                    SetWorldCollisionActive(otherWorld, false);
                if (_regionAnchorRoots.TryGetValue(id, out var otherAnchors))
                    SetAnchorRegionActive(otherAnchors, false);
            }
            regionRoot.Visible = true;
            if (regionRoot.GetNodeOrNull<Node3D>("World") is { } regionWorld)
                SetWorldCollisionActive(regionWorld, true);
            _anchorsRoot = _regionAnchorRoots[regionId];
            SetAnchorRegionActive(_anchorsRoot, true);
        }

        _activeRegionId = regionId;
        BindAnchors(regionId);
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

    /// <summary>
    /// Hiding a region is not enough: its StaticBody3D ground must also stop
    /// colliding, otherwise the loaded garden floor would sit underneath the pond.
    /// Original layers are remembered so water areas and ground restore exactly.
    /// </summary>
    private void SetWorldCollisionActive(Node root, bool active)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is CollisionObject3D body)
            {
                var key = body.GetInstanceId();
                if (active)
                {
                    if (_worldCollisionState.TryGetValue(key, out var saved))
                    {
                        body.CollisionLayer = saved.Layer;
                        body.CollisionMask = saved.Mask;
                    }
                }
                else
                {
                    if (!_worldCollisionState.ContainsKey(key))
                        _worldCollisionState[key] = (body.CollisionLayer, body.CollisionMask);
                    body.CollisionLayer = 0;
                    body.CollisionMask = 0;
                }
            }
            if (child is WorldEnvironment worldEnvironment)
            {
                var key = worldEnvironment.GetInstanceId();
                if (active)
                {
                    if (_worldEnvironmentState.TryGetValue(key, out var saved))
                        worldEnvironment.Environment = saved;
                }
                else
                {
                    if (!_worldEnvironmentState.ContainsKey(key))
                        _worldEnvironmentState[key] = worldEnvironment.Environment;
                    worldEnvironment.Environment = null;
                }
            }
            if (child is Light3D light) light.Visible = active;
            SetWorldCollisionActive(child, active);
        }
    }

    private void UpdateRegionLabels(RegionDefinition region)
    {
        _nurseryLabel.Text = $"{region.NurseryLabel} · E";
        _nurseryLabel.Position = ToVector3(region.NurseryPosition) + new Vector3(0, 2.25f, 0);
        _climbLabel.Visible = false;
        _travelLabel.Position = ToVector3(region.TravelPoint) + new Vector3(0, 2.0f, 0);
        _travelLabel.Text = region.Id == RegionIds.GardenClearing
            ? "E · Path to Reedwater Bank"
            : "E · Return to Garden Clearing";
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
        UpdatePlayerContext();
        UpdateViews(dt);
        UpdateSuggestions();
        UpdateContext();
        UpdatePreview();

        UpdateWaterHazard();

        if (_player is not null && _player.GlobalPosition.Y < -3.1f && !_fallToastShown)
        {
            _fallToastShown = true;
            _ui.ShowToast(_activeRegionId == RegionIds.GardenClearing
                ? "Whoops! The ravine sends you back to the home leaf."
                : "Whoops! The bank sends you back to your safe spot.", UiKit.Coral, 4f);
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
        foreach (var insect in _sim.RegionInsects)
        {
            if (!_insectViews.TryGetValue(insect.Id, out var view)) continue;
            view.UpdateView(delta, Settings.ReducedMotion);
            view.SetPromptVisible(Vector3DistanceToPlayer(ToVector3(insect.Position), 3.2f));
        }
        foreach (var worker in _sim.RegionWorkers)
        {
            if (!_workerViews.TryGetValue(worker.Id, out var view)) continue;
            view.UpdateView(delta, Settings.ReducedMotion, Vector3DistanceToPlayer(ToVector3(worker.Position), 4f));
        }

        var nearbyNursery = Vector3DistanceToPlayer(NurseryInteractionPoint, 3.2f);
        var nurseryStep = _activeRegionId == RegionIds.GardenClearing &&
                          _sim.Missions.ActiveMissionId == "M02" && _sim.Workers.Count == 0;
        _nurseryLabel.Visible = nearbyNursery || nurseryStep;
        _climbLabel.Visible = _activeRegionId == RegionIds.GardenClearing &&
                              _player.ClimbAvailable && !_player.IsClimbing;
        _travelLabel.Visible = Vector3DistanceToPlayer(TravelInteractionPoint, 2.4f);

        var wind = _sim.WeatherPhase != WeatherPhase.Calm;
        _windParticles.Emitting = wind && !Settings.ReducedMotion && _activeRegionId == RegionIds.GardenClearing;
    }

    private void UpdateSuggestions()
    {
        if (_sim is null) return;
        foreach (var anchor in _anchors.Values) anchor.SetSuggested(false);
        if (_tool is not null) return;

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
            Suggest(ReedwaterLayout.EasyA);
            Suggest(ReedwaterLayout.EasyB);
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
            text = _anchorA is null ? "Right-click or Esc cancels" : "Right-click cancels · green preview is safe";
        }
        else if (Vector3DistanceToPlayer(TravelInteractionPoint, 2.4f))
        {
            text = _activeRegionId == RegionIds.GardenClearing
                ? (_sim.ReedwaterUnlocked
                    ? "E · Travel to Reedwater Bank"
                    : "E · Path to Reedwater Bank (finish the garden story)")
                : "E · Return to Garden Clearing";
        }
        else if (Vector3DistanceToPlayer(NurseryInteractionPoint, 2.8f))
        {
            text = $"E · {_sim.ActiveRegion.NurseryLabel} — hatch a helper";
        }
        else if (_sim.RegionInsects.Any(i => i.CanBeCollected && Vector3DistanceToPlayer(ToVector3(i.Position), 3.2f)))
        {
            text = "E · Collect the caught insect";
        }
        else if (_sim.RegionWebs.Any(w => w.Durability < w.MaxDurability && Vector3DistanceToPlayer(WebCenter(w), 3.2f)))
        {
            text = "E · Repair web (2 silk)";
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
        if (_tool is null || _anchorA is null || _hoverAnchor is null || _hoverAnchor.Definition.Id == _anchorA)
        {
            RemovePreview();
            if (_tool is not null)
            {
                _ui.SetStatus(_anchorA is null ? "Choose the first glowing anchor." : "Now choose the second glowing anchor.");
            }
            return;
        }

        var result = _sim.Preview(_anchorA, _hoverAnchor.Definition.Id, _tool.Value);
        foreach (var anchor in _anchors.Values)
        {
            if (anchor == _hoverAnchor)
                anchor.SetHovered(true, result.IsValid);
            else if (anchor.IsHovered)
                anchor.SetHovered(false);
        }
        var signature = $"{_tool}:{_anchorA}:{_hoverAnchor.Definition.Id}:{result.IsValid}";
        if (signature == _previewSignature)
        {
            _ui.SetStatus(result.IsValid
                ? $"Click to build · {result.SilkCost} silk"
                : result.Reason);
            return;
        }
        _previewSignature = signature;
        RemovePreview();
        var a = _sim.GetAnchor(_anchorA);
        var b = _sim.GetAnchor(_hoverAnchor.Definition.Id);
        if (a is null || b is null) return;
        _preview = WebVisual.CreatePreview(_tool.Value, ToVector3(a.Position), ToVector3(b.Position), result.IsValid);
        _previewRoot.AddChild(_preview);
        _ui.SetStatus(result.IsValid
            ? $"Click to build · {result.SilkCost} silk"
            : result.Reason);
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
                _ui.ShowToast("A fly is caught! Walk close and press E.", UiKit.Gold, 4f);
                break;
            case "FoodCollected":
                _audio.Play("collect");
                _ui.ShowToast($"+{evt.Amount} food", UiKit.LeafDark, 2.8f);
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
                _ui.ShowToast($"Reedwater objective {evt.SubjectId} complete!", UiKit.LeafDark, 4.5f);
                _ui.SetStatus(_sim.GetActiveRegionHint());
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
                _ui.ShowVictory(_sim);
                break;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_started || _sim is null) return;
        if (_settingsDialog.Visible) return;

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

        if (@event.IsActionPressed("colony"))
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
                var anchor = RaycastAnchor(mouse.Position);
                if (anchor is not null)
                {
                    OnAnchorClicked(anchor);
                    GetViewport().SetInputAsHandled();
                }
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (_tool is not null && !_ui.BlocksWorldInput)
            {
                var anchor = RaycastAnchor(motion.Position);
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

    private AnchorNode? RaycastAnchor(Vector2 screenPosition)
    {
        if (_player?.Camera is null) return null;
        var from = _player.Camera.ProjectRayOrigin(screenPosition);
        var to = from + _player.Camera.ProjectRayNormal(screenPosition) * 90f;
        var query = PhysicsRayQueryParameters3D.Create(from, to, 4);
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return null;
        return hit["collider"].AsGodotObject() as AnchorNode;
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
        if (_anchorA is null)
        {
            _anchorA = anchor.Definition.Id;
            anchor.SetSelected(true);
            _hoverAnchor = anchor;
            RemovePreview();
            _ui.SetStatus($"First anchor: {anchor.Definition.Label}. Choose the second glowing anchor.");
            _audio.Play("click");
            return;
        }
        if (_anchorA == anchor.Definition.Id)
        {
            _anchors[_anchorA].SetSelected(false);
            _anchorA = null;
            _ui.SetStatus("Choose the first glowing anchor.");
            return;
        }

        var result = _sim.Preview(_anchorA, anchor.Definition.Id, _tool!.Value);
        if (!result.IsValid)
        {
            _audio.Play("cancel");
            _ui.SetStatus(result.Reason);
            _ui.ShowToast(result.Reason, UiKit.Coral, 3.4f);
            anchor.SetHovered(true, false);
            return;
        }

        if (_sim.TryBuildWeb(_anchorA, anchor.Definition.Id, _tool.Value, out _, out _))
        {
            var tool = _tool.Value;
            ProcessEvents();
            ClearBuild();
            _ui.SetStatus(tool == WebType.Capture ? "Capture web built! Watch for a fly." : "Silk bridge built!");
            _autosaveTimer = 0;
            SaveGame(false, true);
        }
        else
        {
            _audio.Play("cancel");
            _ui.SetStatus(result.Reason);
        }
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
        _hoverAnchor = null;
        RemovePreview();
        foreach (var anchor in _anchors.Values)
        {
            anchor.SetBuildMode(type is not null);
            anchor.SetSelected(false);
            anchor.SetHovered(false);
        }
        _player.InputEnabled = type is null;
        _ui.SetBuildMode(type, null, _sim);
        if (type is not null && !silent)
        {
            _audio.Play("click");
            _ui.ShowToast(type == WebType.Capture
                ? "Click two glowing anchors to spin a capture web. Right-click cancels."
                : "Click two glowing anchors to build a silk bridge. Right-click cancels.", UiKit.TealDark, 4f);
        }
        if (type is null) _ui.SetStatus(_sim.GetActiveRegionHint());
    }

    private void ClearBuild()
    {
        if (_tool is null && _anchorA is null) return;
        _tool = null;
        _anchorA = null;
        _hoverAnchor = null;
        RemovePreview();
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
        var player = _player.ToCorePosition();
        if (_sim.TryCollectNearest(player, 3.2, out var insect, out var food, out _))
        {
            _audio.Play("collect");
            _ui.ShowToast($"+{food} food from the {insect?.Definition.Kind ?? "insect"}.", UiKit.LeafDark, 3.2f);
            ProcessEvents();
            return;
        }
        if (_sim.TryRepairNearest(player, 3.2, out _))
        {
            _audio.Play("repair");
            _ui.ShowToast("Web repaired for 2 silk.", UiKit.TealDark, 3f);
            ProcessEvents();
            return;
        }
        if (Vector3DistanceToPlayer(NurseryInteractionPoint, 2.8f))
        {
            _audio.Play("click");
            _ui.OpenNursery(_sim);
            return;
        }
        if (Vector3DistanceToPlayer(TravelInteractionPoint, 2.4f))
        {
            _audio.Play("click");
            OpenTravelDialog();
            return;
        }
        _ui.ShowToast("Move closer to a caught insect, a damaged web, or the nursery.", UiKit.Coral, 4f);
    }

    private void OpenTravelDialog()
    {
        if (_sim is null) return;
        if (_activeRegionId == RegionIds.GardenClearing)
        {
            var unlocked = _sim.ReedwaterUnlocked;
            _ui.OpenTravel(
                "Reedwater Bank",
                "A shallow pond, reed beds and mossy islands lie beyond the east hedge. Your webs and helpers can cross the pond with silk bridges.",
                unlocked,
                unlocked ? "" : "Finish First Threads, Tiny Team and Storm-Smart to open the path.",
                () => TravelToRegion(RegionIds.ReedwaterBank));
        }
        else
        {
            _ui.OpenTravel(
                "Garden Clearing",
                "Return along the marked trail to your sunny home clearing, leaf nursery and ravine.",
                true,
                "",
                () => TravelToRegion(RegionIds.GardenClearing));
        }
    }

    private void TravelToRegion(string regionId)
    {
        if (_sim is null || _player is null) return;
        if (!_sim.TryTravelTo(regionId, out var reason))
        {
            _ui.CloseModal();
            _ui.ShowToast(reason, UiKit.Coral, 4f);
            return;
        }

        _ui.CloseModal();
        var region = _sim.ActiveRegion;
        _sim.SetRegionPlayerPosition(_activeRegionId, _player.ToCorePosition(), _player.CameraPivot.Rotation.Y);
        ActivateRegion(regionId);
        ResetPresentation();
        _player.GlobalPosition = ToVector3(region.ArrivalPoint);
        _player.Velocity = Vector3.Zero;
        _regionSafePosition = _player.GlobalPosition;
        _player.FallResetPosition = ToVector3(region.PlayerSpawn);
        _player.ResetCamera();
        _player.InputEnabled = true;
        _ui.UpdateHud(_sim, Settings);
        _ui.ShowToast($"Welcome to {region.DisplayName}.", UiKit.TealDark, 4.5f);
        _ui.SetStatus(_sim.GetActiveRegionHint());
        RefreshAll();
        ProcessEvents();
        SaveGame(false, true);
    }

    private void TryDismantleNearest()
    {
        if (_sim is null || _ui.BlocksWorldInput) return;
        if (_sim.TryDismantleNearest(_player.ToCorePosition(), 3.2, out var silk, out var reason))
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

    public bool SaveGame(bool showToast, bool quiet = false)
    {
        if (_sim is null || _player is null) return false;
        var data = _sim.ExportSave(_player.ToCorePosition(), _player.CameraPivot.Rotation.Y, Settings);
        var result = SimulateSaveFailureForProbe
            ? SaveResult.Fail("Simulated save failure", "simulated")
            : _saves.Save(data);
        LastSaveError = result.Error ?? "";
        if (showToast) _ui.ShowSaveResult(result.Success, result.Message);
        else if (!result.Success || (!quiet && result.Success)) _ui.ShowSaveResult(result.Success, result.Message);
        return result.Success;
    }

    private void RestartNewGame()
    {
        _ui.CloseModal();
        StartNew();
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
        foreach (var web in _sim.RegionWebs)
        {
            if (_webViews.ContainsKey(web.Id)) continue;
            var a = _sim.GetAnchor(web.AnchorA);
            var b = _sim.GetAnchor(web.AnchorB);
            if (a is null || b is null) continue;
            var view = new WebVisual { Name = $"Web_{web.Id}" };
            _websRoot.AddChild(view);
            view.Configure(web, ToVector3(a.Position), ToVector3(b.Position));
            _webViews[web.Id] = view;
        }
        var activeIds = _sim.RegionWebs.Select(web => web.Id).ToHashSet(StringComparer.Ordinal);
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
        foreach (var insect in _sim.RegionInsects)
        {
            if (_insectViews.ContainsKey(insect.Id)) continue;
            var view = new FlyView { Name = $"Insect_{insect.Id}" };
            _insectsRoot.AddChild(view);
            view.Configure(insect);
            _insectViews[insect.Id] = view;
        }
    }

    private void SyncWorkerViews()
    {
        if (_sim is null) return;
        foreach (var worker in _sim.RegionWorkers)
        {
            if (_workerViews.ContainsKey(worker.Id)) continue;
            var view = new WorkerView { Name = $"Worker_{worker.Id}" };
            _workersRoot.AddChild(view);
            view.Configure(worker);
            _workerViews[worker.Id] = view;
        }
    }

    private Vector3 NurseryInteractionPoint =>
        _sim is not null
            ? ToVector3(_sim.ActiveRegion.NurseryPosition) + new Vector3(0, 0.4f, 0)
            : ToVector3(WorldLayout.NurseryPosition) + new Vector3(0, 0.4f, 0);

    private Vector3 TravelInteractionPoint =>
        ToVector3(_sim?.ActiveRegion.TravelPoint ?? WorldLayout.TravelPoint) + new Vector3(0, 0.2f, 0);

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
            if (_player.IsOnFloor() && !_player.IsClimbing)
                _regionSafePosition = _player.GlobalPosition;
            return;
        }

        var space = GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
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
        var groundQuery = PhysicsRayQueryParameters3D.Create(
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
        return (ToVector3(a.Position) + ToVector3(b.Position)) * 0.5f;
    }

    public static Vector3 ToVector3(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

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
    }

    public void ProbeTeleport(Vec3 position)
    {
        if (_player is null) return;
        _player.GlobalPosition = ToVector3(position);
        _player.Velocity = Vector3.Zero;
    }

    public void ProbeLookAt(Vec3 target)
    {
        if (_player is null) return;
        var dx = (float)target.X - _player.GlobalPosition.X;
        var dz = (float)target.Z - _player.GlobalPosition.Z;
        var yaw = Mathf.Atan2(-dx, -dz);
        _player.CameraPivot.Rotation = new Vector3(0, yaw, 0);
    }

    public void ProbeInteract() => TryInteract();

    public bool ProbeHatch()
    {
        if (_sim is null) return false;
        var success = _sim.TryStartHatch(out _);
        if (success) ProcessEvents();
        return success;
    }

    public bool ProbeRepairNearest()
    {
        if (_sim is null) return false;
        var success = _sim.TryRepairNearest(_player.ToCorePosition(), 4.5, out _);
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
        ActivateRegion(regionId);
        ResetPresentation();
        _player.GlobalPosition = ToVector3(region.ArrivalPoint);
        _player.Velocity = Vector3.Zero;
        _regionSafePosition = _player.GlobalPosition;
        _player.FallResetPosition = ToVector3(region.PlayerSpawn);
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
        var space = GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = 0.2f },
            Transform = new Transform3D(Basis.Identity, _player.GlobalPosition + new Vector3(0, 0.25f, 0))
        };
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        query.CollisionMask = 8;
        return space.IntersectShape(query, 8)
            .Any(hit => hit["collider"].AsGodotObject() is Area3D area && area.IsInGroup("water"));
    }

    public void ProbeOpenTravel() => OpenTravelDialog();
    public bool TravelModalOpen => _ui.IsTravelOpen;
    public bool RegionCompleteModalOpen => _ui.IsRegionCompleteOpen;
    public void ProbeSetRegionPlayer(Vec3 position)
    {
        _regionSafePosition = ToVector3(position);
        ProbeTeleport(position);
    }

    public void ProbeSetSafePosition(Vec3 position) => _regionSafePosition = ToVector3(position);

    public int VisibleAnchorCount => _anchors.Count;
    public int VisibleWebCount => _webViews.Count;
    public int VisibleInsectCount => _insectViews.Count;
    public int VisibleWorkerCount => _workerViews.Count;
    public bool VictoryModalOpen => _ui.IsVictoryOpen;
    public bool PauseModalOpen => _ui.IsPauseOpen;
    public bool HelpModalOpen => _ui.IsHelpOpen;
    public bool ColonyModalOpen => _ui.IsColonyOpen;
    public bool SaveFailedPromptOpen => _ui.IsSaveFailedOpen;
    public bool SimulateSaveFailureForProbe { get; set; }
    public bool PlayerPhysicsPaused => _player?.PhysicsPaused ?? false;
    public bool PlayerClimbing => _player?.IsClimbing ?? false;
    public Vector3 PlayerPosition => _player?.GlobalPosition ?? Vector3.Zero;
    public string ProbeJson { get; set; } = "";

    public Vector2 ProbeScreenPosition(Vec3 worldPosition) =>
        _player.Camera.UnprojectPosition(ToVector3(worldPosition));

    public bool ProbeClickAnchor(string anchorId)
    {
        if (!_anchors.TryGetValue(anchorId, out var anchor)) return false;
        OnAnchorClicked(anchor);
        return true;
    }
    public string LastSaveError { get; private set; } = "";
}
