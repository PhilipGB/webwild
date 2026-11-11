using Godot;

namespace Webwild.Game;

/// <summary>Shared child-friendly visual language for every screen.</summary>
public static class UiKit
{
    public static readonly Color Cream = new("F7F1DE");
    public static readonly Color CreamDim = new("EFE6CC");
    public static readonly Color Ink = new("263F39");
    public static readonly Color Pine = new("1D332E");
    public static readonly Color Leaf = new("78BF66");
    public static readonly Color LeafDark = new("4F8F4A");
    public static readonly Color FreshLeaf = new("A3D981");
    public static readonly Color Teal = new("41B8B2");
    public static readonly Color TealDark = new("2B8782");
    public static readonly Color Violet = new("795E9C");
    public static readonly Color Gold = new("F6C34B");
    public static readonly Color Coral = new("E66F62");
    public static readonly Color Sky = new("93D7EC");
    public static readonly Color Soil = new("986947");
    public static readonly Color Dew = new("DBF6F3");
    public static readonly Color White = new("FFFDF6");

    public static StyleBoxFlat Panel(Color color, int radius = 22, Color? border = null, int borderWidth = 0)
    {
        var style = new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = 20,
            ContentMarginRight = 20,
            ContentMarginTop = 16,
            ContentMarginBottom = 16,
            ShadowColor = new Color(0.08f, 0.14f, 0.12f, 0.2f),
            ShadowSize = 10,
            ShadowOffset = new Vector2(0, 5),
            AntiAliasing = true
        };
        if (border.HasValue && borderWidth > 0)
        {
            style.BorderColor = border.Value;
            style.BorderWidthLeft = borderWidth;
            style.BorderWidthRight = borderWidth;
            style.BorderWidthTop = borderWidth;
            style.BorderWidthBottom = borderWidth;
        }
        return style;
    }

    public static Theme CreateTheme()
    {
        var theme = new Theme
        {
            DefaultFontSize = 19
        };
        theme.SetColor("font_color", "Label", Ink);
        theme.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0));
        theme.SetConstant("outline_size", "Label", 0);
        theme.SetFontSize("font_size", "Button", 20);
        theme.SetColor("font_color", "Button", Ink);
        theme.SetColor("font_hover_color", "Button", Pine);
        theme.SetColor("font_pressed_color", "Button", Pine);
        theme.SetColor("font_disabled_color", "Button", new Color(Ink, 0.45f));
        theme.SetStylebox("normal", "Button", Panel(Cream, 16, new Color(0, 0, 0, 0.08f), 1));
        theme.SetStylebox("hover", "Button", Panel(White, 16, Leaf, 2));
        theme.SetStylebox("pressed", "Button", Panel(FreshLeaf, 16, LeafDark, 2));
        theme.SetStylebox("disabled", "Button", Panel(new Color(CreamDim, 0.75f), 16));
        theme.SetStylebox("focus", "Button", Panel(new Color(0, 0, 0, 0), 16, Gold, 3));
        theme.SetStylebox("panel", "PanelContainer", Panel(Cream, 22));
        theme.SetStylebox("background", "ProgressBar", Panel(CreamDim, 8));
        theme.SetStylebox("fill", "ProgressBar", Panel(Teal, 8));
        theme.SetColor("font_color", "CheckButton", Ink);
        theme.SetColor("font_hover_color", "CheckButton", Pine);
        theme.SetColor("font_color", "OptionButton", Ink);
        theme.SetStylebox("normal", "OptionButton", Panel(Cream, 14, new Color(0, 0, 0, 0.08f), 1));
        theme.SetStylebox("hover", "OptionButton", Panel(White, 14, Leaf, 2));
        theme.SetStylebox("pressed", "OptionButton", Panel(FreshLeaf, 14, LeafDark, 2));
        theme.SetStylebox("focus", "OptionButton", Panel(new Color(0, 0, 0, 0), 14, Gold, 3));
        return theme;
    }

    public static Label Label(string text, int size = 20, Color? color = null, bool wrap = false)
    {
        var label = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Ink);
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        return label;
    }

    public static Label TitleLabel(string text, int size = 76, Color? color = null)
    {
        var label = Label(text, size, color ?? Cream);
        label.AddThemeColorOverride("font_outline_color", Pine);
        label.AddThemeConstantOverride("outline_size", Math.Max(8, size / 8));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return label;
    }

    public static Button Button(string text, int width = 220, int height = 56, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, height),
            FocusMode = Control.FocusModeEnum.All
        };
        if (primary)
        {
            button.AddThemeStyleboxOverride("normal", Panel(Leaf, 16, LeafDark, 2));
            button.AddThemeStyleboxOverride("hover", Panel(FreshLeaf, 16, LeafDark, 3));
            button.AddThemeStyleboxOverride("pressed", Panel(LeafDark, 16, Pine, 2));
            button.AddThemeColorOverride("font_color", Pine);
            button.AddThemeColorOverride("font_hover_color", Pine);
            button.AddThemeColorOverride("font_pressed_color", White);
            button.AddThemeFontSizeOverride("font_size", 22);
        }
        return button;
    }

    public static Texture2D? Icon(string name)
    {
        var path = $"res://art/ui/icons/{name}.svg";
        if (!ResourceLoader.Exists(path)) return null;
        return GD.Load<Texture2D>(path);
    }

    public static HBoxContainer IconLabel(string icon, string text, int fontSize = 19, Color? color = null)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        var texture = Icon(icon);
        if (texture is not null)
        {
            var rect = new TextureRect
            {
                Texture = texture,
                CustomMinimumSize = new Vector2(28, 28),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };
            box.AddChild(rect);
        }
        var label = Label(text, fontSize, color);
        box.AddChild(label);
        return box;
    }

    public static Control Spacer(float height = 10)
    {
        return new Control { CustomMinimumSize = new Vector2(1, height), MouseFilter = Control.MouseFilterEnum.Ignore };
    }

    public static void FullRect(Control control)
    {
        control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    public static Color WithAlpha(Color color, float alpha) => new(color.R, color.G, color.B, alpha);
}
