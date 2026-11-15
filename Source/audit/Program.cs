using Mono.Cecil;
using System.Text.Json;
if (args.Length is < 3 or > 5) throw new ArgumentException("Expected mod DLL, Game directory, manifest, optionally the crash and latest-session logs.");
using var mod = ModuleDefinition.ReadModule(args[0]);
using var game = ModuleDefinition.ReadModule(Path.Combine(args[1], "Sons.dll"));
using var endnight = ModuleDefinition.ReadModule(Path.Combine(args[1], "Endnight.dll"));
using var environment = ModuleDefinition.ReadModule(Path.Combine(args[1], "Sons.Environment.dll"));
using var vail = ModuleDefinition.ReadModule(Path.Combine(args[1], "Sons.Ai.Vail.dll"));
var main = mod.Types.Single(t => t.FullName == "LongStickSpear.Mod");
var hooks = main.NestedTypes.Single(t => t.Name == "Hooks");
var kelvin = mod.Types.Single(t => t.Name == "KelvinLongSticks");
var registration = kelvin.Methods.Single(m => m.Name == "EnsureTypeIds");
const string aiTypeInfo = "Sons.Ai.Vail.Inventory.InventoryTypeList/TypeInfo";
bool TypeTableCall(MethodReference method) => method.DeclaringType is GenericInstanceType table &&
    table.ElementType.FullName == "Il2CppSystem.Collections.Generic.List`1" && table.GenericArguments.Any(t => t.FullName == aiTypeInfo);
Assert(!Calls(registration).Any(m => TypeTableCall(m) && m.Name is "Add" or "Insert" or "set_Item"),
    "Unsafe generic native struct insertion can corrupt Kelvin's shared type table and crash the next load");
var registrationCalls = Calls(registration).ToArray();
var boxedAppendAt = Array.FindIndex(registrationCalls, m => TypeTableCall(m) && m.Name == "System_Collections_IList_Add" &&
    m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "Il2CppSystem.Object");
Assert(boxedAppendAt >= 0, "Native boxed-object insertion required for the AI type struct");
foreach (var getter in new[] { "get_Item", "get_TypeId", "get_GroupId", "get_StringId" })
    Assert(registrationCalls.Skip(boxedAppendAt + 1).Any(m => m.Name == getter), "New native type payload is not read back: " + getter);
using var nativeCore = ModuleDefinition.ReadModule(Path.Combine(args[1], "Il2Cppmscorlib.dll"));
var typeWrapper = vail.Types.Single(t => t.FullName == "Sons.Ai.Vail.Inventory.InventoryTypeList").NestedTypes.Single(t => t.Name == "TypeInfo");
Assert(typeWrapper.BaseType.FullName == "Il2CppSystem.ValueType" && !typeWrapper.IsValueType, "Recheck native TypeInfo representation for this loader");
var nativeList = nativeCore.Types.Single(t => t.FullName == "Il2CppSystem.Collections.Generic.List`1");
var genericAdd = nativeList.Methods.Single(m => m.Name == "Add");
Assert(Calls(genericAdd).Any(m => m.Name == "get_IsValueType") && Calls(genericAdd).Any(m => m.Name == "Il2CppObjectBaseToPtr") &&
    !Calls(genericAdd).Any(m => m.Name == "il2cpp_object_unbox"), "Recheck generated generic Add marshalling for this loader");
var objectAdd = nativeList.Methods.Single(m => m.Name == "System_Collections_IList_Add");
Assert(objectAdd.Parameters.Single().ParameterType.FullName == "Il2CppSystem.Object" && objectAdd.ReturnType.FullName == "System.Int32" &&
    Calls(objectAdd).Any(m => m.Name == "il2cpp_runtime_invoke"), "Native boxed IList signature is unavailable");
Console.WriteLine("PASS native AI struct registration: generated generic Add's boxed-header path is absent; the native IList object overload accepts the box and its stored ID/group/name are read back.");
Assert(!mod.Types.Any(t => t.Name == "TreePrefabRewriter"), "Whole-tree prefab replacement remains compiled");
Assert(!mod.Types.Any(t => t.Name == "WorldPlacementSnapshot"), "Obsolete terrain-placement override remains compiled");
Assert(mod.Types.Any(t => t.Name == "NaturalGroundScatter"), "Dedicated ground-scatter source discovery is missing");
Assert(mod.Types.Any(t => t.Name == "KelvinLongSticks"), "Kelvin Long Stick support is missing");
var kelvinHooks = mod.Types.Single(t => t.Name == "KelvinHooks");
Assert(kelvinHooks.Methods.Any(m => m.Name == "InstallWork"), "Kelvin callbacks are still installed for every actor at load");
Assert(mod.Types.Single(t => t.Name == "KelvinLongSticks").Methods.Any(m => m.Name == "TrackOwner"), "Long Stick sources still depend on base pickup callbacks");
Assert(mod.Types.Single(t => t.Name == "StorageBookArt").Methods.Any(m => m.Name == "Request"), "Book illustration is still copied immediately before detailed mip loading");
Assert(mod.Types.Any(t => t.Name == "StorageBookLabels"), "The custom blueprint caption has no explicit native-label binding");
var animationRecipe = mod.Types.Single(t => t.Name == "CraftingAndInventory").Methods.Single(m => m.Name == "EnsureRecipe");
var animationRoute = Calls(animationRecipe).ToArray();
var sourceAnimationAt = Array.FindIndex(animationRoute, m => m.DeclaringType.Name == "CraftingRecipe" && m.Name == "get_AnimationStateName");
var chosenAnimationAt = Array.FindIndex(animationRoute, m => m.DeclaringType.Name == "RecipeBuilder" && m.Name == "Animation");
Assert(sourceAnimationAt >= 0 && chosenAnimationAt > sourceAnimationAt && !animationRecipe.Body.Instructions.Any(i => Equals(i.Operand, "CraftHerbMix")), "Alternate spear recipe still uses a generic animation instead of the original spear state");
var configInit = mod.Types.Single(t => t.Name == "Config").Methods.Single(m => m.Name == "Init");
var rangeCode = configInit.Body.Instructions;
var rangeAt = Enumerable.Range(0, rangeCode.Count).FirstOrDefault(i => rangeCode[i].Operand is MethodReference m && m.Name == "SetRange", -1);
Assert(rangeAt >= 2 && rangeCode[rangeAt - 2].Operand is float min && min == 0f &&
    rangeCode[rangeAt - 1].Operand is float max && max == 100f, "Natural chance slider has no explicit 0..100 range");
