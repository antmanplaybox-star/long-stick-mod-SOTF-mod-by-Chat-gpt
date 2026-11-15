namespace LongStickSpear.Core;

public static class StorageRules
{
    // Separate namespaces from the Long Stick inventory item. IDs stay stable
    // because native structure saves reference them.
    public const int RecipeId = 813220;
    public const int BuiltPrefabId = 813220;
    public const int GhostPrefabId = 813219;
    public const int Capacity = 20;
    public const string Name = "Long Stick Storage";
    public const string PageKey = "LONG_STICK_SPEAR_STORAGE_PAGE";

    public static int SourceScore(string name)
    {
        var n = new string((name ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant().Replace("recipe", "").Replace("built", "");
        if (n is "storagesticks" or "stickstorage" or "stickholder") return 100;
        return n.Contains("stick") && (n.Contains("storage") || n.Contains("holder")) ? 50 : 0;
    }

    public static int SlotCapacity(int index, int slots)
    {
        if (slots <= 0 || index < 0 || index >= slots) throw new ArgumentOutOfRangeException(nameof(slots));
        return Capacity / slots + (index < Capacity % slots ? 1 : 0);
    }

    public static bool CanDeposit(int itemId, int count, int stored, int capacity) =>
        itemId == Rules.LongStick && count > 0 && stored >= 0 && capacity >= 0 && stored <= capacity && count <= capacity - stored;
}
