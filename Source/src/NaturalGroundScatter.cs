using LongStickSpear.Core;
using Sons.Gameplay;
using Sons.Gameplay.TreeCutting;
using Sons.Inventory;
using UnityEngine;

namespace LongStickSpear;

// Read only the dedicated environmental-scatter pool and native zone membership.
// Generic pickup pools and player/console/tree spawn callbacks are never sources.
internal static class NaturalGroundScatter
{
    private sealed class ZoneCursor
    {
        internal readonly GreebleZone Zone;
        internal int Index;
        internal ZoneCursor(GreebleZone zone) => Zone = zone;
    }
    private sealed record Candidate(Transform Root, PickUp? Pickup, bool Blocked);
    private static readonly List<ZoneCursor> Zones = new();
    private static readonly HashSet<IntPtr> ZonePointers = new();
    private static readonly Dictionary<int, Candidate> Candidates = new();
    private static readonly HashSet<string> Profiles = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LookProfiles = new(StringComparer.Ordinal);
    private static readonly Queue<PickUp> LookedAt = new();
    private static int _poolCursor, _zoneCursor, _poolCount, _poolRows, _zoneCalls, _zoneRows, _stickRows;
    private static bool _poolReported, _poolError, _zoneError;
    private static string _poolLabel = "not read";

    internal static string Status => $"scatterPool='{_poolLabel}', poolLive={_poolCount}, poolRows={_poolRows}, zoneCallbacks={_zoneCalls}, zones={Zones.Count}, zoneRows={_zoneRows}, scatterStickRows={_stickRows}";

    internal static void Reset()
    {
        Zones.Clear(); ZonePointers.Clear(); Candidates.Clear(); Profiles.Clear(); LookProfiles.Clear(); LookedAt.Clear();
        _poolCursor = _zoneCursor = _poolCount = _poolRows = _zoneCalls = _zoneRows = _stickRows = 0;
        _poolReported = _poolError = _zoneError = false; _poolLabel = "not read";
    }

    internal static void ObserveZone(GreebleZone zone)
    {
        if (!NaturalWorldSticks.CanObserve || zone == null || !zone) return;
        _zoneCalls++;
        if (ZonePointers.Add(zone.Pointer)) Zones.Add(new ZoneCursor(zone));
    }

    // Looking at an ordinary stick gives bounded diagnostics only, never a spawn.
    internal static void ObserveLook(PickUp pickup)
    {
        if (NaturalWorldSticks.CanObserve && pickup != null && pickup && pickup._itemId == Rules.Stick &&
            LookProfiles.Count < 16 && LookedAt.Count < 16) LookedAt.Enqueue(pickup);
    }

    internal static void Update()
    {
        try { InspectPool(); }
        catch (Exception ex) { if (!_poolError) { _poolError = true; Mod.Warn("Reading environmental scatter pool: " + ex.Message); } }
        try { InspectZones(); }
        catch (Exception ex) { if (!_zoneError) { _zoneError = true; Mod.Warn("Reading ground-scatter zone: " + ex.Message); } }
        if (LookedAt.Count > 0)
        {
            var pickup = LookedAt.Dequeue();
            try
            {
                if (pickup == null || !pickup) return;
                var names = new List<string>();
                for (var node = pickup.transform; node != null && names.Count < 4; node = node.parent) names.Add(node.name);
                var label = string.Join(" / ", names) + "; pool=" + pickup._poolManagerPool;
                if (LookProfiles.Count < 16 && LookProfiles.Add(label))
                    Mod.Info($"NATURAL LOOK: {label}; id={pickup._itemId}, dynamic={pickup.IsDynamic()}, worldSaver={pickup.GetComponent<WorldItemSaver>() != null}. Diagnostic only; looking does not generate loot.");
            }
            catch (Exception ex) { Mod.Trace("Natural stick look diagnostic: " + ex.Message); }
        }
    }

    private static void InspectPool()
    {
        var pool = GreeblePlugin.Pool;
        if (pool == null || !pool) { _poolLabel = "unavailable"; return; }
        var declared = GreeblePlugin.PoolId;
        var actual = pool.poolName;
        var dedicated = GroundScatterRules.IsDedicatedPool(actual, declared, PickUp.PickupsPoolId);
        _poolLabel = actual;
        var instances = pool._spawned;
        _poolCount = instances?.Count ?? 0;
        if (!_poolReported)
        {
            _poolReported = true;
            Mod.Info($"GROUND SCATTER POOL: actual='{actual}', declared='{declared}', genericPickups='{PickUp.PickupsPoolId}', dedicated={dedicated}, live={_poolCount}.");
        }
        if (!dedicated || instances == null) return;
        for (var i = 0; i < Math.Min(32, _poolCount) && instances.Count > 0; i++)
        {
            if (_poolCursor >= instances.Count) _poolCursor = 0;
            var root = instances[_poolCursor++]; _poolRows++;
            Inspect(root, "scatter pool");
        }
    }

    private static void InspectZones()
    {
        for (var i = 0; i < 32 && Zones.Count > 0; i++)
        {
            if (_zoneCursor >= Zones.Count) _zoneCursor = 0;
            var cursor = Zones[_zoneCursor];
            var zone = cursor.Zone;
            if (zone == null || !zone)
            { ZonePointers.Remove(zone?.Pointer ?? IntPtr.Zero); Zones.RemoveAt(_zoneCursor); continue; }
            if (!zone.CurrentlyVisible) { _zoneCursor++; continue; }
            var instances = zone.Instances;
            if (instances == null || instances.Length == 0) { _zoneCursor++; continue; }
            if (cursor.Index >= instances.Length) { cursor.Index = 0; _zoneCursor++; continue; }
            var index = cursor.Index++;
            var state = zone.InstanceData;
            if (state != null && index < state.Length && state[index].Destroyed) continue;
            var instance = instances[index]; _zoneRows++;
            if (instance != null && instance) Inspect(instance.transform, "scatter zone");
        }
    }

    private static void Inspect(Transform? root, string source)
    {
        if (root == null || !root || !root.gameObject.activeInHierarchy || !root.gameObject.scene.IsValid()) return;
        var identity = root.GetInstanceID();
        if (!Candidates.TryGetValue(identity, out var candidate) || candidate.Root == null || !candidate.Root)
        {
            var pickup = root.GetComponent<PickUp>() ?? root.GetComponentInChildren<PickUp>(true);
            var blocked = pickup != null && pickup._itemId == Rules.Stick &&
                (root.GetComponentInChildren<TreeCutManager>(true) != null || root.GetComponentInChildren<BreakableObject>(true) != null ||
                 root.GetComponentInChildren<StructureStorage>(true) != null);
            candidate = new Candidate(root, pickup, blocked); Candidates[identity] = candidate;
            if (Profiles.Count < 20)
            {
                var label = $"{source}: {root.name}, pickupId={pickup?._itemId ?? -1}, blocked={blocked}";
                if (Profiles.Add(label)) Mod.Info("GROUND SCATTER SOURCE: " + label);
            }
        }
        var item = candidate.Pickup;
        if (item == null || !item || !GroundScatterRules.IsLooseStick(item._itemId,
            item.gameObject.activeInHierarchy, item._isCollected || item._isBeingDestroyed, candidate.Blocked, false)) return;
        _stickRows++;
        NaturalWorldSticks.RecordScatterSpot(item.transform.position, item.transform.rotation);
    }
}
