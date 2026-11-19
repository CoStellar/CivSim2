using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// The technology tree drawn as a layered graph (Patryk, 2026-11-19), by the Sugiyama method used in graph drawing:
/// <list type="number">
/// <item>layers: a technology stands one column right of the latest one it needs, and never left of its era's first column;</item>
/// <item>a line that skips columns gets a waypoint in every column it passes, which takes a place of its own there, so lines
/// run in the gaps beside the cards and never under them;</item>
/// <item>crossings: the order in every column is sorted by the mean position of the neighbours, sweeping right and left
/// a number of times (barycenter heuristic), starting from the branches (categories) so a branch stays together;</item>
/// <item>positions: every card and waypoint moves toward the mean height of its neighbours, keeping the order and a
/// minimum gap, so lines run as straight as they can.</item>
/// </list>
/// Lines enter a card at points spread over its left side. Hovering a card lights it, everything it needs (all the way
/// back) and the lines between them; the rest fades.
/// </summary>
public partial class TechTree : Control
{
    private const int Margin = 16;
    private const int ColumnGap = 96;
    private const int CardGap = 18;
    private const int WayHeight = 8;
    private const int WayGap = 8;
    private const int HeaderHeight = 52;
    private const int Sweeps = 12;

    private readonly PlayContentMessage _content;
    private readonly PlayViewMessage _view;
    private readonly int _cardWidth;
    private readonly int _cardHeight;
    private readonly List<Node> _nodes = [];
    private readonly List<List<Node>> _layers = [];
    private readonly List<Edge> _edges = [];
    private readonly Dictionary<int, Node> _ofTech = [];
    private readonly Dictionary<int, Button> _cards = [];
    private readonly List<(int Era, float X0, float X1)> _bands = [];
    private HashSet<int> _lit = [];

    /// <summary>The technology under the mouse, kept when the window is drawn again (-1: none).</summary>
    public static int Hovered { get; set; } = -1;

    private sealed class Node
    {
        public int Tech = -1; // -1: a waypoint of a long line
        public int Layer;
        public float Y;
        public float Order;
        public readonly List<Node> In = [];
        public readonly List<Node> Out = [];

        public float Height(TechTree t) => Tech >= 0 ? t._cardHeight : WayHeight;
    }

    // One line from a technology to one that needs it, through its waypoints.
    private sealed record Edge(int From, int To, List<Node> Path);

    /// <summary>Lays out the whole tree; <paramref name="card"/> makes the button of a technology.</summary>
    public TechTree(PlayViewMessage view, PlayContentMessage content, Func<PlayTechInfo, Button> card)
    {
        _view = view;
        _content = content;
        _cardWidth = UiKit.W(176);
        _cardHeight = Ui.PixelTheme.FontSize + 14;
        MouseFilter = MouseFilterEnum.Pass;
        var techs = content.Techs.Where(t => t.Key.Length > 0).ToList();
        Layers(techs);
        Links(techs);
        Order();
        Place();

        float width = Margin;
        foreach (PlayTechInfo t in techs)
        {
            Node n = _ofTech[t.Id];
            Button b = card(t);
            b.Position = new Vector2(X(n.Layer), n.Y);
            b.Size = new Vector2(_cardWidth, _cardHeight);
            int id = t.Id;
            b.MouseEntered += () => Light(id);
            b.MouseExited += () => Light(-1);
            AddChild(b);
            _cards[t.Id] = b;
            width = Math.Max(width, X(n.Layer) + _cardWidth + Margin);
        }

        float height = _nodes.Count == 0 ? 100 : _nodes.Max(n => n.Y + n.Height(this)) + Margin;
        CustomMinimumSize = new Vector2(width, height);
        Light(Hovered);
    }

    /// <summary>Left edge of an era's band, to scroll the window to the people's era.</summary>
    public float EraX(int era) => _bands.FirstOrDefault(b => b.Era == era).X0;

    private float X(int layer) => Margin + (layer * (_cardWidth + ColumnGap));

