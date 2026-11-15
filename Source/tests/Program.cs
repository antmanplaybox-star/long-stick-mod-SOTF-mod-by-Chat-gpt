using LongStickSpear.Core;
using System.Numerics;
using V = LongStickSpear.Core.Rules.Vegetation;

var count = 0;
void Check(string name, Action body) { body(); count++; Console.WriteLine("PASS " + name); }
void Assert(bool test, string reason) { if (!test) throw new Exception(reason); }
bool Eligible(V type, string name = "", bool stump = false, bool construction = false, bool cutting = false, bool sticks = true)
    => Rules.ShouldReplace(type, name, stump, construction, cutting, sticks);

Check("A stick-producing SmallTree qualifies", () => Assert(Eligible(V.SmallTree), "Thin tree rejected"));
Check("Stick-producing bushes qualify without tree-specific names", () => Assert(Eligible(V.Bush, "Bush"), "Stick-producing shrub rejected"));
Check("Log-only trees do not acquire extra loot", () => Assert(!Eligible(V.Tree, "Pine", cutting: true, sticks: false), "Log-only tree acquired a stick drop"));
Check("Stumps stay vanilla", () => Assert(!Eligible(V.SmallTree, stump: true), "Stump affected"));
Check("Construction pieces stay vanilla", () => Assert(!Eligible(V.SmallTree, construction: true), "Building affected"));
Check("Native tree-cutting context allows existing stick loot", () => Assert(Eligible(V.SmallTree, cutting: true), "Tree-cutting stick loot rejected"));
Check("Leaf-only small trees retain their drops", () => Assert(!Eligible(V.SmallTree, sticks: false), "Non-stick loot changed"));
Check("An explicit sapling name works without a locator", () => Assert(Eligible(V.Unknown, "Birch_Sapling_LOD0"), "Sapling fallback failed"));
Check("Generic names cannot turn all breakables into thin trees", () => Assert(!Eligible(V.Unknown, "Breakable") && !Eligible(V.Unknown, "Tree"), "Overbroad fallback"));
Check("Stump names stay excluded in every vegetation category", () => {
    foreach (var category in Enum.GetValues<V>())
        Assert(!Eligible(category, "SaplingStump"), "Named stump affected: " + category);
});
Check("Spruce seedlings recorded as Bush in the game log now qualify", () => {
    foreach (var variant in new[] { "A", "B", "C" })
        Assert(Eligible(V.Bush, "TreeSpruceSeedling" + variant + "LocatorData"), "Missed spruce seedling " + variant);
});
Check("Tree seedlings and saplings qualify with a Bush or missing locator", () => {
    foreach (var category in new[] { V.Bush, V.Unknown })
        foreach (var name in new[] { "TreeSpruceSeedlingALocatorData", "Tree_Pine_Seedling_B", "Birch_Sapling_LOD0", "ThinTree_LOD0" })
            Assert(Eligible(category, name), "Missed tree variant: " + category + " / " + name);
});
Check("Stick-producing shrubs from the earlier log now qualify", () => {
    foreach (var name in new[] { "SalalLemonBushALocatorData", "RhododendronALocatorData", "SnowBrushALocatorData" })
        Assert(Eligible(V.Bush, name), "Missed stick-producing shrub: " + name);
    Assert(!Eligible(V.Bush, "CaliforniaLilacBushALocatorData", sticks: false), "Leaf-only bush affected");
});
Check("Stick-producing Tree entries qualify without the log-cutting pipeline", () => {
    Assert(Eligible(V.Tree, "TreeVariant"), "Stick-producing tree rejected");
    Assert(!Eligible(V.Tree, "TreeVariant", sticks: false), "Tree without stick loot affected");
});
Check("Vegetation support still excludes stumps, construction and absent stick loot", () => {
    foreach (var category in new[] { V.Bush, V.Unknown, V.Tree, V.SmallTree, V.Plant }) {
        const string name = "TreeSpruceSeedlingALocatorData";
        Assert(!Eligible(category, name, stump: true), "Stump affected");
        Assert(!Eligible(category, name, construction: true), "Construction affected");
        Assert(!Eligible(category, name, sticks: false), "Non-stick loot affected");
    }
    Assert(!Eligible(V.Other, "TreeSpruceSeedlingALocatorData"), "Unrelated locator category affected");
});
Check("Plant-category stick sources qualify but non-stick plants are excluded", () => {
    Assert(Eligible(V.Plant, "WoodyPlant"), "Stick-producing plant rejected");
    Assert(!Eligible(V.Plant, "WoodyPlant", sticks: false), "Plant without stick loot affected");
});
Check("Known natural categories do not depend on prefab naming", () => {
    foreach (var category in new[] { V.SmallTree, V.Tree, V.Bush, V.Plant })
        foreach (var name in new[] { "", "Breakable(Clone)", "Variant_C", "PlantSeedling", "TreeSeedling_Bush" })
            Assert(Eligible(category, name), "Name filter still hides natural vegetation: " + category + " / " + name);
});
Check("Woody-vegetation names work when the locator is missing", () => {
    foreach (var name in new[] { "SalalLemonBushA", "SnowBrushA_LOD0", "WoodyShrub", "SeedlingB", "SmallTree_Bush" })
        Assert(Eligible(V.Unknown, name), "Missing-locator vegetation rejected: " + name);
    Assert(!Eligible(V.Unknown, "Breakable") && !Eligible(V.Unknown, "Crate"), "Generic scenery prop affected");
});
Check("Non-vegetation locator categories never qualify through a plant name", () => {
    foreach (var name in new[] { "Bush", "SmallTree", "Seedling", "SnowBrushA", "Sapling" })
        Assert(!Eligible(V.Other, name), "Non-vegetation category overridden by name: " + name);
});
Check("Detached falling-tree branches qualify without a locator or tree-like name", () => {
    Assert(Eligible(V.Unknown, "BranchExtras(Clone)", cutting: true), "Detached branch lost its provenance");
    Assert(!Eligible(V.Unknown, "BranchExtras(Clone)"), "Unmarked generic prop gained tree provenance");
    Assert(!Eligible(V.Unknown, "BranchExtras(Clone)", cutting: true, stump: true), "Stump context accepted");
    Assert(!Eligible(V.Unknown, "BranchExtras(Clone)", cutting: true, construction: true), "Construction context accepted");
});
Check("A falling tree's logs and leaves survive stick replacement unchanged", () => {
    var log = new Drop("log", 4); var leaves = new Drop("leaves", 8);
    var original = new[] { log, new Drop("stick", 3), leaves };
    var result = Rules.ReplaceStickReferences(original, d => d.Kind == "stick", d => d with { Kind = "longstick" }, out var replaced);
    Assert(replaced == 1 && result.Count == 3, "Wrong fall-drop table size");
    Assert(ReferenceEquals(result[0], log) && log.Count == 4, "Log loot was altered");
    Assert(ReferenceEquals(result[2], leaves) && result[1] == new Drop("longstick", 3), "Leaves or replacement count altered");
});
Check("A selected falling-tree stick is replaced without reordering spawn choices", () => {
    var original = new[] { new Drop("log", 1), new Drop("stick", 1), new Drop("leaves", 1), new Drop("stick", 1) };
    var result = Rules.ReplaceSelectedStick(original, 1, d => d.Kind == "stick", _ => new Drop("longstick", 1))!;
    Assert(result.Count == original.Length && result[1].Kind == "longstick", "Selected stick not replaced");
    foreach (var i in new[] { 0, 2, 3 }) Assert(ReferenceEquals(result[i], original[i]), "Other spawn choice changed");
    Assert(original[1].Kind == "stick", "Original spawn list mutated");
});
Check("Selecting logs, leaves or an existing Long Stick does not change a spawn table", () => {
    var original = new[] { new Drop("log", 1), new Drop("leaves", 1), new Drop("longstick", 1) };
    for (var i = 0; i < original.Length; i++)
        Assert(Rules.ReplaceSelectedStick(original, i, d => d.Kind == "stick", _ => throw new Exception("Unexpected replacement")) == null, "Non-stick choice replaced");
});
Check("Invalid falling-tree spawn indices leave the table alone", () => {
    var original = new[] { new Drop("stick", 1) };
    foreach (var i in new[] { int.MinValue, -1, 1, int.MaxValue })
        Assert(Rules.ReplaceSelectedStick(original, i, _ => throw new Exception("Invalid index was inspected"), _ => throw new Exception()) == null, "Invalid index accepted");
});
Check("A selected-drop factory failure leaves the original table intact", () => {
    var original = new[] { new Drop("stick", 1), new Drop("log", 1) }; var before = original.ToArray(); var threw = false;
    try { Rules.ReplaceSelectedStick(original, 0, d => d.Kind == "stick", _ => throw new InvalidOperationException()); }
    catch (InvalidOperationException) { threw = true; }
    Assert(threw && original.SequenceEqual(before), "Failure changed the original fall table");
});

