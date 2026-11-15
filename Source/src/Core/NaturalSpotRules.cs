using System.Globalization;
using System.Text;

namespace LongStickSpear.Core;

public readonly record struct NaturalPickupOrigin(int ItemId, bool RuntimeCreated, bool Dynamic, bool Despawns);

public static class NaturalSpotRules
{
    public static bool IsAuthoredWorldItem(int itemId, bool runtimeCreated, bool originalState) =>
        itemId == Rules.Stick && !runtimeCreated && originalState;

    public static bool IsNatural(NaturalPickupOrigin source) => source.ItemId == Rules.Stick &&
        !source.RuntimeCreated && !source.Dynamic && !source.Despawns;

    public static string? Key(float x, float y, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return null;
        return string.Join(":", new[] { x, y, z }.Select(v =>
            Math.Round((double)v * 20, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)));
    }

    public static uint Hash(string key) => WorldStickRules.LocationHash(Encoding.UTF8.GetBytes(key));
    public static bool Selected(string key, float percent) => WorldStickRules.Selected(Hash(key), percent);
}
