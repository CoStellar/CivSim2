using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.State;
using CivSim.Headless;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;

namespace CivSim.Playtest;

/// <summary>
/// Playtest harness (not part of the game): a scripted "player" leads one civilization through the real server and protocol, with one
/// play style, and the run reports how the led civilization did minute by minute. Comparing styles on the same seed and civilization
/// answers "does the player's steering change the outcome?" (agency, ADR 0027).
/// Usage: CivSim.Playtest --seeds 1-10 --civs all --minutes 15 --styles ai,absent,... --out results.csv
/// </summary>
public static class Program
{
    private static readonly string[] AllStyles =
        ["ai", "absent", "absent_research", "absent_land", "absent_war", "tech_cheapest", "tech_dearest", "farm_rush", "settle_now", "warlord", "random"];

    /// <summary>Entry point.</summary>
    public static int Main(string[] args)
    {
        int seedFrom = 1, seedTo = 10, minutes = 15;
        string? civArg = "all";
        string[] styles = AllStyles;
        string outPath = "playtest.csv";
        string size = "small";
        string? transcript = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seeds":
                    string[] p = args[++i].Split('-');
                    seedFrom = int.Parse(p[0], CultureInfo.InvariantCulture);
                    seedTo = p.Length > 1 ? int.Parse(p[1], CultureInfo.InvariantCulture) : seedFrom;
                    break;
                case "--civs": civArg = args[++i]; break;
                case "--minutes": minutes = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--styles": styles = args[++i].Split(','); break;
                case "--out": outPath = args[++i]; break;
                case "--map-size": size = args[++i]; break;
                case "--transcript": transcript = args[++i]; break;
                default:
                    Console.Error.WriteLine($"unknown argument {args[i]}");
                    return 2;
            }
        }

        GameContent content = CivSim.Data.ContentLoader.Load(CivSim.Data.ContentLoader.DefaultDirectory);
        var jobs = new List<(int Seed, int Civ, string Style)>();
        for (int seed = seedFrom; seed <= seedTo; seed++)
        {
            int civCount = Settings(content, seed, size).CivilizationCount;
            IEnumerable<int> civs = civArg == "all" ? Enumerable.Range(0, civCount) : civArg!.Split(',').Select(int.Parse);
            foreach (int civ in civs)
            {
                foreach (string style in styles)
                {
                    jobs.Add((seed, civ, style));
                }
            }
        }

        var rows = new System.Collections.Concurrent.ConcurrentBag<string>();
        int done = 0;
        System.Threading.Tasks.Parallel.ForEach(jobs, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, job =>
        {
            foreach (string row in Play(content, job.Seed, job.Civ, job.Style, minutes, size, transcript is null ? null : $"{transcript}_s{job.Seed}_c{job.Civ}_{job.Style}.txt"))
            {
                rows.Add(row);
            }

            int n = System.Threading.Interlocked.Increment(ref done);
            if (n % 10 == 0)
            {
                Console.WriteLine($"{n}/{jobs.Count}");
            }
        });

        using var w = new StreamWriter(outPath);
        w.WriteLine(Row.Header);
        foreach (string r in rows.OrderBy(x => x, StringComparer.Ordinal))
        {
            w.WriteLine(r);
        }

        Console.WriteLine($"wrote {outPath}: {jobs.Count} games");
        return 0;
    }

    private static GameSettings Settings(GameContent content, int seed, string size) =>
        HeadlessOptions.Parse(["--seed", seed.ToString(CultureInfo.InvariantCulture), "--map-size", size], out _)!.ToSettings(content);

    private static IEnumerable<string> Play(GameContent content, int seed, int civId, string style, int minutes, string size, string? transcriptPath)
    {
        GameServer server = GameServer.CreateNew(Settings(content, seed, size), content);
        (InMemoryConnection client, InMemoryConnection serverSide) = InMemoryConnection.CreatePair();
        server.Accept(serverSide);
        client.Send(new HelloMessage(ProtocolInfo.Version, "bot", ClientRole.Player, CivId: civId));
        server.Pump();
        var bot = new Bot(content, server, client, civId, style, seed);
        using StreamWriter? tw = transcriptPath is null ? null : new StreamWriter(transcriptPath);
        bot.Transcript = tw;
        int ticksPerMinute = server.TicksPerSecond * 60;
        int perSecond = server.TicksPerSecond;
        var output = new List<string>();
        for (int tick = 1; tick <= minutes * ticksPerMinute; tick++)
        {
            bool second = tick % perSecond == 0;
            server.AdvanceTurn(sendViews: false);
            if (second)
            {
                server.SendViews();
                bot.Think(tick);
                server.Pump();
            }

            if (tick % ticksPerMinute == 0)
            {
                output.Add(Row.Of(seed, civId, style, tick / ticksPerMinute, server.State, content, bot));
            }
        }

        return output;
    }
}