Check("Zero-count plant placeholders and the falling cut body retain their original references", () => {
    var cut = new Drop("TreeSpruceSeedlingCCutPrefab", 1); var stick = new Drop("stick", 0);
    var original = new[] { stick, cut };
    var result = Rules.ReplaceStickReferences(original, d => Rules.CanReplaceDirectStick(d.Kind == "stick" ? Rules.Stick : -1, d.Count), d => d with { Kind = "longstick" }, out var changed);
    Assert(changed == 0 && ReferenceEquals(result[0], stick), "Zero-count placeholder was changed");
    Assert(ReferenceEquals(result[1], cut) && stick == new Drop("stick", 0), "Falling cut body or original table changed");
});
Check("Woody vegetation recognition is independent of the native converted table flag", () => {
    foreach (var name in new[] { "TreeSpruceSeedlingCLocatorData", "RhododendronALocatorData", "SalalLemonBushALocatorData", "SnowBrushALocatorData" })
        Assert(Rules.IsVegetationSource(V.Bush, name, false, false, false), "Recognized plant source was lost: " + name);
    var legacy = new Drop("stick", 1);
    var result = Rules.ReplaceStickReferences(new[] { legacy }, d => d.Kind == "stick", d => d with { Kind = "longstick" }, out var changed);
    Assert(changed == 1 && result[0] == new Drop("longstick", 1), "Legacy reference could not be replaced");
});
Check("A cut-prefab graph follows the later emitter rather than requiring immediate sticks", () => {
    var cut = new TestPrefab("TreeSpruceSeedlingCCutPrefab");
    var impact = new TestPrefab("OnImpact"); var emitter = new TestPrefab("SpawnPrefab");
    cut.References.Add(impact); impact.References.Add(emitter);
    var result = PrefabGraph.Gather(cut, n => n.References, n => !n.SharedPickup, n => n.Name);
    Assert(result.SequenceEqual(new[] { cut, impact, emitter }), "Deferred tree source was not traversed");
});
Check("Referenced-prefab cycles terminate without revisiting nodes", () => {
    var a = new TestPrefab("A"); var b = new TestPrefab("B");
    a.References.Add(b); b.References.Add(a);
    Assert(PrefabGraph.Gather(a, n => n.References, _ => true, n => n.Name).Count == 2, "Cycle was not bounded");
});
Check("Shared pickup prefabs are leaves and never become marked tree sources", () => {
    var source = new TestPrefab("CutPrefab"); var pickup = new TestPrefab("StickPickup", true);
    source.References.Add(pickup); pickup.References.Add(new TestPrefab("PickupInternals"));
    var result = PrefabGraph.Gather(source, n => n.References, n => !n.SharedPickup, n => n.Name);
    Assert(result.Count == 1 && ReferenceEquals(result[0], source), "Common pickup assets were tagged or traversed");
});
Check("Prefab traversal respects its budget on a large reference chain", () => {
    var nodes = Enumerable.Range(0, 12).Select(i => new TestPrefab(i.ToString())).ToArray();
    for (var i = 0; i < nodes.Length - 1; i++) nodes[i].References.Add(nodes[i + 1]);
    Assert(PrefabGraph.Gather(nodes[0], n => n.References, _ => true, n => n.Name, 4).Count == 4, "Traversal budget exceeded");
});
Check("Shared prefab references are processed once", () => {
    var source = new TestPrefab("CutPrefab"); var child = new TestPrefab("Emitter");
    source.References.Add(child); source.References.Add(child);
    Assert(PrefabGraph.Gather(source, n => n.References, _ => true, n => n.Name).Count == 2, "Duplicate reference processed twice");
});
Check("Only a live normal-stick pickup with tree provenance can be converted", () => {
    Assert(Rules.CanConvertTreePickup(Rules.Stick, true, true), "Tree-emitted stick rejected");
    Assert(!Rules.CanConvertTreePickup(Rules.Stick, false, true), "Loose ground stick or pool reuse was converted");
    Assert(!Rules.CanConvertTreePickup(Rules.Stick, true, false), "Shared prefab asset was converted");
    foreach (var id in new[] { Rules.LongStick, 78, Rules.Spear, 484 })
        Assert(!Rules.CanConvertTreePickup(id, true, true), "Non-stick item or already-long pickup was converted");
});

