using Endnight.Rendering;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using LongStickSpear.Core;
using Sons.Gameplay;
using Sons.Inventory;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace LongStickSpear;

internal static class LongStickVisuals
{
    internal const string PivotName = "LongStickSpear_LengthPivot";
    private static bool _registered;
    private static int _logs;

    internal static void Initialize()
    {
        if (_registered) return;
        ClassInjector.RegisterTypeInIl2Cpp<LongStickAppearance>();
        _registered = true;
    }

    internal static void Attach(GameObject owner)
    {
        Initialize();
        var appearance = owner.GetComponent<LongStickAppearance>() ?? owner.GetComponentInParent<LongStickAppearance>(true) ?? owner.AddComponent<LongStickAppearance>();
        if (!appearance.enabled) appearance.enabled = true;
        appearance.RequestRefresh();
    }

    internal static bool IsLongPickup(PickUp pickup) => pickup._itemId == Rules.LongStick || pickup.ItemInstance?._itemID == Rules.LongStick;

    internal static void PickupReady(PickUp pickup)
    {
        if (IsLongPickup(pickup)) Attach(pickup.gameObject);
        else pickup.GetComponent<LongStickAppearance>()?.RequestRefresh();
    }

    internal static void Log(string text)
    {
        if (_logs++ < 20 || Config.DebugLogging.Value) Mod.Info("VISUAL " + text);
    }

    internal static void Reset() => _logs = 0;
}

// Installed only on our own pickups and layouts. A separate model pivot keeps
// the visible stretch when a native spawner resets the pickup's root scale.
public sealed class LongStickAppearance : MonoBehaviour
{
    private List<AssetReferenceRenderable>? _sources;
    private List<Binding>? _bindings;
    private List<Transform>? _pending;
    private UnityAction<Transform>? _loaded;
    private bool _scan, _subscribed, _binding;
    private int _polls;
    private float _next;

    public LongStickAppearance(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    public void RequestRefresh()
    {
        _scan = true; _polls = 0; _next = 0;
        if (enabled && gameObject.activeInHierarchy && !_subscribed) OnEnable();
    }

    public void OnEnable()
    {
        try
        {
            KelvinLongSticks.TrackOwner(gameObject);
            _sources ??= new(); _bindings ??= new(); _pending ??= new();
            _loaded ??= (UnityAction<Transform>)new Action<Transform>(ModelLoaded);
            _scan = true; _polls = 0; _next = 0;
            if (!_subscribed)
            {
                _sources.Clear(); _subscribed = true;
                foreach (var source in GetComponentsInChildren<AssetReferenceRenderable>(true))
                {
                    _sources.Add(source);
                    source.AddOnRenderableLoadedListener(_loaded, true);
                }
            }
        }
        catch (Exception ex) { Mod.Warn("Long-stick appearance activation: " + ex); }
    }

    public void LateUpdate()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + .25f;
        try
        {
            var pickup = GetComponent<PickUp>() ?? GetComponentInChildren<PickUp>(true);
            if (pickup != null && !LongStickVisuals.IsLongPickup(pickup))
            {
                RestoreForReuse();
                enabled = false;
                return;
            }
            _bindings ??= new();
            for (var i = _bindings.Count - 1; i >= 0; i--)
                if (!_bindings[i].Maintain()) { _bindings.RemoveAt(i); _scan = true; }
            if (!_scan) return;
            _scan = false; _polls++;
            Discover();
            if (_bindings.Count == 0 && _polls < 80) _scan = true;
            else if (_bindings.Count == 0 && _polls == 80)
                Mod.Warn($"No visible mesh was found for '{name}' after waiting for the model to load.");
        }
        catch (Exception ex) { Mod.Warn("Long-stick appearance update: " + ex); _scan = false; }
    }

    public void OnDisable() => Unsubscribe();
    public void OnDestroy() => Unsubscribe();

    [HideFromIl2Cpp]
    public void RestoreForReuse()
    {
        if (_bindings != null)
        {
            foreach (var binding in _bindings) binding.Restore();
            _bindings.Clear();
        }
        _pending?.Clear(); _scan = false;
        Unsubscribe();
    }