/// <summary>One CSV row: the led civilization after a minute of play.</summary>
internal static class Row
{
    public const string Header =
        "seed,civ,style,minute,alive,people,settlements,villages,techs,era,warriors_lost,orders_sent,orders_rejected,ai_decisions_seen,wars,last_rejection";

    public static string Of(int seed, int civId, string style, int minute, GameState state, GameContent content, Bot bot)
    {
        CivilizationState civ = state.FindCivilization(new CivSim.Core.State.CivId(civId))!;
        long people = 0;
        int settlements = 0, villages = 0;
        int village = content.SettlementLevel("village").Id;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.Owner == civ.Id && s.IsActive)
            {
                settlements++;
                villages += s.Level >= village ? 1 : 0;
                foreach (int p in s.Population)
                {
                    people += p;
                }
            }
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"{seed},{civId},{style},{minute},{(civ.IsAlive ? 1 : 0)},{people},{settlements},{villages},{civ.KnownCount},{civ.Era},0,{bot.Sent},{bot.Rejected},{bot.AiDecisions},{bot.Wars},\"{bot.LastRejection.Replace('"', '\'')}\"");
    }
}

/// <summary>The scripted player. It decides once a second of game time from its view, like a person watching the screen.</summary>
internal sealed class Bot
{
    private readonly GameContent _content;
    private readonly GameServer _server;
    private readonly InMemoryConnection _client;
    private readonly int _civ;
    private readonly string _style;
    private readonly CivSim.Core.Random.DeterministicRandom _rng;
    private long _seq;
    private GameViewMessage? _view;
    private bool _leversSet;
    private readonly HashSet<int> _atWar = [];
    private long _lastCampaignTick = -100000;

    // Food first: what a person new to the game would pick to "settle down and grow" (agriculture as soon as possible).
    private static readonly string[] FarmOrder = ["fire", "flint_knapping", "agriculture", "pottery", "irrigation", "animal_husbandry", "stonemasonry", "barter"];

    public Bot(GameContent content, GameServer server, InMemoryConnection client, int civ, string style, int seed)
    {
        _content = content;
        _server = server;
        _client = client;
        _civ = civ;
        _style = style;
        _rng = new CivSim.Core.Random.DeterministicRandom((ulong)seed * 7919UL + (ulong)civ);
    }

    public StreamWriter? Transcript { get; set; }

    private long _lastDecisionTurn = -1;
    private readonly HashSet<string> _seenAtLast = [];

    private static string Stamp(long turn) => $"[{turn / 600:00}:{turn % 600 / 10:00}]";

    public int Sent { get; private set; }

    public int Rejected { get; private set; }

    public int AiDecisions { get; private set; }

    public int Wars => Declarations;

    public int Declarations { get; private set; }

    public string LastRejection { get; private set; } = string.Empty;

    private void Send(ICommandDto dto)
    {
        _client.Send(new SubmitCommandMessage(_civ, ++_seq, dto));
        Sent++;
    }

    private void Take(params int[] levers)
    {
        foreach (int l in levers)
        {
            Send(new SetLeverDto(l, true));
        }
    }

    public void Think(long tick)
    {
        while (_client.TryReceive(out IProtocolMessage? m))
        {
            switch (m)
            {
                case GameViewMessage v:
                    _view = v;
                    if (Transcript is not null)
                    {
                        foreach (ChronicleEntry e in v.Chronicle ?? [])
                        {
                            Transcript.WriteLine($"{Stamp(e.Turn)} CHRONICLE {e.Text}");
                        }

                        foreach (AiDecisionView d in v.AiDecisions ?? [])
                        {
                            if (d.Turn > _lastDecisionTurn || (d.Turn == _lastDecisionTurn && _seenAtLast.Add(d.Text)))
                            {
                                if (d.Turn > _lastDecisionTurn)
                                {
                                    _lastDecisionTurn = d.Turn;
                                    _seenAtLast.Clear();
                                    _seenAtLast.Add(d.Text);
                                }

                                Transcript.WriteLine($"{Stamp(d.Turn)} AI(lever {d.Lever}) {d.Text}");
                            }
                        }
                    }

                    break;
                case CommandRejectedMessage r:
                    Rejected++;
                    Transcript?.WriteLine($"REJECTED {r.Reason}");
                    if (r.Reason.StartsWith("We are not at war", StringComparison.Ordinal))
                    {
                        _atWar.Clear(); // the war ended without us (white peace): declare again
                    }
                    LastRejection = r.Reason;
                    break;
            }
        }

        if (_view is null)
        {
            return;
        }

        AiDecisions = Math.Max(AiDecisions, _view.AiDecisions?.Count ?? 0);
        if (!_leversSet)
        {
            _leversSet = true;
            switch (_style)
            {
                case "absent": Take(1, 2, 4, 8, 16); break;
                case "absent_research": Take(2); break;
                case "absent_land": Take(1); break;
                case "absent_war": Take(8); break;
                case "tech_cheapest" or "tech_dearest" or "farm_rush": Take(2); break;
                case "settle_now": Take(1, 2); break;
                case "warlord": Take(8); break;
                case "random": Take(1, 2, 4, 8, 16); break;
            }
        }

        switch (_style)
        {
            case "tech_cheapest": Research(o => o.OrderBy(x => x.Cost).First()); break;
            case "tech_dearest": Research(o => o.OrderByDescending(x => x.Cost).First()); break;
            case "farm_rush" or "settle_now": Research(FarmPick); break;
            case "warlord": Warlord(tick); break;
            case "random": RandomPlay(); break;
        }

        if (_style == "settle_now")
        {
            bool farming = _server.State.FindCivilization(new CivSim.Core.State.CivId(_civ))!.Knows(_content.Technology("agriculture").Id);
            foreach (SettlementView s in Mine())
            {
                if (farming && s.Level == 0 && tick % 50 == 0)
                {
                    Send(new SettleDto(s.Id));
                }
            }
        }
    }

