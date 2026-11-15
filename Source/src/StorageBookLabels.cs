using LongStickSpear.Core;
using Sons.Crafting.Structures;
using Sons.Weapon;
using SonsSdk;

namespace LongStickSpear;

internal static class StorageBookLabels
{
    internal const string Caption = "LONG STICK STORAGE";
    internal const string RecipeKey = "LONG_STICK_SPEAR_STORAGE_RECIPE";
    private static bool _reported;

    internal static void Register(StructureRecipe recipe)
    {
        LocalizationTools.BlueprintBookTable.AddEntry(RecipeKey, Caption);
        LocalizationTools.BlueprintBookTable.AddEntry(StorageRules.PageKey, Caption);
        recipe._localizationId = RecipeKey;
    }

    // Reuse the book's own caption after native page/localization updates.
    // Its existing font, size, alignment and transform remain authoritative.
    internal static void Refresh(BlueprintBookController book)
    {
        var pages = book._pages?._pages;
        var index = book._currentPageIndex;
        if (pages == null || index < 0 || index >= pages.Count) return;
        var page = pages[index];
        if (page == null) return;
        var isTop = page._topRecipe?.Id == StorageRules.RecipeId;
        var isBottom = page._bottomRecipe?.Id == StorageRules.RecipeId;
        if (!isTop && !isBottom) return;
        var caption = isTop ? book._topBlueprintName : book._bottomBlueprintName;
        if (caption == null || !caption) return;
        caption.text = Caption;
        caption.enabled = true;
        caption.gameObject.SetActive(true);
        if (!_reported)
        {
            _reported = true;
            Mod.Info($"BOOK TITLE: '{Caption}' applied to native {(isTop ? "top" : "bottom")} blueprint caption, page={index}.");
        }
    }
}
