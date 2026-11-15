# Long Stick Spear — v0.4.6

A Long Stick item, alternate spear recipe and matching book storage for **Sons of the Forest**, **RedLoader 0.8.6**, and **single player**.

## Reload crash fix in 0.4.6

Corrects Kelvin's custom Long Stick inventory-type registration. With the supplied loader, the generic list insertion passes the boxed native struct in the wrong format. This can leave an invalid item-name pointer in the game's shared AI table, then crash when loading another save after returning to the main menu. The new registration uses the native boxed-object overload and verifies the stored type ID, group and name immediately after insertion.

**Fully exit the game before replacing both Long Stick Spear files, then restart.** Returning to the main menu cannot repair a table already populated by an older DLL. Keep **Golf Cart Battery Upgrade v0.1.1** if using that mod; this update replaces Long Stick Spear, not the battery mod. Existing item IDs, Kelvin order/type IDs, holder capacity, crafting recipes and natural-spawn settings are retained.

The supplied fatal stack ends in `KelvinLongSticks.EnsureTypeIds` while converting a native string during `BeforeLoadSave`. The latest session confirms the battery mod's earlier callback fix was active. This release addresses the concrete AI registration defect behind that crash path. Compilation, existing offline scenarios and native API audits pass; repeating the main-menu reload in the game is still required to verify the outcome.

## Spear crafting animation retained from 0.4.5

- The **Long Stick + knife** recipe now selects the original spear recipe's animation state instead of `CraftHerbMix`.
- The animation name is read from the existing spear recipe in the game's recipe database, preserving its native reveal/completion timing. Its crafting completion and jingle audio events are copied to the separate custom recipe as well.
- Ingredients remain **one Long Stick + one reusable knife**. The original **two normal sticks + duct tape + knife** recipe remains available. No ordinary sticks or tape are added to the alternate recipe.
- If the original animation is not ready, setup waits/retries through the existing crafting setup path instead of registering an empty animation or selecting the herb-mixing fallback.

The HD book picture and LONG STICK STORAGE title, Kelvin gathering/filling, the FPS fix, the **10%** default natural chance and **20**-item holder capacity are retained. The 0.4.6 fix changes only Kelvin's native type insertion and its immediate validation, plus the version label/metadata.

## Asking Kelvin to fill Long Stick holders

1. Fully close the game and replace both mod files. Loading another save in the same process does not unload the old version's hooks.
2. Load a single-player save and check gameplay FPS before issuing a Kelvin task. Then build a **Long Stick Storage (20)** holder, or use an existing one.
3. Leave some reachable loose Long Sticks nearby. For a controlled test, `spawnitem 813201 5` creates five Long Sticks.
4. Talk to Kelvin and select **Get → Long Sticks → Fill Holders**, then give him the note.
5. Check that he collects Long Sticks, carries the longer models and deposits them in Long Stick Storage. The holder must stop at 20.
6. Test the original **Get → Sticks → Fill Holders** with an ordinary holder as a separate check.
7. Test cancellation while he carries Long Sticks and save/reload with a partially filled holder and an active task.

This task gathers loose Long Sticks. If none are reachable, Kelvin cannot fill a holder. Normal AI pathfinding, fatigue and rest behavior still apply. The added material menu exposes the fill-holder destination; vanilla material menus retain their existing options.

The custom AI type is registered within the native inventory encoding, and the notebook order is appended to a private order-data copy so existing order indices stay intact. Save/load identity is covered by metadata and routing checks, but has not been exercised in a running game here.

## Renewable extras retained from 0.3.3

Natural Long Stick extras are renewable after reloading. Collected locations are remembered only for the current session, preventing immediate refills while you stand at the same spot. That temporary history is cleared before each save load and when leaving the world.

Collection history previously saved by 0.3.2 is ignored. The old save key remains readable for compatibility, but new saves contain no collected location keys. No save editing or new game is required.

Extra-item placement, tree drops, spear recipes and storage capacity are retained. The default chance is now 10%.

## Natural world placement

Natural loose-stick locations come from dedicated environmental-scatter membership, with authored world-item and static virtual-pickup records retained as fallbacks. The old terrain-definition replacement remains removed. This uses the separate-spawn option requested for this feature:

- By default, **10% of recognized natural stick spots get an additional Long Stick nearby**. The original normal sticks remain.
- At **100%**, every eligible, uncollected spot is selected for an extra Long Stick. Look beside the normal sticks; the normal sticks do not change.
- Selection is stable for the same recorded location. Changing the chance does not reroll spots.
- Sources must be loose normal sticks belonging to the environmental scatter system, or qualifying authored/static records. Console items, player drops, storage drops and tree loot do not qualify.
- The authored-record fallback requires its original/default state and a non-runtime origin. Static virtual records must not be dynamic or scheduled to despawn. These fallback flags are not required for a pickup proven to belong to the dedicated natural-scatter pool/zone.
- Extras use the existing double-length Long Stick pickup. This feature does not modify vanilla pickup IDs, shared prefabs, world save records or tree bodies.
- Collected extra locations are remembered only until you reload or leave the world. Eligible natural spots can produce Long Sticks again in the next session.
- Nearby spawning and cleanup run on the game thread after inventory setup, spread over frames.

