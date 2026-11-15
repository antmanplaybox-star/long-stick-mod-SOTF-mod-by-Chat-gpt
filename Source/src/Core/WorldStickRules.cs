namespace LongStickSpear.Core;

public readonly record struct WorldStickContext(int ItemId, bool MapPickup,
    bool OriginalState, bool HasOverride, int Lod);

public static class WorldStickRules
{
    public const float DefaultChance = 10f;

    public static bool IsEligible(WorldStickContext context) => context.MapPickup &&
        context.ItemId == Rules.Stick && context.OriginalState && !context.HasOverride &&
        context.Lod is >= 0 and <= 2;

    public static float Chance(float percent) => float.IsFinite(percent) ? Math.Clamp(percent, 0f, 100f) : 0f;

    public static uint LocationHash(ReadOnlySpan<byte> id)
    {
        unchecked
        {
            uint hash = 2166136261u ^ 0x4c53574du;
            foreach (var value in id) hash = (hash ^ value) * 16777619u;
            hash ^= hash >> 16; hash *= 0x7feb352du;
            hash ^= hash >> 15; hash *= 0x846ca68bu;
            return hash ^ (hash >> 16);
        }
    }

    public static bool Selected(uint locationHash, float chance) => locationHash % 10000u < Chance(chance) * 100f;

    public static bool ShouldReplace(WorldStickContext context, ReadOnlySpan<byte> id, float chance) =>
        IsEligible(context) && id.Length > 0 && Selected(LocationHash(id), chance);
}
