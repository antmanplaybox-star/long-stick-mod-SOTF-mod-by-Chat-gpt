using LongStickSpear.Core;
using Sons.Gameplay;
using Sons.Lodding;
using UnityEngine;
using Spawn = Sons.Gameplay.BreakableObject.SpawnDefinition;

namespace LongStickSpear;

internal sealed class DropSnapshot
{
    private readonly BreakableObject _owner;
    private readonly Il2CppSystem.Collections.Generic.List<Spawn>? _definitions;
    private readonly GameObject? _legacyPrefab;
    private bool _restored;

    internal DropSnapshot(BreakableObject owner)
    {
        _owner = owner; _definitions = owner._spawnDefinitions;
        _legacyPrefab = owner._pickupPrefab;
    }

    internal void Restore()
    {
        if (_restored) return;
        _restored = true;
        if (_owner == null || !_owner) return;
        _owner._spawnDefinitions = _definitions;
        _owner._pickupPrefab = _legacyPrefab;
    }
}

internal static class TreeDrops
{
    private static readonly HashSet<string> ReportedSources = new(StringComparer.Ordinal);
    internal static void Reset() => ReportedSources.Clear();

    internal static bool IsStick(GameObject? prefab) => TreeLoot.IsNormalStick(prefab);

    internal static DropSnapshot? Prepare(BreakableObject owner)
    {
        if (!Mod.CanReplaceDrops || LongStickItem.Data?.PickupPrefab == null) return null;
        var sources = new List<Spawn>();
        if (owner._spawnDefinitions != null)
            foreach (var drop in owner._spawnDefinitions) if (drop != null) sources.Add(drop);
        var holder = owner._worldObjectLocatorHolder ?? owner.GetComponentInParent<Sons.TerrainDetail.WorldObjectLocatorHolder>();
        var locator = holder?.GetLocator();
        var category = Rules.Vegetation.Unknown;
        if (locator?._data != null)
            category = locator._data.Type switch {
                LodSettingsTypeEnum.SmallTree => Rules.Vegetation.SmallTree,
                LodSettingsTypeEnum.Trees => Rules.Vegetation.Tree,
                LodSettingsTypeEnum.Bush or LodSettingsTypeEnum.SmallBush => Rules.Vegetation.Bush,
                LodSettingsTypeEnum.Plant => Rules.Vegetation.Plant,
                _ => Rules.Vegetation.Other
            };
        var name = locator?.DataName ?? owner.OriginalObject?.name ?? owner.name;
        if (!Rules.IsVegetationSource(category, name, owner._isStump, owner._isConstructionElement, false)) return null;
        // Trees using the native felling pipeline are handled by TreeLoot.
        // This path only changes actual positive direct loot on small plants.
        if (category == Rules.Vegetation.Tree ||
            sources.Any(d => d._spawnCount > 0 && TreeLoot.IsLogPickup(d._prefab)) ||
            (owner._spawnCount > 0 && TreeLoot.IsLogPickup(owner._pickupPrefab))) return null;
        var report = Config.DebugLogging.Value || (ReportedSources.Count < 32 && ReportedSources.Add(category + ":" + name));
        var oldLegacy = owner._pickupPrefab;
        if (report)
            Mod.Info($"DROP TABLE: source='{name}', category={category}, converted={owner._converted}, legacy={TreeLoot.Label(oldLegacy)} x{owner._spawnCount}, definitions=[{string.Join(",", sources.Take(12).Select(d => TreeLoot.Label(d._prefab) + " x" + d._spawnCount + " linked=" + d._linkToObjectActiveState))}].");

        var snapshot = new DropSnapshot(owner);
        try
        {
            var revised = Rules.ReplaceStickReferences(sources,
                d => Rules.CanReplaceDirectStick(IsStick(d._prefab) ? Rules.Stick : -1, d._spawnCount),
                MakeLongReference, out var direct);
            if (direct > 0)
            {
                var list = new Il2CppSystem.Collections.Generic.List<Spawn>();
                foreach (var drop in revised) list.Add(drop);
                owner._spawnDefinitions = list;
            }
            if (Rules.CanReplaceDirectStick(IsStick(oldLegacy) ? Rules.Stick : -1, owner._spawnCount))
                owner._pickupPrefab = LongStickItem.Data!.PickupPrefab.gameObject;
            if (report)
                Mod.Info($"DROP REFERENCES: source='{name}', definitionsChanged={direct}, legacy={TreeLoot.Label(oldLegacy)}->{TreeLoot.Label(owner._pickupPrefab)}, countsPreserved=True, definitions=[{string.Join(",", revised.Take(12).Select(d => TreeLoot.Label(d._prefab) + " x" + d._spawnCount))}].");
            return snapshot;
        }
        catch { snapshot.Restore(); throw; }
    }

    internal static Spawn CopyDefinition(Spawn source, GameObject? prefab) => new() {
        _spawnCount = source._spawnCount, _prefab = prefab,
        _rotation = source._rotation, _startHeightOffset = source._startHeightOffset,
        _shouldStackSpawns = source._shouldStackSpawns, _stackHeightOffset = source._stackHeightOffset,
        _makeRigidBodyDynamic = source._makeRigidBodyDynamic, _lifeTime = source._lifeTime,
        _copyOriginalObjectsScale = source._copyOriginalObjectsScale,
        _addExplosiveForceRigidBodies = source._addExplosiveForceRigidBodies,
        _explosionForce = source._explosionForce, _explosiveUpwardsModifier = source._explosiveUpwardsModifier,
        _linkToObjectActiveState = source._linkToObjectActiveState,
        _linkedObjectActiveStateSource = source._linkedObjectActiveStateSource
    };

    private static Spawn MakeLongReference(Spawn source) => CopyDefinition(source, LongStickItem.Data!.PickupPrefab.gameObject);
}