    private IEnumerable<SettlementView> Mine() => (_view?.Settlements ?? []).Where(s => s.Owner == _civ && s.Status == 0);

    private void Research(Func<IReadOnlyList<TechnologyOption>, TechnologyOption> pick)
    {
        ResearchView? r = _view!.Research;
        if (r is null || r.Options.Count == 0 || !string.IsNullOrEmpty(r.Researching))
        {
            return;
        }

        Send(new SetResearchDto(pick(r.Options).Key));
    }

    private static TechnologyOption FarmPick(IReadOnlyList<TechnologyOption> options)
    {
        foreach (string k in FarmOrder)
        {
            TechnologyOption? o = options.FirstOrDefault(x => x.Key == k);
            if (o is not null)
            {
                return o;
            }
        }

        return options.OrderBy(x => x.Cost).First();
    }

    private void Warlord(long tick)
    {
        CivilizationState me = _server.State.FindCivilization(new CivSim.Core.State.CivId(_civ))!;
        foreach (CivSim.Core.Diplomacy.Relation rel in me.Relations)
        {
            if (rel.HasMet && _atWar.Add(rel.Other.Value))
            {
                Declarations++;
                Send(new DeclareWarDto(rel.Other.Value, Goal: 1)); // loot, like the viewer's War button (the DTO default is revenge)
            }
        }

        if (_atWar.Count == 0 || tick - _lastCampaignTick < 150)
        {
            return;
        }

        // The biggest own settlement sends half its people at the nearest enemy settlement it can see.
        SettlementView? home = Mine().OrderByDescending(s => s.Population?.Sum() ?? 0).FirstOrDefault();
        if (home is null)
        {
            return;
        }

        SettlementView? target = (_view!.Settlements ?? [])
            .Where(s => _atWar.Contains(s.Owner) && s.Status == 0)
            .OrderBy(s => Math.Abs(s.Col - home.Col) + Math.Abs(s.Row - home.Row))
            .FirstOrDefault();
        if (target is not null)
        {
            Send(new LaunchCampaignDto(home.Id, target.Id, Math.Max(1, (home.Population?.Sum() ?? 0) / 5)));
            _lastCampaignTick = tick;
        }
    }

    private void RandomPlay()
    {
        if (_rng.NextInt(10) != 0)
        {
            return; // about one order every 10 seconds
        }

        var mine = Mine().ToList();
        switch (_rng.NextInt(4))
        {
            case 0:
                if (_view!.Research is { Options.Count: > 0 } r)
                {
                    Send(new SetResearchDto(r.Options[_rng.NextInt((int)r.Options.Count)].Key));
                }

                break;
            case 1 when mine.Count > 0:
                SettlementView s = mine[_rng.NextInt((int)mine.Count)];
                Send(new SplitSettlementDto(s.Id, s.Col + _rng.NextInt(9) - 4, Math.Clamp(s.Row + _rng.NextInt(9) - 4, 0, 59), 30));
                break;
            case 2 when mine.Count > 0:
                SettlementView m = mine[_rng.NextInt((int)mine.Count)];
                Send(new MoveSettlementDto(m.Id, m.Col + _rng.NextInt(5) - 2, Math.Clamp(m.Row + _rng.NextInt(5) - 2, 0, 59)));
                break;
            case 3 when mine.Count > 0:
                Send(new SettleDto(mine[_rng.NextInt((int)mine.Count)].Id));
                break;
        }
    }
}