Console.WriteLine("PASS explicit slider metadata range 0..100; SDK default maximum of 10 is not used.");
var targets = new[] {
    (game, "Sons.Gameplay.BreakableObject", "SpawnDrops", "BeforeDrops"),
    (game, "Sons.Gameplay.BreakableObject", "SpawnDrops", "AfterDrops"),
    (game, "Sons.Gameplay.BreakableObject", "SpawnDrops", "DropFinalizer"),
    (game, "Sons.Gameplay.PickUp", "OnEnable", "PickupReady"),
    (game, "Sons.Gameplay.PickUp", "SetItemInstance", "PickupReady"),
    (game, "Sons.Crafting.CraftingSystem", "Initialize", "CraftingInitialize"),
    (game, "Sons.Crafting.CraftingSystem", "Initialize", "CraftingInitialized"),
    (game, "Sons.Crafting.CraftingSystem", "OnEnable", "CraftingOpened"),
    (game, "Sons.Crafting.CraftingSystem", "AddIngredient", "IngredientAdded"),
    (game, "Sons.Crafting.CraftingSystem", "OnRevealResultingItemInternal", "CraftReveal"),
    (game, "Sons.Crafting.CraftingSystem", "OnCraftCompleteInternal", "CraftComplete"),
    (game, "Sons.Crafting.CraftingSystem", "TryCombineIngredients", "BeforeCombine"),
    (game, "Sons.Crafting.CraftingSystem", "TryCombineIngredients", "AfterCombine"),
    (game, "Sons.Crafting.CraftingSystem", "TryCombineIngredients", "CombineFinalizer"),
    (game, "Sons.Inventory.InventoryLayoutItemGroup", "OnAddItemToMat", "BeforeAddToMat"),
    (game, "Sons.Inventory.InventoryLayoutItemGroup", "OnAddItemToMat", "AfterAddToMat"),
    (game, "Sons.Crafting.Structures.ScrewStructureManager", "OnBeforeLoad", "StructuresBeforeLoad"),
    (game, "Sons.Weapon.BlueprintBookController", "Initialize", "BookInitialize"),
    (game, "Sons.Weapon.BlueprintBookController", "UpdatePageLocalization", "BookLocalizationUpdated"),
    (game, "Sons.Inventory.StructureStorage", "ValidateItemPlacement", "ValidateStorageItem"),
    (game, "Sons.Inventory.ItemInstanceManager", "TryAddItems", "BeforeStorageAddItems"),
    (game, "Sons.Inventory.ItemInstanceManager", "TryAddItem", "BeforeStorageAddInstance"),
    (game, "Sons.Inventory.ItemInstanceManager", "DeserializeItemsFromExternalData", "StorageDeserialized"),
    (game, "Sons.Gameplay.TreeCutting.TreeCutManager", "FellTree", "BeforeTreeFell"),
    (game, "Sons.Gameplay.VirtualPickup", "Setup", "NaturalVirtualData"),
    (game, "Sons.Gameplay.VirtualPickupSwitcher", "GeneratePickupData", "NaturalSceneData"),
    (game, "Sons.Gameplay.VirtualPickup", "SwitchToReal", "NaturalVirtualReal"),
    (game, "GreebleZone", "MainThreadRefresh", "GroundZoneRefresh"),
    (game, "GreebleZone", "SpawnIndex", "GroundZoneSpawn"),
    (game, "Sons.Gameplay.PickUp", "GrabEnter", "GroundStickLook")
}.Concat(new[] {
    (game, "Sons.Gameplay.Robby", "Start", "PrepareOrders"),
    (game, "Sons.Gameplay.Robby", "BuildTactipadUI", "PrepareOrders"),
    (game, "Sons.Gameplay.Robby", "RestoreSavedOrders", "PrepareOrders"),
    (game, "Sons.Gameplay.Robby", "PreShowCarousel", "DropOptions"),
    (game, "Sons.Gameplay.Robby", "RunOrder", "BeforeOrder"),
    (game, "Sons.Gameplay.Robby", "RunOrder", "AfterOrder"),
    (game, "Sons.Gameplay.Robby", "GetItemNeededCount", "RobbyItemQuery"),
    (game, "Sons.Gameplay.Robby", "FindValidHolders", "HolderItemQuery"),
    (game, "Sons.Gameplay.Robby", "FindValidHolders", "HoldersFound"),
    (vail, "Sons.Ai.Vail.StimuliTypes.PickupStimuli", "Validate", "ValidatePickup"),
    (vail, "Sons.Ai.Vail.StimuliTypes.PickupStimuli", "Validate", "ValidatedPickup"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "CanPickup", "CanPickup"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "Pickup", "BeforeWorldPickup"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "Pickup", "AfterWorldPickup"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "Pickup", "PickingUp"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "Pickup", "PickedUp"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "ClearWorldItem", "CollectedWorldItem"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "SetInitialCarry", "BeforeRestoreCarry"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "SetInitialCarry", "AfterRestoreCarry"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "GetCount", "InventoryItemQuery"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "IsHolding", "InventoryItemQuery"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "IsFull", "InventoryItemQuery"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "ShowAttachedPickup", "InventoryItemQuery"),
    (vail, "Sons.Ai.Vail.VailActor", "GetInventoryCount", "ActorCountQuery"),
    (vail, "Sons.Ai.Vail.VailActor", "GetNeededCount", "ActorNeededQuery"),
    (vail, "Sons.Ai.Vail.VailActor", "PickupItem", "ActorItemQuery"),
    (vail, "Sons.Ai.Vail.VailActor", "ShowPickupItem", "ActorItemQuery"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryManager", "SetCarryParam", "CarryType"),
    (vail, "Sons.Ai.Vail.Inventory.VailPickupManager", "VailItemToItemId", "ItemId"),
    (vail, "Sons.Ai.Vail.Inventory.VailPickupManager", "ItemIdToVailItem", "ItemType"),
    (vail, "Sons.Ai.Vail.Inventory.VailPickupManager", "GetTypeInfo", "PrepareTypeInfo"),
    (vail, "Sons.Ai.Vail.Inventory.VailPickupManager", "GetMaxHeld", "MaxHeld"),
    (vail, "Sons.Ai.Vail.Inventory.VailPickupManager", "ShouldDropHeldItem", "ShouldDrop"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryTypeList", "GetTypeId", "TypeId"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryTypeList", "GetGroupId", "GroupId"),
    (vail, "Sons.Ai.Vail.Inventory.InventoryTypeList", "TypeIdToString", "TypeName"),
    (game, "Sons.Inventory.InWorldLayoutItemGroup", "Sons_Ai_Vail_Inventory_IVailDropTarget_GetItemType", "HolderType"),
    (game, "Sons.Inventory.InWorldLayoutItemGroup", "Sons_Ai_Vail_Inventory_IVailDropTarget_TryAddItem", "HolderDeposit"),
    (game, "Sons.Inventory.InWorldLayoutItemGroup", "Sons_Ai_Vail_Inventory_IVailDropTarget_TryAddItem", "Deposited")
}).ToArray();
foreach (var (assembly, name, method, hook) in targets)
{
    var managed = hooks.Methods.FirstOrDefault(m => m.Name == hook) ?? kelvinHooks.Methods.Single(m => m.Name == hook);
    var inputs = managed.Parameters.Where(p => !p.Name.StartsWith("__")).ToArray();
    var nativeCandidates = assembly.Types.Single(t => t.FullName == name).Methods.Where(m => m.Name == method);
    if (hook is "BeforeWorldPickup" or "AfterWorldPickup") nativeCandidates = nativeCandidates.Where(m => m.Parameters.Count == 1);
    var native = nativeCandidates
        .Single(m => inputs.All(p => m.Parameters.Any(n => n.Name == p.Name && n.ParameterType.FullName == p.ParameterType.FullName.TrimEnd('&'))));
    Assert(!native.IsAbstract, "Abstract patch target");
    foreach (var p in managed.Parameters)
    {
        if (p.Name == "__state") {
            var expected = hook switch {
                "BeforeDrops" or "AfterDrops" or "DropFinalizer" => "LongStickSpear.DropSnapshot",
                "BeforeOrder" or "AfterOrder" => "LongStickSpear.KelvinLongSticks/OrderScope",
                "BeforeRestoreCarry" or "AfterRestoreCarry" => "LongStickSpear.KelvinLongSticks/RestoreScope",
                "BeforeWorldPickup" or "AfterWorldPickup" => "LongStickSpear.KelvinLongSticks/GatherScope",
                _ => "System.Boolean"
            };
            Assert(p.ParameterType.FullName.TrimEnd('&') == expected, "State type"); continue;
        }
        if (p.Name == "__result") { Assert(p.ParameterType.FullName.TrimEnd('&') == native.ReturnType.FullName, "Native result type"); continue; }
        if (p.Name == "__exception") { Assert(p.ParameterType.FullName == "System.Exception", "Exception type"); continue; }
        if (p.Name == "__instance") { Assert(!native.IsStatic && p.ParameterType.FullName == name, "Native instance type"); continue; }
        var input = native.Parameters.Single(n => n.Name == p.Name);
        Assert(p.ParameterType.FullName.TrimEnd('&') == input.ParameterType.FullName, "Native parameter mismatch");
    }
    Console.WriteLine($"PASS native signature: {name}.{method} -> {hook}");
}
var methods = mod.Types.SelectMany(Walk).SelectMany(t => t.Methods).ToArray();
var calls = methods.SelectMany(Calls).ToArray();
foreach (var write in calls.Where(m => m.Name is "Add" or "Insert" or "set_Item" &&
    m.DeclaringType is GenericInstanceType listType && listType.ElementType.FullName == "Il2CppSystem.Collections.Generic.List`1"))
{
    var argument = ((GenericInstanceType)write.DeclaringType).GenericArguments.Single();
    if (argument.Scope is not AssemblyNameReference assembly) continue;
    var assemblyPath = Path.Combine(args[1], assembly.Name + ".dll");
    if (!File.Exists(assemblyPath)) continue;
    using var reference = ModuleDefinition.ReadModule(assemblyPath);
    var argumentType = reference.Types.SelectMany(Walk).SingleOrDefault(t => t.FullName == argument.FullName);
    Assert(argumentType?.BaseType?.FullName != "Il2CppSystem.ValueType", "Another native struct uses the same unsafe generic list write: " + write.FullName);
}
Console.WriteLine("PASS all other native generic list writes use reference types; no additional boxed native struct insertion remains in this assembly.");
var initCalls = Calls(main.Methods.Single(m => m.Name == "OnInitializeMod")).ToArray();
Assert(!initCalls.Any(m => m.DeclaringType.Namespace.StartsWith("UnityEngine") || m.DeclaringType.Namespace.StartsWith("TheForest") || m.Name == "Register"), "Native game data accessed before SDK ready");
Assert(Calls(main.Methods.Single(m => m.Name == "OnSdkInitialized")).Any(m => m.DeclaringType.Name == "LongStickItem" && m.Name == "Register"), "Item registration not at SDK initialization");
Assert(Calls(main.Methods.Single(m => m.Name == "BeforeLoadSave")).Any(m => m.DeclaringType.Name == "LongStickItem" && m.Name == "Register"), "No before-save-load registration check");
Console.WriteLine("PASS item registration occurs after SDK startup and is checked before save loading.");
var sdkInitCalls = Calls(main.Methods.Single(m => m.Name == "OnSdkInitialized")).ToArray();
Assert(!sdkInitCalls.Any(m => m.Name.StartsWith("Install")), "Native hook installation can run before the main menu");
Assert(Calls(main.Methods.Single(m => m.Name == "BeforeLoadSave")).Any(m => m.Name == "InstallHooks"), "Base save hooks are installed too late");
foreach (var method in new[] { "OnGameStart", "QueueSetup" })
    Assert(!Calls(main.Methods.Single(m => m.Name == method)).Any(m => m.Name == "TrySetup"), "Inventory setup still runs inside startup/spawn dispatch");
var update = main.Methods.Single(m => m.Name == "UpdateSetup");
Assert(Calls(update).Any(m => m.Name == "TrySetup"), "Deferred inventory setup is missing");
Assert(update.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_warmupFrames"), "No warmup frame guard");
Assert(main.Methods.Single(m => m.Name == "OnGameStart").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_warmupFrames"), "World startup does not stage inventory setup");
var forbiddenHooks = new[] { "Awake", "SpawnInitialize", "OnSpawned", "OnImpact", "OnCollisionEnter", "DoSpawn", "SpawnFallingTree", "Spawn", "ActivationProcess", "ForceActivationProcess" };
foreach (var forbidden in forbiddenHooks)
    Assert(!main.Methods.Where(m => m.Name.StartsWith("Install")).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => Equals(i.Operand, forbidden)), "Unsafe global interception remains: " + forbidden);
foreach (var removed in new[] { "FallingTreeDrops", "FallingTreeStickSource", "TreePickupConversion", "TreeStickPickupOverride" })
    Assert(!mod.Types.SelectMany(Walk).Any(t => t.Name == removed), "Removed live-conversion type is still compiled: " + removed);
Assert(!hooks.Methods.Any(m => m.Name is "BeforeTreeImpact" or "BeforeTreeCollision" or "TreePickupInitializing" or "TreePickupDisabled" or "BeforePrefabSpawn" or "BeforeFallPickupSpawn"), "Global impact/spawn/pool callback remains");
Assert(Calls(hooks.Methods.Single(m => m.Name == "PickupReady")).First().Name == "get_WorldActive", "Appearance callback accesses native state at the main menu");
Assert(!Calls(hooks.Methods.Single(m => m.Name == "PickupReady")).Any(m => m.Name == "set__itemId"), "Live pickup appearance callback changes item identity");
Console.WriteLine("PASS no SDK/menu hook installation; inventory setup is deferred beyond startup dispatch; global impact, spawner and pool-conversion hooks/types are absent.");
Assert(!calls.Any(m => m.DeclaringType.FullName == "UnityEngine.Object" && m.Name.StartsWith("FindObjects")), "Unbounded world scan");
Assert(!calls.Any(m => m.Name is "Collect" or "RemoveItem" or "RemoveAll" or "TryAddItems"), "Tree replacement bypasses native drops or directly changes inventory");
Console.WriteLine("PASS no global world scans and no tree-loot inventory injection.");
var snapshot = mod.Types.Single(t => t.Name == "DropSnapshot");
var restore = Calls(snapshot.Methods.Single(m => m.Name == "Restore")).Select(m => m.Name).ToArray();
foreach (var field in new[] { "set__spawnDefinitions", "set__pickupPrefab" })
    Assert(restore.Contains(field), "Incomplete direct-loot restoration: " + field);
Assert(!restore.Any(n => n is "set__brokenPrefab" or "set__converted" or "set__spawnCount"), "Drop cleanup changes native body, migration or count state");
foreach (var hook in new[] { "AfterDrops", "DropFinalizer" })
    Assert(Calls(hooks.Methods.Single(m => m.Name == hook)).Any(m => m.Name == "Restore"), "Missing drop cleanup");
Console.WriteLine("PASS direct loot references restored on completion/errors; no restoration writes to native body or count state.");
var treeLoot = mod.Types.Single(t => t.Name == "TreeLoot");
var treeCalls = Walk(treeLoot).SelectMany(t => t.Methods).SelectMany(Calls).ToArray();
Assert(!treeCalls.Any(m => m.DeclaringType.FullName == "UnityEngine.Object" && m.Name is "Instantiate" or "Destroy"), "Falling-tree body is cloned or destroyed");
Assert(!treeCalls.Any(m => m.DeclaringType.FullName == "UnityEngine.GameObject" && m.Name is ".ctor" or "SetActive"), "Falling-tree body activation is changed");
Assert(!treeCalls.Any(m => m.DeclaringType.FullName == "UnityEngine.Transform" && (m.Name.StartsWith("set_") || m.Name == "SetParent")), "Falling-tree hierarchy, position or scale is changed");
Assert(!calls.Any(m => m.Name is "set__trunkFractured" or "set__trunkFracturedExtras"), "Native falling body or extras reference is replaced");
Assert(!mod.Types.Any(t => t.Name == "TreeFellSnapshot"), "Whole-tree reference snapshot remains");
Assert(!treeCalls.Any(m => m.Name is "GetComponentInParent" or "RegisterTypeInIl2Cpp"), "Injected ancestry/lifecycle behavior remains in tree handling");
Assert(Calls(treeLoot.Methods.Single(m => m.Name == "PrepareFell")).Any(m => m.Name == "get__trunkFractured"), "Known native tree source is not used");
Assert(Calls(treeLoot.Methods.Single(m => m.Name == "Prepare")).Any(m => m.DeclaringType.Name == "Rules" && m.Name == "CanEditFallingTree"), "Log-producing trees are not excluded using the tested rule");
Assert(treeCalls.Any(m => m.DeclaringType.Name == "Rules" && m.Name == "IsLogSource"), "Native log yield classification missing");
Assert(treeCalls.Where(m => m.DeclaringType.FullName == "Sons.VFX.CollisionTriggerActivator" && m.Name.StartsWith("set_")).All(m => m.Name == "set__spawnDefinitions"), "Collision settings or activation groups are changed");
foreach (var field in new[] { "_source", "_location", "_parent", "_ignoreBoltEntities" })
    Assert(Calls(treeLoot.Methods.Single(m => m.Name == "CopyDefinition")).Any(m => m.Name == "set_" + field), "Collision definition loses native field: " + field);
Assert(Calls(treeLoot.Methods.Single(m => m.Name == "Reset")).Any(m => m.Name == "RestoreEdits"), "World cleanup does not restore loot lists");
Assert(Calls(main.Methods.Single(m => m.Name == "WorldExited")).Any(m => m.DeclaringType.Name == "TreeLoot" && m.Name == "Reset"), "World exit does not release edited native loot lists");
Assert(Calls(treeLoot.Methods.Single(m => m.Name == "Update")).Any(m => m.Name == "RestoreEdits"), "Disabling replacements does not restore native loot lists");
Assert(!treeCalls.Any(m => m.Name == "set__itemId"), "Live pickup identity is changed");
Console.WriteLine("PASS original falling body/extras/hierarchy/activation retained, log-producing trees excluded, native loot lists restored on disable/exit, and no live pickup conversion.");
var treeDrops = mod.Types.Single(t => t.Name == "TreeDrops");
var treePrepare = treeDrops.Methods.Single(m => m.Name == "Prepare");
foreach (var name in new[] { "ReplaceStickReferences", "IsVegetationSource", "CanReplaceDirectStick", "IsLogPickup" })
    Assert(Walk(treeDrops).SelectMany(t => t.Methods).SelectMany(Calls).Any(m => m.Name == name), "Direct plant-loot guard missing: " + name);
Assert(!Walk(treeDrops).SelectMany(t => t.Methods).SelectMany(Calls).Any(m => m.Name == "set__brokenPrefab"), "Small-plant falling/cut body reference is changed");
var makeLong = Calls(treeDrops.Methods.Single(m => m.Name == "MakeLongReference")).ToArray();
Assert(makeLong.Any(m => m.Name == "CopyDefinition") && !makeLong.Any(m => m.Name.StartsWith("set_")), "Identity substitution changes native count/settings");
foreach (var field in new[] { "_spawnCount", "_prefab", "_rotation", "_startHeightOffset", "_shouldStackSpawns", "_stackHeightOffset", "_makeRigidBodyDynamic", "_lifeTime", "_copyOriginalObjectsScale", "_addExplosiveForceRigidBodies", "_explosionForce", "_explosiveUpwardsModifier", "_linkToObjectActiveState", "_linkedObjectActiveStateSource" })
    Assert(Calls(treeDrops.Methods.Single(m => m.Name == "CopyDefinition")).Any(m => m.Name == "set_" + field), "Drop definition copy loses a native setting: " + field);
Console.WriteLine("PASS positive direct plant loot only, intact cut-body references, native quantities and complete spawn-definition fields.");
var natural = mod.Types.Single(t => t.Name == "NaturalWorldSticks");
var naturalCalls = Walk(natural).SelectMany(t => t.Methods).SelectMany(Calls).ToArray();
Assert(!hooks.Methods.Any(m => m.Name is "NaturalWorldPrefab" or "BeforeNaturalWorldRefresh"), "Wrong terrain source interception remains");
var registryCalls = Calls(natural.Methods.Single(m => m.Name == "InspectWorldRegistry")).ToArray();
foreach (var name in new[] { "TryGetInstance", "get__worldItems", "get__itemState", "get_RuntimeCreated", "get_State", "get__pickUpComponent", "IsAuthoredWorldItem" })
    Assert(registryCalls.Any(m => m.Name == name), "Authored world-item origin guard missing: " + name);
Assert(!registryCalls.Any(m => m.Name == "GetEnumerator"), "Mutable native world list is enumerated without a budget/index");
foreach (var name in new[] { "CaptureData", "CaptureProxy" })
    Assert(Calls(natural.Methods.Single(m => m.Name == name)).Any(m => m.DeclaringType.Name == "NaturalSpotRules" && m.Name == "IsNatural"), "Virtual static-source filter missing: " + name);
Assert(naturalCalls.Any(m => m.Name == "get_RuntimeCreated") && naturalCalls.Any(m => m.Name == "get__runtimeCreated"), "Runtime-created source flags are ignored");
Assert(!naturalCalls.Any(m => m.Name is "set__itemId" or "set__itemID" or "Collect" or "set__data"), "Natural source processing changes an existing pickup or world locator");
Assert(!naturalCalls.Any(m => m.DeclaringType.FullName == "Sons.Inventory.WorldItemState" && m.Name.StartsWith("set_")), "Vanilla scene-item save state is changed");
Assert(Calls(hooks.Methods.Single(m => m.Name == "PickupReady")).Any(m => m.Name == "QueuePickup"), "Already-real scene pickups are not queued for deferred inspection");
Assert(Calls(natural.Methods.Single(m => m.Name == "InspectScenePickup")).Any(m => m.Name == "IsAuthoredWorldItem"), "Live authored scene pickup save metadata is not checked");
var spawn = natural.Methods.Single(m => m.Name == "Spawn");
var spawnCalls = Calls(spawn).ToArray();
Assert(spawnCalls.Any(m => m.DeclaringType.Name == "LongStickItem" && m.Name == "get_Data") &&
    spawnCalls.Any(m => m.Name == "get_PickupPrefab"), "Extra item is not derived from the dedicated Long Stick template");
Assert(spawnCalls.Any(m => m.Name == "Instantiate") && spawnCalls.Any(m => m.Name == "AddListener"), "Owned extra creation/collection tracking missing");
Assert(spawnCalls.Any(m => m.Name == "DestroyImmediate"), "Owned extras keep native scene savers and could duplicate world-save records");
Assert(Calls(natural.Methods.Single(m => m.Name == "Despawn")).Any(m => m.Name == "RemoveListener"), "Owned extra callback cleanup missing");
Assert(Calls(natural.Methods.Single(m => m.Name == "Initialize")).Any(m => m.DeclaringType.Name == "SonsSaveTools" && m.Name == "Register"), "Legacy save compatibility adapter is not registered");
Assert(Calls(main.Methods.Single(m => m.Name == "OnSdkInitialized")).Any(m => m.DeclaringType.Name == "NaturalWorldSticks" && m.Name == "Initialize"), "Legacy save compatibility is not scheduled at SDK readiness");
var legacySave = natural.NestedTypes.SingleOrDefault(t => t.Name == "LegacySaveAdapter");
Assert(legacySave != null, "Persistent collection adapter remains compiled");
var legacyLoad = legacySave!.Methods.Single(m => m.Name == "Load");
Assert(!Calls(legacyLoad).Any() && !legacyLoad.Body.Instructions.Any(i => i.Operand is FieldReference), "Previously saved collection history is still restored");
var legacyWrite = legacySave.Methods.Single(m => m.Name == "Save");
Assert(Calls(legacyWrite).Count() == 1 && Calls(legacyWrite).Single().Name == ".ctor" &&
    !legacyWrite.Body.Instructions.Any(i => i.Operand is FieldReference), "Collected locations are still saved");
var sessionCollected = natural.Fields.Single(f => f.Name == "CollectedThisSession");
var naturalReset = natural.Methods.Single(m => m.Name == "Reset");
var resetCode = naturalReset.Body.Instructions;
Assert(Enumerable.Range(0, resetCode.Count - 1).Any(i =>
    resetCode[i].Operand is FieldReference f && f.FullName == sessionCollected.FullName &&
    resetCode[i + 1].Operand is MethodReference m && m.Name == "Clear"), "Session collection does not reset");
Assert(Calls(natural.Methods.Single(m => m.Name == "Begin")).First().Name == "Reset" &&
    Calls(main.Methods.Single(m => m.Name == "BeforeLoadSave")).Any(m => m.DeclaringType.Name == "NaturalWorldSticks" && m.Name == "Begin"), "Collection state survives save reload");
Assert(Calls(main.Methods.Single(m => m.Name == "WorldExited")).Any(m => m.DeclaringType.Name == "NaturalWorldSticks" && m.Name == "Reset"), "Natural sources/owned extras survive world exit");
Assert(Calls(main.Methods.Single(m => m.Name == "UpdateSetup")).Any(m => m.DeclaringType.Name == "NaturalWorldSticks" && m.Name == "Update"), "Natural extras are not updated");
Assert(configInit.Body.Instructions.Any(i => i.Operand is float f && f == 10f), "Natural chance default is not ten percent");
Console.WriteLine("PASS authored/static provenance, read-only original sources and owned extras; collection lasts only this session, resets before reload, and legacy saved consumption is ignored.");
var scatter = mod.Types.Single(t => t.Name == "NaturalGroundScatter");
var scatterCalls = Walk(scatter).SelectMany(t => t.Methods).SelectMany(Calls).ToArray();
var nativeScatterCalls = scatterCalls.Where(m => m.DeclaringType.Namespace.StartsWith("UnityEngine") ||
    m.DeclaringType.Namespace.StartsWith("Sons") || m.DeclaringType.Namespace.StartsWith("PathologicalGames") ||
    m.DeclaringType.Namespace.StartsWith("Il2CppSystem") || m.DeclaringType.Name.StartsWith("Greeble")).ToArray();
Assert(!nativeScatterCalls.Any(m => m.Name.StartsWith("set_") || m.Name is "Instantiate" or "Destroy" or "Collect" or "Spawn" or "Despawn" or "Clear" or "Remove" or "RemoveAt"), "Scatter observer modifies native world content");
var poolReads = Calls(scatter.Methods.Single(m => m.Name == "InspectPool")).ToArray();
foreach (var name in new[] { "get_Pool", "get_PoolId", "get_PickupsPoolId", "get__spawned", "IsDedicatedPool" })
    Assert(poolReads.Any(m => m.Name == name), "Missing dedicated environmental pool provenance: " + name);
Assert(!poolReads.Any(m => m.Name == "GetEnumerator"), "Native scatter pool is enumerated without a budget/index");
var zoneReads = Calls(scatter.Methods.Single(m => m.Name == "InspectZones")).ToArray();
foreach (var name in new[] { "get_Instances", "get_InstanceData", "get_CurrentlyVisible" })
    Assert(zoneReads.Any(m => m.Name == name), "Natural zone membership/state is not read: " + name);
var sourceReads = Calls(scatter.Methods.Single(m => m.Name == "Inspect")).ToArray();
Assert(sourceReads.Any(m => m.DeclaringType.Name == "GroundScatterRules" && m.Name == "IsLooseStick") &&
    sourceReads.Any(m => m.Name == "RecordScatterSpot"), "Natural scatter sticks are not recorded");
Assert(sourceReads.OfType<GenericInstanceMethod>().Any(m => m.GenericArguments.Any(a => a.Name == "TreeCutManager")) &&
    sourceReads.OfType<GenericInstanceMethod>().Any(m => m.GenericArguments.Any(a => a.Name == "BreakableObject")) &&
    sourceReads.OfType<GenericInstanceMethod>().Any(m => m.GenericArguments.Any(a => a.Name == "StructureStorage")), "Scatter tree/breakable/storage exclusions missing");
Assert(Calls(natural.Methods.Single(m => m.Name == "Update")).Any(m => m.DeclaringType.Name == "NaturalGroundScatter" && m.Name == "Update") &&
    Calls(naturalReset).Any(m => m.DeclaringType.Name == "NaturalGroundScatter" && m.Name == "Reset"), "Scatter discovery is not deferred or reset with the session");
Assert(!Calls(scatter.Methods.Single(m => m.Name == "ObserveLook")).Any(m => m.Name is "RecordScatterSpot" or "Record" or "Spawn"), "Looking at a runtime drop can generate loot");
Console.WriteLine("PASS bounded dedicated scatter-pool/zone discovery, original sources untouched, vegetation/storage exclusions and diagnostic-only look observer.");
var craft = mod.Types.Single(t => t.Name == "CraftingAndInventory");
var recipeCalls = Calls(craft.Methods.Single(m => m.Name == "EnsureRecipe")).ToArray();
Assert(recipeCalls.Any(m => m.Name == "BuildRecipe" && m.DeclaringType.Name == "RecipeBuilder") && recipeCalls.Any(m => m.Name == "Add"), "SDK recipe template not built and appended");
Assert(!recipeCalls.Any(m => m.Name is "Clear" or "Remove" or "RemoveAt"), "Recipe code deletes vanilla entries");
Assert(recipeCalls.Any(m => m.Name == "AddIngredient" && m.DeclaringType.Name == "RecipeBuilder"), "Reusable-ingredient SDK path missing");
Assert(recipeCalls.Any(m => m.Name == "set__generatedResultingItems"), "Cloned recipe output cache not cleared");
Assert(recipeCalls.Any(m => m.Name == "GenerateResultingItemList"), "Recipe output cache not initialized");
foreach (var audio in new[] { "_craftCompleteAudioEvent", "_duringCraftJingleAudioEvent" })
    Assert(recipeCalls.Any(m => m.Name == "get_" + audio) && recipeCalls.Any(m => m.Name == "set_" + audio), "Original spear crafting audio is not copied: " + audio);
Assert(recipeCalls.Any(m => m.Name == "IsNullOrWhiteSpace"), "Missing native spear animation can create an eventless custom recipe");
Console.WriteLine("PASS alternate recipe retains SDK/reusable-ingredient/output-cache handling and inherits the original spear animation/audio without the HerbMix fallback.");
var refreshCalls = Calls(craft.Methods.Single(m => m.Name == "Refresh")).Select(m => m.Name).ToArray();
Assert(refreshCalls.Contains("RefreshAvailableRecipes") && refreshCalls.Contains("ValidateItemForCrafting"), "Recipe/UI cache refresh missing");
Assert(Calls(main.Methods.Single(m => m.Name == "InitializeCrafting")).Any(m => m.Name == "Refresh"), "No refresh after crafting initialization/opening");
var diagnostics = mod.Types.Single(t => t.Name == "CraftingDiagnostics");
Assert(Calls(diagnostics.Methods.Single(m => m.Name == "OnAdded")).Any(m => m.Name == "Refresh"), "No refresh after adding a custom ingredient");
Assert(Calls(diagnostics.Methods.Single(m => m.Name == "BeforeCombine")).Any(m => m.Name == "Refresh"), "No refresh before craft activation");
Console.WriteLine("PASS recipe availability and long-stick slot validation are refreshed after setup, on inventory open, and around crafting input.");
Assert(!Calls(craft.Methods.Single(m => m.Name == "EnsureIngredientGroup")).Any(m => m.Name == "set__animationClip"), "Native mat-layout animation overwritten");
Console.WriteLine("PASS the crafting ingredient retains its native mat-layout animation.");
var data = mod.Types.Single(t => t.Name == "LongStickItem");
var dataCalls = Calls(data.Methods.Single(m => m.Name == "Register")).ToArray();
Assert(dataCalls.Any(m => m.DeclaringType.Name == "ItemUiData" && m.Name == ".ctor"), "Item UI data not isolated");
Assert(dataCalls.Any(m => m.Name == "IsItemRegistered") && dataCalls.Any(m => m.Name == "RegisterItem"), "Item conflict check or registration missing");
Assert(dataCalls.Any(m => m.Name == "SetActive"), "Pickup template is not isolated");
Console.WriteLine("PASS custom item has separate UI data, a registration collision check, and an inactive pickup template.");
var appearance = mod.Types.Single(t => t.Name == "LongStickAppearance");
var visuals = mod.Types.Single(t => t.Name == "LongStickVisuals");
Assert(appearance.BaseType.FullName == "UnityEngine.MonoBehaviour", "Appearance component is not a MonoBehaviour");
Assert(appearance.Methods.Any(m => m.Name == ".ctor" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.IntPtr"), "Injected component pointer constructor missing");
foreach (var name in new[] { "OnEnable", "OnDisable", "LateUpdate", "OnDestroy" })
    Assert(appearance.Methods.Any(m => m.Name == name && m.IsPublic && m.Parameters.Count == 0), "Appearance lifecycle method missing: " + name);
Assert(Calls(visuals.Methods.Single(m => m.Name == "Initialize")).Any(m => m.Name == "RegisterTypeInIl2Cpp"), "Appearance type is not registered");
Assert(Calls(main.Methods.Single(m => m.Name == "OnSdkInitialized")).Any(m => m.DeclaringType.Name == "LongStickVisuals" && m.Name == "Initialize"), "Appearance registration is not scheduled before item creation");
Assert(dataCalls.Any(m => m.DeclaringType.Name == "LongStickVisuals" && m.Name == "Attach"), "Pickup prefab has no appearance component");
foreach (var name in new[] { "EnsureInventoryGroup", "EnsureIngredientGroup" })
    Assert(Calls(craft.Methods.Single(m => m.Name == name)).Any(m => m.DeclaringType.Name == "LongStickVisuals" && m.Name == "Attach"), "Appearance missing on " + name);
Console.WriteLine("PASS an injected appearance component is attached to only the custom pickup, inventory and ingredient layouts.");
var appearanceCalls = Walk(appearance).SelectMany(t => t.Methods).SelectMany(Calls).ToArray();
Assert(appearanceCalls.Any(m => m.Name == "AddOnRenderableLoadedListener") && appearanceCalls.Any(m => m.Name == "RemoveOnRenderableLoadedListener"), "Native load callback lifecycle missing");
Assert(Calls(appearance.Methods.Single(m => m.Name == "Discover")).Any(m => m.Name == "GetComponentsInChildren"), "Local static-mesh fallback missing");
Assert(!Calls(appearance.Methods.Single(m => m.Name == "ModelLoaded")).Any(m => m.Name == "TryBind"), "Hierarchy is edited during the native activation callback");
Assert(Calls(appearance.Methods.Single(m => m.Name == "TryBind")).Any(m => m.DeclaringType.Name == "StretchFrame" && m.Name == "Create"), "Tested geometry plan is not used");
Assert(Calls(appearance.Methods.Single(m => m.Name == "TryBind")).Any(m => m.Name == "IsControlRoot"), "Pickup/controller root protection missing");
Assert(!hooks.Methods.Any(m => m.Name is "ModelReady" or "AfterModelLoad"), "Old one-shot global scale hooks remain");
Assert(Calls(appearance.Methods.Single(m => m.Name == "LateUpdate")).Any(m => m.Name == "RestoreForReuse"), "Pooled pickup reuse can leave normal items stretched");
Assert(!appearanceCalls.Any(m => m.DeclaringType.FullName == "UnityEngine.Mesh" && m.Name.StartsWith("set_")), "A shared mesh asset is mutated");
Console.WriteLine("PASS deferred model loading, local mesh discovery, a separate length pivot and root protection; shared mesh assets are unchanged.");
var storage = mod.Types.Single(t => t.Name == "LongStickStorage");
var holder = mod.Types.Single(t => t.Name == "LongStickStorageHolder");
var createCalls = Calls(storage.Methods.Single(m => m.Name == "Create")).ToArray();
Assert(createCalls.Count(m => m.Name == "Instantiate") == 3, "Storage does not clone the native recipe, built prefab and ghost");
foreach (var field in new[] { "set__id", "set__boltPrefabId", "set__builtPrefab", "set__structureNodePrefab", "set__recipe" })
    Assert(createCalls.Any(m => m.Name == field), "New storage identity/reference missing: " + field);
Assert(createCalls.Any(m => m.Name == "SetEntityId"), "Unique built/ghost prefab IDs not assigned");
Assert(!createCalls.Any(m => m.Name is "Remove" or "Clear" or "RemoveAt"), "Storage cloning deletes vanilla data");
Assert(Calls(storage.Methods.Single(m => m.Name == "Ensure")).Count(m => m.Name == "AddRecipe") == 2, "Recipe not registered for both building and completed-structure saves");
Assert(Calls(storage.Methods.Single(m => m.Name == "Ensure")).Count(m => m.Name == "RegisterEntity") == 2, "Built and ghost prefabs are not both registered");
Assert(Calls(main.Methods.Single(m => m.Name == "BeforeLoadSave")).Any(m => m.Name == "TryStorageSetup"), "Storage registered too late for save loading");
Console.WriteLine("PASS the separate holder clones native recipe/ghost/built prefabs and registers them before structure save loading.");
Assert(Calls(storage.Methods.Single(m => m.Name == "EnsureBookPage")).Any(m => m.DeclaringType.FullName == "Sons.Crafting.BlueprintBookPageData" && m.Name == ".ctor"), "No new native book page");
Assert(Calls(storage.Methods.Single(m => m.Name == "EnsureBookPage")).Any(m => m.Name == "Add"), "New page not appended");
var bookLabels = mod.Types.Single(t => t.Name == "StorageBookLabels");
var captionRefresh = bookLabels.Methods.Single(m => m.Name == "Refresh");
var captionCalls = Calls(captionRefresh).ToArray();
Assert(Calls(storage.Methods.Single(m => m.Name == "EnsureBookPage")).Any(m => m.DeclaringType.Name == "StorageBookLabels" && m.Name == "Register"), "Blueprint caption localization is not registered");
Assert(Calls(bookLabels.Methods.Single(m => m.Name == "Register")).Count(m => m.Name == "AddEntry") == 2 && Calls(bookLabels.Methods.Single(m => m.Name == "Register")).Any(m => m.Name == "set__localizationId"), "Page and recipe captions do not have explicit localization entries");
Assert(Calls(hooks.Methods.Single(m => m.Name == "BookLocalizationUpdated")).Any(m => m.DeclaringType.Name == "StorageBookLabels" && m.Name == "Refresh"), "Native page-localization update does not apply the caption");
foreach (var nativeCall in new[] { "get__currentPageIndex", "get__topRecipe", "get__bottomRecipe", "get_Id", "get__topBlueprintName", "get__bottomBlueprintName", "set_text" })
    Assert(captionCalls.Any(m => m.Name == nativeCall), "Caption routing does not use the current custom recipe and native slot: " + nativeCall);
Assert((string)bookLabels.Fields.Single(f => f.Name == "Caption").Constant == "LONG STICK STORAGE", "Blueprint caption differs from the requested text");
Assert(!captionCalls.Any(m => m.Name is "set_font" or "set_fontSize" or "set_alignment" or "set_localPosition" or "AddComponent" or "Instantiate"), "Blueprint caption replaces native typography/layout");
Assert(!Calls(main.Methods.Single(m => m.Name == "UpdateSetup")).Any(m => m.DeclaringType.Name == "StorageBookLabels"), "Caption is rewritten every frame instead of on native page updates");
Console.WriteLine("PASS explicit LONG STICK STORAGE caption, separate recipe/page localization, current custom-recipe slot routing and native typography.");
var bookArt = mod.Types.Single(t => t.Name == "StorageBookArt");
var bookUpdate = bookArt.Methods.Single(m => m.Name == "Update");
var bookCalls = Calls(bookUpdate).ToArray();
Assert(Calls(storage.Methods.Single(m => m.Name == "EnsureBookPage")).Any(m => m.DeclaringType.Name == "StorageBookArt" && m.Name == "Request"), "Book page does not queue deferred artwork preparation");
Assert(!Calls(storage.Methods.Single(m => m.Name == "EnsureBookPage")).Any(m => m.DeclaringType.Name == "StorageBookArt" && m.Name == "Create"), "Page registration still bakes a possibly unloaded low-detail texture");
Assert(Calls(main.Methods.Single(m => m.Name == "UpdateSetup")).Any(m => m.DeclaringType.Name == "StorageBookArt" && m.Name == "Update"), "Deferred artwork has no game-thread update");
Assert(bookCalls.First().Name == "get_Count" && bookCalls.Any(m => m.Name == "get_RuntimeReady"), "Book artwork does native work while idle or before world readiness");
Assert(bookCalls.Any(m => m.Name == "set_requestedMipmapLevel") && bookCalls.Any(m => m.Name == "IsRequestedMipmapLevelLoaded") && bookCalls.Any(m => m.Name == "get_loadedMipmapLevel") && bookCalls.Any(m => m.Name == "get_activeMipmapLimit"), "Book texture copy does not wait for its detailed requested mip");
Assert(Array.FindIndex(bookCalls, m => m.Name == "IsRequestedMipmapLevelLoaded") < Array.FindIndex(bookCalls, m => m.Name == "Create"), "Texture readback happens before the mip-readiness check");
Assert(!bookCalls.Any(m => m.Name is "set__topRecipe" or "set__bottomRecipe"), "Deferred image update moves native interaction slots");
var bookCreate = bookArt.Methods.Single(m => m.Name == "Create");
var bookCreateCalls = Calls(bookCreate).ToArray();
Assert(bookCreateCalls.Any(m => m.Name == "set_ignoreMipmapLimit") && bookCreateCalls.Any(m => m.Name == "set_filterMode") && bookCreateCalls.Any(m => m.Name == "set_anisoLevel"), "Owned book texture lacks quality/filtering configuration");
var textureConstructors = bookCreate.Body.Instructions.Where(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.Texture2D" && m.Name == ".ctor").ToArray();
Assert(textureConstructors.Length == 2 && textureConstructors[1].Previous.OpCode.Code == Mono.Cecil.Cil.Code.Ldc_I4_1, "Owned book texture has no mip chain");
Assert(bookCreateCalls.Any(m => m.Name == "ReleaseTemporary") && bookCreateCalls.Any(m => m.Name == "Destroy") && bookCreateCalls.Any(m => m.Name == "set_active"), "Artwork readback lacks temporary texture/active-target cleanup");
Assert(!bookArt.Methods.SelectMany(Calls).Any(m => m.Name is "set_globalMipmapLimit" or "set_streamingTextureForceLoadAll"), "Artwork changes global texture settings");
Assert(Calls(bookArt.Methods.Single(m => m.Name == "Pause")).Any(m => m.Name == "ClearRequestedMipmapLevel") && Calls(storage.Methods.Single(m => m.Name == "WorldExited")).Any(m => m.DeclaringType.Name == "StorageBookArt" && m.Name == "Pause"), "Source texture request survives world exit");
Console.WriteLine("PASS deferred detailed book mip capture, stable recipe slots, owned mip/filter quality, no global texture changes and temporary-resource/request cleanup.");
var holderCalls = Walk(holder).SelectMany(t => t.Methods).SelectMany(Calls).ToArray();
Assert(holderCalls.Any(m => m.Name == "set__maxAmount") && holderCalls.Any(m => m.Name == "SetItemMaxCount"), "Visual and inventory storage capacities not both configured");
Assert(holderCalls.Any(m => m.DeclaringType.Name == "StorageRules" && m.Name == "SlotCapacity"), "Total holder capacity is not split across native slots");
Assert(holderCalls.Any(m => m.DeclaringType.Name == "LongStickVisuals" && m.Name == "Attach"), "Stored long sticks have no appearance component");
Assert(Calls(storage.Methods.Single(m => m.Name == "AllowDeposit")).Any(m => m.Name == "HolderFor"), "Capacity guard affects ordinary inventory managers");
Assert(Calls(storage.Methods.Single(m => m.Name == "AllowDeposit")).Any(m => m.DeclaringType.Name == "StorageRules" && m.Name == "CanDeposit"), "Tested deposit rule not used");
Assert(Calls(hooks.Methods.Single(m => m.Name == "StorageDeserialized")).Any(m => m.Name == "Apply"), "Capacity not reapplied after storage deserialization");
Console.WriteLine("PASS a separate book page, long-stick-only deposit guards, a total twenty-item capacity and post-load capacity configuration.");
Assert(Calls(natural.Methods.Single(m => m.Name == "Spawn")).Any(m => m.DeclaringType.Name == "KelvinLongSticks" && m.Name == "TrackPickup"), "Natural Long Stick extras do not directly register their AI sources");
Assert(Calls(appearance.Methods.Single(m => m.Name == "OnEnable")).Any(m => m.DeclaringType.Name == "KelvinLongSticks" && m.Name == "TrackOwner"), "Custom pickup activation cannot register console/tree/dropped Long Sticks");
var tracking = Calls(kelvin.Methods.Single(m => m.Name == "UpdateWorld")).ToArray();
var storageFilters = tracking.OfType<GenericInstanceMethod>().Where(m => m.Name == "GetComponentInParent").SelectMany(m => m.GenericArguments).Select(t => t.Name).ToArray();
Assert(!storageFilters.Contains("LayoutItem") && new[] { "InventoryLayoutItemGroup", "InWorldLayoutItemGroup", "StructureStorage", "VailActor" }.All(storageFilters.Contains), "Loose AI sources use an overbroad layout-item exclusion or miss actual stored/carried items");
Assert(tracking.Any(m => m.Name == "set__root") && tracking.Any(m => m.Name == "set__target") && tracking.Any(m => m.Name == "IsChildOf"), "Live AI roots/targets are not rebound to the local pickup hierarchy");
Assert(!tracking.Any(m => m.Name is "FindObjectsOfType" or "FindObjectsByType"), "AI resource registration scans the entire world");
foreach (var name in new[] { "ValidatedPickup", "PickedUp", "Deposited" })
    Assert(Calls(kelvinHooks.Methods.Single(m => m.Name == name)).Any(m => m.DeclaringType.Name == "KelvinLongSticks"), "Native work-result diagnostic missing: " + name);
Console.WriteLine("PASS direct natural/appearance source registration, local native AI targets, specific storage/carried exclusions and bounded native work diagnostics.");
var ensureAiIds = Calls(kelvin.Methods.Single(m => m.Name == "EnsureTypeIds")).ToArray();
Assert(ensureAiIds.Any(m => m.Name == "ReserveTypeId") && ensureAiIds.Any(m => m.Name == "get_ItemIdMask") &&
    ensureAiIds.Any(m => m.DeclaringType.Name == "InventoryTypeList" && m.Name == "get_TypeList"), "Kelvin type encoding can overwrite a vanilla item type");
var aiSetup = Calls(kelvin.Methods.Single(m => m.Name == "EnsurePickupType")).ToArray();
Assert(aiSetup.Any(m => m.Name == "Instantiate") && aiSetup.Any(m => m.Name == "set_heldPrefab") &&
    aiSetup.Any(m => m.Name == "set_droppedPrefab") && aiSetup.Any(m => m.Name == "set_maxHeld"), "Separate AI held/dropped Long Stick prefabs or native carrying limit missing");
Assert(aiSetup.Any(m => m.DeclaringType.Name == "LongStickVisuals" && m.Name == "Attach"), "Kelvin's held Long Stick has no length appearance");
var configureAi = Calls(kelvin.Methods.Single(m => m.Name == "ConfigurePickup")).ToArray();
foreach (var nativeType in new[] { "VailPickup", "StickPickupStimuli" })
    Assert(configureAi.OfType<GenericInstanceMethod>().Any(m => m.Name == "AddComponent" && m.GenericArguments.Any(a => a.Name == nativeType)), "Player-only pickup cannot be discovered by Kelvin: " + nativeType);
Assert(configureAi.Any(m => m.Name == "set__itemType") && configureAi.Any(m => m.Name == "set__target"), "AI resource type or stimuli target is not configured");
Assert(Calls(mod.Types.Single(t => t.Name == "LongStickItem").Methods.Single(m => m.Name == "Register")).Any(m => m.Name == "ConfigurePickup"), "The custom world pickup has no AI configuration");
var orderSetup = Calls(kelvin.Methods.Single(m => m.Name == "EnsureOrders")).ToArray();
Assert(orderSetup.Any(m => m.Name == "Instantiate") && orderSetup.Any(m => m.Name == "set__getItemOrders") &&
    orderSetup.Any(m => m.DeclaringType.Name == "RobbyGetOrder" && m.Name == ".ctor") &&
    orderSetup.Any(m => m.Name == "set__controllerOverride") && orderSetup.Any(m => m.Name == "set__orderData"), "Separate appended notebook order with the native stick controller is missing");
Assert(!orderSetup.Any(m => m.Name is "Clear" or "Remove" or "RemoveAt"), "Vanilla notebook entries are deleted");
Assert(Calls(kelvin.Methods.Single(m => m.Name == "ShowDropOptions")).Any(m => m.Name == "GetSelection") &&
    Calls(kelvin.Methods.Single(m => m.Name == "ShowDropOptions")).Any(m => m.Name == "AddMenuItem"), "Long Stick fill-holder destination option missing");
foreach (var name in new[] { "AllowPickup", "InventoryItemQuery" }) {
    var owner = name == "AllowPickup" ? kelvin : kelvinHooks;
    Assert(owner.Methods.Where(m => m.Name == name).SelectMany(Calls).Any(m => m.DeclaringType.Name == "KelvinRules" || m.DeclaringType.Name == "KelvinLongSticks"), "Kelvin order routing missing: " + name);
}
Assert(kelvin.Methods.Where(m => m.Name == "AllowPickup").SelectMany(Calls).Any(m => m.Name == "CanGather"), "Ordinary and Long Stick pickups are not separated");
foreach (var name in new[] { "AfterOrder", "AfterRestoreCarry", "AfterWorldPickup" })
    Assert(Calls(kelvinHooks.Methods.Single(m => m.Name == name)).Any(m => m.Name == "Dispose"), "Kelvin scoped state survives exceptions: " + name);
Assert(Calls(kelvinHooks.Methods.Single(m => m.Name == "PickingUp")).Any(m => m.Name == "Restoring"), "Unbacked virtual stick pickup can turn into a Long Stick");
Assert(Calls(kelvinHooks.Methods.Single(m => m.Name == "PickingUp")).Any(m => m.Name == "Gathering"), "Native pickup delegation loses its verified world-resource context");
Assert(!Calls(kelvinHooks.Methods.Single(m => m.Name == "HolderDeposit")).Any(m => m.Name.StartsWith("set_")), "AI holder deposit converts normal sticks instead of rejecting them");
Assert(Calls(kelvinHooks.Methods.Single(m => m.Name == "CollectedWorldItem")).Any(m => m.Name == "MarkCollected") &&
    Calls(kelvin.Methods.Single(m => m.Name == "MarkCollected")).Any(m => m.Name == "MarkAiCollected"), "AI pickups can immediately refill natural extras");
Assert(Calls(main.Methods.Single(m => m.Name == "InstallHooks")).Any(m => m.DeclaringType.Name == "KelvinHooks" && m.Name == "Install") &&
    Calls(main.Methods.Single(m => m.Name == "BeforeLoadSave")).Any(m => m.DeclaringType.Name == "KelvinLongSticks" && m.Name == "Begin") &&
    Calls(main.Methods.Single(m => m.Name == "WorldExited")).Any(m => m.DeclaringType.Name == "KelvinLongSticks" && m.Name == "Reset"), "Kelvin setup/session cleanup is not wired");
Assert(Calls(kelvinHooks.Methods.Single(m => m.Name == "Apply")).Any(m => m.Name == "Unpatch"), "Partly installed Kelvin hooks cannot be rolled back");
Assert(Calls(kelvinHooks.Methods.Single(m => m.Name == "Install")).Any(m => m.Name == "Where") &&
    Calls(kelvinHooks.Methods.Single(m => m.Name == "InstallWork")).Any(m => m.Name == "Apply"), "Heavy AI hooks are still installed unconditionally at load");
Assert(!Calls(kelvin.Methods.Single(m => m.Name == "UpdateWorld")).Any(m => m.Name == "EnsurePickupType"), "World update still rescans AI type tables");
foreach (var method in kelvin.Methods.Where(m => m.Name == "IsLongTask"))
    Assert(!Calls(method).Any(m => m.Name is "get__activeOrder" or "TryCast" or "GetComponent" or "GetComponentInParent" or "op_Implicit"), "A frequent AI query still crosses native state instead of using the task cache");
Assert(Calls(kelvinHooks.Methods.Single(m => m.Name == "ValidatePickup")).Take(2).Any(m => m.Name == "IsLongTask"), "Pickup validation does native source work for unrelated actors");
Assert(Calls(kelvin.Methods.Single(m => m.Name == "RegisterOwner")).Any(m => m.Name == "ConfigureCarryAliases") &&
    Calls(kelvin.Methods.Single(m => m.Name == "ConfigureCarryAliases")).Any(m => m.Name == "set__itemTypes"), "Own Kelvin carry aliases are not configured");
Assert(Calls(kelvinHooks.Methods.Single(m => m.Name == "BeforeRestoreCarry")).Any(m => m.Name == "ConfigureCarryAliases"), "Restored carried Long Sticks have no native attachment alias");
Assert(Calls(kelvin.Methods.Single(m => m.Name == "Reset")).Any(m => m.Name == "UninstallWork"), "Gathering hooks remain installed across world loads");
foreach (var name in new[] { "BeforeWorldPickup", "PickingUp" })
    Assert(Calls(kelvinHooks.Methods.Single(m => m.Name == name)).Any(m => m.Name == "ConfirmPickup"), "Actual pickup identity is not verified: " + name);
Assert(targets.Length == 69, "Native mapping coverage is incomplete");
Console.WriteLine("PASS deferred AI work, cached task/type setup, inactive loose-item stimuli between tasks and own-instance carry aliases.");
Console.WriteLine("PASS separate Kelvin AI type/prefabs, appended notebook order, gathering filters, carry animation aliases, native holder deposits, save-load type mapping and exception-safe session cleanup.");
using var reader = new StreamReader(mod.Resources.OfType<EmbeddedResource>().Single(r => r.Name.EndsWith("manifest.json")).GetResourceStream());
using var embedded = JsonDocument.Parse(reader.ReadToEnd());
using var external = JsonDocument.Parse(File.ReadAllText(args[2]));
foreach (var property in new[] { "id", "name", "author", "version", "loaderVersion", "type", "platform" })
    Assert(embedded.RootElement.GetProperty(property).GetString() == external.RootElement.GetProperty(property).GetString(), "Manifest mismatch");
Assert(mod.Assembly.Name.Version.ToString(3) == external.RootElement.GetProperty("version").GetString(), "Assembly version mismatch");
Assert(!methods.Any(m => m.IsPInvokeImpl), "Unexpected native imports");
Console.WriteLine("PASS matching metadata/version and no custom native imports.");
if (args.Length >= 4)
{
    var crash = File.ReadAllText(args[3]);
    Assert(crash.Contains("Fatal error. Internal CLR error.") && crash.Contains("Il2CppStringToManaged") &&
        crash.Contains("LongStickSpear.KelvinLongSticks.EnsureTypeIds()") && crash.Contains("LongStickSpear.Mod.BeforeLoadSave()"),
        "Supplied crash does not match native AI type-name conversion during reload");
    Console.WriteLine("PASS supplied fatal stack matches the repaired AI type registration/name-conversion path during BeforeLoadSave; raw crash data is excluded from the package.");
}
if (args.Length >= 5)
{
    var session = File.ReadAllText(args[4]);
    Assert(session.Contains("Golf Cart Battery Upgrade v0.1.1") && session.Contains("native weak save callbacks disabled") &&
        !session.Contains("[GolfCartBatteryUpgrade] Save hook cleanup:") && !session.Contains("[GolfCartBatteryUpgrade] Before-save snapshot hook:"),
        "Supplied session does not establish the battery callback fix is active");
    Console.WriteLine("PASS supplied session uses Battery Upgrade v0.1.1 without the removed save-callback errors; the current fatal stack requires the Long Stick AI registration fix.");
}
Console.WriteLine("Static audit passed. This does not execute the game's native implementations.");
static IEnumerable<TypeDefinition> Walk(TypeDefinition t) { yield return t; foreach(var n in t.NestedTypes.SelectMany(Walk)) yield return n; }
static IEnumerable<MethodReference> Calls(MethodDefinition m) => m.HasBody ? m.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>() : Enumerable.Empty<MethodReference>();
static void Assert(bool ok, string why) { if (!ok) throw new Exception(why); }
