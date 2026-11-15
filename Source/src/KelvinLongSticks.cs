using HarmonyLib;
using LongStickSpear.Core;
using Sons.Ai.Vail;
using Sons.Ai.Vail.Inventory;
using Sons.Ai.Vail.StimuliTypes;
using Sons.Gameplay;
using Sons.Gameplay.GameSetup;
using Sons.Gui;
using Sons.Inventory;
using Sons.Items.Core;
using Sons.Save;
using SonsSdk;
using UnityEngine;
using Object = UnityEngine.Object;
using AiInventory = Sons.Ai.Vail.Inventory.InventoryManager;

namespace LongStickSpear;

internal static class KelvinLongSticks
{
    private sealed record Orders(Robby Robby, RobbyOrderData Data, Robby.RobbyGetOrder LongOrder, int Index);
    private static readonly Dictionary<IntPtr, Orders> Actors = new();
    private static readonly Dictionary<IntPtr, Robby> ActorOwners = new();
    private static readonly Dictionary<IntPtr, Robby> InventoryOwners = new();
    private static readonly HashSet<(int Item, bool LongTask, bool Stored, bool Allowed)> DecisionProfiles = new();
    private static readonly Dictionary<IntPtr, bool> LongTasks = new();
    private static readonly Queue<PickUp> PendingPickups = new();
    private static readonly HashSet<IntPtr> KnownPickups = new();
    private sealed record WorldPickup(PickUp Item, VailPickup Ai, PickupStimuli[] Stimuli);
    private static readonly List<WorldPickup> WorldPickups = new();
    private static readonly Dictionary<IntPtr, WorldPickup> StimulusSources = new();
    private static readonly HashSet<string> TrackingReports = new(StringComparer.Ordinal);
    private static readonly HashSet<(IntPtr Source, bool Valid)> ValidationReports = new();
    private static bool _installed, _session;
    private static int _typeId = -1, _groupId, _maxHeld = 1, _errors, _decisions, _searches;
    private static string _stickType = "";
    private static VailPickupManager.TypeInfo? _pickupInfo;
    private static GameObject? _heldTemplates, _heldPrefab;
    private static float _nextSetup;
    private static IntPtr _configuredManager;
    private static bool _idsReady, _preparing, _registeringIds, _stimuliEnabled;
    private static int _legacyTypeId = -1;
    private static float _nextTaskRefresh;
    private static float _fpsStart;
    private static int _fpsFrames, _fpsReports;
    private static float _nextWorkReport;
    private static int _workReports, _pickupReports, _depositReports;
    [ThreadStatic] private static Robby? _startingLongOrder;
    [ThreadStatic] private static AiInventory? _restoringInventory;
    [ThreadStatic] private static AiInventory? _gatheringInventory;
    internal static bool Enabled => _installed && _session;
    internal static bool Installed => _installed;
    internal static int TypeId => _typeId;
    internal static int GroupId => _groupId;
    internal static int MaxHeld => _maxHeld;
    internal static string StickType => _stickType;

    internal static void SetInstalled() => _installed = true;
    internal static void Begin()
    {
        Reset(); _session = GameSetupManager.GetSaveGameType() == SaveGameType.SinglePlayer;
        if (!_session) return;
        try { EnsureTypeIds(); PreparePickupManager(); }
        catch (Exception ex) { Report("AI item type registration", ex); }
    }

    internal static void Reset()
    {
        _session = false; _startingLongOrder = null; _restoringInventory = null; _gatheringInventory = null;
        KelvinHooks.UninstallWork();
        foreach (var entry in Actors.Values) if (entry.Data != null && entry.Data) Object.Destroy(entry.Data);
        Actors.Clear(); ActorOwners.Clear(); InventoryOwners.Clear(); DecisionProfiles.Clear(); LongTasks.Clear();
        PendingPickups.Clear(); KnownPickups.Clear(); WorldPickups.Clear(); StimulusSources.Clear(); TrackingReports.Clear(); ValidationReports.Clear();
        _configuredManager = IntPtr.Zero; _idsReady = _preparing = _registeringIds = _stimuliEnabled = false;
        _nextSetup = _nextTaskRefresh = _fpsStart = 0; _fpsFrames = _fpsReports = 0; _errors = _decisions = _searches = 0;
        _nextWorkReport = 0; _workReports = _pickupReports = _depositReports = 0;
        // The global type table and our inactive held template outlive a world.
    }

