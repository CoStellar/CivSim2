using System;
using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Perception;
using CivSim.Core.Population;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Episodes;

/// <summary>Warriors one settlement sends to a campaign that leaves from another settlement (stage 4e PR 3).</summary>
/// <param name="Settlement">The settlement that sends them.</param>
/// <param name="Warriors">How many it sends.</param>
public readonly record struct CampaignMuster(SettlementId Settlement, int Warriors);

/// <summary>
/// The rules of a war campaign (GDD 9.3; stage 4e PR 2–3, ADR 0022): who may march where, taking and giving back
/// warriors, arms, food, and the strength in a fight. Battles in rounds come in PR 4.
/// </summary>
public static class Campaigns
{
    /// <summary>
    /// Why <paramref name="civ"/> may not send <paramref name="warriors"/> from <paramref name="home"/> and the
    /// <paramref name="others"/> against <paramref name="target"/>, or null.
    /// </summary>
    public static string? CannotLaunch(GameContent content, GameState state, CivId civ, int home, int target, int warriors, IReadOnlyList<CampaignMuster>? others = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        SettlementState? h = state.FindSettlement(new SettlementId(home));
        SettlementState? t = state.FindSettlement(new SettlementId(target));
        CivilizationState? c = state.FindCivilization(civ);
        if (c is not { IsAlive: true } || h is not { IsActive: true } || h.Owner != civ)
        {
            return "The campaign needs a lived-in settlement of ours.";
        }

        if (t is not { IsActive: true } || t.Owner == civ || t.World != h.World || h.World.Value != 0)
        {
            return "The target must be a lived-in settlement of another civilization.";
        }

        if (!state.Treaties.AtWar(civ, t.Owner))
        {
            return "We are not at war with them.";
        }

        if (state.Treaties.Find(civ, t.Owner, TreatyKind.War) is { } war && !war.ReadyFor(civ, state.Turn))
        {
            return "Our people are not ready for war yet.";
        }

        WorldState world = state.Worlds[0];
        if (!MapMemory.Knows(content, state, c, world.Geometry.Index(t.Cell)))
        {
            return "We do not know where that settlement is.";
        }

        if (state.Episodes.OutFrom(civ, home))
        {
            return "Warriors of that settlement are already out.";
        }

        if (Rests(state, h))
        {
            return "The warriors of that settlement still rest from their last campaign.";
        }

        if (warriors < 1 || warriors > Sendable(content, h))
        {
            return "Not enough warriors stay home.";
        }

        int total = warriors;
        for (int i = 0; i < (others?.Count ?? 0); i++)
        {
            CampaignMuster m = others![i];
            SettlementState? s = state.FindSettlement(m.Settlement);
            if (s is not { IsActive: true } || s.Owner != civ || s.World != h.World || m.Settlement.Value == home)
            {
                return "Warriors can only join from other settlements of ours.";
            }

            for (int j = 0; j < i; j++)
            {
                if (others[j].Settlement == m.Settlement)
                {
                    return "A settlement is listed twice.";
                }
            }

            if (world.Geometry.Distance(s.Cell, h.Cell) > content.War.MusterCells)
            {
                return "That settlement is too far to join.";
            }

            if (state.Episodes.OutFrom(civ, m.Settlement.Value))
            {
                return "Warriors of that settlement are already out.";
            }

            if (Rests(state, s))
            {
                return "The warriors of that settlement still rest from their last campaign.";
            }

            if (m.Warriors < 1 || m.Warriors > Sendable(content, s))
            {
                return "Not enough warriors stay home.";
            }

            total += m.Warriors;
        }

        if (total < content.War.MinWarriors)
        {
            return "Too few warriors.";
        }

        if (total > Era(content, c).ArmyLimit)
        {
            return "More warriors than one leader can lead.";
        }

        return MapMemory.Path(content, state, c, world, world.Geometry.Index(h.Cell), world.Geometry.Index(t.Cell)) is null
            ? "We know no way there."
            : null;
    }

    /// <summary>True while the warriors of <paramref name="s"/> rest after a campaign (stage 6 PR 3b, war.json restMonths).</summary>
    public static bool Rests(GameState state, SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(s);
        return s.RestUntilMonth > state.ActionMonth;
    }

    /// <summary>The kind <paramref name="civ"/>'s armies march as: the last army kind of units.json whose technology it knows.</summary>
    public static Units.UnitKindDefinition ArmyKind(GameContent content, CivilizationState? civ)
    {
        ArgumentNullException.ThrowIfNull(content);
        return civ is null ? content.Units.First(Units.UnitRole.Army) : content.Units.Best(Units.UnitRole.Army, civ.Knows);
    }

    /// <summary>The most warriors <paramref name="s"/> may send (a third stays home: decision of 2026-11-07).</summary>
    public static int Sendable(GameContent content, SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(content);
        return SettlementAssets.Warriors(content, s) * content.War.SendPercent / 100;
    }

    /// <summary>The era rules of <paramref name="civ"/> (army limit, warriors per cell).</summary>
    public static EraDefinition Era(GameContent content, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(civ);
        return content.Eras[Math.Clamp(civ.Era, 0, content.Eras.Count - 1)];
    }

    /// <summary>Cells an army of <paramref name="warriors"/> of <paramref name="civ"/> stands on (a front when more than one).</summary>
    public static int FrontCells(GameContent content, CivilizationState civ, int warriors) =>
        Math.Max(1, (int)IntMath.MulDiv(Math.Max(0, warriors), 1, Era(content, civ).TroopsPerCell, Rounding.Ceiling));

    /// <summary>Takes up to <paramref name="count"/> warriors out of the fighting strata of <paramref name="s"/>; returns how many.</summary>
    public static int TakeWarriors(GameContent content, SettlementState s, int count) => Take(content, s, count, fighters: true);

    /// <summary>Gives <paramref name="count"/> warriors back to the first fighting stratum of <paramref name="s"/>.</summary>
    public static void GiveWarriors(GameContent content, SettlementState s, int count) => Give(content, s, count, fighters: true);

