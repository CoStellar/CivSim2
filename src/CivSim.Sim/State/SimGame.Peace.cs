using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>
/// Terms of a peace offer (ADR 0030): goods and amounts (hundredths), maybe one settlement, and the length of peace (an index
/// into <see cref="PeaceRules.Minutes"/>). <paramref name="WeGive"/>: the proposer gives them; otherwise it demands them.
/// </summary>
public sealed record PeaceTerms(bool WeGive, int Length, int Settlement, IReadOnlyList<(int Good, long Amount)> Goods)
{
    /// <summary>A white peace: nobody gives anything.</summary>
    public static PeaceTerms White(int length = 0) => new(false, length, -1, []);

    /// <summary>True when nobody gives anything.</summary>
    public bool IsWhite => Settlement < 0 && Goods.Count == 0;
}

public sealed partial class SimGame
{
    private int[]? _weary;
    private bool[]? _aggressor;
    private int[]? _truceBreak;

    private int[] Weary() => _weary ??= new int[Civs.Count * Civs.Count];

    private bool[] Aggressors() => _aggressor ??= new bool[Civs.Count * Civs.Count];

    private int[] TruceBreaks() => _truceBreak ??= new int[Civs.Count * Civs.Count];

    /// <summary>War weariness (0-100) of <paramref name="a"/> in its war with <paramref name="b"/> (ADR 0030, point 7).</summary>
    public int Weariness(int a, int b) => Weary()[(a * Civs.Count) + b] / 100;

    /// <summary>The highest war weariness of a civilization over the civilizations it fights or fought lately (0-100).</summary>
    public int Weariness(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int most = 0;
        for (int o = 0; o < Civs.Count; o++)
        {
            most = Math.Max(most, Weariness(civ.Id, o));
        }

        return most;
    }

    /// <summary>True when <paramref name="a"/> declared the war it fights with <paramref name="b"/> (7.3A).</summary>
    public bool Aggressor(int a, int b) => Aggressors()[(a * Civs.Count) + b];

    /// <summary>7.2A: percent less work in the civilization's workshops from its war weariness.</summary>
    public int WarWorkLoss(Civ civ)
    {
        WearinessRules r = Content.Weariness;
        int w = Weariness(civ);
        return w <= r.WorkFrom || r.WorkFrom >= 100 ? 0 : (w - r.WorkFrom) * r.WorkMaxLossPercent / (100 - r.WorkFrom);
    }

    private void MarkAggressor(int attacker, int victim)
    {
        Aggressors()[(attacker * Civs.Count) + victim] = true;
        Aggressors()[(victim * Civs.Count) + attacker] = false;
    }

    // Weariness grows only in a war; the points are whole points of the data, kept in hundredths.
    private void AddWeariness(int civ, int enemy, long hundredths)
    {
        if (civ < 0 || enemy < 0 || civ == enemy || !AtWar(civ, enemy) || hundredths <= 0)
        {
            return;
        }

        int i = (civ * Civs.Count) + enemy;
        Weary()[i] = (int)Math.Min(10_000, Weary()[i] + hundredths);
    }

    // Losses: points per percent of the people the enemy killed.
    private void AddLoss(int civ, int enemy, int dead)
    {
        if (dead <= 0 || civ < 0)
        {
            return;
        }

        long people = Math.Max(1, Civs[civ].People + dead);
        AddWeariness(civ, enemy, dead * 100L * 100 * Content.Weariness.PerLostPercent / people);
    }

    // Once a minute: the war goes on (time, hunger, sieges), and weariness falls in peace. Workshops slow from workFrom up.
    private void WearinessStep()
    {
        WearinessRules r = Content.Weariness;
        int n = Civs.Count;
        foreach (Civ a in Civs)
        {
            if (a.Dead)
            {
                continue;
            }

            bool hungry = false;
            int besieged = 0;
            foreach (int sid in a.Settlements)
            {
                hungry |= Settlements[sid].Hungry;
                besieged += Settlements[sid].Besieged ? 1 : 0;
            }

            for (int b = 0; b < n; b++)
            {
                if (b == a.Id)
                {
                    continue;
                }

                int i = (a.Id * n) + b;
                if (AtWar(a.Id, b))
                {
                    long add = r.PerMinute + (hungry ? r.HungerPerMinute : 0) + ((long)besieged * r.BesiegedPerMinute);
                    AddWeariness(a.Id, b, add * 100);
                }
                else if (Weary()[i] > 0)
                {
                    Weary()[i] = Math.Max(0, Weary()[i] - (r.PeaceFallPerMinute * 100));
                }
            }

            int loss = WarWorkLoss(a);
            if (loss != a.WarWorkLoss)
            {
                a.WarWorkLoss = loss;
                a.Dirty = true; // the workshops' rates are worked out again
            }
        }
    }

