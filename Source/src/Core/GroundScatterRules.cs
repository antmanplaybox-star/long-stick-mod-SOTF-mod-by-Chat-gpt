namespace LongStickSpear.Core;

public static class GroundScatterRules
{
    public static bool IsDedicatedPool(string? actual, string? scatter, string? genericPickups) =>
        !string.IsNullOrWhiteSpace(actual) && !string.IsNullOrWhiteSpace(scatter) &&
        !string.IsNullOrWhiteSpace(genericPickups) &&
        string.Equals(actual, scatter, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(actual, genericPickups, StringComparison.OrdinalIgnoreCase);

    public static bool IsLooseStick(int itemId, bool active, bool collected, bool vegetation, bool storage) =>
        itemId == Rules.Stick && active && !collected && !vegetation && !storage;
}
