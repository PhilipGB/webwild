using System.Text.Json;
using Godot;
using Webwild.Core;

namespace Webwild.Game;

/// <summary>Title menu, settings, continue/new-game flow and runtime probe host.</summary>
public partial class Main : Node3D
{
    public SettingsData Settings { get; private set; } = new();
    public SaveService Saves { get; private set; } = null!;
    public GardenClearing? Game => _game;
    public bool ProbeMode => _probeMode;
    public bool TitleVisible => _titleRoot?.Visible ?? false;
    public bool ContinueAvailable => _continueButton is not null && !_continueButton.Disabled;

    private SettingsService _settingsService = null!;
    private SettingsDialog _settingsDialog = null!;
    private CanvasLayer _settingsLayer = null!;
    private CanvasLayer _titleLayer = null!;
    private Control _titleRoot = null!;
    private Control? _helpOverlay;
    private Node3D? _backdrop;
    private Camera3D _titleCamera = null!;
    private Label _messageLabel = null!;
    private Button _continueButton = null!;
    private GardenClearing? _game;
    private bool _probeMode;
    private bool _sceneValidate;
    private float _orbit;
    private double _messageTime;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _probeMode = args.Any(a => a is "--runtime-probe" or "--probe");
        _sceneValidate = args.Any(a => a == "--scene-validate");
        if (_sceneValidate)
        {
            RunSceneValidation();
            return;
        }

        _settingsService = GameData.CreateSettingsService();
        Settings = _settingsService.Load();
        Saves = GameData.CreateSaveService();
        GameData.ApplyUiScale(Settings);
        GameData.ApplyFullscreen(Settings);

        BuildTitleBackdrop();
        BuildTitleUi();
        BuildSettings();

