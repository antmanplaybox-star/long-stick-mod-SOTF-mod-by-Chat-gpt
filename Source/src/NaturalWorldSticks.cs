using LongStickSpear.Core;
using Sons.Gameplay;
using Sons.Gameplay.TreeCutting;
using Sons.Inventory;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace LongStickSpear;

internal static class NaturalWorldSticks
{
    // Keep the old save key readable, but never save or restore consumed locations.
    // This also ignores any permanent collection history written by v0.3.2.
    private sealed class LegacySaveAdapter : ICustomSaveable<NaturalGroundSaveData>
    {
        public string Name => "LongStickSpear.NaturalGround.v1";
        public bool IncludeInPlayerSave => false;
        public NaturalGroundSaveData Save() => new();
        public void Load(NaturalGroundSaveData data) { }
    }

    private sealed class Spot
    {
        internal readonly string Key;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal GameObject? Root;
        internal PickUp? Pickup;
        internal UnityAction<PickUp>? Callback;
        internal float RetryAt;
        internal Spot(string key, Vector3 position, Quaternion rotation) { Key = key; Position = position; Rotation = rotation; }
    }

    // Stops immediate refills in the same session; Reset clears this before loading.
    private static readonly HashSet<string> CollectedThisSession = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Spot> Spots = new(StringComparer.Ordinal);
    private static readonly List<Spot> Ordered = new();
    private static readonly Queue<PickUp> SceneCandidates = new();
    private static readonly HashSet<IntPtr> Seen = new();
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
    private static bool _singlePlayerWorld, _saveRegistered;
    private static int _mainThread, _cursor, _registryCursor, _registryRows, _registrySticks, _runtimeRejected, _live, _dataCalls, _proxyCalls, _sceneCalls, _normalSources, _reports;
    private static readonly HashSet<IntPtr> RegistrySeen = new();
    private static float _nextReport;
    private static float _nextRegistryRead;
    private static GameObject? _inactive;

    internal static void Initialize()
    {
        if (_saveRegistered) return;
        SonsSaveTools.Register(new LegacySaveAdapter()); _saveRegistered = true;
    }

    internal static void Begin(bool singlePlayer)
    {
        Reset(); _mainThread = Environment.CurrentManagedThreadId; _singlePlayerWorld = singlePlayer;
        Mod.Info($"WORLD STICK SETUP: environmental ground scatter plus static pickup records; extra Long Stick chance={WorldStickRules.Chance(Config.NaturalStickChance.Value)}%; normal world sticks are retained; collection resets on reload.");
    }

    internal static void Started(bool singlePlayer)
    {
        _mainThread = Environment.CurrentManagedThreadId; _singlePlayerWorld = singlePlayer;
        _nextReport = Time.unscaledTime + 3f;
    }

    internal static void Reset()
    {
        _singlePlayerWorld = false;
        NaturalGroundScatter.Reset();
        foreach (var spot in Ordered) Despawn(spot);
        if (_inactive != null && _inactive) Object.Destroy(_inactive);
        _inactive = null; Spots.Clear(); Ordered.Clear(); SceneCandidates.Clear(); Seen.Clear(); RegistrySeen.Clear(); Logged.Clear();
        CollectedThisSession.Clear();
        _mainThread = _cursor = _live = _dataCalls = _proxyCalls = _sceneCalls = _normalSources = _reports = 0;
        _registryCursor = _registryRows = _registrySticks = _runtimeRejected = 0;
        _nextReport = _nextRegistryRead = 0;
    }

    private static bool MainThread => _singlePlayerWorld && Environment.CurrentManagedThreadId == _mainThread;
    internal static bool CanObserve => MainThread && Mod.WorldActive;

    internal static void RecordScatterSpot(Vector3 position, Quaternion rotation)
    {
        if (CanObserve && Mod.RuntimeReady) Record(position, rotation);
    }

    internal static void MarkAiCollected(IntPtr pickup)
    {
        if (!CanObserve) return;
        foreach (var spot in Ordered)
            if (spot.Pickup?.Pointer == pickup) { CollectedThisSession.Add(spot.Key); return; }
    }

    internal static void CaptureData(VirtualPickupData? data)
    {
        if (!MainThread || data == null) return;
        _dataCalls++;
        var itemId = data.ItemInstance?._itemID ?? -1;
        var origin = new NaturalPickupOrigin(itemId, data.RuntimeCreated, data.SpawnInDynamic, data.ShouldDespawn);
        if (itemId == Rules.Stick) ReportOrigin(origin, data.NameId);
        if (Mod.WorldActive && NaturalSpotRules.IsNatural(origin)) Record(data.Position, data.Rotation);
    }

    internal static void CaptureProxy(VirtualPickup proxy)
    {
        if (!MainThread || !Mod.WorldActive) return;
        _proxyCalls++;
        var origin = new NaturalPickupOrigin(proxy._itemInstance?._itemID ?? proxy._itemIdCached,
            proxy._runtimeCreated, proxy._spawnInDynamic, proxy._shouldDespawn);
        if (origin.ItemId == Rules.Stick) ReportOrigin(origin, proxy._nameId);
        if (NaturalSpotRules.IsNatural(origin)) Record(proxy._transformPosition, proxy.transform.rotation);
    }

