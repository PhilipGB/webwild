using Godot;
using Webwild.Core;

namespace Webwild.Game;

/// <summary>Shared accessibility and audio settings, usable from title and pause.</summary>
public partial class SettingsDialog : Control
{
    public event Action<SettingsData>? Changed;
    public event Action? Closed;

    public SettingsData Settings { get; private set; } = new();

    private bool _built;
    private HSlider _master = null!;
    private HSlider _music = null!;
    private HSlider _sfx = null!;
    private HSlider _sensitivity = null!;
    private OptionButton _scale = null!;
    private CheckButton _reducedMotion = null!;
    private CheckButton _lowThreat = null!;
    private CheckButton _fullscreen = null!;
    private CheckButton _tutorialPrompts = null!;
    private Label _masterValue = null!;
    private Label _musicValue = null!;
    private Label _sfxValue = null!;
    private Label _sensitivityValue = null!;

    public SettingsDialog Configure(SettingsData settings)
    {
        Settings = settings;
        Settings.Normalise();
        if (_built) SyncControls();
        return this;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Build();
    }

    private void Build()
    {
        if (_built) return;
        _built = true;
        Visible = false;

        var dim = new ColorRect
        {
            Color = new Color(0.04f, 0.09f, 0.07f, 0.62f),
            MouseFilter = MouseFilterEnum.Stop
        };
        UiKit.FullRect(dim);
        AddChild(dim);

        var center = new CenterContainer();
        UiKit.FullRect(center);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(840, 470), Theme = UiKit.CreateTheme() };
        center.AddChild(panel);
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 6);
        panel.AddChild(outer);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            CustomMinimumSize = new Vector2(800, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        outer.AddChild(scroll);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 4);
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(content);

        var title = UiKit.Label("Settings", 30, UiKit.Pine);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(title);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        content.AddChild(columns);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        left.AddThemeConstantOverride("separation", 6);
        columns.AddChild(left);
        left.AddChild(UiKit.Label("Sound and camera", 19, UiKit.Pine));

        _masterValue = UiKit.Label("", 16, UiKit.TealDark);
        _master = SliderRow(left, "Master volume", 0, 1, 0.05, out _masterValue);
        _musicValue = UiKit.Label("", 16, UiKit.TealDark);
        _music = SliderRow(left, "Music volume", 0, 1, 0.05, out _musicValue);
        _sfxValue = UiKit.Label("", 16, UiKit.TealDark);
        _sfx = SliderRow(left, "Sound effects", 0, 1, 0.05, out _sfxValue);
        _sensitivityValue = UiKit.Label("", 16, UiKit.TealDark);
        _sensitivity = SliderRow(left, "Camera sensitivity", 0.3, 2.0, 0.1, out _sensitivityValue);

        var scaleRow = new HBoxContainer();
        scaleRow.AddThemeConstantOverride("separation", 10);
        scaleRow.AddChild(UiKit.Label("Interface size", 18, UiKit.Ink));
        var scaleSpacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scaleRow.AddChild(scaleSpacer);
        _scale = new OptionButton { CustomMinimumSize = new Vector2(160, 38) };
        _scale.AddItem("100%", 100);
        _scale.AddItem("125%", 125);
        _scale.AddItem("150%", 150);
        _scale.ItemSelected += index => ApplyAndEmit(() => Settings.UiScalePercent = (int)_scale.GetItemId((int)index));
        scaleRow.AddChild(_scale);
        left.AddChild(scaleRow);

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        right.AddThemeConstantOverride("separation", 4);
        columns.AddChild(right);
        right.AddChild(UiKit.Label("Comfort and access", 19, UiKit.Pine));
        _reducedMotion = CheckRow(right, "Reduce motion and camera bob");
        _reducedMotion.Toggled += value => ApplyAndEmit(() => Settings.ReducedMotion = value);
        _lowThreat = CheckRow(right, "Gentle weather (less wind damage)");
        _lowThreat.Toggled += value => ApplyAndEmit(() => Settings.LowThreat = value);
        _tutorialPrompts = CheckRow(right, "Show one-line hints");
        _tutorialPrompts.Toggled += value => ApplyAndEmit(() => Settings.TutorialPrompts = value);
        _fullscreen = CheckRow(right, "Full screen");
        _fullscreen.Toggled += value => ApplyAndEmit(() => Settings.Fullscreen = value);

        content.AddChild(UiKit.Spacer(4));
        var doneRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        var done = UiKit.Button("Done", 320, 54, true);
        done.Pressed += Close;
        doneRow.AddChild(done);
        outer.AddChild(doneRow);

        _master.ValueChanged += value => ApplyAndEmit(() => Settings.MasterVolume = (float)value);
        _music.ValueChanged += value => ApplyAndEmit(() => Settings.MusicVolume = (float)value);
        _sfx.ValueChanged += value => ApplyAndEmit(() => Settings.SfxVolume = (float)value);
        _sensitivity.ValueChanged += value => ApplyAndEmit(() => Settings.CameraSensitivity = (float)value);

        SyncControls();
    }

    private HSlider SliderRow(VBoxContainer parent, string text, double min, double max, double step, out Label valueLabel)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(UiKit.Label(text, 18, UiKit.Ink));
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);
        valueLabel = UiKit.Label("", 16, UiKit.TealDark);
        row.AddChild(valueLabel);
        box.AddChild(row);
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            CustomMinimumSize = new Vector2(360, 22),
            Value = Settings.MasterVolume
        };
        box.AddChild(slider);
        parent.AddChild(box);
        return slider;
    }

    private CheckButton CheckRow(VBoxContainer parent, string text)
    {
        var check = new CheckButton
        {
            Text = text,
            CustomMinimumSize = new Vector2(360, 40)
        };
        check.AddThemeFontSizeOverride("font_size", 17);
        parent.AddChild(check);
        return check;
    }

    private void ApplyAndEmit(Action apply)
    {
        apply();
        Settings.Normalise();
        GameData.ApplyUiScale(Settings);
        GameData.ApplyFullscreen(Settings);
        SyncControls();
        Changed?.Invoke(Settings);
    }

    private void SyncControls()
    {
        if (!_built) return;
        _master.SetValueNoSignal(Settings.MasterVolume);
        _music.SetValueNoSignal(Settings.MusicVolume);
        _sfx.SetValueNoSignal(Settings.SfxVolume);
        _sensitivity.SetValueNoSignal(Settings.CameraSensitivity);
        _masterValue.Text = $"{Math.Round(Settings.MasterVolume * 100)}%";
        _musicValue.Text = $"{Math.Round(Settings.MusicVolume * 100)}%";
        _sfxValue.Text = $"{Math.Round(Settings.SfxVolume * 100)}%";
        _sensitivityValue.Text = $"{Settings.CameraSensitivity:0.0}×";
        var selected = Settings.UiScalePercent switch { 125 => 1, 150 => 2, _ => 0 };
        _scale.Select(selected);
        _reducedMotion.SetPressedNoSignal(Settings.ReducedMotion);
        _lowThreat.SetPressedNoSignal(Settings.LowThreat);
        _fullscreen.SetPressedNoSignal(Settings.Fullscreen);
        _tutorialPrompts.SetPressedNoSignal(Settings.TutorialPrompts);
    }

    public void Open()
    {
        Build();
        SyncControls();
        Visible = true;
        MoveToFront();
        _master.GrabFocus();
    }

    public void Close()
    {
        Visible = false;
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event.IsActionPressed("ui_cancel") || @event.IsActionPressed("cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