    /// <summary>Worth of a settlement as a term of peace (5A): food per person plus a share of its finished buildings' price.</summary>
    public long SettlementWorth(Settlement s)
    {
        ArgumentNullException.ThrowIfNull(s);
        PeaceRules r = Content.Peace;
        long worth = (long)s.People * r.SettlementWorthPerPerson * SimContent.One;
        foreach (int id in Civs[s.Civ].Buildings)
        {
            Building b = Buildings[id];
            if (b.Settlement != s.Id || b.State != BuildingState.Active)
            {
                continue;
            }

            long[] price = Content.Buildings[b.Type].Price;
            for (int g = 0; g < price.Length; g++)
            {
                worth += Worth(g, price[g]) * r.SettlementBuildingPercent / 100;
            }
        }

        return worth;
    }

    /// <summary>What a people has that peace terms can take: its goods and its settlements but the first.</summary>
    public long Wealth(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        long worth = 0;
        for (int g = 0; g < civ.Stock.Length; g++)
        {
            worth += Worth(g, civ.Stock[g]);
        }

        for (int k = 1; k < civ.Settlements.Count; k++)
        {
            worth += SettlementWorth(Settlements[civ.Settlements[k]]);
        }

        return worth;
    }

    /// <summary>
    /// How much the course of the war favours <paramref name="a"/> over <paramref name="b"/>, percent of the loser's wealth
    /// (5A, 7.1A): war score lead and weariness lead; positive: <paramref name="a"/> may demand.
    /// </summary>
    public int Favour(int a, int b)
    {
        PeaceRules r = Content.Peace;
        int score = Score(a, b) - Score(b, a);
        int weary = Weariness(b, a) - Weariness(a, b);
        return Math.Clamp((score / r.ScorePerPercent) + (weary / r.WearinessPerPercent), -r.MaxPercent, r.MaxPercent);
    }

    /// <summary>Worth <paramref name="b"/> should give <paramref name="a"/> by the course of the war (negative: a gives b).</summary>
    public long FairWorth(int a, int b)
    {
        int pct = Favour(a, b);
        if (pct == 0)
        {
            return 0;
        }

        Civ winner = Civs[pct > 0 ? a : b];
        Civ loser = Civs[pct > 0 ? b : a];
        long worth = Wealth(loser) * Math.Abs(pct) / 100;
        if (Aggressor(loser.Id, winner.Id))
        {
            worth = worth * (100 + Content.Peace.AggressorExtraPercent) / 100; // 7.3A: the one who started it pays for it
        }

        return pct > 0 ? worth : -worth;
    }

    /// <summary>Worth of what the terms move from the giver to the other side.</summary>
    public long TermsWorth(PeaceTerms t)
    {
        ArgumentNullException.ThrowIfNull(t);
        long worth = 0;
        foreach ((int good, long amount) in t.Goods)
        {
            worth += Worth(good, amount);
        }

        if (t.Settlement >= 0)
        {
            worth += SettlementWorth(Settlements[t.Settlement]);
        }

        return worth;
    }