    internal static void QueuePickup(PickUp? pickup)
    {
        if (!MainThread || pickup == null || !pickup || pickup._itemId != Rules.Stick || !Seen.Add(pickup.Pointer)) return;
        SceneCandidates.Enqueue(pickup);
    }

    private static void InspectScenePickup(PickUp pickup)
    {
        if (!pickup || pickup._isCollected || !pickup.gameObject.activeInHierarchy || !pickup.gameObject.scene.IsValid()) return;
        var node = pickup.transform;
        for (var depth = 0; depth < 4 && node != null; depth++, node = node.parent)
        {
            var saver = node.GetComponent<WorldItemSaver>();
            if (saver == null || saver._pickUpComponent?.Pointer != pickup.Pointer) continue;
            var state = saver._itemState;
            if (state != null && NaturalSpotRules.IsAuthoredWorldItem(pickup._itemId, state.RuntimeCreated,
                state.State == (int)WorldItemStates.Default))
            {
                _sceneCalls++;
                Record(pickup.transform.position, pickup.transform.rotation);
            }
            return;
        }
    }

    private static void Record(Vector3 position, Quaternion rotation)
    {
        var key = NaturalSpotRules.Key(position.x, position.y, position.z);
        if (key == null || Spots.ContainsKey(key)) return;
        var spot = new Spot(key, position, rotation);
        Spots.Add(key, spot); Ordered.Add(spot);
        if (Ordered.Count <= 20 || Config.DebugLogging.Value && Ordered.Count <= 128)
            Mod.Info($"NATURAL SPOT: key={key}, selected={NaturalSpotRules.Selected(key, Config.NaturalStickChance.Value)}, chance={WorldStickRules.Chance(Config.NaturalStickChance.Value)}%.");
    }

    private static void ReportOrigin(NaturalPickupOrigin origin, string? name)
    {
        _normalSources++;
        var label = $"{origin.RuntimeCreated}:{origin.Dynamic}:{origin.Despawns}:{name}";
        if (Logged.Count < 24 && Logged.Add(label))
            Mod.Info($"PICKUP SOURCE: id={origin.ItemId}, runtime={origin.RuntimeCreated}, dynamic={origin.Dynamic}, despawns={origin.Despawns}, name='{name}', natural={NaturalSpotRules.IsNatural(origin)}.");
    }

    internal static void Update()
    {
        if (!MainThread || !Mod.RuntimeReady || LocalPlayer.Transform == null) return;
        NaturalGroundScatter.Update();
        try
        {
            if (Time.unscaledTime >= _nextRegistryRead)
            { _nextRegistryRead = Time.unscaledTime + 1f; InspectWorldRegistry(); }
        }
        catch (Exception ex) { Mod.Warn("Reading authored world-item records: " + ex.Message); }
        for (var i = 0; i < 16 && SceneCandidates.Count > 0; i++)
        {
            var pickup = SceneCandidates.Dequeue();
            try { if (pickup != null && pickup) InspectScenePickup(pickup); }
            catch (Exception ex) { Mod.Warn("Inspecting static stick placement: " + ex.Message); }
        }
        var position = LocalPlayer.Transform.position;
        var chance = WorldStickRules.Chance(Config.NaturalStickChance.Value);
        var spawned = 0;
        for (var i = 0; i < 64 && i < Ordered.Count; i++)
        {
            if (_cursor >= Ordered.Count) _cursor = 0;
            var spot = Ordered[_cursor++];
            var collected = CollectedThisSession.Contains(spot.Key);
            var distance = (position - spot.Position).sqrMagnitude;
            if (collected || !NaturalSpotRules.Selected(spot.Key, chance) || distance > 110f * 110f)
            { if (spot.Root is not null) Despawn(spot); continue; }
            if (spot.Root is not null && !spot.Root)
            { Despawn(spot); spot.RetryAt = Time.unscaledTime + 30f; }
            if (spot.Root is null && distance < 65f * 65f && _live < 48 && spawned < 2 && Time.unscaledTime >= spot.RetryAt)
            {
                try { Spawn(spot); spawned++; }
                catch (Exception ex) { spot.RetryAt = Time.unscaledTime + 30f; Mod.Warn("Spawning natural Long Stick: " + ex); }
            }
        }
        if (_reports < 8 && Time.unscaledTime >= _nextReport)
        {
            _reports++; _nextReport = Time.unscaledTime + 20f;
            Mod.Info($"NATURAL STATUS: {NaturalGroundScatter.Status}, registryRows={_registryRows}, authoredStickRecords={_registrySticks}, runtimeStickRowsRejected={_runtimeRejected}, dataCalls={_dataCalls}, proxyCalls={_proxyCalls}, inspectedScenePickups={_sceneCalls}, normalSources={_normalSources}, staticSpots={Ordered.Count}, activeLongSticks={_live}, chance={chance}%.");
        }
    }