The chance slider retains its explicit **0–100** range and **10** default. Existing saved settings, including 100, are retained. The confirmed tree-drop, crafting, appearance and storage behavior is retained. The custom pickup now also has Kelvin AI configuration.

## Install and test

1. Close the game completely.
2. Extract the ZIP beside `SonsOfTheForest.exe`, keeping the `Mods` folder and replacing both mod files.
3. Launch with RedLoader and load a copied single-player save.
4. Set **Natural stick spots: extra Long Stick chance (%)** to **100**.
5. Walk near uncollected, loose sticks already lying in the world. Allow a few seconds for discovery. Look for an additional **Long Stick** beside them; **ordinary sticks remain**. Aim at a normal stick long enough to show its pickup prompt, so the log captures its source if spawning still fails.
6. Collect an extra while leaving its normal source stick in place. It should stay collected during this session. Save and reload; a new Long Stick should become available at that eligible spot again.
7. Set the chance to **10** for normal play.

The extra starts about 0.28 metres beside its source, is placed near the ground and can settle under physics. Only nearby extras are instantiated: within 65 metres, up to 48 live extras and two spawns per frame. Uncollected extras beyond 110 metres are removed and may return when approached. A full nearby budget can delay other selected extras.

Changing the chance takes effect during the next bounded update; this setting does not require a reload. Zero removes this feature's uncollected extras, while keeping Long Sticks already in inventory or storage.

Installed files:

```text
Mods/LongStickSpear.dll
Mods/LongStickSpear/manifest.json
```

Keep only one LongStickSpear DLL in `Mods`. No asset bundles or dependency DLLs are included.

## Retained tree behavior

- Stick-producing small/slim trees retain their original falling body, physics, hierarchy, joints and collision behavior. Only normal-stick sources in their native collision-spawn loot lists are replaced.
- A tree with enabled log-producing entries is excluded entirely, including mixed log/stick trees. Normal large trees and their logs remain unchanged.
- Direct small-plant drops replace only positive normal-stick entries. Zero-count placeholders and falling-body references remain native.
- Disabling vegetation replacement restores tracked original loot lists.

The older whole-tree cloning, global impact/collision and generic pooled-pickup conversions remain absent.

## Long Sticks and crafting

The Long Stick is a separate material using the native stick model stretched to twice its length while retaining its thickness. Its stack holds **10**. It is not a held weapon or construction stick.

| Recipe | Ingredients | Result |
|---|---|---|
| Original game recipe | 2 normal sticks + 1 duct tape + knife | 1 crafted spear |
| Added recipe | 1 Long Stick + knife | 1 crafted spear |

The knife is reusable. The added recipe consumes only the Long Stick and uses the original spear recipe's crafting animation and audio. The original recipe remains available. Add the ingredients to the crafting mat and use the cog. Free a spear slot if the game's spear carry limit is reached.

## Long Stick Storage

The additional final book page contains **Long Stick Storage (20)**. It uses the native stick holder's frame, ghost, placement settings and construction materials, accepts only Long Sticks and has a total capacity of **20**. Deposit/retrieval controls and native structure saving remain available. Stored sticks are longer; the frame keeps its normal dimensions. Ordinary stick storage retains its original behavior.

## IDs

| Item or structure | ID |
|---|---|
| Long Stick | 813201 |
| Normal Stick | 392 |
| Combat Knife | 380 |
| Crafted Spear | 474 |
| Duct Tape | 419 |
| Long Stick Storage recipe / built prefab | 813220 |
| Long Stick Storage ghost | 813219 |

Move a little between these dropped-model comparison commands:

```text
spawnitem 813201 1
spawnitem 392 1
```

Console-spawned normal sticks are not natural-world source locations.

## Settings

Open **Pause → MODS → Long Stick Spear → Settings**.

- **Natural stick spots: extra Long Stick chance (%):** default 10, range 0–100. Adds a Long Stick beside the selected percentage of eligible natural spots; original sticks remain.
- **Stick-producing vegetation drops long sticks:** enabled by default; excludes log-producing trees.
- **Long-stick inventory offset X / Z:** moves the new inventory stack relative to normal sticks; reload after changing.
- **Detailed tree-drop logging:** enables additional source diagnostics.

## Diagnostics

Kelvin entries:

- **KELVIN TYPE:** AI type ID, native stick group/name and the native lookup probe result.
- **KELVIN PICKUPS:** separate held/dropped prefabs and native carrying limit.
- **KELVIN MENU / ORDER:** new menu index and an issued Long Stick order.
- **KELVIN SOURCE:** sampled pickup identity, current task, stored-item exclusion and decision.
- **KELVIN HOLDER:** native holder search result and chosen target.
- **KELVIN HOOKS / WORK:** deferred gathering-hook activation and loose-item task availability.
- **KELVIN TRACK:** direct source registration or a sampled reason for exclusion.
- **KELVIN VALIDATE:** native pickup-validation result and whether the stimulus targets its local pickup.
- **KELVIN PICKUP / DEPOSIT:** actual post-pickup carried counts and native holder-add results.
- **KELVIN TASK:** up to twelve task samples, ten seconds apart, recording tracked sources, carried Long Sticks and the holder's item/count.
- **PERFORMANCE:** average FPS, whether gathering hooks are active, current Long Stick task state and tracked loose pickups. Three reports after loading and after a new Long Stick order.

