using System;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.Units;

namespace CivSim.Core.Population;

/// <summary>
/// Learning skills (stage 4F PR 2, ADR 0020, GDD 5.1), in the population phase after births and deaths, in each
/// civilization's own step: apprentices finish their years and become skilled; masters take new apprentices from the
/// free people of the stratum, as many as the settlement can use; spare skilled people move to a settlement of their
/// own that needs them; a civilization that lost a skill gets a master back from a trade partner.
/// </summary>
public sealed class SkillSystem : ITurnSystem
{
    private readonly GameContent _content;
    private readonly SkillRules _rules;
    private long[] _total = [];

    /// <summary>Creates the system.</summary>
    public SkillSystem(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _rules = content.SkillRules;
    }

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Population;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        int skills = _content.Skills.Count;
        if (skills == 0)
        {
            return;
        }

        foreach (SettlementState s in state.Settlements)
        {
            Skills.Trim(_content.Skills, s); // also empty and abandoned places: nobody there knows anything
        }

        CountAway(state);
        CountMissing(state);
        foreach (SettlementState s in state.Settlements)
        {
            int months = WorldClock.MonthsOf(state, s.Owner);
            if (months == 0 || !s.IsActive || s.TotalPopulation() == 0)
            {
                continue;
            }

            CivilizationState civ = state.FindCivilization(s.Owner)!;
            Graduate(s, months);
            TakeApprentices(state, civ, s);
        }

        ListNeedy(state);
        foreach (SettlementState s in state.Settlements)
        {
            if (WorldClock.MonthsOf(state, s.Owner) > 0 && s.IsActive && s.TotalPopulation() > 0)
            {
                MoveSpare(state, s);
            }
        }