    private static void InspectWorldRegistry()
    {
        if (!WorldItemManager.TryGetInstance(out var manager) || manager == null || manager._worldItems == null) return;
        var items = manager._worldItems;
        var budget = Math.Min(64, items.Count);
        for (var i = 0; i < budget && items.Count > 0; i++)
        {
            if (_registryCursor >= items.Count) _registryCursor = 0;
            var saver = items[_registryCursor++]; _registryRows++;
            if (saver == null || !saver || RegistrySeen.Contains(saver.Pointer)) continue;
            var state = saver._itemState;
            var pickup = saver._pickUpComponent;
            if (pickup == null && state?.ItemId == Rules.Stick)
                pickup = saver.GetComponent<PickUp>() ?? saver.GetComponentInChildren<PickUp>(true);
            if (state == null || pickup == null || !pickup || pickup._isCollected || pickup._isBeingDestroyed) continue;
            if (pickup._itemId == Rules.Stick && state.RuntimeCreated) _runtimeRejected++;
            if (!NaturalSpotRules.IsAuthoredWorldItem(pickup._itemId, state.RuntimeCreated,
                state.State == (int)WorldItemStates.Default)) continue;
            RegistrySeen.Add(saver.Pointer); _registrySticks++;
            Record(pickup.transform.position, pickup.transform.rotation);
            if (_registrySticks <= 12) Mod.Info($"SAVED STICK SOURCE: scene item='{state.ObjectNameId}', runtime={state.RuntimeCreated}, state={state.State}.");
        }
    }

    private static void Spawn(Spot spot)
    {
        var prefab = LongStickItem.Data?.PickupPrefab;
        if (prefab == null) return;
        if (_inactive == null || !_inactive)
        { _inactive = new GameObject("LongStickSpear_NaturalTemplates"); _inactive.SetActive(false); }
        var root = Object.Instantiate(prefab.gameObject, _inactive.transform, false);
        try
        {
            root.name = "LongStickSpear_Natural_" + NaturalSpotRules.Hash(spot.Key).ToString("X8");
            // Own extras are regenerated each session. Native scene savers and
            // virtualization are removed/disabled before first activation.
            foreach (var registerer in root.GetComponentsInChildren<WorldItemSaverExplicitRegisterer>(true)) Object.DestroyImmediate(registerer);
            foreach (var saver in root.GetComponentsInChildren<WorldItemSaver>(true)) Object.DestroyImmediate(saver);
            foreach (var virtualizer in root.GetComponentsInChildren<VirtualPickupSwitcher>(true)) virtualizer.enabled = false;
            foreach (var tag in root.GetComponentsInChildren<SpawnedObjectTag>(true)) tag.enabled = false;
            foreach (var timer in root.GetComponentsInChildren<Sons.Utilities.DestroyAfter>(true)) timer.enabled = false;
            var pickup = root.GetComponent<PickUp>() ?? root.GetComponentInChildren<PickUp>(true);
            if (pickup == null || pickup._itemId != Rules.LongStick) throw new InvalidOperationException("Natural extra template is not a Long Stick.");
            var offset = spot.Rotation * new Vector3(.28f, 0, 0);
            var placement = spot.Position + offset + Vector3.up * .12f;
            if (Physics.Raycast(placement + Vector3.up, Vector3.down, out var hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                placement = hit.point + Vector3.up * .12f;
            root.transform.SetPositionAndRotation(placement, spot.Rotation);
            var callback = (UnityAction<PickUp>)new Action<PickUp>(_ => CollectedThisSession.Add(spot.Key));
            pickup.OnPickedUp ??= new UnityEvent<PickUp>();
            pickup.OnPickedUp.AddListener(callback);
            spot.Root = root; spot.Pickup = pickup; spot.Callback = callback; _live++;
            KelvinLongSticks.TrackPickup(pickup);
            root.SetActive(true); root.transform.SetParent(null, true);
            pickup.SetDynamic(true);
            if (_live <= 4 || Config.DebugLogging.Value) Mod.Info($"NATURAL LONG STICK: spawned beside static stick spot {spot.Key}; ID={Rules.LongStick}.");
        }
        catch
        {
            if (ReferenceEquals(spot.Root, root)) Despawn(spot);
            else Object.Destroy(root);
            throw;
        }
    }

    private static void Despawn(Spot spot)
    {
        var hadRoot = spot.Root is not null;
        if (spot.Pickup != null && spot.Pickup && spot.Callback != null && spot.Pickup.OnPickedUp != null)
            spot.Pickup.OnPickedUp.RemoveListener(spot.Callback);
        if (spot.Root != null && spot.Root) Object.Destroy(spot.Root);
        spot.Root = null; spot.Pickup = null; spot.Callback = null;
        if (hadRoot) _live = Math.Max(0, _live - 1);
    }
}
