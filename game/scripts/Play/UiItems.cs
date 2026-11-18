using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// What a panel shows, as a list of plain items. The panels compare the items' signatures and rebuild their nodes only
/// when something changed, so a click is never lost to a refresh.
/// </summary>
internal abstract record UiItem
{
    /// <summary>Text that changes whenever what the item shows changes.</summary>
    public abstract string Sig { get; }
}

/// <summary>A title with a tile and a second line.</summary>
internal sealed record HeaderItem(string Title, string Sub, Color Color, string Short) : UiItem
{
    public override string Sig => $"H{Title}|{Sub}|{Short}";
}

/// <summary>A line of text.</summary>
internal sealed record LineItem(string Text, Color? Color = null, int Size = 16) : UiItem
{
    public override string Sig => $"L{Text}|{Color}";
}

/// <summary>A labelled bar, e.g. people of homes.</summary>
internal sealed record BarItem(string Label, float Fraction, string Value, Color Color) : UiItem
{
    public override string Sig => $"B{Label}|{Fraction:0.00}|{Value}";
}

/// <summary>A small heading that starts a group.</summary>
internal sealed record SectionItem(string Title) : UiItem
{
    public override string Sig => $"S{Title}";
}

/// <summary>One good in a store.</summary>
internal sealed record GoodCell(string Key, string Name, long Stock, long Capacity, long Rate);

/// <summary>A grid of goods with amount, room and trend.</summary>
internal sealed record GoodsItem(IReadOnlyList<GoodCell> Cells) : UiItem
{
    public override string Sig
    {
        get
        {
            var sb = new StringBuilder("G");
            foreach (GoodCell c in Cells)
            {
                sb.Append(c.Key).Append(c.Stock / 100).Append('/').Append(c.Capacity / 100).Append(c.Rate).Append(';');
            }

            return sb.ToString();
        }
    }
}

/// <summary>A button.</summary>
internal sealed record ActItem(string Text, Action Do, string Tooltip = "", bool Enabled = true, Color? Accent = null) : UiItem
{
    public override string Sig => $"A{Text}|{Tooltip}|{Enabled}";
}

/// <summary>A picture tile with a caption below: units to train and the like.</summary>
internal sealed record TileCell(string Name, string Short, Color Color, string Tooltip, Action Do, bool Enabled, string Caption);

/// <summary>A row of picture tiles that wraps.</summary>
internal sealed record TilesItem(IReadOnlyList<TileCell> Tiles) : UiItem
{
    public override string Sig
    {
        get
        {
            var sb = new StringBuilder("T");
            foreach (TileCell t in Tiles)
            {
                sb.Append(t.Name).Append(t.Enabled).Append(t.Caption).Append(t.Tooltip).Append(';');
            }

            return sb.ToString();
        }
    }
}

/// <summary>A table cell; clickable when it has an action.</summary>
internal sealed record CellItem(string Text, Color? Color = null, Action? Do = null);

/// <summary>A table.</summary>
internal sealed record TableItem(int Columns, IReadOnlyList<CellItem> Cells) : UiItem
{
    public override string Sig
    {
        get
        {
            var sb = new StringBuilder("X");
            foreach (CellItem c in Cells)
            {
                sb.Append(c.Text).Append(c.Color).Append(';');
            }

            return sb.ToString();
        }
    }
}

/// <summary>Anything else: made by a function, rebuilt when its signature changes.</summary>
internal sealed record CustomItem(string Key, Func<Control> Make) : UiItem
{
    public override string Sig => $"C{Key}";
}

