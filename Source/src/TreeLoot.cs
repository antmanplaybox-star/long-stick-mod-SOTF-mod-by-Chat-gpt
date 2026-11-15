using LongStickSpear.Core;
using Sons.Gameplay;
using Sons.Gameplay.TreeCutting;
using Sons.VFX;
using UnityEngine;
using Definitions = Il2CppSystem.Collections.Generic.List<Sons.VFX.SpawnDefinition>;

namespace LongStickSpear;

// Edit only native loot tables. Tree bodies, extras, transforms, activation
// groups, joints, colliders, Bolt identity and falling state remain native.
internal static class TreeLoot
{
    private sealed record Edit(CollisionTriggerActivator Owner, Definitions Original, Definitions Applied)
    {
        internal void Restore()
        {
            if (Owner != null && Owner && Owner._spawnDefinitions?.Pointer == Applied.Pointer)
                Owner._spawnDefinitions = Original;
        }
    }

    private static readonly Dictionary<IntPtr, Edit> Edits = new();
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    internal static void Reset()
    {
        RestoreEdits();
        Reported.Clear();
    }

    internal static void Update()
    {
        if (!Mod.CanReplaceDrops && Edits.Count > 0) RestoreEdits();
    }

    private static void RestoreEdits()
    {
        foreach (var edit in Edits.Values)
            try { edit.Restore(); }
            catch (Exception ex) { Mod.Warn("Restoring native tree loot entries: " + ex.Message); }
        Edits.Clear();
    }

    internal static void PrepareFell(TreeCutManager tree)
    {
        if (!Mod.CanReplaceDrops || LongStickItem.Data?.PickupPrefab == null) return;
        Prepare(tree._trunkFractured);
    }

    private static void Prepare(GameObject? root)
    {
        if (root == null || !root) return;
        var candidates = new List<(CollisionTriggerActivator Owner, Definitions Original)>();
        var yields = new List<TreeDropSource>();
        foreach (var activator in root.GetComponentsInChildren<CollisionTriggerActivator>(true))
        {
            // Disabled spawn-prefab options (such as quarter-log placeholders)
            // are not this tree's emitted loot. Leave activation lists alone.
            if (activator._spawnDefinitions == null) continue;
            var definitions = activator._spawnDefinitions;
            var matching = false;
            foreach (var definition in definitions)
            {
                if (definition == null) continue;
                yields.Add(new TreeDropSource(ItemId(definition._source), definition._source?.name ?? "", activator._spawnPrefabs));
                if (activator._spawnPrefabs && IsNormalStick(definition._source)) matching = true;
            }
            if (matching) candidates.Add((activator, definitions));
        }

        // Complete the log-yield check before writing any native field.
        if (!Rules.CanEditFallingTree(yields))
        {
            var hasLogs = yields.Any(y => y.SpawnEnabled && Rules.IsLogSource(y.ItemId, y.Name));
            Report(root.name, hasLogs ? "LOG TREE: original loot and falling body retained." : "TREE LOOT: no ordinary stick emission to replace.");
            return;
        }

        var prepared = new List<Edit>();
        var replaced = 0;
        foreach (var (owner, original) in candidates)
        {
            var revised = new Definitions();
            foreach (var source in original)
            {
                if (source == null) { revised.Add(source!); continue; }
                if (!IsNormalStick(source._source)) { revised.Add(source); continue; }
                revised.Add(CopyDefinition(source, LongStickItem.Data!.PickupPrefab.gameObject));
                replaced++;
            }
            prepared.Add(new Edit(owner, original, revised));
        }

        try
        {
            foreach (var edit in prepared)
            {
                edit.Owner._spawnDefinitions = edit.Applied;
                Edits[edit.Owner.Pointer] = edit;
            }
        }
        catch
        {
            foreach (var edit in prepared)
            {
                edit.Restore();
                if (Edits.TryGetValue(edit.Owner.Pointer, out var current) && ReferenceEquals(current, edit))
                    Edits.Remove(edit.Owner.Pointer);
            }
            throw;
        }
        Report(root.name, $"TREE LOOT: {replaced} TreeCutStickPickup entries changed {Rules.Stick}->{Rules.LongStick}; original falling body, extras and physics retained.");
    }

    internal static Sons.VFX.SpawnDefinition CopyDefinition(Sons.VFX.SpawnDefinition source, GameObject prefab) => new() {
        _source = prefab, _location = source._location, _parent = source._parent,
        _ignoreBoltEntities = source._ignoreBoltEntities
    };

    internal static bool IsNormalStick(GameObject? source) => ItemId(source) == Rules.Stick;
    internal static bool IsLogPickup(GameObject? source) => source != null && source && Rules.IsLogSource(ItemId(source), source.name);

    private static int ItemId(GameObject? source)
    {
        if (source == null || !source) return -1;
        var pickup = source.GetComponent<PickUp>() ?? source.GetComponentInChildren<PickUp>(true);
        return pickup == null ? -1 : pickup._itemId;
    }

    internal static string Label(GameObject? source)
    {
        if (source == null || !source) return "null";
        var id = ItemId(source);
        return source.name + (id < 0 ? "" : "[id=" + id + "]");
    }

    private static void Report(string source, string message)
    {
        if (Config.DebugLogging.Value || (Reported.Count < 48 && Reported.Add(source)))
            Mod.Info(source + ": " + message);
    }
}