    internal static void Update()
    {
        try { UpdateWorld(); }
        catch (Exception ex) { Report("deferred world update", ex); }
    }
    private static void UpdateWorld()
    {
        if (!Enabled || !Mod.RuntimeReady) return;
        if (_fpsReports < 3)
        {
            var now = Time.realtimeSinceStartup;
            if (_fpsStart == 0) _fpsStart = now;
            _fpsFrames++;
            var elapsed = now - _fpsStart;
            if (elapsed >= 5f)
            {
                Mod.Info($"PERFORMANCE: averageFPS={_fpsFrames / elapsed:F1}, kelvinGatherHooks={KelvinHooks.WorkInstalled}, activeLongOrder={_stimuliEnabled}, trackedLongPickups={WorldPickups.Count}.");
                _fpsStart = now; _fpsFrames = 0; _fpsReports++;
            }
        }
        if (Time.unscaledTime >= _nextSetup)
        {
            _nextSetup = Time.unscaledTime + 2f;
            PreparePickupManager();
            foreach (var entry in Actors.Values) if (entry.Robby != null && entry.Robby) RegisterOwner(entry.Robby);
        }
        if (Time.unscaledTime >= _nextTaskRefresh)
        {
            _nextTaskRefresh = Time.unscaledTime + .25f;
            foreach (var pair in Actors)
                LongTasks[pair.Key] = pair.Value.Robby != null && pair.Value.Robby && IsLongOrder(pair.Value.Robby._activeOrder?._order);
            var active = LongTasks.Values.Any(value => value);
            if (active) KelvinHooks.InstallWork();
            if (active != _stimuliEnabled)
            {
                _stimuliEnabled = active;
                foreach (var item in WorldPickups) SetSourceActive(item, active);
                Mod.Info($"KELVIN WORK: longOrder={active}, trackedLoosePickups={WorldPickups.Count}; idle pickups do not advertise AI work.");
            }
        }
        for (var i = 0; i < 4 && PendingPickups.Count > 0; i++)
        {
            var item = PendingPickups.Dequeue();
            if (item == null || !item || item._itemId != Rules.LongStick || item._isCollected || item._isBeingDestroyed) continue;
            if (!item.gameObject.activeInHierarchy) { TrackingReport("inactive pickup"); KnownPickups.Remove(item.Pointer); continue; }
            if (item.GetComponentInParent<InventoryLayoutItemGroup>(true) != null ||
                item.GetComponentInParent<InWorldLayoutItemGroup>(true) != null ||
                item.GetComponentInParent<StructureStorage>(true) != null || item.GetComponentInParent<VailActor>(true) != null)
            { TrackingReport("stored, inventory or carried pickup excluded"); continue; }
            var ai = item.GetComponent<VailPickup>() ?? item.GetComponentInParent<VailPickup>(true) ?? item.GetComponentInChildren<VailPickup>(true);
            if (ai == null || !ai) { TrackingReport("pickup has no VailPickup"); continue; }
            // Use the live local hierarchy, not a cached prefab-root reference.
            var root = item.transform;
            var foundRoot = false;
            for (var depth = 0; depth < 6 && root != null; depth++)
            {
                if (ai.transform.Pointer == root.Pointer || ai.transform.IsChildOf(root)) { foundRoot = true; break; }
                root = root.parent;
            }
            if (!foundRoot || root == null || !root) { TrackingReport("pickup has no local AI root"); continue; }
            ai._root = root; ai._pickupTransform = item.transform; ai._itemType = KelvinRules.ItemType;
            var stimuli = root.GetComponentsInChildren<PickupStimuli>(true).ToArray();
            if (stimuli.Length == 0)
            {
                var added = root.gameObject.AddComponent<StickPickupStimuli>();
                added.enabled = false; stimuli = new PickupStimuli[] { added };
            }
            var source = new WorldPickup(item, ai, stimuli);
            foreach (var stimulus in stimuli)
            {
                stimulus._target = ai; stimulus._transform = stimulus.transform;
                stimulus.SetStaticPosition(false); StimulusSources[stimulus.Pointer] = source;
            }
            WorldPickups.Add(source); SetSourceActive(source, _stimuliEnabled);
            if (WorldPickups.Count <= 12)
                Mod.Info($"KELVIN TRACK: '{root.name}', item={item._itemId}, stimuli={stimuli.Length}, active={_stimuliEnabled}, tracked={WorldPickups.Count}.");
        }
        if (_stimuliEnabled && Time.unscaledTime >= _nextWorkReport && _workReports < 12)
        {
            _nextWorkReport = Time.unscaledTime + 10f; _workReports++;
            foreach (var entry in Actors.Values)
                if (entry.Robby != null && entry.Robby && IsLongTask(entry.Robby))
                {
                    var inventory = entry.Robby._actor?._inventoryManager;
                    var target = entry.Robby._dropStimuliInstance?._dropTarget?.TryCast<InWorldLayoutItemGroup>();
                    Mod.Info($"KELVIN TASK: actor='{entry.Robby.name}', tracked={WorldPickups.Count}, heldLong={inventory?.GetCount(KelvinRules.ItemType) ?? -1}, holderItem={target?._itemId ?? -1}, holderCount={target?.GetCurrentCount() ?? -1}.");
                }
        }
    }