Check("All stick definitions retain their quantities and order while leaves retain identity", () => {
    var leaves = new Drop("leaves", 4); var a = new Drop("stick", 2); var b = new Drop("stick", 1);
    var original = new List<Drop> { leaves, a, b };
    var revised = Rules.ReplaceStickReferences(original, d => d.Kind == "stick", d => d with { Kind = "longstick" }, out var replaced);
    Assert(replaced == 2 && revised.Count == 3, "A native definition was removed");
    Assert(ReferenceEquals(revised[0], leaves) && revised[1] == new Drop("longstick", 2) && revised[2] == new Drop("longstick", 1), "Other loot, ordering or quantities changed");
    Assert(original.SequenceEqual(new[] { leaves, a, b }) && a.Count == 2, "Original definitions were mutated");
});
Check("Unrelated loot does not call the replacement factory", () => {
    var d = new Drop("stone", 3);
    var result = Rules.ReplaceStickReferences(new[] { d }, x => x.Kind == "stick", _ => throw new Exception("Unexpected factory call"), out var changed);
    Assert(changed == 0 && ReferenceEquals(result.Single(), d), "Unrelated loot changed");
});
Check("A failed replacement leaves the source list intact", () => {
    var original = new List<Drop> { new("stick", 2), new("leaves", 4) };
    var snapshot = original.ToArray(); var threw = false;
    try { Rules.ReplaceStickReferences(original, _ => true, _ => throw new InvalidOperationException(), out _); }
    catch (InvalidOperationException) { threw = true; }
    Assert(threw && original.SequenceEqual(snapshot), "Failure mutated the source");
});
Check("Existing long-stick drops do not get replaced again", () => {
    var original = new[] { new Drop("longstick", 1) };
    var revised = Rules.ReplaceStickReferences(original, x => x.Kind == "stick", _ => throw new Exception(), out var changed);
    Assert(changed == 0 && revised.SequenceEqual(original), "Repeated replacement duplicated loot");
});
Check("Linked placeholders stay native and positive direct loot retains its condition", () => {
    var original = new[] { new Drop("stick", 0, true), new Drop("leaves", 6, true), new Drop("stick", 4, false) };
    var result = Rules.ReplaceStickReferences(original, d => Rules.CanReplaceDirectStick(d.Kind == "stick" ? Rules.Stick : -1, d.Count), d => d with { Kind = "longstick" }, out var changed);
    Assert(changed == 1 && result.SequenceEqual(new[] { original[0], original[1], new Drop("longstick", 4) }), "Placeholder, count or linked-state settings were changed");
});
Check("A replacement failure after an earlier copy leaves all native source entries intact", () => {
    var original = new[] { new Drop("stick", 0), new Drop("stick", 3) }; var calls = 0; var failed = false;
    try { Rules.ReplaceStickReferences(original, d => d.Kind == "stick", d => ++calls == 2 ? throw new InvalidOperationException() : d with { Kind = "longstick" }, out _); }
    catch (InvalidOperationException) { failed = true; }
    Assert(failed && calls == 2 && original.SequenceEqual(new[] { new Drop("stick", 0), new Drop("stick", 3) }), "Partial failure changed a source entry");
});
Check("A tree with no stick references gains no long-stick definition", () => {
    var original = new[] { new Drop("log", 2), new Drop("cut body", 1) };
    var result = Rules.ReplaceStickReferences(original, d => d.Kind == "stick", _ => throw new Exception("No stick should be created"), out var changed);
    Assert(changed == 0 && result.SequenceEqual(original), "A log-only tree acquired long-stick loot");
});
Check("The logged small spruce tree qualifies while disabled quarter-log options stay native", () => {
    var sources = new[] { new TreeDropSource(392, "TreeCutStickPickup", true), new TreeDropSource(408, "LogQuarterX2Pickup", false), new TreeDropSource(406, "LogQuarterX1Pickup", false) };
    Assert(Rules.CanEditFallingTree(sources), "Recorded stick-producing tree was excluded");
});
Check("The user's final large log-producing tree is excluded", () => {
    var sources = new[] { new TreeDropSource(78, "LogPickup", true), new TreeDropSource(408, "LogQuarterX2Pickup", false) };
    Assert(!Rules.CanEditFallingTree(sources), "Recorded large log-producing tree was included");
});
Check("A tree with both active log and stick yields is left entirely native in either order", () => {
    var sources = new[] { new TreeDropSource(392, "TreeCutStickPickup", true), new TreeDropSource(78, "LogPickup", true) };
    Assert(!Rules.CanEditFallingTree(sources) && !Rules.CanEditFallingTree(sources.Reverse()), "Mixed log tree could be edited");
});
Check("Disabled stick spawners do not qualify an unrelated tree", () => {
    Assert(!Rules.CanEditFallingTree(new[] { new TreeDropSource(392, "TreeCutStickPickup", false), new TreeDropSource(-1, "LeafFX", true) }), "An inactive stick option qualified a tree");
});
Check("Active quarter-log loot also excludes a tree without depending on an ID list", () => {
    Assert(!Rules.CanEditFallingTree(new[] { new TreeDropSource(392, "TreeCutStickPickup", true), new TreeDropSource(408, "LogQuarterX2Pickup", true) }), "An active log variant was changed");
    Assert(Rules.IsLogSource(78, "UnknownName") && Rules.IsLogSource(406, "logQuarterX1Pickup"), "Native log variants were missed");
});
Check("Empty, unrelated and already-long loot do not create new stick drops", () => {
    Assert(!Rules.CanEditFallingTree(Array.Empty<TreeDropSource>()), "Empty tree gained loot");
    Assert(!Rules.CanEditFallingTree(new[] { new TreeDropSource(813201, "LongStickSpear_Pickup", true), new TreeDropSource(-1, "LeafFX", true) }), "Already-long tree gained duplicate loot");
});
Check("Only positive normal-stick direct counts qualify for plant reference replacement", () => {
    foreach (var count in new[] { int.MinValue, -1, 0 }) Assert(!Rules.CanReplaceDirectStick(392, count), "Inactive count accepted");
    Assert(Rules.CanReplaceDirectStick(392, 1) && Rules.CanReplaceDirectStick(392, 4), "Positive stick count rejected");
    Assert(!Rules.CanReplaceDirectStick(78, 4) && !Rules.CanReplaceDirectStick(813201, 1), "Logs or existing long sticks changed");
});
Check("The alternate spear uses exactly one consumable long stick and a reusable knife", () => {
    Assert(Rules.SpearIngredients.SequenceEqual(new[] { new Ingredient(813201, 1, false), new Ingredient(380, 1, true) }), "Incorrect recipe");
    Assert(!Rules.SpearIngredients.Any(i => i.ItemId == Rules.Tape || i.ItemId == Rules.Stick), "Tape or short sticks required");
    Assert(Rules.Spear == 474 && Rules.LongStick != Rules.Stick, "Item IDs collide or result isn't vanilla spear");
});
Check("Model extent detection supports sticks oriented on each axis", () => {
    Assert(Rules.LongestAxis(2, .03f, .04f) == 0, "X");
    Assert(Rules.LongestAxis(.04f, 2, .03f) == 1, "Y");
    Assert(Rules.LongestAxis(.04f, .03f, 2) == 2, "Z");
});
Check("Invalid model extents are rejected", () => {
    var threw = 0;
    foreach (var x in new[] { float.NaN, float.PositiveInfinity, -.1f })
        try { Rules.LongestAxis(x, 1, 1); } catch (ArgumentOutOfRangeException) { threw++; }
    Assert(threw == 3, "Invalid mesh math accepted");
});
Matrix4x4 Matrix(StretchFrame frame) => Matrix4x4.CreateScale(frame.ModelScale) * Matrix4x4.CreateScale(frame.PivotScale) *
    Matrix4x4.CreateFromQuaternion(frame.PivotRotation) * Matrix4x4.CreateTranslation(frame.PivotPosition);
