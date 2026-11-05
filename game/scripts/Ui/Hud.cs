using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CivSim.Game.Map;
using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>
/// Developer HUD (GDD 12.4 layout, simplified): top bar with turn, speed and overlay; cell inspector on the left;
/// chronicle on the right; turn-time chart at the bottom. Built in code so it needs no art.
/// </summary>
public partial class Hud : CanvasLayer
{
    private static readonly (SimulationSpeed Speed, string Label)[] Speeds =
    [
        (SimulationSpeed.Paused, "Pause"), (SimulationSpeed.X1, "x1"), (SimulationSpeed.X2, "x2"), (SimulationSpeed.X5, "x5"), (SimulationSpeed.Max, "Max"),
    ];

    private readonly Dictionary<SimulationSpeed, Button> _speedButtons = [];
    private Label _turn = null!;
    private Label _perf = null!;
    private OptionButton _overlay = null!;
    private RichTextLabel _inspector = null!;
    private RichTextLabel _chronicle = null!;
    private TurnChart _chart = null!;

    /// <summary>Raised when the user picks a speed.</summary>
    public event Action<SimulationSpeed>? SpeedChosen;

    /// <summary>Raised when the user picks an overlay.</summary>
    public event Action<MapOverlay>? OverlayChosen;

    /// <inheritdoc />
    public override void _Ready()
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // Top bar.
        var top = new PanelContainer();
        top.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        root.AddChild(top);
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 12);
        top.AddChild(bar);
        _turn = new Label { Text = "Connecting...", CustomMinimumSize = new Vector2(220, 0) };
        bar.AddChild(_turn);
        var group = new ButtonGroup();
        foreach ((SimulationSpeed speed, string label) in Speeds)
        {
            var b = new Button { Text = label, ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None };
            SimulationSpeed s = speed;
            b.Pressed += () => SpeedChosen?.Invoke(s);
            bar.AddChild(b);
            _speedButtons[speed] = b;
        }

        bar.AddChild(new Label { Text = "   Overlay:" });
        _overlay = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach (string name in Enum.GetNames<MapOverlay>())
        {
            _overlay.AddItem(name);
        }

        _overlay.ItemSelected += i => OverlayChosen?.Invoke((MapOverlay)(int)i);
        bar.AddChild(_overlay);
        _perf = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right };
        bar.AddChild(_perf);

        // Inspector (left).
        var left = new PanelContainer { CustomMinimumSize = new Vector2(320, 0) };
        left.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        left.OffsetTop = 44;
        left.OffsetBottom = -140;
        root.AddChild(left);
        _inspector = new RichTextLabel { BbcodeEnabled = true, Text = "Click a hex to inspect it.", FitContent = false };
        left.AddChild(_inspector);

        // Chronicle (right).
        var right = new PanelContainer { CustomMinimumSize = new Vector2(380, 0) };
        right.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        right.OffsetLeft = -380;
        right.OffsetTop = 44;
        right.OffsetBottom = -140;
        root.AddChild(right);
        var box = new VBoxContainer();
        right.AddChild(box);
        box.AddChild(new Label { Text = "Chronicle" });
        _chronicle = new RichTextLabel { ScrollFollowing = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(_chronicle);

        // Chart (bottom).
        _chart = new TurnChart { MouseFilter = Control.MouseFilterEnum.Ignore };
        _chart.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _chart.OffsetTop = -130;
        root.AddChild(_chart);
    }

    /// <summary>Shows the current overlay in the drop-down (after a keyboard change).</summary>
    public void ShowOverlay(MapOverlay overlay) => _overlay.Select((int)overlay);

    /// <summary>Appends chronicle lines.</summary>
    public void AddChronicle(IReadOnlyList<ChronicleEntry> lines)
    {
        foreach (ChronicleEntry e in lines)
        {
            _chronicle.AppendText(string.Create(CultureInfo.InvariantCulture, $"[{e.Turn}] {e.Text}\n"));
        }
    }

    /// <summary>Refreshes the top bar, the chart and the inspector.</summary>
    public void UpdateStatus(ClientSession session, MapView map, double fps)
    {
        GameViewMessage? view = session.View;
        if (view is not null)
        {
            ClientWorld? w = map.World;
            string where = w is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"   {w.Name} {w.Width}x{w.Height}");
            _turn.Text = string.Create(CultureInfo.InvariantCulture, $"Turn {view.Turn}   Year {view.Month / 12}{where}");
            if (_speedButtons.TryGetValue(view.Speed, out Button? b) && !b.ButtonPressed)
            {
                b.SetPressedNoSignal(true);
            }
        }

        string error = session.LastError is null ? string.Empty : "   ! " + session.LastError;
        _perf.Text = string.Create(CultureInfo.InvariantCulture, $"{fps:F0} FPS   {map.VisibleChunks} chunks drawn{error}");
        _chart.SetValues(session.TurnMilliseconds);
        _inspector.Text = Inspect(session, map);
    }

    private static string Inspect(ClientSession session, MapView map)
    {
        if (map.Selected is not { } sel || map.World is not { } w)
        {
            return "Click a hex to inspect it.\n\n[color=gray]Mouse: left = select, right/middle drag = move, wheel = zoom.\nKeys: WASD/arrows move, +/- zoom, Space pause, 1-5 speed, Tab overlay.[/color]";
        }

        int i = w.Index(sel.Col, sel.Row);
        ContentMessage? c = session.Content;
        var sb = new StringBuilder();
        CultureInfo inv = CultureInfo.InvariantCulture;
        sb.Append(inv, $"[b]Cell {sel.Col}, {sel.Row}[/b]\n");
        if (w.Visibility[i] == MapChunkMessage.Unknown)
        {
            sb.Append("Not discovered.");
            return sb.ToString();
        }

        string biome = c is not null && w.Biome[i] < c.Biomes.Count ? c.Biomes[w.Biome[i]].Name : w.Biome[i].ToString(inv);
        string relief = c is not null && w.Relief[i] < c.Reliefs.Count ? c.Reliefs[w.Relief[i]] : w.Relief[i].ToString(inv);
        string feature = c is not null && w.Feature[i] < c.Features.Count ? c.Features[w.Feature[i]] : w.Feature[i].ToString(inv);
        sb.Append(inv, $"Biome: {biome}\nRelief: {relief}\nFeature: {feature}\n");
        sb.Append(inv, $"Elevation: {w.Elevation[i]} (sea level 100)\nTemperature: {w.Temperature[i]} °C\nHumidity: {w.Humidity[i]}\nFertility: {w.Fertility[i]}\n");
        int d = w.Deposit[i];
        if (d != 0)
        {
            string name = c is not null && d <= c.Deposits.Count ? c.Deposits[d - 1].Name : d.ToString(inv);
            string depth = w.DepositDepth[i] switch { 0 => "surface", 1 => "shallow", _ => "deep" };
            sb.Append(inv, $"Deposit: {name}, {w.DepositRichness[i]} units, {depth}\n");
        }
        else
        {
            sb.Append("Deposit: none\n");
        }

        var rivers = new List<string>();
        string[] sides = ["N", "NE", "SE"];
        for (int e = 0; e < MapChunkMessage.RiverEdgesPerCell; e++)
        {
            int size = w.River[(i * MapChunkMessage.RiverEdgesPerCell) + e];
            if (size > 0)
            {
                rivers.Add(string.Create(inv, $"{sides[e]} (size {size})"));
            }
        }

        sb.Append("Rivers on own edges: ").Append(rivers.Count == 0 ? "none" : string.Join(", ", rivers)).Append('\n');
        sb.Append("Owner: ").Append(w.Owner[i] < 0 ? "none" : FindCiv(session, w.Owner[i])).Append('\n');
        AppendSettlement(sb, session, w.Id, sel.Col, sel.Row);
        return sb.ToString();
    }

    // Settlement on the selected cell: owner, level and warehouse (only when the server sent it).
    private static void AppendSettlement(StringBuilder sb, ClientSession session, int world, int col, int row)
    {
        if (session.View?.Settlements is not { } settlements)
        {
            return;
        }

        CultureInfo inv = CultureInfo.InvariantCulture;
        ContentMessage? c = session.Content;
        foreach (SettlementView s in settlements)
        {
            if (s.World != world || s.Col != col || s.Row != row)
            {
                continue;
            }

            string level = c?.SettlementLevels is { } levels && s.Level < levels.Count ? levels[s.Level] : s.Level.ToString(inv);
            sb.Append(inv, $"\n[b]{s.Name}[/b]\n{level} of {FindCiv(session, s.Owner)}\n");
            if (s.Stock is null)
            {
                sb.Append("[color=gray]Warehouse not visible.[/color]\n");
                continue;
            }

            long total = 0;
            var lines = new StringBuilder();
            for (int g = 0; g < s.Stock.Length; g++)
            {
                if (s.Stock[g] == 0)
                {
                    continue;
                }

                total += s.Stock[g];
                string name = c?.Goods is { } goods && g < goods.Count ? goods[g].Name : g.ToString(inv);
                lines.Append(inv, $"  {name}: {s.Stock[g] / 100.0:F2}\n");
            }

            sb.Append(inv, $"Warehouse ({total / 100.0:F2} units):\n");
            sb.Append(lines.Length == 0 ? "  empty\n" : lines.ToString());
        }
    }

    private static string FindCiv(ClientSession session, int id)
    {
        if (session.View is { } v)
        {
            foreach (CivilizationView c in v.Civilizations)
            {
                if (c.Id == id)
                {
                    return c.Name;
                }
            }
        }

        return id.ToString(CultureInfo.InvariantCulture);
    }
}