    private static void SetSourceActive(WorldPickup source, bool active)
    {
        if (source.Item == null || !source.Item || source.Item._isCollected || source.Item._isBeingDestroyed) return;
        foreach (var stimulus in source.Stimuli)
        {
            if (stimulus == null || !stimulus) continue;
            stimulus._target = source.Ai;
            if (active) { stimulus.SetValid(true); stimulus.SetToClosestNavGraph(); }
            stimulus.enabled = active;
        }
    }
    private static void TrackingReport(string reason)
    { if (TrackingReports.Count < 8 && TrackingReports.Add(reason)) Mod.Info("KELVIN TRACK: " + reason + "."); }

    internal static void TrackOwner(GameObject owner)
    {
        if (!Enabled || owner == null || !owner) return;
        try
        {
            var pickup = owner.GetComponent<PickUp>() ?? owner.GetComponentInChildren<PickUp>(true);
            if (pickup != null && pickup) TrackPickup(pickup);
        }
        catch (Exception ex) { Report("pickup source registration", ex); }
    }

    internal static void TrackPickup(PickUp pickup)
    {
        if (Enabled && pickup._itemId == Rules.LongStick && KnownPickups.Add(pickup.Pointer)) PendingPickups.Enqueue(pickup);
    }

    internal static void Report(string action, Exception ex)
    { if (_errors++ < 6) Mod.Warn("KELVIN " + action + ": " + ex); }

    internal static void PreparePickupManager()
    {
        if (!Enabled || _preparing || _registeringIds) return;
        try
        {
            var manager = VailPickupManager._instance;
            if (manager != null && manager && _configuredManager == manager.Pointer) return;
            _preparing = true;
            if (EnsurePickupType() && manager != null) _configuredManager = manager.Pointer;
        }
        catch (Exception ex) { Report("AI pickup readiness", ex); }
        finally { _preparing = false; }
    }

    private static void EnsureTypeIds()
    {
        if (!Enabled || _idsReady || _registeringIds) return;
        _registeringIds = true;
        try
        {
        var list = InventoryTypeList.TypeList;
        if (list == null) throw new InvalidOperationException("Kelvin's inventory type table is not ready.");
        _stickType = VailPickupManager.ItemIdToVailItem(Rules.Stick);
        if (string.IsNullOrEmpty(_stickType) || _stickType == KelvinRules.ItemType)
            throw new InvalidOperationException("The original stick AI type could not be identified.");
        _groupId = InventoryTypeList.GetGroupId(_stickType);
        var used = new List<int>();
        for (var i = 0; i < list.Count; i++)
        {
            var info = list[i]; used.Add(info.TypeId);
            if (info.StringId != KelvinRules.ItemType) continue;
            if (_typeId < 0 || info.TypeId != _typeId || info.GroupId != _groupId)
                throw new InvalidOperationException("The Long Stick AI type name is already registered by another system.");
            _idsReady = true; return;
        }
        var next = KelvinRules.ReserveTypeId(used, AiInventory.ItemIdMask);
        if (_typeId >= 0 && next != _typeId)
            throw new InvalidOperationException("Kelvin's saved Long Stick type ID is no longer available.");
        _typeId = next;
        // TypeInfo is a native value type represented by a managed wrapper
        // class. This loader's generated List<T>.Add passes its boxed header
        // as the value payload, corrupting StringId in the shared type table.
        // The native IList object overload receives the box and unboxes it
        // inside IL2CPP, using the game's correct struct layout.
        var addedAt = list.System_Collections_IList_Add(new InventoryTypeList.TypeInfo(KelvinRules.ItemType, _typeId, _groupId));
        if (addedAt < 0 || addedAt >= list.Count)
            throw new InvalidOperationException("Kelvin's Long Stick type was not appended to the native table.");
        var added = list[addedAt];
        if (added.TypeId != _typeId || added.GroupId != _groupId || added.StringId != KelvinRules.ItemType)
            throw new InvalidOperationException("Kelvin's Long Stick type did not retain its native value payload.");
        _legacyTypeId = used.Contains(AiInventory.ItemIdMask) || _typeId == AiInventory.ItemIdMask ? -1 : AiInventory.ItemIdMask;
        _idsReady = true;
        bool nativeLookup;
        try { nativeLookup = InventoryTypeList.GetTypeId(KelvinRules.ItemType) == _typeId && InventoryTypeList.GetGroupId(KelvinRules.ItemType) == _groupId; }
        catch { nativeLookup = false; }
        if (!nativeLookup) KelvinHooks.InstallTypeFallback();
        Mod.Info($"KELVIN TYPE: Long Stick AI type={_typeId}, group={_groupId}, native stick type='{_stickType}', nativeTypeLookup={nativeLookup}, registration=boxed IList; native payload verified.");
        }
        finally { _registeringIds = false; }
    }
    internal static bool IsLongTypeId(int id) => id == _typeId || id == _legacyTypeId && _legacyTypeId >= 0;

