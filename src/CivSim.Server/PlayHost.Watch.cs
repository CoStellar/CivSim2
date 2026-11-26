using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CivSim.Protocol.Messages;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Server;

/// <summary>
/// The full view of observers and fallen players (Patryk, 2026-11-26): a table of every civilization, the intentions log (AI decisions
/// and summaries of players' orders: effects, not every command), watching through one civilization's eyes, and their own chat
/// channel (they read everyone and write only to spectators, so nobody tells a player what the others plan).
/// </summary>
public sealed partial class PlayHost
{
    // The intentions log: the newest lines, and how many were ever written (spectators count what they have).
    private readonly List<PlayIntentLine> _intents = [];
    private long _intentCount;
    private int _aiLogRead;
    private long _lastOverview;
    private long _lastIntentFlush;

    // Players' orders since the last summary, per civilization.
    private readonly SortedDictionary<int, Tally> _tallies = [];

    private bool Spectating(PlayClient c) => c.Connection.IsOpen && (c.Role == PlayPlayerInfo.Observer || (c.Civ >= 0 && _game.Civs[c.Civ].Dead));

    // Every round: the AI writes its decisions while someone watches; the log and the table go out every few seconds.
    private void Spectators()
    {
        List<PlayClient> watching = _clients.FindAll(Spectating);
        _game.AiLogOn = watching.Count > 0; // costs nothing while nobody watches (not in the game state)
        if (watching.Count == 0)
        {
            _aiLogRead = _game.AiLogEntries.Count;
            _tallies.Clear();
            return;
        }

        ReadAiLog();
        long now = _game.Ticks;
        if (now - _lastIntentFlush >= (long)Rules.IntentSeconds * SimContent.TicksPerSecond)
        {
            _lastIntentFlush = now;
            FlushTallies();
        }

        long clock = Clock();
        if (clock - _lastOverview < Seconds(Rules.OverviewSeconds) && !watching.Exists(c => c.IntentsSent == 0 && _intentCount > 0))
        {
            return;
        }

        _lastOverview = clock;
        List<PlayOverviewRow> rows = Overview();
        foreach (PlayClient c in watching)
        {
            long first = Math.Max(c.IntentsSent, _intentCount - _intents.Count);
            List<PlayIntentLine> fresh = _intents.GetRange((int)(first - (_intentCount - _intents.Count)), (int)(_intentCount - first));
            c.IntentsSent = _intentCount;
            c.Connection.Send(new PlayOverviewMessage(rows, fresh));
        }
    }

    private void AddIntent(PlayIntentLine line)
    {
        _intents.Add(line);
        _intentCount++;
        int keep = Math.Max(10, Rules.IntentLines);
        if (_intents.Count > keep)
        {
            _intents.RemoveRange(0, _intents.Count - keep);
        }
    }

    private void ReadAiLog()
    {
        List<AiLogEntry> log = _game.AiLogEntries;
        if (_aiLogRead > log.Count)
        {
            _aiLogRead = 0; // the log dropped its older half
        }

        for (; _aiLogRead < log.Count; _aiLogRead++)
        {
            AiLogEntry e = log[_aiLogRead];
            if (_game.Civs[e.Civ].IsAi)
            {
                AddIntent(new PlayIntentLine(e.Tick, e.Civ, false, (int)e.Kind, e.Text));
            }
        }
    }

