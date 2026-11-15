using LongStickSpear.Core;
using Sons.Crafting;
using Sons.Inventory;
using Sons.Items.Core;
using SonsSdk;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LongStickSpear;

internal static class CraftingAndInventory
{
    private static InventoryLayoutItemGroup? _inventoryGroup;
    private static IngredientLayoutItemGroup? _ingredientGroup;
    private static bool _configuring;

    internal static bool Ensure(CraftingSystem system)
    {
        if (_configuring) return false;
        _configuring = true;
        try
        {
        var item = LongStickItem.Register();
        EnsureRecipe(system);
        EnsureIngredientGroup(system, item);
        EnsureInventoryGroup(item);
        var cachedGroups = system._ingredientLayoutItemGroups;
        if (cachedGroups != null && _ingredientGroup != null)
        {
            var present = false;
            foreach (var g in cachedGroups)
                if (g != null && g.Pointer == _ingredientGroup.Pointer) { present = true; break; }
            if (!present) cachedGroups.Add(_ingredientGroup);
        }
        return _inventoryGroup != null && _inventoryGroup && _ingredientGroup != null && _ingredientGroup;
        }
        finally { _configuring = false; }
    }

    private static void EnsureRecipe(CraftingSystem system)
    {
        var recipes = system._recipeDatabase?._recipes ?? throw new InvalidOperationException("Recipe database is not ready.");
        foreach (var recipe in recipes)
            if (recipe != null && recipe.name == Rules.RecipeName) return;
        CraftingRecipe? vanilla = null;
        foreach (var recipe in recipes)
        {
            if (recipe?._resultingItems == null) continue;
            var makesSpear = false;
            foreach (var result in recipe._resultingItems)
                if (result.Id == Rules.Spear) { makesSpear = true; break; }
            if (makesSpear && recipe.HasIngredient(Rules.Stick)) { vanilla = recipe; break; }
        }
        if (vanilla == null) throw new InvalidOperationException("The original crafted-spear recipe is not ready.");
        // Keep the custom recipe/ingredients but use the original spear's
        // animation state, including its native reveal/completion events.
        var animation = vanilla.AnimationStateName;
        if (string.IsNullOrWhiteSpace(animation)) throw new InvalidOperationException("The original spear crafting animation is not ready.");
        var builder = new ItemTools.RecipeBuilder();
        foreach (var ingredient in Rules.SpearIngredients)
            builder.AddIngredient(ingredient.ItemId, ingredient.Count, ingredient.Reusable);
        var extra = builder.AddResult(Rules.Spear).Animation(animation).BuildRecipe();
        extra.name = Rules.RecipeName;
        extra._craftCompleteAudioEvent = vanilla._craftCompleteAudioEvent;
        extra._duringCraftJingleAudioEvent = vanilla._duringCraftJingleAudioEvent;
        extra._generatedResultingItems = new Il2CppSystem.Collections.Generic.List<CraftingRecipe.ResultingItem>();
        extra._useContainerDataForResultingItems = false;
        extra._containerItemData = null;
        extra._weaponMod = null;
        extra._recipeType = CraftingRecipe.Type.CraftNewItem;
        extra._forceNewItemInstance = true;
        extra._hasCustomName = false;
        extra.GenerateResultingItemList();
        recipes.Add(extra);
        Mod.Info($"Added Long Stick + reusable knife recipe: sourceRecipe='{vanilla.name}', animation='{extra.AnimationStateName}', generatedOutputs={extra._generatedResultingItems?.Count ?? 0}. The original spear recipe remains available.");
    }

    internal static void Refresh(CraftingSystem system, bool validateSlots)
    {
        if (_configuring || system == null || !system || !system._isInitialized) return;
        system.RefreshAvailableRecipes(true);
        if (!validateSlots || _inventoryGroup == null || !_inventoryGroup) return;
        foreach (var slot in _inventoryGroup._layoutItems)
            if (slot != null && slot && slot.gameObject.activeInHierarchy) slot.ValidateItemForCrafting(0);
    }

    internal static void PrepareAddToMat(InventoryLayoutItemGroup group)
    {
        if (group._itemId != Rules.LongStick) return;
        group._itemData = LongStickItem.Register();
        var system = SonsSdk.GameState.CraftingSystem;
        Ensure(system);
        Refresh(system, true);
    }

