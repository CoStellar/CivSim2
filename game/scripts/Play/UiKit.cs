using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// A small square picture drawn in code (placeholder for Patryk's icons): a coloured tile with one or two letters.
/// Goods, buildings, units and alerts use it, so the screen can be read at a glance without art.
/// </summary>
public partial class Glyph : Control
{
    private readonly Color _color;
    private readonly string _text;
    private readonly bool _round;

    /// <summary>Creates a tile of <paramref name="size"/> pixels.</summary>
    public Glyph(Color color, string text, int size = 24, bool round = false)
    {
        _color = color;
        _text = text;
        _round = round;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Pass;
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        var box = new StyleBoxFlat
        {
            BgColor = _color,
            BorderColor = _color.Darkened(0.45f),
            CornerDetail = 4,
        };
        box.SetBorderWidthAll(2);
        box.SetCornerRadiusAll(_round ? (int)(Size.X / 2) : 4);
        DrawStyleBox(box, new Rect2(Vector2.Zero, Size));
        Font font = UiKit.GlyphFont;
        int fontSize = Size.X >= 44 ? 24 : 12; // Galmuri 11 Bold: sharp at 12 and 24, whatever font the text uses
        Vector2 textSize = font.GetStringSize(_text, HorizontalAlignment.Left, -1, fontSize);
        Color ink = _color.Luminance > 0.55f ? new Color(0.1f, 0.08f, 0.06f) : Colors.White;
        DrawString(font, new Vector2((Size.X - textSize.X) / 2, (Size.Y + (fontSize * 0.7f)) / 2), _text, HorizontalAlignment.Left, -1, fontSize, ink);
    }
}

/// <summary>Colours, short names and small builders shared by the parts of the play screen.</summary>
public static class UiKit
{
    /// <summary>Light text.</summary>
    public static readonly Color Text = new(0.96f, 0.94f, 0.9f);

    /// <summary>Headings (parchment).</summary>
    public static readonly Color Heading = new(0.95f, 0.76f, 0.47f);

    /// <summary>Secondary text.</summary>
    public static readonly Color Muted = new(0.66f, 0.63f, 0.6f);

    /// <summary>Good news, growth.</summary>
    public static readonly Color Good = new(0.55f, 0.85f, 0.45f);

    /// <summary>Bad news, shortage.</summary>
    public static readonly Color Bad = new(1f, 0.45f, 0.38f);

    /// <summary>Needs a look.</summary>
    public static readonly Color Warn = new(1f, 0.82f, 0.35f);

    private static readonly Dictionary<string, Color> GoodColors = new()
    {
        ["food"] = new Color("d9a441"),
        ["wood"] = new Color("8b5a2b"),
        ["stone"] = new Color("9a9a9a"),
        ["clay"] = new Color("c06040"),
        ["hides"] = new Color("a0703c"),
        ["tools"] = new Color("6f8090"),
        ["clothing"] = new Color("a070b8"),
        ["pottery"] = new Color("d07850"),
        ["textiles"] = new Color("c0a0e0"),
        ["copper"] = new Color("c87533"),
        ["tin"] = new Color("b8c0c8"),
        ["bronze"] = new Color("cd8f42"),
        ["iron"] = new Color("707a86"),
        ["arms"] = new Color("b03838"),
    };

    private static readonly Dictionary<string, Color> GroupColors = new()
    {
        ["food"] = new Color("5f9e3a"),
        ["farm"] = new Color("8fb040"),
        ["tools"] = new Color("8a6a48"),
        ["mine"] = new Color("6a6a78"),
        ["craft"] = new Color("c07a38"),
        ["store"] = new Color("b89a60"),
        ["house"] = new Color("4f7fb8"),
        ["fort"] = new Color("8a5050"),
        ["army"] = new Color("b84040"),
        ["learn"] = new Color("8a5ab8"),
        ["care"] = new Color("3a9a90"),
        ["road"] = new Color("7a7a6a"),
    };

    private static readonly Dictionary<string, Color> CategoryColors = new()
    {
        ["food"] = new Color("5f9e3a"),
        ["industry"] = new Color("c07a38"),
        ["building"] = new Color("8a7a60"),
        ["trade"] = new Color("c8a640"),
        ["military"] = new Color("b84040"),
        ["governance"] = new Color("4f7fb8"),
        ["science"] = new Color("8a5ab8"),
    };

    /// <summary>The interface font (the pixel font when the theme is loaded).</summary>
    public static Font Font => Ui.PixelTheme.Current?.DefaultFont ?? ThemeDB.FallbackFont;

    private static Font? _glyphFont;

    /// <summary>The small sharp font of the tiles' letters.</summary>
    public static Font GlyphFont => _glyphFont ??= LoadGlyphFont() ?? Font;

