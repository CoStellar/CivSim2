using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Game.Map;
using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// Waypoints (Patryk, 2026-11-24).
/// Roads and works: besides drawing hex by hex, Shift + clicks set points joined by the best way (the server finds it) and
/// Ctrl + clicks set points joined as straight as the hexes allow; letting the key go starts the building.
/// Units: Ctrl + right clicks give the selected units points to walk through; other units can be selected and given theirs
/// meanwhile; letting Ctrl go sends every planned unit at once.
/// </summary>
public partial class PlayGame
{
    private readonly List<(int Col, int Row)> _roadPoints = [];
    private bool _roadStraight;
    private readonly Dictionary<int, List<(int Col, int Row)>> _unitPlans = [];

    // True when the event was a waypoint action.
    private bool WaypointInput(InputEvent e)
    {
        if (_session is null || _overlay is null)
        {
            return false;
        }

        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } left
                when _drawMode == DrawMode.Road && !_drawing && (left.ShiftPressed || left.CtrlPressed) && !OverUi():
                if (MouseCell() is { } at)
                {
                    if (_roadPoints.Count == 0 || _roadPoints[^1] != at)
                    {
                        _roadPoints.Add(at);
                    }

                    _roadStraight = left.CtrlPressed;
                    _overlay.DrawPath.Clear();
                    _overlay.DrawPath.AddRange(_roadStraight ? LineCells(_roadPoints) : _roadPoints);
                    _uiTimer = 0;
                }

                return true;

            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } right
                when right.CtrlPressed && !_combat && _drawMode == DrawMode.None && SelectedUnitIds().Count > 0 && !OverUi():
                if (MouseCell() is { } to && _session.PlayView is { } v)
                {
                    foreach (int id in SelectedUnitIds())
                    {
                        if (!_unitPlans.TryGetValue(id, out List<(int Col, int Row)>? plan))
                        {
                            PlayUnitView? u = v.Units.FirstOrDefault(x => x.Id == id && x.Civ == v.Civ);
                            if (u is null)
                            {
                                continue;
                            }

                            plan = [(u.Col, u.Row)];
                            _unitPlans[id] = plan;
                        }

                        plan.Add(to);
                    }

                    ShowPlans();
                    _overlay.Flash(to.Col, to.Row, new Color(0.6f, 0.9f, 1f));
                }

                return true;

            case InputEventKey { Pressed: false, Keycode: Key.Shift or Key.Ctrl } key:
                bool used = false;
                if (_roadPoints.Count > 0 && (key.Keycode == Key.Ctrl) == _roadStraight)
                {
                    SendRoadPoints();
                    used = true;
                }

                if (key.Keycode == Key.Ctrl && _unitPlans.Count > 0)
                {
                    SendPlans();
                    used = true;
                }

                return used;
        }

        return false;
    }

    private void SendRoadPoints()
    {
        if (_roadPoints.Count < 2)
        {
            Toast("Click at least two hexes while holding the key: the road goes from the first to the last.");
        }
        else
        {
            List<(int Col, int Row)> cells = _roadStraight ? LineCells(_roadPoints) : [.. _roadPoints];
            int[] flat = [.. cells.SelectMany(c => new[] { c.Col, c.Row })];
            _session!.Play(_drawRoad >= WorkSlotBase ? PlayCommandKind.BuildWork : PlayCommandKind.BuildRoad, _drawRoad >= WorkSlotBase ? _drawRoad - WorkSlotBase : _drawRoad, cells: flat);
            _overlay!.Flash(cells[^1].Col, cells[^1].Row, new Color(0.85f, 0.7f, 0.5f));
        }

        _roadPoints.Clear();
        _overlay?.DrawPath.Clear();
        _uiTimer = 0;
    }

    private void SendPlans()
    {
        int sent = 0;
        foreach ((int unit, List<(int Col, int Row)> plan) in _unitPlans)
        {
            if (plan.Count < 2)
            {
                continue;
            }

            int[] flat = [.. plan.Skip(1).SelectMany(c => new[] { c.Col, c.Row })];
            (int col, int row) = plan[^1];
            _session!.Play(PlayCommandKind.Move, unit, col, row, _hiddenWalk.Contains(unit), cells: flat);
            sent++;
        }

        Toast(sent == 1 ? "The unit sets off along its points." : $"{sent} units set off along their points.");
        _unitPlans.Clear();
        ShowPlans();
    }

    /// <summary>Screenshots: a track armed with three points in straight lines, and our first unit planned through two points.</summary>
    public void ScriptPlans()
    {
        if (_session?.PlayView is not { } v || _overlay is null || v.Settlements.FirstOrDefault(s => s.Civ == v.Civ) is not { } home)
        {
            return;
        }

        ArmRoad(1);
        _roadStraight = true;
        _roadPoints.AddRange([(home.Col + 1, home.Row + 1), (home.Col + 5, home.Row + 2), (home.Col + 7, home.Row - 2)]);
        _overlay.DrawPath.Clear();
        _overlay.DrawPath.AddRange(LineCells(_roadPoints));
        if (v.Units.FirstOrDefault(u => u.Civ == v.Civ && u.State == 1) is { } unit)
        {
            _unitPlans[unit.Id] = [(unit.Col, unit.Row), (unit.Col - 4, unit.Row - 1), (unit.Col - 3, unit.Row - 5), (unit.Col + 1, unit.Row - 6)];
            ShowPlans();
        }
    }

    // Esc: the planned ways and road points are dropped.
    private bool CancelPlans()
    {
        if (_unitPlans.Count == 0 && _roadPoints.Count == 0)
        {
            return false;
        }

        _unitPlans.Clear();
        _roadPoints.Clear();
        _overlay?.DrawPath.Clear();
        ShowPlans();
        return true;
    }

    private void ShowPlans()
    {
        if (_overlay is null)
        {
            return;
        }

        _overlay.Plans.Clear();
        _overlay.Plans.AddRange(_unitPlans.Values);
        _uiTimer = 0;
    }

    // The hexes on straight lines from point to point (the shorter way round the planet), each touching the one before.
    private List<(int Col, int Row)> LineCells(List<(int Col, int Row)> points)
    {
        var cells = new List<(int Col, int Row)>();
        if (_session is null || !_session.Worlds.TryGetValue(0, out ClientWorld? w) || points.Count == 0)
        {
            return cells;
        }

        float period = w.Width * HexLayout.ColumnStep;
        cells.Add(points[0]);
        for (int k = 1; k < points.Count; k++)
        {
            Vector2 a = HexLayout.Center(points[k - 1].Col, points[k - 1].Row);
            Vector2 b = HexLayout.Center(points[k].Col, points[k].Row);
            if (b.X - a.X > period / 2)
            {
                b.X -= period;
            }
            else if (a.X - b.X > period / 2)
            {
                b.X += period;
            }

            int steps = Math.Max(1, (int)((b - a).Length() / 6));
            for (int s = 1; s <= steps; s++)
            {
                (int col, int row) = HexLayout.CellAt(a.Lerp(b, (float)s / steps));
                col = ((col % w.Width) + w.Width) % w.Width;
                if (row < 0 || row >= w.Height || cells[^1] == (col, row))
                {
                    continue;
                }

                cells.Add((col, row));
            }
        }

        return cells;
    }
}