    private static bool EnsurePickupType()
    {
        EnsureTypeIds();
        var manager = VailPickupManager._instance;
        var data = LongStickItem.Data;
        if (manager == null || !manager || manager._pickupTypes == null || data?.PickupPrefab == null) return false;
        VailPickupManager.TypeInfo? placeholder = null;
        for (var i = 0; i < manager._pickupTypes.Count; i++)
        {
            var existing = manager._pickupTypes[i];
            if (existing.itemType != KelvinRules.ItemType) continue;
            if (_pickupInfo != null && existing.Pointer == _pickupInfo.Pointer) return true;
            if (existing.heldPrefab == null && existing.droppedPrefab == null) { placeholder = existing; break; }
            if (_heldPrefab != null && existing.heldPrefab?.Pointer == _heldPrefab.Pointer &&
                existing.droppedPrefab?.Pointer == data.PickupPrefab.gameObject.Pointer)
            { _pickupInfo = existing; return true; }
            throw new InvalidOperationException("Long Stick AI pickup prefabs are already registered by another system.");
        }
        var original = manager.GetTypeInfo(_stickType);
        if (original == null || original.heldPrefab == null) return false;
        _maxHeld = Math.Max(1, original.maxHeld);
        if (_heldPrefab == null || !_heldPrefab)
        {
            _heldTemplates = new GameObject("LongStickSpear_KelvinTemplates");
            _heldTemplates.SetActive(false); Object.DontDestroyOnLoad(_heldTemplates);
            _heldPrefab = Object.Instantiate(original.heldPrefab, _heldTemplates.transform, false);
            _heldPrefab.name = "LongStickSpear_KelvinHeld";
            foreach (var pickup in _heldPrefab.GetComponentsInChildren<PickUp>(true))
            {
                pickup._itemId = Rules.LongStick; pickup._itemDataCached = data; pickup.ItemInstance = null;
            }
            // Keep the native held-model asset references. The AI type carries
            // item identity; the separate appearance pivot supplies the length.
            ConfigurePickup(_heldPrefab, false);
            LongStickVisuals.Attach(_heldPrefab);
            _heldPrefab.SetActive(true); // Parent remains inactive.
        }
        ConfigurePickup(data.PickupPrefab.gameObject);
        _pickupInfo = placeholder ?? new VailPickupManager.TypeInfo();
        _pickupInfo.itemType = KelvinRules.ItemType; _pickupInfo.maxHeld = _maxHeld;
        _pickupInfo.heldPrefab = _heldPrefab; _pickupInfo.droppedPrefab = data.PickupPrefab.gameObject;
        if (placeholder == null) manager._pickupTypes.Add(_pickupInfo);
        Mod.Info($"KELVIN PICKUPS: separate Long Stick held/dropped prefabs, native stick carrying limit={_maxHeld}.");
        return true;
    }

