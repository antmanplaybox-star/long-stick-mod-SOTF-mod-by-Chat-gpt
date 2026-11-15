using System.Reflection;
using HarmonyLib;
using LongStickSpear.Core;
using Sons.Ai.Vail;
using Sons.Ai.Vail.Inventory;
using Sons.Ai.Vail.StimuliTypes;
using Sons.Gameplay;
using Sons.Inventory;
using UnityEngine;
using AiInventory = Sons.Ai.Vail.Inventory.InventoryManager;

namespace LongStickSpear;

internal static class KelvinHooks
{
    private sealed record Spec(Type Type, string Method, string? Prefix = null, string? Postfix = null,
        Type[]? Arguments = null, string? Finalizer = null);
    private static HarmonyLib.Harmony? _harmony;
    private static bool _workInstalled, _workFailed, _installingWork, _fallbackInstalled;
    internal static bool WorkInstalled => _workInstalled;
    private static readonly HashSet<string> WorkHooks = new(StringComparer.Ordinal) {
        nameof(RobbyItemQuery), nameof(HolderItemQuery), nameof(HoldersFound), nameof(ValidatePickup), nameof(CanPickup),
        nameof(BeforeWorldPickup), nameof(PickingUp), nameof(CollectedWorldItem), nameof(InventoryItemQuery),
        nameof(ActorCountQuery), nameof(ActorNeededQuery), nameof(ActorItemQuery)
    };
    private static bool Work(Spec spec) => WorkHooks.Contains(spec.Prefix ?? "") || WorkHooks.Contains(spec.Postfix ?? "");
    private static bool TypeFallback(Spec spec) => spec.Prefix is nameof(TypeId) or nameof(GroupId);

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        if (KelvinLongSticks.Installed) return;
        _harmony = harmony;
        Apply(Specs.Where(spec => !Work(spec) && !TypeFallback(spec)));
        KelvinLongSticks.SetInstalled();
    }
    internal static void InstallWork()
    {
        if (_workInstalled || _workFailed || _installingWork || _harmony == null) return;
        _installingWork = true;
        try { Apply(Specs.Where(Work)); _workInstalled = true; Mod.Info("KELVIN HOOKS: gathering hooks activated for the first Long Stick task."); }
        catch (Exception ex) { _workFailed = true; KelvinLongSticks.Report("gathering hook activation", ex); }
        finally { _installingWork = false; }
    }
    internal static void InstallTypeFallback()
    {
        if (_fallbackInstalled || _harmony == null) return;
        Apply(Specs.Where(TypeFallback)); _fallbackInstalled = true;
        Mod.Info("KELVIN TYPE: compatibility lookup hooks required; native lookup probe did not match.");
    }
    internal static void UninstallWork()
    {
        if (_workInstalled && _harmony != null)
            foreach (var spec in Specs.Where(Work))
            {
                var target = AccessTools.Method(spec.Type, spec.Method, spec.Arguments);
                if (target == null) continue;
                foreach (var name in new[] { spec.Prefix, spec.Postfix, spec.Finalizer })
                    if (PatchMethod(name) is { } patch) _harmony.Unpatch(target, patch);
            }
        _workInstalled = _workFailed = _installingWork = false;
    }
    private static readonly Spec[] Specs = {
            new(typeof(Robby), nameof(Robby.Start), Postfix: nameof(PrepareOrders)),
            new(typeof(Robby), nameof(Robby.BuildTactipadUI), Prefix: nameof(PrepareOrders)),
            new(typeof(Robby), nameof(Robby.RestoreSavedOrders), Prefix: nameof(PrepareOrders)),
            new(typeof(Robby), nameof(Robby.PreShowCarousel), Postfix: nameof(DropOptions)),
            new(typeof(Robby), nameof(Robby.RunOrder), Prefix: nameof(BeforeOrder), Finalizer: nameof(AfterOrder)),
            new(typeof(Robby), nameof(Robby.GetItemNeededCount), Prefix: nameof(RobbyItemQuery)),
            new(typeof(Robby), nameof(Robby.FindValidHolders), Prefix: nameof(HolderItemQuery), Postfix: nameof(HoldersFound)),
            new(typeof(PickupStimuli), nameof(PickupStimuli.Validate), Prefix: nameof(ValidatePickup), Postfix: nameof(ValidatedPickup), Arguments: new[] { typeof(VailActor) }),
            new(typeof(AiInventory), nameof(AiInventory.CanPickup), Prefix: nameof(CanPickup)),
            new(typeof(AiInventory), nameof(AiInventory.Pickup), Prefix: nameof(BeforeWorldPickup),
                Arguments: new[] { typeof(VailPickup) }, Finalizer: nameof(AfterWorldPickup)),
            new(typeof(AiInventory), nameof(AiInventory.Pickup), Prefix: nameof(PickingUp), Postfix: nameof(PickedUp),
                Arguments: new[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(VailPickup) }),
            new(typeof(AiInventory), nameof(AiInventory.ClearWorldItem), Prefix: nameof(CollectedWorldItem)),
            new(typeof(AiInventory), nameof(AiInventory.SetInitialCarry), Prefix: nameof(BeforeRestoreCarry), Finalizer: nameof(AfterRestoreCarry)),
            new(typeof(AiInventory), nameof(AiInventory.GetCount), Prefix: nameof(InventoryItemQuery)),
            new(typeof(AiInventory), nameof(AiInventory.IsHolding), Prefix: nameof(InventoryItemQuery)),
            new(typeof(AiInventory), nameof(AiInventory.IsFull), Prefix: nameof(InventoryItemQuery)),
            new(typeof(AiInventory), nameof(AiInventory.ShowAttachedPickup), Prefix: nameof(InventoryItemQuery)),
            new(typeof(VailActor), nameof(VailActor.GetInventoryCount), Prefix: nameof(ActorCountQuery)),
            new(typeof(VailActor), nameof(VailActor.GetNeededCount), Prefix: nameof(ActorNeededQuery)),
            new(typeof(VailActor), nameof(VailActor.PickupItem), Prefix: nameof(ActorItemQuery)),
            new(typeof(VailActor), nameof(VailActor.ShowPickupItem), Prefix: nameof(ActorItemQuery)),
            new(typeof(AiInventory), nameof(AiInventory.SetCarryParam), Prefix: nameof(CarryType)),
            new(typeof(VailPickupManager), nameof(VailPickupManager.VailItemToItemId), Prefix: nameof(ItemId)),
            new(typeof(VailPickupManager), nameof(VailPickupManager.ItemIdToVailItem), Prefix: nameof(ItemType)),
            new(typeof(VailPickupManager), nameof(VailPickupManager.GetTypeInfo), Prefix: nameof(PrepareTypeInfo)),
            new(typeof(VailPickupManager), nameof(VailPickupManager.GetMaxHeld), Prefix: nameof(MaxHeld)),
            new(typeof(VailPickupManager), nameof(VailPickupManager.ShouldDropHeldItem), Prefix: nameof(ShouldDrop)),
            new(typeof(InventoryTypeList), nameof(InventoryTypeList.GetTypeId), Prefix: nameof(TypeId)),
            new(typeof(InventoryTypeList), nameof(InventoryTypeList.GetGroupId), Prefix: nameof(GroupId)),
            new(typeof(InventoryTypeList), nameof(InventoryTypeList.TypeIdToString), Prefix: nameof(TypeName)),
            new(typeof(InWorldLayoutItemGroup), nameof(InWorldLayoutItemGroup.Sons_Ai_Vail_Inventory_IVailDropTarget_GetItemType), Prefix: nameof(HolderType)),
            new(typeof(InWorldLayoutItemGroup), nameof(InWorldLayoutItemGroup.Sons_Ai_Vail_Inventory_IVailDropTarget_TryAddItem), Prefix: nameof(HolderDeposit), Postfix: nameof(Deposited))
        };
    private static void Apply(IEnumerable<Spec> specs)
    {
        var harmony = _harmony ?? throw new InvalidOperationException("Kelvin hook owner is not ready.");
        var resolved = specs.Select(spec => (
            Target: AccessTools.Method(spec.Type, spec.Method, spec.Arguments) ?? throw new MissingMethodException(spec.Type.FullName, spec.Method),
            Prefix: PatchMethod(spec.Prefix), Postfix: PatchMethod(spec.Postfix), Finalizer: PatchMethod(spec.Finalizer))).ToArray();
        var applied = new List<(MethodInfo Target, MethodInfo? Prefix, MethodInfo? Postfix, MethodInfo? Finalizer)>();
        try
        {
            foreach (var spec in resolved)
            {
                applied.Add(spec);
                harmony.Patch(spec.Target, spec.Prefix == null ? null : new HarmonyMethod(spec.Prefix),
                    spec.Postfix == null ? null : new HarmonyMethod(spec.Postfix),
                    finalizer: spec.Finalizer == null ? null : new HarmonyMethod(spec.Finalizer));
            }
        }
        catch
        {
            foreach (var spec in applied)
                foreach (var patch in new[] { spec.Prefix, spec.Postfix, spec.Finalizer })
                    if (patch != null) harmony.Unpatch(spec.Target, patch);
            throw;
        }
    }

    private static MethodInfo? PatchMethod(string? name) => name == null ? null :
        AccessTools.Method(typeof(KelvinHooks), name) ?? throw new MissingMethodException(typeof(KelvinHooks).FullName, name);

    public static void PrepareOrders(Robby __instance) => KelvinLongSticks.EnsureOrders(__instance);
    public static void DropOptions(Robby __instance, int carouselId)
    {
        try { KelvinLongSticks.ShowDropOptions(__instance, carouselId); }
        catch (Exception ex) { KelvinLongSticks.Report("notebook options", ex); }
    }
    public static void BeforeOrder(Robby __instance, Robby.RobbyOrder order, out KelvinLongSticks.OrderScope? __state)
    { __state = KelvinLongSticks.Enabled ? new KelvinLongSticks.OrderScope(__instance, order) : null; }
    public static Exception? AfterOrder(KelvinLongSticks.OrderScope? __state, Exception? __exception)
    { __state?.Dispose(); return __exception; }
    public static void RobbyItemQuery(Robby __instance, ref string itemType)
    { if (KelvinLongSticks.Enabled) itemType = KelvinRules.ForOrder(itemType, KelvinLongSticks.StickType, KelvinLongSticks.IsLongTask(__instance)); }
    public static void HolderItemQuery(Robby __instance, ref string vailItemType)
    { if (KelvinLongSticks.Enabled) vailItemType = KelvinRules.ForOrder(vailItemType, KelvinLongSticks.StickType, KelvinLongSticks.IsLongTask(__instance)); }
    public static void HoldersFound(Robby __instance, bool __result)
    {
        try { KelvinLongSticks.HolderSearch(__instance, __result); }
        catch (Exception ex) { KelvinLongSticks.Report("holder diagnostic", ex); }
    }
    public static bool ValidatePickup(PickupStimuli __instance, VailActor vailActor, ref bool __result)
    {
        if (!KelvinLongSticks.Enabled || !KelvinLongSticks.IsLongTask(vailActor)) return true;
        try
        {
            if (KelvinLongSticks.AllowPickup(vailActor, __instance.GetPickupTarget()) == false)
            { __result = false; return false; }
        }
        catch (Exception ex) { KelvinLongSticks.Report("pickup validation", ex); }
        return true;
    }
    public static bool CanPickup(AiInventory __instance, VailPickup item, ref bool __result)
    {
        if (!KelvinLongSticks.Enabled) return true;
        try
        {
            if (KelvinLongSticks.AllowPickup(__instance, item) == false) { __result = false; return false; }
        }
        catch (Exception ex) { KelvinLongSticks.Report("inventory pickup validation", ex); }
        return true;
    }
    public static void ValidatedPickup(PickupStimuli __instance, VailActor vailActor, bool __result)
    {
        try { KelvinLongSticks.ValidateResult(__instance, vailActor, __result); }
        catch (Exception ex) { KelvinLongSticks.Report("pickup result diagnostic", ex); }
    }
    public static void PickedUp(AiInventory __instance, string itemType, VailPickup worldItem)
    {
        try { KelvinLongSticks.PickupResult(__instance, itemType); }
        catch (Exception ex) { KelvinLongSticks.Report("carried-item diagnostic", ex); }
    }
    public static void Deposited(InWorldLayoutItemGroup __instance, int itemId, int __result)
    {
        try { KelvinLongSticks.DepositResult(__instance, itemId, __result); }
        catch (Exception ex) { KelvinLongSticks.Report("holder deposit diagnostic", ex); }
    }
    public static bool PickingUp(AiInventory __instance, ref string itemType, VailPickup worldItem)
    {
        if (!KelvinLongSticks.Enabled) return true;
        try
        {
            if (KelvinLongSticks.ConfirmPickup(__instance, worldItem) == false) return false;
            if (KelvinLongSticks.IsLongTask(__instance) &&
                (itemType == KelvinLongSticks.StickType || itemType == KelvinRules.ItemType))
            {
                if (worldItem == null || !worldItem)
                {
                    if (!KelvinLongSticks.Gathering(__instance) &&
                        !(itemType == KelvinRules.ItemType && KelvinLongSticks.Restoring(__instance))) return false;
                    itemType = KelvinRules.ItemType; return true;
                }
                if (worldItem._itemType != KelvinRules.ItemType) return false;
                itemType = KelvinRules.ItemType;
            }
        }
        catch (Exception ex) { KelvinLongSticks.Report("gathering Long Stick", ex); return false; }
        return true;
    }
    public static bool BeforeWorldPickup(AiInventory __instance, VailPickup worldItem, out KelvinLongSticks.GatherScope? __state)
    {
        __state = null;
        if (!KelvinLongSticks.Enabled) return true;
        try
        {
            if (KelvinLongSticks.ConfirmPickup(__instance, worldItem) == false) return false;
            if (KelvinLongSticks.IsLongTask(__instance) && worldItem != null &&
                (worldItem._itemType == KelvinRules.ItemType || worldItem._itemType == KelvinLongSticks.StickType))
            {
                if (worldItem == null || !worldItem || worldItem._itemType != KelvinRules.ItemType) return false;
                __state = new KelvinLongSticks.GatherScope(__instance);
            }
        }
        catch (Exception ex) { KelvinLongSticks.Report("world pickup", ex); return false; }
        return true;
    }
    public static Exception? AfterWorldPickup(VailPickup worldItem, KelvinLongSticks.GatherScope? __state, Exception? __exception)
    { __state?.Dispose(); return __exception; }
    public static void CollectedWorldItem(VailPickup worldItem)
    {
        try { KelvinLongSticks.MarkCollected(worldItem); }
        catch (Exception ex) { KelvinLongSticks.Report("natural pickup collection", ex); }
    }
    public static void BeforeRestoreCarry(AiInventory __instance, string itemName, out KelvinLongSticks.RestoreScope? __state)
    {
        __state = null;
        if (!KelvinLongSticks.Enabled || itemName != KelvinRules.ItemType) return;
        KelvinHooks.InstallWork(); KelvinLongSticks.ConfigureCarryAliases(__instance);
        __state = new KelvinLongSticks.RestoreScope(__instance);
    }
    public static Exception? AfterRestoreCarry(KelvinLongSticks.RestoreScope? __state, Exception? __exception)
    { __state?.Dispose(); return __exception; }
    public static void InventoryItemQuery(AiInventory __instance, ref string itemType)
    { if (KelvinLongSticks.Enabled && itemType == KelvinLongSticks.StickType && KelvinLongSticks.IsLongTask(__instance)) itemType = KelvinRules.ItemType; }
    public static void ActorCountQuery(VailActor __instance, ref string itemNameId)
    { if (KelvinLongSticks.Enabled) itemNameId = KelvinRules.ForOrder(itemNameId, KelvinLongSticks.StickType, KelvinLongSticks.IsLongTask(__instance)); }
    public static void ActorNeededQuery(VailActor __instance, ref string itemName)
    { if (KelvinLongSticks.Enabled) itemName = KelvinRules.ForOrder(itemName, KelvinLongSticks.StickType, KelvinLongSticks.IsLongTask(__instance)); }
    public static void ActorItemQuery(VailActor __instance, ref string itemType)
    { if (KelvinLongSticks.Enabled) itemType = KelvinRules.ForOrder(itemType, KelvinLongSticks.StickType, KelvinLongSticks.IsLongTask(__instance)); }
    public static void CarryParent(ref string itemName)
    { if (KelvinLongSticks.Enabled) itemName = KelvinRules.ForCarryAnimation(itemName, KelvinLongSticks.StickType); }
    public static void CarryType(ref string itemType)
    { if (KelvinLongSticks.Enabled) itemType = KelvinRules.ForCarryAnimation(itemType, KelvinLongSticks.StickType); }
    public static bool ItemId(string vailItemName, ref int __result)
    {
        if (!KelvinLongSticks.Enabled || vailItemName != KelvinRules.ItemType) return true;
        __result = Rules.LongStick; return false;
    }
    public static bool ItemType(int itemId, ref string __result)
    {
        if (!KelvinLongSticks.Enabled || itemId != Rules.LongStick) return true;
        __result = KelvinRules.ItemType; return false;
    }
    public static bool MaxHeld(string itemType, ref int __result)
    {
        if (!KelvinLongSticks.Enabled || itemType != KelvinRules.ItemType) return true;
        KelvinLongSticks.PreparePickupManager();
        __result = KelvinLongSticks.MaxHeld; return false;
    }
    public static void PrepareTypeInfo(string itemType)
    { if (KelvinLongSticks.Enabled && itemType == KelvinRules.ItemType) KelvinLongSticks.PreparePickupManager(); }
    public static bool ShouldDrop(string itemType, ref bool __result)
    {
        if (!KelvinLongSticks.Enabled || itemType != KelvinRules.ItemType) return true;
        __result = VailPickupManager.ShouldDropHeldItem(KelvinLongSticks.StickType); return false;
    }
    public static bool TypeId(string itemType, ref int __result)
    {
        if (!KelvinLongSticks.Enabled || KelvinLongSticks.TypeId < 0 || itemType != KelvinRules.ItemType) return true;
        __result = KelvinLongSticks.TypeId; return false;
    }
    public static bool GroupId(string itemType, ref int __result)
    {
        if (!KelvinLongSticks.Enabled || KelvinLongSticks.TypeId < 0 || itemType != KelvinRules.ItemType) return true;
        __result = KelvinLongSticks.GroupId; return false;
    }
    public static bool TypeName(int typeId, ref string __result)
    {
        if (!KelvinLongSticks.Enabled || KelvinLongSticks.TypeId < 0 || !KelvinLongSticks.IsLongTypeId(typeId)) return true;
        __result = KelvinRules.ItemType; return false;
    }
    public static bool HolderType(InWorldLayoutItemGroup __instance, ref string __result)
    {
        if (!KelvinLongSticks.IsLongHolder(__instance)) return true;
        __result = KelvinRules.ItemType; return false;
    }
    public static bool HolderDeposit(InWorldLayoutItemGroup __instance, int itemId, ref int __result)
    {
        if (!KelvinLongSticks.IsLongHolder(__instance) || itemId == Rules.LongStick) return true;
        __result = 0; return false;
    }
}
