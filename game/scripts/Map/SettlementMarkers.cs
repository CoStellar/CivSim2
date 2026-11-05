using System.Collections.Generic;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Map;

/// <summary>
/// Developer markers for settlements (until pixel art in Stage 5): a square in the owner's colour, larger for
/// higher levels. Drawn on every copy of the wrapped map that is on screen.
/// </summary>
public partial class SettlementMarkers : Node2D
{
    private readonly List<SettlementView> _settlements = [];
    private IReadOnlyList<SettlementView>? _source;
    private Rect2 _view;
    private Vector2 _period;
    private int _world;

    /// <summary>Sets what to draw; redraws only when the settlements or the visible area changed.</summary>
    public void Show(IReadOnlyList<SettlementView>? settlements, int world, Vector2 period, Rect2 view)
    {
        if (ReferenceEquals(settlements, _source) && world == _world && period == _period && view == _view)
        {
            return;
        }

        _source = settlements;
        _settlements.Clear();
        if (settlements is not null)
        {
            _settlements.AddRange(settlements);
        }

        _world = world;
        _period = period;
        _view = view;
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (_period.X <= 0 || _period.Y <= 0)
        {
            return;
        }

        foreach (SettlementView s in _settlements)
        {
            if (s.World != _world)
            {
                continue;
            }

            Vector2 c = HexLayout.Center(s.Col, s.Row);
            int kx0 = Mathf.FloorToInt((_view.Position.X - c.X) / _period.X);
            int kx1 = Mathf.CeilToInt((_view.End.X - c.X) / _period.X);
            int ky0 = Mathf.FloorToInt((_view.Position.Y - c.Y) / _period.Y);
            int ky1 = Mathf.CeilToInt((_view.End.Y - c.Y) / _period.Y);
            float half = 7 + (3 * s.Level);
            bool empty = s.Population is { } people && System.Array.TrueForAll(people, p => p == 0);
            Color fill = empty ? new Color(0.35f, 0.35f, 0.35f) : OverlayColors.CivColor(s.Owner);
            for (int ky = ky0; ky <= ky1; ky++)
            {
                for (int kx = kx0; kx <= kx1; kx++)
                {
                    Vector2 p = c + new Vector2(kx * _period.X, ky * _period.Y);
                    var rect = new Rect2(p - new Vector2(half, half), new Vector2(2 * half, 2 * half));
                    DrawRect(rect, Colors.Black, filled: true);
                    DrawRect(rect.Grow(-2), fill, filled: true);
                }
            }
        }
    }
}
