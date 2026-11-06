using CivSim.Game.Map;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>
/// The pixel interface theme (stage 5; GDD 12.4): opaque 9-slice frames from <c>ui/</c> pictures in the CivSim
/// palette, drawn twice as large without smoothing, and the Pixelify Sans font without anti-aliasing. The frames are
/// placeholders in the stone age look (leather and stone); Patryk's frames replace them by name in stage 13, and
/// later eras get their own themes (a module). Returns null when the pictures are missing, so the viewer falls back
/// to Godot's default look.
/// </summary>
public static class PixelTheme
{
    /// <summary>How much the interface pictures are enlarged (whole numbers only, GDD 12).</summary>
    public const int Scale = 2;

    /// <summary>Body text size; a multiple of the font's pixel grid.</summary>
    public const int FontSize = 16;

    /// <summary>Light text colour (palette).</summary>
    public static readonly Color Text = new(0.984f, 0.976f, 0.984f);

    /// <summary>Heading colour (palette, parchment).</summary>
    public static readonly Color Heading = new(0.949f, 0.761f, 0.475f);

    /// <summary>Muted text colour (palette).</summary>
    public static readonly Color Muted = new(0.663f, 0.635f, 0.627f);

    /// <summary>
    /// The theme in use, or null. Set on each interface root: themes do not pass through a CanvasLayer, so the
    /// window's theme alone would not reach the HUD and the menu.
    /// </summary>
    public static Theme? Current { get; set; }

    /// <summary>Builds the theme from the pictures in <paramref name="art"/>.</summary>
    public static Theme? Build(ArtLibrary art)
    {
        StyleBox? panel = Frame(art, "frame_panel", 6, 10);
        StyleBox? normal = Frame(art, "button_normal", 3, 6);
        if (panel is null || normal is null)
        {
            return null;
        }

        var theme = new Theme();
        FontFile? font = LoadFont();
        if (font is not null)
        {
            theme.DefaultFont = font;
        }

        theme.DefaultFontSize = FontSize;
        StyleBox bar = Frame(art, "frame_bar", 6, 6) ?? panel;
        StyleBox hover = Frame(art, "button_hover", 3, 6) ?? normal;
        StyleBox pressed = Frame(art, "button_pressed", 3, 6) ?? normal;
        StyleBox disabled = Frame(art, "button_disabled", 3, 6) ?? normal;
        StyleBox field = Frame(art, "field", 3, 5) ?? normal;
        StyleBox tabOn = Frame(art, "tab_selected", 3, 6) ?? pressed;
        StyleBox tabOff = Frame(art, "tab_unselected", 3, 6) ?? normal;
        StyleBox tooltip = Frame(art, "tooltip", 3, 5) ?? panel;
        var empty = new StyleBoxEmpty();

        theme.SetStylebox("panel", "PanelContainer", panel);
        theme.SetStylebox("panel", "Panel", panel);
        theme.SetStylebox("panel", "TopBar", bar);
        theme.SetTypeVariation("TopBar", "PanelContainer");
        foreach (string type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            theme.SetStylebox("normal", type, normal);
            theme.SetStylebox("hover", type, hover);
            theme.SetStylebox("pressed", type, pressed);
            theme.SetStylebox("hover_pressed", type, pressed);
            theme.SetStylebox("disabled", type, disabled);
            theme.SetStylebox("focus", type, empty);
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_hover_color", type, Heading);
            theme.SetColor("font_pressed_color", type, Heading);
            theme.SetColor("font_hover_pressed_color", type, Heading);
            theme.SetColor("font_disabled_color", type, Muted);
        }

        // Check boxes: no frame, pixel boxes as icons.
        foreach (string type in new[] { "CheckBox", "CheckButton" })
        {
            foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
            {
                theme.SetStylebox(state, type, empty);
            }

            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_hover_color", type, Heading);
            theme.SetColor("font_pressed_color", type, Text);
            theme.SetColor("font_hover_pressed_color", type, Heading);
        }

        if (Icon(art, "check_on") is { } on && Icon(art, "check_off") is { } off)
        {
            theme.SetIcon("checked", "CheckBox", on);
            theme.SetIcon("unchecked", "CheckBox", off);
            theme.SetIcon("checked_disabled", "CheckBox", on);
            theme.SetIcon("unchecked_disabled", "CheckBox", off);
        }

        foreach (string type in new[] { "LineEdit", "SpinBox" })
        {
            theme.SetStylebox("normal", type, field);
            theme.SetStylebox("focus", type, field);
            theme.SetStylebox("read_only", type, field);
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_placeholder_color", type, Muted);
        }

        theme.SetStylebox("panel", "TabContainer", panel);
        theme.SetStylebox("tab_selected", "TabContainer", tabOn);
        theme.SetStylebox("tab_unselected", "TabContainer", tabOff);
        theme.SetStylebox("tab_hovered", "TabContainer", tabOn);
        theme.SetColor("font_selected_color", "TabContainer", Heading);
        theme.SetColor("font_unselected_color", "TabContainer", Muted);
        theme.SetColor("font_hovered_color", "TabContainer", Text);

        theme.SetStylebox("panel", "Tree", field);
        theme.SetStylebox("focus", "Tree", empty);
        theme.SetStylebox("selected", "Tree", pressed);
        theme.SetStylebox("selected_focus", "Tree", pressed);
        theme.SetStylebox("title_button_normal", "Tree", tabOff);
        theme.SetStylebox("title_button_hover", "Tree", tabOff);
        theme.SetStylebox("title_button_pressed", "Tree", tabOff);
        theme.SetColor("font_color", "Tree", Text);
        theme.SetColor("title_button_color", "Tree", Heading);

        theme.SetStylebox("panel", "PopupMenu", panel);
        theme.SetStylebox("hover", "PopupMenu", pressed);
        theme.SetColor("font_color", "PopupMenu", Text);
        theme.SetColor("font_hover_color", "PopupMenu", Heading);
        theme.SetStylebox("panel", "TooltipPanel", tooltip);
        theme.SetColor("font_color", "TooltipLabel", new Color(0.114f, 0.09f, 0.063f));

        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("default_color", "RichTextLabel", Text);
        theme.SetStylebox("normal", "RichTextLabel", empty);
        if (font is not null)
        {
            var bold = new FontVariation { BaseFont = font, VariationOpentype = new Godot.Collections.Dictionary { ["wght"] = 700 } };
            theme.SetFont("bold_font", "RichTextLabel", bold);
            theme.SetFont("font", "Heading", bold);
            theme.SetTypeVariation("Heading", "Label");
            theme.SetColor("font_color", "Heading", Heading);
        }

        if (Frame(art, "slider_track", 3, 0) is { } track)
        {
            theme.SetStylebox("slider", "HSlider", track);
            theme.SetStylebox("grabber_area", "HSlider", Frame(art, "slider_fill", 3, 0) ?? track);
            theme.SetStylebox("grabber_area_highlight", "HSlider", Frame(art, "slider_fill", 3, 0) ?? track);
        }

        if (Icon(art, "slider_grabber") is { } grabber)
        {
            theme.SetIcon("grabber", "HSlider", grabber);
            theme.SetIcon("grabber_highlight", "HSlider", grabber);
        }

        if (Frame(art, "scroll_grabber", 2, 0) is { } scroll)
        {
            theme.SetStylebox("grabber", "VScrollBar", scroll);
            theme.SetStylebox("grabber_highlight", "VScrollBar", scroll);
            theme.SetStylebox("grabber_pressed", "VScrollBar", scroll);
            theme.SetStylebox("scroll", "VScrollBar", field);
        }

        theme.SetConstant("separation", "HBoxContainer", 6);
        theme.SetConstant("separation", "VBoxContainer", 4);
        return theme;
    }