    // The table of civilizations.
    private List<PlayOverviewRow> Overview()
    {
        var rows = new List<PlayOverviewRow>(_game.Civs.Count);
        foreach (Civ c in _game.Civs)
        {
            int units = 0, soldiers = 0;
            foreach (int id in c.Units)
            {
                Unit u = _game.Units[id];
                if (_game.Content.Units[u.Kind].Army)
                {
                    units++;
                    soldiers += u.People;
                }
            }

            Settlement? capital = null;
            foreach (int sid in c.Settlements)
            {
                Settlement s = _game.Settlements[sid];
                if (capital is null || s.People > capital.People)
                {
                    capital = s;
                }
            }

            int percent = 0;
            if (c.Researching >= 0 && !c.Dead)
            {
                long total = Math.Max(1, _game.ResearchTicks(c, c.Researching));
                percent = (int)Math.Clamp(100 - ((c.ResearchEnd - _game.Ticks) * 100 / total), 0, 100);
            }

            var stock = new long[c.Stock.Length];
            for (int g = 0; g < stock.Length; g++)
            {
                stock[g] = c.Stock[g] / SimContent.One;
            }

            List<int> wars = [], alliances = [], trade = [];
            foreach (Civ o in _game.Civs)
            {
                if (o.Id == c.Id || o.Dead || c.Dead)
                {
                    continue;
                }

                if (_game.AtWar(c.Id, o.Id))
                {
                    wars.Add(o.Id);
                }

                if (_game.Has(c.Id, o.Id, TreatyKind.Alliance))
                {
                    alliances.Add(o.Id);
                }

                if (_game.Has(c.Id, o.Id, TreatyKind.Trade))
                {
                    trade.Add(o.Id);
                }
            }

            CivSim.Core.Map.HexCoord at = capital is null ? new CivSim.Core.Map.HexCoord(-1, -1) : _game.Geo.FromIndex(capital.Center);
            string? player = c.IsAi ? null : _clients.Find(x => x.Civ == c.Id && x.Connection.IsOpen)?.Name ?? _seatNames.GetValueOrDefault(c.Id);
            rows.Add(new PlayOverviewRow(c.Id, c.Name, player, c.Dead, c.People, c.Settlements.Count, c.Era, c.Researching, percent,
                units, soldiers, stock, _game.ScoreOf(c), [.. wars], [.. alliances], [.. trade], at.Col, at.Row));
        }

        return rows;
    }

    // A player's order that the game took: counted into the next summary of that civilization (3a).
    private void TallyOrder(int civ, PlayCommandMessage m)
    {
        if (!_clients.Exists(Spectating))
        {
            return;
        }

        if (!_tallies.TryGetValue(civ, out Tally? t))
        {
            t = new Tally();
            _tallies[civ] = t;
        }

        SimContent content = _game.Content;
        switch (m.Kind)
        {
            case PlayCommandKind.Build when (uint)m.A < (uint)content.Buildings.Count:
                t.Count(t.Builds, content.Buildings[m.A].Name);
                break;
            case PlayCommandKind.Train when (uint)m.B < (uint)content.Units.Count:
                t.Count(t.Trains, content.Units[m.B].Name);
                break;
            case PlayCommandKind.Move or PlayCommandKind.Formation:
                t.Count(t.Moves, Toward(civ, m.Col, m.Row));
                break;
            case PlayCommandKind.Attack or PlayCommandKind.AttackWall:
                t.Count(t.Attacks, Toward(civ, m.Col, m.Row));
                break;
            case PlayCommandKind.Found:
                t.Founds++;
                break;
            case PlayCommandKind.Research when (uint)m.A < (uint)content.Techs.Count:
                t.Research = content.Techs[m.A].Name;
                break;
            case PlayCommandKind.SendCaravan or PlayCommandKind.Route or PlayCommandKind.Barter:
                t.Trade++;
                break;
            case PlayCommandKind.DeclareWar when (uint)m.A < (uint)_game.Civs.Count:
                AddIntent(new PlayIntentLine(_game.Ticks, civ, true, PlayIntentLine.War, $"declares war on {_game.Civs[m.A].Name}"));
                break;
            case PlayCommandKind.ProposePeace when (uint)m.A < (uint)_game.Civs.Count:
                AddIntent(new PlayIntentLine(_game.Ticks, civ, true, PlayIntentLine.Peace, $"offers peace to {_game.Civs[m.A].Name}"));
                break;
            case PlayCommandKind.AnswerPeace when (uint)m.A < (uint)_game.Civs.Count:
                AddIntent(new PlayIntentLine(_game.Ticks, civ, true, PlayIntentLine.Peace, $"{(m.Flag ? "accepts" : "refuses")} peace with {_game.Civs[m.A].Name}"));
                break;
            case PlayCommandKind.ProposeTreaty or PlayCommandKind.AnswerTreaty or PlayCommandKind.CancelTreaty when (uint)m.A < (uint)_game.Civs.Count:
                string what = (TreatyKind)m.B == TreatyKind.Alliance ? "an alliance" : "a trade treaty";
                string verb = m.Kind == PlayCommandKind.ProposeTreaty ? "offers" : m.Kind == PlayCommandKind.CancelTreaty ? "ends" : m.Flag ? "accepts" : "refuses";
                AddIntent(new PlayIntentLine(_game.Ticks, civ, true, PlayIntentLine.Diplomacy, $"{verb} {what} with {_game.Civs[m.A].Name}"));
                break;
            default:
                break;
        }
    }