/// <summary>Turns items into nodes.</summary>
internal static class UiRender
{
    /// <summary>Signature of a list of items.</summary>
    public static string Sig(List<UiItem> items)
    {
        var sb = new StringBuilder();
        foreach (UiItem i in items)
        {
            sb.Append(i.Sig).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Fills <paramref name="box"/> with nodes for the items; <paramref name="clicked"/> runs after any action.</summary>
    public static void Render(VBoxContainer box, List<UiItem> items, Action clicked)
    {
        UiKit.Clear(box);
        foreach (UiItem item in items)
        {
            box.AddChild(Make(item, clicked));
        }
    }

    private static Control Make(UiItem item, Action clicked)
    {
        switch (item)
        {
            case HeaderItem h:
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);
                row.AddChild(new Glyph(h.Color, h.Short, 40));
                var col = new VBoxContainer();
                col.AddThemeConstantOverride("separation", 0);
                col.AddChild(UiKit.Label(h.Title, 20, UiKit.Heading));
                if (h.Sub.Length > 0)
                {
                    col.AddChild(UiKit.Label(h.Sub, 14, UiKit.Muted));
                }

                row.AddChild(col);
                return row;
            }

            case LineItem l:
                return UiKit.Label(l.Text, l.Size, l.Color, wrap: true);
            case BarItem b:
            {
                var row = new HBoxContainer();
                Label name = UiKit.Label(b.Label, 14, UiKit.Muted);
                name.CustomMinimumSize = new Vector2(UiKit.W(86), 0);
                row.AddChild(name);
                row.AddChild(UiKit.Bar(b.Fraction, b.Color, 0, Ui.PixelTheme.FontSize + 6, b.Value));
                return row;
            }

            case SectionItem s:
            {
                var col = new VBoxContainer();
                col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
                col.AddChild(UiKit.Label(s.Title.ToUpperInvariant(), 13, UiKit.Heading));
                col.AddChild(new HSeparator());
                return col;
            }

            case GoodsItem g:
            {
                var grid = new GridContainer { Columns = 2 };
                grid.AddThemeConstantOverride("h_separation", 10);
                foreach (GoodCell c in g.Cells)
                {
                    var cell = new HBoxContainer { TooltipText = $"{c.Name}: {c.Stock / 100} of {c.Capacity / 100}, {UiKit.Rate(c.Rate)} a minute", MouseFilter = Control.MouseFilterEnum.Pass };
                    cell.AddChild(new Glyph(UiKit.GoodColor(c.Key), UiKit.Short(c.Name)[..1], 24, round: true));
                    var stack = new VBoxContainer();
                    stack.AddThemeConstantOverride("separation", 0);
                    Color rate = c.Rate > 0 ? UiKit.Good : c.Rate < 0 ? UiKit.Bad : UiKit.Muted;
                    var top = new HBoxContainer();
                    top.AddChild(UiKit.Label($"{c.Stock / 100}", 14));
                    top.AddChild(UiKit.Label(UiKit.Rate(c.Rate), 12, rate));
                    stack.AddChild(top);
                    bool full = c.Capacity > 0 && c.Stock >= c.Capacity * 98 / 100;
                    stack.AddChild(UiKit.Bar(c.Capacity <= 0 ? 0 : (float)c.Stock / c.Capacity, full ? UiKit.Warn : UiKit.GoodColor(c.Key), 90, 5));
                    cell.AddChild(stack);
                    grid.AddChild(cell);
                }

                return grid;
            }

            case ActItem a:
            {
                Button b = UiKit.Button(a.Text, () =>
                {
                    a.Do();
                    clicked();
                }, a.Tooltip, a.Enabled);
                b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                if (a.Accent is { } accent)
                {
                    b.AddThemeColorOverride("font_color", accent);
                }

                return b;
            }

            case TilesItem t:
            {
                var flow = new HFlowContainer();
                flow.AddThemeConstantOverride("h_separation", 6);
                flow.AddThemeConstantOverride("v_separation", 6);
                foreach (TileCell tile in t.Tiles)
                {
                    var button = new Button { FocusMode = Control.FocusModeEnum.None, TooltipText = tile.Tooltip, Disabled = !tile.Enabled, CustomMinimumSize = new Vector2(UiKit.W(92), 40 + Ui.PixelTheme.FontSize) };
                    var inner = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
                    inner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                    var glyph = new Glyph(tile.Enabled ? tile.Color : tile.Color.Darkened(0.5f), tile.Short, 32) { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
                    inner.AddChild(glyph);
                    Label cap = UiKit.Label(tile.Caption, 11, tile.Enabled ? UiKit.Text : UiKit.Muted);
                    cap.HorizontalAlignment = HorizontalAlignment.Center;
                    cap.MouseFilter = Control.MouseFilterEnum.Ignore;
                    inner.AddChild(cap);
                    button.AddChild(inner);
                    TileCell captured = tile;
                    button.Pressed += () =>
                    {
                        captured.Do();
                        clicked();
                    };
                    flow.AddChild(button);
                }

                return flow;
            }

            case TableItem x:
            {
                var grid = new GridContainer { Columns = x.Columns };
                grid.AddThemeConstantOverride("h_separation", 14);
                foreach (CellItem c in x.Cells)
                {
                    if (c.Do is { } act)
                    {
                        Button b = UiKit.Button(c.Text, () =>
                        {
                            act();
                            clicked();
                        });
                        b.Alignment = HorizontalAlignment.Left;
                        grid.AddChild(b);
                    }
                    else
                    {
                        grid.AddChild(UiKit.Label(c.Text, 14, c.Color));
                    }
                }

                return grid;
            }

            case CustomItem c:
                return c.Make();
            default:
                return new Control();
        }
    }
}