    // Step 1: columns. Each era starts right of everything before it; inside, one column right of what is needed.
    private void Layers(List<PlayTechInfo> techs)
    {
        var layer = new Dictionary<int, int>();
        int eraStart = 0;
        for (int era = 0; era < _content.Eras.Count; era++)
        {
            var ofEra = techs.Where(t => t.Era == era).ToList();
            int Of(PlayTechInfo t)
            {
                if (layer.TryGetValue(t.Id, out int l))
                {
                    return l;
                }

                layer[t.Id] = eraStart; // guards against a loop in the data
                int best = eraStart;
                foreach (int p in t.From ?? [])
                {
                    PlayTechInfo? pt = techs.FirstOrDefault(x => x.Id == p);
                    if (pt is not null && pt.Era <= era)
                    {
                        best = Math.Max(best, Of(pt) + 1);
                    }
                }

                layer[t.Id] = best;
                return best;
            }

            int last = eraStart;
            foreach (PlayTechInfo t in ofEra)
            {
                last = Math.Max(last, Of(t));
            }

            if (ofEra.Count > 0)
            {
                _bands.Add((era, X(eraStart) - (ColumnGap / 2f), X(last) + _cardWidth + (ColumnGap / 2f)));
                eraStart = last + 1;
            }
        }

        foreach (PlayTechInfo t in techs)
        {
            var n = new Node { Tech = t.Id, Layer = layer[t.Id] };
            _nodes.Add(n);
            _ofTech[t.Id] = n;
        }
    }

    // Step 2: lines, with a waypoint in every column a line passes.
    private void Links(List<PlayTechInfo> techs)
    {
        foreach (PlayTechInfo t in techs)
        {
            Node to = _ofTech[t.Id];
            foreach (int p in t.From ?? [])
            {
                if (!_ofTech.TryGetValue(p, out Node? from) || from.Layer >= to.Layer)
                {
                    continue;
                }

                var path = new List<Node> { from };
                Node last = from;
                for (int l = from.Layer + 1; l < to.Layer; l++)
                {
                    var way = new Node { Layer = l };
                    _nodes.Add(way);
                    Join(last, way);
                    path.Add(way);
                    last = way;
                }

                Join(last, to);
                path.Add(to);
                _edges.Add(new Edge(p, t.Id, path));
            }
        }

        int layers = _nodes.Count == 0 ? 0 : _nodes.Max(n => n.Layer) + 1;
        for (int l = 0; l < layers; l++)
        {
            _layers.Add([]);
        }

        foreach (Node n in _nodes)
        {
            _layers[n.Layer].Add(n);
        }

        static void Join(Node a, Node b)
        {
            a.Out.Add(b);
            b.In.Add(a);
        }
    }