    // Where an order sends people: a settlement near the target hex (whose), or open land.
    private string Toward(int civ, int col, int row)
    {
        CivSim.Core.Map.HexCoord at = _game.Geo.Wrap(col, row);
        Settlement? best = null;
        int bestDist = 4;
        foreach (Civ c in _game.Civs)
        {
            foreach (int sid in c.Settlements)
            {
                Settlement s = _game.Settlements[sid];
                int d = _game.Geo.Distance(at, _game.Geo.FromIndex(s.Center));
                if (d < bestDist)
                {
                    best = s;
                    bestDist = d;
                }
            }
        }

        return best is null ? "open land" : best.Civ == civ ? $"our {best.Name}" : $"{best.Name} of {_game.Civs[best.Civ].Name}";
    }

    private void FlushTallies()
    {
        foreach ((int civ, Tally t) in _tallies)
        {
            var parts = new List<string>();
            int kind = PlayIntentLine.Building;
            if (t.Attacks.Count > 0)
            {
                parts.Add("attacks " + Join(t.Attacks));
                kind = PlayIntentLine.War;
            }

            if (t.Moves.Count > 0)
            {
                parts.Add("moves troops toward " + Join(t.Moves));
                kind = kind == PlayIntentLine.War ? kind : PlayIntentLine.Army;
            }

            if (t.Trains.Count > 0)
            {
                parts.Add("trains " + Join(t.Trains));
                kind = kind is PlayIntentLine.War ? kind : PlayIntentLine.Army;
            }

            if (t.Founds > 0)
            {
                parts.Add(t.Founds == 1 ? "founds a settlement" : $"founds {t.Founds} settlements");
                kind = kind is PlayIntentLine.War or PlayIntentLine.Army ? kind : PlayIntentLine.Expansion;
            }

            if (t.Builds.Count > 0)
            {
                parts.Add("builds " + Join(t.Builds));
            }

            if (t.Research is { } r)
            {
                parts.Add("researches " + r);
                kind = parts.Count == 1 ? PlayIntentLine.Research : kind;
            }

            if (t.Trade > 0)
            {
                parts.Add(t.Trade == 1 ? "sends goods" : $"sends goods {t.Trade} times");
                kind = parts.Count == 1 ? PlayIntentLine.Trade : kind;
            }

            if (parts.Count > 0)
            {
                AddIntent(new PlayIntentLine(_game.Ticks, civ, true, kind, string.Join("; ", parts)));
            }
        }

        _tallies.Clear();
    }

    private static string Join(SortedDictionary<string, int> counts) =>
        string.Join(", ", counts.Select(kv => kv.Value > 1 ? $"{kv.Key} ×{kv.Value}" : kv.Key));

    // A spectator picks whose eyes to watch through (−1: the whole map).
    private void Eye(PlayClient client, int civ)
    {
        int eye = civ >= 0 && civ < _game.Civs.Count ? civ : -1;
        if (client.Eye != eye)
        {
            client.Eye = eye;
            client.ViewSoon = true;
        }
    }

    private sealed class Tally
    {
        public SortedDictionary<string, int> Builds { get; } = new(StringComparer.Ordinal);

        public SortedDictionary<string, int> Trains { get; } = new(StringComparer.Ordinal);

        public SortedDictionary<string, int> Moves { get; } = new(StringComparer.Ordinal);

        public SortedDictionary<string, int> Attacks { get; } = new(StringComparer.Ordinal);

        public int Founds { get; set; }

        public string? Research { get; set; }

        public int Trade { get; set; }

        public void Count(SortedDictionary<string, int> into, string what) => into[what] = into.GetValueOrDefault(what) + 1;
    }
}