    internal static void ConfigurePickup(GameObject owner, bool worldPickup = true)
    {
        var pickup = owner.GetComponentInChildren<VailPickup>(true);
        if (pickup == null)
        {
            pickup = owner.AddComponent<VailPickup>();
            pickup._root = owner.transform; pickup._pickupTransform = owner.transform;
            pickup._rigidbody = owner.GetComponentInChildren<Rigidbody>(true);
            pickup._colliders = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider>(
                owner.GetComponentsInChildren<Collider>(true).ToArray());
            pickup._enabledOnDrop = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject>(0);
        }
        foreach (var aiPickup in owner.GetComponentsInChildren<VailPickup>(true))
        {
            aiPickup._itemType = KelvinRules.ItemType; aiPickup._root = owner.transform;
            aiPickup._pickupTransform = owner.transform;
        }
        var stimuli = owner.GetComponentsInChildren<PickupStimuli>(true);
        if (!worldPickup)
        { foreach (var stimulus in stimuli) stimulus.enabled = false; return; }
        if (stimuli.Length == 0)
        {
            var stimulus = owner.AddComponent<StickPickupStimuli>();
            stimulus._target = pickup; stimulus._isValid = true;
            stimulus._transform = owner.transform; stimulus.SetStaticPosition(false);
            stimulus.enabled = false;
        }
        else foreach (var stimulus in stimuli)
        { stimulus._target = pickup; stimulus._transform = stimulus.transform; stimulus._isValid = true; stimulus.enabled = false; }
    }

    internal static void EnsureOrders(Robby robby)
    {
        if (!Enabled || robby == null || !robby) return;
        try
        {
            RegisterOwner(robby);
            if (Actors.TryGetValue(robby.Pointer, out var current) && robby._orderData?.Pointer == current.Data.Pointer) return;
            EnsureTypeIds(); PreparePickupManager();
            var table = LocalizationTools.RobbyTable;
            if (table != null) table.AddEntry(KelvinRules.OrderKey, KelvinRules.OrderLabel);
            if (Actors.TryGetValue(robby.Pointer, out var old) && robby._orderData?.Pointer == old.Data.Pointer) return;
            var original = robby._orderData;
            if (original?._getItemOrders == null) return;
            Robby.RobbyGetOrder? source = null;
            foreach (var order in original._getItemOrders) if (order?._itemType == _stickType) { source = order; break; }
            if (source == null) throw new InvalidOperationException("Kelvin's normal stick order was not found.");
            var data = Object.Instantiate(original);
            data.name = "LongStickSpear_KelvinOrders";
            data._getItemOrders = new Il2CppSystem.Collections.Generic.List<Robby.RobbyGetOrder>();
            foreach (var order in original._getItemOrders) data._getItemOrders.Add(order);
            var added = new Robby.RobbyGetOrder {
                _itemName = KelvinRules.OrderKey, _itemType = KelvinRules.ItemType,
                _controllerOverride = source._controllerOverride, _enabled = true,
                _needsStimuli = "", _needsStimuliType = null, _maxStimuliRange = source._maxStimuliRange,
                _expireTimeMinutes = source._expireTimeMinutes, _restartAfterExpire = source._restartAfterExpire,
                _checkCompleteOnDrop = source._checkCompleteOnDrop
            };
            var index = data._getItemOrders.Count; data._getItemOrders.Add(added);
            robby._orderData = data;
            Actors[robby.Pointer] = new Orders(robby, data, added, index);
            Mod.Info($"KELVIN MENU: Get > Long Sticks > Fill Holders added at order index {index}; targets Long Stick Storage only; vanilla order indices retained.");
        }
        catch (Exception ex) { Report("notebook setup", ex); }
    }

    internal static void ShowDropOptions(Robby robby, int carousel)
    {
        if (!Enabled || carousel != (int)Robby.CarouselName.DropLocation ||
            !Actors.TryGetValue(robby.Pointer, out var entry)) return;
        var ui = robby._tactiPad;
        if (ui == null || ui.GetSelection((int)Robby.CarouselName.GetItem) != entry.Index) return;
        ui.ClearSubMenu((int)Robby.CarouselName.DropLocation);
        ui.AddMenuItem((int)Robby.CarouselName.DropLocation, (int)Robby.DropLocationType.FillHolder,
            "FILL HOLDERS", (int)Robby.CarouselName.None);
    }