    // Step 3: the order in each column. Start by branch, then sweep by the mean order of the neighbours.
    private void Order()
    {
        string[] cats = _content.Categories is { } cs ? [.. cs] : [];
        foreach (List<Node> layer in _layers)
        {
            foreach (Node n in layer)
            {
                n.Order = n.Tech >= 0 ? (Array.IndexOf(cats, _content.Techs[n.Tech].Category) * 100) + (n.Tech * 0.01f) : 0;
            }
        }

        // Waypoints start beside where they come from.
        Renumber(_layers[0]);
        for (int l = 1; l < _layers.Count; l++)
        {
            foreach (Node n in _layers[l].Where(n => n.Tech < 0))
            {
                n.Order = n.In[0].Order;
            }

            Renumber(_layers[l]);
        }

        for (int sweep = 0; sweep < Sweeps; sweep++)
        {
            bool right = sweep % 2 == 0;
            int from = right ? 1 : _layers.Count - 2;
            int to = right ? _layers.Count : -1;
            for (int l = from; l != to; l += right ? 1 : -1)
            {
                foreach (Node n in _layers[l])
                {
                    List<Node> near = right ? n.In : n.Out;
                    if (near.Count > 0)
                    {
                        n.Order = near.Average(x => x.Order);
                    }
                }

                Renumber(_layers[l]);
            }
        }

        static void Renumber(List<Node> layer)
        {
            layer.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : (a.Tech >= 0).CompareTo(b.Tech >= 0));
            for (int i = 0; i < layer.Count; i++)
            {
                layer[i].Order = i;
            }
        }
    }

    // Step 4: heights. Stack each column, then pull every item toward its neighbours, keeping order and gaps.
    private void Place()
    {
        foreach (List<Node> layer in _layers)
        {
            float y = HeaderHeight;
            foreach (Node n in layer)
            {
                n.Y = y;
                y += n.Height(this) + Gap(n);
            }
        }

        for (int sweep = 0; sweep < Sweeps; sweep++)
        {
            bool right = sweep % 2 == 0;
            int from = right ? 1 : _layers.Count - 2;
            int to = right ? _layers.Count : -1;
            for (int l = from; l != to; l += right ? 1 : -1)
            {
                List<Node> layer = _layers[l];
                var want = new float[layer.Count];
                for (int i = 0; i < layer.Count; i++)
                {
                    Node n = layer[i];
                    List<Node> near = [.. n.In, .. n.Out];
                    want[i] = near.Count == 0 ? n.Y : near.Average(Middle) - (n.Height(this) / 2);
                }

                Balance(layer, want);
            }
        }

        // Up to the top, below the era names.
        float top = _nodes.Count == 0 ? HeaderHeight : _nodes.Min(n => n.Y);
        foreach (Node n in _nodes)
        {
            n.Y += HeaderHeight - top;
        }
    }

    // Each item as near its wish as the order and the gaps allow: items that would overlap form a block, and a block sits
    // where its members want on average (so a crowd spreads up and down, not only down).
    private void Balance(List<Node> layer, float[] want)
    {
        var blocks = new List<(int First, int Last, float Sum, float Top)>();
        for (int i = 0; i < layer.Count; i++)
        {
            (int First, int Last, float Sum, float Top) block = (i, i, want[i], want[i]);
            while (blocks.Count > 0)
            {
                (int First, int Last, float Sum, float Top) prev = blocks[^1];
                float bottom = prev.Top + Span(layer, prev.First, prev.Last) + Math.Max(Gap(layer[prev.Last]), Gap(layer[block.First]));
                if (block.Top >= bottom)
                {
                    break;
                }

                // Merge: the members' wishes, each less its offset from the block's top.
                blocks.RemoveAt(blocks.Count - 1);
                float offset = Span(layer, prev.First, block.First - 1) + Math.Max(Gap(layer[block.First - 1]), Gap(layer[block.First]));
                float sum = prev.Sum + block.Sum - (offset * (block.Last - block.First + 1));
                int count = block.Last - prev.First + 1;
                block = (prev.First, block.Last, sum, sum / count);
            }

            blocks.Add(block);
        }

        foreach ((int first, int last, float _, float top) in blocks)
        {
            float y = top;
            for (int i = first; i <= last; i++)
            {
                layer[i].Y = y;
                y += layer[i].Height(this) + (i < last ? Math.Max(Gap(layer[i]), Gap(layer[i + 1])) : 0);
            }
        }
    }

    // Height of items first..last with the gaps between them.
    private float Span(List<Node> layer, int first, int last)
    {
        float h = 0;
        for (int i = first; i <= last; i++)
        {
            h += layer[i].Height(this) + (i < last ? Math.Max(Gap(layer[i]), Gap(layer[i + 1])) : 0);
        }

        return h;
    }

    private float Middle(Node n) => n.Y + (n.Height(this) / 2);

    private static float Gap(Node n) => n.Tech >= 0 ? CardGap : WayGap;

    // Lights a technology, what it needs all the way back, and the lines between them (-1: nothing lit).
    private void Light(int tech)
    {
        Hovered = tech;
        _lit = [];
        if (tech >= 0)
        {
            var stack = new Stack<int>([tech]);
            while (stack.Count > 0)
            {
                int t = stack.Pop();
                if (_lit.Add(t))
                {
                    foreach (int p in _content.Techs[t].From ?? [])
                    {
                        stack.Push(p);
                    }
                }
            }
        }

        foreach ((int id, Button b) in _cards)
        {
            b.Modulate = _lit.Count == 0 || _lit.Contains(id) ? Colors.White : new Color(1, 1, 1, 0.35f);
        }

        QueueRedraw();
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        Font font = UiKit.Font;
        int size = Ui.PixelTheme.FontSize;
        foreach ((int era, float x0, float x1) in _bands)
        {
            Color band = era % 2 == 0 ? new Color(1, 1, 1, 0.035f) : new Color(0, 0, 0, 0.12f);
            if (era == _view.Era)
            {
                band = new Color(0.9f, 0.75f, 0.4f, 0.07f);
            }

            float left = Math.Max(0, x0);
            DrawRect(new Rect2(left, 0, x1 - left, Size.Y), band);
            string note = era <= _view.Era ? "" : era == _view.Era + 1 && _view.EraNeed > 0 ? $"opens at {_view.EraNeed} of the {_content.Eras[_view.Era]} ({_view.EraKnown} now)" : "later";
            DrawString(font, new Vector2(left + 12, size + 8), _content.Eras[era], HorizontalAlignment.Left, x1 - left - 24, size + 2, era <= _view.Era ? UiKit.Heading : UiKit.Muted);
            if (note.Length > 0)
            {
                DrawString(font, new Vector2(left + 12, (size * 2) + 14), note, HorizontalAlignment.Left, x1 - left - 24, size - 4, UiKit.Muted);
            }
        }

        // Where each line enters its card: spread over the left side, in the order of where the lines come from.
        var entry = new Dictionary<(int, int), float>();
        foreach (IGrouping<int, Edge> g in _edges.GroupBy(e => e.To))
        {
            var list = g.OrderBy(e => Middle(e.Path[^2])).ToList();
            for (int k = 0; k < list.Count; k++)
            {
                entry[(list[k].From, list[k].To)] = list.Count == 1 ? 0.5f : 0.2f + (0.6f * k / (list.Count - 1));
            }
        }

        // Faint lines first, lit ones on top.
        foreach (bool lit in new[] { false, true })
        {
            foreach (Edge e in _edges)
            {
                bool on = _lit.Contains(e.To) && _lit.Contains(e.From);
                if (on != lit)
                {
                    continue;
                }

                bool known = _view.Techs[e.From].Known;
                Color color = on ? new Color(1f, 0.9f, 0.55f, 1f)
                    : known && _view.Techs[e.To].Known ? new Color(0.45f, 0.75f, 0.4f, 0.75f)
                    : known ? new Color(0.95f, 0.8f, 0.45f, 0.7f)
                    : new Color(0.6f, 0.55f, 0.5f, 0.4f);
                if (_lit.Count > 0 && !on)
                {
                    color.A *= 0.35f;
                }

                DrawPolyline(Route(e, entry[(e.From, e.To)]), color, on ? 3 : 2, true);
            }
        }
    }

    // The way of a line: out of the right side of its card, level through each waypoint's column, curves in the gaps.
    private Vector2[] Route(Edge e, float entryAt)
    {
        var pts = new List<Vector2>();
        Node first = e.Path[0];
        Vector2 a = new(X(first.Layer) + _cardWidth, Middle(first));
        for (int k = 1; k < e.Path.Count; k++)
        {
            Node n = e.Path[k];
            bool card = n.Tech >= 0;
            float y = card ? n.Y + (_cardHeight * entryAt) : Middle(n);
            Curve(pts, a, new Vector2(X(n.Layer), y));
            if (!card)
            {
                // Straight through the column, in the waypoint's own place.
                a = new Vector2(X(n.Layer) + _cardWidth, y);
                pts.Add(a);
            }
        }

        return [.. pts];
    }

    // A smooth S from a to b with level ends (a cubic curve), in the gap between two columns.
    private static void Curve(List<Vector2> pts, Vector2 a, Vector2 b)
    {
        const int n = 14;
        float dx = (b.X - a.X) / 2;
        Vector2 c1 = a + new Vector2(dx, 0);
        Vector2 c2 = b - new Vector2(dx, 0);
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            float u = 1 - t;
            pts.Add((u * u * u * a) + (3 * u * u * t * c1) + (3 * u * t * t * c2) + (t * t * t * b));
        }
    }
}
