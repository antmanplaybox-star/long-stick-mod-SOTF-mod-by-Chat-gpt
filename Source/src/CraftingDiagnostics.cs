using LongStickSpear.Core;
using Sons.Crafting;
using Sons.Inventory;
using TheForest.Utils;

namespace LongStickSpear;

internal static class CraftingDiagnostics
{
    private static int _events;

    internal static bool HasLongStick(CraftingSystem system)
    {
        var ingredients = system?.CurrentIngredients;
        if (ingredients == null) return false;
        foreach (var ingredient in ingredients)
            if (ingredient != null && ingredient.ItemId == Rules.LongStick && ingredient.Count > 0) return true;
        return false;
    }

    internal static void Report(CraftingSystem system, string step, bool force = false)
    {
        if (!force && !HasLongStick(system)) return;
        if (_events++ >= 40 && !Config.DebugLogging.Value) return;
        var parts = new List<string>();
        if (system.CurrentIngredients != null)
            foreach (var ingredient in system.CurrentIngredients)
                if (ingredient != null) parts.Add($"{ingredient.ItemId}x{ingredient.Count}");
        var available = new List<string>();
        if (system._availableRecipes != null)
            foreach (var recipe in system._availableRecipes)
                if (recipe != null) available.Add(recipe.name + ":" + recipe.AnimationStateName);
        var nativeMatch = system.HasSatisfiedRecipeWithCurrentIngredients(out var matched);
        var longCount = LocalPlayer.Inventory?.AmountOf(Rules.LongStick, false, false) ?? -1;
        var spearCount = LocalPlayer.Inventory?.AmountOf(Rules.Spear, false, false) ?? -1;
        Mod.Info($"CRAFT {step}: mat=[{string.Join(",", parts)}], available=[{string.Join(",", available)}], " +
            $"nativeMatch={nativeMatch}, selected='{matched?.name ?? "none"}', carriedLongSticks={longCount}, carriedSpears={spearCount}, initialized={system._isInitialized}.");
    }

    internal static void BeforeCombine(CraftingSystem system)
    {
        if (!HasLongStick(system)) return;
        CraftingAndInventory.Ensure(system);
        CraftingAndInventory.Refresh(system, false);
        Report(system, "before cog click");
    }

    internal static void OnAdded(CraftingSystem system, ItemInstance item)
    {
        if (item?.Data?.Id != Rules.LongStick && !HasLongStick(system)) return;
        CraftingAndInventory.Refresh(system, true);
        Report(system, "ingredient added", true);
    }

    internal static void Completed(CraftingSystem system, CraftingRecipe recipe, string phase)
    {
        if (recipe == null || recipe.name != Rules.RecipeName) return;
        Report(system, phase, true);
    }

    internal static void Reset() => _events = 0;
}
