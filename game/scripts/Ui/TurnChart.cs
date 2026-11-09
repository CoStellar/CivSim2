using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>Bar chart of recent turn times (a simple developer chart, GDD 13).</summary>
public partial class TurnChart : Control
{
    private double[] _values = [];

    /// <summary>Replaces the data (milliseconds, oldest first).</summary>
    public void SetValues(IReadOnlyCollection<double> values)
    {
        _values = [.. values];
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        Vector2 size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.114f, 0.09f, 0.063f));
        if (_values.Length == 0)
        {
            DrawString(GetThemeDefaultFont(), new Vector2(8, 18), "Turn time: no turns yet", HorizontalAlignment.Left, -1, 14, Colors.White);
            return;
        }

        double max = 0.001;
        double sum = 0;
        foreach (double v in _values)
        {
            max = System.Math.Max(max, v);
            sum += v;
        }

        // Fixed slots, newest on the right, so the chart scrolls instead of stretching.
        float bar = size.X / Net.ClientSession.HistoryLength;
        float x0 = size.X - (bar * _values.Length);
        for (int i = 0; i < _values.Length; i++)
        {
            float h = (float)(_values[i] / max) * (size.Y - 24);
            DrawRect(new Rect2(x0 + (i * bar), size.Y - h, System.Math.Max(1, bar - 1), h), new Color(0.31f, 0.76f, 0.97f));
        }

        string label = string.Create(
            CultureInfo.InvariantCulture,
            $"Turn time: last {_values[^1]:F3} ms, avg {sum / _values.Length:F3} ms, max {max:F3} ms");
        DrawString(GetThemeDefaultFont(), new Vector2(8, 18), label, HorizontalAlignment.Left, -1, 14, Colors.White);
    }
}