    private static Font? LoadGlyphFont()
    {
        string path = ProjectSettings.GlobalizePath("res://fonts/Galmuri11-Bold.ttf");
        var f = new FontFile { Antialiasing = TextServer.FontAntialiasing.None, Hinting = TextServer.Hinting.None, SubpixelPositioning = TextServer.SubpixelPositioning.Disabled };
        return System.IO.File.Exists(path) && f.LoadDynamicFont(path) == Error.Ok ? f : null;
    }

    /// <summary>A width in pixels grown for the chosen font.</summary>
    public static int W(int px) => (int)(px * Ui.PixelTheme.Wide);

    /// <summary>Colour of a good by key.</summary>
    public static Color GoodColor(string key) => GoodColors.TryGetValue(key, out Color c) ? c : new Color("888888");

    /// <summary>Colour of a building by its first group.</summary>
    public static Color GroupColor(string[]? groups) =>
        groups is { Length: > 0 } && GroupColors.TryGetValue(groups[0], out Color c) ? c : new Color("777068");

    /// <summary>Colour of a technology branch.</summary>
    public static Color CategoryColor(string category) => CategoryColors.TryGetValue(category, out Color c) ? c : new Color("777068");

    /// <summary>One or two letters for a name: "Gatherers' hut" → "GH", "Huts" → "Hu".</summary>
    public static string Short(string name)
    {
        string[] words = name.Replace("'", "", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
        {
            return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
        }

        return name.Length >= 2 ? $"{char.ToUpperInvariant(name[0])}{name[1]}" : name.ToUpperInvariant();
    }

    /// <summary>Hundredths as a short number: 1250 → "12.5".</summary>
    public static string Num(long hundredths) =>
        hundredths % 100 == 0 ? (hundredths / 100).ToString(CultureInfo.InvariantCulture) : (hundredths / 100.0).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>A rate with its sign: "+2.5", "-1", "0".</summary>
    public static string Rate(long hundredths) => hundredths > 0 ? $"+{Num(hundredths)}" : hundredths < 0 ? $"-{Num(-hundredths)}" : "0";

    /// <summary>A label.</summary>
    public static Label Label(string text, int size = 16, Color? color = null, bool wrap = false)
    {
        // A pixel font is sharp only at whole multiples of its grid: one size for text, one for titles.
        bool title = size > 16;
        size = title ? Ui.PixelTheme.TitleSize : Ui.PixelTheme.FontSize;
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Pass };
        if (title && Ui.PixelTheme.TitleFont is { } font)
        {
            l.AddThemeFontOverride("font", font);
        }

        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Text);
        if (wrap)
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }

        return l;
    }

    /// <summary>A flat coloured box style.</summary>
    public static StyleBoxFlat Flat(Color bg, Color? border = null, int radius = 4, int borderWidth = 2)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border ?? bg.Darkened(0.4f) };
        s.SetBorderWidthAll(borderWidth);
        s.SetCornerRadiusAll(radius);
        s.SetContentMarginAll(6);
        return s;
    }

    /// <summary>A thin progress bar with an optional text over it.</summary>
    public static Control Bar(float fraction, Color color, int width = 0, int height = 10, string? text = null)
    {
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = Math.Clamp(fraction, 0, 1), ShowPercentage = false, MouseFilter = Control.MouseFilterEnum.Pass };
        bar.CustomMinimumSize = new Vector2(width, height);
        if (width == 0)
        {
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }

        bar.AddThemeStyleboxOverride("background", Flat(new Color(0.12f, 0.1f, 0.09f), new Color(0.3f, 0.26f, 0.22f), 3, 1));
        bar.AddThemeStyleboxOverride("fill", Flat(color, color.Darkened(0.3f), 3, 1));
        if (text is null)
        {
            return bar;
        }

        var stack = new Control { CustomMinimumSize = new Vector2(width, Math.Max(height, Ui.PixelTheme.FontSize + 6)), SizeFlagsHorizontal = width == 0 ? Control.SizeFlags.ExpandFill : Control.SizeFlags.ShrinkBegin };
        bar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        stack.AddChild(bar);
        Label l = Label(text, 13);
        l.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.VerticalAlignment = VerticalAlignment.Center;
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 4);
        stack.AddChild(l);
        return stack;
    }

    /// <summary>A button without keyboard focus (so the map keeps the keys).</summary>
    public static Button Button(string text, Action act, string tooltip = "", bool enabled = true)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, TooltipText = tooltip, Disabled = !enabled };
        b.Pressed += act;
        return b;
    }

    /// <summary>Removes every child of a node.</summary>
    public static void Clear(Node node)
    {
        foreach (Node n in node.GetChildren())
        {
            node.RemoveChild(n);
            n.QueueFree();
        }
    }
}