    /// <summary>
    /// Strength of <paramref name="warriors"/> of <paramref name="s"/> with the best arms <paramref name="civ"/> knows and
    /// the settlement's stores allow, in warrior-percent (a warrior with stone-tipped spears = 100). Each warrior takes the
    /// strongest level still in stock; with <paramref name="useUp"/> the goods are taken out of the stores.
    /// </summary>
    public static long ArmedStrength(GameContent content, CivilizationState? civ, SettlementState s, int warriors, bool useUp)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        IReadOnlyList<ArmamentDefinition> arms = content.War.Armaments;
        Span<Amount> stock = s.Stock;
        long strength = 0;
        int left = Math.Max(0, warriors);
        ulong used = 0;
        while (left > 0)
        {
            int best = -1;
            for (int a = 0; a < arms.Count && a < 64; a++)
            {
                ArmamentDefinition d = arms[a];
                if ((used & (1UL << a)) != 0 || (d.Technology >= 0 && civ?.Knows(d.Technology) != true))
                {
                    continue;
                }

                if (best < 0 || d.StrengthPercent > arms[best].StrengthPercent)
                {
                    best = a;
                }
            }

            if (best < 0)
            {
                break; // nothing left at all: the rest fights with nothing
            }

            used |= 1UL << best;
            ArmamentDefinition arm = arms[best];
            int covered = left;
            if (arm.Good >= 0)
            {
                long have = arm.Good < stock.Length ? stock[arm.Good].Hundredths : 0;
                covered = (int)Math.Min(left, have / Math.Max(1, arm.PerWarrior.Hundredths));
                if (useUp && covered > 0)
                {
                    stock[arm.Good] -= arm.PerWarrior * covered;
                }
            }

            strength += (long)covered * arm.StrengthPercent;
            left -= covered;
        }