    private static bool IsLongOrder(Robby.RobbyOrder? order) =>
        order?.TryCast<Robby.RobbyGetOrder>()?._itemType == KelvinRules.ItemType;
    internal static bool IsLongTask(Robby? robby) => Enabled && robby is not null &&
        (_startingLongOrder?.Pointer == robby.Pointer || LongTasks.TryGetValue(robby.Pointer, out var value) && value);
    private static void RegisterOwner(Robby robby)
    {
        var actor = robby._actor;
        if (actor == null || !actor) return;
        ActorOwners[actor.Pointer] = robby;
        var inventory = actor._inventoryManager;
        if (inventory != null && inventory) InventoryOwners[inventory.Pointer] = robby;
        LongTasks[robby.Pointer] = IsLongOrder(robby._activeOrder?._order);
        if (inventory != null && inventory) ConfigureCarryAliases(inventory);
    }
    internal static void ConfigureCarryAliases(AiInventory inventory)
    {
        if (inventory?._carryAttachments != null)
            foreach (var attachment in inventory._carryAttachments)
            {
                var types = attachment?._itemTypes;
                if (types == null || !types.Contains(_stickType) || types.Contains(KelvinRules.ItemType)) continue;
                var copy = new Il2CppSystem.Collections.Generic.List<string>();
                foreach (var type in types) copy.Add(type);
                copy.Add(KelvinRules.ItemType); attachment!._itemTypes = copy;
            }
    }
    internal static bool IsLongTask(VailActor? actor) => actor is not null &&
        ActorOwners.TryGetValue(actor.Pointer, out var robby) && IsLongTask(robby);
    internal static bool IsLongTask(AiInventory inventory) => Enabled && inventory is not null &&
        InventoryOwners.TryGetValue(inventory.Pointer, out var robby) && IsLongTask(robby);

    internal sealed class OrderScope : IDisposable
    {
        private readonly Robby? _previous;
        private bool _disposed;
        internal OrderScope(Robby robby, Robby.RobbyOrder order)
        {
            _previous = _startingLongOrder;
            _startingLongOrder = IsLongOrder(order) ? robby : null;
            LongTasks[robby.Pointer] = _startingLongOrder != null;
            if (_startingLongOrder != null)
            {
                KelvinHooks.InstallWork(); _workReports = 0; _nextWorkReport = 0;
                _fpsStart = 0; _fpsFrames = _fpsReports = 0;
                Mod.Info("KELVIN ORDER: gathering Long Sticks for Long Stick Storage.");
            }
        }
        public void Dispose() { if (!_disposed) { _startingLongOrder = _previous; _disposed = true; } }
    }

    internal sealed class RestoreScope : IDisposable
    {
        private readonly AiInventory? _previous;
        private bool _disposed;
        internal RestoreScope(AiInventory inventory) { _previous = _restoringInventory; _restoringInventory = inventory; }
        public void Dispose() { if (!_disposed) { _restoringInventory = _previous; _disposed = true; } }
    }
    internal static bool Restoring(AiInventory inventory) => _restoringInventory?.Pointer == inventory.Pointer;
    internal sealed class GatherScope : IDisposable
    {
        private readonly AiInventory? _previous;
        private bool _disposed;
        internal GatherScope(AiInventory inventory) { _previous = _gatheringInventory; _gatheringInventory = inventory; }
        public void Dispose() { if (!_disposed) { _gatheringInventory = _previous; _disposed = true; } }
    }
    internal static bool Gathering(AiInventory inventory) => _gatheringInventory?.Pointer == inventory.Pointer;

    internal static bool? AllowPickup(VailActor? actor, VailPickup? item)
    {
        if (!Enabled || actor is null) return null;
        if (!ActorOwners.TryGetValue(actor.Pointer, out var robby)) return null;
        if (item == null || !item) return null;
        var type = item._itemType;
        if (type != _stickType && type != KelvinRules.ItemType) return null;
        var longTask = IsLongTask(robby);
        if (type == _stickType)
        {
            LogDecision(Rules.Stick, longTask, false, !longTask);
            return longTask ? false : null;
        }
        if (!longTask) { LogDecision(Rules.LongStick, false, false, false); return false; }
        var root = item.GetRoot();
        var pickup = item.GetComponent<PickUp>() ?? root?.GetComponent<PickUp>() ?? root?.GetComponentInChildren<PickUp>(true);
        var id = pickup?._itemId ?? (item._itemType == KelvinRules.ItemType ? Rules.LongStick : -1);
        if (id is not Rules.Stick and not Rules.LongStick) return null;
        var insideStorage = item.GetComponentInParent<InWorldLayoutItemGroup>(true) != null ||
            item.GetComponentInParent<StructureStorage>(true) != null;
        var allow = KelvinRules.CanGather(id, longTask, insideStorage);
        LogDecision(id, longTask, insideStorage, allow);
        return allow ? null : false; // Native availability, claims and capacity checks still run.
    }
    private static void LogDecision(int id, bool longTask, bool stored, bool allowed)
    {
        if (_decisions >= 20 || !longTask && id != Rules.LongStick || !DecisionProfiles.Add((id, longTask, stored, allowed))) return;
        _decisions++;
        Mod.Info($"KELVIN SOURCE: item={id}, longOrder={longTask}, stored={stored}, allowed={allowed}.");
    }

