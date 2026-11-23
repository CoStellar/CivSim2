using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>One chapter of the game manual: its title and its lines as written in the file.</summary>
public sealed record ManualChapter(string Title, IReadOnlyList<string> Lines);

/// <summary>
/// The game manual (Patryk, 2026-11-23): how to play, every key and how to move through the eras. The text lives in
/// <c>data/sim/manual.md</c> so it can be changed without code. Its format is a small part of Markdown:
/// <c># Title</c> starts a chapter, <c>## Heading</c> a heading in it, <c>| Keys | What they do</c> a row of the key table,
/// <c>- text</c> a point; other lines are text, and an empty line ends a paragraph.
/// </summary>
public static class Manual
{
    /// <summary>The manual's file inside the data folder.</summary>
    public static string PathIn(string dataDirectory) => System.IO.Path.Combine(dataDirectory, "sim", "manual.md");

    /// <summary>Reads the manual's chapters; an empty list when the file is missing.</summary>
    public static List<ManualChapter> Load(string dataDirectory)
    {
        string path = PathIn(dataDirectory);
        return System.IO.File.Exists(path) ? Parse(System.IO.File.ReadAllText(path)) : [];
    }

    /// <summary>Splits the manual's text into chapters.</summary>
    public static List<ManualChapter> Parse(string text)
    {
        var chapters = new List<ManualChapter>();
        string? title = null;
        var lines = new List<string>();
        foreach (string raw in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.StartsWith("# ", StringComparison.Ordinal))
            {
                if (title is not null)
                {
                    chapters.Add(new ManualChapter(title, lines));
                }

                title = raw[2..].Trim();
                lines = [];
            }
            else if (title is not null)
            {
                lines.Add(raw.TrimEnd());
            }
        }

        if (title is not null)
        {
            chapters.Add(new ManualChapter(title, lines));
        }

        return chapters;
    }

    /// <summary>A chapter as BBCode for a <see cref="RichTextLabel"/>.</summary>
    public static string ToBbCode(ManualChapter chapter, int fontSize)
    {
        var sb = new StringBuilder();
        sb.Append($"[font_size={fontSize + 10}][color=#f2c278]{Escape(chapter.Title)}[/color][/font_size]\n");
        bool inTable = false;
        var paragraph = new StringBuilder();

        void EndParagraph()
        {
            if (paragraph.Length > 0)
            {
                sb.Append(Inline(paragraph.ToString())).Append("\n\n");
                paragraph.Clear();
            }
        }

        void EndTable()
        {
            if (inTable)
            {
                sb.Append("[/table]\n\n");
                inTable = false;
            }
        }

        foreach (string line in chapter.Lines)
        {
            if (line.StartsWith("| ", StringComparison.Ordinal))
            {
                EndParagraph();
                string[] cells = line[2..].Split(" | ", 2);
                if (!inTable)
                {
                    sb.Append("[table=2]");
                    inTable = true;
                }

                sb.Append("[cell padding=0,3,18,3]").Append(Keys(cells[0].Trim())).Append("[/cell]");
                sb.Append("[cell padding=0,3,0,3]").Append(Inline(cells.Length > 1 ? cells[1].Trim() : "")).Append("[/cell]");
                continue;
            }

            EndTable();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                EndParagraph();
                sb.Append($"[font_size={fontSize + 4}][color=#f2c278]{Escape(line[3..].Trim())}[/color][/font_size]\n");
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                EndParagraph();
                sb.Append("  •  ").Append(Inline(line[2..].Trim())).Append('\n');
            }
            else if (line.Trim().Length == 0)
            {
                EndParagraph();
                if (sb.Length > 0 && sb[^1] == '\n' && (sb.Length < 2 || sb[^2] != '\n'))
                {
                    sb.Append('\n');
                }
            }
            else
            {
                if (paragraph.Length > 0)
                {
                    paragraph.Append(' ');
                }

                paragraph.Append(line.Trim());
            }
        }

        EndTable();
        EndParagraph();
        return sb.ToString().TrimEnd() + "\n";
    }

    // "Shift + Tab, Q / E" → each key drawn as a key cap, the joins between them in grey.
    private static string Keys(string text)
    {
        var sb = new StringBuilder();
        string[] parts = Regex.Split(text, "( \\+ | / |, | or )");
        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1)
            {
                sb.Append("[color=#a8a099]").Append(Escape(parts[i])).Append("[/color]");
            }
            else if (parts[i].Trim().Length > 0)
            {
                sb.Append("[bgcolor=#4a3a26][color=#ffe3a3] ").Append(Escape(parts[i].Trim())).Append(" [/color][/bgcolor]");
            }
        }

        return sb.ToString();
    }

    // **bold** shows in the heading colour; `key` as a key cap.
    private static string Inline(string text)
    {
        var sb = new StringBuilder();
        string[] bold = Escape(text).Split("**");
        for (int i = 0; i < bold.Length; i++)
        {
            string part = bold[i];
            string[] code = part.Split('`');
            var inner = new StringBuilder();
            for (int k = 0; k < code.Length; k++)
            {
                inner.Append(k % 2 == 1 ? $"[bgcolor=#4a3a26][color=#ffe3a3] {code[k]} [/color][/bgcolor]" : code[k]);
            }

            sb.Append(i % 2 == 1 ? $"[color=#f2c278]{inner}[/color]" : inner.ToString());
        }

        return sb.ToString();
    }

    private static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);
}

