using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// The technology tree drawn as a tree (Patryk, 2026-11-19): eras are bands from left to right, inside an era a technology
/// stands one column right of the technologies of its era it needs, and lines join every technology to those it needs.
/// Rows are chosen so that a technology sits level with what it comes from, which keeps the branches straight. Eras the
/// people has not reached are not shown beyond the next one.
/// </summary>
public partial class TechTree : Control
{
    private const int Margin = 12;
    private const int Gap = 54;
    private const int RowGap = 12;
    private const int HeaderHeight = 46;

    private readonly PlayContentMessage _content;
    private readonly PlayViewMessage _view;
    private readonly Dictionary<int, Vector2> _at = [];
    private readonly List<(int Era, float X0, float X1, string Note)> _bands = [];
    private readonly int _cardWidth;
    private readonly int _cardHeight;

    /// <summary>Lays out the tree; <paramref name="card"/> makes the button of a technology.</summary>
    public TechTree(PlayViewMessage view, PlayContentMessage content, Func<PlayTechInfo, Button> card)
    {
        _view = view;
        _content = content;
        _cardWidth = UiKit.W(168);
        _cardHeight = Ui.PixelTheme.FontSize + 14;
        MouseFilter = MouseFilterEnum.Pass;
        int lastEra = Math.Min(content.Eras.Count - 1, view.Era + 1);
        var shown = content.Techs.Where(t => t.Era <= lastEra && t.Key.Length > 0).ToList();
        var byId = shown.ToDictionary(t => t.Id);

        // Column inside its era: one right of the deepest technology of the same era it needs.
        var local = new Dictionary<int, int>();
        int Local(PlayTechInfo t)
        {
            if (local.TryGetValue(t.Id, out int d))
            {
                return d;
            }

            local[t.Id] = 0; // guards against a loop in the data
            int best = 0;
            foreach (int p in t.From ?? [])
            {
                if (byId.TryGetValue(p, out PlayTechInfo? pt) && pt.Era == t.Era)
                {
                    best = Math.Max(best, Local(pt) + 1);
                }
            }

            local[t.Id] = best;
            return best;
        }

        foreach (PlayTechInfo t in shown)
        {
            Local(t);
        }

        // Columns of the eras one after another.
        var columns = new List<List<PlayTechInfo>>();
        float x = Margin;
        for (int era = 0; era <= lastEra; era++)
        {
            int count = shown.Where(t => t.Era == era).Select(t => local[t.Id]).DefaultIfEmpty(-1).Max() + 1;
            float x0 = x;
            for (int k = 0; k < count; k++)
            {
                columns.Add([.. shown.Where(t => t.Era == era && local[t.Id] == k)]);
                x += _cardWidth + Gap;
            }

            string note = era <= view.Era ? "" : view.EraNeed > 0 ? $"opens at {view.EraNeed} of the {content.Eras[view.Era]} ({view.EraKnown} now)" : "";
            _bands.Add((era, Math.Max(0, x0 - (Gap / 2f)), x - (Gap / 2f), note));
        }

        // Rows: each column in turn, every technology as close as it can get to the mean row of what it needs; the branch
        // (category) breaks ties, so a branch stays together.
        var rowOf = new Dictionary<int, float>();
        string[] cats = content.Categories is { } cs ? [.. cs] : [];
        int maxRow = 0;
        float colX = Margin;
        foreach (List<PlayTechInfo> col in columns)
        {
            var wish = new List<(PlayTechInfo T, float Want)>();
            foreach (PlayTechInfo t in col)
            {
                var from = (t.From ?? []).Where(rowOf.ContainsKey).Select(p => rowOf[p]).ToList();
                float want = from.Count > 0 ? from.Average() : Array.IndexOf(cats, t.Category) * 1.5f;
                wish.Add((t, want));
            }

            var taken = new HashSet<int>();
            foreach ((PlayTechInfo t, float want) in wish.OrderBy(w => w.Want).ThenBy(w => Array.IndexOf(cats, w.T.Category)).ThenBy(w => w.T.Id))
            {
                int row = Math.Max(0, (int)MathF.Round(want));
                for (int step = 0; ; step++)
                {
                    // Nearest free row, below first then above.
                    int down = row + step;
                    int up = row - step;
                    if (!taken.Contains(down))
                    {
                        row = down;
                        break;
                    }

                    if (up >= 0 && !taken.Contains(up))
                    {
                        row = up;
                        break;
                    }
                }

                taken.Add(row);
                rowOf[t.Id] = row;
                maxRow = Math.Max(maxRow, row);
                _at[t.Id] = new Vector2(colX, HeaderHeight + (row * (_cardHeight + RowGap)));
            }

            colX += _cardWidth + Gap;
        }

        foreach (PlayTechInfo t in shown)
        {
            Button b = card(t);
            b.Position = _at[t.Id];
            b.Size = new Vector2(_cardWidth, _cardHeight);
            AddChild(b);
        }

        CustomMinimumSize = new Vector2(colX - Gap + Margin, HeaderHeight + ((maxRow + 1) * (_cardHeight + RowGap)) + Margin);
    }