    internal static bool? AllowPickup(AiInventory inventory, VailPickup? item) =>
        InventoryOwners.TryGetValue(inventory.Pointer, out var robby) && robby != null && robby ? AllowPickup(robby._actor, item) : null;

    // Full identity/storage verification belongs to the actual pickup, not each
    // candidate considered by every AI query.
    internal static bool? ConfirmPickup(AiInventory inventory, VailPickup? item)
    {
        if (!Enabled || item == null || !item || !InventoryOwners.TryGetValue(inventory.Pointer, out var robby)) return null;
        var type = item._itemType;
        if (type != _stickType && type != KelvinRules.ItemType) return null;
        var root = item.GetRoot();
        var pickup = item.GetComponent<PickUp>() ?? root?.GetComponent<PickUp>() ?? root?.GetComponentInChildren<PickUp>(true);
        var id = pickup?._itemId ?? (type == KelvinRules.ItemType ? Rules.LongStick : Rules.Stick);
        var longTask = IsLongTask(robby);
        if (!longTask) return id == Rules.LongStick ? false : null;
        if (id != Rules.LongStick) return false;
        var stored = item.GetComponentInParent<InWorldLayoutItemGroup>(true) != null || item.GetComponentInParent<StructureStorage>(true) != null;
        return KelvinRules.CanGather(id, true, stored) ? null : false;
    }

    internal static void MarkCollected(VailPickup worldItem)
    {
        if (!Enabled || worldItem == null || !worldItem) return;
        var root = worldItem.GetRoot();
        var pickup = worldItem.GetComponent<PickUp>() ?? root?.GetComponentInChildren<PickUp>(true);
        if (pickup?._itemId == Rules.LongStick) NaturalWorldSticks.MarkAiCollected(pickup.Pointer);
    }

    internal static void HolderSearch(Robby robby, bool found)
    {
        if (IsLongTask(robby) && _searches++ < 16)
            Mod.Info($"KELVIN HOLDER: found={found}, target='{robby._dropStimuliInstance?._dropTarget?.name ?? "none"}'.");
    }
    internal static bool IsLongHolder(InWorldLayoutItemGroup group) => Enabled && group._itemId == Rules.LongStick &&
        group._storage != null && LongStickStorage.HolderFor(group) != null;

    internal static void ValidateResult(PickupStimuli stimulus, VailActor actor, bool result)
    {
        if (!Enabled || !IsLongTask(actor) || ValidationReports.Count >= 24 ||
            !StimulusSources.TryGetValue(stimulus.Pointer, out var source) || !ValidationReports.Add((stimulus.Pointer, result))) return;
        Mod.Info($"KELVIN VALIDATE: source='{source.Item.name}', nativeValid={result}, stimulusValid={stimulus.IsValid()}, targetLocal={stimulus._target?.Pointer == source.Ai.Pointer}.");
    }
    internal static void PickupResult(AiInventory inventory, string itemType)
    {
        if (!Enabled || itemType != KelvinRules.ItemType || _pickupReports++ >= 24) return;
        Mod.Info($"KELVIN PICKUP: carriedLong={inventory.GetCount(KelvinRules.ItemType)}, carriedTotal={inventory.GetCountAll()}.");
    }
    internal static void DepositResult(InWorldLayoutItemGroup group, int itemId, int added)
    {
        if (!IsLongHolder(group) || _depositReports++ >= 24) return;
        Mod.Info($"KELVIN DEPOSIT: requestedItem={itemId}, added={added}, stored={group.GetCurrentCount()}, slotCapacity={group._maxAmount}.");
    }
}
