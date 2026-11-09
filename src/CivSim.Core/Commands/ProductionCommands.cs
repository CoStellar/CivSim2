using System;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.State;

namespace CivSim.Core.Commands;

/// <summary>
/// Chooses the recipe a settlement's artisans work (GDD 4.4, stage 3e), or <see cref="SettlementState.NoRecipe"/> to
/// stop. A recipe is allowed when a technology the civilization knows unlocks it (stage 3f).
/// </summary>
public sealed record SetRecipeCommand(GameContent Content, SettlementId Settlement, int RecipeId) : ICommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Economy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CommandResult own = SettlementRules.CheckOwn(state, civ, Settlement, out SettlementState? s);
        if (!own.Success)
        {
            return own;
        }

        if (!s!.IsActive)
        {
            return CommandResult.Fail("Nobody lives there.");
        }

        if (RecipeId == SettlementState.NoRecipe)
        {
            return CommandResult.Ok;
        }

        if (RecipeId < 0 || RecipeId >= Content.Recipes.Count)
        {
            return CommandResult.Fail("No such recipe.");
        }

        return Research.Technologies.KnowsRecipe(Content, state.FindCivilization(civ)!, RecipeId)
            ? CommandResult.Ok
            : CommandResult.Fail("No technology the civilization knows unlocks that recipe.");
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.FindSettlement(Settlement)!.RecipeId = RecipeId;
    }
}
