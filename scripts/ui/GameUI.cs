using Godot;
using Webwild.Core;

namespace Webwild.Game;

public partial class GameUI : CanvasLayer
{
    public event Action<WebType>? BuildToolRequested;
    public event Action? CancelRequested;
    public event Action? InteractRequested;
    public event Action? DismantleRequested;
    public event Action? PauseRequested;
    public event Action? SaveRequested;
    public event Action? QuitToMenuRequested;
    public event Action? SettingsRequested;
    public event Action? NewGameRequested;
    public event Action? HatchRequested;
    public event Action? RecoveryRequested;
    public event Action? ContinuePlayingRequested;

    private Control _root = null!;
    private Label _foodLabel = null!;
    private Label _silkLabel = null!;
    private Label _missionTitle = null!;
    private Label _missionHint = null!;
    private VBoxContainer _stepsBox = null!;
    private Label _masteryLabel = null!;
    private Label _statusLabel = null!;
    private Label _contextLabel = null!;
    private Label _toastLabel = null!;
    private PanelContainer _toastPanel = null!;
    private PanelContainer _warningPanel = null!;
    private Label _warningLabel = null!;
    private Button _captureButton = null!;
    private Button _bridgeButton = null!;
    private Button _helpButton = null!;
    private Button _colonyButton = null!;
    private Button _interactButton = null!;
    private Button _dismantleButton = null!;
    private Button _cancelButton = null!;
    private Button _pauseButton = null!;
    private Control _modalRoot = null!;
    private PanelContainer _modalPanel = null!;
    private VBoxContainer _modalContent = null!;
    private Label _modalTitle = null!;
    private bool _pauseOpen;
    private bool _nurseryOpen;
    private bool _victoryOpen;
    private bool _colonyOpen;
    private bool _helpOpen;
    private bool _confirmOpen;
    private bool _saveFailedOpen;
    private float _toastTimer;
    private float _toastDuration;
    private GameSimulation? _lastSim;
    private WebType? _activeTool;
    private string? _anchorA;
    private string _missionSignature = "";
    private bool _tutorialPrompts = true;
    private readonly List<Button> _workerChips = new();

    public bool IsModalOpen => _pauseOpen || _nurseryOpen || _victoryOpen || _colonyOpen || _helpOpen || _confirmOpen || _saveFailedOpen;
    public bool PausesSimulation => _pauseOpen || _victoryOpen || _helpOpen || _confirmOpen || _saveFailedOpen;
    public bool BlocksWorldInput => IsModalOpen;
    public bool IsVictoryOpen => _victoryOpen;
    public bool IsPauseOpen => _pauseOpen;
    public bool IsHelpOpen => _helpOpen;
    public bool IsColonyOpen => _colonyOpen;
    public bool IsNurseryOpen => _nurseryOpen;
    public bool IsSaveFailedOpen => _saveFailedOpen;

    public override void _Ready()
    {
        Layer = 10;
        BuildHud();
    }

