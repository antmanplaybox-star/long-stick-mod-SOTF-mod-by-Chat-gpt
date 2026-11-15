using RedLoader;
using RedLoader.Preferences;
using SonsSdk.Attributes;
using LongStickSpear.Core;

namespace LongStickSpear;

[SettingsUiMode(SettingsUiMode.ESettingsUiMode.OptIn)]
public static class Config
{
    [SettingsUiInclude] public static ConfigEntry<bool> ReplaceTreeDrops { get; private set; } = null!;
    [SettingsUiInclude] public static ConfigEntry<float> InventoryOffsetX { get; private set; } = null!;
    [SettingsUiInclude] public static ConfigEntry<float> InventoryOffsetZ { get; private set; } = null!;
    [SettingsUiInclude] public static ConfigEntry<bool> DebugLogging { get; private set; } = null!;
    [SettingsUiInclude] public static ConfigEntry<float> NaturalStickChance { get; private set; } = null!;

    public static void Init()
    {
        var category = ConfigSystem.CreateFileCategory("LongStickSpear", "Long Stick Spear", "LongStickSpear.cfg");
        ReplaceTreeDrops = category.CreateEntry("ReplaceTreeDrops", true, "Stick-producing vegetation drops long sticks",
            "Replace normal stick loot from natural trees, seedlings, shrubs and falling-tree branches with Long Sticks. Turning this off keeps existing Long Sticks and both spear recipes available.");
        NaturalStickChance = category.CreateEntry("NaturalStickChance", WorldStickRules.DefaultChance, "Natural stick spots: extra Long Stick chance (%)",
            "Adds one Long Stick near this percentage of natural ground-stick spots; original sticks remain. Default 10, range 0 to 100. Player drops, console spawns, storage and tree loot are not sources. Collected extras can return after reloading; no permanent depletion. 0 disables extras.");
        NaturalStickChance.SetRange(0f, 100f);
        InventoryOffsetX = category.CreateEntry("InventoryOffsetX", .26f, "Long-stick inventory offset X",
            "Position of the new stack relative to the normal sticks, in inventory metres. Takes effect the next time a save is loaded.");
        InventoryOffsetZ = category.CreateEntry("InventoryOffsetZ", .14f, "Long-stick inventory offset Z",
            "Adjust this if another inventory mod overlaps the long-stick stack. Takes effect the next time a save is loaded.");
        DebugLogging = category.CreateEntry("DebugLogging", false, "Detailed tree-drop logging",
            "Log the source category and drop decision when a breakable object drops loot.");
    }
}
