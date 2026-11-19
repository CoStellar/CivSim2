using System.Linq;
using System;
using System.Collections.Generic;
using CivSim.Game.Map;
using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>What a drawn path is for.</summary>
public enum DrawMode
{
    /// <summary>Not drawing.</summary>
    None,

    /// <summary>Right drag with units selected: they walk exactly these hexes.</summary>
    Move,

    /// <summary>Left drag after "Draw a patrol": the selected units walk the loop again and again.</summary>
    Patrol,

    /// <summary>Left drag after "Draw the way": a standing caravan route along these hexes.</summary>
    Route,

    /// <summary>Left drag after picking a road in the bar: build a road over these hexes.</summary>
    Road,
}

/// <summary>
/// Drawing paths hex by hex (decision of 2026-11-18): while the mouse button is held, every hex under the mouse joins the
/// path in order. Moving back onto the hex before the last takes the last one off again. A quick click is still a simple
/// "go there"; with Shift several units can be selected and all take the same path.
/// </summary>
public partial class PlayGame
{
    private DrawMode _drawMode;
    private bool _drawing;
    private Vector2 _lastDrawPoint;
    private (int From, int To, int Good) _drawRoute;
    private int _drawRoad;

    // Arms a drawing that starts with the next left press (patrol, route).
    private void ArmDraw(DrawMode mode, (int From, int To, int Good) route = default)
    {
        _drawMode = mode;
        _drawRoute = route;
        _drawing = false;
        _overlay?.DrawPath.Clear();
        if (_overlay is not null)
        {
            _overlay.DrawColor = mode switch
            {
                DrawMode.Route => new Color(0.95f, 0.75f, 0.35f),
                DrawMode.Road => new Color(0.85f, 0.7f, 0.5f),
                _ => new Color(0.55f, 0.85f, 1f),
            };
        }

        _uiTimer = 0;
    }

    private void BeginDraw(DrawMode mode)
    {
        _drawMode = mode;
        _drawing = true;
        _overlay?.DrawPath.Clear();
        if (_overlay is not null && mode == DrawMode.Move)
        {
            _overlay.DrawColor = new Color(1f, 0.95f, 0.5f);
        }

        _lastDrawPoint = GetGlobalMousePosition();
        AddDrawCell(_lastDrawPoint);
    }

    // Adds every hex between the last mouse point and this one, so a fast mouse leaves no gaps.
    private void ExtendDraw()
    {
        Vector2 now = GetGlobalMousePosition();
        float length = (now - _lastDrawPoint).Length();
        int steps = Math.Max(1, (int)(length / 8));
        for (int k = 1; k <= steps; k++)
        {
            AddDrawCell(_lastDrawPoint.Lerp(now, (float)k / steps));
        }

        _lastDrawPoint = now;
    }

    private void AddDrawCell(Vector2 world)
    {
        if (_overlay is null || _session is null || !_session.Worlds.TryGetValue(0, out ClientWorld? w))
        {
            return;
        }

        (int col, int row) = HexLayout.CellAt(world);
        if (row < 0 || row >= w.Height)
        {
            return;
        }

        col = ((col % w.Width) + w.Width) % w.Width;
        List<(int Col, int Row)> path = _overlay.DrawPath;
        if (path.Count > 0 && path[^1] == (col, row))
        {
            return;
        }

        if (path.Count > 1 && path[^2] == (col, row))
        {
            path.RemoveAt(path.Count - 1); // going back undoes the last hex
            return;
        }

        path.Add((col, row));
    }