    private void BuildHud()
    {
        _root = new Control { Name = "HudRoot", MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiKit.CreateTheme() };
        AddChild(_root);
        UiKit.FullRect(_root);

        // Top-left resources.
        var left = new HBoxContainer { Position = new Vector2(22, 18) };
        left.AddThemeConstantOverride("separation", 10);
        left.AddChild(ResourcePill("food", out _foodLabel));
        left.AddChild(ResourcePill("silk", out _silkLabel));
        _root.AddChild(left);

        // A compact action column keeps the primary build buttons uncluttered
        // while making keyboard actions discoverable by mouse too.
        var actions = new VBoxContainer { Position = new Vector2(22, 82) };
        actions.AddThemeConstantOverride("separation", 6);
        _interactButton = ActionButton("E · Interact", "food", () => InteractRequested?.Invoke());
        _dismantleButton = ActionButton("X · Remove web", "web", () => DismantleRequested?.Invoke());
        _cancelButton = ActionButton("Cancel build", "cancel", () => CancelRequested?.Invoke());
        _cancelButton.Visible = false;
        _pauseButton = ActionButton("Esc · Pause", "settings", () => PauseRequested?.Invoke());
        actions.AddChild(_interactButton);
        actions.AddChild(_dismantleButton);
        actions.AddChild(_cancelButton);
        actions.AddChild(_pauseButton);
        _root.AddChild(actions);

        // Top-right mission.
        var missionPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(440, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        missionPanel.AnchorLeft = 1;
        missionPanel.AnchorRight = 1;
        missionPanel.AnchorTop = 0;
        missionPanel.AnchorBottom = 0;
        missionPanel.OffsetLeft = -460;
        missionPanel.OffsetRight = -20;
        missionPanel.OffsetTop = 18;
        missionPanel.OffsetBottom = 18;
        missionPanel.GrowHorizontal = Control.GrowDirection.Begin;
        missionPanel.GrowVertical = Control.GrowDirection.End;
        var missionBox = new VBoxContainer();
        missionBox.AddThemeConstantOverride("separation", 4);
        _missionTitle = UiKit.Label("M01 · First Threads", 21, UiKit.Pine);
        _missionTitle.AddThemeColorOverride("font_color", UiKit.Pine);
        _missionHint = UiKit.Label("", 16, new Color(UiKit.Ink, 0.9f), true);
        _missionHint.CustomMinimumSize = new Vector2(390, 0);
        _stepsBox = new VBoxContainer();
        _stepsBox.AddThemeConstantOverride("separation", 2);
        _masteryLabel = UiKit.Label("", 15, UiKit.Violet);
        _masteryLabel.Visible = false;
        missionBox.AddChild(_missionTitle);
        missionBox.AddChild(_missionHint);
        missionBox.AddChild(_stepsBox);
        missionBox.AddChild(_masteryLabel);
        missionPanel.AddChild(missionBox);
        _root.AddChild(missionPanel);

        // Wind forecast banner.
        _warningPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(380, 0),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _warningPanel.AnchorLeft = 0.5f;
        _warningPanel.AnchorRight = 0.5f;
        _warningPanel.AnchorTop = 0;
        _warningPanel.AnchorBottom = 0;
        _warningPanel.OffsetLeft = -190;
        _warningPanel.OffsetRight = 190;
        _warningPanel.OffsetTop = 18;
        _warningPanel.OffsetBottom = 18;
        _warningPanel.GrowHorizontal = Control.GrowDirection.Both;
        _warningPanel.AddThemeStyleboxOverride("panel", UiKit.Panel(new Color(UiKit.Gold, 0.96f), 18, UiKit.Coral, 3));
        var warningBox = new HBoxContainer();
        warningBox.AddThemeConstantOverride("separation", 8);
        var windIcon = UiKit.Icon("wind");
        if (windIcon is not null)
            warningBox.AddChild(new TextureRect { Texture = windIcon, CustomMinimumSize = new Vector2(30, 30), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize });
        _warningLabel = UiKit.Label("Wind is coming!", 18, UiKit.Pine);
        warningBox.AddChild(_warningLabel);
        _warningPanel.AddChild(warningBox);
        _root.AddChild(_warningPanel);

        // Toolbar.
        var bottom = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(0, 0)
        };
        bottom.AnchorLeft = 0;
        bottom.AnchorRight = 1;
        bottom.AnchorTop = 1;
        bottom.AnchorBottom = 1;
        bottom.OffsetLeft = 0;
        bottom.OffsetRight = 0;
        bottom.OffsetTop = -170;
        bottom.OffsetBottom = -12;
        bottom.GrowVertical = Control.GrowDirection.Begin;
        bottom.AddThemeConstantOverride("separation", 7);
        bottom.MouseFilter = Control.MouseFilterEnum.Ignore;

        _statusLabel = UiKit.Label("", 19, UiKit.Cream);
        _statusLabel.AddThemeColorOverride("font_outline_color", UiKit.Pine);
        _statusLabel.AddThemeConstantOverride("outline_size", 6);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bottom.AddChild(_statusLabel);

        var toolbar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        toolbar.AddThemeConstantOverride("separation", 12);
        toolbar.MouseFilter = Control.MouseFilterEnum.Ignore;

        _captureButton = ToolbarButton("web", "Capture web  [1]", 218);
        _captureButton.Pressed += () => BuildToolRequested?.Invoke(WebType.Capture);
        _bridgeButton = ToolbarButton("bridge", "Silk bridge  [2]", 218);
        _bridgeButton.Pressed += () => BuildToolRequested?.Invoke(WebType.Bridge);
        _helpButton = UiKit.Button("?", 56, 62);
        _helpButton.TooltipText = "How to play";
        _helpButton.Pressed += OpenHelp;
        _colonyButton = UiKit.Button("Spiders 1/5  [Tab]", 184, 62);
        _colonyButton.Pressed += () => ToggleColony();
        toolbar.AddChild(_captureButton);
        toolbar.AddChild(_bridgeButton);
        toolbar.AddChild(_helpButton);
        toolbar.AddChild(_colonyButton);
        bottom.AddChild(toolbar);

        _contextLabel = UiKit.Label("", 18, UiKit.Cream);
        _contextLabel.AddThemeColorOverride("font_outline_color", UiKit.Pine);
        _contextLabel.AddThemeConstantOverride("outline_size", 6);
        _contextLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _contextLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bottom.AddChild(_contextLabel);
        _root.AddChild(bottom);

        // Toasts.
        _toastPanel = new PanelContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(520, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _toastPanel.AnchorLeft = 0.5f;
        _toastPanel.AnchorRight = 0.5f;
        _toastPanel.AnchorTop = 0;
        _toastPanel.AnchorBottom = 0;
        _toastPanel.OffsetLeft = -260;
        _toastPanel.OffsetRight = 260;
        _toastPanel.OffsetTop = 210;
        _toastPanel.OffsetBottom = 210;
        _toastPanel.GrowHorizontal = Control.GrowDirection.Both;
        _toastLabel = UiKit.Label("", 20, UiKit.Pine, true);
        _toastLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _toastLabel.CustomMinimumSize = new Vector2(470, 0);
        _toastPanel.AddChild(_toastLabel);
        _root.AddChild(_toastPanel);

        BuildModalRoot();
    }

    private PanelContainer ResourcePill(string icon, out Label label)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", UiKit.Panel(new Color(UiKit.Cream, 0.96f), 18, new Color(0, 0, 0, 0.08f), 1));
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        var texture = UiKit.Icon(icon);
        if (texture is not null)
            box.AddChild(new TextureRect { Texture = texture, CustomMinimumSize = new Vector2(32, 32), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize });
        label = UiKit.Label("0", 23, UiKit.Pine);
        label.CustomMinimumSize = new Vector2(46, 32);
        box.AddChild(label);
        panel.AddChild(box);
        return panel;
    }