/// <summary>
/// The manual on screen: the chapters on the left, the chosen one on the right. Used by the start menu (How to play) and in
/// the game (F1). Keys: Up and Down scroll, Page Up and Page Down scroll a page, Left and Right (or Q and E) turn the
/// chapters.
/// </summary>
public partial class ManualView : HBoxContainer
{
    private readonly List<ManualChapter> _chapters;
    private readonly bool _focusable;
    private readonly List<Button> _buttons = [];
    private RichTextLabel _text = null!;
    private int _chapter;

    /// <summary>Creates the view; <paramref name="focusable"/> lets the chapter buttons take keyboard focus (start menu).</summary>
    public ManualView(List<ManualChapter> chapters, bool focusable, int chapter = 0)
    {
        _chapters = chapters;
        _focusable = focusable;
        _chapter = Math.Clamp(chapter, 0, Math.Max(0, chapters.Count - 1));
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
    }

    /// <summary>Required by Godot; not used.</summary>
    public ManualView()
        : this([], false)
    {
    }

    /// <summary>The chapter shown.</summary>
    public int Chapter => _chapter;

    /// <inheritdoc />
    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 16);
        var list = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
        list.AddThemeConstantOverride("separation", 4);
        AddChild(list);
        var group = new ButtonGroup();
        for (int i = 0; i < _chapters.Count; i++)
        {
            int index = i;
            var b = new Button
            {
                Text = _chapters[i].Title,
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = i == _chapter,
                Alignment = HorizontalAlignment.Left,
                FocusMode = _focusable ? FocusModeEnum.All : FocusModeEnum.None,
                TooltipText = $"Chapter {i + 1} (Left and Right turn the chapters)",
            };
            b.Pressed += () => ShowChapter(index);
            if (_focusable)
            {
                b.FocusEntered += () => ShowChapter(index);
            }

            list.AddChild(b);
            _buttons.Add(b);
        }

        string hint = _focusable ? "Up / Down: chapter\nPage Up / Down: scroll" : "Up / Down: scroll\nLeft / Right: chapter";
        list.AddChild(new Label { Text = hint, Modulate = new Color(0.66f, 0.63f, 0.6f) });

        _text = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = true,
            FitContent = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(420, 300),
            SelectionEnabled = false,
            FocusMode = FocusModeEnum.None,
        };
        _text.AddThemeFontSizeOverride("normal_font_size", PixelTheme.FontSize);
        AddChild(_text);
        ShowChapter(_chapter);
    }

    /// <summary>Gives keyboard focus to the chapter shown (start menu).</summary>
    public void FocusChapter()
    {
        if (_focusable && _chapter < _buttons.Count)
        {
            _buttons[_chapter].GrabFocus();
        }
    }

    /// <summary>Shows a chapter from the top.</summary>
    public void ShowChapter(int index)
    {
        if (_chapters.Count == 0)
        {
            _text.Text = "The manual (data/sim/manual.md) is missing.";
            return;
        }

        _chapter = Math.Clamp(index, 0, _chapters.Count - 1);
        _text.Text = Manual.ToBbCode(_chapters[_chapter], PixelTheme.FontSize);
        _text.ScrollToLine(0);
        for (int i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].SetPressedNoSignal(i == _chapter);
        }
    }

    /// <summary>Handles a key meant for the manual; true when it was used.</summary>
    public bool HandleKey(Key key)
    {
        VScrollBar bar = _text.GetVScrollBar();
        float page = Math.Max(60, _text.Size.Y - 60);
        switch (key)
        {
            case Key.Up or Key.W:
                bar.Value -= 48;
                return true;
            case Key.Down or Key.S:
                bar.Value += 48;
                return true;
            case Key.Pageup:
                bar.Value -= page;
                return true;
            case Key.Pagedown:
                bar.Value += page;
                return true;
            case Key.Home:
                bar.Value = 0;
                return true;
            case Key.End:
                bar.Value = bar.MaxValue;
                return true;
            case Key.Left or Key.Q or Key.A:
                ShowChapter((_chapter + _chapters.Count - 1) % Math.Max(1, _chapters.Count));
                return true;
            case Key.Right or Key.E or Key.D:
                ShowChapter((_chapter + 1) % Math.Max(1, _chapters.Count));
                return true;
            default:
                return false;
        }
    }
}