        return strength;
    }

    /// <summary>Strength of the weakest level that needs neither technology nor goods (bare hands), percent.</summary>
    public static int BareHandsPercent(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        int best = 0;
        foreach (ArmamentDefinition a in content.War.Armaments)
        {
            if (a.Technology < 0 && a.Good < 0)
            {
                best = Math.Max(best, a.StrengthPercent);
            }
        }

        return best;
    }

    /// <summary>
    /// Strength of the defence of <paramref name="target"/> in warrior-percent: its warriors with the arms its stores
    /// allow (not used up) and a share of everyone else bare-handed (population.json defenderNonWarriorPercent).
    /// </summary>
    public static long DefenceStrength(GameContent content, GameState state, SettlementState target)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(target);
        int warriors = SettlementAssets.Warriors(content, target);
        long armed = ArmedStrength(content, state.FindCivilization(target.Owner), target, warriors, useUp: false);
        long others = (long)(target.TotalPopulation() - warriors) * content.Population.DefenderNonWarriorPercent * BareHandsPercent(content) / 100;
        return Math.Max(1, armed + others);
    }

    /// <summary>Strength of a campaign in a fight, warrior-percent: warriors × arms × morale.</summary>
    public static long AttackStrength(Episode e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return (long)e.Warriors * e.ArmsPercent * e.Morale / 100;
    }

    /// <summary>Strength of a campaign in a battle: <see cref="AttackStrength"/>, lowered when it has no food left (stage 4e PR 4).</summary>
    public static long BattleStrength(GameContent content, Episode e)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(e);
        bool hungry = FoodIn(content, e.Food) + FoodIn(content, e.Loot) <= Amount.Zero;
        return AttackStrength(e) * (hungry ? content.War.Battle.HungryStrengthPercent : 100) / 100;
    }

    /// <summary>Strength of the defence of <paramref name="target"/> with the terrain of its cell (stage 4e PR 4), warrior-percent.</summary>
    public static long DefenceInPlace(GameContent content, GameState state, SettlementState target, int from)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(target);
        WorldState world = state.Worlds[target.World.Value];
        int terrain = Battles.TerrainPercent(content, world, world.Geometry.Index(target.Cell), from);
        return DefenceStrength(content, state, target) * (100 + terrain) / 100;
    }

    /// <summary>Men who fight for <paramref name="s"/>: its warriors and a share of everyone else.</summary>
    public static int Defenders(GameContent content, SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(content);
        int warriors = SettlementAssets.Warriors(content, s);
        return warriors + (int)((long)(s.TotalPopulation() - warriors) * content.Population.DefenderNonWarriorPercent / 100);
    }

    /// <summary>Kills <paramref name="count"/> defenders of <paramref name="s"/>: warriors first, then the others; returns how many died.</summary>
    public static int KillDefenders(GameContent content, SettlementState s, int count)
    {
        int dead = Take(content, s, count, fighters: true);
        return dead + Take(content, s, count - dead, fighters: false);
    }

    /// <summary>
    /// True when <paramref name="civ"/> sees <paramref name="cell"/> now: seen in its last own step, or at most
    /// <see cref="WarRules.ArmySightCells"/> from one of its armies on campaign (stage 4e PR 5).
    /// </summary>
    public static bool Sees(GameContent content, GameState state, CivilizationState civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        if (MapMemory.SeesNow(state, civ, cell))
        {
            return true;
        }

        HexGlobe geo = state.Worlds[0].Geometry;
        HexCoord at = geo.FromIndex(cell);
        foreach (Episode e in state.Episodes.All)
        {
            if (!e.Ended && e.Kind == EpisodeKind.Campaign && e.Civ == civ.Id && geo.Distance(geo.FromIndex(e.Cell), at) <= content.War.ArmySightCells)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The battle <paramref name="civ"/> expects when <paramref name="attackers"/> men of strength <paramref name="attack"/>
    /// (warrior-percent, morale in) attack <paramref name="target"/> from <paramref name="from"/> (-1: not known), or null
    /// when the target is not in sight (stage 4e PR 5): the defenders as they are now, with their arms and the terrain.
    /// </summary>
    public static ExpectedBattle? Expect(GameContent content, GameState state, CivilizationState civ, SettlementState target, int attackers, long attack, int morale, int from)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(target);
        int cell = state.Worlds[target.World.Value].Geometry.Index(target.Cell);
        if (!Sees(content, state, civ, cell))
        {
            return null;
        }

        BattleRules rules = content.War.Battle;
        long defence = DefenceInPlace(content, state, target, from) * rules.DefenderMorale / 100;
        return Battles.Expect(rules, attackers, attack, morale, Defenders(content, target), defence);
    }

    /// <summary>
    /// Cells from <paramref name="cell"/> to the reach of carriers: its distance to the nearest own settlement of
    /// <paramref name="civ"/> beyond <see cref="WarRules.CarrierCells"/>, 0 within reach (stage 4e PR 5).
    /// </summary>
    public static int BeyondSupplyCells(GameContent content, GameState state, CivId civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        WorldState world = state.Worlds[0];
        HexCoord at = world.Geometry.FromIndex(cell);
        int nearest = int.MaxValue;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive && s.Owner == civ && s.World == world.Id)
            {
                nearest = Math.Min(nearest, world.Geometry.Distance(at, s.Cell));
            }
        }

        return nearest == int.MaxValue ? 0 : Math.Max(0, nearest - content.War.CarrierCells);
    }

    /// <summary>
    /// Months of action an army needs to march <paramref name="cells"/> cells of open land (stage 6 PR 3b: the army walks as a
    /// unit of its kind; <see cref="MarchMonthsOf"/> for a known way with its terrain).
    /// </summary>
    public static int MarchMonths(GameContent content, int cells, Units.UnitKindDefinition? kind = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        kind ??= content.Units.First(Units.UnitRole.Army);
        return cells <= 0 ? 0 : MarchMonthsOf(content, (long)cells * kind.TicksPerCell);
    }

    /// <summary>Months of action <paramref name="ticks"/> ticks of marching take.</summary>
    public static int MarchMonthsOf(GameContent content, long ticks)
    {
        ArgumentNullException.ThrowIfNull(content);
        return ticks <= 0 ? 0 : (int)Math.Min(int.MaxValue, IntMath.MulDiv(ticks, 1, Math.Max(1, content.Time.TicksPerActionMonth), Rounding.Ceiling));
    }

    /// <summary>Months the food of a campaign lasts: what it carries, the food among its loot and what carriers bring.</summary>
    public static int FoodMonthsLeft(GameContent content, Episode e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Amount food = FoodIn(content, e.Food) + FoodIn(content, e.Loot) + FoodIn(content, e.ConvoyFood);
        return MonthsOf(content, food, e.Warriors);
    }

    /// <summary>Whole months <paramref name="food"/> feeds <paramref name="warriors"/> (at most 120).</summary>
    public static int MonthsOf(GameContent content, Amount food, int warriors)
    {
        long year = Math.Max(1, FoodFor(content, Math.Max(1, warriors), 12).Hundredths);
        return (int)Math.Min(120, food.Hundredths * 12 / year);
    }

    /// <summary>Food <paramref name="warriors"/> eat in <paramref name="months"/> months.</summary>
    public static Amount FoodFor(GameContent content, int warriors, int months)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.War.FoodPerWarriorPerYear.MultiplyRatio((long)Math.Max(0, warriors) * Math.Max(0, months), 12, Rounding.Ceiling);
    }

    /// <summary>
    /// Moves food out of <paramref name="s"/> into <paramref name="into"/> (index = good), at most <paramref name="want"/>
    /// and at most <see cref="WarRules.SupplySharePercent"/> of each food good, goods in id order; returns how much.
    /// </summary>
    public static Amount TakeFood(GameContent content, SettlementState s, Span<Amount> into, Amount want)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        Span<Amount> stock = s.Stock;
        Amount taken = Amount.Zero;
        for (int g = 0; g < stock.Length && g < into.Length && taken < want; g++)
        {
            if (!content.Goods[g].IsFood || stock[g] <= Amount.Zero)
            {
                continue;
            }

            Amount n = Amount.Min(stock[g].MultiplyPercent(content.War.SupplySharePercent, Rounding.Floor), want - taken);
            stock[g] -= n;
            into[g] += n;
            taken += n;
        }

        return taken;
    }

    /// <summary>Food <paramref name="s"/> can give a campaign or its carriers (<see cref="WarRules.SupplySharePercent"/> of each food good).</summary>
    public static Amount SpareFood(GameContent content, SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        ReadOnlySpan<Amount> stock = s.StockView;
        Amount total = Amount.Zero;
        for (int g = 0; g < stock.Length && g < content.Goods.Count; g++)
        {
            total += content.Goods[g].IsFood ? stock[g].MultiplyPercent(content.War.SupplySharePercent, Rounding.Floor) : Amount.Zero;
        }

        return total;
    }

    /// <summary>All food in <paramref name="goods"/> (index = good).</summary>
    public static Amount FoodIn(GameContent content, Amount[] goods)
    {
        ArgumentNullException.ThrowIfNull(goods);
        return FoodIn(content, (ReadOnlySpan<Amount>)goods);
    }

    /// <summary>All food in <paramref name="goods"/> (index = good), e.g. a settlement's stock.</summary>
    public static Amount FoodIn(GameContent content, ReadOnlySpan<Amount> goods)
    {
        ArgumentNullException.ThrowIfNull(content);
        Amount total = Amount.Zero;
        for (int g = 0; g < goods.Length && g < content.Goods.Count; g++)
        {
            total += content.Goods[g].IsFood ? goods[g] : Amount.Zero;
        }

        return total;
    }

    /// <summary>Takes up to <paramref name="count"/> people who do not fight out of <paramref name="s"/> (carriers); returns how many.</summary>
    public static int TakeCarriers(GameContent content, SettlementState s, int count) => Take(content, s, count, fighters: false);

    /// <summary>Gives <paramref name="count"/> carriers back to the first stratum of <paramref name="s"/> that does not fight.</summary>
    public static void GiveCarriers(GameContent content, SettlementState s, int count) => Give(content, s, count, fighters: false);

    /// <summary>People of <paramref name="s"/> who do not fight.</summary>
    public static int NonFighters(GameContent content, SettlementState s) => s.TotalPopulation() - SettlementAssets.Warriors(content, s);

    private static int Take(GameContent content, SettlementState s, int count, bool fighters)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        int taken = 0;
        Span<int> people = s.Population;
        for (int k = 0; k < people.Length && k < content.Strata.Count && taken < count; k++)
        {
            if (content.Strata[k].Fights == fighters)
            {
                int n = Math.Min(people[k], count - taken);
                people[k] -= n;
                taken += n;
            }
        }

        return taken;
    }

    private static void Give(GameContent content, SettlementState s, int count, bool fighters)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        Span<int> people = s.Population;
        for (int k = 0; k < people.Length && k < content.Strata.Count; k++)
        {
            if (content.Strata[k].Fights == fighters)
            {
                people[k] += count;
                return;
            }
        }
    }
}

