using HarmonyLib;
using RedLoader;
using Sons.Crafting;
using Sons.Crafting.Structures;
using Sons.Gameplay;
using Sons.Gameplay.GameSetup;
using Sons.Gameplay.TreeCutting;
using Sons.Inventory;
using Sons.Items.Core;
using Sons.Save;
using Sons.TerrainDetail;
using Sons.Weapon;
using SonsSdk;
using SUI;
using TheForest.Utils;
using UnityEngine;

namespace LongStickSpear;

public sealed class Mod : SonsMod
{
    private static Mod? _instance;
    private bool _singlePlayer, _world, _ready, _hooks;
    private int _warnings, _attempts, _warmupFrames;
    private float _nextSetup;
    private bool _storageReady;
    private int _storageAttempts;
    private float _nextStorage;
    internal static bool WorldActive => _instance is { _world: true, _singlePlayer: true };
    internal static bool RuntimeReady => _instance is { _world: true, _singlePlayer: true, _ready: true };
    internal static bool CanReplaceDrops => RuntimeReady && Config.ReplaceTreeDrops.Value;

    protected override void OnInitializeMod()
    {
        _instance = this;
        HarmonyPatchAll = false;
        Config.Init();
        SdkEvents.BeforeLoadSave.Subscribe(BeforeLoadSave);
        SdkEvents.OnWorldExited.Subscribe(WorldExited);
        SdkEvents.OnAfterSpawn.Subscribe(QueueSetup);
        SdkEvents.OnSonsSceneInitialized.Subscribe(scene => { if (scene == ESonsScene.Game) TryStorageSetup(false); });
        GlobalEvents.OnUpdate.Subscribe(UpdateSetup);
    }

    protected override void OnSdkInitialized()
    {
        SettingsRegistry.CreateSettings(this, null, typeof(Config), false, null);
        try
        {
            // Some installs do not create the database until a world loads.
            // BeforeLoadSave checks it again before deserializing inventory.
            LongStickVisuals.Initialize();
            LongStickStorage.Initialize();
            NaturalWorldSticks.Initialize();
            if (ItemDatabaseManager._itemsCache != null && ItemDatabaseManager._instance != null) LongStickItem.Register();
            else Info("Waiting for the item database; registration will run before the save is loaded.");
            Info("Startup ready. Native hooks wait for save loading; tree drop setup waits for world inventory setup.");
        }
        catch (Exception ex) { Warn("Custom item initialization: " + ex); }
    }