    private Button ToolbarButton(string icon, string text, int width)
    {
        var button = UiKit.Button(text, width, 62);
        var texture = UiKit.Icon(icon);
        if (texture is not null)
        {
            button.Icon = texture;
            button.ExpandIcon = true;
            button.AddThemeConstantOverride("icon_max_width", 32);
        }
        button.AddThemeFontSizeOverride("font_size", 19);
        return button;
    }

    private Button ActionButton(string text, string icon, Action pressed)
    {
        var button = UiKit.Button(text, 196, 46);
        button.AddThemeFontSizeOverride("font_size", 16);
        var texture = UiKit.Icon(icon);
        if (texture is not null)
        {
            button.Icon = texture;
            button.ExpandIcon = true;
            button.AddThemeConstantOverride("icon_max_width", 22);
        }
        button.Pressed += pressed;
        return button;
    }

    private void BuildModalRoot()
    {
        _modalRoot = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        UiKit.FullRect(_modalRoot);
        _root.AddChild(_modalRoot);

        var dim = new ColorRect { Color = new Color(0.05f, 0.1f, 0.08f, 0.58f), MouseFilter = Control.MouseFilterEnum.Stop };
        UiKit.FullRect(dim);
        _modalRoot.AddChild(dim);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(center);
        _modalRoot.AddChild(center);

        _modalPanel = new PanelContainer { CustomMinimumSize = new Vector2(620, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        center.AddChild(_modalPanel);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            CustomMinimumSize = new Vector2(580, 560)
        };
        _modalPanel.AddChild(scroll);
        _modalContent = new VBoxContainer();
        _modalContent.AddThemeConstantOverride("separation", 10);
        _modalContent.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_modalContent);
        _modalTitle = UiKit.Label("", 30, UiKit.Pine);
        _modalTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _modalContent.AddChild(_modalTitle);
    }

    private void OpenModal(string title, int width = 620)
    {
        foreach (var child in _modalContent.GetChildren())
        {
            if (child == _modalTitle) continue;
            child.QueueFree();
        }
        _workerChips.Clear();
        _modalTitle.Text = title;
        _modalPanel.CustomMinimumSize = new Vector2(width, 0);
        _modalRoot.Visible = true;
    }