        BringBack(state);
    }

    // Apprentices who have learned their years become skilled; a step shorter than the years graduates its share,
    // rounded up (eras 1-2 steps are longer than any training).
    private void Graduate(SettlementState s, int months)
    {
        Span<int> skilled = s.Skilled;
        Span<int> trainees = s.Trainees;
        for (int k = 0; k < skilled.Length && k < _content.Skills.Count; k++)
        {
            if (trainees[k] == 0)
            {
                continue;
            }

            long span = 12L * _content.Skills[k].TrainingYears;
            int done = span <= months ? trainees[k] : (int)Math.Min(trainees[k], ((long)trainees[k] * months + span - 1) / span);
            trainees[k] -= done;
            skilled[k] += done;
        }
    }

    // Masters take apprentices from the free people of the stratum, up to what the settlement can use.
    private void TakeApprentices(GameState state, CivilizationState civ, SettlementState s)
    {
        Span<int> skilled = s.Skilled;
        Span<int> trainees = s.Trainees;
        int c = state.IndexOfCivilization(civ.Id);
        for (int k = 0; k < skilled.Length && k < _content.Skills.Count; k++)
        {
            SkillDefinition def = _content.Skills[k];
            int masters = Have(s, k);
            if (masters == 0 || !civ.Knows(def.TechnologyId))
            {
                continue;
            }

            // Masters also teach for the settlements of their people that have nobody of the skill.
            long need = Need(s, k);
            if (_missing[(c * _content.Skills.Count) + k])
            {
                need += (need * _rules.TeachForOthersPercent / 100) + _rules.MoveSpareAbove;
            }

            long room = (long)masters * _rules.ApprenticesPerMaster - trainees[k];
            long want = need - masters - trainees[k];
            long take = Math.Min(Math.Min(room, want), Skills.Free(_content, s, def.StratumId));
            if (take > 0)
            {
                trainees[k] += (int)take;
            }
        }
    }

    private bool[] _missing = [];
    private int[] _awaySkilled = []; // by settlement and skill: skilled people out in the settlement's work groups
    private int[] _awayPeople = []; // by settlement and stratum

    // Work groups are their settlements' people (stage 6 PR 4b-2): their masters still teach and their people still need the skill,
    // or a village whose farmers all work the fields would train none and ask other settlements for theirs.
    private void CountAway(GameState state)
    {
        int n = state.NextSettlementId; // arrays by settlement id
        int skills = _content.Skills.Count;
        int strata = _content.Strata.Count;
        if (_awaySkilled.Length < n * skills || _awayPeople.Length < n * strata)
        {
            _awaySkilled = new int[n * skills];
            _awayPeople = new int[n * strata];
        }

        Array.Clear(_awaySkilled);
        Array.Clear(_awayPeople);
        UnitBook book = state.Units;
        for (int i = 0; i < book.Count; i++)
        {
            Unit u = book[i];
            if (u.Gone || u.Role != UnitRole.Workers || u.Home < 0 || u.Home >= n || state.FindSettlement(new SettlementId(u.Home)) is not { } h || h.Owner != u.Owner)
            {
                continue;
            }

            for (int k = 0; k < skills && k < u.Skilled.Length; k++)
            {
                _awaySkilled[(u.Home * skills) + k] += u.Skilled[k];
            }

            for (int st = 0; st < strata && st < u.People.Length; st++)
            {
                _awayPeople[(u.Home * strata) + st] += u.People[st];
            }
        }
    }

    // Skilled people of the settlement, with those in its work groups.
    private int Have(SettlementState s, int skill)
    {
        int at = (s.Id.Value * _content.Skills.Count) + skill;
        return s.SkilledView[skill] + (at < _awaySkilled.Length ? _awaySkilled[at] : 0);
    }

    // People of the skill the settlement can use, with the people of its work groups.
    private int Need(SettlementState s, int skill)
    {
        int need = Skills.Needed(_content, s, skill);
        SkillDefinition def = _content.Skills[skill];
        int at = (s.Id.Value * _content.Strata.Count) + def.StratumId;
        int away = at < _awayPeople.Length ? _awayPeople[at] : 0;
        return away == 0 || _content.SkillLinks.ForRecipe(s.RecipeId) == skill ? need : need + (int)((long)away * def.JobsPercent / 100);
    }

    // For each civilization and skill it knows: is there a lived-in settlement that needs it and has nobody of it?
    private void CountMissing(GameState state)
    {
        int skills = _content.Skills.Count;
        int civs = state.Civilizations.Count;
        if (_missing.Length < civs * skills)
        {
            _missing = new bool[civs * skills];
        }

        Array.Clear(_missing, 0, civs * skills);
        foreach (SettlementState s in state.Settlements)
        {
            if (!s.IsActive || s.TotalPopulation() == 0)
            {
                continue;
            }

            int c = state.IndexOfCivilization(s.Owner);
            for (int k = 0; c >= 0 && k < skills && k < s.SkilledView.Length; k++)
            {
                if (Have(s, k) == 0 && s.TraineesView[k] == 0 && s.PopulationView[_content.Skills[k].StratumId] > 0)
                {
                    _missing[(c * skills) + k] = true;
                }
            }
        }
    }

    // Spare skilled people go to the nearest settlement of their civilization within the trade link distance that
    // needs them (ties: lowest id), with their place in the stratum.
    private void MoveSpare(GameState state, SettlementState from)
    {
        WorldState world = state.Worlds[from.World.Value];
        for (int k = 0; k < from.SkilledView.Length && k < _content.Skills.Count; k++)
        {
            int have = from.SkilledView[k]; // only those at home can move
            int spare = Math.Min(have, Have(from, k) - Need(from, k));
            if (have < _rules.MoveMinMasters || spare < _rules.MoveSpareAbove)
            {
                continue;
            }

            int going = Math.Max(1, spare * _rules.MoveSharePercent / 100);
            SettlementState? to = null;
            int best = int.MaxValue;
            int c = state.IndexOfCivilization(from.Owner);
            foreach (int i in _needy[(c * _content.Skills.Count) + k])
            {
                SettlementState s = state.Settlements[i];
                if (s == from || s.World != from.World || Need(s, k) - Have(s, k) - s.TraineesView[k] <= 0)
                {
                    continue;
                }

                int d = world.Geometry.Distance(from.Cell, s.Cell);
                if (d <= _content.Trade.LinkCells && d < best)
                {
                    best = d;
                    to = s;
                }
            }

            if (to is null)
            {
                continue;
            }

            going = Math.Min(going, Need(to, k) - Have(to, k) - to.TraineesView[k]);
            int stratum = _content.Skills[k].StratumId;
            from.Skilled[k] -= going;
            from.Population[stratum] -= going;
            to.Skilled[k] += going;
            to.Population[stratum] += going;
        }
    }

    private System.Collections.Generic.List<int>[] _needy = [];

    // For each civilization and skill, its lived-in settlements short of hands at the start of the moves, in id order.
    // A settlement that becomes short during the moves (more people came for another skill) waits for the next step.
    private void ListNeedy(GameState state)
    {
        int skills = _content.Skills.Count;
        int size = state.Civilizations.Count * skills;
        if (_needy.Length < size)
        {
            var grown = new System.Collections.Generic.List<int>[size];
            for (int n = 0; n < size; n++)
            {
                grown[n] = n < _needy.Length ? _needy[n] : [];
            }

            _needy = grown;
        }

        for (int n = 0; n < size; n++)
        {
            _needy[n].Clear();
        }

        for (int i = 0; i < state.Settlements.Count; i++)
        {
            SettlementState s = state.Settlements[i];
            int c = state.IndexOfCivilization(s.Owner);
            if (c < 0 || !s.IsActive || s.TotalPopulation() == 0)
            {
                continue;
            }

            for (int k = 0; k < skills && k < s.SkilledView.Length; k++)
            {
                if (Need(s, k) - Have(s, k) - s.TraineesView[k] > 0)
                {
                    _needy[(c * skills) + k].Add(i);
                }
            }
        }
    }

    // A civilization that knows a technology but lost its skill gets one master back per own step from a trade
    // partner that has enough of them.
    private void BringBack(GameState state)
    {
        int skills = _content.Skills.Count;
        int civs = state.Civilizations.Count;
        if (_total.Length < civs * skills)
        {
            _total = new long[civs * skills];
        }

        Array.Clear(_total, 0, civs * skills);
        foreach (SettlementState s in state.Settlements)
        {
            int c = state.IndexOfCivilization(s.Owner);
            for (int k = 0; c >= 0 && k < skills && k < s.SkilledView.Length; k++)
            {
                _total[(c * skills) + k] += s.SkilledView[k] + s.TraineesView[k];
            }
        }

        for (int c = 0; c < civs; c++)
        {
            CivilizationState civ = state.Civilizations[c];
            if (!civ.IsAlive || WorldClock.MonthsOf(state, civ.Id) == 0)
            {
                continue;
            }

            for (int k = 0; k < skills; k++)
            {
                if (_total[(c * skills) + k] > 0 || !civ.Knows(_content.Skills[k].TechnologyId) || !FromPartner(state, civ, k))
                {
                    continue;
                }

                Skills.Seed(_content, state, civ.Id, k, 1);
            }
        }
    }

    private bool FromPartner(GameState state, CivilizationState civ, int skill)
    {
        int skills = _content.Skills.Count;
        for (int o = 0; o < state.Civilizations.Count; o++)
        {
            CivilizationState other = state.Civilizations[o];
            if (other.Id != civ.Id && other.IsAlive && _total[(o * skills) + skill] >= _rules.MoveMinMasters
                && state.Treaties.Has(civ.Id, other.Id, TreatyKind.Trade))
            {
                return true;
            }
        }

        return false;
    }
}