    private void InstallHooks()
    {
        if (_hooks) return;
        var drops = AccessTools.Method(typeof(BreakableObject), nameof(BreakableObject.SpawnDrops), new[] { typeof(Vector3) })
            ?? throw new MissingMethodException("BreakableObject.SpawnDrops");
        HarmonyInstance.Patch(drops,
            new HarmonyMethod(typeof(Hooks), nameof(Hooks.BeforeDrops)),
            new HarmonyMethod(typeof(Hooks), nameof(Hooks.AfterDrops)),
            finalizer: new HarmonyMethod(typeof(Hooks), nameof(Hooks.DropFinalizer)));
        InstallFallingTreeHooks();
        var pickupEnable = AccessTools.Method(typeof(PickUp), nameof(PickUp.OnEnable), Type.EmptyTypes)
            ?? throw new MissingMethodException("PickUp.OnEnable");
        HarmonyInstance.Patch(pickupEnable, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.PickupReady)));
        var pickupInstance = AccessTools.Method(typeof(PickUp), nameof(PickUp.SetItemInstance), new[] { typeof(ItemInstance), typeof(bool) })
            ?? throw new MissingMethodException("PickUp.SetItemInstance");
        HarmonyInstance.Patch(pickupInstance, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.PickupReady)));
        var initialize = AccessTools.Method(typeof(CraftingSystem), nameof(CraftingSystem.Initialize), Type.EmptyTypes)
            ?? throw new MissingMethodException("CraftingSystem.Initialize");
        HarmonyInstance.Patch(initialize, new HarmonyMethod(typeof(Hooks), nameof(Hooks.CraftingInitialize)),
            new HarmonyMethod(typeof(Hooks), nameof(Hooks.CraftingInitialized)));
        PatchCrafting(nameof(CraftingSystem.OnEnable), nameof(Hooks.CraftingOpened));
        PatchCrafting(nameof(CraftingSystem.AddIngredient), nameof(Hooks.IngredientAdded));
        PatchCrafting(nameof(CraftingSystem.OnRevealResultingItemInternal), nameof(Hooks.CraftReveal));
        PatchCrafting(nameof(CraftingSystem.OnCraftCompleteInternal), nameof(Hooks.CraftComplete));
        var combine = AccessTools.Method(typeof(CraftingSystem), nameof(CraftingSystem.TryCombineIngredients), Type.EmptyTypes)
            ?? throw new MissingMethodException("CraftingSystem.TryCombineIngredients");
        HarmonyInstance.Patch(combine, new HarmonyMethod(typeof(Hooks), nameof(Hooks.BeforeCombine)),
            new HarmonyMethod(typeof(Hooks), nameof(Hooks.AfterCombine)),
            finalizer: new HarmonyMethod(typeof(Hooks), nameof(Hooks.CombineFinalizer)));
        var addToMat = AccessTools.Method(typeof(InventoryLayoutItemGroup), nameof(InventoryLayoutItemGroup.OnAddItemToMat))
            ?? throw new MissingMethodException("InventoryLayoutItemGroup.OnAddItemToMat");
        HarmonyInstance.Patch(addToMat, new HarmonyMethod(typeof(Hooks), nameof(Hooks.BeforeAddToMat)),
            new HarmonyMethod(typeof(Hooks), nameof(Hooks.AfterAddToMat)));
        var structuresBeforeLoad = AccessTools.Method(typeof(ScrewStructureManager), nameof(ScrewStructureManager.OnBeforeLoad), Type.EmptyTypes)
            ?? throw new MissingMethodException("ScrewStructureManager.OnBeforeLoad");
        HarmonyInstance.Patch(structuresBeforeLoad, prefix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.StructuresBeforeLoad)));
        var bookInitialize = AccessTools.Method(typeof(BlueprintBookController), nameof(BlueprintBookController.Initialize))
            ?? throw new MissingMethodException("BlueprintBookController.Initialize");
        HarmonyInstance.Patch(bookInitialize, prefix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.BookInitialize)));
        var bookLocalization = AccessTools.Method(typeof(BlueprintBookController), nameof(BlueprintBookController.UpdatePageLocalization), Type.EmptyTypes)
            ?? throw new MissingMethodException("BlueprintBookController.UpdatePageLocalization");
        HarmonyInstance.Patch(bookLocalization, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.BookLocalizationUpdated)));
        var storageValidate = AccessTools.Method(typeof(StructureStorage), nameof(StructureStorage.ValidateItemPlacement), new[] { typeof(ItemInstance) })
            ?? throw new MissingMethodException("StructureStorage.ValidateItemPlacement");
        HarmonyInstance.Patch(storageValidate, prefix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.ValidateStorageItem)));
        var managerAdd = AccessTools.Method(typeof(ItemInstanceManager), nameof(ItemInstanceManager.TryAddItems), new[] { typeof(int), typeof(int), typeof(bool) })
            ?? throw new MissingMethodException("ItemInstanceManager.TryAddItems");
        HarmonyInstance.Patch(managerAdd, prefix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.BeforeStorageAddItems)));
        var managerAddInstance = AccessTools.Method(typeof(ItemInstanceManager), nameof(ItemInstanceManager.TryAddItem), new[] { typeof(ItemInstance), typeof(bool) })
            ?? throw new MissingMethodException("ItemInstanceManager.TryAddItem");
        HarmonyInstance.Patch(managerAddInstance, prefix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.BeforeStorageAddInstance)));
        var deserialize = AccessTools.Method(typeof(ItemInstanceManager), nameof(ItemInstanceManager.DeserializeItemsFromExternalData))
            ?? throw new MissingMethodException("ItemInstanceManager.DeserializeItemsFromExternalData");
        HarmonyInstance.Patch(deserialize, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.StorageDeserialized)));
        _hooks = true;
        try
        {
            var setup = AccessTools.Method(typeof(VirtualPickup), nameof(VirtualPickup.Setup), new[] { typeof(VirtualPickupData) })
                ?? throw new MissingMethodException("VirtualPickup.Setup(data)");
            HarmonyInstance.Patch(setup, prefix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.NaturalVirtualData)));
            var data = AccessTools.Method(typeof(VirtualPickupSwitcher), nameof(VirtualPickupSwitcher.GeneratePickupData), Type.EmptyTypes)
                ?? throw new MissingMethodException("VirtualPickupSwitcher.GeneratePickupData");
            HarmonyInstance.Patch(data, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.NaturalSceneData)));
            var real = AccessTools.Method(typeof(VirtualPickup), nameof(VirtualPickup.SwitchToReal), new[] { typeof(ItemInstance) })
                ?? throw new MissingMethodException("VirtualPickup.SwitchToReal");
            HarmonyInstance.Patch(real, prefix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.NaturalVirtualReal)));
        }
        catch (Exception ex) { Warn("Natural static-pickup hooks could not be installed: " + ex); }
        InstallGroundScatterHooks();
        try { KelvinHooks.Install(HarmonyInstance); }
        catch (Exception ex) { Warn("Kelvin Long Stick support could not be installed: " + ex); }
    }

    private void InstallGroundScatterHooks()
    {
        foreach (var (type, method, hook) in new[] {
            (typeof(GreebleZone), nameof(GreebleZone.MainThreadRefresh), nameof(Hooks.GroundZoneRefresh)),
            (typeof(GreebleZone), nameof(GreebleZone.SpawnIndex), nameof(Hooks.GroundZoneSpawn)),
            (typeof(PickUp), nameof(PickUp.GrabEnter), nameof(Hooks.GroundStickLook)) })
        {
            try
            {
                var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method);
                HarmonyInstance.Patch(target, postfix: new HarmonyMethod(typeof(Hooks), hook));
            }
            catch (Exception ex) { Warn("Ground-scatter observer " + method + ": " + ex.Message); }
        }
    }

    private void InstallFallingTreeHooks()
    {
        var fell = AccessTools.Method(typeof(TreeCutManager), nameof(TreeCutManager.FellTree), new[] { typeof(bool) })
            ?? throw new MissingMethodException("TreeCutManager.FellTree");
        HarmonyInstance.Patch(fell, prefix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.BeforeTreeFell)));
    }

    private void PatchCrafting(string method, string postfix)
    {
        var original = AccessTools.Method(typeof(CraftingSystem), method)
            ?? throw new MissingMethodException(typeof(CraftingSystem).FullName, method);
        HarmonyInstance.Patch(original, postfix: new HarmonyMethod(typeof(Hooks), postfix));
    }

    private void BeforeLoadSave()
    {
        try { NaturalWorldSticks.Begin(GameSetupManager.GetSaveGameType() == SaveGameType.SinglePlayer); }
        catch (Exception ex) { Warn("Preparing natural stick placements: " + ex); }
        try { InstallHooks(); }
        catch (Exception ex) { Warn("Preparing base hooks before save loading: " + ex); }
        try { LongStickItem.Register(); }
        catch (Exception ex) { Warn("Before loading inventory: " + ex); }
        KelvinLongSticks.Begin();
        TryStorageSetup(true);
    }

    private void QueueSetup() { if (!_ready) _warmupFrames = 2; }

    protected override void OnGameStart()
    {
        _world = true; _ready = false; _attempts = 0; _warnings = 0; _storageReady = false; _storageAttempts = 0; _nextStorage = 0;
        TreeDrops.Reset();
        TreeLoot.Reset();
        _warmupFrames = 2;
        _singlePlayer = GameSetupManager.GetSaveGameType() == SaveGameType.SinglePlayer;
        NaturalWorldSticks.Started(_singlePlayer);
        if (!_singlePlayer) { Info("Multiplayer is outside this build's scope; tree drops are unchanged."); return; }
        TryStorageSetup(true);
    }

    private void UpdateSetup()
    {
        TreeLoot.Update();
        NaturalWorldSticks.Update();
        KelvinLongSticks.Update();
        StorageBookArt.Update();
        if (!_world || !_singlePlayer) return;
        if (_warmupFrames > 0) { _warmupFrames--; return; }
        if (_world && _singlePlayer && !_storageReady && _storageAttempts < 30 && Time.unscaledTime >= _nextStorage)
        {
            _nextStorage = Time.unscaledTime + 1; _storageAttempts++;
            TryStorageSetup(_storageAttempts is 1 or 30);
        }
        if (!_world || !_singlePlayer || _ready || _attempts >= 30 || Time.unscaledTime < _nextSetup) return;
        _nextSetup = Time.unscaledTime + 1;
        TrySetup();
    }

    private void TryStorageSetup(bool report)
    {
        try { _storageReady = LongStickStorage.Ensure(); }
        catch (Exception ex)
        {
            if (report) Warn("Long-stick book storage registration: " + ex);
            else Trace("Book storage registration will retry: " + ex.Message);
        }
    }

    private void TrySetup()
    {
        if (!_world || !_singlePlayer || _ready || LocalPlayer.Inventory == null) return;
        _attempts++;
        try
        {
            InstallHooks();
            var system = SonsSdk.GameState.CraftingSystem;
            if (!CraftingAndInventory.Ensure(system)) return;
            _ready = true;
            CraftingAndInventory.Refresh(system, true);
            Info($"Ready. Tree-drop replacement enabled={Config.ReplaceTreeDrops.Value}. Combine one Long Stick with the knife on the crafting mat for a normal spear.");
        }
        catch (Exception ex)
        {
            if (_attempts == 1 || _attempts == 30) Warn($"Inventory/crafting setup ({_attempts}/30): {ex}");
        }
    }

    private static void InitializeCrafting(CraftingSystem system)
    {
        try
        {
            if (GameSetupManager.GetSaveGameType() == SaveGameType.SinglePlayer && LocalPlayer.Inventory != null)
            {
                CraftingAndInventory.Ensure(system);
                CraftingAndInventory.Refresh(system, true);
            }
        }
        catch (Exception ex) { Trace("Early crafting layout setup will retry after spawn: " + ex.Message); }
    }

    private void WorldExited()
    {
        _world = false; _singlePlayer = false; _ready = false; _attempts = 0; _nextSetup = 0;
        _storageReady = false; _storageAttempts = 0; _nextStorage = 0;
        CraftingAndInventory.Reset(); LongStickVisuals.Reset(); CraftingDiagnostics.Reset(); TreeDrops.Reset(); TreeLoot.Reset(); NaturalWorldSticks.Reset(); LongStickStorage.WorldExited(); KelvinLongSticks.Reset();
    }

    internal static void Info(string text) => RLog.Msg("[LongStickSpear 0.4.6] " + text);
    internal static void Trace(string text) { if (Config.DebugLogging.Value) Info(text); }
    internal static void Warn(string text) { if (_instance == null || _instance._warnings++ < 20) RLog.Warning("[LongStickSpear] " + text); }

    private static class Hooks
    {
        public static void GroundZoneRefresh(GreebleZone __instance)
        {
            try { NaturalGroundScatter.ObserveZone(__instance); }
            catch (Exception ex) { Trace("Ground-scatter zone observer: " + ex.Message); }
        }
        public static void GroundZoneSpawn(GreebleZone __instance, bool forPreview)
        {
            if (!forPreview) GroundZoneRefresh(__instance);
        }
        public static void GroundStickLook(PickUp __instance)
        {
            try { NaturalGroundScatter.ObserveLook(__instance); }
            catch (Exception ex) { Trace("Ground-stick look observer: " + ex.Message); }
        }
        public static void NaturalVirtualData(VirtualPickupData virtualPickupData)
        {
            try { NaturalWorldSticks.CaptureData(virtualPickupData); }
            catch (Exception ex) { Warn("Reading static virtual pickup data: " + ex.Message); }
        }
        public static void NaturalSceneData(VirtualPickupData? __result)
        {
            try { NaturalWorldSticks.CaptureData(__result); }
            catch (Exception ex) { Warn("Reading authored pickup data: " + ex.Message); }
        }
        public static void NaturalVirtualReal(VirtualPickup __instance)
        {
            try { NaturalWorldSticks.CaptureProxy(__instance); }
            catch (Exception ex) { Warn("Reading static pickup activation: " + ex.Message); }
        }
        public static void BeforeTreeFell(TreeCutManager __instance)
        {
            try { TreeLoot.PrepareFell(__instance); }
            catch (Exception ex) { Warn("Preparing native tree loot entries: " + ex); }
        }
        public static void BeforeDrops(BreakableObject __instance, out DropSnapshot? __state)
        {
            __state = null;
            try { __state = TreeDrops.Prepare(__instance); }
            catch (Exception ex) { Warn("Tree-drop preparation: " + ex); }
        }
        public static void AfterDrops(DropSnapshot? __state) => Restore(__state);
        public static void DropFinalizer(DropSnapshot? __state, Exception? __exception)
        {
            Restore(__state);
            if (__state != null && __exception != null) Warn("Native tree-drop error: " + __exception);
        }
        private static void Restore(DropSnapshot? snapshot)
        {
            try { snapshot?.Restore(); }
            catch (Exception ex) { Warn("Restoring original drop definitions: " + ex); }
        }
        public static void PickupReady(PickUp __instance)
        {
            if (!WorldActive) return;
            try
            {
                LongStickVisuals.PickupReady(__instance);
                if (__instance._itemId == LongStickSpear.Core.Rules.LongStick) KelvinLongSticks.TrackPickup(__instance);
                NaturalWorldSticks.QueuePickup(__instance);
            }
            catch (Exception ex) { Warn("Long-stick pickup appearance: " + ex); }
        }
        public static void StructuresBeforeLoad() => _instance?.TryStorageSetup(true);
        public static void BookInitialize() => _instance?.TryStorageSetup(true);
        public static void BookLocalizationUpdated(BlueprintBookController __instance)
        {
            try { StorageBookLabels.Refresh(__instance); }
            catch (Exception ex) { Warn("Updating long-stick storage book caption: " + ex.Message); }
        }
        public static bool ValidateStorageItem(StructureStorage __instance, ItemInstance itemInstanceData, ref bool __result)
        {
            try
            {
                var holder = LongStickStorage.HolderFor(__instance);
                if (holder == null) return true;
                holder.Apply();
                if (itemInstanceData?._itemID == Core.Rules.LongStick) return true;
                __result = false; return false;
            }
            catch (Exception ex) { Warn("Validating long-stick storage: " + ex); __result = false; return false; }
        }
        public static bool BeforeStorageAddItems(ItemInstanceManager __instance, int itemId, int count, ref bool __result)
        {
            try { if (LongStickStorage.AllowDeposit(__instance, itemId, count)) return true; }
            catch (Exception ex) { Warn("Long-stick holder deposit check: " + ex); }
            __result = false; return false;
        }
        public static bool BeforeStorageAddInstance(ItemInstanceManager __instance, ItemInstance item, ref bool __result)
        {
            try { if (LongStickStorage.AllowDeposit(__instance, item?._itemID ?? -1, 1)) return true; }
            catch (Exception ex) { Warn("Long-stick holder instance check: " + ex); }
            __result = false; return false;
        }
        public static void StorageDeserialized(ItemInstanceManager __instance)
        {
            try { LongStickStorage.HolderFor(__instance)?.Apply(); }
            catch (Exception ex) { Warn("Restoring long-stick holder capacity: " + ex); }
        }
        public static void CraftingInitialize(CraftingSystem __instance) => InitializeCrafting(__instance);
        public static void CraftingInitialized(CraftingSystem __instance) => InitializeCrafting(__instance);
        public static void CraftingOpened(CraftingSystem __instance) => InitializeCrafting(__instance);
        public static void BeforeAddToMat(InventoryLayoutItemGroup __instance)
        {
            if (__instance._itemId != Core.Rules.LongStick) return;
            try
            {
                CraftingAndInventory.PrepareAddToMat(__instance);
                CraftingDiagnostics.Report(SonsSdk.GameState.CraftingSystem, "Long Stick right-click (before transfer)", true);
            }
            catch (Exception ex) { Warn("Preparing Long Stick crafting input: " + ex); }
        }
        public static void AfterAddToMat(InventoryLayoutItemGroup __instance)
        {
            if (__instance._itemId != Core.Rules.LongStick) return;
            try { CraftingDiagnostics.Report(SonsSdk.GameState.CraftingSystem, "Long Stick right-click (after transfer)", true); }
            catch (Exception ex) { Warn("Long Stick transfer diagnostic: " + ex.Message); }
        }
        public static void IngredientAdded(CraftingSystem __instance, ItemInstance itemInstance)
        {
            try { CraftingDiagnostics.OnAdded(__instance, itemInstance); }
            catch (Exception ex) { Warn("Refreshing the Long Stick recipe: " + ex); }
        }
        public static void BeforeCombine(CraftingSystem __instance, out bool __state)
        {
            __state = false;
            try
            {
                __state = CraftingDiagnostics.HasLongStick(__instance);
                if (__state) CraftingDiagnostics.BeforeCombine(__instance);
            }
            catch (Exception ex) { Warn("Preparing the Long Stick craft: " + ex); }
        }
        public static void AfterCombine(CraftingSystem __instance, bool __result, bool __state)
        {
            if (!__state) return;
            try { CraftingDiagnostics.Report(__instance, "cog click returned " + __result, true); }
            catch (Exception ex) { Warn("Craft result diagnostic: " + ex.Message); }
        }
        public static void CombineFinalizer(bool __state, Exception? __exception)
        {
            if (__state && __exception != null) Warn("Native Long Stick craft exception: " + __exception);
        }
        public static void CraftReveal(CraftingSystem __instance, CraftingRecipe recipe)
        {
            try { CraftingDiagnostics.Completed(__instance, recipe, "spear revealed"); }
            catch (Exception ex) { Warn("Craft reveal diagnostic: " + ex.Message); }
        }
        public static void CraftComplete(CraftingSystem __instance, CraftingRecipe recipe)
        {
            try { CraftingDiagnostics.Completed(__instance, recipe, "craft completed"); }
            catch (Exception ex) { Warn("Craft completion diagnostic: " + ex.Message); }
        }
    }
}
