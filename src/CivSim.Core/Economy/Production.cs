using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Economy;

/// <summary>
/// Shares goods inside each trade network (GDD 4, 4.5; stage 3e), before production and eating, in the default order
/// of priorities: (1) food for the people, each settlement keeping <see cref="TradeRules.FoodReserveYears"/> of its
/// needs; (2) the goods of higher needs (tools, clothing); (3) the inputs of the recipes being worked. Upkeep and
/// construction (steps 2 and 3 of GDD 4.5) have nothing to share yet. Goods come from the nearest settlement with a
/// surplus first and lose <see cref="TradeRules.LossPercentPerCell"/> per cell on the way.
/// </summary>
public sealed class TradeSystem(GameContent content) : ITurnSystem
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));

    // Buffers reused from turn to turn (no allocations once they are big enough).
    private int[] _dist = [];
    private int[] _order = [];
    private long[] _keys = [];
    private long[] _target = [];
    private long[] _keep = [];
    private long[] _food = [];
    private long[] _need = [];
    private long[] _input = [];

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Economy;

    /// <summary>Goods that arrived through the networks in the last turn, hundredths (for reports).</summary>
    public long LastMovedHundredths { get; private set; }

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        int months = state.MonthsPerTurn(_content);
        int goods = _content.Goods.Count;
        long moved = 0;
        foreach (List<int> members in TradeNetworks.Build(_content, state))
        {
            int n = members.Count;
            if (n < 2)
            {
                continue;
            }

            Prepare(state, members, months);

            // (1) Food: this turn's needs plus the reserve.
            for (int k = 0; k < n; k++)
            {
                _target[k] = (_food[k] * months / 12) + (_food[k] * _content.Trade.FoodReserveYears.Hundredths / 100);
                _keep[k] = _target[k];
            }

            moved += Share(state, members, food: true, good: -1);

            // (2) Higher needs, keeping what the donor's own recipe needs; (3) recipe inputs.
            for (int g = 0; g < goods; g++)
            {
                if (_content.Goods[g].IsFood)
                {
                    continue;
                }

                bool any = false;
                for (int k = 0; k < n; k++)
                {
                    long needs = _need[(k * goods) + g];
                    _target[k] = (needs * months / 12) + needs;
                    _keep[k] = _target[k] + _input[(k * goods) + g];
                    any |= _keep[k] > 0;
                }

                if (!any)
                {
                    continue;
                }

                moved += Share(state, members, food: false, good: g);
                Array.Copy(_keep, _target, n);
                moved += Share(state, members, food: false, good: g);
            }
        }

        LastMovedHundredths = moved;
    }

    // Distances, donor order and every member's demand, once per network.
    private void Prepare(GameState state, List<int> members, int months)
    {
        int n = members.Count;
        int goods = _content.Goods.Count;
        Grow(ref _dist, n * n);
        Grow(ref _order, n * n);
        Grow(ref _keys, n);
        Grow(ref _target, n);
        Grow(ref _keep, n);
        Grow(ref _food, n);
        Grow(ref _need, n * goods);
        Grow(ref _input, n * goods);
        WorldState world = state.Worlds[state.Settlements[members[0]].World.Value];
        for (int a = 0; a < n; a++)
        {
            _dist[(a * n) + a] = 0;
            for (int b = a + 1; b < n; b++)
            {
                int d = world.Geometry.Distance(state.Settlements[members[a]].Cell, state.Settlements[members[b]].Cell);
                _dist[(a * n) + b] = d;
                _dist[(b * n) + a] = d;
            }
        }

        // Row r: the other members by distance from r, ties in member order (the key carries both).
        for (int r = 0; r < n; r++)
        {
            int c = 0;
            for (int k = 0; k < n; k++)
            {
                if (k != r)
                {
                    _keys[c] = ((long)_dist[(r * n) + k] << 32) | (uint)k;
                    c++;
                }
            }

            Array.Sort(_keys, 0, c);
            for (int k = 0; k < c; k++)
            {
                _order[(r * n) + k] = (int)(_keys[k] & 0xFFFF_FFFF);
            }
        }

        Array.Clear(_need, 0, n * goods);
        Array.Clear(_input, 0, n * goods);
        for (int k = 0; k < n; k++)
        {
            SettlementState s = state.Settlements[members[k]];
            long food = 0;
            ReadOnlySpan<int> people = s.PopulationView;
            for (int st = 0; st < people.Length && st < _content.Strata.Count; st++)
            {
                if (people[st] == 0)
                {
                    continue;
                }

                for (int iNeed = 0; iNeed < _content.Strata[st].Needs.Count; iNeed++)
                {
                    NeedDefinition need = _content.Strata[st].Needs[iNeed];
                    long perYear = need.PerYear.Hundredths * people[st];
                    if (need.AnyOfCategory)
                    {
                        food += need.Category == GoodCategory.Food ? perYear : 0;
                    }
                    else
                    {
                        _need[(k * goods) + need.GoodId] += perYear;
                    }
                }
            }

            _food[k] = food;
            if (s.RecipeId >= 0 && s.RecipeId < _content.Recipes.Count)
            {
                RecipeDefinition recipe = _content.Recipes[s.RecipeId];
                long runs = Demand.Runs(_content, s, recipe, months);
                for (int iQ = 0; iQ < recipe.Inputs.Count; iQ++)
                {
                    GoodQuantity q = recipe.Inputs[iQ];
                    _input[(k * goods) + q.GoodId] += q.Amount.Hundredths * runs / 100;
                }
            }
        }
    }

    // Brings every member up to _target[k] from members above _keep[k], nearest first.
    private long Share(GameState state, List<int> members, bool food, int good)
    {
        long arrived = 0;
        int n = members.Count;
        for (int r = 0; r < n; r++)
        {
            SettlementState receiver = state.Settlements[members[r]];
            long missing = _target[r] - Have(receiver, food, good);
            if (missing <= 0)
            {
                continue;
            }

            for (int j = 0; j < n - 1; j++)
            {
                int d = _order[(r * n) + j];
                SettlementState donor = state.Settlements[members[d]];
                long spare = Have(donor, food, good) - _keep[d];
                if (spare <= 0)
                {
                    continue;
                }

                int percent = TradeNetworks.ArrivingPercent(_content, _dist[(r * n) + d]);
                long send = Math.Min(spare, ((missing * 100) + percent - 1) / percent);
                long got = Move(donor, receiver, send, percent, food, good);
                arrived += got;
                missing -= got;
                if (missing <= 0)
                {
                    break;
                }
            }
        }

        return arrived;
    }

    private long Have(SettlementState s, bool food, int good)
    {
        if (!food)
        {
            return s.StockView[good].Hundredths;
        }

        long sum = 0;
        ReadOnlySpan<Amount> stock = s.StockView;
        for (int g = 0; g < stock.Length; g++)
        {
            sum += _content.Goods[g].IsFood ? stock[g].Hundredths : 0;
        }

        return sum;
    }

    // Takes `send` hundredths from the donor (food: good by good in id order) and delivers `percent` of it.
    private long Move(SettlementState from, SettlementState to, long send, int percent, bool food, int good)
    {
        long delivered = 0;
        Span<Amount> stock = from.Stock;
        for (int g = 0; g < stock.Length && send > 0; g++)
        {
            if (food ? !_content.Goods[g].IsFood : g != good)
            {
                continue;
            }

            long take = Math.Min(send, stock[g].Hundredths);
            if (take <= 0)
            {
                continue;
            }

            stock[g] -= Amount.FromHundredths(take);
            send -= take;
            long arrive = take * percent / 100;
            to.Add(g, Amount.FromHundredths(arrive));
            delivered += arrive;
        }

        return delivered;
    }

    private static void Grow<T>(ref T[] buffer, int size)
    {
        if (buffer.Length < size)
        {
            buffer = new T[Math.Max(size, buffer.Length * 2)];
        }
    }
}