/// <summary>
/// Sends warriors on a campaign against an enemy settlement (GDD 9.3; stage 4e PR 2–3, ADR 0022): warriors of
/// <see cref="Home"/> and of the <see cref="Others"/> (own settlements near it) take their arms and food and gather at
/// <see cref="Home"/>, then march in steps of a month; a third of every settlement's warriors stays home.
/// </summary>
public sealed record LaunchCampaignCommand(GameContent Content, SettlementId Home, SettlementId Target, int Warriors, IReadOnlyList<CampaignMuster>? Others = null) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Military;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ) =>
        Campaigns.CannotLaunch(Content, state, civ, Home.Value, Target.Value, Warriors, Others) is { } reason ? CommandResult.Fail(reason) : CommandResult.Ok;

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        SettlementState home = state.FindSettlement(Home)!;
        SettlementState target = state.FindSettlement(Target)!;
        CivilizationState c = state.FindCivilization(civ)!;
        WarRules rules = Content.War;
        HexGlobe geo = state.Worlds[0].Geometry;
        int count = 1 + (Others?.Count ?? 0);
        int[] members = new int[count];
        int[] sent = new int[count];
        var food = new Amount[Content.Goods.Count];
        long strength = 0;
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            SettlementState s = i == 0 ? home : state.FindSettlement(Others![i - 1].Settlement)!;
            int asked = i == 0 ? Warriors : Others![i - 1].Warriors;
            int taken = Campaigns.TakeWarriors(Content, s, asked);
            strength += Campaigns.ArmedStrength(Content, c, s, taken, useUp: true);
            members[i] = s.Id.Value;
            sent[i] = taken;
            total += taken;
        }

        // Food for the first months, as much as the warriors carry, from every settlement in turn.
        Amount want = Amount.Min(Campaigns.FoodFor(Content, total, rules.CarryFoodMonths), SettlementAssets.CarryCapacity(Content, state, civ, total));
        Amount packed = Amount.Zero;
        for (int i = 0; i < count && packed < want; i++)
        {
            packed += Campaigns.TakeFood(Content, state.FindSettlement(new SettlementId(members[i]))!, food, want - packed);
        }

        Episode e = state.Episodes.Begin(new Episode
        {
            Kind = EpisodeKind.Campaign,
            Civ = civ,
            Other = target.Owner,
            StartMonth = state.Month,
            NextTick = state.Turn + Content.Time.ActionTicks(rules.CampaignStepMonths),
            TickMonths = rules.CampaignStepMonths,
            Home = home.Id.Value,
            Target = target.Id.Value,
            GoalTarget = target.Id.Value,
            Warriors = total,
            Mustered = total,
            Members = members,
            MemberWarriors = sent,
            ArmsPercent = total > 0 ? (int)(strength / total) : Campaigns.BareHandsPercent(Content),
            Morale = 100,
            Food = food,
            Cell = geo.Index(home.Cell),
            Phase = count > 1 ? CampaignPhase.Muster : CampaignPhase.March,
            Loot = new Amount[Content.Goods.Count],
            ConvoyFood = new Amount[Content.Goods.Count],
        });

        // Stage 6 PR 3b: the army is a unit at home; the warriors of the other settlements walk there as units of their own
        // (the muster ends when all of them stand at home).
        Units.UnitKindDefinition kind = Campaigns.ArmyKind(Content, c);
        Units.Unit army = state.Units.Add(
            kind, civ, home.World, geo.Index(home.Cell), home.Id.Value, Units.UnitOrder.Idle, Content.Strata.Count, Content.Skills.Count, Content.Goods.Count);
        army.Episode = e.Id;
        army.People[0] = sent[0];
        e.Army = army.Id;
        for (int i = 1; i < count; i++)
        {
            SettlementState s = state.FindSettlement(new SettlementId(members[i]))!;
            Units.Unit joining = state.Units.Add(
                kind, civ, s.World, geo.Index(s.Cell), s.Id.Value, Units.UnitOrder.Move, Content.Strata.Count, Content.Skills.Count, Content.Goods.Count);
            joining.Target = geo.Index(home.Cell);
            joining.Episode = e.Id;
            joining.People[0] = sent[i];
        }

        Treaty? war = state.Treaties.Find(civ, target.Owner, TreatyKind.War);
        if (war is not null)
        {
            war.Campaigns++;
            war.LastCampaignTick = state.Turn;
        }

        string from = count == 1 ? home.Name : $"{count} settlements gathering at {home.Name}";
        return $"{c.Name} sent {total} warriors from {from} against {target.Name}.";
    }
}

/// <summary>
/// Calls a campaign home (stage 4e PR 5): an army that is still gathering or marching turns back; the survivors go home
/// as after a battle. The commander sends it when what it sees now promises no victory or the food will not last.
/// </summary>
public sealed record RecallCampaignCommand(long Episode) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Military;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Find(state, civ) is null ? CommandResult.Fail("no campaign of this civilization on the march") : CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        Episode e = Find(state, civ)!;
        e.Phase = CampaignPhase.Return;
        e.Foraging = false;
        string name = state.FindCivilization(civ)?.Name ?? civ.ToString();
        string target = state.FindSettlement(new SettlementId(e.Target))?.Name ?? "the enemy";
        return $"{name} called its warriors back from the march on {target}.";
    }

    private Episode? Find(GameState state, CivId civ)
    {
        foreach (Episode e in state.Episodes.All)
        {
            if (e.Id == Episode && !e.Ended && e.Kind == EpisodeKind.Campaign && e.Civ == civ && e.Phase is CampaignPhase.Muster or CampaignPhase.March)
            {
                return e;
            }
        }

        return null;
    }
}

/// <summary>
/// Combat phase (GDD 11.1 phase 6; stage 4e PR 2–3, ADR 0022, stage 6 PR 3b): the army of a campaign walks as a unit in
/// every tick (UnitSystem); every campaign whose step (a month of action) is due eats, gathers its warriors, sends its army
/// on towards its target, fights a month after the target is within its kind's range, takes what its warriors can carry and
/// goes home (fleeing when beaten), where the survivors go back to their settlements and rest. Carriers from the nearest own
/// settlement bring food after it; a hungry campaign loses morale, raids nearby enemy settlements for food and turns back
/// when its morale is gone. A campaign turns back when peace is made, the target is gone or the way is lost.
/// </summary>
public sealed class CampaignSystem(GameContent content) : ITurnSystem
{
    private const ulong FightSalt = 0xCA0000UL;
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Combat;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        EpisodeBook book = state.Episodes;
        if (book.Count == 0)
        {
            return;
        }

        for (int k = 0; k < book.All.Count; k++)
        {
            Episode e = book.All[k];
            if (e.Ended || e.Kind != EpisodeKind.Campaign)
            {
                continue;
            }

            // Stage 6 PR 3b: the army and its carriers walk as units in every tick; the campaign follows where they stand.
            Follow(state, e);
            if (e.Phase == CampaignPhase.Battle && e.RoundTick <= state.Turn)
            {
                BattleRound(context, state, e); // a round a second (stage 6), between the monthly steps
                Follow(state, e);
            }

            if (e.NextTick > state.Turn)
            {
                continue;
            }

            Step(context, state, e);
            e.NextTick = state.Turn + _content.Time.ActionTicks(e.TickMonths);
            if (e.Ended)
            {
                Release(state, e);
            }
        }

