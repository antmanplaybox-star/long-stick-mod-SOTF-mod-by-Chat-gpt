using Bolt;
using Endnight.Types;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using LongStickSpear.Core;
using Sons.Crafting;
using Sons.Crafting.Structures;
using Sons.Gameplay;
using Sons.Gameplay.GameSetup;
using Sons.Inventory;
using Sons.Items.Core;
using Sons.Save;
using Sons.Weapon;
using SonsSdk;
using SonsSdk.Networking;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LongStickSpear;

internal static class LongStickStorage
{
    private static bool _registeredType, _building;
    private static StructureRecipe? _recipe;
    private static GameObject? _templates;
    private static bool _reported;
    private static int _sourceRecipeId = -1;

    internal static void Initialize()
    {
        if (_registeredType) return;
        ClassInjector.RegisterTypeInIl2Cpp<LongStickStorageHolder>();
        _registeredType = true;
    }

    internal static bool Ensure()
    {
        if (_building || GameSetupManager.GetSaveGameType() != SaveGameType.SinglePlayer) return false;
        var manager = SingletonBehaviour<ScrewStructureManager>._instance;
        var crafting = SingletonBehaviour<StructureCraftingSystem>._instance;
        if (manager == null || !manager || manager._database?._recipes == null || crafting == null || !crafting || crafting._recipeDatabase == null)
            return false;
        if (ItemDatabaseManager._itemsCache == null || ItemDatabaseManager._instance == null) return false;
        _building = true;
        try
        {
            Initialize();
            LongStickItem.Register();
            if (_recipe == null || !_recipe)
            {
                var source = FindSource(manager._database);
                if (source == null) return false;
                EnsureFreeRecipeId(manager._database);
                EnsureFreeRecipeId(crafting._recipeDatabase);
                EnsureFreePrefabId(StorageRules.BuiltPrefabId, null);
                EnsureFreePrefabId(StorageRules.GhostPrefabId, null);
                Create(source);
            }
            AddRecipe(manager._database, _recipe!);
            AddRecipe(crafting._recipeDatabase, _recipe!);
            RegisterEntity(_recipe!._builtPrefab, StorageRules.BuiltPrefabId);
            RegisterEntity(_recipe!._structureNodePrefab, StorageRules.GhostPrefabId);
            var hasPage = EnsureBookPage();
            if (hasPage && !_reported)
            {
                _reported = true;
                Mod.Info($"BOOK: {StorageRules.Name} added. Separate native blueprint {StorageRules.RecipeId}; total capacity={StorageRules.Capacity} Long Sticks; same holder model and build ingredients as the source.");
            }
            return hasPage;
        }
        finally { _building = false; }
    }

    private static StructureRecipe? FindSource(StructureRecipeDatabase database)
    {
        StructureRecipe? best = null; var bestScore = -1;
        foreach (var candidate in database._recipes)
        {
            if (candidate == null || candidate.Id == StorageRules.RecipeId || candidate._builtPrefab == null || candidate._structureNodePrefab == null) continue;
            var groups = candidate._builtPrefab.GetComponentsInChildren<InWorldLayoutItemGroup>(true);
            var hasStick = false; var otherItems = false;
            foreach (var group in groups)
            {
                if (group._itemId == Rules.Stick) hasStick = true;
                else if (group._itemId > 0) otherItems = true;
            }
            if (!hasStick || otherItems || candidate._builtPrefab.GetComponentInChildren<ScrewStructureWithStorage>(true) == null) continue;
            var score = System.Math.Max(StorageRules.SourceScore(candidate.name), StorageRules.SourceScore(candidate.DisplayName));
            if (score > bestScore) { bestScore = score; best = candidate; }
        }
        return best;
    }

    private static void Create(StructureRecipe source)
    {
        var holder = new GameObject("LongStickSpear_StoragePrefabs");
        holder.SetActive(false); Object.DontDestroyOnLoad(holder);
        StructureRecipe? recipe = null;
        try
        {
            recipe = Object.Instantiate(source);
            recipe.name = "LongStickSpear_StorageRecipe";
            recipe._id = StorageRules.RecipeId;
            recipe._displayName = StorageRules.Name + " (20)";
            recipe._localizationId = "LONG_STICK_SPEAR_STORAGE_RECIPE";
            recipe._isDiscovered = true;
            recipe._isDiscoveredInCreative = true;
            recipe._boltPrefabId = StorageRules.GhostPrefabId;
            recipe._relocateRecipeOverride = null;
            recipe._dynamicParentRecipeOverride = null;
            recipe._screwParentRecipeOverride = null;
            recipe._freeformParentRecipeOverride = null;
            recipe._ingredients = new Il2CppSystem.Collections.Generic.List<StructureCraftingRecipeIngredient>();
            foreach (var ingredient in source._ingredients)
                recipe._ingredients.Add(new StructureCraftingRecipeIngredient { ItemId = ingredient.ItemId, Count = ingredient.Count });

            var built = Object.Instantiate(source._builtPrefab, holder.transform, false);
            built.name = "LongStickSpear_StorageBuilt";
            var ghost = Object.Instantiate(source._structureNodePrefab, holder.transform, false);
            ghost.name = "LongStickSpear_StorageGhost";
            var structure = built.GetComponentInChildren<ScrewStructureWithStorage>(true)
                ?? throw new InvalidOperationException("The cloned holder has no native storage structure.");
            structure._recipe = recipe;
            var node = ghost.GetComponent<StructureCraftingNode>()
                ?? throw new InvalidOperationException("The cloned holder has no native construction node.");
            node._recipe = recipe;
            recipe._builtPrefab = built;
            recipe._structureNodePrefab = ghost;
            SetEntityId(built, StorageRules.BuiltPrefabId);
            SetEntityId(ghost, StorageRules.GhostPrefabId);
            var marker = built.AddComponent<LongStickStorageHolder>();
            marker.Apply();
            built.SetActive(true); ghost.SetActive(true); // Parent remains inactive.
            recipe.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _recipe = recipe; _templates = holder; _sourceRecipeId = source.Id;
            Mod.Info($"STORAGE: cloned '{source.name}' (recipe {source.Id}) into a new 20-capacity holder. Vanilla holder data is untouched.");
        }
        catch
        {
            Object.Destroy(holder);
            if (recipe != null) Object.Destroy(recipe);
            throw;
        }
    }