/// <summary>
/// Production (GDD 4.4, CORE simplified: one-step recipes; stage 3e). The artisans of a settlement work its recipe:
/// a recipe needs its workers for its years, so runs per turn = artisans × turn years / (workers × years), as far as
/// the inputs in the warehouse go. Food used as an input never cuts into this turn's food for the people (GDD 4.5:
/// feeding the people comes before production).
/// </summary>
public sealed class ProductionSystem(GameContent content) : ITurnSystem
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Economy;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        int months = state.MonthsPerTurn(_content);
        for (int iS = 0; iS < state.Settlements.Count; iS++)
        {
            SettlementState s = state.Settlements[iS];
            s.RunsMade = 0;
            if (!s.IsActive || s.RecipeId < 0 || s.RecipeId >= _content.Recipes.Count)
            {
                continue;
            }

            RecipeDefinition recipe = _content.Recipes[s.RecipeId];
            long runs = Demand.Runs(_content, s, recipe, months);
            long foodFree = FoodStock(s) - (Demand.FoodPerYear(_content, s) * months / 12);
            for (int iQ = 0; iQ < recipe.Inputs.Count; iQ++)
            {
                GoodQuantity q = recipe.Inputs[iQ];
                long have = s.StockView[q.GoodId].Hundredths;
                if (_content.Goods[q.GoodId].IsFood)
                {
                    have = Math.Min(have, Math.Max(0, foodFree));
                }

                runs = Math.Min(runs, have * 100 / Math.Max(1, q.Amount.Hundredths));
            }

            if (runs <= 0)
            {
                continue;
            }

            for (int iQ = 0; iQ < recipe.Inputs.Count; iQ++)
            {
                GoodQuantity q = recipe.Inputs[iQ];
                s.TryTake(q.GoodId, Amount.FromHundredths(q.Amount.Hundredths * runs / 100));
            }

            for (int iQ = 0; iQ < recipe.Outputs.Count; iQ++)
            {
                GoodQuantity q = recipe.Outputs[iQ];
                s.Add(q.GoodId, Amount.FromHundredths(q.Amount.Hundredths * runs / 100));
            }

            s.RunsMade = runs;
        }
    }

    private long FoodStock(SettlementState s)
    {
        long sum = 0;
        ReadOnlySpan<Amount> stock = s.StockView;
        for (int g = 0; g < stock.Length; g++)
        {
            sum += _content.Goods[g].IsFood ? stock[g].Hundredths : 0;
        }

        return sum;
    }
}