    /// <summary>Left edge of an era's band, to scroll the window to the people's era.</summary>
    public float EraX(int era) => _bands.FirstOrDefault(b => b.Era == era).X0;

    /// <inheritdoc/>
    public override void _Draw()
    {
        // Era bands, every other one a little lighter, with the era's name on top.
        Font font = UiKit.Font;
        int size = Ui.PixelTheme.FontSize;
        foreach ((int era, float x0, float x1, string note) in _bands)
        {
            Color band = era % 2 == 0 ? new Color(1, 1, 1, 0.035f) : new Color(0, 0, 0, 0.12f);
            if (era == _view.Era)
            {
                band = new Color(0.9f, 0.75f, 0.4f, 0.07f);
            }

            DrawRect(new Rect2(x0, 0, x1 - x0, Size.Y), band);
            DrawString(font, new Vector2(x0 + 10, size + 6), _content.Eras[era], HorizontalAlignment.Left, x1 - x0 - 20, size + 2, era <= _view.Era ? UiKit.Heading : UiKit.Muted);
            if (note.Length > 0)
            {
                DrawString(font, new Vector2(x0 + 10, (size * 2) + 12), note, HorizontalAlignment.Left, x1 - x0 - 20, size - 4, UiKit.Muted);
            }
        }

        // Branches: from the right side of what is needed to the left side of what needs it.
        foreach (PlayTechInfo t in _content.Techs)
        {
            if (!_at.TryGetValue(t.Id, out Vector2 to))
            {
                continue;
            }

            foreach (int p in t.From ?? [])
            {
                if (!_at.TryGetValue(p, out Vector2 from))
                {
                    continue;
                }

                bool known = _view.Techs[p].Known;
                bool both = known && _view.Techs[t.Id].Known;
                Color color = both ? new Color(0.45f, 0.75f, 0.4f, 0.8f) : known ? new Color(0.95f, 0.8f, 0.45f, 0.75f) : new Color(0.55f, 0.5f, 0.45f, 0.45f);
                Vector2 a = from + new Vector2(_cardWidth, _cardHeight / 2f);
                Vector2 b = to + new Vector2(0, _cardHeight / 2f);
                DrawPolyline(Curve(a, b), color, 2, true);
            }
        }
    }

    // A smooth S from a to b (a cubic curve with level ends).
    private static Vector2[] Curve(Vector2 a, Vector2 b)
    {
        const int n = 16;
        var pts = new Vector2[n + 1];
        float dx = Math.Max(24, (b.X - a.X) / 2);
        Vector2 c1 = a + new Vector2(dx, 0);
        Vector2 c2 = b - new Vector2(dx, 0);
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            float u = 1 - t;
            pts[i] = (u * u * u * a) + (3 * u * u * t * c1) + (3 * u * t * t * c2) + (t * t * t * b);
        }

        return pts;
    }
}