    private static void SetEntityId(GameObject root, int id)
    {
        var entity = root.GetComponent<BoltEntity>() ?? throw new InvalidOperationException("Storage prefab has no BoltEntity.");
        entity._prefabId = id;
        entity._sceneGuid = "00000000-0000-0000-0000-000000000000";
        entity._sceneObjectAutoAttach = false;
    }

    private static void RegisterEntity(GameObject prefab, int id)
    {
        EnsureFreePrefabId(id, prefab);
        EntityManager.RegisterPrefab(prefab);
    }

    private static void EnsureFreePrefabId(int id, GameObject? expected)
    {
        var lookup = PrefabDatabase._lookup;
        if (lookup == null || !lookup.TryGetValue(new PrefabId(id), out var existing) || existing == null || !existing) return;
        if (expected == null || existing.Pointer != expected.Pointer)
            throw new InvalidOperationException($"Prefab ID {id} is already registered. The existing prefab was not overwritten.");
    }

    private static void EnsureFreeRecipeId(StructureRecipeDatabase database)
    {
        foreach (var entry in database._recipes)
            if (entry != null && entry.Id == StorageRules.RecipeId && (_recipe == null || entry.Pointer != _recipe.Pointer))
                throw new InvalidOperationException($"Structure recipe ID {StorageRules.RecipeId} is already used. The existing recipe was not overwritten.");
    }

    private static void AddRecipe(StructureRecipeDatabase database, StructureRecipe recipe)
    {
        EnsureFreeRecipeId(database);
        foreach (var entry in database._recipes)
            if (entry != null && entry.Id == recipe.Id) return;
        database._recipes.Add(recipe);
    }

    private static bool EnsureBookPage()
    {
        if (_recipe == null) return false;
        var prefab = ItemTools.GetHeldPrefab(ItemTools.Identifiers.BlueprintBook);
        var book = prefab?.GetComponent<BlueprintBookController>();
        var pages = book?._pages?._pages;
        if (pages == null) return false;
        foreach (var page in pages)
            if (page?._topRecipe?.Id == StorageRules.RecipeId || page?._bottomRecipe?.Id == StorageRules.RecipeId) return true;

        BlueprintBookPageData? sourcePage = null; var isTop = true;
        foreach (var page in pages)
        {
            if (IsSourcePageRecipe(page?._topRecipe)) { sourcePage = page; isTop = true; break; }
            if (IsSourcePageRecipe(page?._bottomRecipe)) { sourcePage = page; isTop = false; break; }
        }
        if (sourcePage?._pageImage == null) throw new InvalidOperationException("The original stick holder's book illustration was not found.");
        StorageBookLabels.Register(_recipe);
        _recipe._recipePageId = pages.Count;
        var addedPage = new BlueprintBookPageData {
            _topRecipe = isTop ? _recipe : null, _bottomRecipe = isTop ? null : _recipe, _pageImage = sourcePage._pageImage,
            _pageTitleLocalizationId = StorageRules.PageKey
        };
        pages.Add(addedPage);
        StorageBookArt.Request(addedPage, sourcePage._pageImage, isTop);
        return true;
    }

    private static bool IsSourcePageRecipe(StructureRecipe? recipe)
    {
        return recipe != null && recipe.Id == _sourceRecipeId;
    }

    internal static LongStickStorageHolder? HolderFor(Component component) =>
        _registeredType && component != null && component ? component.GetComponentInParent<LongStickStorageHolder>(true) : null;

    internal static bool AllowDeposit(ItemInstanceManager manager, int itemId, int count)
    {
        var holder = HolderFor(manager);
        if (holder == null) return true;
        holder.Apply();
        var quota = holder.CapacityFor(manager);
        return StorageRules.CanDeposit(itemId, count, manager.GetItemCount(Rules.LongStick), quota);
    }