If the notebook option is missing or Kelvin does not gather/deposit correctly, include `_RedLoader/Latest.log` after giving him one test order.

**BOOK ART** records completed image dimensions, the loaded source mip, quality limit and filtering. A waiting message means the original page texture is still in use. If the illustration remains pixelated, include this entry from the new log. If the book was already open during preparation, close and reopen it to refresh its displayed material.

**BOOK TITLE** confirms that `LONG STICK STORAGE` was applied to the native top/bottom blueprint caption. This is separate from the book page heading. It reports once per game process when the custom page is displayed.

Natural-world entries:

- **WORLD STICK SETUP:** effective chance and separate-extra approach.
- **GROUND SCATTER POOL:** actual/declared environmental pool, generic pickup pool, dedicated-source decision and live count.
- **GROUND SCATTER SOURCE:** bounded samples of real pool/zone objects, pickup IDs and exclusions.
- **NATURAL LOOK:** the hierarchy, pool and dynamic flag of a normal stick you look at. This is diagnostic only.
- **SAVED STICK SOURCE:** samples accepted authored scene-item records.
- **PICKUP SOURCE:** samples virtual-pickup origin flags.
- **NATURAL SPOT:** recognized locations and their percentage selection.
- **NATURAL LONG STICK:** confirms a custom pickup was instantiated.
- **NATURAL STATUS:** environmental pool/live entries, zone callbacks/instances, recognized scatter-stick rows, fallback registry activity, recognized spots and active extras.

Status reports are bounded. Callback counts alone do not prove visible loot. If no Long Sticks appear beside natural sticks at 100%, include the new `_RedLoader/Latest.log`; these counters distinguish source discovery from spawning.

Existing **TREE LOOT**, **LOG TREE**, **DROP TABLE**, **VISUAL**, **CRAFT**, **STORAGE** and **BOOK** diagnostics remain. Include `ErrorLog.log` as well if the game crashes.

The recipe-registration message logs `sourceRecipe` and the selected `animation`, allowing a new log to confirm which native spear animation was reused. Existing CRAFT reveal/complete messages track the custom recipe's native events.

## Validation and limits

- Release build: **zero warnings and zero errors** against the supplied game and loader assemblies.
- **94 offline scenarios pass**, including the 10% default/boundary/distribution, Kelvin type-ID isolation, order/carry routing, normal versus Long Stick gathering, stored-item exclusion, prior ground-source rules, geometry and storage limits.
- Static audit checks **69 available native hook mappings**. New guards verify that the native spear animation is read before configuring the custom recipe, the HerbMix fallback is absent, both native audio events are copied and an empty animation is rejected. Existing caption, HD-artwork, Kelvin, tree, natural-source, crafting-cache and storage checks remain.
- The previous 0.4.4 DLL fails the new original-spear-animation regression guard as expected.
- HD artwork, book captions, Kelvin, natural spawning, tree drops, item appearance and holder source files are unchanged. Inventory/crafting layout code and ingredient rules are unchanged; recipe setup changes its animation/audio selection and log.

These checks do not execute Unity, native method bodies, IL2CPP detours, navigation, animations or save/reload. **The original spear animation on the alternate recipe requires an in-game check.** Natural spawning, the FPS fix, Kelvin gathering/filling, HD artwork and the title were confirmed in preceding releases.

Single player only. New extras still require an eligible natural source location and the configured chance; collection no longer permanently blacklists any extra locations. The normal stick icon is retained; the item is named Long Stick. An item ID conflict prevents registration instead of overwriting another item. Keep the mod installed for saves containing Long Sticks or custom holders/blueprints; remove those items/structures or restore a pre-mod save before uninstalling.

Created by **Ibrahim**, with development assistance from **ChatGPT (OpenAI)**.

## Source and building

Source, offline checks and static audit are included under `Source`. Build with .NET SDK 6 or a compatible later SDK against your own game/loader files:

```powershell
dotnet build Source/src/LongStickSpear.csproj -c Release -p:GameDir="D:/SteamLibrary/steamapps/common/Sons Of The Forest"
dotnet run --project Source/tests/Tests.csproj -c Release
dotnet run --project Source/audit/Audit.csproj -c Release -p:LoaderDir="D:/SteamLibrary/steamapps/common/Sons Of The Forest/_Redloader/net6" -- "Source/src/bin/Release/net6.0/LongStickSpear.dll" "D:/SteamLibrary/steamapps/common/Sons Of The Forest/_Redloader/Game" "Source/src/manifest.json"
```

`GameAssembliesDir`, `LoaderDir` and `UserLoaderDir` can also be supplied individually. Game/loader assemblies are not redistributed.
