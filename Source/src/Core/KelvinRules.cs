namespace LongStickSpear.Core;

public static class KelvinRules
{
    public const string ItemType = "LongStickSpear_LongStick";
    public const string OrderKey = "LONG_STICK_SPEAR_KELVIN_GET";
    public const string OrderLabel = "LONG STICKS";

    public static int ReserveTypeId(IEnumerable<int> existing, int mask)
    {
        if (mask is < 1 or > 255) throw new InvalidOperationException("Unsupported Kelvin inventory encoding.");
        var used = existing.ToHashSet();
        // Native consumers can use the type ID as a table index. Prefer the
        // first available slot rather than the largest encodable number.
        for (var id = 1; id <= mask; id++) if (!used.Contains(id)) return id;
        throw new InvalidOperationException("No free Kelvin item type ID remains.");
    }

    public static string ForOrder(string requested, string stickType, bool longOrder) =>
        longOrder && requested == stickType ? ItemType : requested;
    public static string ForCarryAnimation(string requested, string stickType) =>
        requested == ItemType ? stickType : requested;
    public static bool CanGather(int itemId, bool longOrder, bool insideStorage) =>
        longOrder ? itemId == Rules.LongStick && !insideStorage : itemId != Rules.LongStick;
}