        if (_probeMode)
        {
            var probe = new RuntimeProbe { Name = "RuntimeProbe" };
            AddChild(probe);
            probe.Run(this);
        }
        else
        {
            RefreshContinue();
        }
    }

    private void BuildTitleBackdrop()
    {
        var scene = GD.Load<PackedScene>("res://scenes/boot/TitleBackdrop.tscn")
            ?? throw new InvalidOperationException("TitleBackdrop.tscn is missing.");
        _backdrop = scene.Instantiate<Node3D>();
        AddChild(_backdrop);
        _titleCamera = _backdrop.GetNode<Camera3D>("Camera3D");
    }

    private void RunSceneValidation()
    {
        var checks = new List<SceneValidationCheck>();
        var packed = GD.Load<PackedScene>("res://scenes/world/GardenClearing.tscn");
        GardenClearing? instance = null;
        if (packed is null)
        {
            checks.Add(new SceneValidationCheck("scene-loads", false, "GardenClearing.tscn is missing"));
        }
        else
        {
            instance = packed.Instantiate<GardenClearing>();
            instance.ValidationOnly = true;
            AddChild(instance);
            checks.AddRange(SceneContentValidator.Validate(instance));
        }

        var reedwaterPacked = GD.Load<PackedScene>("res://scenes/world/regions/ReedwaterBank/ReedwaterBank.tscn");
        ReedwaterRegion? reedwater = null;
        if (reedwaterPacked is null)
        {
            checks.Add(new SceneValidationCheck("rw-scene-loads", false,
                "ReedwaterBank.tscn is missing"));
        }
        else
        {
            reedwater = reedwaterPacked.Instantiate<ReedwaterRegion>();
            AddChild(reedwater);
            checks.AddRange(SceneContentValidator.ValidateReedwater(reedwater));
        }

        var passes = checks.Count(check => check.Pass);
        var failures = checks.Count - passes;
        var directory = GameData.ArtifactsPath;
        Directory.CreateDirectory(directory);
        var report = new
        {
            generatedUtc = DateTime.UtcNow,
            engine = Engine.GetVersionInfo().ToString(),
            renderer = DisplayServer.GetName(),
            passes,
            failures,
            checks = checks.Select(check => new { name = check.Name, pass = check.Pass, detail = check.Detail })
        };
        var path = Path.Combine(directory, "scene_validation.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        foreach (var check in checks)
            GD.Print($"[SCENE] {(check.Pass ? "PASS" : "FAIL")} {check.Name}" +
                     (string.IsNullOrWhiteSpace(check.Detail) ? "" : $" — {check.Detail}"));
        GD.Print($"[SCENE] SUMMARY passes={passes} failures={failures} report={path}");

        instance?.Free();
        reedwater?.Free();
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private void BuildTitleUi()
    {
        _titleLayer = new CanvasLayer { Name = "TitleLayer", Layer = 10 };
        AddChild(_titleLayer);

        _titleRoot = new Control { Name = "TitleRoot", MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiKit.CreateTheme() };
        _titleLayer.AddChild(_titleRoot);
        UiKit.FullRect(_titleRoot);

        var shade = new ColorRect
        {
            Color = new Color(0.05f, 0.11f, 0.08f, 0.14f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        UiKit.FullRect(shade);
        _titleRoot.AddChild(shade);

        var margin = new MarginContainer();
        margin.AnchorLeft = 0;
        margin.AnchorRight = 0;
        margin.AnchorTop = 0;
        margin.AnchorBottom = 1;
        margin.OffsetLeft = 54;
        margin.OffsetRight = 680;
        margin.OffsetTop = 26;
        margin.OffsetBottom = -20;
        margin.AddThemeConstantOverride("margin_left", 0);
        margin.AddThemeConstantOverride("margin_top", 0);
        _titleRoot.AddChild(margin);

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", UiKit.Panel(new Color(UiKit.Cream, 0.94f), 30, new Color(1, 1, 1, 0.18f), 2));
        margin.AddChild(panel);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        panel.AddChild(scroll);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(column);

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 12);
        var spiderIcon = UiKit.Icon("spider");
        if (spiderIcon is not null)
        {
            titleRow.AddChild(new TextureRect
            {
                Texture = spiderIcon,
                CustomMinimumSize = new Vector2(72, 72),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            });
        }
        var titleStack = new VBoxContainer();
        var title = UiKit.Label("Webwild", 64, UiKit.Pine);
        title.AddThemeConstantOverride("outline_size", 0);
        titleStack.AddChild(title);
        titleStack.AddChild(UiKit.Label("A tiny spider colony in one garden", 19, UiKit.TealDark));
        titleRow.AddChild(titleStack);
        column.AddChild(titleRow);

        column.AddChild(UiKit.Label(
            "Spin webs, catch insects, hatch helpers and keep your colony safe when the wind blows.",
            17, new Color(UiKit.Ink, 0.85f), true));
        column.AddChild(UiKit.Spacer(6));

        var newGame = UiKit.Button("New Garden", 420, 56, true);
        newGame.Pressed += StartNewGame;
        column.AddChild(newGame);

        _continueButton = UiKit.Button("Continue", 420, 52);
        _continueButton.Pressed += ContinueGame;
        column.AddChild(_continueButton);

        var help = UiKit.Button("How to Play", 420, 50);
        help.Pressed += OpenHelp;
        column.AddChild(help);

        var settings = UiKit.Button("Settings", 420, 50);
        settings.Pressed += OpenSettings;
        column.AddChild(settings);

        var quit = UiKit.Button("Quit", 420, 48);
        quit.Pressed += () => GetTree().Quit();
        column.AddChild(quit);

        _messageLabel = UiKit.Label("", 16, UiKit.Coral, true);
        _messageLabel.CustomMinimumSize = new Vector2(420, 34);
        column.AddChild(_messageLabel);

        if (UiKit.Icon("web") is { } webIcon)
        {
            var footer = new HBoxContainer();
            footer.AddThemeConstantOverride("separation", 6);
            footer.AddChild(new TextureRect
            {
                Texture = webIcon,
                CustomMinimumSize = new Vector2(24, 24),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
            });
            footer.AddChild(UiKit.Label($"v{GameData.Version} · offline · no ads", 15, new Color(UiKit.Ink, 0.6f)));
            column.AddChild(footer);
        }
    }

    private void BuildSettings()
    {
        _settingsLayer = new CanvasLayer { Name = "TitleSettingsLayer", Layer = 40 };
        AddChild(_settingsLayer);
        _settingsDialog = new SettingsDialog();
        _settingsLayer.AddChild(_settingsDialog);
        _settingsDialog.Configure(Settings);
        _settingsDialog.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(SettingsData settings)
    {
        CopySettings(settings, Settings);
        Settings.Normalise();
        GameData.ApplyUiScale(Settings);
        _game?.ApplySettings(Settings);
        SaveSettingsWithFeedback();
    }

    private void SaveSettingsWithFeedback()
    {
        var result = GameData.CreateSettingsService().Save(Settings);
        if (result.Success)
        {
            _settingsDialog.ClearPersistenceError();
            return;
        }
        _settingsDialog.ShowPersistenceError(
            "Couldn't save settings. Check disk space or permissions, then retry.",
            SaveSettingsWithFeedback);
        ShowMessage("Couldn't save settings. Try again.", UiKit.Coral);
    }

    private void RefreshContinue()
    {
        if (_continueButton is null) return;
        var result = Saves.Load();
        if (result.Success && result.Data is not null)
        {
            var data = result.Data;
            var mission = data.Missions.Completed.Count >= 3
                ? "Garden complete"
                : data.Missions.Completed.Count == 0
                    ? "M01 · First Threads"
                    : data.Missions.Completed.Count == 1 ? "M02 · Tiny Team" : "M03 · Storm-Smart";
            _continueButton.Disabled = false;
            _continueButton.Text = $"Continue · {mission}";
            if (result.RecoveredFromBackup) ShowMessage("Recovered your last good save.", UiKit.TealDark);
        }
        else
        {
            _continueButton.Disabled = true;
            _continueButton.Text = "Continue (no saved garden yet)";
        }
    }

    private void ContinueGame()
    {
        var result = Saves.Load();
        if (!result.Success || result.Data is null)
        {
            ShowMessage(result.Message, UiKit.Coral);
            RefreshContinue();
            return;
        }
        StartGame(result.Data);
    }

    public void ProbeReturnToTitle() => ReturnToTitle();

    public GardenClearing? ProbeContinue()
    {
        var result = Saves.Load();
        return result.Success && result.Data is not null ? StartGame(result.Data) : null;
    }

    private void StartNewGame() => StartGame(null);

    public GardenClearing StartGame(SaveGameData? data)
    {
        HideHelp();
        _settingsDialog.Close();
        _titleRoot.Visible = false;
        if (_backdrop is not null)
        {
            _backdrop.QueueFree();
            _backdrop = null;
        }
        _messageLabel.Text = "";

        if (_game is not null)
        {
            _game.QueueFree();
            _game = null;
        }

        var packed = GD.Load<PackedScene>("res://scenes/world/GardenClearing.tscn");
        if (packed is null)
            throw new InvalidOperationException(
                "GardenClearing.tscn is missing. A mandatory scene is not replaced procedurally.");
        var clearing = packed.Instantiate<GardenClearing>()
            ?? throw new InvalidOperationException(
                "GardenClearing.tscn did not instantiate as GardenClearing.");
        clearing.Name = "GardenClearing";
        clearing.Configure(Settings, Saves, _probeMode);
        AddChild(clearing);
        clearing.QuitToMenuRequested += ReturnToTitle;
        clearing.SettingsChanged += OnGameSettingsChanged;
        _game = clearing;
        if (data is null) clearing.StartNew(); else clearing.StartFromSave(data);
        return clearing;
    }

    private void OnGameSettingsChanged(SettingsData settings)
    {
        CopySettings(settings, Settings);
        Settings.Normalise();
        _settingsDialog.Configure(Settings);
        RefreshContinue();
    }

    private void ReturnToTitle()
    {
        if (_game is not null)
        {
            _game.QueueFree();
            _game = null;
        }
        BuildTitleBackdrop();
        _titleRoot.Visible = true;
        _titleCamera.Current = true;
        _orbit = 0;
        RefreshContinue();
    }

    private void OpenSettings()
    {
        _settingsDialog.Configure(Settings);
        _settingsDialog.Open();
    }

    private void OpenHelp()
    {
        if (_helpOverlay is not null) return;
        var root = new Control { Name = "HelpOverlay", MouseFilter = Control.MouseFilterEnum.Stop, Theme = UiKit.CreateTheme() };
        UiKit.FullRect(root);
        _titleLayer.AddChild(root);
        var dim = new ColorRect { Color = new Color(0.04f, 0.09f, 0.07f, 0.64f), MouseFilter = Control.MouseFilterEnum.Stop };
        UiKit.FullRect(dim);
        root.AddChild(dim);
        var center = new CenterContainer();
        UiKit.FullRect(center);
        root.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(640, 0) };
        center.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);
        var title = UiKit.Label("How to Play", 34, UiKit.Pine);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);
        foreach (var (icon, text) in new[]
                 {
                     ("spider", "WASD or arrow keys move. Space jumps."),
                     ("web", "Press 1, then click two glowing anchors to spin a capture web."),
                     ("bridge", "Press 2 and click two anchors to build a silk bridge."),
                     ("food", "Walk to a caught insect and press E to collect food."),
                     ("repair", "Press E beside a damaged web to repair it for 2 silk."),
                     ("nest", "Press E at the leaf nursery to hatch a helper (up to 5 spiders)."),
                     ("wind", "Wind is forecast before it arrives. Sheltered webs near the log take less damage."),
                     ("quest", "Right-click or Esc cancels a build. X dismantles a nearby web."),
                     ("silk", "Tab shows your colony. Esc pauses the garden.")
                 })
        {
            column.AddChild(UiKit.IconLabel(icon, text, 18));
        }
        column.AddChild(UiKit.Spacer(4));
        var close = UiKit.Button("Got it", 260, 56, true);
        close.Pressed += HideHelp;
        column.AddChild(close);
        _helpOverlay = root;
    }

    private void HideHelp()
    {
        if (_helpOverlay is null) return;
        _helpOverlay.QueueFree();
        _helpOverlay = null;
    }

    private void ShowMessage(string text, Color color)
    {
        _messageLabel.Text = text;
        _messageLabel.AddThemeColorOverride("font_color", color);
        _messageTime = 6;
    }

    public override void _Process(double delta)
    {
        if (_backdrop is not null && _titleRoot.Visible)
        {
            _orbit += (float)delta * 0.08f;
            var radius = 21f;
            _titleCamera.Position = new Vector3(Mathf.Sin(_orbit) * radius - 2f, 8.4f, Mathf.Cos(_orbit) * radius + 1f);
            _titleCamera.LookAt(new Vector3(-1.5f, 1.1f, 0), Vector3.Up);
        }
        if (_messageTime > 0)
        {
            _messageTime -= delta;
            if (_messageTime <= 0) _messageLabel.Text = "";
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel") && !@event.IsActionPressed("cancel")) return;
        if (_settingsDialog.Visible)
        {
            _settingsDialog.Close();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_helpOverlay is not null)
        {
            HideHelp();
            GetViewport().SetInputAsHandled();
        }
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
}