    [HideFromIl2Cpp]
    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        if (_sources == null || _loaded == null) return;
        foreach (var source in _sources)
        {
            if (source == null || !source) continue;
            try { source.RemoveOnRenderableLoadedListener(_loaded); }
            catch (Exception ex) { Mod.Trace("Appearance callback cleanup: " + ex.Message); }
        }
        _sources.Clear();
    }

    [HideFromIl2Cpp]
    private void ModelLoaded(Transform model)
    {
        try
        {
            _pending ??= new();
            if (model != null && model && !_pending.Any(t => t != null && t.Pointer == model.Pointer)) _pending.Add(model);
            _scan = true; _polls = 0; _next = 0;
        }
        catch (Exception ex) { Mod.Warn("Long-stick loaded-model setup: " + ex); }
    }

    [HideFromIl2Cpp]
    private void Discover()
    {
        if (_pending is { Count: > 0 })
        {
            var queued = _pending.ToArray(); _pending.Clear();
            foreach (var model in queued) TryBind(model);
        }
        if (_sources != null)
            foreach (var source in _sources)
            {
                if (source == null || !source) continue;
                var cached = source._cachedLoadedObject;
                if (cached != null && cached) TryBind(cached.transform);
            }
        // Local fallback handles static meshes and paths which skip the old
        // virtual OnLoadDone hook. No scan of the world is performed.
        foreach (var mesh in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mesh == null || mesh.sharedMesh == null || Covered(mesh.transform)) continue;
            var root = ModelRoot(mesh.transform);
            if (IsControlRoot(root)) CopyStaticRoot(mesh);
            else TryBind(root);
        }
        foreach (var mesh in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (mesh != null && !Covered(mesh.transform)) TryBind(ModelRoot(mesh.transform));
    }

    [HideFromIl2Cpp]
    private bool IsControlRoot(Transform model) => model.Pointer == transform.Pointer ||
        model.GetComponent<PickUp>() != null || model.GetComponent<LayoutItem>() != null;

    [HideFromIl2Cpp]
    private Transform ModelRoot(Transform model)
    {
        var root = model;
        while (root.parent != null && root.parent.Pointer != transform.Pointer &&
            root.parent.GetComponent<AssetReferenceRenderable>() == null && root.parent.GetComponent<LayoutItem>() == null &&
            root.GetComponent<AssetReferenceRenderable>() == null && !IsControlRoot(root)) root = root.parent;
        return root;
    }

    [HideFromIl2Cpp]
    private bool Covered(Transform model)
    {
        if (_bindings != null)
            foreach (var binding in _bindings)
                if (binding.Model != null && binding.Model && (model.Pointer == binding.Model.Pointer || model.IsChildOf(binding.Model))) return true;
        for (var node = model; node != null && node.Pointer != transform.Pointer; node = node.parent)
            if (node.name == LongStickVisuals.PivotName) return true;
        return false;
    }

    [HideFromIl2Cpp]
    private void TryBind(Transform model)
    {
        if (_binding || model == null || !model || !model.IsChildOf(transform) || IsControlRoot(model) || Covered(model)) return;
        if (_bindings != null)
            foreach (var binding in _bindings)
                if (binding.Model != null && binding.Model && binding.Model.IsChildOf(model)) return;
        if (!TryBounds(model, out var bounds)) return;
        var size = bounds.size;
        if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < .001f) return;
        _binding = true;
        GameObject? pivotObject = null;
        var oldParent = model.parent;
        var oldPosition = model.localPosition; var oldRotation = model.localRotation; var oldScale = model.localScale;
        try
        {
            var plan = StretchFrame.Create(ToNumerics(oldPosition), ToNumerics(oldRotation), ToNumerics(oldScale), ToNumerics(size));
            pivotObject = new GameObject(LongStickVisuals.PivotName) { layer = model.gameObject.layer };
            var pivot = pivotObject.transform;
            pivot.SetParent(oldParent, false);
            pivot.SetLocalPositionAndRotation(ToUnity(plan.PivotPosition), ToUnity(plan.PivotRotation));
            pivot.localScale = ToUnity(plan.PivotScale);
            model.SetParent(pivot, false);
            model.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.localScale = ToUnity(plan.ModelScale);
            _bindings ??= new();
            _bindings.Add(new Binding(model, pivot, plan));
            LongStickVisuals.Log($"'{name}' model '{model.name}' stretched on local axis {plan.Axis}, factor=2; loaded geometry uses a separate length pivot.");
        }
        catch
        {
            if (model != null && model)
            {
                model.SetParent(oldParent, false);
                model.SetLocalPositionAndRotation(oldPosition, oldRotation);
                model.localScale = oldScale;
            }
            if (pivotObject != null) Object.Destroy(pivotObject);
            throw;
        }
        finally { _binding = false; }
    }

    [HideFromIl2Cpp]
    private void CopyStaticRoot(MeshFilter source)
    {
        var renderer = source.GetComponent<MeshRenderer>();
        if (renderer == null || !renderer.enabled) return;
        var copy = new GameObject("LongStickSpear_StaticModel") { layer = source.gameObject.layer };
        try
        {
            copy.transform.SetParent(source.transform, false);
            copy.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            var visible = copy.AddComponent<MeshRenderer>();
            visible.sharedMaterials = renderer.sharedMaterials;
            visible.shadowCastingMode = renderer.shadowCastingMode;
            visible.receiveShadows = renderer.receiveShadows;
            TryBind(copy.transform);
            if (!Covered(copy.transform)) { Object.Destroy(copy); return; }
            renderer.enabled = false;
            _bindings![^1].HiddenOriginal = renderer;
        }
        catch { Object.Destroy(copy); throw; }
    }

    [HideFromIl2Cpp]
    private static bool TryBounds(Transform model, out Bounds bounds)
    {
        bounds = default; var found = false;
        foreach (var mesh in model.GetComponentsInChildren<MeshFilter>(true))
            if (mesh.sharedMesh != null) Include(mesh.sharedMesh.bounds, mesh.transform, model, ref bounds, ref found);
        foreach (var mesh in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            Include(mesh.localBounds, mesh.transform, model, ref bounds, ref found);
        return found;
    }

    [HideFromIl2Cpp]
    private static void Include(Bounds source, Transform mesh, Transform target, ref Bounds result, ref bool found)
    {
        var c = source.center; var e = source.extents;
        for (var i = 0; i < 8; i++)
        {
            var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            var local = target.InverseTransformPoint(mesh.TransformPoint(corner));
            if (!float.IsFinite(local.x) || !float.IsFinite(local.y) || !float.IsFinite(local.z)) continue;
            if (!found) { result = new Bounds(local, Vector3.zero); found = true; } else result.Encapsulate(local);
        }
    }

    [HideFromIl2Cpp] private static NVector3 ToNumerics(Vector3 v) => new(v.x, v.y, v.z);
    [HideFromIl2Cpp] private static NQuaternion ToNumerics(Quaternion q) => new(q.x, q.y, q.z, q.w);
    [HideFromIl2Cpp] private static Vector3 ToUnity(NVector3 v) => new(v.X, v.Y, v.Z);
    [HideFromIl2Cpp] private static Quaternion ToUnity(NQuaternion q) => new(q.X, q.Y, q.Z, q.W);

    private sealed class Binding
    {
        internal readonly Transform Model, Pivot;
        private readonly StretchFrame _plan;
        internal MeshRenderer? HiddenOriginal;
        internal Binding(Transform model, Transform pivot, StretchFrame plan) { Model = model; Pivot = pivot; _plan = plan; }
        internal bool Maintain()
        {
            if (Model == null || !Model || Pivot == null || !Pivot)
            {
                if (Model != null && Model) Model.localScale = ToUnity(_plan.ModelScale);
                if (Pivot != null && Pivot) Object.Destroy(Pivot.gameObject);
                return false;
            }
            if (Model.parent == null || Model.parent.Pointer != Pivot.Pointer) Model.SetParent(Pivot, false);
            if (Pivot.localScale != ToUnity(_plan.PivotScale)) Pivot.localScale = ToUnity(_plan.PivotScale);
            if (Model.localPosition != Vector3.zero || Model.localRotation != Quaternion.identity)
                Model.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (Model.localScale != ToUnity(_plan.ModelScale)) Model.localScale = ToUnity(_plan.ModelScale);
            if (HiddenOriginal != null && HiddenOriginal && HiddenOriginal.enabled) HiddenOriginal.enabled = false;
            return true;
        }
        internal void Restore()
        {
            if (HiddenOriginal != null && HiddenOriginal)
            {
                HiddenOriginal.enabled = true;
                if (Model != null && Model) Object.Destroy(Model.gameObject);
            }
            else if (Model != null && Model && Pivot != null && Pivot && Model.parent != null && Model.parent.Pointer == Pivot.Pointer)
            {
                Model.SetParent(Pivot.parent, false);
                Model.SetLocalPositionAndRotation(ToUnity(_plan.PivotPosition), ToUnity(_plan.PivotRotation));
                Model.localScale = ToUnity(_plan.ModelScale);
            }
            else if (Model != null && Model) Model.localScale = ToUnity(_plan.ModelScale);
            if (Pivot != null && Pivot) Object.Destroy(Pivot.gameObject);
        }
    }
}
