namespace LongStickSpear.Core;

public readonly record struct Ingredient(int ItemId, int Count, bool Reusable);
public readonly record struct TreeDropSource(int ItemId, string Name, bool SpawnEnabled);

public static class Rules
{
    // Stable across releases: this ID is written into ordinary inventory save data.
    public const int LongStick = 813201;
    public const int Stick = 392;
    public const int Knife = 380;
    public const int Spear = 474;
    public const int Tape = 419;
    public const int Capacity = 10;
    public const string ItemName = "Long Stick";
    public const string RecipeName = "LongStickSpear_Recipe_v1";

    public static readonly Ingredient[] SpearIngredients = {
        new(LongStick, 1, false), new(Knife, 1, true)
    };

    public enum Vegetation { Unknown, SmallTree, Tree, Bush, Other, Plant }

    public static bool ShouldReplace(Vegetation category, string name, bool stump,
        bool construction, bool treeContext, bool dropsSticks)
    {
        return dropsSticks && IsVegetationSource(category, name, stump, construction, treeContext);
    }

    public static bool IsVegetationSource(Vegetation category, string name, bool stump,
        bool construction, bool treeContext)
    {
        if (stump || construction) return false;
        var n = (name ?? "").Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();
        if (n.Contains("stump")) return false;
        // Native cutting/falling-tree provenance is positive evidence. Only
        // normal stick loot is replaced, so logs from the same tree stay logs.
        if (treeContext) return true;
        // Small woody vegetation is spread across all of these native LOD
        // categories. Its actual stick loot matters, not a tree-only name.
        if (category is Vegetation.SmallTree or Vegetation.Tree or Vegetation.Bush or Vegetation.Plant) return true;
        if (category != Vegetation.Unknown) return false;
        // With no locator, retain a natural-vegetation name requirement so a
        // generic breakable prop that drops sticks is not mistaken for a plant.
        return n.Contains("sapling") || n.Contains("smalltree") || n.Contains("slimtree") || n.Contains("thintree") ||
            n.Contains("seedling") || n.Contains("bush") || n.Contains("shrub") || n.Contains("brush");
    }

    public static bool CanConvertTreePickup(int itemId, bool treeSource, bool liveInstance) =>
        itemId == Stick && treeSource && liveInstance;

    public static bool CanEditFallingTree(IEnumerable<TreeDropSource> sources)
    {
        var hasSticks = false;
        foreach (var source in sources)
        {
            if (!source.SpawnEnabled) continue;
            if (IsLogSource(source.ItemId, source.Name)) return false;
            if (source.ItemId == Stick) hasSticks = true;
        }
        return hasSticks;
    }

    public static bool IsLogSource(int itemId, string name) => itemId == 78 ||
        (name ?? "").StartsWith("Log", StringComparison.OrdinalIgnoreCase);

    public static bool CanReplaceDirectStick(int itemId, int count) => itemId == Stick && count > 0;

    public static int LongestAxis(float x, float y, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || x < 0 || y < 0 || z < 0)
            throw new ArgumentOutOfRangeException("Invalid model extent");
        return x >= y && x >= z ? 0 : y >= z ? 1 : 2;
    }

    public static List<T> ReplaceStickReferences<T>(IEnumerable<T> drops, Func<T, bool> isStick,
        Func<T, T> makeLongStick, out int replaced)
    {
        var result = new List<T>(); replaced = 0;
        foreach (var drop in drops)
        {
            if (!isStick(drop)) { result.Add(drop); continue; }
            replaced++; result.Add(makeLongStick(drop));
        }
        return result;
    }

    public static List<T>? ReplaceSelectedStick<T>(IReadOnlyList<T> options, int index,
        Func<T, bool> isStick, Func<T, T> makeLongStick)
    {
        if (index < 0 || index >= options.Count || !isStick(options[index])) return null;
        var result = new List<T>(options);
        result[index] = makeLongStick(options[index]);
        return result;
    }
}