void Near(float actual, float expected) => Assert(Math.Abs(actual - expected) < .0001f, $"Expected {expected}, got {actual}");
float Span(Vector3 delta, Matrix4x4 transform) => Vector3.Distance(Vector3.Transform(-delta / 2, transform), Vector3.Transform(delta / 2, transform));
Check("A rotated model doubles its length while preserving both thickness axes", () => {
    var p = new Vector3(.3f, .4f, -.8f);
    var q = Quaternion.CreateFromYawPitchRoll(.7f, .2f, -.4f);
    var frame = StretchFrame.Create(p, q, Vector3.One, new Vector3(.03f, .04f, 1));
    var native = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(p);
    var stretched = Matrix(frame);
    Near(Span(Vector3.UnitZ, stretched), 2 * Span(Vector3.UnitZ, native));
    Near(Span(Vector3.UnitX, stretched), Span(Vector3.UnitX, native));
    Near(Span(Vector3.UnitY, stretched), Span(Vector3.UnitY, native));
    Assert(Vector3.Distance(Vector3.Transform(Vector3.Zero, stretched), p) < .0001f, "Model origin moved");
});
Check("Nonuniform native scaling is included when selecting the length axis", () => {
    var frame = StretchFrame.Create(Vector3.Zero, Quaternion.Identity, new Vector3(.01f, 2, 1), new Vector3(10, 1, .1f));
    Assert(frame.Axis == 1, "Raw mesh bounds incorrectly overrode the visible length axis");
    Near(Span(Vector3.UnitY, Matrix(frame)), 4);
    Near(Span(Vector3.UnitX, Matrix(frame)), .01f);
});
Check("A native pickup-root scale reset cannot cancel the separate length pivot", () => {
    var frame = StretchFrame.Create(new Vector3(0, .1f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, .6f), Vector3.One, new Vector3(2, .03f, .03f));
    var baseMatrix = Matrix4x4.CreateFromQuaternion(frame.PivotRotation) * Matrix4x4.CreateTranslation(frame.PivotPosition);
    foreach (var nativeScale in new[] { Vector3.One, new Vector3(.4f), new Vector3(1.2f, .8f, .9f), Vector3.One }) {
        var parent = Matrix4x4.CreateScale(nativeScale) * Matrix4x4.CreateTranslation(4, 2, -3);
        Near(Span(Vector3.UnitX, Matrix(frame) * parent), 2 * Span(Vector3.UnitX, baseMatrix * parent));
        Near(Span(Vector3.UnitY, Matrix(frame) * parent), Span(Vector3.UnitY, baseMatrix * parent));
    }
});
Check("Mirrored mesh scales keep their handedness and unchanged thickness", () => {
    var frame = StretchFrame.Create(Vector3.Zero, Quaternion.Identity, new Vector3(-1, 1, 1), new Vector3(1, .03f, .04f));
    Near(Span(Vector3.UnitX, Matrix(frame)), 2);
    Near(Span(Vector3.UnitY, Matrix(frame)), 1);
    Assert(Matrix(frame).GetDeterminant() < 0, "Mirroring was lost");
});
Check("Empty or invalid loaded mesh extents do not create a length pivot", () => {
    var threw = 0;
    foreach (var extent in new[] { Vector3.Zero, new Vector3(float.NaN, 1, 1), new Vector3(float.PositiveInfinity, 1, 1) })
        try { StretchFrame.Create(Vector3.Zero, Quaternion.Identity, Vector3.One, extent); }
        catch (ArgumentOutOfRangeException) { threw++; }
    Assert(threw == 3, "Invalid visual model accepted");
});
Check("The long-stick holder accepts two ten-stick trips and rejects the twenty-first", () => {
    var stored = 0;
    foreach (var trip in new[] { 10, 10 }) {
        Assert(StorageRules.CanDeposit(Rules.LongStick, trip, stored, StorageRules.Capacity), "A valid trip was rejected");
        stored += trip;
    }
    Assert(stored == 20 && !StorageRules.CanDeposit(Rules.LongStick, 1, stored, StorageRules.Capacity), "Holder exceeds twenty");
    Assert(Rules.Capacity == 10, "Player carry capacity was changed");
});
Check("Normal sticks and unrelated items cannot enter the long-stick holder", () => {
    foreach (var id in new[] { Rules.Stick, Rules.Spear, Rules.Tape, 78, -1 })
        Assert(!StorageRules.CanDeposit(id, 1, 0, 20), "Wrong item accepted: " + id);
});
Check("A batch cannot overflow a nearly full holder", () => {
    Assert(StorageRules.CanDeposit(Rules.LongStick, 2, 18, 20), "Last two slots rejected");
    Assert(!StorageRules.CanDeposit(Rules.LongStick, 3, 18, 20), "Batch overflow accepted");
    Assert(!StorageRules.CanDeposit(Rules.LongStick, 21, 0, 20), "Oversized batch accepted");
});
Check("Removing one item from a full holder opens exactly one slot", () => {
    const int restoredCount = 20;
    Assert(!StorageRules.CanDeposit(Rules.LongStick, 1, restoredCount, 20), "Full holder not full");
    Assert(StorageRules.CanDeposit(Rules.LongStick, 1, restoredCount - 1, 20), "Freed slot unavailable");
    Assert(!StorageRules.CanDeposit(Rules.LongStick, 2, restoredCount - 1, 20), "More than one slot opened");
});
Check("Multiple native storage slots share a total budget of twenty", () => {
    for (var slots = 1; slots <= 64; slots++) {
        var sum = Enumerable.Range(0, slots).Sum(i => StorageRules.SlotCapacity(i, slots));
        Assert(sum == 20, "Capacity multiplied by the number of native slots");
        foreach (var i in Enumerable.Range(0, slots)) Assert(StorageRules.SlotCapacity(i, slots) >= 0, "Negative quota");
    }
});
Check("Invalid or overflowing deposit counts are rejected", () => {
    foreach (var amount in new[] { int.MinValue, -1, 0, 21, int.MaxValue })
        Assert(!StorageRules.CanDeposit(Rules.LongStick, amount, 0, 20), "Invalid count accepted");
    Assert(!StorageRules.CanDeposit(Rules.LongStick, 1, int.MaxValue, 20), "Stored count overflow accepted");
    Assert(!StorageRules.CanDeposit(Rules.LongStick, 1, -1, 20), "Negative stored count accepted");
});
Check("Native stick-holder names outrank unrelated structures", () => {
    Assert(StorageRules.SourceScore("Storage_SticksRecipe") > StorageRules.SourceScore("Shelf"), "StorageSticks not identified");
    Assert(StorageRules.SourceScore("Stick Holder") == 100 && StorageRules.SourceScore("Rock Storage") == 0, "Wrong storage source scored");
});
Dictionary<string, TestPrefab> CopyGraph(TestPrefab root, Func<TestPrefab, TestPrefab> leaf)
{
    var sources = PrefabGraph.Gather(root, n => n.References, n => !n.SharedPickup, n => n.Name);
    return PrefabGraph.CloneAndRewrite(sources, n => n.Name,
        n => { var copy = new TestPrefab(n.Name); copy.References.AddRange(n.References); return copy; },
        (copy, resolve) => { for (var i = 0; i < copy.References.Count; i++) copy.References[i] = resolve(copy.References[i]); }, leaf);
}
Check("Copied cyclic tree emitters resolve to copies without recursion", () => {
    var cut = new TestPrefab("cut"); var impact = new TestPrefab("impact");
    cut.References.Add(impact); impact.References.Add(cut);
    var copies = CopyGraph(cut, n => n);
    Assert(copies.Count == 2 && !ReferenceEquals(copies["cut"], cut), "Source tree was reused");
    Assert(ReferenceEquals(copies["cut"].References[0], copies["impact"]), "Cut still references the original emitter");
    Assert(ReferenceEquals(copies["impact"].References[0], copies["cut"]), "Cycle did not resolve to copied root");
});
Check("Two cut branches share one copied deferred emitter", () => {
    var cut = new TestPrefab("cut"); var left = new TestPrefab("left"); var right = new TestPrefab("right"); var emitter = new TestPrefab("emitter");
    cut.References.AddRange(new[] { left, right }); left.References.Add(emitter); right.References.Add(emitter);
    var copies = CopyGraph(cut, n => n);
    Assert(copies.Count == 4 && ReferenceEquals(copies["left"].References[0], copies["right"].References[0]), "Shared emitter was duplicated");
    Assert(ReferenceEquals(copies["left"].References[0], copies["emitter"]), "Shared link retained native emitter");
});
Check("A deferred emitter replaces stick leaves while preserving logs, order and sources", () => {
    var cut = new TestPrefab("cut"); var emitter = new TestPrefab("emitter");
    var stick = new TestPrefab("stick", true); var log = new TestPrefab("log", true); var longer = new TestPrefab("long", true);
    cut.References.Add(emitter); emitter.References.AddRange(new[] { log, stick, log, stick });
    var copies = CopyGraph(cut, n => ReferenceEquals(n, stick) ? longer : n);
    Assert(copies.Count == 2 && copies["emitter"].References.SequenceEqual(new[] { log, longer, log, longer }), "Native loot ordering or unrelated leaf changed");
    Assert(ReferenceEquals(cut.References[0], emitter) && emitter.References.SequenceEqual(new[] { log, stick, log, stick }), "Original tree or shared pickup changed");
});
Check("A failed copy rewrite leaves native source references intact", () => {
    var cut = new TestPrefab("cut"); var emitter = new TestPrefab("emitter"); cut.References.Add(emitter);
    var failed = false;
    try {
        PrefabGraph.CloneAndRewrite(new[] { cut, emitter }, n => n.Name,
            n => { var copy = new TestPrefab(n.Name); copy.References.AddRange(n.References); return copy; },
            (copy, resolve) => { if (copy.References.Count > 0) copy.References[0] = resolve(copy.References[0]); throw new InvalidOperationException("failed rewrite"); }, n => n);
    } catch (InvalidOperationException) { failed = true; }
    Assert(failed && ReferenceEquals(cut.References[0], emitter) && emitter.References.Count == 0, "Failure altered the original prefab graph");
});
var naturalContext = new WorldStickContext(392, true, true, false, 0);
var naturalId = new byte[] { 0x11, 0x24, 0x46, 0x87, 0x92, 0x07, 0x15, 0x55, 0x01, 0x03, 0x99, 0x44, 0x15, 0x78, 0x10, 0x20 };
Check("Natural ground stick chance defaults to ten percent", () => {
    Assert(WorldStickRules.DefaultChance == 10f, "Default differs from the requested 10 percent");
    Assert(WorldStickRules.Selected(999, WorldStickRules.DefaultChance) && !WorldStickRules.Selected(1000, WorldStickRules.DefaultChance), "10 percent bucket boundary is wrong");
});
Check("The same natural location keeps its choice across repeated loads and LODs", () => {
    var expected = WorldStickRules.ShouldReplace(naturalContext, naturalId, 15);
    foreach (var lod in new[] { 0, 1, 2, 0, 2, 1 })
        Assert(WorldStickRules.ShouldReplace(naturalContext with { Lod = lod }, naturalId.ToArray(), 15) == expected, "LOD or copied save identity rerolled the location");
});
Check("Drop and storage contexts stay unchanged even at a hundred percent", () => {
    foreach (var source in new[] { "inventory drop", "console spawn", "tree drop", "storage removal", "loose runtime pickup" }) {
        var context = naturalContext with { MapPickup = false };
        Assert(!WorldStickRules.ShouldReplace(context, naturalId, 100), "Non-map source was selected: " + source);
    }
});
Check("Cleared or overridden map locations are not repopulated", () => {
    Assert(!WorldStickRules.ShouldReplace(naturalContext with { OriginalState = false }, naturalId, 100), "Collected/damaged location was changed");
    Assert(!WorldStickRules.ShouldReplace(naturalContext with { HasOverride = true }, naturalId, 100), "Runtime override was replaced");
    Assert(!WorldStickRules.ShouldReplace(naturalContext, Array.Empty<byte>(), 100), "Location without a stable map identity was accepted");
});
Check("Other natural items and already-long sticks do not enter the chance roll", () => {
    foreach (var item in new[] { 78, 380, 419, 474, 813201, -1 })
        Assert(!WorldStickRules.ShouldReplace(naturalContext with { ItemId = item }, naturalId, 100), "Other item was selected: " + item);
});
Check("Billboard and invalid LOD requests keep their native source", () => {
    foreach (var lod in new[] { -128, -1, 3, 127 })
        Assert(!WorldStickRules.ShouldReplace(naturalContext with { Lod = lod }, naturalId, 100), "Non-pickup LOD accepted");
});
Check("Zero disables natural replacements and a hundred percent selects all eligible locations", () => {
    for (var i = 0; i < 1000; i++) {
        var id = BitConverter.GetBytes(i);
        Assert(!WorldStickRules.ShouldReplace(naturalContext, id, 0), "Zero percent converted a placement");
        Assert(WorldStickRules.ShouldReplace(naturalContext, id, 100), "Hundred percent missed an eligible placement");
    }
});
Check("Natural chance values are clamped without accepting non-finite settings", () => {
    Assert(WorldStickRules.Chance(-10) == 0 && WorldStickRules.Chance(120) == 100, "Percent bounds are not clamped");
    foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        Assert(!WorldStickRules.Selected(0, value), "Invalid chance caused a replacement");
});
Check("The stable map roll produces about ten percent over many independent positions", () => {
    var selected = 0;
    for (var i = 0; i < 100000; i++) if (WorldStickRules.ShouldReplace(naturalContext, BitConverter.GetBytes(i), WorldStickRules.DefaultChance)) selected++;
    Assert(selected is > 9500 and < 10500, "Natural placement distribution is biased: " + selected);
});
Check("A placement identity has a process-independent fixed hash", () => {
    var a = WorldStickRules.LocationHash(naturalId);
    Assert(a == 3799554552u, "Stable placement hash changed; existing locations would reroll");
    Assert(a == WorldStickRules.LocationHash(naturalId.ToArray()), "Hash depends on buffer identity");
    Assert(a != WorldStickRules.LocationHash(new byte[] { 1, 2, 3, 4 }), "Different placement identities collapsed");
});
Check("Only authored default-state world-item stick records are natural sources", () => {
    Assert(NaturalSpotRules.IsAuthoredWorldItem(392, false, true), "Authored stick record rejected");
    Assert(!NaturalSpotRules.IsAuthoredWorldItem(392, true, true), "Runtime drop became a world source");
    Assert(!NaturalSpotRules.IsAuthoredWorldItem(392, false, false), "Consumed/destroyed world item became a source");
    foreach (var item in new[] { 78, 380, 474, 813201 })
        Assert(!NaturalSpotRules.IsAuthoredWorldItem(item, false, true), "Other authored item accepted");
});
Check("Static virtual pickup provenance rejects runtime, dynamic and despawning sticks", () => {
    Assert(NaturalSpotRules.IsNatural(new(392, false, false, false)), "Static map pickup rejected");
    Assert(!NaturalSpotRules.IsNatural(new(392, true, false, false)), "Runtime-created pickup accepted");
    Assert(!NaturalSpotRules.IsNatural(new(392, false, true, false)), "Dynamic drop accepted");
    Assert(!NaturalSpotRules.IsNatural(new(392, false, false, true)), "Temporary tree/pool loot accepted");
    Assert(!NaturalSpotRules.IsNatural(new(813201, false, false, false)), "Our own Long Stick became a new source");
});
Check("Natural spot keys tolerate tiny position noise and distinguish nearby stick locations", () => {
    var a = NaturalSpotRules.Key(12.501f, 1.201f, -20.501f);
    Assert(a == NaturalSpotRules.Key(12.502f, 1.202f, -20.502f), "Tiny load jitter made a new spot");
    Assert(a != NaturalSpotRules.Key(13.501f, 1.201f, -20.501f), "Separate stick positions collapsed");
});
Check("Invalid world positions never create spawn records", () => {
    foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }) {
        Assert(NaturalSpotRules.Key(value, 1, 2) == null && NaturalSpotRules.Key(1, value, 2) == null &&
            NaturalSpotRules.Key(1, 2, value) == null, "Invalid position accepted");
    }
});
Check("Supplemental spot chance is stable, zero-disabled and complete at a hundred percent", () => {
    var key = NaturalSpotRules.Key(-531.2f, 123.7f, 824.2f)!;
    Assert(!NaturalSpotRules.Selected(key, 0) && NaturalSpotRules.Selected(key, 100), "Spot percent endpoints failed");
    Assert(NaturalSpotRules.Selected(key, 15) == NaturalSpotRules.Selected(string.Concat(key), 15), "Spot identity rerolled");
});
Check("Position-key formatting is independent of the current language", () => {
    var before = System.Globalization.CultureInfo.CurrentCulture;
    try {
        var expected = NaturalSpotRules.Key(-1.25f, 0.5f, 78.3f);
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        Assert(NaturalSpotRules.Key(-1.25f, 0.5f, 78.3f) == expected, "Language changed location keys");
    } finally { System.Globalization.CultureInfo.CurrentCulture = before; }
});
Check("Only the declared dedicated scatter pool is a natural pool source", () => {
    Assert(GroundScatterRules.IsDedicatedPool("Greebles", "Greebles", "PickUps"), "Dedicated natural pool rejected");
    Assert(GroundScatterRules.IsDedicatedPool("greebles", "GREEBLES", "PickUps"), "Pool casing affected provenance");
    Assert(!GroundScatterRules.IsDedicatedPool("PickUps", "PickUps", "pickups"), "Generic player/console/tree pickup pool accepted");
    Assert(!GroundScatterRules.IsDedicatedPool("OtherPool", "Greebles", "PickUps"), "Unrelated runtime pool accepted");
    foreach (var empty in new string?[] { null, "", " " })
        Assert(!GroundScatterRules.IsDedicatedPool(empty, "Greebles", "PickUps") &&
            !GroundScatterRules.IsDedicatedPool("Greebles", empty, "PickUps") &&
            !GroundScatterRules.IsDedicatedPool("Greebles", "Greebles", empty), "Missing pool provenance accepted");
});
Check("Natural ground scatter accepts loose normal sticks and excludes other item types", () => {
    Assert(GroundScatterRules.IsLooseStick(392, true, false, false, false), "Loose normal stick rejected");
    foreach (var item in new[] { -1, 0, 78, 380, 474, 813201 })
        Assert(!GroundScatterRules.IsLooseStick(item, true, false, false, false), "Other item or custom extra became a source");
});
Check("Ground-scatter source guards exclude collected items, inactive templates, vegetation and storage", () => {
    Assert(!GroundScatterRules.IsLooseStick(392, true, true, false, false), "Collected source accepted");
    Assert(!GroundScatterRules.IsLooseStick(392, false, false, false, false), "Inactive template accepted");
    Assert(!GroundScatterRules.IsLooseStick(392, true, false, true, false), "Tree or breakable loot accepted");
    Assert(!GroundScatterRules.IsLooseStick(392, true, false, false, true), "Storage content accepted");
});
Check("Kelvin reserves a distinct nonzero type within the native encoding mask", () => {
    var used = new[] { 0, 1, 2, 3, 31, 30 };
    var chosen = KelvinRules.ReserveTypeId(used, 31);
    Assert(chosen == 4 && chosen > 0 && chosen <= 31 && !used.Contains(chosen), "AI type overwrites a native type or leaves an unnecessary ID gap");
    Assert(chosen == KelvinRules.ReserveTypeId(used.Reverse(), 31), "Registration order changes the saved AI type ID");
});
Check("Kelvin refuses an exhausted item type encoding rather than overwriting another type", () => {
    var refused = false;
    try { KelvinRules.ReserveTypeId(Enumerable.Range(0, 16), 15); }
    catch (InvalidOperationException) { refused = true; }
    Assert(refused, "A full type registry was overwritten");
});
Check("Unsupported Kelvin type encodings are rejected", () => {
    foreach (var mask in new[] { -1, 0, 256, int.MaxValue }) {
        var refused = false;
        try { KelvinRules.ReserveTypeId(Array.Empty<int>(), mask); }
        catch (InvalidOperationException) { refused = true; }
        Assert(refused, "An invalid encoding mask was accepted");
    }
});
Check("The new Kelvin order gathers Long Sticks rather than converting ordinary sticks", () => {
    Assert(KelvinRules.CanGather(813201, true, false), "Long Stick resource rejected");
    foreach (var id in new[] { 392, 78, 474, 380, -1 })
        Assert(!KelvinRules.CanGather(id, true, false), "Other resource accepted by the Long Stick order");
});
Check("Kelvin's ordinary order leaves Long Sticks for the separate order", () => {
    Assert(KelvinRules.CanGather(392, false, false), "Normal stick collection changed");
    Assert(!KelvinRules.CanGather(813201, false, false), "Ordinary order consumes custom Long Sticks");
});
Check("The Long Stick order does not empty storage to refill another holder", () => {
    Assert(!KelvinRules.CanGather(813201, true, true), "Stored Long Stick can be scavenged");
    Assert(KelvinRules.CanGather(392, false, true), "Unrelated vanilla stored-item policy changed");
});
Check("Stick controller queries use the Long Stick inventory only during its own order", () => {
    Assert(KelvinRules.ForOrder("stick", "stick", true) == KelvinRules.ItemType, "Long order uses normal inventory");
    Assert(KelvinRules.ForOrder("stick", "stick", false) == "stick", "Original stick order changed");
    Assert(KelvinRules.ForOrder("log", "stick", true) == "log", "Log query redirected");
    Assert(KelvinRules.ForOrder(KelvinRules.ItemType, "stick", true) == KelvinRules.ItemType, "Explicit custom query changed");
});
Check("Carrying reuses stick animation names while inventory identity remains separate", () => {
    Assert(KelvinRules.ForCarryAnimation(KelvinRules.ItemType, "stick") == "stick", "Native carry animation not reused");
    Assert(KelvinRules.ForCarryAnimation("log", "stick") == "log", "Other carry type changed");
    Assert(KelvinRules.ItemType != "stick" && KelvinRules.ItemType != Rules.LongStick.ToString(), "AI type aliases vanilla sticks or game item IDs");
});
Check("Kelvin type IDs remain contiguous for the game's dense native tables", () => {
    for (var nativeCount = 1; nativeCount <= 31; nativeCount++)
        Assert(KelvinRules.ReserveTypeId(Enumerable.Range(0, nativeCount), 31) == nativeCount,
            "Appended type does not occupy the next native table slot");
});
Console.WriteLine($"{count} offline source, probability, Kelvin routing, geometry and storage scenarios passed. These do not execute Unity or the game.");
record Drop(string Kind, int Count, bool Linked = false);
sealed class TestPrefab
{
    public TestPrefab(string name, bool sharedPickup = false) { Name = name; SharedPickup = sharedPickup; }
    public string Name { get; }
    public bool SharedPickup { get; }
    public List<TestPrefab> References { get; } = new();
}