    private void FinishDraw()
    {
        if (_session is null || _overlay is null)
        {
            return;
        }

        List<(int Col, int Row)> path = _overlay.DrawPath;
        var cells = new int[path.Count * 2];
        for (int k = 0; k < path.Count; k++)
        {
            cells[2 * k] = path[k].Col;
            cells[(2 * k) + 1] = path[k].Row;
        }

        switch (_drawMode)
        {
            case DrawMode.Move:
                if (path.Count <= 1 && (path.Count == 1 ? path[0] : MouseCell()) is { } spot && SendFormation(spot.Col, spot.Row))
                {
                    break; // several soldiers in combat mode: in formation
                }

                foreach (int unit in SelectedUnitIds())
                {
                    if (path.Count <= 1)
                    {
                        // A click, not a drawing: go there (or attack what is there).
                        (int col, int row) = path.Count == 1 ? path[0] : MouseCell() ?? (0, 0);
                        bool attack = IsArmy(unit) && EnemyAt(col, row);
                        bool wall = IsArmy(unit) && !attack && EnemyWallAt(col, row);
                        if (!attack && ShipAt(col, row, unit) is int ship)
                        {
                            _session.Play(PlayCommandKind.Board, unit, b: ship); // aboard our ship there
                            continue;
                        }

                        if (!attack && Carrying(unit))
                        {
                            _session.Play(PlayCommandKind.Land, unit, col, row); // the ship lands its soldiers there
                            continue;
                        }

                        _session.Play(attack ? PlayCommandKind.Attack : wall ? PlayCommandKind.AttackWall : PlayCommandKind.Move, unit, col, row, _hiddenWalk.Contains(unit));
                        _overlay.Flash(col, row, attack ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 1f, 1f));
                    }
                    else
                    {
                        (int col, int row) = path[^1];
                        _session.Play(PlayCommandKind.Move, unit, col, row, _hiddenWalk.Contains(unit), cells: cells);
                        _overlay.Flash(col, row, new Color(1f, 1f, 1f));
                    }
                }

                break;
            case DrawMode.Patrol:
                if (path.Count >= 2)
                {
                    foreach (int unit in SelectedUnitIds())
                    {
                        _session.Play(PlayCommandKind.Patrol, unit, cells: cells);
                    }
                }
                else
                {
                    Toast("Hold the left button and draw the patrol over the hexes.");
                }

                break;
            case DrawMode.Route:
                if (path.Count >= 2)
                {
                    _session.Play(PlayCommandKind.Route, _drawRoute.From, _drawRoute.Good, _routeKeep, flag: false, b: _drawRoute.To, c: 20, cells: cells, d: _routeUntil);
                }

                break;
            case DrawMode.Road:
                if (path.Count >= 1)
                {
                    _session.Play(_drawRoad >= WorkSlotBase ? PlayCommandKind.BuildWork : PlayCommandKind.BuildRoad, _drawRoad >= WorkSlotBase ? _drawRoad - WorkSlotBase : _drawRoad, cells: cells);
                    _overlay.Flash(path[^1].Col, path[^1].Row, new Color(0.85f, 0.7f, 0.5f));
                }

                break;
        }

        CancelDraw();
    }

    // Cycles which of our routes show (G): none, orders under way, those and the standing ones; the selected ones always show.
    private void ToggleRoutes()
    {
        if (_overlay is not null)
        {
            _overlay.RouteDisplay = (_overlay.RouteDisplay + 1) % 3;
            _overlay.Restatic();
            Toast(_overlay.RouteDisplay switch
            {
                0 => "Routes: only those of what is selected (G for more).",
                1 => "Routes: orders under way - marches and carriers (G for more).",
                _ => "Routes: orders under way, patrols and caravan routes (G to hide).",
            });
        }
    }

    private void CancelDraw()
    {
        _drawMode = DrawMode.None;
        _drawing = false;
        _overlay?.DrawPath.Clear();
        _uiTimer = 0;
    }

    private List<int> SelectedUnitIds()
    {
        var list = new List<int>();
        if (_overlay is not null)
        {
            list.AddRange(_overlay.SelectedUnits);
        }

        if (_unit >= 0 && !list.Contains(_unit))
        {
            list.Add(_unit);
        }

        return list;
    }

    private string? DrawHint() => _drawMode switch
    {
        DrawMode.Move when _drawing => "Draw the way over the hexes. Let go of the right button to send them.",
        DrawMode.Patrol => _drawing ? "Draw the patrol over the hexes. Let go to start it." : "Hold the left button and draw the patrol over the hexes. Esc: cancel.",
        DrawMode.Route => _drawing ? "Draw the caravans' way. Let go to set the route." : "Hold the left button and draw the caravans' way from here to the other settlement. Esc: cancel.",
        DrawMode.Road => _drawing ? $"Draw the road over the hexes ({_overlay?.DrawPath.Count ?? 0} so far). Let go to start building." : "Hold the left button and draw the road over the hexes, also into empty land. Esc: cancel.",
        _ => null,
    };

    // A wall of another people on the hex: soldiers sent there break it.
    private bool EnemyWallAt(int col, int row)
    {
        if (_session is null || !_session.Worlds.TryGetValue(0, out CivSim.Game.Net.ClientWorld? w) || _session.PlayView is not { } v)
        {
            return false;
        }

        int i = w.Index(col, row);
        return (w.Works[i] & 4) != 0 && w.Owner[i] != v.Civ;
    }

    // Our ship on the hex that can take this land unit aboard (null = none).
    private int? ShipAt(int col, int row, int unit)
    {
        if (_session?.PlayView is not { } v || _session.PlayContent is not { } c)
        {
            return null;
        }

        PlayUnitView? me = v.Units.FirstOrDefault(x => x.Id == unit);
        if (me is null || c.Units[me.Kind].Naval || c.Units[me.Kind].System)
        {
            return null;
        }

        PlayUnitView? ship = v.Units.FirstOrDefault(x => x.Civ == v.Civ && x.Col == col && x.Row == row && c.Units[x.Kind].Carries > 0);
        return ship?.Id;
    }

    // A ship of ours with soldiers aboard.
    private bool Carrying(int unit) => _session?.PlayView?.Units.FirstOrDefault(x => x.Id == unit) is { Aboard: > 0 };
}
