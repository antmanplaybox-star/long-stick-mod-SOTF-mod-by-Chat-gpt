using LongStickSpear.Core;
using Sons.Gameplay;
using Sons.Inventory;
using Sons.Items.Core;
using SonsSdk;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LongStickSpear;

internal static class LongStickItem
{
    private static ItemData? _item;
    private static GameObject? _templates;
    internal static ItemData? Data => _item != null && _item ? _item : null;

    internal static ItemData Register()
    {
        if (ItemDatabaseManager._itemsCache == null || ItemDatabaseManager._instance == null)
            throw new InvalidOperationException("The item database is not ready yet.");

        if (ItemTools.IsItemRegistered(Rules.LongStick))
        {
            var registered = ItemDatabaseManager.ItemById(Rules.LongStick);
            if (_item == null || !registered || registered.Pointer != _item.Pointer)
                throw new InvalidOperationException($"Item ID {Rules.LongStick} is already used by another item. Nothing was overwritten.");
            return _item;
        }

        var stick = ItemDatabaseManager.ItemById(Rules.Stick);
        var feather = ItemDatabaseManager.ItemById(ItemTools.Identifiers.Feather);
        if (!stick || !feather || !stick.PickupPrefab)
            throw new InvalidOperationException("The vanilla stick pickup is not ready yet.");

        var item = Object.Instantiate(feather);
        item.name = "LongStickSpear_ItemData";
        item._id = Rules.LongStick;
        item._name = Rules.ItemName;
        item._editorName = Rules.ItemName;
        item._maxAmount = Rules.Capacity;
        // Retain the SDK template's native crafting flags instead of replacing
        // its entire type mask with a guessed subset.
        item._type = feather._type | Sons.Items.Core.Types.CraftingMaterial | Sons.Items.Core.Types.Droppable;
        item._eachInstanceIsUnique = false;
        item._isStructureElement = false;
        item._isLegacyItem = false;
        item._canBeSpawned = true;
        item._excludeFromSpawnAll = false;
        item._allowPickupWhenInventoryIsFull = false;
        item._preventEquipFromInventoryView = feather._preventEquipFromInventoryView;
        item._allowQuickSelect = false;
        item._canBeHotKeyed = false;
        item._canNotBeStored = false;
        item._hasFirstLook = false;
        item._isPerishable = false;
        item._isVolumeContainer = false;
        item._hasVisualVariant = false;
        item._requiresBothHands = false;
        item._heldPrefab = null;
        item._propPrefab = null;
        item._pickupBundlePrefab = null;
        item._pickupCanBeBundled = false;
        item._maxCountPerBundle = 1;
        item._craftingIngredientController = feather._craftingIngredientController;
        item._tags = new Il2CppSystem.Collections.Generic.List<string>();
        item._aliases = new Il2CppSystem.Collections.Generic.List<string>();
        item._aliases.Add("longstick");
        item._aliases.Add("long stick");
        // Allocate UI data explicitly: never alter another item's shared object.
        item._uiData = new ItemUiData {
            _itemId = Rules.LongStick,
            _title = Rules.ItemName,
            _titlePlural = "Long Sticks",
            _description = "A stick twice the normal length. Combine one with a knife to craft a spear without duct tape.",
            _translationKey = "LongStickSpear_Item",
            _icon = stick._uiData._icon,
            _outlineIcon = stick._uiData._outlineIcon,
            _uiColors = stick._uiData._uiColors,
            _leftClick = ItemUiData.LeftClickCommands.none,
            _rightClick = ItemUiData.RightClickCommands.Combine,
            _amountDisplay = ItemUiData.AmountDisplay.Amount,
            _showGotItem = true,
            _showCollectedItem = true,
            _showCantCarryMoreItem = true
        };

        GameObject? holder = null;
        try
        {
            holder = new GameObject("LongStickSpear_InactiveTemplates");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var pickupObject = Object.Instantiate(stick.PickupPrefab.gameObject, holder.transform, false);
            pickupObject.name = "LongStickSpear_Pickup";
            var pickup = pickupObject.GetComponent<PickUp>() ?? pickupObject.GetComponentInChildren<PickUp>(true);
            if (pickup == null) throw new InvalidOperationException("Vanilla stick prefab has no PickUp component.");
            pickup._itemId = Rules.LongStick;
            pickup._itemDataCached = item;
            pickup.ItemInstance = null;
            pickup._amountToAdd = 1;
            pickup._forceAutoEquip = false;
            pickup._preventAutoEquip = true;
            pickup._destroyIfFull = false;
            pickup._destroyIfAlreadyOwned = false;
            pickup._hideIfAlreadyOwned = false;
            pickup._additionalItems = new Il2CppSystem.Collections.Generic.List<ItemAndRange>();
            pickup._poolManagerPool = "";
            pickup._poolManagerDespawnCreature = false;
            pickup._disableInsteadOfDestroy = false;
            pickup._isCollected = false;
            pickup._isBeingDestroyed = false;
            pickup._pendingDestroyThroughBolt = false;
            pickup._remainingItemCountNotCollected = 0;
            foreach (var renderable in pickupObject.GetComponentsInChildren<ItemRenderable>(true))
                ConfigureRenderable(renderable);
            LongStickVisuals.Attach(pickupObject);
            KelvinLongSticks.ConfigurePickup(pickupObject);
            pickupObject.SetActive(true); // Parent stays inactive: this is a template.
            item._pickupPrefab = pickupObject.transform;
            ItemTools.RegisterItem(item);
            _item = item; _templates = holder;
            Mod.Info($"Registered {Rules.ItemName}, ID={Rules.LongStick}, capacity={Rules.Capacity}. Vanilla item data is unchanged.");
            return item;
        }
        catch
        {
            if (holder != null) Object.Destroy(holder);
            Object.Destroy(item);
            throw;
        }
    }

    internal static void ConfigureRenderable(ItemRenderable renderable)
    {
        renderable.SetItemId(Rules.LongStick);
        renderable._hasVariations = false;
        renderable._useVariations = false;
        renderable._useAnimated = false;
        renderable._overrideAnimatorController = null;
        renderable._playOnLoad = false;
        renderable._cachedLoadedObject = null;
    }
}