        book.RemoveEnded();
        state.Units.RemoveGone(); // units that joined or were released this tick leave the book now (saves see only the living)
    }

    // Where the army and the carriers stand now (views, sight and the commander read the campaign's cells).
    private static void Follow(GameState state, Episode e)
    {
        if (e.Army >= 0 && state.Units.Find(e.Army) is { } army)
        {
            e.Cell = army.Cell;
            army.People[0] = e.Warriors;
        }

        if (e.Convoy >= 0 && state.Units.Find(e.Convoy) is { } convoy)
        {
            e.ConvoyCell = convoy.Cell;
        }
    }

    // A campaign that ended lets go of its units: the army and gathering warriors are gone (their men went home with the
    // campaign); carriers on the way walk home with what they carry.
    private void Release(GameState state, Episode e)
    {
        for (int k = 0; k < state.Units.Count; k++)
        {
            Units.Unit u = state.Units[k];
            if (u.Episode != e.Id || u.Gone)
            {
                continue;
            }

            if (u.Role == Units.UnitRole.Carriers)
            {
                SendCarriersHome(state, e, u);
            }
            else
            {
                u.Gone = true;
            }
        }

        e.Army = -1;
        e.Convoy = -1;
    }

    private void Step(TurnContext context, GameState state, Episode e)
    {
        CivilizationState? civ = state.FindCivilization(e.Civ);
        if (civ is not { IsAlive: true })
        {
            e.Ended = true;
            return;
        }

        Treaty? war = state.Treaties.Find(e.Civ, e.Other, TreatyKind.War);
        if (war is not null)
        {
            war.CampaignMonths += e.TickMonths;
            war.LastCampaignTick = state.Turn;
        }

        WorldState world = state.Worlds[0];
        if (e.Phase == CampaignPhase.Muster && state.FindSettlement(new SettlementId(e.Home)) is { IsActive: true } home && home.Owner == e.Civ)
        {
            // While they gather at home, the warriors eat from its stores, not from their packs (stage 4e PR 6).
            Campaigns.TakeFood(_content, home, e.Food, Campaigns.FoodFor(_content, e.Warriors, e.TickMonths));
        }

        bool hungry = !Eat(e);
        if (e.Phase == CampaignPhase.Muster)
        {
            if (war is null)
            {
                Gathered(state, e, all: true); // peace while gathering: everyone counts as home
                e.Phase = CampaignPhase.Return;
            }
            else if (Gathered(state, e, all: false))
            {
                e.Phase = CampaignPhase.March;
            }
        }
        else if (e.Phase == CampaignPhase.March)
        {
            March(context, state, e, civ, war, world, hungry);
        }
        else if (e.Phase == CampaignPhase.Battle)
        {
            // The battle runs round by round (BattleRound); the warriors keep eating meanwhile.
        }
        else
        {
            GoHome(context, state, e, civ, world);
        }

        if (!e.Ended)
        {
            Supply(state, e, civ, world);
        }
    }

    // The muster (stage 6 PR 3b): warriors of the other settlements walk to the army at home; those who stand there join
    // it. True when nobody is still on the way (with all, everyone joins at once).
    private bool Gathered(GameState state, Episode e, bool all)
    {
        Units.Unit? army = e.Army >= 0 ? state.Units.Find(e.Army) : null;
        bool done = true;
        for (int k = 0; k < state.Units.Count; k++)
        {
            Units.Unit u = state.Units[k];
            if (u.Episode != e.Id || u.Gone || u.Role != Units.UnitRole.Army || (army is { } a && u.Id == a.Id))
            {
                continue;
            }

            bool there = army is not { } at || u.Cell == at.Cell || (u.Order == Units.UnitOrder.Idle && u.Target == u.Cell);
            if (all || there || u.Order == Units.UnitOrder.Idle)
            {
                u.Gone = true; // joined (an idle one that found no way counts as home too: its men were counted at the start)
            }
            else
            {
                done = false;
            }
        }

        return done;
    }

    // A month of food for every warrior: first what they carry, then the food among the loot. Hunger costs morale.
    private bool Eat(Episode e)
    {
        Amount need = Campaigns.FoodFor(_content, e.Warriors, e.TickMonths);
        need = EatFrom(e.Food, need);
        need = EatFrom(e.Loot, need);
        bool fed = need <= Amount.Zero;
        if (fed)
        {
            e.Morale = Math.Min(100, e.Morale + (_content.War.FedMoralePerMonth * e.TickMonths));
        }
        else
        {
            e.HungryMonths += e.TickMonths;
            e.Morale = Math.Max(0, e.Morale - (_content.War.HungerMoralePerMonth * e.TickMonths));
        }

        return fed;
    }

    private Amount EatFrom(Amount[] goods, Amount need)
    {
        for (int g = 0; g < goods.Length && g < _content.Goods.Count && need > Amount.Zero; g++)
        {
            if (_content.Goods[g].IsFood && goods[g] > Amount.Zero)
            {
                Amount n = Amount.Min(goods[g], need);
                goods[g] -= n;
                need -= n;
            }
        }

        return need;
    }

    private void March(TurnContext context, GameState state, Episode e, CivilizationState civ, Treaty? war, WorldState world, bool hungry)
    {
        if (e.Morale < _content.War.RetreatMorale)
        {
            context.Chronicle($"Hunger broke the warriors of {civ.Name}: they turned back.");
            e.Phase = CampaignPhase.Return;
            return;
        }

        if (hungry && !e.Foraging && war is not null && Forage(state, e, civ, world) is { } prey)
        {
            e.Foraging = true;
            e.Target = prey.Id.Value;
            context.Chronicle($"The hungry warriors of {civ.Name} turned on {prey.Name} for food.");
        }

        SettlementState? target = state.FindSettlement(new SettlementId(e.Target));
        if (e.Foraging && (target is not { IsActive: true } || target.Owner != e.Other))
        {
            e.Foraging = false; // the prey is gone: on to the goal
            e.Target = e.GoalTarget;
            target = state.FindSettlement(new SettlementId(e.Target));
        }

        if (war is null || target is not { IsActive: true } || target.Owner != e.Other)
        {
            e.Phase = CampaignPhase.Return;
            return;
        }

        // Stage 6 PR 3b: the army walks as a unit (UnitSystem) towards its target until the target is within its kind's
        // range; it attacks a month of action after it got there, so that its commander can still call it back (PR 5).
        Units.Unit? army = e.Army >= 0 ? state.Units.Find(e.Army) : null;
        if (army is not { } a)
        {
            e.Phase = CampaignPhase.Return;
            return;
        }

        int goal = world.Geometry.Index(target.Cell);
        int range = _content.Units.Of(a.Kind).RangeCells;
        if (world.Geometry.Distance(world.Geometry.FromIndex(a.Cell), target.Cell) > range)
        {
            e.InRangeSince = -1;
            if (a.Order == Units.UnitOrder.Idle && a.Target == goal)
            {
                e.Phase = CampaignPhase.Return; // it found no known way there (UnitSystem stops it)
            }
            else if (a.Order != Units.UnitOrder.Attack || a.Target != goal)
            {
                a.Order = Units.UnitOrder.Attack;
                a.Target = goal;
                a.ForgetPath();
            }

            return;
        }

        if (e.InRangeSince < 0)
        {
            e.InRangeSince = state.Turn;
        }

        if (state.Turn - e.InRangeSince < _content.Time.ActionTicks(1))
        {
            return; // in reach: the attack comes a month of action later
        }

        e.InRangeSince = -1;
        BeginBattle(state, e, target);
        BattleRound(context, state, e); // the first round at once, the next every RoundTicks ticks
    }

    // Stage 6 (decision of 2026-11-09): the battle is fought a round every BattleRules.RoundTicks ticks, so it lasts some
    // seconds and the army can be seen fighting; nobody can call it back meanwhile.
    private void BeginBattle(GameState state, Episode e, SettlementState target)
    {
        BattleRules rules = _content.War.Battle;
        long attack = Campaigns.BattleStrength(_content, e);
        long defence = Campaigns.DefenceInPlace(_content, state, target, e.Cell) * rules.DefenderMorale / 100;
        Contacts.RecordAttack(_content, state, target.Owner, e.Civ);
        e.Fight = Battles.Begin(rules, e.Warriors, attack, e.Morale, Campaigns.Defenders(_content, target), defence);
        e.FightLost = 0;
        e.FightKilled = 0;
        e.Battles++;
        e.Phase = CampaignPhase.Battle;
        e.RoundTick = state.Turn;
    }

    // One round of the battle at the campaign's target: the dead fall at once (the army shrinks on the map); when the
    // battle is over, its end is decided as before (stage 4e PR 4).
    private void BattleRound(TurnContext context, GameState state, Episode e)
    {
        CivilizationState? civ = state.FindCivilization(e.Civ);
        SettlementState? target = state.FindSettlement(new SettlementId(e.Target));
        Treaty? war = state.Treaties.Find(e.Civ, e.Other, TreatyKind.War);
        if (civ is not { IsAlive: true } || war is null || target is not { IsActive: true } || target.Owner != e.Other)
        {
            // Peace, or the place is gone or changed hands: the battle breaks off and the warriors go home.
            if (war is not null && target is not null && (e.FightLost > 0 || e.FightKilled > 0))
            {
                Wars.RecordFight(_content, state, target.Owner, e.Civ, false, e.FightKilled, e.FightLost);
            }

            e.Phase = CampaignPhase.Return;
            e.Foraging = false;
            e.Target = e.GoalTarget;
            return;
        }

        BattleRules rules = _content.War.Battle;
        BattleState b = e.Fight;
        do
        {
            DeterministicRandom rng = state.Random.Derive(RandomStreamId.Combat, FightSalt | (ulong)e.Id, (state.Turn * 128L) + b.Rounds);
            (int lostA, int lostD) = Battles.Round(rules, rng, ref b);
            int lost = Math.Min(e.Warriors, lostA);
            e.Warriors -= lost;
            e.Dead += lost;
            e.FightLost += lost;
            int killed = Campaigns.KillDefenders(_content, target, lostD);
            e.Killed += killed;
            e.FightKilled += killed;
        }
        while (!b.Over && rules.RoundTicks <= 0);

        e.Fight = b;
        e.Morale = b.MoraleA;
        if (!b.Over)
        {
            e.RoundTick = state.Turn + rules.RoundTicks;
            return;
        }

        bool foray = e.Foraging;
        (string text, bool won) = Conclude(state, e, civ, target, war, b.Result);
        context.Chronicle(text);
        if (foray)
        {
            e.Forays++;
            e.LastPrey = target.Id.Value;
            e.Foraging = false;
            e.Target = e.GoalTarget;
            e.Phase = won ? CampaignPhase.March : CampaignPhase.Return; // fed, they march on to their goal
            e.Fleeing = !won;
            return;
        }

        e.Phase = CampaignPhase.Return;
        e.Fleeing = !won; // beaten: they flee home, nobody turns them (stage 6 PR 3b)
    }

    // The nearest known settlement of the enemy within reach of a hungry campaign, other than its goal.
    private SettlementState? Forage(GameState state, Episode e, CivilizationState civ, WorldState world)
    {
        HexCoord at = world.Geometry.FromIndex(e.Cell);
        SettlementState? best = null;
        int bestDistance = int.MaxValue;
        foreach (int i in state.Grid.Within(world, at, _content.War.ForageCells))
        {
            SettlementState s = state.Settlements[i];
            if (!s.IsActive || s.Owner != e.Other || s.Id.Value == e.GoalTarget || s.Id.Value == e.LastPrey)
            {
                continue;
            }

            int d = world.Geometry.Distance(at, s.Cell);
            if (d <= _content.War.ForageCells && d < bestDistance && MapMemory.Knows(_content, state, civ, world.Geometry.Index(s.Cell)))
            {
                best = s;
                bestDistance = d;
            }
        }

        return best;
    }

    // Carriers (stage 6 PR 3b: a unit): when food runs low, the own settlement in reach that can send the most food sends
    // some; they walk after the army (their way is found again at most once a month of action, as the army moves) and hand
    // the food over when they stand next to it, then walk home.
    private void Supply(GameState state, Episode e, CivilizationState civ, WorldState world)
    {
        WarRules rules = _content.War;
        if (e.Convoy >= 0)
        {
            Units.Unit? convoy = state.Units.Find(e.Convoy);
            if (convoy is not { } c)
            {
                e.Convoy = -1;
                e.ConvoyFrom = -1;
                e.ConvoyCarriers = 0;
                return;
            }

            if (world.Geometry.Distance(world.Geometry.FromIndex(c.Cell), world.Geometry.FromIndex(e.Cell)) <= 1)
            {
                for (int g = 0; g < e.Food.Length && g < e.ConvoyFood.Length; g++)
                {
                    e.Food[g] += e.ConvoyFood[g];
                    e.ConvoyFood[g] = Amount.Zero;
                }

                e.Convoys++;
                SendCarriersHome(state, e, c);
            }
            else if (c.Order == Units.UnitOrder.Idle && c.Target == e.Cell)
            {
                SendCarriersHome(state, e, c); // they found no way to the army: they go home with the food
            }
            else if (c.Target != e.Cell)
            {
                c.Order = Units.UnitOrder.Move;
                c.Target = e.Cell; // the army moved: after it
                c.ForgetPath();
            }

            return;
        }

        if (e.Phase == CampaignPhase.Return || Campaigns.FoodIn(_content, e.Food) >= Campaigns.FoodFor(_content, e.Warriors, rules.ConvoyBelowMonths))
        {
            return;
        }

        SettlementState? from = CarrierSource(state, world, e);
        if (from is null)
        {
            return;
        }

        Amount want = Campaigns.FoodFor(_content, e.Warriors, rules.CarryFoodMonths) - Campaigns.FoodIn(_content, e.Food);
        long perPerson = Math.Max(1, SettlementAssets.CarryCapacity(_content, state, e.Civ, 1).Hundredths);
        int carriers = (int)Math.Min(
            (long)Campaigns.NonFighters(_content, from) * rules.CarriersPercent / 100,
            IntMath.MulDiv(Math.Max(0, want.Hundredths), 1, perPerson, Rounding.Ceiling));
        if (carriers <= 0 || want <= Amount.Zero)
        {
            return;
        }

        Amount load = Campaigns.TakeFood(_content, from, e.ConvoyFood, Amount.Min(want, SettlementAssets.CarryCapacity(_content, state, e.Civ, carriers)));
        if (load <= Amount.Zero)
        {
            return;
        }

        e.ConvoyCarriers = Campaigns.TakeCarriers(_content, from, (int)IntMath.MulDiv(load.Hundredths, 1, perPerson, Rounding.Ceiling));
        e.ConvoyFrom = from.Id.Value;
        e.ConvoyCell = world.Geometry.Index(from.Cell);
        Units.Unit unit = state.Units.Add(
            _content.Units.Best(Units.UnitRole.Carriers, civ.Knows), e.Civ, from.World, e.ConvoyCell, from.Id.Value, Units.UnitOrder.Move,
            _content.Strata.Count, _content.Skills.Count, _content.Goods.Count);
        unit.Target = e.Cell;
        unit.Episode = e.Id;
        unit.People[NonFightingStratum()] = e.ConvoyCarriers; // they join their home again when they come back (UnitSystem)
        e.Convoy = unit.Id;
    }

    // The carriers leave the campaign and walk home with the food they still have; they join their settlement there.
    private void SendCarriersHome(GameState state, Episode e, Units.Unit carriers)
    {
        Span<Amount> cargo = carriers.Cargo;
        for (int g = 0; g < e.ConvoyFood.Length && g < cargo.Length; g++)
        {
            cargo[g] += e.ConvoyFood[g];
        }

        Array.Clear(e.ConvoyFood);
        carriers.Episode = -1;
        carriers.Order = Units.UnitOrder.Return;
        carriers.ForgetPath();
        e.Convoy = -1;
        e.ConvoyCarriers = 0;
        e.ConvoyFrom = -1;
    }

    // The first stratum whose people do not fight (carriers come from it and go back to it, as Campaigns.GiveCarriers).
    private int NonFightingStratum()
    {
        for (int k = 0; k < _content.Strata.Count; k++)
        {
            if (!_content.Strata[k].Fights)
            {
                return k;
            }
        }

        return 0;
    }

    private void GoHome(TurnContext context, GameState state, Episode e, CivilizationState civ, WorldState world)
    {
        if (e.Warriors <= 0)
        {
            Finish(e);
            return;
        }

        SettlementState? home = state.FindSettlement(new SettlementId(e.Home));
        if (home is not { IsActive: true } || home.Owner != e.Civ)
        {
            home = Nearest(state, world, e.Civ, e.Cell, int.MaxValue);
            if (home is null)
            {
                Finish(e); // nowhere to go back to: the warriors scatter
                return;
            }

            e.Home = home.Id.Value;
        }

        // Stage 6 PR 3b: the army walks home as a unit (fleeing after a lost battle); it is home when it stands there.
        int at = world.Geometry.Index(home.Cell);
        if (e.Army >= 0 && state.Units.Find(e.Army) is { } army && army.Cell != at)
        {
            Units.UnitOrder order = e.Fleeing ? Units.UnitOrder.Flee : Units.UnitOrder.Return;
            if (army.Order == Units.UnitOrder.Idle && army.Home == home.Id.Value && army.Target == at)
            {
                // It found no known way home: the survivors come home by themselves.
            }
            else
            {
                if (army.Order != order || army.Home != home.Id.Value)
                {
                    army.Order = order;
                    army.Home = home.Id.Value;
                    army.Target = at;
                    army.ForgetPath();
                }

                return;
            }
        }

        // The survivors go back to the settlements they came from, in proportion, and rest there; the food and the loot
        // stay here.
        int left = e.Warriors;
        long rest = state.ActionMonth + _content.War.RestMonths;
        for (int i = 1; i < e.Members.Length && e.Mustered > 0; i++)
        {
            SettlementState? m = state.FindSettlement(new SettlementId(e.Members[i]));
            int share = (int)((long)e.Warriors * e.MemberWarriors[i] / e.Mustered);
            if (m is { IsActive: true } && m.Owner == e.Civ && share > 0)
            {
                Campaigns.GiveWarriors(_content, m, share);
                m.RestUntilMonth = Math.Max(m.RestUntilMonth, rest);
                left -= share;
            }
        }

        Campaigns.GiveWarriors(_content, home, left);
        home.RestUntilMonth = Math.Max(home.RestUntilMonth, rest);
        Span<Amount> stock = home.Stock;
        for (int g = 0; g < stock.Length; g++)
        {
            stock[g] += (g < e.Loot.Length ? e.Loot[g] : Amount.Zero) + (g < e.Food.Length ? e.Food[g] : Amount.Zero);
        }

        Array.Clear(e.Loot);
        Array.Clear(e.Food);
        context.Chronicle($"{civ.Name}'s warriors came home to {home.Name}: {e.Warriors} of them.");
        Finish(e);
    }

    private static void Finish(Episode e) => e.Ended = true; // its units are released after the step (Release)

    // The end of a battle at the target (stage 4e PR 4; fought round by round since stage 6): the score, and what the
    // winners take.
    private (string Text, bool Won) Conclude(GameState state, Episode e, CivilizationState civ, SettlementState target, Treaty war, BattleResult battle)
    {
        string attackers = civ.Name;
        CivId defender = target.Owner;
        string defenders = state.FindCivilization(defender)?.Name ?? defender.ToString();
        int lost = e.FightLost;
        int killed = e.FightKilled;
        string rounds = battle.Rounds == 1 ? "1 round" : $"{battle.Rounds} rounds";
        if (!battle.AttackersWon)
        {
            Wars.RecordFight(_content, state, defender, e.Civ, false, killed, lost);
            return (battle.Broken == 1
                ? $"{defenders} routed the warriors of {attackers} at {target.Name} after {rounds} ({lost} attackers and {killed} defenders fell)."
                : $"{defenders} held {target.Name} against {attackers} for {rounds} ({lost} attackers and {killed} defenders fell).", false);
        }

        e.Wins++;
        bool camp = _content.SettlementLevels[target.Level].Mobile;
        bool taken = !e.Foraging && !camp && war.Goal == WarGoal.Land && war.GoalSettlement == target.Id.Value;
        string loot = Plunder(state, e, target);
        string flight = string.Empty;
        if (taken)
        {
            // Decision of 2026-11-07: a conquered village keeps its people, who now belong to the winner.
            target.Owner = e.Civ;
            target.ClaimCiv = defender.Value;
            target.ClaimUntilMonth = state.ActionMonth + (_content.Assets.ClaimYears * 12L);
        }
        else if (camp)
        {
            flight = FleeCamp(state, target); // GDD 9.3: the people of a beaten camp flee to their nearest settlement
        }

        Wars.RecordFight(_content, state, e.Civ, defender, taken, lost, killed);
        string fell = $" ({lost} attackers and {killed} defenders fell in {rounds})";
        return (taken
            ? $"{attackers} took {target.Name} from {defenders}{fell}{loot}."
            : $"{attackers} beat {defenders} at {target.Name}{fell}{loot}{flight}.", true);
    }

    // The people of a beaten camp take what they can carry to their nearest own settlement; the camp is left.
    private string FleeCamp(GameState state, SettlementState camp)
    {
        WorldState world = state.Worlds[camp.World.Value];
        SettlementState? refuge = null;
        int best = int.MaxValue;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive && s.Owner == camp.Owner && s.Id != camp.Id && s.World == camp.World)
            {
                int d = world.Geometry.Distance(camp.Cell, s.Cell);
                if (d < best)
                {
                    best = d;
                    refuge = s;
                }
            }
        }

        if (refuge is null)
        {
            return string.Empty; // nowhere to go: they stay in their camp
        }

        SettlementAssets.Carry(camp, refuge, SettlementAssets.CarryCapacity(_content, state, camp.Owner, camp.TotalPopulation()));
        Span<int> people = camp.Population;
        Span<int> to = refuge.Population;
        for (int k = 0; k < people.Length && k < to.Length; k++)
        {
            to[k] += people[k];
        }

        Skills.Move(camp, refuge, 100);
        people.Clear();
        SettlementAssets.Abandon(_content, state, camp);
        return $"; the survivors fled to {refuge.Name}";
    }

    // The winners take what their warriors can carry next to their food, the same share of every store; hungry
    // warriors raiding for food take the food first.
    private string Plunder(GameState state, Episode e, SettlementState target)
    {
        Amount capacity = SettlementAssets.CarryCapacity(_content, state, e.Civ, e.Warriors) - Campaigns.FoodIn(_content, e.Food) - Total(e.Loot);
        long carried = 0;
        long food = 0;
        if (e.Foraging)
        {
            food = Take(target.Stock, e.Loot, capacity.Hundredths, foodOnly: true);
            carried += food;
        }

        carried += Take(target.Stock, e.Loot, capacity.Hundredths - carried, foodOnly: false);
        if (carried <= 0)
        {
            return string.Empty;
        }

        return food > 0
            ? $" and carried off {food / 100} loads of food and {(carried - food) / 100} of other goods"
            : $" and carried off {carried / 100} loads of goods";
    }

    // Moves at most room hundredths out of stock into loot, the same share of every good (only food when foodOnly).
    private long Take(Span<Amount> stock, Amount[] loot, long room, bool foodOnly)
    {
        long total = 0;
        for (int g = 0; g < stock.Length; g++)
        {
            total += !foodOnly || _content.Goods[g].IsFood ? stock[g].Hundredths : 0;
        }

        if (total <= 0 || room <= 0)
        {
            return 0;
        }

        long share = Math.Min(total, room);
        long carried = 0;
        for (int g = 0; g < stock.Length && g < loot.Length; g++)
        {
            if (foodOnly && !_content.Goods[g].IsFood)
            {
                continue;
            }

            long take = stock[g].Hundredths * share / total;
            stock[g] -= Amount.FromHundredths(take);
            loot[g] += Amount.FromHundredths(take);
            carried += take;
        }

        return carried;
    }

    private static Amount Total(Amount[] goods)
    {
        Amount total = Amount.Zero;
        foreach (Amount a in goods)
        {
            total += a;
        }

        return total;
    }

    // The nearest own settlement within reach of the carriers that has food and people to carry it (ties: lowest id).
    // The own settlement within the carriers' reach that can send the most food (stage 4e PR 6: the nearest one often had
    // only crumbs, and its convoy kept the others from sending), the nearer on a tie.
    private SettlementState? CarrierSource(GameState state, WorldState world, Episode e)
    {
        HexCoord at = world.Geometry.FromIndex(e.Cell);
        SettlementState? best = null;
        int bestDistance = int.MaxValue;
        Amount bestFood = Amount.Zero;
        long perPerson = Math.Max(1, SettlementAssets.CarryCapacity(_content, state, e.Civ, 1).Hundredths);
        foreach (int i in state.Grid.Within(world, at, _content.War.CarrierCells))
        {
            SettlementState s = state.Settlements[i];
            if (!s.IsActive || s.Owner != e.Civ)
            {
                continue;
            }

            int d = world.Geometry.Distance(at, s.Cell);
            long carriers = (long)Campaigns.NonFighters(_content, s) * _content.War.CarriersPercent / 100;
            if (d > _content.War.CarrierCells || carriers <= 0)
            {
                continue;
            }

            // What its carriers can bring: its spare food, at most what they carry.
            Amount food = Amount.Min(Campaigns.SpareFood(_content, s), Amount.FromHundredths(carriers * perPerson));
            if (food > bestFood || (food == bestFood && food > Amount.Zero && d < bestDistance))
            {
                best = s;
                bestDistance = d;
                bestFood = food;
            }
        }

        return best;
    }

    private static SettlementState? Nearest(GameState state, WorldState world, CivId civ, int cell, int maxDistance)
    {
        HexCoord at = world.Geometry.FromIndex(cell);
        SettlementState? best = null;
        int bestDistance = int.MaxValue;
        foreach (int i in state.Grid.Within(world, at, maxDistance))
        {
            SettlementState s = state.Settlements[i];
            if (s.IsActive && s.Owner == civ)
            {
                int d = world.Geometry.Distance(at, s.Cell);
                if (d < bestDistance && d <= maxDistance)
                {
                    bestDistance = d;
                    best = s;
                }
            }
        }

        return best;
    }
}