    public void CloseModal()
    {
        _pauseOpen = _nurseryOpen = _victoryOpen = _colonyOpen = _helpOpen = _confirmOpen = _saveFailedOpen = false;
        _modalRoot.Visible = false;
        foreach (var child in _modalContent.GetChildren())
        {
            if (child == _modalTitle) continue;
            child.QueueFree();
        }
        _workerChips.Clear();
        if (_lastSim is not null && !_victoryOpen) SetStatus(_lastSim.GetHint());
    }

    private void ConfigureModalState(bool pause = false, bool nursery = false, bool victory = false, bool colony = false, bool help = false, bool confirm = false, bool saveFailed = false)
    {
        _pauseOpen = pause;
        _nurseryOpen = nursery;
        _victoryOpen = victory;
        _colonyOpen = colony;
        _helpOpen = help;
        _confirmOpen = confirm;
        _saveFailedOpen = saveFailed;
        _modalRoot.Visible = true;
    }

    public void UpdateHud(GameSimulation sim, SettingsData settings)
    {
        _lastSim = sim;
        _foodLabel.Text = sim.Ledger.Food.ToString();
        _silkLabel.Text = sim.Ledger.Silk.ToString();
        _colonyButton.Text = $"Spiders {sim.TotalSpiders}/{sim.MaxSpiders}  [Tab]";
        _captureButton.Text = $"Capture web  [1]  {sim.Tuning.Webs.Capture.SilkCost} silk";
        _bridgeButton.Text = $"Silk bridge  [2]  {sim.Tuning.Webs.Bridge.SilkCost} silk";
        UpdateMission(sim);
        var warning = sim.WeatherPhase == WeatherPhase.Warning;
        _warningPanel.Visible = warning;
        if (warning)
            _warningLabel.Text = $"Wind in {(int)Math.Ceiling(sim.WeatherRemaining)}s — protect your web!";
        if (sim.MasteryAwarded)
        {
            _masteryLabel.Visible = true;
            _masteryLabel.Text = "★ Clever Weaver — 3 insects, 2 webs";
        }
        else
        {
            _masteryLabel.Visible = false;
        }
        UpdateToast((float)GetProcessDeltaTime());
    }

    public void SetTutorialPrompts(bool enabled)
    {
        _tutorialPrompts = enabled;
        _statusLabel.Visible = enabled;
        _contextLabel.Visible = enabled;
    }

    public void RefreshOpenPanels(GameSimulation sim)
    {
        if (_nurseryOpen) RefreshNursery(sim);
        if (_colonyOpen) RefreshColony(sim);
    }

    private void UpdateMission(GameSimulation sim)
    {
        var mission = sim.Missions.ActiveMission;
        var signature = mission is null
            ? "done"
            : $"{mission.Id}:{sim.Missions.StepIndex}:{string.Join(",", sim.Missions.StepProgressList)}:{sim.Missions.CompletedMissions.Count}";
        if (signature == _missionSignature) return;
        _missionSignature = signature;
        if (mission is null)
        {
            _missionTitle.Text = "Garden complete";
            _missionHint.Text = "Keep playing, weave new webs and fill the nursery.";
        }
        else
        {
            _missionTitle.Text = $"{mission.Id} · {mission.Title}";
            _missionHint.Text = mission.Hint;
        }
        foreach (var child in _stepsBox.GetChildren()) child.QueueFree();
        if (mission is not null)
        {
            for (var i = 0; i < mission.Steps.Count; i++)
            {
                var done = i < sim.Missions.StepIndex;
                var active = i == sim.Missions.StepIndex;
                var prefix = done ? "✓" : active ? "●" : "○";
                var text = MissionTracker.DescribeStep(mission.Steps[i]);
                var step = mission.Steps[i];
                if (step.Count > 1)
                    text += $" ({sim.Missions.StepProgress}/{step.Count})";
                var label = UiKit.Label($"{prefix}  {text}", 16,
                    done ? UiKit.TealDark : active ? UiKit.Pine : new Color(UiKit.Ink, 0.55f));
                _stepsBox.AddChild(label);
            }
        }
    }

