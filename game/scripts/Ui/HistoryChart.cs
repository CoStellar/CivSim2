using System;
using System.Collections.Generic;
using System.Globalization;
using CivSim.Game.Map;
using CivSim.Game.Net;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>
/// Line chart of one statistic of every civilization over game years (stage 5): the followed civilization is drawn
/// thick and bright, the others thin and faded, each in its civilization colour.
/// </summary>
public partial class HistoryChart : Control
{
    private CivHistory? _history;
    private int _followed = -1;
    private int _count = -1;

    /// <summary>What the chart shows.</summary>
    public enum Measure
    {
        /// <summary>People in lived-in settlements.</summary>
        People,

        /// <summary>Lived-in settlements.</summary>
        Settlements,

        /// <summary>Known technologies.</summary>
        Technologies,
    }

    /// <summary>The statistic shown.</summary>
    public Measure Shown { get; set; } = Measure.People;

    /// <summary>Redraws when there are new samples or another civilization is followed.</summary>
    public void Show(CivHistory history, int followed)
    {
        if (ReferenceEquals(history, _history) && followed == _followed && history.Samples.Count == _count)
        {
            return;
        }

        _history = history;
        _followed = followed;
        _count = history.Samples.Count;
        QueueRedraw();
    }

    /// <summary>Forces a redraw (after the measure changed).</summary>
    public void Refresh()
    {
        _count = -1;
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        Vector2 size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.114f, 0.09f, 0.063f));
        Font font = GetThemeDefaultFont();
        IReadOnlyList<CivHistory.Sample>? samples = _history?.Samples;
        if (samples is null || samples.Count < 2)
        {
            DrawString(font, new Vector2(8, 18), "Statistics appear after a few turns.", HorizontalAlignment.Left, -1, 13, Colors.Gray);
            return;
        }

        long max = 1;
        var civs = new SortedSet<int>();
        foreach (CivHistory.Sample s in samples)
        {
            foreach ((int civ, var v) in s.Civs)
            {
                civs.Add(civ);
                max = Math.Max(max, Value(v));
            }
        }

        const float left = 8;
        const float top = 22;
        float w = size.X - left - 8;
        float h = size.Y - top - 18;
        long y0 = samples[0].Year;
        long y1 = Math.Max(y0 + 1, samples[^1].Year);
        foreach (int civ in civs)
        {
            bool followed = civ == _followed;
            Color color = OverlayColors.CivColor(civ);
            if (_followed >= 0 && !followed)
            {
                color = color with { A = 0.35f };
            }

            var points = new List<Vector2>(samples.Count);
            foreach (CivHistory.Sample s in samples)
            {
                if (s.Civs.TryGetValue(civ, out var v))
                {
                    points.Add(new Vector2(left + (w * (s.Year - y0) / (y1 - y0)), top + h - (h * Value(v) / max)));
                }
            }

            if (points.Count >= 2)
            {
                DrawPolyline([.. points], color, followed ? 3f : 1.5f, antialiased: true);
            }
        }

        string what = Shown switch { Measure.Settlements => "Settlements", Measure.Technologies => "Technologies", _ => "People" };
        DrawString(font, new Vector2(left, 16), string.Create(CultureInfo.InvariantCulture, $"{what} (max {max:N0})"), HorizontalAlignment.Left, -1, 13, Colors.White);
        DrawString(font, new Vector2(left, size.Y - 4), string.Create(CultureInfo.InvariantCulture, $"year {y0}"), HorizontalAlignment.Left, -1, 12, Colors.Gray);
        DrawString(font, new Vector2(size.X - 90, size.Y - 4), string.Create(CultureInfo.InvariantCulture, $"year {y1}"), HorizontalAlignment.Right, 82, 12, Colors.Gray);
    }

    private long Value((long People, int Settlements, int Technologies) v) => Shown switch
    {
        Measure.Settlements => v.Settlements,
        Measure.Technologies => v.Technologies,
        _ => v.People,
    };
}