    // Pixelify Sans from res://fonts, read as a plain file so it needs no import; drawn without smoothing.
    private static FontFile? LoadFont()
    {
        string path = ProjectSettings.GlobalizePath("res://fonts/PixelifySans.ttf");
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        var font = new FontFile
        {
            Antialiasing = TextServer.FontAntialiasing.None,
            Hinting = TextServer.Hinting.None,
            SubpixelPositioning = TextServer.SubpixelPositioning.Disabled,
            GenerateMipmaps = false,
        };
        return font.LoadDynamicFont(path) == Error.Ok ? font : null;
    }

    // A 9-slice frame: the picture enlarged, `margin` source pixels kept unstretched at each side, `content` source
    // pixels of padding inside.
    private static StyleBoxTexture? Frame(ArtLibrary art, string name, int margin, int content)
    {
        if (Icon(art, name) is not { } texture)
        {
            return null;
        }

        var box = new StyleBoxTexture { Texture = texture };
        box.SetTextureMarginAll(margin * Scale);
        box.SetContentMarginAll(System.Math.Max(margin, content / 2) * Scale);
        return box;
    }

    private static ImageTexture? Icon(ArtLibrary art, string name)
    {
        Image? image = art.LoadImage("ui", name);
        if (image is null)
        {
            return null;
        }

        image.Resize(image.GetWidth() * Scale, image.GetHeight() * Scale, Image.Interpolation.Nearest);
        return ImageTexture.CreateFromImage(image);
    }
}