    public void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }

    public void SetBuildMode(WebType? type, string? anchorA, GameSimulation sim)
    {
        _activeTool = type;
        _anchorA = anchorA;
        _captureButton.ButtonPressed = type == WebType.Capture;
        _bridgeButton.ButtonPressed = type == WebType.Bridge;
        _cancelButton.Visible = type is not null;
        if (type is null)
        {
            SetStatus(sim.GetHint());
        }
        else
        {
            var name = type == WebType.Capture ? "capture web" : "silk bridge";
            if (anchorA is null) SetStatus($"Building {name}: click the first glowing anchor.  Right-click cancels.");
            else SetStatus($"Now click the second anchor.  Right-click cancels.");
        }
    }

    public void ShowToast(string text, Color? color = null, float duration = 3.2f)
    {
        _toastLabel.Text = text;
        _toastLabel.AddThemeColorOverride("font_color", color ?? UiKit.Pine);
        _toastPanel.Visible = true;
        _toastPanel.Modulate = Colors.White;
        _toastTimer = duration;
        _toastDuration = duration;
    }

    private void UpdateToast(float delta)
    {
        if (!_toastPanel.Visible) return;
        _toastTimer -= delta;
        if (_toastTimer <= 0)
        {
            _toastPanel.Visible = false;
            return;
        }
        if (_toastTimer < 0.5f) _toastPanel.Modulate = new Color(1, 1, 1, _toastTimer / 0.5f);
    }

    public void SetContext(string? text)
    {
        _contextLabel.Text = text ?? "";
    }

    private bool _helpReturnToPause;

    public void OpenPause()
    {
        _helpReturnToPause = false;
        OpenModal("Paused", 500);
        ConfigureModalState(pause: true);
        _modalContent.AddChild(UiKit.Label("The garden is resting. Your webs are safe.", 18, new Color(UiKit.Ink, 0.8f), true));
        _modalContent.AddChild(UiKit.Spacer(8));
        var resume = UiKit.Button("Resume", 360, 58, true);
        resume.Pressed += CloseModal;
        _modalContent.AddChild(resume);
        var save = UiKit.Button("Save game", 360, 54);
        save.Pressed += () => SaveRequested?.Invoke();
        _modalContent.AddChild(save);
        var help = UiKit.Button("How to play", 360, 54);
        help.Pressed += OpenHelp;
        _modalContent.AddChild(help);
        var settings = UiKit.Button("Settings", 360, 54);
        settings.Pressed += () => SettingsRequested?.Invoke();
        _modalContent.AddChild(settings);
        var newGame = UiKit.Button("New game…", 360, 54);
        newGame.Pressed += ConfirmNewGame;
        _modalContent.AddChild(newGame);
        var quit = UiKit.Button("Save and quit to menu", 360, 54);
        quit.Pressed += () => QuitToMenuRequested?.Invoke();
        _modalContent.AddChild(quit);
    }

    public void OpenSaveFailedQuitPrompt(string message, Action retry, Action quitWithoutSaving)
    {
        OpenModal("Couldn't save", 560);
        ConfigureModalState(saveFailed: true);
        _modalContent.AddChild(UiKit.IconLabel("silk", "Your garden is still running.", 21, UiKit.Pine));
        _modalContent.AddChild(UiKit.Label(message, 18, UiKit.Coral, true));
        _modalContent.AddChild(UiKit.Label(
            "Try saving again, keep playing, or leave without saving. Nothing has been discarded yet.",
            17, new Color(UiKit.Ink, 0.8f), true));
        _modalContent.AddChild(UiKit.Spacer(6));
        var retryButton = UiKit.Button("Try saving again", 420, 58, true);
        retryButton.Pressed += () =>
        {
            CloseModal();
            retry?.Invoke();
        };
        _modalContent.AddChild(retryButton);
        var keepPlaying = UiKit.Button("Keep playing", 420, 52);
        keepPlaying.Pressed += CloseModal;
        _modalContent.AddChild(keepPlaying);
        var quit = UiKit.Button("Quit without saving", 420, 52);
        quit.Pressed += () =>
        {
            CloseModal();
            quitWithoutSaving?.Invoke();
        };
        _modalContent.AddChild(quit);
    }

    private void ConfirmNewGame()
    {
        OpenModal("Start a new garden?", 560);
        ConfigureModalState(confirm: true);
        _modalContent.AddChild(UiKit.Label("This replaces the garden you are playing now. Your last save will stay as a backup.", 19, UiKit.Ink, true));
        var yes = UiKit.Button("Start a new garden", 360, 58, true);
        yes.Pressed += () => NewGameRequested?.Invoke();
        _modalContent.AddChild(yes);
        var no = UiKit.Button("Go back", 360, 54);
        no.Pressed += OpenPause;
        _modalContent.AddChild(no);
    }

    public void OpenHelp()
    {
        _helpReturnToPause = _pauseOpen;
        OpenModal("How to play", 620);
        ConfigureModalState(help: true);
        var lines = new (string Icon, string Text)[]
        {
            ("spider", "WASD or arrow keys move. Space jumps."),
            ("web", "Press 1, then click two glowing anchors to spin a capture web."),
            ("bridge", "Press 2 and click two anchors to make a silk bridge."),
            ("food", "Walk to a caught insect and press E to collect food."),
            ("repair", "Press E beside a damaged web to repair it for 2 silk."),
            ("nest", "Press E at the leaf nursery to hatch a helper (up to 5 spiders)."),
            ("wind", "Wind is forecast before it arrives. Sheltered webs near the log take less damage."),
            ("quest", "Right-click or Esc cancels a build. X dismantles a nearby web."),
            ("silk", "Tab shows your colony. Esc pauses the game.")
        };
        foreach (var (icon, text) in lines)
            _modalContent.AddChild(UiKit.IconLabel(icon, text, 18));
        _modalContent.AddChild(UiKit.Spacer(6));
        var close = UiKit.Button("Got it", 260, 56, true);
        close.Pressed += () =>
        {
            if (_helpReturnToPause) OpenPause(); else CloseModal();
        };
        _modalContent.AddChild(close);
    }

    public void OpenNursery(GameSimulation sim)
    {
        OpenModal("Leaf Nursery", 600);
        ConfigureModalState(nursery: true);
        RefreshNursery(sim);
    }

    private void RefreshNursery(GameSimulation sim)
    {
        if (!_nurseryOpen) return;
        // Remove only dynamic content below the title.
        foreach (var child in _modalContent.GetChildren())
        {
            if (child != _modalTitle) child.QueueFree();
        }
        _workerChips.Clear();
        _modalContent.AddChild(UiKit.IconLabel("nest", $"{sim.TotalSpiders} of {sim.MaxSpiders} spiders live here", 22, UiKit.Pine));
        var body = UiKit.Label(sim.Workers.Count == 0
            ? "A little egg is waiting for food and silk. Hatch a helper and it will collect and repair on its own."
            : "Your helpers choose useful jobs. They cross silk bridges and keep webs repaired.", 18, new Color(UiKit.Ink, 0.86f), true);
        _modalContent.AddChild(body);
        var chips = new HBoxContainer();
        chips.AddThemeConstantOverride("separation", 6);
        chips.AddChild(Chip("You"));
        foreach (var worker in sim.Workers) chips.AddChild(Chip($"{worker.Name}\n{worker.DisplayState}"));
        _modalContent.AddChild(chips);
        _modalContent.AddChild(UiKit.Spacer(6));
        var can = sim.CanHatch();
        var hatch = UiKit.Button($"Hatch helper  ·  {sim.Tuning.Nursery.HatchFoodCost} food + {sim.Tuning.Nursery.HatchSilkCost} silk", 480, 60, can.CanHatch);
        hatch.Disabled = !can.CanHatch;
        hatch.Pressed += () => HatchRequested?.Invoke();
        _modalContent.AddChild(hatch);
        if (!can.CanHatch)
            _modalContent.AddChild(UiKit.Label(can.Reason, 16, UiKit.Coral, true));
        if (sim.PendingHatch is not null)
        {
            var bar = new ProgressBar { MaxValue = sim.Tuning.Nursery.HatchTimeSeconds, Value = sim.Tuning.Nursery.HatchTimeSeconds - sim.PendingHatch.Remaining, CustomMinimumSize = new Vector2(480, 24) };
            _modalContent.AddChild(UiKit.Label("A helper is hatching…", 17, UiKit.TealDark));
            _modalContent.AddChild(bar);
        }
        if (sim.CanUseEmergencyRecovery)
        {
            var rescue = UiKit.Button("Nursery rescue: +4 silk (3 uses)", 480, 54);
            rescue.Pressed += () => RecoveryRequested?.Invoke();
            _modalContent.AddChild(rescue);
            _modalContent.AddChild(UiKit.Label("Use this only when your silk runs out.", 15, new Color(UiKit.Ink, 0.7f)));
        }
        var close = UiKit.Button("Back to the garden", 260, 54);
        close.Pressed += CloseModal;
        _modalContent.AddChild(close);
    }

    private Control Chip(string text)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiKit.Panel(UiKit.CreamDim, 12));
        var label = UiKit.Label(text, 15, UiKit.Pine);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        panel.AddChild(label);
        return panel;
    }

    public void ToggleColony()
    {
        if (_colonyOpen)
        {
            CloseModal();
            return;
        }
        if (IsModalOpen) return;
        OpenModal("Your colony", 520);
        ConfigureModalState(colony: true);
        if (_lastSim is not null) RefreshColony(_lastSim);
        var close = UiKit.Button("Close  [Tab]", 240, 52, true);
        close.Pressed += CloseModal;
        _modalContent.AddChild(close);
    }

    private void RefreshColony(GameSimulation sim)
    {
        if (!_colonyOpen) return;
        foreach (var child in _modalContent.GetChildren())
        {
            if (child != _modalTitle) child.QueueFree();
        }
        _modalContent.AddChild(UiKit.Label($"Spiders: {sim.TotalSpiders} / {sim.MaxSpiders}", 22, UiKit.Pine));
        AddColonyRow("You", "Explorer", "spider_player");
        foreach (var worker in sim.Workers)
            AddColonyRow(worker.Name, worker.DisplayState, worker.Colour switch
            {
                "violet" => "spider_worker_violet", "green" => "spider_worker_green", _ => "spider_worker_orange"
            });
        _modalContent.AddChild(UiKit.Label("Helpers work by themselves — collecting caught insects first, then repairing damaged webs.", 17, new Color(UiKit.Ink, 0.8f), true));
    }

    private void AddColonyRow(string name, string state, string iconModel)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiKit.Panel(UiKit.CreamDim, 14));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var badge = new ColorRect
        {
            CustomMinimumSize = new Vector2(12, 42),
            Color = iconModel.Contains("violet") ? UiKit.Violet : iconModel.Contains("green") ? UiKit.Leaf : UiKit.Gold
        };
        row.AddChild(badge);
        row.AddChild(UiKit.Label(name, 22, UiKit.Pine));
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(spacer);
        row.AddChild(UiKit.Label(state, 18, UiKit.TealDark));
        panel.AddChild(row);
        _modalContent.AddChild(panel);
    }

    public void ShowVictory(GameSimulation sim)
    {
        OpenModal("Your garden is thriving!", 640);
        ConfigureModalState(victory: true);
        _modalContent.AddChild(UiKit.IconLabel("quest", "First Threads · Tiny Team · Storm-Smart complete", 21, UiKit.TealDark));
        _modalContent.AddChild(UiKit.Label(
            $"You caught {sim.InsectsCollected} insects, raised {sim.Workers.Count} helper{(sim.Workers.Count == 1 ? "" : "s")} and kept the colony safe through the wind.",
            20, UiKit.Pine, true));
        if (sim.MasteryAwarded)
            _modalContent.AddChild(UiKit.Label("★ Clever Weaver badge earned: 3 insects with only 2 capture webs!", 18, UiKit.Violet, true));
        _modalContent.AddChild(UiKit.Spacer(8));
        var keep = UiKit.Button("Keep playing", 400, 60, true);
        keep.Pressed += () =>
        {
            CloseModal();
            ContinuePlayingRequested?.Invoke();
        };
        _modalContent.AddChild(keep);
        var save = UiKit.Button("Save game", 400, 52);
        save.Pressed += () => SaveRequested?.Invoke();
        _modalContent.AddChild(save);
        var menu = UiKit.Button("Save and return to menu", 400, 52);
        menu.Pressed += () => QuitToMenuRequested?.Invoke();
        _modalContent.AddChild(menu);
    }

    public void ShowSaveResult(bool success, string message)
    {
        ShowToast(success ? message : message, success ? UiKit.TealDark : UiKit.Coral, success ? 2.2f : 4.5f);
    }

    public void SetWorldVisibleDuringVictory(bool visible)
    {
        if (_victoryOpen) _modalRoot.Visible = visible;
    }

    public string DebugModal()
    {
        var children = string.Join("|", _modalContent.GetChildren().Select(c => c.GetType().Name));
        return $"modalVisible={_modalRoot.Visible} panel={_modalPanel.Size} content={_modalContent.Size} title='{_modalTitle.Text}' children={children}";
    }
}
