# Validation — Long Stick Spear v0.4.6

## Reported failure

After a normal first load and battery installation with Golf Cart Battery Upgrade v0.1.1, returning to the main menu and loading a save again caused a fatal CLR error. The supplied fatal stack passes through Il2CppStringToManaged, KelvinLongSticks.EnsureTypeIds, KelvinLongSticks.Begin and Mod.BeforeLoadSave. The latest session confirms Battery Upgrade v0.1.1 was active and contains none of its earlier native weak save-callback errors.

The user's test without the battery mod did not reproduce the issue. That is relevant isolation evidence, but does not establish the battery capacity code as the sole cause. The current fatal stack and supplied generated API bodies expose a separate, concrete native struct marshalling defect in the Long Stick AI registration. The battery mod is left at v0.1.1 for this fix.

## Native boundary defect

InventoryTypeList.TypeInfo is a native value type, represented in the supplied interop assembly by a CLR class deriving from Il2CppSystem.ValueType. Its fields are StringId, TypeId and GroupId. The generated List<T>.Add wrapper selects its argument representation using CLR Type.IsValueType; this wrapper class takes the object branch. It passes the boxed object pointer directly to il2cpp_runtime_invoke, without unboxing the TypeInfo payload. A native by-value struct argument consequently receives the boxed header instead of the intended string/type/group data. A copied class pointer can then be misread as StringId, matching the fatal native string-conversion path on a subsequent scan.

The correction uses the supplied System_Collections_IList_Add(Il2CppSystem.Object) wrapper. Its native signature expects a boxed object; the game's IList implementation performs the cast/unboxing within IL2CPP. The returned append index is checked, then the stored TypeId, GroupId and StringId are read back and verified before marking registration ready. The existing free-ID selection, duplicate checks, native lookup/fallback, legacy carried-item mapping and session cleanup remain.

No new native hooks, imports, unsafe pointer operations, table replacement or global list clearing are added. A complete game restart is necessary when replacing the old DLL: a table corrupted earlier in the same process cannot safely be repaired by catching a managed exception or merely returning to the main menu.

## Validation

- Release build: zero warnings and errors against the supplied game, RedLoader and SonsSdk assemblies.
- All 94 existing offline source, probability, Kelvin routing, geometry and storage scenarios pass. The pure rules are unchanged.
- All 69 native hook mappings and the existing tree, crafting, storage, natural-spawn, UI and Kelvin audits pass.
- The new native-boundary audit verifies the TypeInfo representation and generated generic Add marshalling, rejects the unsafe insertion, requires the boxed-object signature, and checks the post-insertion readback of ID/group/name.
- The published v0.4.5 assembly must fail the new unsafe-native-struct insertion guard. This negative check verifies that the audit detects the actual previous path.
- Optional log checks confirm that the supplied fatal stack names the repaired Kelvin setup path and that the latest session ran with Battery Upgrade v0.1.1 without the removed callback errors. Raw user logs and personal data are excluded.
- Packaging compares production sources with the published v0.4.5 archive. Only KelvinLongSticks.cs, the Mod.cs version label and project/manifest versions change. Crafting, appearance, trees, natural spawning, book artwork, storage and the remaining Kelvin implementation are preserved.
- Cargo Cart v0.1.16, Golf Cart Battery Upgrade v0.1.1 and Skull Storage v0.1.0 sources/binaries are verified against their published archives and are not shipped in this standalone update.
- Matching manifests, one mod DLL, ZIP CRC and file SHA-256 sums are checked.

These checks inspect managed code and the supplied native interop signatures; they do not execute Unity, native AI code or a real save reload. They establish that the identified bad marshalling route is removed, not that every possible cause of missing world assets or a native crash has been reproduced or ruled out.

## Live test

1. Fully close the game. Replace Mods/LongStickSpear.dll and Mods/LongStickSpear/manifest.json with this release, then restart. Keep Battery Upgrade v0.1.1 installed for the reported combined test.
2. Load a single-player save, check the world normally, and test a battery upgrade. The Long Stick registration line should say registration=boxed IList and native payload verified.
3. Quit to the main menu, load the save again and repeat more than once. Confirm normal trees/vegetation/rocks and successful loading.
4. Ask Kelvin to Get → Long Sticks → Fill Holders with reachable loose Long Sticks and a holder. Also check his normal stick order.
5. Save/reload while Kelvin carries Long Sticks and verify his custom task/item mapping. Check the Long Stick + knife spear recipe and a saved Long Stick holder.
6. If the crash recurs, supply the newest Latest and ErrorLog from that attempt. The new log marker distinguishes this registration fix from the prior DLL.