    private static void EnsureInventoryGroup(ItemData item)
    {
        if (_inventoryGroup != null && _inventoryGroup) return;
        var source = ItemTools.GetInventoryLayoutItemGroup(Rules.Stick);
        if (source == null || !source) throw new InvalidOperationException("The vanilla stick inventory group is not ready.");
        var existing = ItemTools.GetInventoryLayoutItemGroup(Rules.LongStick);
        if (existing != null && existing) { _inventoryGroup = existing; return; }
        var staging = new GameObject("LongStickSpear_LayoutStaging");
        staging.SetActive(false);
        GameObject? clone = null;
        try
        {
            clone = Object.Instantiate(source.gameObject, staging.transform, false);
            clone.name = "LongStickSpear_InventoryLayout";
            var group = clone.GetComponent<InventoryLayoutItemGroup>();
            group._itemId = Rules.LongStick;
            group._itemData = item;
            group._startDisabled = false;
            group._isInfiniteGroupCount = false;
            group._isOneToOneCollection = true;
            group._layoutItems = new Il2CppSystem.Collections.Generic.List<InventoryLayoutItem>();
            foreach (var layout in clone.GetComponentsInChildren<InventoryLayoutItem>(true))
            {
                layout._ItemInstance_k__BackingField = null;
                layout._isInitialized = false;
                layout.gameObject.SetActive(false);
                group._layoutItems.Add(layout);
            }
            if (group._layoutItems.Count == 0) throw new InvalidOperationException("No stick inventory layout slots were cloned.");
            ConfigureModels(clone);
            LongStickVisuals.Attach(clone);
            clone.transform.SetParent(source.transform.parent, false);
            var x = FiniteOffset(Config.InventoryOffsetX.Value, .26f);
            var z = FiniteOffset(Config.InventoryOffsetZ.Value, .14f);
            clone.transform.localPosition = source.transform.localPosition + new Vector3(x, 0, z);
            clone.SetActive(true);
            _inventoryGroup = group;
            Mod.Info($"Long-stick inventory stack created beside the normal sticks ({group._layoutItems.Count} visual slots).");
        }
        catch { if (clone != null) Object.Destroy(clone); throw; }
        finally { Object.Destroy(staging); }
    }

    private static void EnsureIngredientGroup(CraftingSystem system, ItemData item)
    {
        if (_ingredientGroup != null && _ingredientGroup) return;
        var root = system._ingredientLayoutGroups;
        if (root == null) throw new InvalidOperationException("The crafting mat is not ready.");
        var source = root.GetComponentsInChildren<IngredientLayoutItemGroup>(true).FirstOrDefault(g => g._itemId == Rules.Stick);
        if (source == null) throw new InvalidOperationException("The vanilla stick ingredient layout is not ready.");
        var existing = root.GetComponentsInChildren<IngredientLayoutItemGroup>(true).FirstOrDefault(g => g._itemId == Rules.LongStick);
        if (existing != null) { _ingredientGroup = existing; return; }
        var staging = new GameObject("LongStickSpear_IngredientStaging");
        staging.SetActive(false);
        GameObject? clone = null;
        try
        {
            clone = Object.Instantiate(source.gameObject, staging.transform, false);
            clone.name = "LongStickSpear_IngredientLayout";
            var group = clone.GetComponent<IngredientLayoutItemGroup>();
            group._itemId = Rules.LongStick;
            group._itemData = item;
            group._isOneToOneCollection = true;
            // Preserve the source layout animation: it controls placing the
            // ingredient on the mat, independently of the craft animation.
            group._layoutItems = new Il2CppSystem.Collections.Generic.List<IngredientLayoutItem>();
            foreach (var layout in clone.GetComponentsInChildren<IngredientLayoutItem>(true))
            {
                layout._ItemInstance_k__BackingField = null;
                layout.gameObject.SetActive(false);
                group._layoutItems.Add(layout);
            }
            if (group._layoutItems.Count == 0) throw new InvalidOperationException("No stick crafting layout slots were cloned.");
            ConfigureModels(clone);
            LongStickVisuals.Attach(clone);
            clone.transform.SetParent(source.transform.parent, false);
            clone.transform.localPosition = source.transform.localPosition;
            clone.SetActive(true);
            _ingredientGroup = group;
            Mod.Info("Long-stick crafting-mat layout created.");
        }
        catch { if (clone != null) Object.Destroy(clone); throw; }
        finally { Object.Destroy(staging); }
    }

    private static void ConfigureModels(GameObject root)
    {
        var models = root.GetComponentsInChildren<ItemRenderable>(true);
        if (models.Length == 0) throw new InvalidOperationException("No ItemRenderable was found in the vanilla stick layout.");
        foreach (var model in models)
        {
            // A loaded inventory can already contain an instantiated model.
            // Remove only cloned descendants; never touch an external asset.
            var cached = model._cachedLoadedObject;
            if (cached != null && cached && cached.transform.IsChildOf(root.transform)) Object.Destroy(cached);
            LongStickItem.ConfigureRenderable(model);
        }
    }

    private static float FiniteOffset(float value, float fallback) => float.IsFinite(value) ? Mathf.Clamp(value, -2, 2) : fallback;
    internal static void Reset() { _inventoryGroup = null; _ingredientGroup = null; _configuring = false; }
}
