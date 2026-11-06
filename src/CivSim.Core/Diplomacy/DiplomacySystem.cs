using System;
using CivSim.Core.Content;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Diplomacy;

/// <summary>
/// State phase of diplomacy (GDD 9.1, 11.1 phase 9; stage 4d): treaties of civilizations that died out end, the
/// truce after a war runs out, proposals nobody answered lapse and old refusals are forgotten, and civilizations at
/// war with the same enemy remember it (GDD 8.5: common enemy, while it lasts).
/// </summary>
public sealed class DiplomacySystem(GameContent content) : ITurnSystem
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private ulong[] _enemies = [];
    private int[] _indexOfId = [];

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.State;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        TreatyBook book = state.Treaties;
        long month = state.Month;
        book.RemoveAll(t => !Alive(state, t.A) || !Alive(state, t.B) || (t.UntilMonth > 0 && month >= t.UntilMonth));
        long retry = _content.Treaties.RetryYears * 12L;
        book.RemoveProposals(p => !Alive(state, p.From) || !Alive(state, p.To) || (p.Status == ProposalStatus.Declined && month >= p.Month + retry));
        for (int k = 0; k < book.Proposals.Count; k++)
        {
            TreatyProposal p = book.Proposals[k];
            if (p.Status == ProposalStatus.Pending && p.Month < month)
            {
                book.SetProposal(k, p with { Status = ProposalStatus.Declined, Month = month });
            }
        }

        foreach (CivilizationState civ in state.Civilizations)
        {
            foreach (Relation r in civ.Relations)
            {
                if (!book.Has(civ.Id, r.Other, TreatyKind.Trade))
                {
                    Contacts.End(r, RelationEvent.ActiveTrade, month);
                }
            }
        }

        CommonEnemies(state, month);
    }

    // GDD 8.5: we fight the same enemy. Civilizations are indexed by bit (at most 64 of them, GDD 3).
    private void CommonEnemies(GameState state, long month)
    {
        int n = state.Civilizations.Count;
        if (n < 3 || n > 64)
        {
            return;
        }

        if (_enemies.Length != n)
        {
            _enemies = new ulong[n];
        }

        int maxId = 0;
        foreach (CivilizationState c in state.Civilizations)
        {
            maxId = Math.Max(maxId, c.Id.Value);
        }

        if (_indexOfId.Length <= maxId)
        {
            _indexOfId = new int[maxId + 1];
        }

        Array.Fill(_indexOfId, -1);
        for (int k = 0; k < n; k++)
        {
            _indexOfId[state.Civilizations[k].Id.Value] = k;
        }

        Array.Clear(_enemies);
        foreach (Treaty t in state.Treaties.All)
        {
            if (t.Kind == TreatyKind.War)
            {
                int a = _indexOfId[t.A.Value];
                int b = _indexOfId[t.B.Value];
                _enemies[a] |= 1UL << b;
                _enemies[b] |= 1UL << a;
            }
        }

        int months = state.MonthsPerTurn(_content);
        for (int x = 0; x < n; x++)
        {
            CivilizationState civ = state.Civilizations[x];
            foreach (Relation r in civ.Relations)
            {
                int z = r.Other.Value < _indexOfId.Length ? _indexOfId[r.Other.Value] : -1;
                bool common = civ.IsAlive && r.HasMet && z >= 0 && (_enemies[x] & _enemies[z]) != 0 && (_enemies[x] & (1UL << z)) == 0;
                if (common)
                {
                    Contacts.Sustain(_content.Relations, r, RelationEvent.CommonEnemy, month, months);
                }
                else
                {
                    Contacts.End(r, RelationEvent.CommonEnemy, month);
                }
            }
        }
    }

    private static bool Alive(GameState state, CivId id) => state.FindCivilization(id) is { IsAlive: true };
}