    /// <summary>True when an army of <paramref name="civ"/> besieges the settlement now.</summary>
    public bool BesiegedBy(Settlement s, int civ)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!s.Besieged)
        {
            return false;
        }

        foreach (int id in Civs[civ].Units)
        {
            Unit u = Units[id];
            UnitType t = Content.Units[u.Kind];
            if (t.Army && u.People > 0 && u.State is not (UnitState.Gone or UnitState.Training) && Distance(u.Cell, s.Center) <= Content.Siege.BlockadeRange)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the civilization sees the settlement or remembers it where it stands.</summary>
    public bool KnowsSettlement(Civ civ, Settlement s)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(s);
        return Visible(civ, s.Center) || (civ.SeenSettlements.TryGetValue(s.Center, out SettlementMemory? seen) && seen.Settlement == s.Id);
    }

    // 2B: how far the settlement lies from the land of a civilization (hexes; int.MaxValue when it has none).
    private int DistanceToLand(Settlement s, Civ civ)
    {
        int best = int.MaxValue;
        foreach (int sid in civ.Settlements)
        {
            foreach (int cell in Settlements[sid].Cells)
            {
                best = Math.Min(best, Distance(cell, s.Center));
            }
        }

        return best;
    }

    /// <summary>Why the settlement cannot change hands in a peace to <paramref name="taker"/> (null: it can).</summary>
    public string? SettlementTermRefused(Civ taker, Settlement s)
    {
        ArgumentNullException.ThrowIfNull(taker);
        ArgumentNullException.ThrowIfNull(s);
        Civ owner = Civs[s.Civ];
        if (owner.Settlements.Count == 0 || owner.Settlements[0] == s.Id)
        {
            return $"{s.Name} is the first settlement of {owner.Name}: it is not given away in a peace.";
        }

        if (!BesiegedBy(s, taker.Id) && DistanceToLand(s, taker) > Content.Peace.SettlementHexes)
        {
            return $"{s.Name} is neither besieged by our army nor within {Content.Peace.SettlementHexes} hexes of our land.";
        }

        return null;
    }

    // Checks the terms of an offer from civ to other; null when they can be offered.
    private string? TermsRefused(Civ civ, Civ other, PeaceTerms t)
    {
        if ((uint)t.Length >= (uint)Content.Peace.Minutes.Length)
        {
            return "Choose a length of peace.";
        }

        Civ giver = t.WeGive ? civ : other;
        Civ taker = t.WeGive ? other : civ;
        if (!t.WeGive && !t.IsWhite)
        {
            if (Favour(civ.Id, other.Id) <= 0)
            {
                return "Only the side the war favours may demand: offer a white peace, or give something for peace.";
            }

            long left = WarStart(civ.Id, other.Id) + Content.War.TributeAfterTicks - Ticks;
            if (left > 0)
            {
                return $"Terms can be demanded only after {Content.War.TributeAfterTicks / SimContent.TicksPerSecond} s of war ({(left + SimContent.TicksPerSecond - 1) / SimContent.TicksPerSecond} s more); a white peace is possible now.";
            }
        }

        var seen = new HashSet<int>();
        foreach ((int good, long amount) in t.Goods)
        {
            if ((uint)good >= (uint)Content.Goods.Count || amount <= 0 || !seen.Add(good))
            {
                return "Name each good once, with an amount.";
            }

            if (!KnowsGood(civ, good))
            {
                return "We do not know that good.";
            }

            if (giver.Stock[good] < amount)
            {
                return $"{(giver == civ ? "We have" : $"{giver.Name} have")} only {giver.Stock[good] / SimContent.One} {GoodText(good)}.";
            }

            if (!t.WeGive && good == Content.Food && amount * 100 > giver.Stock[good] * Content.War.TributeFoodMaxPercent)
            {
                return $"At most {Content.War.TributeFoodMaxPercent}% of their food can be demanded ({giver.Stock[good] * Content.War.TributeFoodMaxPercent / 100 / SimContent.One}).";
            }
        }

        if (t.Settlement >= 0)
        {
            if ((uint)t.Settlement >= (uint)Settlements.Count || Settlements[t.Settlement].Civ != giver.Id || Settlements[t.Settlement].People <= 0)
            {
                return "That settlement is not theirs to give.";
            }

            if (!t.WeGive && !KnowsSettlement(civ, Settlements[t.Settlement]))
            {
                return "We know no such settlement of theirs.";
            }

            if (SettlementTermRefused(taker, Settlements[t.Settlement]) is string why)
            {
                return why;
            }
        }

        return null;
    }

    /// <summary>
    /// 4A: terms the course of the war suggests for <paramref name="a"/> to offer <paramref name="b"/>: when the war favours a,
    /// a settlement it may take and goods it lacks most, worth what the loser should give; otherwise a white peace.
    /// </summary>
    public PeaceTerms SuggestTerms(int a, int b, int length = 1)
    {
        long fair = FairWorth(a, b);
        if (fair <= 0 || Ticks - WarStart(a, b) < Content.War.TributeAfterTicks)
        {
            return PeaceTerms.White(length);
        }

        Civ us = Civs[a];
        Civ them = Civs[b];
        int settlement = -1;
        long best = 0;
        for (int k = 1; k < them.Settlements.Count; k++)
        {
            Settlement s = Settlements[them.Settlements[k]];
            long w = SettlementWorth(s);
            if (w <= fair && w > best && s.People > 0 && KnowsSettlement(us, s) && SettlementTermRefused(us, s) is null)
            {
                best = w;
                settlement = s.Id;
            }
        }

        long left = fair - best;
        return new PeaceTerms(false, length, settlement, PickGoods(us, them, left, demand: true));
    }

    /// <summary>Goods worth <paramref name="worth"/> that <paramref name="giver"/> gives (for a gift for peace).</summary>
    public PeaceTerms GiftTerms(Civ giver, Civ taker, long worth, int length)
    {
        ArgumentNullException.ThrowIfNull(giver);
        ArgumentNullException.ThrowIfNull(taker);
        return new PeaceTerms(true, length, -1, PickGoods(taker, giver, worth, demand: false));
    }

    // Goods worth about `worth` from the payer's store: when demanding, those the taker has least of first (it takes what it
    // lacks); when giving, those the payer has most of. At most half of each good, food within the tribute cap, four goods.
    private List<(int, long)> PickGoods(Civ taker, Civ payer, long worth, bool demand)
    {
        var picked = new List<(int, long)>();
        if (worth <= 0)
        {
            return picked;
        }

        var order = new List<int>();
        for (int g = 0; g < Content.Goods.Count; g++)
        {
            if (payer.Stock[g] >= SimContent.One && Content.Goods[g].WorthPercent > 0 && KnowsGood(demand ? taker : payer, g))
            {
                order.Add(g);
            }
        }

        order.Sort((x, y) =>
        {
            long kx = demand ? Worth(x, taker.Stock[x]) - Worth(x, payer.Stock[x]) : -Worth(x, payer.Stock[x]);
            long ky = demand ? Worth(y, taker.Stock[y]) - Worth(y, payer.Stock[y]) : -Worth(y, payer.Stock[y]);
            return kx != ky ? kx.CompareTo(ky) : x.CompareTo(y);
        });

        foreach (int g in order)
        {
            if (worth <= 0 || picked.Count >= 4)
            {
                break;
            }

            long most = g == Content.Food ? payer.Stock[g] * Content.War.TributeFoodMaxPercent / 100 : payer.Stock[g] / 2;
            long per = Worth(g, SimContent.One);
            if (per <= 0)
            {
                continue;
            }

            long units = Math.Min(most / SimContent.One, demand ? worth / per : (worth + per - 1) / per); // a demand stays within the worth
            if (units <= 0)
            {
                continue;
            }

            picked.Add((g, units * SimContent.One));
            worth -= units * per;
        }

        return picked;
    }

    /// <summary>Words for terms, from the point of view of <paramref name="viewer"/>.</summary>
    public string TermsText(PeaceTerms t, int proposer, int viewer)
    {
        ArgumentNullException.ThrowIfNull(t);
        int minutes = Content.Peace.Minutes[Math.Clamp(t.Length, 0, Content.Peace.Minutes.Length - 1)];
        if (t.IsWhite)
        {
            return $"peace for {minutes} min, nobody gives anything";
        }

        var parts = new List<string>();
        foreach ((int good, long amount) in t.Goods)
        {
            parts.Add($"{amount / SimContent.One} {GoodText(good)}");
        }

        if (t.Settlement >= 0)
        {
            parts.Add(Settlements[t.Settlement].Name);
        }

        bool proposerGives = t.WeGive;
        string who = (proposerGives ? proposer == viewer : proposer != viewer) ? "we give" : "they give";
        return $"peace for {minutes} min; {who} {string.Join(", ", parts)}";
    }

    // Peace (ADR 0030): goods by caravan, the settlement at once, a peace of the chosen length.
    private void MakePeace(Civ proposer, Civ answerer, PeaceTerms t)
    {
        Civ giver = t.WeGive ? proposer : answerer;
        Civ taker = t.WeGive ? answerer : proposer;
        if (t.Goods.Count > 0 && giver.Settlements.Count > 0 && taker.Settlements.Count > 0)
        {
            (Settlement from, Settlement to) = ClosestPair(giver, taker);
            var load = new long[Content.Goods.Count];
            foreach ((int g, long amount) in t.Goods)
            {
                load[g] = Math.Min(amount, giver.Stock[g]);
                TakeFromCiv(giver, from, g, load[g]);
                load[g] = Math.Min(load[g], from.Stock[g]);
            }

            Unit c = NewCarrier(giver, Content.Caravans, from, NearestStore(from, to.Center), load, 100);
            c.Job = CarrierJob.ToSettlement;
            c.Target = to.Id;
            Walk(c, NearestStore(to, from.Center));
            SyncTotals(giver);
        }

        bool settled = t.Settlement >= 0 && Settlements[t.Settlement].Civ == giver.Id && SettlementTermRefused(taker, Settlements[t.Settlement]) is null;
        SetWar(proposer.Id, answerer.Id, false);
        PeaceMade(proposer.Id, answerer.Id);
        if (t.IsWhite)
        {
            // P5 (Patryk, 2026-11-24): after a white peace no new war between the two for a while.
            long until = Ticks + ((long)Content.Peace.WhiteTruceMinutes * SimContent.TicksPerMinute);
            NoWar()[(proposer.Id * Civs.Count) + answerer.Id] = until;
            NoWar()[(answerer.Id * Civs.Count) + proposer.Id] = until;
        }

        int length = Math.Clamp(t.Length, 0, Content.Peace.Minutes.Length - 1);
        long truce = Ticks + ((long)Content.Peace.Minutes[length] * SimContent.TicksPerMinute);
        int n = Civs.Count;
        Truces()[(proposer.Id * n) + answerer.Id] = truce;
        Truces()[(answerer.Id * n) + proposer.Id] = truce;
        int breakPct = length < Content.Peace.BreakPercent.Length ? Content.Peace.BreakPercent[length] : 100;
        TruceBreaks()[(proposer.Id * n) + answerer.Id] = breakPct;
        TruceBreaks()[(answerer.Id * n) + proposer.Id] = breakPct;
        foreach (Unit u in Units)
        {
            if ((u.Civ == proposer.Id && (u.TargetUnit >= 0 && Units[u.TargetUnit].Civ == answerer.Id || u.TargetSettlement >= 0 && Settlements[u.TargetSettlement].Civ == answerer.Id))
                || (u.Civ == answerer.Id && (u.TargetUnit >= 0 && Units[u.TargetUnit].Civ == proposer.Id || u.TargetSettlement >= 0 && Settlements[u.TargetSettlement].Civ == proposer.Id)))
            {
                u.TargetUnit = -1;
                u.TargetSettlement = -1;
            }
        }

        PeacesMade++;
        PeaceWorthGiven += TermsWorth(t);
        NewsAbout(proposer.Id, answerer.Id, $"{proposer.Name} and {answerer.Name} made peace for {Content.Peace.Minutes[length]} minutes"
            + (t.IsWhite ? "." : $"; {giver.Name} give {string.Join(", ", t.Goods.Select(x => $"{x.Amount / SimContent.One} {GoodText(x.Good)}").Concat(settled ? [Settlements[t.Settlement].Name] : []))}."));
        if (settled)
        {
            SettlementsByPeace++;
            Capture(taker, Settlements[t.Settlement], byPeace: true);
        }
    }

    /// <summary>Peaces made in this game (for --sim-report).</summary>
    public int PeacesMade { get; private set; }

    /// <summary>Worth that changed hands in peaces (for --sim-report).</summary>
    public long PeaceWorthGiven { get; private set; }

    /// <summary>Settlements that changed hands in peaces (for --sim-report).</summary>
    public int SettlementsByPeace { get; private set; }

    // Tests: the enemy killed this many of the victim's people (score and weariness, as in a battle).
    internal void KillForTest(Civ victim, Civ by, int dead)
    {
        Scores()[(by.Id * Civs.Count) + victim.Id] += dead;
        AddLoss(victim.Id, by.Id, dead);
    }

    // Tests: sets a weariness directly and works the workshops' loss out again.
    internal void WearyForTest(Civ civ, Civ enemy, int points)
    {
        Weary()[(civ.Id * Civs.Count) + enemy.Id] = points * 100;
        civ.WarWorkLoss = WarWorkLoss(civ);
    }

    // Tests: the civilization's totals after its settlements' stores were changed.
    internal void SyncForTest(Civ civ) => SyncTotals(civ);

    // Lifts the white-peace truce between two civilizations (tests that declare war again at once).
    internal void EndWhiteTruceForTest(int a, int b)
    {
        NoWar()[(a * Civs.Count) + b] = 0;
        NoWar()[(b * Civs.Count) + a] = 0;
    }

    private long[]? _noWarUntil;

    private long[] NoWar() => _noWarUntil ??= new long[Civs.Count * Civs.Count];

    /// <summary>The tick until which <paramref name="a"/> may not start a war on <paramref name="b"/> after a white peace (P5).</summary>
    public long NoWarUntil(int a, int b) => a == b ? 0 : NoWar()[(a * Civs.Count) + b];

    private void HashPeace(Action<long> add)
    {
        foreach (long n in NoWar())
        {
            add(n);
        }

        foreach (int w in Weary())
        {
            add(w);
        }

        foreach (bool x in Aggressors())
        {
            add(x ? 1 : 0);
        }

        foreach (int b in TruceBreaks())
        {
            add(b);
        }
    }
}