    internal static void WorldExited() { _reported = false; StorageBookArt.Pause(); }
}

public sealed class LongStickStorageHolder : MonoBehaviour
{
    private bool _applying;
    public LongStickStorageHolder(IntPtr pointer) : base(pointer) { }
    public void Awake() => ApplySafely();
    public void OnEnable() => ApplySafely();
    public void Start() => ApplySafely();

    [HideFromIl2Cpp]
    private void ApplySafely()
    {
        try { Apply(); }
        catch (Exception ex) { Mod.Warn("Long-stick holder setup: " + ex); }
    }

    [HideFromIl2Cpp]
    public void Apply()
    {
        if (_applying) return;
        _applying = true;
        try
        {
            var data = LongStickItem.Data;
            if (data == null) return;
            var groups = GetComponentsInChildren<InWorldLayoutItemGroup>(true).Where(Owns).ToArray();
            var storages = ResolveSlots(groups);
            if (groups.Length == 0 || storages.Count == 0) throw new InvalidOperationException("The holder has no native storage layout.");
            foreach (var group in groups)
            {
                if (group._itemId != Rules.Stick && group._itemId != Rules.LongStick) continue;
                var storage = group._storage;
                var index = storages.FindIndex(s => s.Pointer == storage.Pointer);
                var capacity = StorageRules.SlotCapacity(index, storages.Count);
                group._storage = storage;
                group._itemId = Rules.LongStick;
                group._itemData = data;
                group._maxAmount = capacity;
                group._isOneToOneCollection = true;
                group._transferAmount = 1;
                group._forceEquip = false;
                // Style the stored contents only. The holder's frame may be on
                // the same GameObject as the layout group and must not stretch.
                foreach (var slot in group.GetComponentsInChildren<InWorldLayoutItem>(true).Where(Owns))
                {
                    foreach (var renderable in slot.GetComponentsInChildren<ItemRenderable>(true))
                        if (renderable._itemId != Rules.LongStick) renderable.SetItemId(Rules.LongStick);
                    foreach (var pickup in slot.GetComponentsInChildren<PickUp>(true))
                    {
                        if (pickup._itemId != Rules.Stick) continue;
                        pickup._itemId = Rules.LongStick;
                        pickup._itemDataCached = data;
                        if (pickup.ItemInstance?._itemID == Rules.Stick) pickup.ItemInstance = null;
                    }
                    LongStickVisuals.Attach(slot.gameObject);
                }
            }
            for (var i = 0; i < storages.Count; i++)
                ConfigureManager(storages[i]._itemInstanceManager ?? storages[i].GetComponent<ItemInstanceManager>(), StorageRules.SlotCapacity(i, storages.Count));
        }
        finally { _applying = false; }
    }

    [HideFromIl2Cpp]
    public int CapacityFor(ItemInstanceManager manager)
    {
        var storages = ResolveSlots(GetComponentsInChildren<InWorldLayoutItemGroup>(true).Where(Owns).ToArray());
        for (var i = 0; i < storages.Count; i++)
        {
            var candidate = storages[i]._itemInstanceManager ?? storages[i].GetComponent<ItemInstanceManager>();
            if (candidate != null && candidate.Pointer == manager.Pointer) return StorageRules.SlotCapacity(i, storages.Count);
        }
        return 0;
    }

    [HideFromIl2Cpp]
    private List<StructureStorage> ResolveSlots(InWorldLayoutItemGroup[] groups)
    {
        var all = GetComponentsInChildren<StructureStorage>(true).Where(Owns).ToArray();
        var result = new List<StructureStorage>();
        foreach (var group in groups)
        {
            if (group._itemId != Rules.Stick && group._itemId != Rules.LongStick) continue;
            var storage = group._storage;
            if (storage == null || !Owns(storage))
                storage = group.GetComponentInParent<StructureStorage>(true) ?? (all.Length == 1 ? all[0] : null);
            if (storage == null || !Owns(storage))
                throw new InvalidOperationException("A holder layout has no matching cloned storage slot.");
            group._storage = storage;
            if (!result.Any(s => s.Pointer == storage.Pointer)) result.Add(storage);
        }
        return result;
    }

    [HideFromIl2Cpp]
    private bool Owns(Component component) =>
        (component.transform.Pointer == transform.Pointer || component.transform.IsChildOf(transform)) &&
        component.GetComponentInParent<LongStickStorageHolder>(true)?.Pointer == Pointer;

    [HideFromIl2Cpp]
    private static void ConfigureManager(ItemInstanceManager? manager, int capacity)
    {
        if (manager == null) return;
        var map = manager._itemsMap;
        if (map == null) return;
        if (!map.ContainsKey(Rules.LongStick))
            map.Add(Rules.LongStick, new ItemInstanceManager.Items {
                Count = 0, MaxCount = capacity, UniqueInstances = new Il2CppSystem.Collections.Generic.List<ItemInstance>()
            });
        manager.SetItemMaxCount(Rules.LongStick, capacity);
    }
}
