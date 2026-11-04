# Map Editor Lite: Session Change Log

Environment: SPT 4.1.6, client plugin targets net471 (BepInEx), server mod targets net10.0.
Status: all changes built successfully (server 0 errors, client 0 errors). In-game results are in the "Test results" section below.

---

## Test results (in-game, reported by the project helper)

| # | Feature | Result | Notes |
|---|---------|--------|-------|
| 1 | Gravity OFF preview | **PASS** | Preview sits exactly on the gizmo. |
| 2 | Gravity ON preview (downward) | **PASS** | Preview lands on the world floor or object below. |
| 3 | Gizmo drag fix | **PASS (minor)** | A small jump may still be seen but is much less noticeable. Accepted as good enough. |
| 4 | Loot Spawn Groups | **PASS** | Tested over 3 raids: the first two raids picked the same spawn, the third picked the other one. Exactly one spawn appeared each time. |
| 5 | Locked containers / safes | **PASS** | Two copies of the same safe, one Shut and one Locked, with a random key set. The Locked one is locked and the key opens it. The Shut one stays unlocked. |
| 6 | Old packs (before these changes) | **PASS** | An old pack loads and behaves as before. |
| 7 | Export and reload | **PASS** | Group fields and lock state survive saving and reloading. |
| 8 | Gravity ON search DOWN (10 m) | **PASS** | |
| 9 | Gravity ON search UP (10 m), first version | **FAIL** | A single upward raycast did not find surfaces above the marker. Reworked, see section 8 (follow-up fix). |
| 10 | Gravity ON search UP, reworked | **PASS** | Tested going below the game world floor, then tested to make sure the gizmo and preview object didn't tweak out on shelvs and small spaces. |

Not part of this work: the web tool in `Tool\`. It was not built or tested, and it is not being modified by this team beyond the optional fields added to `types.ts`.

Still to retest: marker under a floor (preview should land on top of that floor), marker between two shelves (the closer one should win), marker with no surface within 10 m (preview should hide, and reappear when dragged back).

---

## Summary

| # | Change | Area | Files |
|---|--------|------|-------|
| 1 | Gravity-OFF preview now sits at the exact marker position | Client | `LootPreviewSpawner.cs` |
| 2 | Loose-loot preview offset no longer rotated by marker rotation | Client | `LootPreviewSpawner.cs` |
| 3 | Gravity-ON ground ray starts 0.1 m above marker (was 3 m) | Client | `LootPreviewSpawner.cs` |
| 4 | Live preview follows the marker during drag | Client | `LootPreviewSpawner.cs` |
| 5 | Gizmo drag plane is fixed for the whole drag (feedback-loop fix) | Client | `MapEditorController.cs` |
| 6 | Containers/safes get an explicit "Initial Door State" (Shut/Locked) | Client | `CustomEditorUI.cs`, `RuntimeInteractiveObjectSpawner.cs` |
| 7 | Loot Spawn Groups (pick exactly one loose-loot spawn per group) | Client, Server, Tool | `MarkerTypes.cs`, `CustomEditorUI.cs`, `PackData.cs`, `LootTransformer.cs`, `WttSpawnConverter.cs`, `types.ts` |
| 8 | Gravity-ON preview searches 10 m up AND down for the nearest surface; hides if none | Client | `LootPreviewSpawner.cs` |
| 10 | UI audit fixes: group selection/Clear/Delete Group, compact Groups rows, GameObjects pagination, scrollable details pane (not yet tested in-game) | Client | `CustomEditorUI.cs`, `MarkerManager.cs` |
| 9 | Custom-item previews no longer show the Doge placeholder (native bundle tokens, empty-key filter, Doge rejection) | Client | `LootPreviewSpawner.cs` |

---

## 1–4. Preview and spawn-position fixes (`Client\LootPreviewSpawner.cs`)

### Problems found
- `SpawnAtMarker` always used `GetGroundPosition(pos) ?? pos`, a downward raycast starting 3 m above the marker. It could hit a roof, rack, car or ledge, so the preview appeared at the wrong height even when "Use Gravity" was off. The saved marker position and the server output were always exact; only the client preview was wrong.
- `UpdateForMarker` rotated a world-space offset by the marker rotation.
- With gravity ON, releasing the mouse after a drag respawned the preview, re-raycast from marker + 3 m, and could snap to something above the marker.

### Changes
- `SpawnAtMarker`: `marker.useGravity ? (GetPreviewGroundPosition(pos) ?? pos) : pos`. Gravity OFF means exact marker X/Y/Z, with no raycast.
- New private static `GetPreviewGroundPosition(Vector3)`: `Physics.Raycast(markerPos + Vector3.up * 0.1f, Vector3.down, out hit, 100f)`. A roof or rack above the marker is no longer selected as the ground. Edge case: a marker placed below a surface may not find that surface (accepted).
- `UpdateForMarker` (loose loot):
  - (Later superseded by section 8: the ground search is now 10 m up and down.)
- Gravity ON: re-raycasts with `GetPreviewGroundPosition` on each update and sets `meta.offset = ground - marker`. If there is no hit, the previous offset is kept. This keeps the preview following the marker live during a drag.
  - Gravity OFF: offset forced to `Vector3.zero`.
  - Zone previews keep `markerPos + markerRot * offset`.
- The marker's own position is never modified by ground correction. Only the preview object moves. `MarkerManager.SnapSelectedToGround` is unchanged.

---

## 5. Gizmo drag feedback-loop fix (`Client\MapEditorController.cs`)

### Root cause
`GetGizmoDragPlane` built a camera-facing plane through the marker's *current* position, and `ApplyGizmoDrag` rebuilt it every frame while the start hit point stayed fixed. Each move shifted the plane, which changed the intersection, which caused another move. The effect was amplified, especially with tilted gizmos, and showed up as large jumps (often vertical).

### Changes
- `ApplyGizmoDrag` no longer calls `GetGizmoDragPlane` each frame. It uses the plane stored at drag start (`_gizmoDragPlane`, created in `RecordGizmoDragStart`) and returns early if the plane normal is `Vector3.zero` (no active drag).
- The mouse-up handler resets it with `_gizmoDragPlane = new Plane();`, so the next drag creates a fresh plane and start intersection.
- Axis behavior, plane handles, gizmo orientation and intentional vertical (Y gizmo) movement are unchanged. No Y lock, clamping or scaling was added.
- The preview still follows the marker live during the drag (see 4).

---

## 6. Container / safe locking (`Client\CustomEditorUI.cs`, `Client\RuntimeInteractiveObjectSpawner.cs`)

### Root cause
Doors set both `KeyId` and `DoorState = Locked`. The container branch applied `KeyId` but never set a lock state, so a container with a key was never locked. The server's `PackData.KeyId` is never read; locking happens on the client in `RuntimeInteractiveObjectSpawner`.

### Design
Explicit lock state, as with doors. A non-empty `KeyId` does **not** imply locked.

### Changes
- `CustomEditorUI.BuildInteractiveObject` (Container branch):
  - New "Initial Door State" dropdown with **Shut** and **Locked**.
  - A live amber warning when the state is Locked and the Key Template Id is empty.
  - New helper `IsContainerLockedWithoutKey`.
  - A key with state Shut is allowed and the container stays unlocked.
- `RuntimeInteractiveObjectSpawner.InitializeContainerLootCoroutine`: right after `lootable.Init(controller)`, when `initialDoorState == "Locked"`, it sets `DoorState`, `InitialDoorState`, `FallbackState` and `CurrentAngle = GetAngle(Locked)`. It logs a warning if `keyId` is empty.
- Init order (decompiled from the install): `Init()` does **not** reset `DoorState`. `OnEnable` copies `DoorState` into `InitialDoorState`, which is why the lock is applied after `Init()`. The key match compares `key.Template.KeyId == KeyId`.
- Pack JSON: reuses the existing `initialDoorState` string field. The saved `keyId` format and `PackData` are unchanged.
- Result: Key + Locked gives `DoorState = Locked` and `KeyId = supplied id`. Key + Shut stays unlocked.

---

## 7. Loot Spawn Groups (loose loot only)

Pick exactly **one** loose-loot spawn per group, per raid.

### Concept
- The existing editor **Group** field is untouched. It is editor hierarchy and lock organisation only, and the server never reads it.
- The new Loot Group is a separate gameplay concept with its own fields.
- Per-spawn Loot Group ids may repeat; that means "same gameplay group". Each spawn keeps its own unique id.

### New JSON fields (on loose-loot spawns only; all optional)
```json
"lootGroupEnabled": true,
"lootGroupId": "Nolan_Safe",
"lootGroupWeight": 1.0
```
Old packs omit these, so they load as ungrouped (enabled = false, weight = 1.0) and behave exactly as before.

### How SPT does the pick (verified by decompiling SPT 4.1.6 `LocationLootGenerator`)
`ChooseForcedPositions` groups forced spawnpoints by `Template.Id`. If a group has more than one entry, it draws exactly one, weighted by `Spawnpoint.Probability`. Entries with different ids are independent. No custom random system was added.

### Changes
- `Server\PackData.cs` (`LooseLootSpawn`): added `LootGroupEnabled`, `LootGroupId`, `LootGroupWeight` (default 1.0), and the computed `IsInLootGroup` (enabled, non-blank id, weight > 0).
- `Server\LootTransformer.cs` (the `LooseLoot.AddTransformer`):
  - Grouped spawns go through the quest filter (per profile) and the item filter first. Failing members are dropped.
  - Each eligible member is added to **`SpawnpointsForced`** with:
    - `LocationId` = the spawn's own unique id
    - `Template.Id` = `"MEL_LG_" + trimmed groupId` (shared)
    - `Probability` = weight
    - `Template.IsAlwaysSpawn` = true
  - Grouped spawns ignore `SpawnChance` and the spawn's own `Forced` flag.
  - Ungrouped spawns take the original code path, unchanged.
  - The `MEL_LG_` prefix avoids collisions with real spawn ids and SPT's Christmas-id filter.
  - A duplicate `LocationId` guard prevents double adds if the transformer runs again.
  - Temporary logging per raid and location: `[MEL] Loot group '<id>' on <location>: N eligible member(s); SPT will pick one.`
- `Server\WttSpawnConverter.cs`: `WriteForcedSpawns` now filters `s.Forced && !s.IsInLootGroup`, so grouped spawns are not also exported to WTT files.
- `Client\MarkerTypes.cs`: same three fields on the client `LooseLootSpawn`.
- `Client\CustomEditorUI.cs`: new `BuildLootGroupControls`, called from `BuildLooseLootSpawn`:
  - **Part of Loot Group** toggle.
  - When on:
    - **Loot Group** id field, with an amber warning if empty (an empty id is treated as ungrouped).
    - **Selection Weight**; values of 0 or below are ignored.
    - Member count, with a warning if the group has only one member.
    - A note that Spawn Chance and Forced are ignored and only the first eligible item spawns.
    - Buttons to reuse existing group ids on the map.
  - No separate Loot Groups tab. The list is derived from the markers (V1 needs no persistent group storage).
- `Tool\src\types.ts`: optional `lootGroupEnabled`, `lootGroupId`, `lootGroupWeight` on `LooseLootSpawn`, so web tool exports keep them.

### Behavior notes and risks
- **Grouped spawns spawn only their first eligible item.** This is how SPT's forced path works (it ignores `ItemDistribution`).
- Quest conditions are honored per profile before the pick.
- If the picked item hits a `LootMaxSpawnLimits` cap, the group spawns nothing that raid (no re-draw).
- A single-member group always spawns.
- If every member is filtered out, nothing spawns for that group.
- The same group id across two packs on the same map merges into one group.

### Suggested test plan
1. 4 spawns, same group, weight 1: exactly one appears per raid, varying across raids.
2. Weights 10/1/1/1: the first is chosen far more often.
3. Two groups (4 and 3 members): one pick from each.
4. Ungrouped loot: unchanged.
5. Same Loot Group in different editor Groups: still one group.
6. Same editor Group, different Loot Groups: separate groups.

---

## 8. Bidirectional ground detection for gravity-ON previews (`Client\LootPreviewSpawner.cs`)

This supersedes the single downward ray described in section 3. Only the preview's display position is affected.

### Behavior (Use Gravity ON)
- Searches straight **down** (up to 10 m) and straight **up** (up to 10 m) from the marker/gizmo.
- The first collider hit in each direction is used, so shelves remain individual surfaces.
- Both directions hit: the closer one wins. Only one hits: that surface is used. Neither hits: the preview is **hidden**. **// DOENST WORK**
- No horizontal search. The default raycast layer handling is unchanged.
- The marker/gizmo position is never modified. Gizmo drag and the fixed drag plane are untouched.

### Changes
- `GetPreviewGroundPosition`:
  - Down ray starts 0.1 m above the marker (so a surface at the marker's own height is still found) with length 10.1 m.
  - Up ray starts at the marker with length 10 m.
  - Distances are compared with the 0.1 m start offset removed from the down hit.
  - Returns `null` when neither ray hits.
- `SpawnAtMarker`: if there is no surface, the preview is spawned and then hidden via the new helper `SetMarkerPreviewsVisible(markerId, false)`.
- `UpdateForMarker` (loose loot): gravity ON re-queries every update and shows or hides the preview depending on whether a surface was found, so it hides when dragged away from all surfaces and reappears when dragged back. Gravity OFF sets the offset to zero, places the preview exactly at the marker and makes sure it is visible.
- `LoadRealPreviewCoroutine`: when the real item model replaces the temporary fallback, it keeps the fallback's hidden state, so a hidden preview does not reappear.

### Follow-up fix: upward search did not work in testing
- Cause (likely): Unity raycasts ignore back faces, so a ray fired upward passes through single-sided floor/ceiling mesh colliders seen from underneath.
- Fix: the upward search is now a downward scan (`Physics.RaycastNonAlloc`, buffer of 32) from 10 m above the marker. It picks the lowest hit that is more than 0.1 m above the marker. The preview therefore sits on the TOP face of the nearest surface above the marker. The downward search is unchanged, and the closer of the two still wins.
- Limitation: if the marker is inside a solid collider, that collider is not detected by the scan. If several colliders are above the marker, the lowest top face wins.
- Re-test needed in-game (marker under a floor).

### Unchanged
Gravity OFF (exact marker position, no raycast), gizmo drag system, drag plane, loot spawning, loot groups, and `MarkerManager.SnapSelectedToGround`.

### Caveats
- The up ray starts exactly at the marker position; if the gizmo has its own collider on the default raycast layers it could be hit. No marker colliders were found in the client code, but this was not verified in-game.
- Original upward-ray version failed in testing; the reworked version is awaiting a re-test.

---

## 9. Custom-item preview showed the Doge placeholder (`Client\LootPreviewSpawner.cs`)

**Status: fix confirmed working in-game by the owner.** Example: "Goshan cargo manifests" (base of the custom "Nolans Shipping Documents") previewed as the Doge in MEL but worked in Ultimate Loot Editor (ULE). MEL logged `assetBundle | Bundle key is null or empty` and `objectPool | bone SpecialSlot1/2/3 not found in GameObject Doge`.

### Cause
- MEL built its own bundle list from `template.Prefab` and `template.UsePrefab`, and added keys even when their `path` was empty. ULE instead asks EFT for the item's bundles with `GetAllBundleTokens()`.
- When the real model's pool is missing, `CreateLootPrefab` returns EFT's Doge placeholder. MEL accepted any non-null result as success, so the Doge replaced the cyan fallback sphere and the retries stopped.

### Changes
- `PreloadBundles` builds the runtime item, calls `item.GetAllBundleTokens()` and `await EasyAssetsExtensions.LoadBundles(tokens)`, as ULE does. A failure in this step is logged at debug level and does not stop the preview.
- The manual `LoadBundlesAndCreatePools` call is kept (MEL still uses the synchronous `CreateLootPrefab`), but `CollectResourceKeys` now skips keys with an empty `path`.
- `TrySpawnRealPreview` rejects a result whose name contains "Doge". The object is returned to the pool (falling back to `Destroy`) and the existing 5-frame retry continues, so the cyan sphere is kept if the model never loads.
- If all retries fail, a warning is logged: `Could not load real preview model for template <tpl>; keeping fallback sphere.`
- Bundle-token lifetime: on success the tokens are stored in the new `PreviewLootMarker.bundleTokens` field and released in `DestroyPreview`. On failure or when the fallback was already removed, they are released immediately.

### Credit: Ultimate Loot Editor (ULE)
The bundle-loading approach in this fix comes from **Ultimate Loot Editor (ULE) by GoatBoy**, licensed MIT (`Copyright (c) 2026 GoatBoy`). Please credit GoatBoy and ULE in release notes/README.
- Taken from ULE (`PresetPreviewBridge.CreateWorldPreviewObjectAsync` and `WorldPreviewObject`, in `src\UltimateLootEditor.Client\PresetPreviewBridge.cs`): resolving an item's bundles with `item.GetAllBundleTokens()`, loading them with `EFT.EasyAssetsExtensions.LoadBundles(tokens)`, keeping the tokens for the preview's lifetime, and releasing them reflectively through the token's `Release` method (`TryReleaseBundleTokens`). The pool-return cleanup with `AssetPoolObject.ReturnToPool` (`ReturnPreviewPrefab`) follows the same pattern.
- Written by us, not from ULE: the Doge-placeholder rejection, the empty-key filter, the retry and fallback-sphere handling, and the warning log.
- MEL still uses `CreateLootPrefab` and does not use ULE's `CreateCleanLootPrefabAsync`.
- The code was adapted, not copied wholesale. If larger ULE portions are ever copied, the MIT license requires including its copyright and permission notice.

### Unchanged
Gravity, ground detection, gizmo movement and drag planes, loot groups, server spawning and pack formats. Previews that already worked (e.g. Nolan's Safe Key) use the same path as before.

### Build
`dotnet build -c Release -p:SPTPath=E:\SPT_TRADER_QUEST_MAKER_4.1.X` in `Client\`: 0 errors.

---

## 10. UI audit follow-ups (`Client\CustomEditorUI.cs`, `Client\MarkerManager.cs`)

Source audit of the in-game UI, then small scoped fixes. All built in Release: 0 errors, 60 warnings (the same pre-existing warnings, mostly CS0649). **None of these have been tested in-game yet.**

### 10a. Group selection and Clear refresh
- `MarkerManager.SelectGroup` now clears the primary selection (`_selected = null`) before filling `SelectedIds`. Before, a stale primary marker stayed selected.
- The Groups Clear button now calls `RequestRefresh()`, as Assign does.

### 10b. Groups tab rework
- Clicking a group row only fills the Group input and highlights the row (`_selectedGroupName`). It no longer selects members or changes the inspector.
- Rows are compact: name plus member count. The redundant Select button was removed.
- Assign and Clear still act on the currently selected markers.

### 10c. Clear and Delete Group
- Cause of "Clear does nothing": group names are derived from marker `group` strings, so a group stays listed while any member keeps the assignment. Clear also did nothing silently when no pack marker was selected, and vanilla markers are skipped.
- `ClearGroupAssignmentFromSelection` now reports a status message when no editable pack marker is selected. It keeps the selection and refreshes the hierarchy, Groups list and inspector.
- New Delete Group button with a confirmation panel. `MarkerManager.DeleteGroupAssignments` clears the group from all members (including locked ones) and removes it from `lockedGroups`. It never deletes markers. Cancel changes nothing.

### 10d. GameObjects pagination
- 150 rows per page, with `<` / `>` buttons, "Page X / Y" and "Showing a–b of N" (thousands separators).
- Prev is disabled on the first page and Next on the last page. Zero results show "Showing 0 of 0".
- The page resets to 1 only when the search text changes. It is clamped when the result count shrinks. Changing pages never changes the selected object.
- The name filter and the scan cache are unchanged. The old "refine filter" text was removed.

### 10e. GameObjects details pane scrolling
- The right column now has a fixed action row (Set Source / Cancel) and a scroll view holding the object name and details text. Long paths and component lists are reachable. The results list scrolls independently.

### Not changed
Gravity, rotation gizmos, preview loading, loot spawn group generation, locked-container logic, and scene-outline performance (`MarkerRenderer.cs`).

### Known remaining audit items (not fixed)
- Scene-outline cost scales with the whole scene cache.
- Fixed-size panels can clip at small windows or a large UI scale.
- Groups has no member browsing.

### In-game checks still needed
Group row click (no selection change), Assign/Clear/Delete Group with confirmation, pagination across a large scan (reset on filter change, clamp, selection kept across pages), details pane scrolling with the wheel and scrollbar, and layout at the 180 px panel height.

---

## Build and deployment notes
- Server build output goes to `SPT_Runtime\user\mods\MapEditorLite\` and should contain only `MapEditorLite.Server.dll` plus the data folders (`cache`, `db`, `editor`, `exports`, `imports`, `lang`, `packs`, `prefabs`, `spawns`). Extra files from the build (`.deps.json`, `.pdb`, `staticwebassets.endpoints.json`) are deleted after each build.
- Client build output goes to `BepInEx\plugins\` and should contain only `MapEditorLite.Client.dll` (the `.pdb` and `System.*.dll` are removed).
- The client build needs the SPT install path: `-p:SPTPath="<path to SPT install>"`.
- The new DLLs have not been copied into any SPT install by the assistant; the owner tested with their own installed copies.

## Not changed
Editor Group system, doors, zones, bots, server `PackData` for containers, spawn positioning for markers, Snap To Ground, saved JSON format for existing fields, and `.csproj` files.

## Notes
- The README says SPT 4.0.13, but the code targets 4.1.x.
- Test results are in the "Test results" section at the top. Only the reworked upward search (section 8) still needs an in-game re-test.

---

## Investigation entry — runtime decals and texture painting (2026-10-11)

### Scope
This investigation compares four ways of adding custom artwork to world objects in MEL for SPT 4.1.x: surface decals, UV texture painting, projective PNG overlays, and external texture export/import. The review is limited to the existing client plugin and runtime code paths already inspected in [Client/Plugin.cs](Client/Plugin.cs), [Client/MapEditorController.cs](Client/MapEditorController.cs), [Client/MarkerRenderer.cs](Client/MarkerRenderer.cs), [Client/LootPreviewSpawner.cs](Client/LootPreviewSpawner.cs), [Client/CustomEditorUI.cs](Client/CustomEditorUI.cs), and [Client/RuntimeCutVolumeSpawner.cs](Client/RuntimeCutVolumeSpawner.cs). No implementation work or project-file changes were made.

### Confirmed MEL capabilities relevant to artwork
- MEL already has direct access to Unity `GameObject`, `Transform`, `Collider`, and `Renderer` information and uses them for scene-object picking and outline rendering in [Client/MarkerRenderer.cs](Client/MarkerRenderer.cs).
- MEL already creates runtime `Material` instances with `Shader.Find(...)` and assigns them to renderers in [Client/LootPreviewSpawner.cs](Client/LootPreviewSpawner.cs), which is relevant for decal or projected-image overlays.
- MEL already modifies runtime meshes in [Client/RuntimeCutVolumeSpawner.cs](Client/RuntimeCutVolumeSpawner.cs) by replacing `MeshFilter.sharedMesh` and updating `MeshCollider` state; this confirms that direct mesh mutation is part of the current runtime pattern.
- No inspected code shows a built-in decal system, texture-paint editor, or asset-projection workflow. The current features are marker-based edit tooling, scene-object selection, and runtime preview/state creation rather than a custom rendered-art system.

### 1) Surface decals for blood spatters, graffiti, and warning signs

#### Practicality with current MEL
- Likely the simplest first proof of concept.
- The current MEL architecture already selects world objects, creates overlays/preview visuals, and swaps materials on `Renderer` objects. That is enough to attach a decal-like object or projected sprite without a full mesh/UV editor.
- It does not require a map-wide UV conversion layer and is the least invasive fit to the existing marker/UI structure.

#### Required Unity capabilities
- A world-space decal quad or billboard placed on a hit surface, plus a transparent shader or a custom decal shader.
- `RaycastHit`/surface normal or `Bounds`-based placement, `Transform.position/rotation`, and `Renderer.material` assignment.
- Preferably a shader that supports alpha, sort order, and optional normal-based fade; a basic transparent `Unlit/Transparent` or custom `Standard` material is enough for a first pass.

#### Curved objects
- Works well on curved surfaces when decal is a small world-space plane or projected sprite that sits on the target normal.
- For fuel tanks, this is easier than full UV painting because the decal can follow the local hit point and normal rather than depending on mesh UV layout.

#### Multiple materials, UV seams, shared materials
- Usually easy to manage: the decal is a separate object or layer, not a modification to the base material.
- Shared materials are less risky because the decal can be a child object with its own material instance.
- This avoids UV seam and atlas issues entirely.

#### Performance / memory cost
- Low to moderate. One quad or billboard per decal is inexpensive.
- Memory is mostly the decal texture asset and per-decal material/transform state.
- Best for small numbers of local marks; not good for painting large areas across every object.

#### Save/restore after raid or restart
- Store decal data in MEL map JSON (position, rotation, scale, target object reference or world position, texture ID or bundle path, alpha, fade settings).
- Recreate decals on load by resolving the target object and rebuilding the overlay objects.
- This fits the existing save model in [Client/JsonStorage.cs](Client/JsonStorage.cs) and the current map-data schema in [Client/MarkerTypes.cs](Client/MarkerTypes.cs), although the data model would need new fields for decal entries.

### 2) UV-based texture painting using raycasts and texture coordinates

#### Practicality with current MEL
- Technically possible, but not a simple MEL extension.
- The codebase already mutates meshes in `RuntimeCutVolumeSpawner`, which shows mesh writing is possible in principle, but the inspected project does not include a UV-paint pipeline.
- This is the least practical first feature because it requires accurate UV mapping and texture manipulation for each target mesh.

#### Required Unity capabilities
- Access to `MeshFilter.sharedMesh` / `Mesh` vertices, UVs, triangles, and potentially a writable `Texture2D`.
- A painting algorithm that maps a raycast hit into a local UV coordinate and writes into a runtime texture.
- Material cloning for each target object because shared materials would otherwise affect every instance using that material.
- For a realistic solution: a texture atlas or per-object render texture, plus a custom shader that samples the painted texture.

#### Curved objects
- Works only if the target mesh has stable UVs and if the projection hit can be mapped to a valid texel area.
- Fuel tanks and many curved prop meshes often use cylindrical or repeated UVs; these can be sensible, but they also create distortion, seam issues, and repeated wrap-around artifacts.

#### Multiple materials, UV seams, and shared materials
- This is the main risk.
- Multiple materials mean multiple texture channels; shared materials mean one painted texture can affect all objects using the same asset.
- UV seams and repeated textures create visible joins or stretched results.
- A robust solution would need per-object material cloning, per-material texture replacement, and careful seam handling.

#### Performance / memory cost
- High. A writable texture, mesh sampling, per-frame paint updates, and duplicate material instances can become expensive.
- Storing a full set of per-object painted textures adds memory and save/load complexity.

#### Save/restore after raid or restart
- Possible, but requires saving painted textures or generating a deterministic texture atlas for each object.
- This is much heavier than decals and risks duplicated assets or memory spikes after reload.

### 3) Imported transparent PNG images projected onto object surfaces

#### Practicality with current MEL
- Plausible as a second-step prototype after surface decals.
- MEL already creates custom materials and can apply them to renderers in preview code. That is enough to bootstrap a projected-image overlay workflow.
- However, this is not a built-in feature in the inspected code; it would require a new projection shader or a custom material setup.

#### Required Unity capabilities
- A texture (`Texture2D`/`RenderTexture`), a custom material/shader with UV or projector-space sampling, and a world-space projection matrix.
- Custom shader input such as a projector position, orientation, and scale, or a simpler plane-based approximation using object-local coordinates.
- Either a world-space quad + transparent shader or a projector-based shader that samples a texture based on hit position.

#### Curved objects
- Works better than flat UV painting when the image is treated as a projected overlay rather than baked into the mesh.
- For a fuel tank, a projected image anchored to the hit normal can look acceptable even when the object is curved, especially if the effect is limited to decals, warning labels, graffiti, or directional signage.

#### Multiple materials, UV seams, and shared materials
- Better than UV painting because it does not require changing the underlying mesh UV layout.
- It still needs material cloning on the target object if the object uses a shared material, or a separate overlay object with its own shader.
- Good for single-surface artwork and labels; poor for large repeated texturing or full-surface wraps.

#### Performance / memory cost
- Moderate. One texture per overlay plus some shader math is manageable.
- More expensive than plain decals if many projected images are active at once.

#### Save/restore after raid or restart
- Straightforward if the texture is saved as a PNG and reloaded with a map-data entry containing target object id, transform, and texture path.
- Re-building the scene on load is easier than restoring a full UV painted mesh because the effect is stored as overlay metadata rather than mesh data.

### 4) Exporting textures for external editing and reimporting them

#### Practicality with current MEL
- This is a viable workflow idea, but not itself a runtime painting feature.
- It is likely the least invasive for an art pipeline, because it treats the target texture as an asset rather than an interactive edit surface.
- The current code does not show a texture-export pipeline, so this would be a new feature rather than a natural extension of the existing MEL client plugin.

#### Required Unity capabilities
- Read the target `Texture2D` from a `Renderer` or material, encode it to PNG, save it to a mod folder, open it externally, then reimport and assign the edited texture back.
- This is feasible with Unity APIs (`Texture2D.EncodeToPNG`, `AssetDatabase` in editor, and runtime texture handling if the game supports it).
- In a live runtime environment, reassigning textures is possible if the texture is not read-only, but the project does not show an existing asset pipeline for this.

#### Curved objects
- It depends on the original UV mapping. For a curved object, the result is only as good as the object’s UV layout.
- If the object uses a repeating or cylindrical map, external edits may create obvious seams or distortion.

#### Multiple materials, UV seams, and shared materials
- It inherits the same difficulties as UV painting, but with the benefit of a more controlled artist workflow.
- Shared materials can affect many objects, so material cloning remains required.

#### Performance / memory cost
- Moderate for a single export/import cycle, but not a real-time runtime technique.
- Memory cost is mostly the texture asset and serialized PNG, which is manageable.

#### Save/restore after raid or restart
- Best for persistence because PNGs are easy to store and reuse.
- The main challenge is writing the metadata needed to map the edited texture back to the intended object and material path.

### Recommendation
The simplest technique for a first proof of concept is surface decals, not UV painting. It aligns best with MEL’s actual architecture: world-object selection, runtime material assignment, transform-based placement, and saved map metadata. It is the least dependent on UV layout, handles curved objects better than baking to mesh UVs, and avoids the risk of shared-material and seam corruption across multiple objects.

This is not the same as saying a full realistic custom-art system is already supported. The code currently shows no built-in decal pipeline or mesh painting workflow; it only confirms access to world objects, renderers, materials, and mesh mutation. A decal prototype would therefore be the most defensible next step, while UV painting and full texture export/import are more complex follow-up investigations.

### Uncertainties and recommended next steps for Serenity
- Confirm whether the live SPT 4.1.x runtime exposes a supported shader or projector-based decal approach without rewriting game materials.
- Confirm whether the target objects are usually single-material, shared-material, or atlas-based, since that changes the practicality of any projected overlay or UV edit.
- Confirm whether a map-driven overlay system can be saved/restored with MEL’s existing JSON storage without editing the game’s source assets.
- Next investigation target: inspect the exact runtime material/shader assumptions for the relevant object classes and determine whether a world-space decal or projected texture can be assigned cleanly to a selected `Renderer` without breaking shared materials or the game’s default asset state.

### Conclusion
For a first MEL feature spike, surface decals or projected transparent PNG overlays are the best candidates. UV painting and external texture export/import are more ambitious and higher-risk because they depend on mesh UV integrity, material ownership, and a larger asset-management layer. The most plausible near-term extension is a decal-like overlay system built on the current scene-object selection and material assignment capabilities already present in MEL.

---

## Investigation entry — in-game world object selection for a "Draw on Object" mode (2026-10-11)

Investigation only. No source code or game assets were modified. Statements below are tagged **[Confirmed]** (read in MEL code) or **[Assumption]** (Unity/game behaviour not verified in MEL code or in-game).

### 1. Does MEL already have a raycast / object-selection system?
**[Confirmed] Yes, three separate ones**, all driven from `MapEditorController.HandleMouseInput()` (`Client\MapEditorController.cs`):
1. **Marker picking**: `MarkerRenderer.PickFromScreenPosition(Camera, Vector2, float)` (`Client\MarkerRenderer.cs`). Raycasts with layer mask `1 << 2` (`QueryTriggerInteraction.Collide`) against MEL's own marker visuals (identified by the `MarkerVisual` component, `Client\MarkerVisual.cs`), with a screen-distance fallback (40 px). It does not touch world geometry.
2. **World GameObject picking by ray**: `CustomEditorUI.PickSceneObjectAtMouse(bool silent)` (private), exposed as `TryPickSourceSceneObject()` and `GetSceneObjectHover()` (`Client\CustomEditorUI.cs`). It uses `Physics.RaycastAll(cam.ScreenPointToRay(Input.mousePosition), 100f)`, takes the closest hit that passes `IsValidPick`, and returns a `GameObject`. This is the closest existing match to the requested "click a fuel tank" action.
3. **World GameObject picking by cached bounds**: `MarkerRenderer.SetSceneObjects(...)` / `PickSceneObject(Ray, float, Func<GameObject,bool>)`, built from the scanned object cache (`CustomEditorUI.ScanSceneObjects()`, which keeps objects that have a `MeshFilter` or `SkinnedMeshRenderer` mesh; `LODGroup` objects are treated as one unit). It uses `Bounds.IntersectRay` only, not mesh hits, and is used for outlines and hover highlight.

Existing consumers of the world picker (state flags in `CustomEditorUI`, handled in `HandleMouseInput`): `IsPickingSource` (set a marker's `sourceObjectName`/`sourceObjectPosition`), `IsPickingScatter`, `IsPickingSceneGO` (select in the GameObjects tab via `SelectSceneGO`).

### 2. How the object under the cursor is detected
**[Confirmed]** `Camera.main` + `ScreenPointToRay(Input.mousePosition)` + `Physics.RaycastAll`. `HandleMouseInput` returns early when the free-cam cursor is locked (`_freeCam && _freeCamCursorLocked`) or when `IsMouseOverUI()` is true. The free-cam camera is tagged `MainCamera` (`MapEditorController.cs`, ~line 1218), so `Camera.main` resolves to it while free-cam is active.
**[Confirmed limitation]** The picker returns only the `GameObject`. `RaycastHit.point`, `normal`, `collider`, `triangleIndex` and `textureCoord` are discarded. A drawing mode needs all of them (at minimum point, normal, collider).
**[Confirmed]** `PickSceneObjectAtMouse` has no layer mask (`Physics.DefaultRaycastLayers`, default trigger behaviour) and a fixed 100 m range. `LootPreviewSpawner` (ground search, lines ~79-84) shows the pattern `Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore`, which the new mode should reuse to skip trigger volumes.
**[Assumption]** `RaycastHit.transform` returns the Rigidbody's transform when the collider belongs to a Rigidbody (documented Unity behaviour). `PickSceneObjectAtMouse` uses `hit.transform.gameObject`, so on rigidbody props it may return the root instead of the collider/mesh object. A drawing mode should use `hit.collider` and then resolve renderers explicitly. Not verified in-game.

### 3. Telling real world geometry apart from markers, UI, and unrelated colliders
**[Confirmed] Already in place**
- `CustomEditorUI.IsEditorObject(GameObject)` rejects anything whose ancestor name starts with `MLE_`, is named `Player`, or has a `PreviewLootMarker`. Marker visuals live under `MLE_VisualRoot` / `MLE_PreviewRoot` (`MapEditorController.Awake`).
- `ShouldIgnorePick` applies the user-editable `ScenePickFilterConfig` (`enabled`, default filters `SPAWNS`, `LOD`, `BornPositions`), loaded from `BepInEx\config\MapEditorLite\scenePickFilters.json`, by substring match on the hierarchy path.
- `IsMouseOverUI()` / `CustomEditorUI.IsScreenPointOverEditorUI(...)` block clicks through editor panels.
- Marker visuals use layer 2 for picking (`MarkerRenderer`), separate from the world picker.
**[Confirmed] Gaps**
- Trigger colliders are not excluded by `PickSceneObjectAtMouse` (no `QueryTriggerInteraction` argument). MEL creates trigger primitives (`CreatePrimitiveVisual` sets `isTrigger = true`) and extract triggers on the `Triggers` layer (`RuntimeExtractZoneSpawner`).
- No check that the hit has a visible `Renderer`; invisible collision proxies would be accepted. The scan cache (`HasMesh`) does use a mesh test, but the raycast picker does not.
- Players, bots and dropped loot are only partly excluded (name `Player` and `PreviewLootMarker`). Other characters, bots, and loose items are **not** verified as excluded.
- Hidden/"soft-removed" objects (`RemovedObjectHelper.SoftRemove`) are not tested for during picking.
**Proposed filter chain (not built)**: RaycastAll with `QueryTriggerInteraction.Ignore`, then skip `IsEditorObject`, skip `ScenePickFilterConfig` matches, require an enabled `Renderer` in the hit object or its parents/children, skip `Player`/`BotOwner`/`LootItem`-type components (component list to be confirmed from decompiled assemblies), then take the nearest remaining hit.

### 4. Retrieving transform, mesh, renderer, and materials
**[Confirmed]** Transform, path (`CustomEditorUI.GetFullPath`), `WorldInteractiveObject` id/state, `LootableContainer`, `StationaryWeapon` are already read in `FormatSceneGODetails` (`CustomEditorUI.cs`). `MeshFilter.sharedMesh` / `SkinnedMeshRenderer.sharedMesh` are read in `HasMesh`/`HasMeshInChildren` and in `RuntimeCutVolumeSpawner.ApplyCut`. `Renderer` and `Collider` bounds are read in `MarkerRenderer.CalculateBounds`.
**[Confirmed] Not present**: MEL does not read `Renderer.sharedMaterials`/`materials`, shader names, textures, or UVs of scene objects. The only `Material` code is for MEL's own previews, gizmos, and wireframes. Material/texture inspection of picked world objects is new work.
**[Confirmed]** `RuntimeCutVolumeSpawner.CutMeshSafe` checks `mesh.isReadable` and falls back to GPU readback (`CutMeshGpuReadback`) when CPU access fails. This is direct evidence that **some game meshes are not CPU-readable**, so any feature depending on mesh data (UVs, triangle lookups, `MeshCollider.textureCoord`) must handle unreadable meshes. This shows up as an uncertainty for UV-based approaches, much less so for a plain world-space decal.
**[Assumption]** Fuel tanks, walls and containers may have several renderers, several submeshes/materials, LOD levels, and separate collider-only children. Which of these apply to specific objects is not verified and needs an in-game dump.

### 5. Identifying the same object after reloading the map
**[Confirmed] What MEL does today** (no stable global object id exists in any of this code):
| Mechanism | Where | Notes |
|---|---|---|
| Name + nearest position | `LootPreviewSpawner.FindSourceObject`, `RuntimeStaticObjectSpawner.FindSourceObject` (name index via `BuildSourceNameIndex`), `RuntimeInteractiveObjectSpawner.FindSourceObject`, `RuntimeCutVolumeSpawner.FindSourceObject`, `CustomEditorUI`/`MarkerManager.FindSceneObjectByNameAndPosition` | Saved as `sourceObjectName` + `sourceObjectPosition` (`IHasSourceObject`, `MarkerTypes.cs`) |
| Hierarchy path | `RemovedObject.path` (`GetFullPath`) | Path match only used as a secondary signal |
| `WorldInteractiveObject.Id` | `RemovedObject.worldObjectId`; `RuntimeInteractiveObjectSpawner.FindRemovedObject` | Only exists for interactive objects (doors, containers, etc.). Not for fuel tanks or walls |
| Position/rotation/scale score | `RuntimeInteractiveObjectSpawner.FindRemovedObject` | Fallback scoring ("proximityScore") and a logged warning about possible ambiguity |
**[Confirmed]** `GetInstanceID()` is used only for in-session de-duplication (`CustomEditorUI.ScanSceneObjects`, `LootPreviewSpawner`). It is **not** persisted anywhere, and should not be.
**[Confirmed]** Sibling index is not used to identify world objects; the only `GetSiblingIndex` use is for UI layout.
**Proposed practical identity record (not built)**, saved per decal/drawing in the map JSON:
1. `worldObjectId` (when a `WorldInteractiveObject` exists), as a strong key.
2. `path` (full hierarchy path), `name`, and the path of the specific renderer/collider hit relative to the selected root.
3. World `position`, `rotation`, `localScale` of the root at pick time (the same triple already stored in `RemovedObject`).
4. `siblingIndex` path (list of sibling indices from the scene root) to separate identical siblings (new; not used anywhere in MEL today).
5. Scene name (`Scene.name` from `SceneManager`) and, for artwork, the **hit point and normal in the object's local space**, plus the mesh name and renderer material names as a verification hint.
Resolution order on load: worldObjectId, then exact path + sibling path, then name + nearest to saved position within a tolerance, and only then loose name match. **Never** silently apply to a loose match; log it and surface it in the UI as "unresolved/ambiguous". This extends the existing `FindRemovedObject` strategy rather than inventing a new one.

### 6. Multiple identical objects and dynamic spawns
**[Confirmed]** Duplicates are common enough that MEL already compensates with nearest-position selection. Weaknesses:
- `LootPreviewSpawner.FindSourceObject` has a **name-only fallback** (case-insensitive, first match in scene order) used when the position search finds nothing, so it can return a wrong duplicate. `RuntimeInteractiveObjectSpawner` explicitly logs a "PROXIMITY fallback ... may be ambiguous if duplicates exist nearby" warning.
- Matching by position fails when the source has moved (the code comments mention opened doors). That does not apply to static tanks and walls, but does to dynamic or movable props.
- `RuntimeInteractiveObjectSpawner` includes a "scan for late-loaded objects not in the cache" fallback, which indicates objects can finish loading after the plugin first indexes them. Artwork restore must wait for the same readiness signals (the spawners poll for `GameWorld`/`MainPlayer`), and re-resolve after streaming.
- MEL's own clones are created by `Object.Instantiate(source)` and renamed (for example `InteractiveObject_{obj.name}`); they must be excluded from artwork targeting and should be handled as their own marker-owned targets.
**[Assumption]** Whether the game's map scenes are loaded additively in several chunks, and whether object names/hierarchies are identical between raids on the same map, was not verified. Dynamically spawned objects (bots, dropped loot, other mod spawns) have no stable identity and should be excluded from v1.

### 7. New classes/methods needed for a safe object-selection mode (proposed, not implemented)
- `ObjectSelectionMode` (state in `MapEditorController`, or a new `WorldObjectPicker` component): enter/exit, owns the cursor state, takes precedence over marker picking, ignores clicks over editor UI, Esc to cancel.
- `WorldObjectPicker.TryPick(Camera, Vector2, out WorldPickResult)`: one raycast pipeline returning a struct with `GameObject root`, `Collider collider`, `Renderer renderer`, `Vector3 point`, `Vector3 normal`, `int triangleIndex` (when available), reusing `IsEditorObject`, `ShouldIgnorePick`, and `IsScreenPointOverEditorUI`. Move or share those private helpers (currently private in `CustomEditorUI`).
- `WorldObjectInfo.Describe(GameObject)`: renderers, submesh count, `sharedMaterials` names and shader names, `mesh.isReadable`, `MeshCollider` presence, LOD info, `WorldInteractiveObject.Id`. Output into `FormatSceneGODetails`/Output tab first so real data can be collected from several maps.
- `WorldObjectId` (data class) + `WorldObjectResolver.Resolve(WorldObjectId)`: the identity record and resolution order above, with an explicit result state (`Exact`, `Fuzzy`, `Ambiguous`, `NotFound`), generalising `FindRemovedObject`. Share one scene index instead of the four duplicated `FindSourceObject` implementations (do not refactor those in the first pass).
- `Draw on Object` entry point: a context action in `CustomEditorUI` (the existing GameObjects tab details pane / `_goActionBtnRow`, `_contextMenuPanel`) that hands a `WorldPickResult` to the later drawing feature.
- Selection highlight: reuse `MarkerRenderer.SetSelectedSceneObject` / the 3D outline.
- Map data: a new list on `MapData` (for example `drawings`/`decals`) with `WorldObjectId`, local hit point/normal, and asset reference, with `JsonStorage.Load` defaulting it to empty so old files still load.
- Safety rules: never modify shared materials or meshes during selection (read-only); ignore players/bots/loot and MEL-owned objects; no persistence of instance IDs; all failures logged, never thrown into the game loop.

### Confirmed vs missing summary
| Capability | Status |
|---|---|
| Raycast from cursor to world GameObject | Confirmed (`PickSceneObjectAtMouse`) |
| Reject MEL objects / editor UI | Confirmed (`IsEditorObject`, `IsScreenPointOverEditorUI`, `IsMouseOverUI`) |
| User-configurable path filters | Confirmed (`ScenePickFilterConfig`) |
| Hit point / normal / UV from pick | Missing (discarded) |
| Trigger and invisible-collider rejection | Missing |
| Renderer/material/shader readout for scene objects | Missing |
| Mesh readability handling | Partial (exists only in `RuntimeCutVolumeSpawner`) |
| Stable object id | Only `WorldInteractiveObject.Id`; otherwise name + path + position heuristics |
| Duplicate disambiguation | Weak (nearest position; name-only fallback) |
| Dynamic object handling | Not handled |

### Unresolved questions
- Do tank/wall/container objects use `MeshCollider`, primitive colliders, or separate proxy colliders, and are their visible renderers children of the collider object? (In-game dump needed.)
- Are the names, hierarchy paths, and sibling order of target objects identical between raids on the same map? Does it change after a game update?
- Which gameplay components (players, bots, loot, corpses, doors) must be excluded, and what are their exact types in SPT 4.1.x?
- Does `RaycastHit.transform` return the rigidbody root for typical props here?
- Are scenes loaded additively, and when does streaming finish relative to MEL's spawners?

### Recommended next steps for Serenity
1. **Read-only diagnostic first.** Extend the existing GameObject details (`FormatSceneGODetails`) or add a debug-only action to log, for the picked object: hit collider type, trigger flag, layer, owning rigidbody, all renderers with `sharedMaterials` + shader names, `mesh.isReadable`, hierarchy path, sibling-index path, and `WorldInteractiveObject.Id`. Run it on fuel tanks, walls, containers, and a few duplicates on one or two maps, across two raids.
2. Compare identity data between raids to decide whether `path + siblingIndexPath + local position` is stable enough, or whether a looser tolerance is needed.
3. Only then design `WorldPickResult` / `WorldObjectId` / `WorldObjectResolver` and the "Draw on Object" action, with the filter chain above.
4. Keep the first drawing proof of concept to a decal-style overlay (see the previous entry), which needs hit point and normal but not UV data.

---

## Investigation entry — persistent storage for custom surface edits (2026-10-11)

Investigation only. No source code, map packs, or game assets were modified. **[Confirmed]** = read in MEL code; **[Assumption]** = not verified.

### Existing storage conventions (inspected)
| Purpose | Location / code | Notes |
|---|---|---|
| Per-map editor working copy | `<ServerModDirectory>\editor\<mapId>.json` via `JsonStorage` (`Client\JsonStorage.cs`), `MarkerManager.Save()` | `MapData` serialised with Newtonsoft, invariant culture, indented |
| Exported packs | `<ServerModDirectory>\exports\<packName>.json` via `MapEditorController.ExportPack` (name cleaned by `SanitizePackName`). One `PackData` file holds `maps[mapId] = MapData`; re-exporting merges into the existing file | |
| Final packs | `<ServerModDirectory>\packs\` (`Plugin.ServerModPacksDirectory`), scanned recursively for `*.json` by the runtime spawners | |
| Custom asset bundles | `packs\bundles\` and `packs\<packDir>\bundles\`, discovered by `BundleInjector.DiscoverBundles` | Keyed by file name without extension |
| Prefab templates | `<ServerModDirectory>\prefabs\*.json` via `PrefabStorage` | Name-based, sanitised file names |
| Other pre-created folders | `spawns`, `imports`, `cache` (created in `Plugin.Awake`) | No code found that reads `imports` or `cache` for artwork; their intended use is not documented. Not assumed available |
`ServerModDirectory` is `<SPT root>\user\mods\MapEditorLite` (`Plugin.Awake`). **[Confirmed]** MEL writes only under this folder; no game asset is overwritten by any inspected save path.

**Load precedence [Confirmed]**: `MapEditorController.DetectRaid` loads `LoadMapDataFromExports(mapId)` first (preferred pack name from the UI, then any `exports\*.json` that has that map) and only falls back to `JsonStorage.Load(mapId)` when no export contains the map. Separately, each runtime spawner (`RuntimeStaticObjectSpawner.LoadPacks` and the others) reads **all** `*.json` under `packs\` (and for several, `exports\`) at runtime, filters by `pack.maps[mapId]`, and spawns. So the runtime applies *exported/pack* data, and the editor working copy `editor\<mapId>.json` is only used inside the editor when no export exists for that map. Surface edits stored only in `editor\` would therefore **not** be applied by a runtime component unless that component also reads `editor\`.

**Server side [Confirmed]**: `Server\PackData.cs` uses `System.Text.Json` with explicit `[JsonPropertyName]` on known fields, and `PackLoader` calls `JsonSerializer.Deserialize<PackData>` with default options. **[Assumption, standard System.Text.Json behaviour, not tested here]** unknown fields (such as a new `drawings` list) are ignored, so a client-only list should not break server loading. Needs a quick check with a real pack before relying on it. The server mod only deals with loot/spawn data; it has no role in client-side visuals.

**Backups/import [Confirmed]**: No backup feature exists. Searches for backup/`.bak`/`File.Copy` found nothing. `ExportPack` overwrites the pack file after merging (no copy of the previous file). There is no general pack import UI: "Import Vanilla Loot" (`VanillaImporter`) reads server DB files, not packs. "Export" is the only pack file operation in the editor. Image loading from files (`ReadAllBytes`, `LoadImage`) is **not present anywhere** in the client; the only external asset loading is `AssetBundle.LoadFromFile(Async)`.

### Proposed persistence approach (smallest reliable design)
1. **Data in the existing map JSON, as a new optional list on `MapData`** (`Client\MarkerTypes.cs`), for example `surfaceDecals`. This follows the same pattern as `removedObjects` (which already stores world-object identity). Reuses `JsonStorage`, `ExportPack`, undo snapshots (`MarkerManager.Snapshot` serialises `MapData`), and pack merging with no new storage code. Old files load with an empty list (Newtonsoft default for missing fields; `MarkerTypes` fields are initialised in-place).
2. **Image files stored as plain PNGs beside the pack, not in the bundle folders.** Suggested path: `<ServerModDirectory>\packs\images\<file>.png` (final) with the editor reading from `<ServerModDirectory>\imports\` while authoring (the existing `imports` folder is created but unused). The JSON stores a **relative file name**, never an absolute path. **Do not place images under any folder named `bundles`**: `BundleInjector.DiscoverBundles` registers every file there except `.json/.txt/.md` as a bundle, so a PNG there would be treated as a bundle [Confirmed]. **Keep metadata for images out of `.json` files under `packs\`**, because spawners parse every `*.json` there as a possible pack (non-pack JSON is skipped with a warning on the server, silently on some client paths).
3. **Export copies the referenced PNGs** into the same pack folder so a pack is portable; import is a manual copy (matching how bundles are shipped today). Content hash (for example SHA-1) stored per image lets the runtime detect a missing or changed file.
4. **Non-destructive rendering**: edits are always separate overlay GameObjects (or per-object material copies if a later technique needs them), created at map load, destroyed on map unload. Original meshes, materials, and textures are never written. This also means "restore the original appearance" never requires touching SPT files.

### Data to serialise per edit (proposed record, not implemented)
- `id` (GUID string, like `MarkerBase.id`/`RemovedObject.id`), `name`, `group` (optional, reuses the editor group/lock convention), `enabled`.
- **Target identity** (from the previous entry): `worldObjectId`, `path`, `siblingIndexPath`, `name`, root `position`/`rotation`/`scale` (`TransformData`, same as `RemovedObject`), scene name.
- **Placement, stored relative to the target** so it survives small root shifts: `localPosition`, `localRotation` (or surface normal), `scale` (width, height, depth), `TransformData` style, plus the world-space hit point at authoring time as a fallback/verification value.
- **Image**: `imageFile` (relative), `imageHash`, `tint`/`opacity`, `blendMode` (alpha), `sortOrder`/`depthBias` (for z-fighting).
- **Material hint (verification only)**: name of the renderer that was hit, its material names/shader name at authoring time. Used to decide whether the target is still compatible; it is not used to modify the material.
- `schemaVersion` (integer) so the format can change without breaking old packs.
All numeric values must round-trip with the invariant culture, like the existing serialiser settings.

### Associating edits with map and object
- **Map**: by `MapData.map` / `PackData.maps[mapId]`, exactly as every other marker list; `mapId` comes from `GameWorld.LocationId` (fallback `MainPlayer.Location`) as in all existing spawners [Confirmed].
- **Object**: the identity record above. Restore resolves it with the order exact id -> path + sibling path -> name + nearest position within a tolerance. This extends `RuntimeInteractiveObjectSpawner.FindRemovedObject`, and shares its known weakness with duplicates [Confirmed warning in that code].

### Restoring edits when a map loads
Follow the existing runtime pattern [Confirmed]: a new MonoBehaviour attached in `Plugin.Awake` (like `RuntimeStaticObjectSpawner`), a `DetectRaid`-style loop waiting for `GameWorld` and `MainPlayer`, a `LoadPacks` that reads the packs/exports folders and filters by map id, then a coroutine per edit with a wait/retry for late-loaded source objects (the existing spawners already retry with "not ready, waiting..."). The editor path (`MapEditorController.DetectRaid`) must also show the same edits through the same code, so what the author sees is what players get. Skip quest-gating (not needed for the proof of concept).
**[Gap]** The runtime does not read `editor\<mapId>.json` at all. For the proof of concept the author must Export (as for every other feature), or the new component must explicitly read the editor copy; this must be decided before writing code.

### Removing edits / restoring the original look
- **Edit-level**: delete the entry from the list (same as `manager.Data.removedObjects.Remove(...)`), destroy its overlay object, then save/export. The original object is untouched, so nothing else needs undoing.
- **Pack-level**: delete the pack/export file or its `maps[mapId].surfaceDecals` list. Removing the images folder alone must be safe (see missing files below).
- **Runtime safety**: overlay objects are parented under a MEL-owned root (`MLE_`-prefixed, so `CustomEditorUI.IsEditorObject` already treats them as editor objects and will not pick them) and destroyed on map change, as `RuntimeStaticObjectSpawner.ClearSpawned` does for spawned objects.
- **[Assumption]** Techniques that replace a material or texture (UV painting, material swaps) would need a stored "original material" reference to restore, like `RemovedObjectHelper` stores `RemovalState`; this is another reason to prefer separate overlay objects for the first proof of concept.

### Missing files, incompatible objects, invalid configuration (proposed policy)
- **Missing/unreadable image or hash mismatch**: skip that edit, log one warning with file name and edit id, show it in the editor Output tab (`CustomEditorUI.LogOutput`), keep the JSON entry so the file can be restored later. Never delete data automatically.
- **Target not found**: follow the resolver states (`Exact`, `Fuzzy`, `Ambiguous`, `NotFound`); apply only `Exact`, and `Fuzzy` only when position agreement is within tolerance; log the rest. Editor lists them as "unresolved" so the author can re-pick or delete.
- **Incompatible object** (renderer/material hint no longer matches, mesh missing, target is inactive): skip and log; do not attempt to adapt.
- **Invalid values** (NaN, zero or negative scale, oversize images, unknown `schemaVersion`, bad JSON): validate when loading, ignore the single bad entry, never throw into the game loop (all existing loaders wrap in `try/catch` and log, for example `LoadPacks`).
- **Size limits (to decide)**: cap image dimensions and file size to avoid memory spikes. Values were not measured here.
- **Texture lifetime**: textures created from files must be destroyed on map unload (`Object.Destroy`); one texture per distinct file, reused by all edits that reference it.

### Do existing import/export/backup features support this?
- **Export (`ExportPack`)**: supports the JSON part for free if the new list is on `MapData`. It does **not** copy any sidecar files, so image copying and missing-image warnings must be added.
- **Import**: no pack-import feature exists; packs are installed by copying files. Images would be copied the same way.
- **Backup**: no mechanism exists. A pre-export copy of the old pack file (and of the images folder) is not provided today; this is a recommended addition but outside the proof of concept.
- **Pack merging**: `ExportPack` replaces `pack.maps[currentMapId]` wholesale, so exporting a map also replaces any decals stored for that map in that pack file (expected, consistent with other lists).

### Unresolved risks
- **Runtime source of truth**: editor copy vs exported pack (see the gap above).
- **Image loading is new**: no existing PNG loading in MEL. `ImageConversion.LoadImage` needs `UnityEngine.ImageConversionModule`, which is not referenced in `Client\Client.csproj` (only `UnityEngine.AssetBundleModule` was seen in the grep; the full reference list was not reviewed). Whether it is available and allowed in the SPT 4.1.x runtime is **unverified**; an alternative is shipping textures inside an asset bundle through the existing `BundleInjector`/`AssetBundle.LoadFromFile` path, which is already proven in this plugin.
- **Object identity across raids**: unverified (see the previous entry); the diagnostic step is a prerequisite.
- **Duplicate/dynamic objects** and late streaming of scenes.
- **Server tolerance** of an unknown `surfaceDecals` property: believed fine, untested.
- **Pack size and distribution**: many PNGs make packs large; no versioning for images exists.
- **Concurrent writers**: two MEL sessions or a hand-edited file can overwrite each other; no locking exists in the current JSON write paths.

### Recommended next steps for Serenity
1. Run the read-only object diagnostic from the previous entry, and confirm identity stability across two raids.
2. Check whether PNG loading (`ImageConversion.LoadImage`, `Texture2D`) works inside the SPT 4.1.x client plugin with a throwaway log-only test; if it does not, pivot to asset-bundle textures.
3. Decide the runtime source of truth (exports/packs only vs also `editor\`).
4. Then prototype: add the optional list on `MapData`, one new runtime spawner modelled on `RuntimeStaticObjectSpawner`, an `images` subfolder, and the missing-file/unresolved-target logging above. Add a pre-export `.bak` copy only if testing shows pack overwrites are a real problem.

---

## Planning entry — first 3D painting proof of concept: "Decal on Object" (2026-10-11)

**Status: PLAN ONLY. Nothing in this entry has been implemented, built, or tested.** No source, project, pack, or asset files were changed. Implementation waits for owner approval.

### Conclusions carried over from the four investigations
- MEL has no decal, texture-paint, material-readout, or PNG-loading code today (verified by searching `Client\`). It does have: a world raycast picker (`CustomEditorUI.PickSceneObjectAtMouse`), a marker/gizmo/inspector/hierarchy framework (`MarkerBase` subclasses), per-map JSON (`JsonStorage`, `ExportPack`), and runtime spawner components that apply pack data on map load (`RuntimeStaticObjectSpawner` and siblings).
- Of the four artwork techniques, a world-space surface decal (a separate overlay quad, no change to original meshes/materials/textures) is the simplest and the only one that does not depend on mesh UVs, mesh readability, or shared-material handling.

### Verified facts vs assumptions
**Verified in the repository**
- `Client\Client.csproj` (net471) references `UnityEngine`, `CoreModule`, `PhysicsModule`, `IMGUIModule`, `UIModule`, `UI`, `InputLegacyModule`, `TextRenderingModule`, `AIModule`, `AssetBundleModule`, `UnityWebRequestModule`. It does **not** reference `UnityEngine.ImageConversionModule` or `UnityWebRequestTextureModule`.
- MEL already creates runtime `Material`s with `Shader.Find("Standard")` / `Shader.Find("Sprites/Default")` and sets alpha-blend keywords by hand (`LootPreviewSpawner.CreateFallbackPreview`, ~lines 952-963; `Standard` with `_Mode = 3`, `_SrcBlend`/`_DstBlend`, `_ZWrite = 0`, `renderQueue = 3000`). The same source line already guards `?? Shader.Find("Sprites/Default")`, which implies a null result from `Shader.Find` is possible; whether either shader is actually present in the SPT 4.1.x build is **not verified**.
- Adding a marker kind touches many places. The newest kind, `NavMeshArea`, is referenced about 48 times across `CustomEditorUI.cs` (6), `MapEditorController.cs` (7), `MarkerManager.cs` (17), `MarkerTypes.cs` (6), `MarkerRenderer.cs` (12) and the new runtime builder (9). That is the realistic cost of a new `MarkerBase` subclass.
- Runtime components are attached in `Plugin.Awake` (`gameObject.AddComponent<Runtime...>()`), each with `Awake`, a `DetectRaid`-style `Update`, a `LoadPacks` over `packs\` (and some over `exports\`), and `OnDestroy`.
- `CustomEditorUI.IsEditorObject` rejects any object under a name starting with `MLE_`, and `MarkerRenderer.PickFromScreenPosition` picks markers on layer 2 via `MarkerVisual`.
- The repo contains no game `Managed` DLLs (only MEL's own built DLLs), so the Unity version and available modules could **not** be checked offline.

**Assumptions (must be proven by the milestone noted)**
- A1 (M0): `ImageConversion.LoadImage` / `Texture2D.LoadImage` is available at runtime in the SPT 4.1.x client, and a reference to `UnityEngine.ImageConversionModule.dll` exists in `EscapeFromTarkov_Data\Managed` of the target install.
- A2 (M0): `Shader.Find("Sprites/Default")` (or another transparent unlit shader) returns non-null and renders correctly in a raid; shaders not included in the player build are not findable by name.
- A3 (M1): the picked hit on a fuel tank/wall/container returns a usable collider and visible renderer, with a normal that points out of the surface.
- A4 (M4): identity (`path` + sibling path + name + root position) resolves the same object after a restart.
- A5 (M2): a small quad slightly offset along the surface normal renders without z-fighting or culling, and is visible under the game's lighting/post-processing at normal distance.
- A6 (M2): the unlit sprite-style shader will look flat (not lit or shadowed); acceptable for a first prototype.
- A7: tarkov's own decal/projector system is **not** used; whether it is accessible was not investigated.

### Recommended rendering technique
**World-space quad decal.** One MEL-owned child GameObject per decal: a `MeshFilter` + `MeshRenderer` using a unit quad, a per-decal `Material` (alpha-blended, unlit, double-sided or back-face handled) whose main texture is the loaded PNG. It is placed at the picked hit point, oriented so the quad faces along the hit normal, offset a few millimetres (`normal * ~0.003 m`) to avoid z-fighting, with width/height scale. It is **not** parented to the world object for the prototype (placed in world space under a MEL-owned root), which means:
- the original object's mesh/material/texture is never touched (rollback is just destroying the quad),
- it cannot be affected by LOD swaps or mesh cuts on the target,
- works on curved surfaces only as a flat patch: on a fuel tank it will float off the surface away from the hit point. For the prototype, accept this and test on flat surfaces (wall, container face, flat tank end) first; curved-surface fidelity (mesh-conforming/projector decals) is explicitly deferred.

Fallback technique if A2 fails: build the material from a shader inside an asset bundle shipped with the mod (the existing `BundleInjector` / `AssetBundle.LoadFromFile` path is proven in MEL). Fallback if A1 fails: ship the texture inside that same bundle instead of loading PNG at runtime.

### Scope of the proof of concept (in / out)
**In**: enter editor, pick one world object (flat face), choose a PNG from a MEL folder, create one decal, adjust position/rotation/scale with the existing gizmo and inspector fields, preview live, save via the normal MEL save/export, reload map and restart to verify, delete the decal to restore the original appearance.
**Out (explicitly deferred)**: freehand brush, layers, undo/redo of drawing (the normal marker undo may incidentally work, but it is not a goal), UV painting, curved-surface conforming, per-object material edits, multiple decals per object tuning, image editing, shipping/import UI, backups, quest gating, multiplayer/other-client sync, lighting/normal-map support.

### Data model (smallest)
- New `SurfaceDecal : MarkerBase` in `Client\MarkerTypes.cs`, new `MarkerKind.SurfaceDecal`, new `List<SurfaceDecal> surfaceDecals` on `MapData` (default empty so old files load).
- Fields (reusing existing `MarkerBase` `id`, `name`, `group`, `position`, `rotation`): `imageFile`, `imageHash` (optional in M3), `scale` (`TransformData` width/height/unused depth), `opacity`, `normalOffset`, and a target record: `targetName`, `targetPath`, `targetPosition` (world), `targetSiblingPath` (optional), `targetWorldObjectId` (optional), `rendererName` (hint), `schemaVersion = 1`.
- Reusing `MarkerBase.position/rotation` means the existing gizmo, selection, hierarchy, group/lock, and undo snapshot (JSON of `MapData`) work without new code; this is the main reason for choosing it over a standalone list.
- Also **mirror** the field names in `Server\PackData.cs`? **No** for the prototype: the server never uses decals. Needs a one-time check that the server ignores the unknown `surfaceDecals` property when parsing a pack containing it (A8, milestone M3).

### New components / classes
1. `SurfaceDecal` (data, above).
2. `DecalTextureCache` (static): loads PNG bytes from `<ModDataDirectory>\imports\` (editor) or `<ServerModPacksDirectory>\images\` (final), creates one `Texture2D` per file, caches, destroys on map unload, validates size (a cap to be decided, e.g. max 2048x2048) and returns null on failure.
3. `DecalMeshFactory` (static) or inline helper: shared unit quad mesh and the per-decal material builder (shader lookup with fallback and null checks).
4. `SurfaceDecalPreview` (editor): builds/updates the quad for a given `SurfaceDecal`, hooked into the same call sites that rebuild `LootPreviewSpawner` previews so edits made via inspector/gizmo update live. Alternatively a small `SurfaceDecalView` MonoBehaviour tag on the quad (like `MarkerVisual`) with `decalId`.
5. `RuntimeSurfaceDecalSpawner : MonoBehaviour` (raid): modelled on `RuntimeStaticObjectSpawner` (Awake, `DetectRaid`, `LoadPacks` over `packs\` + `exports\`, map filter, `OnDestroy`/`ClearSpawned`). For the prototype it creates the quad from the saved world transform and only uses the target record for a verification check (resolve by name + nearest position, log if the target is missing). Quad is shown even if the target is missing? **Decision to confirm with owner**: recommended default is *skip and log* when the target is not found, so artwork never floats in empty space.
6. `WorldPickResult` + small `WorldObjectPicker.TryPick` (minimal version): a new method that does the same raycast as `PickSceneObjectAtMouse` but returns `RaycastHit` data (point, normal, collider, renderer), uses `QueryTriggerInteraction.Ignore`, requires a `Renderer`, and reuses `IsEditorObject`/`ShouldIgnorePick`. Put it next to the existing helpers in `CustomEditorUI.cs` (or a new `WorldObjectPicker.cs`) to avoid changing the existing picker's behaviour.
7. Read-only diagnostic (M0/M1): a log action that prints collider/renderer/material/shader/readability/path/sibling-path/id for the picked object.

### Exact files likely to change (expected, to be confirmed while implementing)
| File | Change |
|---|---|
| `Client\MarkerTypes.cs` | `MarkerKind.SurfaceDecal`, `SurfaceDecal` class, `MapData.surfaceDecals` |
| `Client\MarkerManager.cs` | include decals in `GetAllMarkers`, `FindById`, delete/duplicate/select paths (same spots as `navMeshAreas`, ~17 references) |
| `Client\MarkerRenderer.cs` | gizmo-box visual for the new kind in `CreateVisual`/colour tables (~12 references for `NavMeshArea`) |
| `Client\CustomEditorUI.cs` | "Draw on Object" action in the GameObjects tab details row (`RefreshGOActionRow` / `_goActionBtnRow`), inspector fields (image file, scale, opacity), hierarchy entry (~6 references) |
| `Client\MapEditorController.cs` | on pick-mode click create decal at hit; spawn/clear decal previews in `DetectRaid`; dispose on map change (~7 references) |
| `Client\Plugin.cs` | `gameObject.AddComponent<RuntimeSurfaceDecalSpawner>()` after the other spawners; create the `images` folder next to `imports` |
| `Client\Client.csproj` | only if A1 needs it: add `UnityEngine.ImageConversionModule` Reference (matching the other `HintPath` entries) |
| `Client\Locale.cs`, `lang\*.json` | optional UI strings; `Locale` appears to add defaults in code, so tests can use English only |
| New: `Client\SurfaceDecal*.cs`, `Client\DecalTextureCache.cs`, `Client\RuntimeSurfaceDecalSpawner.cs`, `Client\WorldObjectPicker.cs` | the components above |
| **Not changed** | `Server\*` (unless A8 fails), `JsonStorage.cs` (list is part of `MapData`), `PrefabStorage.cs`, `BundleInjector.cs` |

### Implementation sequence (independently testable milestones)
**M0 — Feasibility probe (no feature code).** Log-only checks run once in a raid by a temporary, clearly marked diagnostic: (a) `Shader.Find` for `Sprites/Default`, `Unlit/Transparent`, `Standard` and log which return null; (b) load one test PNG from `imports\` via the intended loader and log width/height/format; (c) if the module reference is missing, report it rather than guessing. *Pass*: both shader and PNG load work. *Fail*: switch to the asset-bundle fallback and revise the plan before M1.
**M1 — Pick with hit data + diagnostic.** Add `WorldObjectPicker.TryPick` and a log-only "Describe pick" action. Collect results on a fuel tank, wall, container, and two duplicates, on two maps, across two raids. *Pass*: usable collider/renderer/normal on flat surfaces; identity record is stable across raids (A4 evidence). Nothing is saved.
**M2 — In-memory decal preview.** Add `DecalTextureCache`, quad/material builder, and a single hard-coded keybinding or button that places one decal at the picked hit with a test PNG. No saving. *Pass*: visible, correct orientation, no z-fighting, correct transparency, no errors, and it disappears when the editor/map unloads.
**M3 — Data model and persistence.** Add `SurfaceDecal`, `MapData.surfaceDecals`, `MarkerKind`, hierarchy/inspector entries, gizmo adjust, save and export. Confirm an old map JSON still loads, the new JSON round-trips, and the server still loads a pack that contains `surfaceDecals` (A8). *Pass*: decal survives Save, Export and editor reopen in the same session.
**M4 — Runtime restore.** Add `RuntimeSurfaceDecalSpawner` (Plugin.Awake attach, map detection, load from `packs\`/`exports\`, target verification, missing-file/target logging). *Pass*: with the editor disabled (`EnableEditor=false`), the exported decal appears after starting a new raid and after a full game restart.
**M5 — Remove / restore.** Delete from the hierarchy and via "clear all decals on this map"; destroy the quad and texture; save/export; verify a fresh raid shows no decal and the original object is visually identical. Verify removing the PNG file only logs a warning.
**M6 — Hardening (still small).** Image size cap, NaN/zero-scale validation, schemaVersion check, clean teardown on map change, performance check with e.g. 20 decals.

### Build procedure (from the existing session notes; **not run**)
- Client: `dotnet build -c Release -p:SPTPath="<SPT install>"` in `Client\`. Output `MapEditorLite.Client.dll` to `BepInEx\plugins\` (the changelog's build notes say only that DLL should remain there; delete the `.pdb`/`System.*.dll`).
- Server: unchanged unless A8 fails; if rebuilt, only `MapEditorLite.Server.dll` plus data folders should remain under `SPT_Runtime\user\mods\MapEditorLite\` (delete `.deps.json`, `.pdb`, `staticwebassets.endpoints.json`).
- Per the previous notes, DLL deployment into an SPT install is done by the owner; the assistant does not copy DLLs into game folders.
- Back up the currently deployed `MapEditorLite.Client.dll` before replacing it.

### Runtime test procedure (to be run by the owner in-game)
1. Set `EnableEditor = true`; start a raid on a small map; open the editor (default F8) and free-cam.
2. M0/M1 diagnostics: read `BepInEx\LogOutput.log` for the shader/PNG/pick data. Repeat in a second raid.
3. M2/M3: place one decal on a flat wall using `test.png` (a small RGBA PNG with transparent regions) in `imports\`. Move with gizmo, rotate, scale via inspector; check from several angles/distances and in dark/bright areas.
4. Save + Export (existing buttons). Restart the raid on the same map, then fully restart the game; confirm the decal returns in the same place.
5. Disable the editor (`EnableEditor=false`) and confirm the decal still appears (runtime-only path).
6. Delete the decal, export, restart a raid, confirm original appearance.
7. Negative tests: rename/delete the PNG; edit the JSON to an invalid scale; point the target to a non-existent name; load an old pack without `surfaceDecals`; confirm warnings only, no exceptions, no gameplay impact.
8. Check the frame rate with 20 decals visible, and memory use with several large PNGs.

### Compatibility and performance risks
- Unverified: PNG loader availability (A1), shader availability (A2), identity stability (A4), server tolerance of the new JSON field (A8).
- A flat quad on a curved tank will visibly float/clip; documented limitation, deferred.
- Z-fighting and sorting against the game's transparent/fog/post-processing effects; render queue and depth offset may need tuning on-site.
- Anti-cheat/online: not investigated; MEL is for the local SPT client only, and no networking is involved.
- Interaction with other mods (for example mods that replace/hide objects, or MEL's own cut-volume/removed-object features moving the target): a decal parented to world space will stay behind if the target is removed. Parenting to the target is deferred because LOD/streaming/hierarchy rebuilds make it riskier; revisit after M4.
- Memory: each distinct PNG is one uncompressed `Texture2D` (about 4 bytes per pixel before mips); a 2048x2048 image is roughly 16 MB (about 21 MB with mips), so enforce the cap and share textures between decals.
- Draw calls: one per decal (no batching because each has its own material instance); fine for tens of decals, not hundreds.
- Raycast filtering gaps (bots, loose loot, triggers) listed in the previous entry still apply.
- Concurrent edits to the same JSON from two sessions are not guarded (existing MEL behaviour).

### Rollback strategy
- All work happens on a copy of the repo/branch (this folder is not a git repository, so make a dated zip/copy of `Client\` and `CHANGELOG_SESSION.md` before M2; **to be confirmed with the owner**, since the repo has no version control).
- Keep the currently deployed `MapEditorLite.Client.dll` and the server DLL as backups; rollback is restoring them.
- Prototype switch: gate all new behaviour behind a new BepInEx config entry (for example `EnableDecalPrototype`, default `false`) so a failing prototype is disabled without removing code. The spawner and UI entries check it.
- Data safety: the new JSON fields are optional, and older MEL builds (Newtonsoft default) ignore unknown fields **[assumption, not tested]**; the PNGs sit outside `bundles\`; no SPT file is overwritten. To fully remove: delete `surfaceDecals` entries (editor "clear all decals") or the `images\` files, and delete the new `.cs` files / revert the listed edits.
- Milestone gates: stop and report after M0 and M1 before committing to M2+. If M0 fails with both techniques, stop and revert to documentation only.

### Open questions for the owner
1. Is there a preferred place for the PNG folder (`imports\` while authoring, `packs\images\` for shipping as proposed)?
2. Skip-and-log (recommended) vs show-anyway when the target object cannot be found?
3. Is a config flag (`EnableDecalPrototype`) acceptable?
4. Which map and which flat object should be the reference test target?
5. Should the work be done on a copy of `Client\` first, since there is no git history here?

### Next step
**Waiting for owner approval before any source change.** Suggested first approval: M0 (feasibility probe) only.
---

## Investigation entry — 2026-10-11

### Scope
Architecture review for a future 3D painting / custom image projection feature in MEL for SPT 4.1.x. No source code, project files, dependencies, game assets, map packs, or installed DLLs were modified.

### Confirmed client plugin startup and initialization
- Entry point: [Client/Plugin.cs](Client/Plugin.cs) (`Plugin : BaseUnityPlugin` in `Awake()`).
- Startup confirms the mod detects the game root, creates `user/mods/MapEditorLite` directories, initializes JSON storage and prefab storage, injects bundles, binds config values and localization, optionally attaches `MapEditorController`, and registers runtime spawner components (`RuntimeStaticObjectSpawner`, `RuntimeInteractiveObjectSpawner`, `RuntimeExtractZoneSpawner`, `RuntimeBotSpawnSpawner`, `RuntimeLightZoneSpawner`, `RuntimeOcclusionRepairSpawner`, `RuntimeCutVolumeSpawner`, `RuntimeBlockerSpawner`, `RuntimeNavMeshBuilder`).
- `Plugin.Awake()` is the clear initialization boundary for the MEL client plugin.

### Editor/UI communication path
- The editor is not a web-client-to-plugin bridge; the runtime UI is a Unity `MonoBehaviour` pipeline in [Client/MapEditorController.cs](Client/MapEditorController.cs) and [Client/CustomEditorUI.cs](Client/CustomEditorUI.cs).
- `MapEditorController.Awake()` creates the visual root, preview root, `MarkerManager`, `MarkerRenderer`, `LootPreviewSpawner`, and then adds `CustomEditorUI` with `Init(this, _manager, _renderer, _previews)`.
- The UI reads and writes the same in-memory manager/renderer objects directly, so communication is direct C# references rather than an external API.

### Map identification and world object model
- Map detection is based on `GameWorld.LocationId` with a fallback to `world.MainPlayer.Location` in [Client/MapEditorController.cs](Client/MapEditorController.cs).
- Runtime map load is via `MapEditorController.DetectRaid()` -> `LoadMapDataFromExports()` / `JsonStorage.Load(mapId)` and then `_manager.SetMapData(mapData)`.
- The canonical editable object model is in [Client/MarkerTypes.cs](Client/MarkerTypes.cs): `MapData`, `MarkerBase`, `LooseLootSpawn`, `LootZone`, `StaticObject`, `InteractiveObject`, `ExtractZone`, `BotSpawnPoint`, `BotSpawnZone`, `PmcSpawnZone`, `LightZone`, `TriggerZone`, `OcclusionRepairVolume`, `CutVolume`, `Blocker`, `NavMeshArea`, `RemovedObject`.
- The code is strongly marker-oriented; MEL stores map entities as data objects with transforms and optional source-object references, not as arbitrary runtime Unity scene objects by default.

### Selection capabilities: arbitrary object picking vs marker editing
- Confirmed: MEL can inspect and select arbitrary scene objects via `MarkerRenderer.SetSceneObjects(...)`, `MarkerRenderer.PickSceneObject(...)`, and the scene-object browser in [Client/CustomEditorUI.cs](Client/CustomEditorUI.cs).
- This path uses `Collider` and `Renderer` bounds (`CalculateBounds(GameObject)`) and ray-based picking. Scene objects can be highlighted and selected for inspection.
- Confirmed limitation: the main editing system still respects the marker model (`MarkerManager`, `MarkerBase`, preview spawners, and map JSON save files). It is not a general-purpose editor for arbitrary runtime GameObjects in the sense of mesh editing or object graph mutation.
- The current design supports picking existing scene objects for association or placement, but not full arbitrary-world-object editing as a first-class MEL entity.

### Unity access available in the current codebase
- Confirmed access patterns include:
  - `GameObject` traversal via transforms and `GetComponentsInChildren<Collider/T>(true)` and `Renderer` lookups.
  - `Bounds` calculation and encapsulation (`MarkerRenderer.CalculateBounds()`).
  - `Ray` intersection and camera screen picking (`PickSceneObject`, `PickFromScreenPosition`, `Update3DSceneObjectOutlines`).
  - `Transform`, position/rotation, and vector conversions (`TransformData` in [Client/MarkerTypes.cs](Client/MarkerTypes.cs)).
  - Runtime object activation / hidden state / custom spawn creation via various `Runtime*Spawner` classes.
- Not confirmed in the reviewed code: direct editable mesh procedural generation, UV manipulation, custom material projection pipeline, or texture painting API. The codebase appears to support preview placement and scene-object association much more than custom mesh/texture editing.

### Map pack, asset, and map configuration loading/saving
- Editor persistence: [Client/JsonStorage.cs](Client/JsonStorage.cs) saves per-map JSON under the mod’s `editor` directory (`SaveDirectory = Path.Combine(modDataDirectory, "editor")`).
- Pack loading: [Server/PackLoader.cs](Server/PackLoader.cs) scans `MapLoot` and `user/mods/.../MapLoot` directories, plus legacy paths, then deserializes `PackData` objects.
- Server registration: [Server/ServerPlugin.cs](Server/ServerPlugin.cs) loads packs, writes forced static/loot spawn files into `db/CustomLootspawns` and `db/CustomStaticSpawns`, and registers them via `WTTServerCommonLib`.
- This creates a two-layer architecture: editor state JSON (per map) plus pack/registration data loaded by the server mod.

### Best extension candidates for a 3D editing feature
- [Client/MapEditorController.cs](Client/MapEditorController.cs) — overall editor lifecycle, selection, gizmo, map load, freecam, drag handling.
- [Client/MarkerManager.cs](Client/MarkerManager.cs) — active map data, selection, editing state, save/load, undo/redo.
- [Client/MarkerRenderer.cs](Client/MarkerRenderer.cs) — render visuals, bounds, scene-object outlines, picking, 3D selection helpers.
- [Client/LootPreviewSpawner.cs](Client/LootPreviewSpawner.cs) — preview placement and runtime object instantiation.
- [Client/CustomEditorUI.cs](Client/CustomEditorUI.cs) — inspector, object browser, source object picking, scene-selection workflows.
- [Client/MarkerTypes.cs](Client/MarkerTypes.cs) — canonical data model for objects and custom map entities.
- [Client/JsonStorage.cs](Client/JsonStorage.cs) and [Server/PackLoader.cs](Server/PackLoader.cs) — persistence and map pack import/export flow.

### Key unresolved questions
- Does the Unity runtime expose a supported API for direct mesh editing, UV painting, or custom material projection on arbitrary world objects?
- Is custom image projection meant to operate on existing scene meshes, marker previews, or generated geometry?
- Does MEL need a new editing subsystem (for 3D brush/projection state) or can it fit into the current preview-picker-renderer pipeline?

### Recommended next step
- Next investigation should focus on the exact Unity API surface available to MEL for mesh/material projection: scene-object access, renderer/material access, and whether the game exposes any editable mesh/UV API. That is the gating question for a true 3D painting/projection feature. If those APIs are absent, a practical next step is to design a marker-based custom projection workflow layered on top of the existing preview/selection architecture rather than a direct scene-mesh editor.

### Conclusion
MEL already has a strong map-editor foundation for placement, selection, previewing, and map-data persistence, and it can inspect arbitrary scene objects. What it does not yet show is a general-purpose 3D editing system for custom mesh/texture painting or image projection. The most promising extension path is to reuse the existing controller/renderer/preview pipeline, but the missing technical evidence is the availability of Unity mesh/material/projection APIs and any existing game-side hooks for them.

---

## M0 feasibility probe — decal shaders and PNG loading (2026-10-11)

**Scope:** M0 only. No decal placement, picking changes, data model, persistence, UI, or runtime spawner. No world objects, SPT materials/meshes, or game assets are touched. Nothing was deployed.

### Static inspection (confirmed)
- **No SPT install exists on this machine.** Searched `C:\` and `D:\` (depth-limited) for `EscapeFromTarkov_Data`; only `C:\Battlestate Games\BsgLauncher` and `Escape from Tarkov Arena` were found, neither containing SPT's `Managed` folder. The actual SPT 4.1.x `UnityEngine.*.dll` files therefore could **not** be inspected, and the Unity version is not known from evidence. (Other Unity games' `UnityEngine.ImageConversionModule.dll` exist elsewhere on disk; they say nothing about EFT and were not used.)
- **`Client\Client.csproj` references** (all `$(SPTPath)\EscapeFromTarkov_Data\Managed\...`): `UnityEngine`, `CoreModule`, `AIModule`, `AssetBundleModule`, `IMGUIModule`, `PhysicsModule`, `InputLegacyModule`, `TextRenderingModule`, `UIModule`, `UnityWebRequestModule`, `UnityEngine.UI`, plus Comfort, Newtonsoft.Json, Sirenix, Assembly-CSharp. **No** `ImageConversionModule` or `UnityWebRequestTextureModule` reference. The csproj was not modified.
- **Reference of the existing built DLL** (`BepInEx\plugins\MapEditorLite.Client.dll`, read via reflection metadata only, not loaded for execution) lists the same modules (version `0.0.0.0`, i.e. split-module Unity). It does not reference `ImageConversionModule`, consistent with MEL never using PNG APIs (grep for `LoadImage`, `ImageConversion`, `EncodeToPNG`, `UnityWebRequestTexture` in `Client\` returned nothing).
- Not concluded: that `ImageConversion` is unavailable. In split-module Unity, `Texture2D` (CoreModule) and `ImageConversion.LoadImage` (ImageConversionModule) are separate; the module DLL is normally shipped in `Managed`, but whether it exists in EFT's folder is **unverified**. Because MEL's csproj lacks the reference, a direct `ImageConversion.LoadImage(...)` call would likely fail to compile (assumption). The probe therefore resolves the type by reflection at runtime, so no new dependency or csproj change is needed to test availability.
- **Shaders already used by MEL** (`Shader.Find` with null-coalescing fallbacks): `Sprites/Default`, `Standard` (`LootPreviewSpawner.cs` lines 269, 280, 407, 953; `MarkerRenderer.cs` lines 1181, 1426), `Hidden/Internal-Colored`, `Unlit/Color` (`MarkerRenderer.cs` line 1191). The fallback chains show the author guarded against null results, but there is no evidence in the repo of which shader actually resolves in-game. **Shader availability and transparent rendering are unverified.**
- **Logging convention:** `Plugin.Log` (`ManualLogSource` named `MEL`, `Plugin.cs`) with `LogInfo`/`LogWarning`/`LogError`; config via `base.Config.Bind(section, key, default, description)` in `Plugin.Awake`; raid detection via `Singleton<GameWorld>.Instance` (`RuntimeStaticObjectSpawner`).

### Diagnostic code added (opt-in, default off)
- **New file** `Client\DecalFeasibilityProbe.cs` (`internal static class DecalFeasibilityProbe`, coroutine `Run()`).
- **Edit** `Client\Plugin.cs`, end of `Awake`: binds `[Diagnostics] EnableDecalFeasibilityProbe` (default `false`, separate from any future `EnableDecalPrototype`) and starts `DecalFeasibilityProbe.Run()` only if true.
- **Behaviour when enabled:** waits 10 s after plugin load and runs once (stage `startup`), then waits for `Singleton<GameWorld>.Instance` and 5 s and runs once more (stage `raid`). Each run logs, with prefix `[M0]`:
  - Unity version, `SystemInfo.graphicsDeviceType`, `graphicsShaderLevel`;
  - which `UnityEngine.Image*`/`UnityEngine.UnityWebRequest*` assemblies are loaded;
  - whether `UnityEngine.ImageConversion` (in `UnityEngine.ImageConversionModule`) and `UnityEngine.Networking.UnityWebRequestTexture` (in `UnityEngine.UnityWebRequestTextureModule`) resolve via `Type.GetType`;
  - if `ImageConversion.LoadImage(Texture2D, byte[])` is found (reflection), loads an embedded 1x1 PNG (base64 in the source) into a temporary `Texture2D` and logs return value, size, format, `isReadable`, and pixel (0,0);
  - for `Sprites/Default`, `Unlit/Transparent`, `Unlit/Texture`, `Unlit/Color`, `Standard`, `Hidden/Internal-Colored`, `UI/Default`, `Legacy Shaders/Particles/Alpha Blended`, `Particles/Standard Unlit`: `Shader.Find` result, `shader.isSupported`, and `renderQueue` and `HasProperty` for `_MainTex`, `_Color`, `_Mode`, `_ZWrite`, `_SrcBlend` on a temporary `Material`.
- **Cleanup in code:** the temporary `Texture2D` and every temporary `Material` are destroyed in `finally` blocks. No `GameObject`, `Renderer`, or scene object is created or modified. The probe does **not** draw anything, so it cannot confirm visual correctness of transparency, only presence and `isSupported`.

### Build result
- `dotnet build -c Release -p:SPTPath=C:\nonexistent-spt -o <temp>` in `Client\`: **fails, as expected, because no SPT install is available** (about 2,000 lines of output, dominated by `MSB3245` unresolved-reference warnings and `CS0246` missing-type errors for `Comfort`, `EFT`, `UnityEngine`, etc.). Every error in the new file is the same missing-assembly cascade; no independent syntax error was reported, but this is **not** a real compile check. The temp output folder was deleted; `BepInEx\plugins\MapEditorLite.Client.dll` is unchanged (still dated 2026-10-08 15:00:08). Pre-existing: the project cannot build here without `-p:SPTPath` pointing to an install. `Client\obj` (and `bin`) already existed and were regenerated by the build attempt.
- **A real build has not been done.** Owner needs to build with a real `SPTPath`.

### Runtime test result
- **Not run.** No game is available to me. All runtime questions below remain unverified.

### Confirmed vs unverified
| Item | Status |
|---|---|
| MEL logging/config/raid-detection conventions | Confirmed (source) |
| `Client.csproj` lacks `ImageConversionModule`/`UnityWebRequestTextureModule` references | Confirmed |
| MEL already uses `Shader.Find`/`new Material` at runtime (no crash evidence either way) | Confirmed in source only |
| `ImageConversionModule` exists in EFT `Managed` / loaded at runtime | **Unverified** (probe) |
| `ImageConversion.LoadImage` decodes PNG in SPT 4.1.x | **Unverified** (probe) |
| `UnityWebRequestTexture` available | **Unverified** (probe) |
| `Sprites/Default`, `Unlit/Transparent`, `Standard` findable and supported in-game | **Unverified** (probe) |
| Transparent material renders correctly on a quad in a raid | **Unverified**, not covered by M0 |
| Probe compiles | **Unverified** (needs real SPT references) |

### Manual test steps for the owner (the assistant will not deploy)
1. Back up the current `BepInEx\plugins\MapEditorLite.Client.dll` from the SPT install.
2. Build: in `Client\`, `dotnet build -c Release -p:SPTPath="<SPT install root>"`. Report any compile error verbatim (especially from `DecalFeasibilityProbe.cs`).
3. Copy only `Client\bin\Release\MapEditorLite.Client.dll` to `<SPT>\BepInEx\plugins\`.
4. Start the game once, close it, then edit `BepInEx\config\com.shaneeexd.mapeditorlite.cfg` and set `[Diagnostics]` `EnableDecalFeasibilityProbe = true`. (`EnableEditor` can stay false.)
5. Launch, wait at the main menu for about 20 s, then start an offline raid and wait about 15 s.
6. Send back all lines containing `[M0]` from `BepInEx\LogOutput.log`.
7. Roll back: set the flag to false and/or restore the backed-up DLL.

### Interpretation guide for the results
- `ImageConversion type: NOT FOUND` → plan must switch to the asset-bundle texture route or to `UnityWebRequestTexture` if that resolves.
- `LoadImage returned True` with 1x1 size → PNG loading works; adding the module reference to `Client.csproj` (a separate, approved step) would then be justified for typed use.
- A transparent-capable shader that is found with `isSupported=True` (`Sprites/Default` or `Unlit/Transparent` preferred) → proceed to the visual quad test in M2; if none, use a shader shipped in an asset bundle.

### Next recommended milestone
Owner runs the manual steps above and returns the `[M0]` log. Based on it, decide the PNG loader and shader, then proceed to M1 (pick with hit data). Not started.

---

## M0 release build verification (2026-10-11)

**Scope:** build and static verification only. **No DLL was deployed, no installed game file was modified, and no in-game test was performed.**

### Build configuration inspected
- `Client\Client.csproj`: net471, `AssemblyName` `MapEditorLite.Client`, all game references use `$(SPTPath)`; no `Directory.Build.props` applies to `Client\` (the only one is under the separate `UltimateLootEditor-main\`). No `SPTPath` environment variable or per-user config exists. Unchanged by this task.
- The only SPT path recorded in this project's history is `E:\SPT_TRADER_QUEST_MAKER_4.1.X` (earlier changelog build note, "0 errors"). It was verified on disk rather than assumed: the folder exists and contains `BepInEx\core\0Harmony.dll`, `BepInEx\core\BepInEx.dll`, `BepInEx\plugins\spt\spt-reflection.dll`, `BepInEx\plugins\WTT-ClientCommonLib\WTT-ClientCommonLib.dll`, and every `EscapeFromTarkov_Data\Managed\` assembly the csproj lists. (The earlier note that no SPT install existed came from a search of `C:\` and `D:\` only; the install is on `E:\`.) A second folder `E:\SPT 4.1.X` also exists and was not used.
- Side finding: that Managed folder **does contain** `UnityEngine.ImageConversionModule.dll` and `UnityEngine.UnityWebRequestTextureModule.dll` (file presence only; not yet shown to load or work at runtime). This resolves the "module may be missing" part of assumption A1; the runtime behaviour is still unverified.

### Build
- Command (run in `Client\`): `dotnet clean -c Release -p:SPTPath=E:\SPT_TRADER_QUEST_MAKER_4.1.X` then `dotnet build -c Release -p:SPTPath=E:\SPT_TRADER_QUEST_MAKER_4.1.X --no-incremental`
- **Result: Build succeeded, 0 errors, 60 warnings.** (SDK 10.0.102, target net471 unchanged.)
- Warnings: all `CS0649` (never-assigned fields, mostly JSON DTOs) and `CS0169`, in existing files (`VanillaImporter.cs`, `RuntimeInteractiveObjectSpawner.cs`, `ItemNameResolver.cs`, etc.). **None from `DecalFeasibilityProbe.cs` or `Plugin.cs`.** Not suppressed. They are pre-existing in kind; I did not run a baseline build without the probe, so "pre-existing" is inferred from the files and warning types, not measured.
- No source change was needed; the previous dummy-path failure was purely missing references. The probe compiled as written.

### Output
- Path: `C:\Users\ifunH\Desktop\MapLootEditorLite-main\Client\bin\Release\net471\MapEditorLite.Client.dll` (built 2026-10-11 16:13)
- Size: 19,076,608 bytes (includes the embedded vanilla location data)
- SHA-256: `CBD5E008DC4E1BF218C6FFED7317CAC1AFBE6E4F7E70A74A04850B0A9AF563F5`
- The SHA-256 of the DLL currently installed at `E:\SPT_TRADER_QUEST_MAKER_4.1.X\BepInEx\plugins\MapEditorLite.Client.dll` is `9E7A6F5688F99DE464D80F7265DCCC27A59BF1E94F72EC3D3BE6A20625801155` (different, as expected; read only, not touched).
- The old `BepInEx\plugins\MapEditorLite.Client.dll` in this repo folder (dated 2026-10-08) was not modified.

### Diagnostic configuration check (source plus built DLL)
- `[Diagnostics] EnableDecalFeasibilityProbe` is bound in `Plugin.Awake` with default `false`; probe starts only when true. Separate from any future `EnableDecalPrototype` (not created). Confirmed in source; the DLL contains the type `DecalFeasibilityProbe`, the config key and the `[M0]` strings (byte-level string search).
- Probe creates only a temporary `Texture2D` and `Material`s, destroyed in `finally` blocks; no `GameObject`/`Renderer`, no access to world materials. All logs use the `[M0]` prefix. Confirmed by reading the source; not run.
- No discrepancies found.

### Not verified
PNG decoding, shader availability, transparency rendering, and in-game probe behaviour. These need the runtime test.

### Manual steps for the owner (assistant does not deploy)
1. Close the game. Back up: copy `E:\SPT_TRADER_QUEST_MAKER_4.1.X\BepInEx\plugins\MapEditorLite.Client.dll` to a name outside the plugin scan (for example the `.dll.backup-...` naming already used there) .
2. Copy the built DLL (above) over `E:\SPT_TRADER_QUEST_MAKER_4.1.X\BepInEx\plugins\MapEditorLite.Client.dll`.
3. Launch once to the main menu, then exit. Edit `E:\SPT_TRADER_QUEST_MAKER_4.1.X\BepInEx\config\com.shaneeexd.mapeditorlite.cfg` and set `EnableDecalFeasibilityProbe = true` under `[Diagnostics]`.
4. Launch, wait about 20 s at the main menu, start an offline raid, wait about 15 s.
5. Send the `[M0]` lines from `BepInEx\LogOutput.log`.
6. Roll back: set the flag to `false`, and/or restore the backed-up DLL.

### Next action
Owner performs the manual test and returns the `[M0]` log. M1 not started.

---

## M0 runtime results — decal feasibility probe (2026-10-11)

The owner supplied `[M0]` logs from both startup and an active raid. The results were identical at both stages:

- Unity `2022.3.43f1`, Direct3D11, shader level 50.
- `UnityEngine.ImageConversionModule` and `UnityEngine.UnityWebRequestTextureModule` were loaded. Both `UnityEngine.ImageConversion` and `UnityEngine.Networking.UnityWebRequestTexture` resolved at runtime.
- The embedded 1x1 PNG decoded successfully using `ImageConversion.LoadImage(Texture2D, byte[])`: returned `True`, produced a readable `1x1 ARGB32` texture, and `GetPixel(0,0)` returned `RGBA(0.000, 0.000, 1.000, 0.498)`.
- Shader results: `Sprites/Default` found and supported, queue 3000, `_MainTex` and `_Color`; `Unlit/Transparent` not found; `Unlit/Texture`, `Unlit/Color`, `Standard`, `Hidden/Internal-Colored`, `UI/Default`, `Legacy Shaders/Particles/Alpha Blended`, and `Particles/Standard Unlit` found and reported supported. `Legacy Shaders/Particles/Alpha Blended` is queue 3000 and has `_MainTex`; `Sprites/Default` is the best-supported initial candidate for a transparent sprite material based on the reported shader name, support, queue, and properties.
- Runtime PNG decoding is confirmed for this embedded test image. The texture module files and APIs are confirmed present/loaded. Since the client project does not yet reference `ImageConversionModule`, direct typed API use in future code still needs a deliberate project-reference change; the probe's reflection path avoided that change.
- Shader lookup and Unity's `isSupported` are confirmed in the target runtime, but actual visible transparency on a world-space quad is **not** confirmed: the probe created no renderer or scene object. Shader property names/queue alone do not guarantee the required visual result.

**Status:** M0 feasibility probe completed. No source, project, pack, or game asset changes were made as part of runtime testing; no additional build was run for these supplied logs. No DLL deployment was performed by the assistant. M1 (world-object picking) has not started.

**Next action:** proceed only with owner approval to M1. Keep the first later rendering check as a disposable in-memory quad using `Sprites/Default` (or the supported legacy alpha particle shader), with isolated material/texture resources and teardown; visually verify alpha blending, orientation, depth/z-fighting, and both sides in-game before relying on the shader for any feature.

---

## M1 implementation — world surface picker with hit data (2026-10-11)

**Scope:** extend the existing GameObjects scene-pick interaction to emit read-only surface diagnostics. No new editing UI, decal placement/rendering, texture or material modification, persistence, `MapData` change, or game-asset change. The new code creates no Unity objects.

### Files changed
- `Client\\WorldSurfaceHit.cs` (new): internal transient result record. Keeps `HitGameObject`, `HitCollider`, `HitTransform` distinct, plus the selected/target GameObject, hit point/normal, triangle index, associated renderer, mesh, and that renderer's `sharedMaterials`. It is runtime-only and is not serialized.
- `Client\\CustomEditorUI.cs`: added `TryPickWorldSurfaceHit`, `TryResolveSurfaceRenderer`, `IsUsableSurfaceRenderer`, `IsCharacterOrLooseLoot`, `AddRejectionReason`, `LogWorldSurfaceHitAtMouse`, and `LogWorldSurfaceHit`. Uses the existing camera ray, `IsEditorObject`, `IsValidPick` / `ShouldIgnorePick`, `_pickUseParent` convention, and existing path builder. Existing `PickSceneObjectAtMouse` logic and its behavior for its other callers are unchanged.
- `Client\\MapEditorController.cs`: in the existing `_ui.IsPickingSceneGO` click handler, calls `LogWorldSurfaceHitAtMouse()` alongside the existing scene object pick. Existing selected GameObject behavior remains unchanged; invalid/ambiguous M1 surface candidates are reported rather than being presented as valid surface hits.

### Capture, filtering, and diagnostic behavior
- On an explicit click while using the existing GameObjects **Pick from Scene** interaction, a separate `Physics.RaycastAll` probe (100 m, default raycast layers, `QueryTriggerInteraction.Ignore`) sorts hits nearest-first. It captures the original collider and collider transform separately from the `GameObject` currently used as the editor's selected object. If `_pickUseParent` is enabled, that selection target follows the existing immediate-parent behavior.
- Rejects trigger colliders (also excluded by query mode); MEL-prefixed/editor preview objects and player-named objects via `IsEditorObject`; character/loose-loot candidates by component-type names containing `EFT.Player`, `BotOwner`, `ObservedPlayer`, or `LootItem`; existing configurable scene-pick path filters; candidates without an enabled, active-in-hierarchy, non-force-hidden renderer with at least one non-null shared material; and ambiguous renderer associations where multiple child renderers exist but none is on the hit transform's ancestor chain.
- The nearest eligible renderer is selected from the hit transform through the selected object's transform; if none is on that chain, a unique usable child renderer can be associated. The result captures renderer mesh from `MeshFilter.sharedMesh`, `SkinnedMeshRenderer.sharedMesh`, or (as fallback) `MeshCollider.sharedMesh`; captures `sharedMaterials` without instantiating materials; and keeps `RaycastHit.triangleIndex` as unavailable when Unity reports a negative index.
- If distinct selected objects have eligible hits within 1 mm of the nearest hit, the probe reports ambiguity and does not emit a surface-hit record. Reject reasons are bounded to three, processing stops beyond the nearest eligible hit plus 1 mm, and diagnostics are click-triggered/rate-limited (repeat same object within 2 s suppressed; all M1 logs at least 1 s apart).
- `[M1]` logs include selected/hit object identity and hierarchy path, collider type/trigger/layer, world point/normal, triangle availability, hit transform, selected target root position/rotation/lossy scale, renderer type/path, mesh name/vertex/submesh/readability details when available, each shared material name/shader, and `WorldInteractiveObject.Id` when available. Missing values are explicitly logged as `unavailable`.
- The component-name exclusions are defensive heuristics, not a complete semantic taxonomy of every EFT dynamic object. They must be checked against real target objects and logs. This diagnostics probe does not change the legacy selection result, so an existing scene pick may still select an object that M1 rejects as an unsuitable surface.

### Build and verification
- Used the previously verified real SPT path `E:\\SPT_TRADER_QUEST_MAKER_4.1.X`; all project references exist there. Command in `Client\\`: `dotnet clean -c Release -p:SPTPath=E:\\SPT_TRADER_QUEST_MAKER_4.1.X`, then `dotnet build -c Release -p:SPTPath=E:\\SPT_TRADER_QUEST_MAKER_4.1.X --no-incremental`.
- First compile caught one new `CS1503` at `CustomEditorUI.cs` (passed `Transform` to the existing `GameObject` path helper); corrected to pass `hit.Renderer.gameObject`. The clean final build then succeeded: **0 errors, 60 warnings**.
- Final warnings: 118 `CS0649` (never-assigned DTO fields) and 2 `CS0169` (unused fields), all in existing files such as `VanillaImporter.cs`, `RuntimeInteractiveObjectSpawner.cs`, `RuntimeNavMeshBuilder.cs`, and `ItemNameResolver.cs`. None from `CustomEditorUI.cs`, `MapEditorController.cs`, or `WorldSurfaceHit.cs`. These warning categories and files were present before this edit according to prior M0 build history; no baseline build was repeated for this M1 change.
- Output: `Client\\bin\\Release\\net471\\MapEditorLite.Client.dll`, 19,085,824 bytes, SHA-256 `9FC60041DA521467C599746E7F7B53D52E1A538CAA2D9715D99669920180A0D8`.
- Build output was not copied or deployed by the assistant. No in-game test has been run for M1; only compilation is confirmed.

### Object identity assumptions / limits
- Hierarchy paths, names, transforms, material slots, and `WorldInteractiveObject.Id` are diagnostic evidence only, not a durable identity contract. The presence and uniqueness of `WorldInteractiveObject.Id` vary by object type. No Unity instance ID is saved or treated as persistent.
- Duplicate objects can still share names/paths across raids; current M1 reports current-session hit facts only and does not resolve an object after a restart. M1 does not claim the captured path or transform uniquely identifies a future object.
- Renderer association is intentionally conservative: multiple possible child renderers cause rejection rather than silent selection. The component-name filters can miss differently named dynamic objects or overmatch unusual types; validate in-game.

### Manual in-game test (owner deploys; assistant does not)
1. Close the game and back up `E:\\SPT_TRADER_QUEST_MAKER_4.1.X\\BepInEx\\plugins\\MapEditorLite.Client.dll`.
2. Copy `Client\\bin\\Release\\net471\\MapEditorLite.Client.dll` to `E:\\SPT_TRADER_QUEST_MAKER_4.1.X\\BepInEx\\plugins\\MapEditorLite.Client.dll`.
3. Start a raid with `EnableEditor=true`, press F8, open the **GameObjects** tab, choose **Pick from Scene**, and click stationary examples: a wall, container, fuel tank (including a flat face), and a door/interactive object. Also try an object with multiple renderers, loose loot, a character, an editor preview, and a trigger-only target if available.
4. Check `E:\\SPT_TRADER_QUEST_MAKER_4.1.X\\BepInEx\\LogOutput.log`. Collect `[M1]` accepted-hit lines and rejection/ambiguity warnings. Repeat a click on the same object within two seconds to confirm throttling.
5. Return the logs, noting the map and target object. Do not infer cross-raid identity from this test alone.
6. Roll back by restoring the backed-up DLL. No config flag was added for M1.

### Remaining before M2
Run the manual test and review actual hit/renderer/filter data. Resolve any false rejections or incorrect renderer associations first. M2 would be a disposable, in-memory quad preview only after this evidence is satisfactory; it is not implemented here.

### M1 runtime-log review and picker correction (2026-10-11)

The owner tested the first M1 build in-game and provided these results:
- Normal MEL picker reported `Pipes_pile_01 (5)`, `Wood_box_01_closed`, `Electric_box_06`, `tank_plits`, and `UAZ_buhanka_off`.
- The separate M1 raycast diagnostic reported `tank_plits`' child collider and `road_factory4` instead, with hit points on the ground plane (`y` about `-0.60` or `1.12`) and upward normals. It also associated the tank collider with a renderer/material named `plits_ground_4` / `Custom/Vert Paint Shader Solid`, and the road hit with `Custom/Vert Paint SoftCutout Decal`.
- **Conclusion:** the initial diagnostic's second raycast could disagree with the GameObjects selection, so those first logs do not prove what object was selected. The mismatch likely arose from independently querying the mouse/raycast and allowing an eligible hit behind the actual selection; exact runtime cause remains unknown. This surfaced a genuine M1 correctness issue. Renderer data is useful but not evidence of decal compatibility.

**Correction made:** the existing `PickSceneObjectAtMouse` now has an overload returning the exact `RaycastHit` used to choose its selected GameObject. The existing GameObjects click passes that hit and selected object into M1 diagnostics; M1 no longer performs a second raycast or independently chooses a candidate. It rejects/logs if the collider is not in the selected object's hierarchy, trigger, editor/player, character/loot, filtered, or lacks a usable/non-ambiguous renderer. The legacy picker still returns the same selected GameObject and existing callers use the original wrapper unchanged. Modified: `Client\\CustomEditorUI.cs`, `Client\\MapEditorController.cs`; no decal/rendering/persistence work.

**Final corrected M1 build:** clean Release build against `E:\\SPT_TRADER_QUEST_MAKER_4.1.X`, using `dotnet clean -c Release -p:SPTPath=E:\\SPT_TRADER_QUEST_MAKER_4.1.X` and `dotnet build -c Release -p:SPTPath=E:\\SPT_TRADER_QUEST_MAKER_4.1.X --no-incremental`. **0 errors, 60 existing warnings** (118 CS0649 and 2 CS0169; none in changed/new files). Output `Client\\bin\\Release\\net471\\MapEditorLite.Client.dll`, 19,085,312 bytes, SHA-256 `EA843C7DEB00621AAAF931E9528DE5F808A89EC945EEA64ADC26110A521AC7CC`. Not deployed by the assistant; no new runtime test has been performed after this correction.

**Retest requested before M2:** back up the installed plugin DLL, deploy the corrected build manually, repeat picks on stationary objects, and send `[M1]` logs. For every accepted record, `selected=` and `hitGO=` should be the same object or have an ancestor/descendant relationship consistent with the selected hierarchy; the emitted hit/collider should now be from the exact RaycastHit used by MEL's normal picker. Picks rejected as non-surface should produce `[M1] World surface pick rejected...`, not a surface record for a different object. Restore the backup after testing if needed. Until this retest confirms correct object association and acceptable rejection behavior, do not start M2.

### M1 corrected-build runtime results — renderer ambiguity (2026-10-11)

The owner supplied four corrected-build results:
- `Vagon_tank` and `Vagon_tank_red` each hit a child collider named `Vagon_tank_BALLISTIC_metalthick`; both were rejected because the selected object contains 12 enabled child renderers and the hit collider's transform ancestry did not identify a unique renderer.
- A GUID-named object was rejected because its selected collider was a trigger.
- `Pillar_concrete_rough_01 (4)` hit `Pillar_concrete_rough_01_BALLISTIC_Concrete` and was rejected because the renderer association was ambiguous (2 enabled child renderers).

**Assessment:** this is consistent with the M1 safety requirement: trigger colliders and unresolved renderer relationships are rejected rather than silently assigned to a potentially incorrect mesh. The `selected` and `hit` names correspond to the selected object and its collider child; unlike the first run, no different unrelated world surface was reported. This confirms the corrected picker path is behaving as designed for these cases. It does **not** establish that the collider belongs to the exact visible mesh among the sibling renderers, nor that any renderer is suitable for decals. No eligible surface-hit record has yet been demonstrated.

No source changes, build, deployment, or M2 work were performed in response to these logs. Continue M1 validation by trying a plainly simple static object whose collider is on the same transform as its visible renderer, or where only one usable renderer exists under the selected object. If all practical map assets use separate ballistic colliders and multiple renderers, the next bounded investigation should map collider-to-renderer relationships using read-only diagnostics (candidate renderer hierarchy paths and types); do not relax the ambiguity rejection until that mapping is supported by runtime evidence. Record one accepted `[M1]` surface result and at least one intentional rejection before considering M1 complete.

### M1 additional result — Loader collider (2026-10-11)

The owner tested `Loader_01_off`; the existing scene picker selected it, while the exact selected raycast hit was child `Loader_01_BALLISTIC_metalthin`. M1 rejected it because there are 27 enabled child renderers and no unique association. This reinforces that target props commonly use dedicated ballistic collider objects and numerous visual renderers. The safety rejection is functioning, but currently prevents diagnostics from accepting these representative vehicle/loader assets.

No source change, build, deployment, or M2 work was performed. Do not relax the ambiguity check based only on this result. Next, collect renderer hierarchy paths and nearest renderer/collider ancestor/descendant relationships for this collider and its selected parent (read-only), ideally along with the Vagon and pillar cases. Then determine whether there is a principled collider-to-renderer mapping or if the correct M1 scope must report a candidate set as unresolved. An accepted simple-object result is still needed before M1 completion.

### M1 additional runtime results — repeated collider/renderer ambiguity (2026-10-11)

The owner supplied more logs:
- `Pillar_concrete_rough_01 (4)` → hit `Pillar_concrete_rough_01_BALLISTIC_Concrete`, 2 enabled renderers.
- `Loader_01_off` → hit `Loader_01_BALLISTIC_metalthin`, 27 enabled renderers.
- `eurocube_skeleton_01` → hit `eurocube_skeleton_01_COLLIDER`, 2 enabled renderers (three separate clicks).
- `eurocube_update` → hit `eurocube_COLLIDER`, 7 enabled renderers.
- `autoloader_rezerv` → hit `autoloader_COLLIDER`, 19 enabled renderers.

Every item was rejected as an ambiguous collider-to-renderer association. The repeated static-prop/container cases confirm that merely relying on a unique renderer ancestor or a single renderer under the selected object is not a practical assumption for representative SPT assets. The ambiguity guard should remain; selecting the nearest renderer by name or distance without evidence risks attaching a surface edit to the wrong visual part.

**M1 diagnostic extension added:** when an association is ambiguous, the rejection now includes each candidate renderer (up to 32) with hierarchy path, renderer type, relationship to the hit transform (`same`, `ancestor-of-hit`, `descendant-of-hit`, or `separate-branch`), mesh name if accessible, distance from the hit point to the renderer's world-space bounds, and up to four material/shader names. This is read-only inspection; it does not choose a renderer or change scene objects/materials. No scene-wide scans or per-frame logging. This should provide evidence for the collider/render hierarchy mapping in the next runtime pass.

**Build:** clean Release build against `E:\\SPT_TRADER_QUEST_MAKER_4.1.X` succeeded with 0 errors and 60 warnings (118 CS0649, 2 CS0169; none in changed/new files). Output `Client\\bin\\Release\\net471\\MapEditorLite.Client.dll`, 19,086,336 bytes, SHA-256 `A012EAD9D93614767A043A40156D67B5F414FD54168B25C4BC3C9C0EECD73028`. Not deployed by the assistant and not yet tested in-game.

**Next test:** after backing up and manually deploying that exact DLL, repeat one representative pick each for the loader, pillar, eurocube, and autoloader. Collect the full `[M1]` rejection lines (including candidate summaries). The bounds-distance field is only a diagnostic hint, not proof that a mesh surface corresponds to the collider hit. Do not proceed to M2 until candidate mapping is understood and a valid surface association strategy is supported by the output.

### M1 candidate renderer diagnostic results — LOD and shadow overlap (2026-10-11)

The owner supplied the candidate-enriched output for a VAZ car, old fuel pump, Cruze wheel, green rail tanker, and old green barrel. The records consistently identify dedicated hit colliders (`VAZ_2109_COLLIDER`, `colider`, `Cruze_wheel_01_COLLIDER`, `Vagon_tank_COLLIDER`, `Metal_barrel_04_Closed_COLLIDER`) and list visual candidates on separate child branches.

Evidence from the candidate data:
- The VAZ reported 21 candidates, including paired `LOD0`/`LOD1`, glass, and `SHADOW_LOD` renderers. Its main body LOD candidates have bounds distance `0.000`, as do some other candidates; the glass bounds are about `0.016` away.
- The gas pump reported 8 candidates. Main pump and its LOD and shadow versions all had `0.000` bounds distance; metal components were about `1.094` away.
- The Cruze wheel reported its visual `LOD0` and `SHADOW_LOD0`, both on separate branches and both effectively overlapping the hit point (distances `0.000` and `0.003`).
- The green tanker reported body LOD0/LOD1 at `0.000`, shadow LODs also at `0.000`, and distant wheel shadow candidates.
- The barrel reported visual LOD0 and shadow LOD0, both at `0.000`.

**Conclusion:** the diagnostic confirms why a simple bounds-distance choice would be unsafe: LOD variants and shadow renderers can overlap the same collider hit, and several candidates have identical zero-distance scores. Names/materials identify likely visual vs shadow candidates in these examples but are heuristic evidence, not a stable runtime selection contract; current output does not include each renderer's `forceRenderingOff`, `LODGroup` membership/current LOD, or a verified collider-to-render mesh mapping. Keep ambiguous associations rejected; do not select the first, closest, or non-shadow-named renderer by guess.

This output does show the raycast hit location and normal are present for the exact collision surface, independently of renderer selection. Since the proposed M2 technique is a separate world-space overlay and does not edit a mesh or material, a later prototype might not need a unique renderer at all if the collider hit can be validated against the visible object's geometry. That is a **design possibility, not yet approved or verified**: decal alignment on a ballistic collider could still be wrong, and no quad was drawn. Before deciding whether M1 is complete or M2 can proceed, obtain hit point/normal/collider details for these same picks (the current supplied attachment starts at the picked name and rejection; it contains candidate summaries) and, if needed, add a read-only diagnostic for LODGroup/current renderer states or compare against visible surfaces in-game.

No code change, build, deployment, or rendering work followed these logs. The last verified DLL remains the M1 candidate-diagnostic build recorded above.


## Configurable boss follower counts (2026-10-12)

### Investigation (decompiled installed SPT: Assembly-CSharp + SPTarkov.Server.Core, `E:\SPT_TRADER_QUEST_MAKER_4.1.X`)
- MEL's existing Reshala preset (`bossBully`, Savage/Boss) is spawned by `RuntimeBotSpawnSpawner` through `BotCreationData.Create(...)` + `BotSpawner.TryToSpawnInZoneAndDelay(..., forced)`. A point spawn used a bare `BotSpawnParams` (no `ShallBeGroup`), so `SetBoss` never ran and Reshala had **no followers** (not even vanilla 3-4).
- SPT's native system: `BotBossSpawn.SpawnBossAndFollowers` sets `ShallBeGroup = new ShallBeGroupParams(true, true, followers + 1)`, spawns the boss, then `SpawnBossSupports` creates N profiles of the follower role with the *same* `BotSpawnParams` and calls `TryToSpawnInZoneAndDelay(zone, data, false, true, points, force)`. The count comes from `BossEscortAmount` (parsed to `EscortCount`); there is **no engine cap** on it.
- `BotSpawner.method_10` decrements `ShallBeGroup.RemainCount` per bot; `ActivateBotCallback` calls `SetBoss(StartCount)` once for the first bot activated (hence followers are submitted only after the boss exists). `GetGroupAndSetEnemies`: followers join the boss's non-full `BotsGroup` in the zone. `BotCreatorClient` accepts fewer positions than profiles (reuses). `PatrolPoint.GetSubPoint` clamps, so >4 followers is safe. Forced spawns skip the `MaxBots` check.
- Server: `BotController` generates `Math.Max(PresetBatch[role], condition.Limit)` profiles, so a client request for 10 is honored (not limited by `presetBatch`, e.g. followerBully 5).
- `BotCreationData.Create` skips null profiles, so the actual count can be lower than requested (handled/logged).
- SAIN, MoreBotsPlugin binaries contain no references to ShallBeGroup/SetBoss/follower-count APIs (string scan only, not a behavioral test).

### Implementation
New fields on `BotSpawnPoint` and `BotSpawnZone`: `followersEnabled` (default false), `minFollowers` (0), `maxFollowers` (0). Old saves/presets deserialize to the defaults -> identical legacy behavior.
- Runtime (`RuntimeBotSpawnSpawner.cs`): `GetFollowerType` (enabled + sanitized range + role map + random count), `MapFollowerType` (bossBully->followerBully, bossSanitar->followerSanitar, bossKojaniy->followerKojaniy, bossZryachiy->followerZryachiy, bossBoar->followerBoar), `SpawnBossWithFollowers`. Flow: shared `BotSpawnParams` with `ShallBeGroup(true,true,n+1)`; boss submitted with the **unchanged** forced spawn call; wait (<=120 s) for the boss via `BotSpawner.OnBotCreated` (profile id match); then create `n` real follower profiles and submit them forced at the boss point; log requested/submitted/actual and, after 30 s, the boss's `Followers.Count`, `TargetFollowersCount` and `BotsGroup.MembersCount`. Followers disabled / 0 rolled / unmapped boss -> old code path exactly. Zones with followers enabled spawn each boss individually (each its own group); others still use the old grouped call.
- Validation: UI clamps min>=0, max>=min; runtime sanitizes and warns.
- UI: in-game inspector (`CustomEditorUI.cs`) toggle + Min/Max Followers for points and zones; web Tool (`MarkerLists.tsx`, `types.ts`) same.
- Not changed: Reshala preset/enum, point/zone grouped spawn path, PMC zones, other spawn code.

### Files changed
`Client\MarkerTypes.cs`, `Client\RuntimeBotSpawnSpawner.cs`, `Client\CustomEditorUI.cs`, `Server\PackData.cs`, `Tool\src\types.ts`, `Tool\src\MarkerLists.tsx`, `CHANGELOG_SESSION.md`.

### Build (code/build verification only)
`dotnet build -c Release -p:SPTPath=E:\SPT_TRADER_QUEST_MAKER_4.1.X` (Client): 0 errors, 60 warnings (all pre-existing CS0649 style; none in changed code). Tool `tsc --noEmit`: clean. Server project not built separately (two property-only additions).
DLL: `Client\bin\Release\net471\MapEditorLite.Client.dll`, SHA256 `E14E2F3212906EE3580C1796205A3461D43907A3D9B90B30F7BB3E839DD7D148`. **Not deployed.**

### Testing status
**NOT tested in game.** Only compiled/type-checked.

### Limitations / unresolved
- Boss roles without a count-based follower role (Gluhar, Knight, Kolontay, Killa, Tagilla, Partisan) log a warning and spawn without followers.
- If a vanilla boss group in the same BotZone is already full, followers may form their own groups (vanilla quirk).
- Followers spawn on the boss point (repeated); a crowd of 7-10 at one point may look stacked or be repositioned by the spawn system - unverified.
- Raid-wide bot caps / spawn delays unverified for forced follower spawns; backend may fetch profiles sequentially (brief delay for 10).
- Followers are submitted after the boss is created, so they appear shortly after Reshala.

### In-game test plan for Serenity (Reshala point, Customs, Forced)
1. Followers off (or Max 0): Reshala spawns alone as before (log has no "followers" lines).
2. Enabled, Min 3, Max 4: log `Submitted boss bossBully with N requested followerBully`, then `Submitted N followerBully followers`; after ~30 s `Boss group check ... followers=N target=N+1? groupMembers=N+1`. Check they follow Reshala, move and fight together.
3. Enabled, Min 7, Max 10: same, N in 7-10; watch for warnings about profile count shortfall or boss timeout. Confirm no unrelated scavs appear and no duplicate followers (vanilla Reshala wave not active on the same map/zone).
Next: report log lines back; if followers fragment or stack, consider spreading follower points.

## Boss spawn failure: generic Boss preset with empty wildSpawnType (2026-10-12)

### Failure logs
`Forcing spawn for 1 custom bot requests` / `Updated core point for e6c3aed4-... to 11` / `Boss preset has no explicit wildSpawnType (entry id=e6c3aed4-...)` / `Skipping spawn request with unknown wild spawn type`. Same with followers off and Min 3/Max 4.

### Confirmed findings
- Warning emitted by `RuntimeBotSpawnSpawner.ResolveWildSpawnType` (preset == Boss and `BotSpawnPresetMapping.GetWildSpawnType` returns ""), then `SpawnBotAt` logs the second warning. This happens before any follower code, so followers are not involved.
- The live pack `E:\SPT_TRADER_QUEST_MAKER_4.1.X\SPT_Runtime\user\mods\MapEditorLite\packs\Nolan_Quest_Pack.json` (saved 06:08) contains exactly one point, id e6c3aed4-..., with `preset: "Boss"`, `wildSpawnType: ""`, category Boss, followersEnabled true, min 3, max 4. So the entry is the *generic* "Boss (generic)" preset, **not Reshala**, and the empty role is already in the saved file (missing at editor/save time, not lost in load).
- The server (`Server\PackData.cs`) is only a data model; no server code consumes or validates wildSpawnType, and there is no network spawn request: the client reads the pack JSON from disk. Not a client/server contract failure. Field names/types for the follower fields match in client, server and Tool; old packs load with defaults.
- My follower change did not touch preset selection, `ResolveWildSpawnType`, or the forced-spawn call/`BotSpawnParams` for the solo path. The Reshala preset still maps to `bossBully` and selecting it in the in-game editor (`ApplyPreset`) or the Tool (`presetChange`) fills it.
- The in-game inspector had **no Wild Type field** for points/zones, so a generic Boss entry could never be given a role in-game.

### Hypothesis (not provable from available data)
The entry was created with "Boss (generic)" rather than "Reshala (Bully)" (or picked from the dropdown/Tool by mistake). No git history/backups exist (not a git repo) and the earlier working Reshala pack is no longer on disk, so how the previous working entry selected bossBully is unverified; code indicates preset Reshala -> "bossBully".

### Changes (no behavior fallback added; validation kept)
- `Client\CustomEditorUI.cs`: editable "Wild Type" field for bot spawn points and zones, plus a warning label when preset is generic Boss with blank type.
- `Client\RuntimeBotSpawnSpawner.cs`: diagnostic `[MEL diag]` warning on missing role (id, preset, stored wildSpawnType, followersEnabled) and an info line per request (id, preset, resolved role, followersEnabled). Spawn logic unchanged; no assault fallback; no hardcoded Reshala.
- Server and Tool unchanged this round.
- Data not modified: the pack must be fixed by selecting "Reshala (Bully)" (or Wild Type `bossBully`) on the point.

### Builds (build verification only)
- Client: 0 errors, 60 warnings (pre-existing). `Client\bin\Release\net471\MapEditorLite.Client.dll` SHA256 7A0B0625541E097889A02A3006E43CE88066574C36443FE542CF465F8C90E1AF
- Server (unchanged code, rebuilt for check): 0 errors, 3 warnings. `Server\bin\Release\net10.0\MapEditorLite.Server.dll` SHA256 0516F5469A20428DD890493B0380BD618FE010BC03617831D5D4C7EFB5510506
- Nothing deployed.

### In-game verification (NOT done)
1. Edit the point: preset "Reshala (Bully)" (Wild Type shows bossBully), followers disabled. Expect `[MEL diag] Spawn request ... role=bossBully` and Reshala appearing, moving, fighting.
2. Followers Min 3/Max 4: expect boss + follower submit logs, group check ~30s later (followers/target/groupMembers); confirm they follow and fight with Reshala.
3. Only then Min 7/Max 10.
Unresolved: follower behavior still untested in game.

## Missing Reshala followers: diagnosis and diagnostics (2026-10-12)

### In-game results reported
Reshala solo: PASS. Reshala + followers Min 3/Max 4: FAIL (boss spawns, no followers).

### Evidence from `E:\SPT_TRADER_QUEST_MAKER_4.1.X\BepInEx\LogOutput.log` (confirmed)
- `Submitted boss bossBully with 4 requested followerBully followers ...` -> settings were read, count 4 rolled, follower role followerBully selected, boss request submitted.
- `Boss bossBully was not created within 120s; followers were not spawned.` -> **exact failing step: MEL's wait for the boss (`SpawnBossWithFollowers`, `OnBotCreated` match by profile id, 120 s timeout) ended without a match, so the follower profile generation/submission code never ran.** Nothing after the wait (profile generation, submit, group) executed; no exception was logged.
- The deployed DLL was the earlier follower build (hash E14E2F32...), not the diag build.
- The log has no timestamps, and the boss request was submitted during map loading (before "Raid start signal received" and several 30 s interactive-object warnings), so a 120 s timer started at submission can plausibly expire before the raid starts and the spawner creates the boss. Alternative: the OnBotCreated profile-id match failed. **Which of these is true is NOT proven**; the user observed Reshala spawning, but not when.
- Native SPT comparison (from earlier decompile, `BotBossSpawn.SpawnBossAndFollowers`): vanilla sends followers from the boss spawn callback (`SpawnBossMain` callback/`OnBotStateChange`), not a timer; same shared `BotSpawnParams` with `ShallBeGroup(true,true,n+1)`. MEL mirrors that structure but triggers on `OnBotCreated`.

### Change (diagnostics + removal of the suspect timeout; no pipeline rewrite)
`Client\RuntimeBotSpawnSpawner.cs` `SpawnBossWithFollowers` only:
- All steps log with `[MEL followers]`: start (id, boss, role, rolled count, time), boss profile id, every `OnBotCreated` (role, profile id, whether it matches the boss, time), boss request submitted (labelled request-only), wait progress every 30 s, boss obtained (profile, role, state, group id/members, IamBoss, ShallBeGroup remain/IsBossSetted), follower generation requested/returned counts, per-follower creation (group id, same group as boss), follower request submitted (request-only), final check after 30 s (followers created, boss Followers/Target, group members).
- Boss wait extended from 120 s to 900 s with progress logs. Follower logic, role mapping, solo path, validation unchanged. No server changes.

### Build (build verification only)
Client: 0 errors, 60 warnings (pre-existing). `Client\bin\Release\net471\MapEditorLite.Client.dll` SHA256 D989FED11D0A50B4E1C5C670D6DC33EA725C7033A4480D03EEA5FE62E8299F43. Server unchanged, not rebuilt. **Not deployed.**

### In-game status
Followers NOT verified. Retest (Min 3/Max 4) after deploying the new DLL; send all `[MEL followers]` lines:
- If `OnBotCreated ... matchesBoss=True` appears and followers follow: timeout was the cause.
- If Reshala's `OnBotCreated` appears with matchesBoss=False: profile-id match is the cause (use role/ShallBeGroup instead).
- If no OnBotCreated for Reshala at all but he appears: event not firing for this path; need another hook.
- Check `sameGroupAsBoss` and the final check counts.
Do not try 7-10 until 3-4 spawn.

## Boss identity mismatch and missing followers: investigation (2026-10-12, run at 07:01)

### Evidence (confirmed)
- Log `BepInEx\LogOutput.log` (07:01-07:08 run). MEL request id e6c3aed4..., rolledCount=3, our boss profile generated = `...f501`, submitted 07:01:56.
- `OnBotCreated` bossBully `...f4ff` at 07:02:00 and 4 x followerBully (`f6aa, f6ab, f6ad, f6ae`) at 07:02:02, all `matchesBoss=False`. MEL's "Follower profile generation started" never appeared, so MEL generated none of them. Our `f501` was never created in 330 s+ of waiting (pmc bots were created meanwhile).
- `SPT_Data\database\locations\bigmap\base.json`: `bossBully`, BossChance 39, BossZone `ZoneDormitory,ZoneGasStation,ZoneScavBase`, BossEscortAmount 4, BossEscortType followerBully. MEL's point is in `ZoneGasStation`.

### Findings
1. `OnBotCreated` (decompiled `BotSpawner.ActivateBotCallback`) fires after `ActivateBot` returned a real BotOwner and it was added to `_bots`. It is invoked before `AfterCreation`/`SetBoss`, so it means "BotOwner instantiated and registered", not "active and visible/initialised". It does not mean a profile was generated or a request submitted. Do not treat it as proof of a working group.
2. Profile ID mismatch is NOT an ID regeneration. SPT does not rewrite profile ids; `f4ff` and `f501` are two different profiles. MEL's identity check is correct. `f4ff` (generated just before our `f501`) belongs to a different request: SPT's native Reshala wave (confirmed: matching role, count 4 = BossEscortAmount, zone list contains ZoneGasStation, 39% chance). The comparison cannot be replaced by a role match without risking attaching MEL followers to the vanilla boss.
3. The four followerBully events came from SPT's native `BotBossSpawn.SpawnBossAndFollowers` (strong inference from the config + log; direct call path not logged). Not MEL (no MEL follower code ran), count 4 is vanilla escort amount not our rolled 3.
4. Implication: **the "Reshala spawns" observed in the failing follower test, and possibly in earlier solo tests, may have been the vanilla Reshala (39% roll), not MEL's boss.** This is a hypothesis; it is NOT verified that MEL's own solo spawn ever created a bot (earlier logs only show "Submitted", not OnBotCreated).
5. Why MEL's `f501` never got created is NOT proven. `BotSpawner.TryToSpawnInZoneInner` silently queues the request (SimpleBotSpawnDelayModel, retried later WITHOUT forcedSpawn and WITHOUT our spawn point) if `_botCreator.StartProfilesLoaded` is false, if `CanAtZoneByType` is false, or later retries fail zone `HaveFreeSpace`. MEL submits during BotsController init (before raid start). Hypotheses: queued because start profiles were not loaded, then starved because the zone is full (vanilla Reshala + 4 followers there). Needs the new diagnostics. (Decompiled from the installed Assembly-CSharp; temporary decompile output deleted.)
6. Native comparison: vanilla sets ShallBeGroup(true,true,n+1) and submits boss then followers from the boss path; MEL's design mirrors that but depends on MEL's own boss existing first. Native `SpawnBossAndFollowers` takes a BossLocationSpawn wave with a server-defined escort amount; it was not adopted because it would need a synthesized wave and isn't verified yet. Recommendation deferred until diagnostics prove the queueing cause.

### Change (diagnostics only; no runtime behaviour change)
`Client\RuntimeBotSpawnSpawner.cs`: added `DescribeSpawnerState` (startProfilesLoaded, SpawnDelaysService.WaitCount, _inSpawnProcess, allBots, SpawnStopped, zone HaveFreeSpace(1)) logged before submit, after submit, and with every "Still waiting" line; added `DescribeBotOwner` (instance id, position, distance from MEL point, BotState, group id, members, IamBoss) appended to every `[MEL followers] OnBotCreated` line. Matching logic, timeout (kept at 900 s), solo path, validation unchanged. Server unchanged.

### Build (compile verification only)
Client: 0 errors. `Client\bin\Release\net471\MapEditorLite.Client.dll` SHA256 751BEB7054E4F668525724A2C624D27F29F417B430617BB796E276412607063C. Server not changed or rebuilt. NOT deployed (deployed DLL is the previous diag build D989FED1...).

### In-game status
Followers still NOT verified. MEL's own boss creation is unverified.

### Retest (Min 3 / Max 4)
1. For a clean test, avoid the vanilla Reshala: set bigmap bossBully BossChance to 0 (or retry until none spawns) and note it in the log; optional but otherwise results are ambiguous.
2. Deploy the new DLL manually; run Customs with the quest active.
3. Send the `[MEL followers]` lines. Read: `AFTER boss submit` delayedWaitCount increased => queued by BotSpawner (check startProfilesLoaded/zoneFreeSpace); inSpawnProcess increased => activation began, look for an OnBotCreated with matching profile; `distToMelPoint` ~0 for the matched boss; follower lines should show `sameGroupAsBoss=True`.
Do not try 7-10 until 3-4 followers are visible.

### Next steps for Serenity
Read the new state lines; if queued, submit after `StartProfilesLoaded` (or raid start) and re-check forced behaviour; consider guarding against the vanilla boss duplicate; only then re-evaluate native SpawnBossAndFollowers.

## Follow-up: MEL's Reshala DID spawn, but OnBotCreated never reported him (2026-10-12, run 07:23)

### Evidence (confirmed)
- Boss `bossBully` profile `6accc341fc940e6fac6a5489` submitted 07:23:58; SPT accepted it (`startProfilesLoaded=True`, `inSpawnProcess` 2->3, not queued). No vanilla Reshala event appeared.
- The user later killed a Reshala at the MEL point (Gas Station), alone, with no guards. So MEL's solo boss does spawn and the earlier solo PASS is real.
- MEL's `OnBotCreated` handler logged NO bossBully/followerBully event for the whole run (only 3 pmc events despite ~19 live bots). So `BotSpawner.OnBotCreated` is not a reliable signal for this boss in this mod stack (cause unknown; `OnBotCreated = null` is only set in `BotSpawner.Dispose`; another mod/patch is a hypothesis, not verified). MEL therefore waited forever and never ran follower code -> boss alone. This is the confirmed reason followers did not appear.
- Not confirmed: that the killed Reshala's profile id equals `...5489` (not logged). Position + absence of escorts + no vanilla event strongly suggests it is MEL's.

### Change (`Client\RuntimeBotSpawnSpawner.cs`, follower path only)
- Boss wait loop polls every 1 s `GameWorld.AllAlivePlayersList` for an AI player whose `Profile.Id` equals the generated boss profile id (`FindLiveBotByProfileId`); exact id match, so the vanilla Reshala can never be adopted. OnBotCreated still works as before if it fires. On a live match the existing follower code runs unchanged (same group params, native `TryToSpawnInZoneAndDelay`).
- New diagnostics: log line "Boss found via live world lookup", and the 30 s waiting line lists all live boss*/follower* bots with profile id and distance to the MEL point (`DescribeLiveBossBots`), so an id mismatch would be visible.
- Server unchanged. Solo path, validation, timeout (900 s) unchanged.

### Build (compile verification only)
Client 0 errors. DLL `Client\bin\Release\net471\MapEditorLite.Client.dll` SHA256 A7A2764745B0328DE2B301F4BFC74E5198052B6AD842EF71D4ADF10DF7C6574B. NOT deployed.

### In-game status
Solo: boss confirmed spawning (user observation). Followers: still NOT verified. Retest Min 3/Max 4 and look for "Boss found via live world lookup", then "Follower profile generation completed", "Follower created ... sameGroupAsBoss", and the "Final check" line (bossFollowers/groupMembers). If the boss is not found while he exists, the waiting line's liveBossFollowerBots list shows his actual profile id.

## Custom bot location behaviour: followers leave the Gas Station (2026-10-12)

### Status
Followers now spawn (user-confirmed; log 07:38 shows boss found by live lookup at distToMelPoint=0.0m). They then move toward the road. **Cause NOT yet established; no behaviour fix implemented.** Only read-only instrumentation was added.

### Examined
`Client\RuntimeBotSpawnSpawner.cs` (CreateSpawnMarker, UpdateMarkerCorePoint, SpawnBotAt, SpawnBossWithFollowers), `Client\MarkerTypes.cs` (BotSpawnPoint/BotSpawnZone), `BepInEx\LogOutput.log`, installed plugins/mods, decompiled `BotOwner`, `BotZone`, `PatrollingData`, `StandartBotBrain`, `PatrolWay`, `BotPointControl`.

### Verified
- A spawn point only chooses WHERE the bot is created. `BotOwner.PreActivate(zone,...)` stores `SpawnBotZone`; afterwards movement comes from the bot's `PatrollingData` (patrol modes incl. bossRoundProtect, bossCoverScouts, bossStayAtPlaces, follower; `PatrolWay`s belong to the `BotZone`, here ZoneGasStation) and from the active brain. Nothing in MEL's CorePointId/spawn-point fields constrains movement. Followers use the follower patrol mode (`PatrolPointChooserFollower`) tied to the boss, so they follow the boss's choices.
- Boss spawned exactly at the MEL point (dist 0.0 m).
- Installed AI-affecting mods: SAIN (+Solarint server mod, replaces brains/layers), DrakiaXYZ-BigBrain, DrakiaXYZ-Waypoints (custom patrol/waypoints), MoreBotsPlugin. Any of these, or the vanilla ZoneGasStation patrol ways (which may route along the road), could be driving the movement. Which one is NOT established.
- MEL has `radius` on points/zones (spawn scatter only) and no operating-area/leash setting.
- Native location restriction: BotZone patrol ways and `BotLocationModifier.LeaveDist` (despawn distance) exist; neither is a guard/leash mechanism suitable as-is. No fix chosen.

### Change (diagnostics only, no behavioural change)
`Client\RuntimeBotSpawnSpawner.cs`: `TrackMelBotMovement` + `DescribeMovement`. Called after the boss is found (follower path) and after the solo submit. Finds the leader by exact profile id and, every 10 s for 300 s, logs `[MEL movement]` for the leader and every bot sharing its `BotsGroup`: position, distance from MEL point, `Mover.TargetPoint` and its distance, SAIN/BigBrain active layer (`Brain.ActiveLayerName`), brain state, patrol active/status/way name, group id, IamBoss, spawn zone and group zone. Reads only; never moves bots; vanilla bots are not tracked (only the leader's group).

### Build (compile verification only)
Client 0 errors. DLL `Client\bin\Release\net471\MapEditorLite.Client.dll` SHA256 1AAA6CAC0DBE764A66BC108E474B292A74DA135C1A4ABF8E9FC595AE2DA8330A. Server unchanged. NOT deployed.

### Runtime testing: NOT done. Issue NOT fixed.

### Gas Station test
Deploy manually, run Customs with followers 3-4 and quest active, stay out of the area (or watch from distance) and send the `[MEL movement]` lines. Read: `layer` (SAIN/BigBrain layer name vs vanilla), `way` (name of the vanilla patrol way and whether it is the road), `dest`/`destDist` (where they are going), whether leader and members share the same layer, and when `dist` starts growing. Also try the same with SAIN disabled (and separately Waypoints) to see which changes the behaviour. Check that bots still fight when engaged.

### Next steps for Serenity
Based on the layer/way data pick one: (a) vanilla patrol way of ZoneGasStation -> choose a different/own BotZone or restrict patrol ways for MEL bots only; (b) SAIN layer/personality (e.g. wander/search) -> use SAIN's per-bot settings API if available, scoped to MEL bots; (c) Waypoints mod. Then add an optional per-entry `operatingRadius` (default 0 = current behaviour) to BotSpawnPoint/BotSpawnZone, mirrored in Server\PackData.cs and Tool types.ts/MarkerLists.tsx, applied only to bots MEL spawned.

## Operating zones feature: read-only investigation (no code changed)

**Status: NO source, project, config or deployed DLL was changed. NO build was run. NOTHING was deployed.** Only decompilation into session temp files (deleted).

### Files / methods examined
- Client: MarkerTypes.cs (BotSpawnPoint, BotSpawnZone), RuntimeBotSpawnSpawner.cs (CreateSpawnMarker, ResolveBotZoneName, zone scatter, SpawnBotAt, SpawnBossWithFollowers, TrackMelBotMovement), CustomEditorUI.cs (Radius fields), MarkerRenderer.cs (CreateBotSpawnPointVisual, CreateBotSpawnZoneVisual, ApplyZoneScale).
- Server/PackData.cs, Tool/src/types.ts (radius mirrors).
- SPT Assembly-CSharp: BotZone.PatrolWays, PatrolWay, PatrolPointChooserBasic/Boss/Follower, PatrollingData, BotMover, BotsGroup, BotGoToPointData.
- Mods (decompiled, read-only): BigBrain BrainManager (AddCustomLayer(type, brains, priority, roles), RemoveLayer/ExcludeLayer), Waypoints (WaypointPatch, FindPathPatch), SAIN layer class list.

### Confirmed
- `radius` = spawn geometry only (point: spawn collider; zone: random scatter radius*scale). Must not be reused.
- An optional `operatingRadius` (0 = off/current behavior) can be added to BotSpawnPoint/BotSpawnZone, PackData and types.ts; old packs deserialize with default (same pattern as follower fields). Editor: inspector field + second wire sphere in MarkerRenderer.
- MEL bots are assigned to the nearest vanilla BotZone (ResolveBotZoneName). SPT patrol choosers use BotsGroup.BotZone.PatrolWays, so MEL bots inherit that zone's vanilla ways (incl. AltPatrol_outside_azs3). The BotZone is shared with vanilla bots: mutating PatrolWays is not acceptable.
- Followers' PatrolPointChooserFollower takes the boss's way/points, so followers follow boss destinations.
- Waypoints (decompiled): patches BotsController zone init (adds navmesh data) and BotPathFinderCorePoints.FindPath. No custom patrol/leash API; no "AltPatrol" strings found in it (origin of that way name is unverified, likely vanilla/zone data).
- BigBrain supports adding custom layers per brain name AND per WildSpawnType role with a priority, and excluding layers: a role-scoped layer would only affect MEL roles... but role-scoping also hits vanilla bots of the same role (e.g. vanilla bossBully), so a per-bot activeWhen check (MEL-spawned profile-id set) is required.
- SAIN has its own combat layers (CombatSoloLayer, CombatSquadLayer, SAINAvoidThreatLayer, etc.) that take control in combat; observed in logs.
- Earlier run (08:35): bots stayed 5-31m (follower once 80m) with PatrolAssault / Patrol.reserve layers; cause of earlier run-away to road NOT established.

### Hypotheses (unverified)
- A MEL-only BigBrain layer, priority just above patrol but below SAIN combat, could redirect to a local point when the current patrol target is outside radius.
- BotMover.TargetPoint can be read for log-only checking; SAIN may override destinations in non-combat states.
- SPT point-filter delegate in FindNextPoint could be applied per MEL bot only; reachability per-bot not confirmed.

### Options
1. Leave as is / author picks different zone: zero risk, not configurable.
2. MEL BigBrain leash layer (active only for MEL-tracked bots, outside combat, when target/position beyond operatingRadius): no teleport, no per-frame fight if it only acts on exit-of-radius; needs BigBrain dependency and priority check vs SAIN.
3. Patrol-point filter for MEL bots only: native, light; depends on being able to reach each bot's chooser.
4. Rejected: mutating BotZone.PatrolWays (affects vanilla), teleporting, per-frame GoToPoint.
- Rules: combat overrides leash; followers bounded via boss; regroup via follower chooser inheritance; enemies outside zone ignored for patrol only.

### Smallest experiment
Log-only (extend TrackMelBotMovement): per MEL bot log Mover.TargetPoint, patrol way name, distance from spawn, active layer, flagged when beyond candidate radius (e.g. 40m) for a Gas Station run; no behavior change. Then optionally a MEL-only filter at fixed radius.

### Diagnostics needed first
Origin of AltPatrol_outside_azs3 (vanilla vs mod), SAIN destination logic in non-combat, per-bot chooser access, live followerBully group IDs/count.

### Open / deferred
Only 1 of 3 followers appears in boss group: separate investigation next. Do not combine.

### Next steps for Serenity
Run the log-only experiment (needs a small approved code change), review results, then choose option 2 or 3.

### Addendum: source scoping of Waypoints and SAIN (read-only; still no code changed, no build, nothing deployed)
Sources read: C:\Users\ifunH\Downloads\SPT-Waypoints-master (patches, components) and C:\Users\ifunH\Downloads\SAIN-master (Plugin\BigBrainHandler.cs, Layers\SAINLayer.cs, Layers\Combat\Solo\CombatSoloLayer.cs, BotAction/BotDecisionManager patrol-pause call sites).
Confirmed:
- Waypoints patches only: BotsController zone init (navmesh), BotPathFinderCorePoints.FindPath, door links/exfil door blockers, debug. It never reads or writes BotZone.PatrolWays (only commented-out debug code does). "AltPatrol" occurs in neither source: AltPatrol_outside_azs3 is vanilla map/zone data, NOT added by Waypoints or SAIN. Waypoints therefore cannot be the source of patrol destinations; it can only change how paths are computed (FindPath prefix) - effect on the observed route is not proven.
- SAIN registers BigBrain layers for normal bosses/followers (brain lists): DebugLayer 99, FlashedLayer 85, AvoidThreatLayer 80, CombatSquadLayer 70, CombatSoloLayer 69 (fixed). Everything below 69 (vanilla patrol layers) is only reached when SAIN layers are inactive.
- SAIN layers call BotOwner.PatrollingData.Pause() when they activate and BotMover.Stop(); when switching away they reset BotMover state (SetPlayerToNavMesh) so SPT patrol resumes cleanly. SAIN does not override patrol destinations outside combat; it only takes over via its layers (SearchAction / MoveToEngage can pull bots far from home during combat).
- BigBrain AddCustomLayer(type, brains, priority, roles) is the mechanism; a MEL layer placed below 69 and above vanilla patrol would not conflict with SAIN combat and would run only when SAIN is idle.
Implication: option 2 (MEL BigBrain leash layer, priority <69, active only for MEL-tracked profile IDs, only when not in combat and bot/target beyond operatingRadius) is now better supported; hypotheses remaining: exact vanilla patrol layer priorities, BigBrain role-scoping hitting vanilla bots (so IsActive must check MEL-tracked set), and whether combat "search" drift needs a separate limit.

## Follower count investigation (only 1 of 3 followers in boss group): diagnostics added, NOT fixed

**Status:** one read-only diagnostic added to `Client\RuntimeBotSpawnSpawner.cs`. Client build: 0 errors, 60 warnings (none in RuntimeBotSpawnSpawner.cs). DLL `Client\bin\Release\net471\MapEditorLite.Client.dll` SHA256 `5120780B31DAA51B5B924F600B0822C02283774DAFA245644F13B25B5B67DBDA`. **NOT deployed, NOT tested in game.** (Deployed plugin is still `1AAA6CAC...`.) The issue is NOT fixed.

### Examined
RuntimeBotSpawnSpawner.cs (SpawnBossWithFollowers, TrackMelBotMovement, FindLiveBotByProfileId, DescribeBotOwner); LogOutput.log (08:35 run) and Player.log; SPT decompiled: BotSpawner (TryToSpawnInZoneAndDelay, TryToSpawnInZoneInner, method_7, method_10, ActivateBotCallback, AfterCreation, GetGroupAndSetEnemies), BotCreatorClient (ActivateBot, method_0, CreateBot, method_3), BotCreationData, ShallBeGroupParams, BotZoneGroups (Group, AddBot), BotsGroup (AddMember, IsFull, TargetMembersCount), BotBoss/BotBossFollowers (SetBoss, UpdateFollowers), BotBossSpawn (SpawnBossAndFollowers, SpawnBossSupports, SpawnEscort).

### Confirmed facts
- MEL submits all follower profiles in ONE BotCreationData and ONE TryToSpawnInZoneAndDelay call, exactly like vanilla SpawnBossSupports/SpawnEscort (same shared BotSpawnParams, forced spawn, points repeated per follower). MEL is not structurally different from native here.
- SPT's submit call returns nothing (void); it does not confirm spawns. method_10 runs once per data object (decrements ShallBeGroup.RemainCount once: log 3 -> 2 after follower submit) and ActivateBot -> method_0 then loops all profiles sequentially with `await CreateBot`; each bot gets its group in method_3 via GetGroupAndSetEnemies. ActivateBot is fire-and-forget, so failures inside it are not surfaced to MEL.
- Group membership is added in AfterCreation (BotZoneGroups.AddBot -> AddMember) which runs inside ActivateBotCallback AFTER OnBotCreated.Invoke; boss.SetBoss runs last in ActivateBotCallback. In the 08:35 run: boss group=5 had members=0 when found, final groupMembers=2 (boss + follower ...242); boss and follower both reported iamBoss=False for the whole 300s; no OnBotCreated line was ever logged for MEL bots (followersCreated=0). So ActivateBotCallback's tail (SetBoss, TargetFollowersCount) evidently did not take effect as in vanilla, while membership did update. Why is unproven.
- Only profile ids for the boss were logged; follower ids were never logged, and MEL's tracker lists only members of the BOSS's group object. So the log cannot say whether the other 2 followers spawned elsewhere/in another group.
- Player.log shows 6 unrelated NullReferenceExceptions (BloodOnScreen, CameraManager); none from spawning.
- No other plugin patches ActivateBotCallback/GetGroupAndSetEnemies (string scan); MoreBotsPlugin and spt-debugging subscribe to OnBotCreated.

### Hypotheses (unproven)
1. The other 2 followers spawned but in separate groups (so untracked by MEL).
2. They never spawned (sequential CreateBot loop aborted/cancelled, silently).
3. A subscriber on OnBotCreated (other mod) interrupts the callback before AfterCreation/SetBoss for some bots (would explain iamBoss=False and no MEL handler output).
4. Membership/tracking only: followers exist in the boss group but spawn later than the 30s final check.

### Diagnostic change
`ReconcileFollowers` (read-only): logs requested follower profile ids at submit, then at t+2/5/10/20/40/60s looks each id up in the live world and logs alive, role, position/distance, state, group id/members, sameGroupAsBoss, plus boss IamBoss/Followers/TargetFollowersCount, nearby boss/follower bots and spawner counters. No change to generation, requests, timing, grouping, boss detection, solo path or timeout.

### Retest (Gas Station, Reshala, Min 3 / Max 4) - Serenity
Deploy the new DLL manually, spawn, send LogOutput.log. Read `[MEL followers] Requested follower profile ids` then `Reconcile` lines:
- "NOT in live world" for a requested id at t+60 => it never spawned (spawn failure; hypothesis 2).
- foundLive=3 but inBossGroup<3 with different group ids => group assignment (hypothesis 1).
- foundLive=3, inBossGroup=3 => the earlier problem was tracking/timing only (hypothesis 4).
- bossIamBoss=False with Followers 0/0 => SetBoss/ActivateBotCallback tail not running (hypothesis 3); investigate OnBotCreated subscribers next.
Do not test 7-10 until resolved.

### Follower reconcile result (09:12 run, diagnostic DLL 5120780B...; user-deployed and tested)
Evidence (LogOutput.log): requested followers ..9c36, ..9c35, ..9c38 (boss ..9a87, group 2). At t+2s through t+60s only ..9c36 exists in the live world (alive, sameGroupAsBoss=True, at the MEL point, Active from ~t+20s). ..9c35 and ..9c38 were "NOT in live world" at every checkpoint. Boss group members=2 (boss + 1 follower), bossIamBoss=False, bossFollowers=0/0 throughout. No spawn-related exceptions in LogOutput.log or Player.log (only unrelated BloodOnScreen/CameraManager NREs; one SAIN init error for a PlayerScav bot, unrelated).
Confirmed: this is a SPAWN failure, not group assignment or tracking. Profiles 2 and 3 never became live bots; the one that did is in the correct group. Earlier "groupMembers=2" was accurate.
Not yet proven why: BotCreatorClient.method_0 creates profiles sequentially (await CreateBot) inside a fire-and-forget task; the loop stopping after the first bot would match the evidence (as would an exception thrown inside the first bot's creation callback chain, e.g. from an OnBotCreated subscriber, which also fits iamBoss=False because SetBoss is the last step of ActivateBotCallback). Unverified. No code changed since the diagnostic build; nothing newly deployed by me.
Next steps for Serenity: next diagnostic = log the first-bot callback chain (subscribe to OnBotCreated for the follower data to see whether it fires for ..9c36 and catch exceptions; check which mods subscribe to BotSpawner.OnBotCreated: MoreBotsPlugin, spt-debugging) and BotCreationData.Profiles/_positions counts after submit. Then decide a fix. Not fixed.

## SPT BotCreatorClient.method_0 / follower creation investigation (READ-ONLY)
**No code was changed. No build was run. Nothing was deployed.** Deployed DLL remains SHA256 5120780B...67DBDA (the diagnostic build).

### Examined
SPT 4.1 Assembly-CSharp (ilspycmd): BotCreatorClient (method_0, method_1, CreateBot, ActivateBot, method_3), BotSpawner (TryToSpawnInZoneAndDelay, method_7, method_10, ActivateBotCallback, AfterCreation, GetGroupAndSetEnemies), BotCreationData (Create, GetPosition, AddPosition, Profiles), GetProfileDataParams, ShallBeGroupParams, ProfileSettings.TryChangeRoleToAssaultGroup, BotBossSpawn.SpawnBossAndFollowers/SpawnEscort. MEL: RuntimeBotSpawnSpawner (GetFollowerType, SpawnBossWithFollowers, ReconcileFollowers), MarkerTypes, CustomEditorUI, Server PackData.cs, Tool types.ts/MarkerLists.tsx. LogOutput.log 09:12 run.

### Confirmed facts (source)
- method_0 = `foreach (profile in data.Profiles)` { cancel/SpawnStopped check -> return; pos = data.GetPosition(); if pos != null: `await CreateBot(...)` }. Strictly sequential; one profile at a time. A null position silently skips that profile.
- CreateBot: awaits _playerCreator (loads/instantiates player), BotOwner.Create, then `callback(botOwner)` synchronously (=> method_3: PreActivate with group from GetGroupAndSetEnemies; then BotSpawner.ActivateBotCallback: _bots.Add, OnBotCreated?.Invoke, AfterCreation, callback, _inSpawnProcess--, and SetBoss for first bot of a ShallBeGroup data).
- Order inside one bot: world registration (player created) -> BotOwner.Create -> PreActivate/group assignment -> OnBotCreated -> AfterCreation -> MEL callback -> SetBoss. OnBotCreated runs synchronously inside the loop, so an exception in any subscriber (or PreActivate/AfterCreation) propagates out of CreateBot and out of method_0, ending the loop; remaining profiles are never created.
- method_10 calls `_botCreator.ActivateBot(...)` WITHOUT await or HandleExceptions (method_7 itself has HandleExceptions, but it does not observe the un-awaited inner Task). Any exception in that chain is unobserved: no log, no retry. method_1 (single-profile path) does use HandleExceptions.
- No try/catch exists in method_0/CreateBot. No per-profile error recovery.
- method_10 calls ShallBeGroup.DescreaseCount() ONCE per BotCreationData (not per bot): boss 4->3, follower batch 3->2 (matches log). SetBoss goes to the first bot activated for a data whose ShallBeGroup.IsBossSetted is false; with MEL's shared params this can be a follower, explaining bossIamBoss=False / bossFollowers=0/0.
- Followers are not treated differently by method_0; only by GetGroupAndSetEnemies (reuses a boss group unless full; BotsGroup.TargetMembersCount=StartCount) and counters.
- Skip/queue/reject points: TryToSpawnInZoneInner queues when StartProfilesLoaded false, CanAtZoneByType false, or (non-forced) HaveFreeSpace/MaxBots/IsValidSpawn fail. MEL passes forcedSpawn=true so the free-space and MaxBots checks are bypassed. In method_0: SpawnStopped/cancel -> return; GetPosition()==null -> skip; _playerCreator returning null -> skip silently.
- Native vanilla path (BotBossSpawn.SpawnBossAndFollowers) is structurally the same as MEL: shared ShallBeGroupParams(true,true,followers+1), boss request, then one batched follower BotCreationData + TryToSpawnInZoneAndDelay(forceSpawn). MEL already uses the native workflow; it is not a different mechanism. Vanilla needs a BossLocationSpawn wave object; no verified way to drive it with a specific MEL profile/spawn point.
- Settings: followersEnabled/minFollowers/maxFollowers live in MarkerTypes.cs (BotSpawnPoint, BotSpawnZone), PackData.cs (defaults false/0/0, no validation server-side), Tool types.ts (+ MarkerLists.tsx clamps min>=0, max>=min), CustomEditorUI (same clamp, no upper bound). Runtime GetFollowerType sanitizes, rolls rng.Next(min,max+1), returns null for 0 (so min=0/max=0 -> no follower request, boss spawns alone; a rolled 0 also skips followers). randomSpawnTypes is resolved before role resolution at L304/L1358 for marker creation; with `assault` in the list on a boss point it could pick assault - not proven to affect the test (boss was bossBully), flagged as a risk.
- SPT limit found: GROUP_WAVE_SIZE_MAX=5 (BotGlobalsCoreSettings, used in BotSpawner ~L395 for wave group sizing). Not shown to be applied to MEL's forced path.

### Evidence from the 09:12 run
3 follower IDs requested, only ..9c36 ever live (alive, boss group 2). ..9c35 and ..9c38 never appeared through t+60s; no exceptions logged at plugin/Player.log level. Consistent with "loop ended after first bot", and also with "later CreateBots still awaiting/slow".

### Hypotheses (NOT proven)
1. An exception after the first bot's callback chain (OnBotCreated subscriber, PreActivate, AfterCreation, MEL callback, SetBoss) aborts method_0, swallowed by the un-awaited task. Fits the pattern; no direct evidence; no subscriber identified (MoreBotsPlugin, spt-debugging contain OnBotCreated strings - candidates only).
2. _playerCreator for profiles 2-3 stalls/returns null (silent skip).
3. GetPosition() returned null / data.SpawnStopped set (less likely; 3 positions are added).
Mutation of data.Profiles during the loop is NOT supported: the only RemoveProfile caller is BotCreationData.Separate, not used on this path.

### Native vs current
Native mechanism is the same pipeline MEL already uses; switching offers no gain and risks the working boss detection. Recommend staying with MEL's flow.

### Smallest distinguishing experiment (not implemented)
Read-only log additions: (a) log followerData.Profiles.Count and CountPositions right after Create/submit; (b) temporary OnBotCreated subscriber logging each follower profile ID at event time and the invocation count; (c) log the inSpawnProcess delta once the first follower is live (if it stays +2 above baseline, two creations are pending/lost); (d) Harmony postfix/finalizer on BotCreatorClient.CreateBot (or method_0) that logs profile ID before/after and any exception. (d) directly proves or disproves hypothesis 1. No behavior changes.

### Recommended plan (not implemented)
1. Run the diagnostic experiment above and identify the failing stage.
2. If an exception: fix/guard the cause; if due to another mod, report it. If silent skip: consider submitting followers as separate single-profile requests (method_1 path has HandleExceptions) - only after evidence.
3. Settings: keep min 0 (already disables followers); default max 10; add a MEL config/inspector cap (e.g., hard cap 10, opt-in 20) clamped in Tool, CustomEditorUI, Server PackData and GetFollowerType; warn that GROUP_WAVE_SIZE_MAX=5, per-bot sequential creation cost and bot count/perf scale with followers, and log any shortfall (requested vs live) via the existing reconcile.

### Next steps for Serenity
Approve the diagnostic experiment; no follower-count fix is claimed. Nothing built or deployed this task; in-game status unchanged (1 of 3 followers).

## Follower creation failure: exception instrumentation (log-only, diagnostic build)
**Status: built only. NOT deployed. NOT tested in game. The 1-of-3 follower problem is NOT fixed and its cause is still unproven.**

### Changes
- NEW `Client\FollowerCreationDiagnostics.cs`: temporary Harmony hooks (own id `com.shane.mapeditorlite.followerdiag`, applied once, guarded by a static flag) on BotCreatorClient.method_0, CreateBot, method_3 and BotSpawner.ActivateBotCallback, AfterCreation. Prefix/postfix only read; finalizers are void (original exception is rethrown unchanged, nothing is swallowed); async methods (method_0, CreateBot) are observed via a ContinueWith on the returned Task that only reads status/exception. Only profile IDs registered by MEL as requested followers are logged; vanilla bots are ignored. A per-profile "last stage" map is printed when method_0 ends.
- `Client\RuntimeBotSpawnSpawner.cs`: ApplyPatch calls FollowerCreationDiagnostics.Apply() after the existing patch; SpawnBossWithFollowers registers follower ids and logs "Pre-submit" and "Post-submit" lines (profile count, positions passed, data position count, spawnStopped, inSpawnProcess, allBots, delayedWaitCount). Follower generation, request, SetBoss, boss lookup, solo path, ReconcileFollowers unchanged.
- Limitation: a Task continuation marks a faulted method_0 task's exception as observed (suppresses only the UnobservedTaskException event). Behaviour is otherwise unchanged. method_0 PROFILE IDs are logged from data.Profiles at entry.

### Build
Client Release: 0 errors, 60 warnings (same as before; none from new code). DLL `Client\bin\Release\net471\MapEditorLite.Client.dll`, SHA256 `066C39FB0BB9DE1D0E06FD0A7BC78DDA607D5ED934E3D53CE1CD367758E77F43`. Previous deployed hash 5120780B...67DBDA. Not deployed.

### How to read the log (Gas Station, Reshala, Min 3 / Max 4)
Look for `[MEL followerdiag]` (and `[MEL followers] Pre-submit/Post-submit`).
- Failure before CreateBot: no `method_0 ENTER` listing the follower ids, or Pre-submit shows followerProfiles/positions < requested, or spawnStopped=True, or delayedWaitCount increased on Post-submit (request queued).
- method_0 entered but profile skipped: `method_0 ENTER` lists all 3, but a given id never gets `CreateBot ENTER` and `method_0 COMPLETED` (null position/cancel).
- Null result / incomplete creation: `CreateBot ENTER` then `CreateBot COMPLETED` with no `PreActivate ... ENTER` for that id (player null or cancelled; CreateBot returns silently).
- Exception aborting the loop: a `... THREW` line (PreActivate/callback, ActivateBotCallback or AfterCreation) or `CreateBot FAULTED`/`method_0 FAULTED` with the stack trace; ids after it show last stage 'requested'. The stage named in "last stage" and the stack identify which step (a stack through OnBotCreated delegates would point at a subscriber; do not blame one without it).
- Created but group problem: all three reach `ActivateBotCallback DONE`, and `[MEL followers] Reconcile` finds them live but sameGroupAsBoss=False / different group id.
- Tracking problem: ids reach DONE and Reconcile finds them live and in the boss group, yet MEL's bookkeeping (followersCreated, bossFollowers, groupMembers) is lower.
- If method_0 never finishes and no log after the first bot: creation still awaiting (player creator stalled): last stage 'CreateBot entered' for ids 2/3.

### Next steps for Serenity
Deploy manually, run the test, send LogOutput.log. Nothing is claimed fixed until the diagnostic output identifies the stage. No follower limits, ACID, operating zones or single-profile requests were touched.

## Multi-follower creation aborted by MoreBotsAPI: root cause found, fix built (NOT deployed, NOT tested in game)

### Confirmed (diagnostic log, 09:3x run)
method_0 ENTER profiles=4; first follower ..ecddd3 reached ActivateBotCallback, which THREW `NullReferenceException at MoreBotsAPI.Components.HuntManager.OnBotCreated (BotOwner) [0x0000b]`; PreActivate/callback THREW, CreateBot FAULTED, method_0 FAULTED; the other three stayed at 'requested'.

### Root cause (decompiled MoreBotsPlugin.dll, HuntManager)
`OnBotCreated(bot)` runs `bot.SpawnProfileData.SpawnParams.Id_spawn.ToLower().Contains("hunt")`. MEL created `new BotSpawnParams()` with `Id_spawn == null` (BotSpawnParams.Id_spawn is a plain public string, no default). The null at IL offset 0xb is Id_spawn (SpawnProfileData and SpawnParams are non-null). Vanilla `BotSpawner.ActivateBotsByWave` sets `Id_spawn = wave.TriggerId` (a string). So this is a MEL-side incompatibility, not a MoreBotsAPI bug per se (vanilla `new BotSpawnParams()` at BotSpawner L823/L392 would hit it too). HuntManager.InitRaid subscribes OnBotCreated at raid start, i.e. before any MEL handler.

### Why the bot is live before the throw
CreateBot awaits _playerCreator (player registered in GameWorld), BotOwner.Create, then method_3 -> PreActivate (group assigned via GetGroupAndSetEnemies) -> ActivateBotCallback. OnBotCreated is invoked inside ActivateBotCallback after `_bots.Add`. The exception leaves the bot alive and grouped, but SKIPS: later OnBotCreated subscribers (including MEL's handlers), AfterCreation (BotZoneGroups.AddBot / enemy setup), `_inSpawnProcess--`, and SetBoss. It propagates out of CreateBot and method_0 (no try/catch), and method_10 does not await/HandleExceptions the task, so it was invisible.

### Explains earlier mysteries (inference from code + logs)
- "OnBotCreated never fires for MEL's boss": MoreBots' subscriber threw first in the multicast, so MEL's handler was never reached; the live-world lookup workaround was compensating.
- `_inSpawnProcess` stuck high (12, then 20): each faulted creation leaked a decrement.
- Boss `iamBoss=False`/`bossFollowers=0/0`: SetBoss after the throw was never run for the boss.
- Affects EVERY MEL-spawned bot that goes through BotCreationData with a null Id_spawn (solo path L719 and zone group path L682 as well). Single-profile requests survive (bot lives) but with the skipped steps; multi-profile requests lose bots 2..N.

### Options
- A (fix MoreBotsAPI): rejected, third-party mod must not be modified. The only MEL-side equivalent is giving it the non-null value it expects; that is the chosen fix.
- B (separate single-profile requests): rejected. The exception would still happen on each bot (skipped AfterCreation, SetBoss, counter leak). ActivateBot(profile,...) (method_1) HandleExceptions only logs; it also does not assign bot.SpawnProfileData, so the shared ShallBeGroup/group config would not be carried. Not needed once the null is gone.
- C (global suppression/try-catch of OnBotCreated): rejected by constraints.

### Change (smallest)
`Client\RuntimeBotSpawnSpawner.cs`, `SpawnBossWithFollowers`: `spawnParams.Id_spawn = string.Empty;` (mirrors vanilla's untriggered TriggerId). "" does not contain "hunt" so HuntManager ignores MEL bots; PatrolWayWithConditions treats Id_spawn.Length<=0 as suitable; TriggerType stays default. Nothing else changed (boss lookup, follower generation, counts, groups, SetBoss, inspector, diagnostics kept).
- Expected side effect to watch: the boss callback chain now completes, so AfterCreation, SetBoss and OnBotCreated delivery will now happen for the boss (native lifecycle). SetBoss goes to the first bot activated from the shared ShallBeGroup (the boss), so iamBoss/bossFollowers should now read correctly. This changes the boss's initialisation relative to the previously "working" run; verify the boss still spawns, moves and fights.
- NOT changed (deliberately): solo path (L719) and zone-group path (L682) still pass null Id_spawn and will still trip HuntManager. Recommended follow-up once the follower fix is verified: set `Id_spawn = string.Empty` there too (zone groups lose members 2..N otherwise).

### Build
Client Release: 0 errors, 60 warnings (unchanged). DLL `Client\bin\Release\net471\MapEditorLite.Client.dll`, SHA256 `92867BF660C816922BABEFED2CF6061686BE45B5AF714058F88329FB0F51AEA1`. Not deployed. Previous: 066C39FB...77F43.

### Test plan (Reshala, Min 4 / Max 4, Gas Station)
Deploy manually. Expect: no `HuntManager` NullReferenceException; `method_0 ENTER profiles=4`, four `CreateBot ENTER`, four `ActivateBotCallback DONE`, `method_0 COMPLETED`; `[MEL followers] OnBotCreated` for the boss and `Follower created #1..#4`; Reconcile shows foundLive=4, inBossGroup=4, same group id; boss iamBoss=True, bossFollowers ~4; inSpawnProcess returns to baseline. Then confirm in game that Reshala and 4 followers move, stay grouped and fight, and that Reshala still spawns at the Gas Station.

### Status
Fix built, NOT verified. Code/build verification only; no in-game result yet. Next steps for Serenity: deploy, run the test, send LogOutput.log; then decide on applying the same Id_spawn fix to solo/zone-group paths and removing the temporary followerdiag hooks.

## Boss follower compatibility audit and hardening (all bosses)
**Status: built only. NOT deployed. NOT tested in game (except the Reshala baseline below).**

### Baseline (in-game verified earlier by the user)
Reshala spawns; 3 requested followers completed creation callbacks, all in group 5, sameGroupAsBoss=True (after the Id_spawn="" fix). Preserved: SpawnBossWithFollowers flow, exact-profile-ID boss lookup, shared ShallBeGroupParams, follower generation/submit, counts, timeouts, solo path.

### Audit (code + installed SPT data; no code changed during the audit)
Pipeline: SpawnBotAt -> ResolveWildSpawnType (explicit wildSpawnType or preset map; refuses to fall back to assault) -> GetFollowerType (followersEnabled, min/max, MapFollowerType) -> SpawnBossWithFollowers (boss BotCreationData + exact profile-id live lookup; one batched follower BotCreationData sharing the boss's ShallBeGroupParams; forced; Id_spawn="") -> ReconcileFollowers (exact profile-id checks). Follower association is by the shared ShallBeGroupParams + SPT's GetGroupAndSetEnemies, then verified by exact profile id/group reference; MEL never matches by role.
Vanilla escort data (SPT_Data locations base.json) vs MapFollowerType:
- Supported already: bossBully->followerBully (4), bossSanitar->followerSanitar (3), bossKojaniy->followerKojaniy (2-3), bossZryachiy->followerZryachiy (2), bossBoar->followerBoar (vanilla also adds followerBoarClose1/2 supports, not spawned).
- Gap fixed: bossGluhar (vanilla escort followerGluharSecurity x2, supports Assault/Security/Scout x2 each) and bossKolontay (escort followerKolontaySecurity x2, supports Assault/Security) had no mapping, so followers silently did nothing (logged "no count-based follower type"). Mapped to their vanilla primary escort role; mixed support roles are NOT spawned.
- Not supported (intentionally unchanged): bossKnight (escort role exUsec + supports followerBigPipe/BirdEye, mixed roles), bossTagilla (vanilla followerBully amt 0), bossKilla (followerTagilla amt 0), bossPartisan (no real followers). They keep the existing warning and spawn solo.
- No Reshala hardcoding found in the follower pipeline; only boss-keyed MapFollowerType. Log strings mention Reshala only as an example.

### Hazards found (documented, NOT changed)
1. SPT GetGroupAndSetEnemies picks an existing group by ROLE (BotZoneGroups.Group -> BotsGroup.IsSuitable(initialBot role)) when the bot is not yet IamBoss and the group is not full. If a vanilla boss group of the same type is open in the same BotZone at the same time, a MEL boss/follower could join it. Not observed; would need e.g. a vanilla Reshala spawning simultaneously in the same zone. Mitigation here is detection only (below); altering group assignment was out of scope.
2. Solo-boss path (L719) and zone-group path (L682) still pass a null Id_spawn, so MoreBotsAPI HuntManager.OnBotCreated throws for them (skips AfterCreation/SetBoss; zone groups lose members 2..N). Left untouched per "preserve solo". Recommended follow-up: set Id_spawn = string.Empty there too.
3. Followers for Boar/Gluhar/Kolontay are a single role only, unlike vanilla's mixed supports.

### Changes
`Client\RuntimeBotSpawnSpawner.cs` only:
- MapFollowerType: added bossGluhar->followerGluharSecurity, bossKolontay->followerKolontaySecurity.
- ReconcileFollowers: added a FINAL RECONCILE verdict by exact profile ids (live+alive, in the boss's own group, and any foreign member of the boss group listed). Logs RESULT=OK, or a WARNING RESULT=PARTIAL_OR_FAILED listing missing / not-in-boss-group / foreign ids. Read-only; no behavior change.
Missing profiles (follower generation returning fewer) were already handled (submitted vs requested logged; 0 => boss alone with a warning); the new verdict reports the shortfall instead of implying success.

### Build
Client Release: 0 errors, 60 warnings (unchanged, none new). DLL `Client\bin\Release\net471\MapEditorLite.Client.dll`, SHA256 `44759FDD7A5C2B8B1470A74276F033129A2ACAF52ECB72C73F651786E2449008`. Not deployed.

### Verified by code/build only vs needs in-game
- Code/build: compiles; Reshala code path untouched except the added read-only verdict; role mappings checked against the installed enum and vanilla database.
- Needs in-game: Reshala regression (3 followers, group, FINAL RECONCILE RESULT=OK); Gluhar (Reserve) and Kolontay (Streets) followers actually generate profiles for followerGluharSecurity/followerKolontaySecurity and join the right group; Sanitar/Kojaniy/Zryachiy/Boar not tested this session.

### Next recommended test
Deploy manually; Gas Station Reshala Min3/Max3 first. Expect Follower created #1-#3, sameGroupAsBoss=True and `FINAL RECONCILE ... RESULT=OK`. Then one other boss (e.g. Kojaniy on Woods, Min2/Max2, or Gluhar on Reserve). Send LogOutput.log. Nothing is claimed working for other bosses until tested.

# Full Boss and Follower Spawning Audit (READ-ONLY, no code changed)

Status: diagnosis only. NO source edited, NO build run, NO DLL deployed, NO in-game test performed by me. Last built DLL remains SHA256 44759FDD7A5C2B8B1470A74276F033129A2ACAF52ECB72C73F651786E2449008 (not deployed by me).

## Trigger
Goons test (Knight preset, Customs Gas Station): one bossKnight spawned, warning "Followers requested for bossKnight, but it has no count-based follower type in SPT; spawning without followers."

## Evidence (code/data/log; not in-game confirmation of fixes)
- MarkerTypes.GetWildSpawnType: no preset for Big Pipe, BirdEye, bossBoarSniper, bossKillaAgro, bossTagillaAgro. Knight -> bossKnight only.
- RuntimeBotSpawnSpawner.MapFollowerType is single-role and has no bossKnight entry -> GetFollowerType null -> SOLO path (SpawnBotAt). The warning is correct by design, not a bug in follower creation.
- Vanilla data (SPT_Data locations base.json): bossKnight escort = exUsec x2 (x0 on shoreline) PLUS Supports followerBigPipe:1, followerBirdEye:1, followerGluharScout:0. Vanilla BotBossSpawn.SpawnEscort submits each support as its own BotCreationData sharing the boss spawnParams (ShallBeGroup bossGroup, followers+1). Goons are therefore ONE group of boss + exUsec escorts + BigPipe + BirdEye, not three independent bosses and Knight/BigPipe/BirdEye are not interchangeable.
- Assembly WildSpawnTypeExtension suitability groups: list_6 {bossKnight, followerBirdEye, followerBigPipe}; Gluhar {boss, Assault, Scout, Security, Snipe}; Kolontay {boss, Assault, Security}; Boar {boss, Boar, Close1, Close2}; Bully, Zryachiy, Kojaniy, Sanitar boss+follower pairs. Tagilla, Killa, Partisan, Boar sniper have NO follower groups.
- Knight log: solo path passes `new BotSpawnParams()` with Id_spawn==null (MoreBots hazard). Knight movement log shows iamBoss=False, group=2 and SAIN layers. No HuntManager exception text was found in the current LogOutput.log (FollowerCreationDiagnostics hooks only wrap the follower path), so the NRE on the solo path is UNPROVEN from this log; it is predicted from the confirmed decompiled HuntManager.OnBotCreated code.

## MoreBots assessment
MoreBotsAPI (decompiled, not modified) is a cause of ONE confirmed defect: HuntManager.OnBotCreated NREs when Id_spawn is null (already fixed on the followers path only). Solo and zone-group paths still pass null. Its other patches (SuitableFollowersListPatch: adds custom groups to SubInitList only; ShallBossAttackPatch: custom types only; BotsGroupIsPlayerEnemyPatch: faction revenge only; FixRaidEndSpawnTypePatch: custom types only) do not alter vanilla boss/follower suitability or grouping. MoreBots is NOT the cause of the "no count-based follower type" warning.

## Matrix (status classes: CW confirmed working in-game, CB confirmed broken, UV unsupported by vanilla, LT likely needs in-game test, UM unknown/no instrumentation)
| Boss | MEL role | Vanilla followers | MEL follower mapping | Status |
|---|---|---|---|---|
| Reshala | bossBully | followerBully x4 | yes | CW (3 followers, group 5, sameGroupAsBoss=True) |
| Sanitar | bossSanitar | followerSanitar x3 | yes | LT |
| Kojaniy | bossKojaniy | followerKojaniy x2-3 | yes | LT |
| Zryachiy | bossZryachiy | followerZryachiy x2 | yes | LT |
| Boar | bossBoar | followerBoar x6 + Boar x4 + Close1 + Close2 | followerBoar only | LT, partial (mixed supports unmodelled) |
| Gluhar | bossGluhar | Security x2 + Assault x2 + Security x2 + Scout x2 | followerGluharSecurity only | LT, partial |
| Kolontay | bossKolontay | Security x2 + Assault x2 + Security x2 | followerKolontaySecurity only | LT, partial |
| Knight (Goons) | bossKnight | exUsec x2 + BigPipe + BirdEye | none | CB for goons: spawns solo by design; needs multi-role design |
| Big Pipe / BirdEye | followerBigPipe / followerBirdEye | n/a (they are followers) | no preset | UV as bosses; only valid as Knight supports |
| Tagilla | bossTagilla | followerBully x0 | none | UV |
| Killa | bossKilla | followerTagilla x0 | none | UV |
| Partisan | bossPartisan | x0 | none | UV |
| Boar sniper / KillaAgro / TagillaAgro | no preset | boarSniper x1-2 / none | none | UV / not exposed |
| Cultist/Priest | sectantWarrior | priest has sectantWarrior x2-4 | none | UV (not a boss preset) |

## Shared root causes / separate fixes
1. Null Id_spawn on SOLO and ZONE-GROUP paths (SpawnBotAt solo, SpawnZoneGroup): same MoreBots NRE; skips AfterCreation/SetBoss-adjacent steps (Knight iamBoss=False is consistent). Fix: Id_spawn=string.Empty on those BotSpawnParams.
2. Single-role MapFollowerType cannot express Goons or mixed supports (Boar, Gluhar, Kolontay). Separate design: ordered list of (role,count) supports sharing the boss ShallBeGroupParams, each submitted like vanilla SpawnEscort. Goons additionally needs exUsec escorts (note exUsec is not in the list_6 suitability group, so group reuse by role needs in-game verification).
3. No FINAL RECONCILE for solo bosses (observability gap).
4. Role-based group reuse hazard (BotZoneGroups.Group / IsSuitable) could place a MEL boss/follower in a same-type vanilla group in the same zone; unobserved, FINAL RECONCILE reports foreign members on the followers path only. Do not weaken exact-profile-ID matching.
5. Tagilla/Killa/Partisan: nothing to fix.

## Recommended minimal fixes (AWAITING APPROVAL; not implemented)
A. Id_spawn="" on solo and zone-group paths (tiny, same pattern as the proven Reshala fix).
B. Goons design: spawn Knight with a support list [exUsec x2, BigPipe x1, BirdEye x1] in one shared group, reusing SpawnBossWithFollowers machinery; no follower-count increase.
C. Then, optionally, supports for Boar/Gluhar/Kolontay.

## Next tests (in game, after approval/build)
Regression: Reshala 3-4 followers (must still show group N, sameGroupAsBoss=True). Then Sanitar (followerSanitar), checking FINAL RECONCILE RESULT=OK.

## Cleanup
Temp decompile folder %TEMP%\mbx removed.

# Stage 1: Fix Remaining Null Id_spawn Paths

## Code change (Client\RuntimeBotSpawnSpawner.cs only; 2 edits)
- SpawnZoneGroup (~L682): `new BotSpawnParams()` -> `new BotSpawnParams { Id_spawn = string.Empty }` (ShallBeGroup line unchanged).
- SpawnBotAt solo path (~L720): `new BotSpawnParams()` -> `new BotSpawnParams { Id_spawn = string.Empty }` inside GetProfileDataParams.
- SpawnBossWithFollowers already had Id_spawn = string.Empty (untouched). A grep confirms no other `new BotSpawnParams` exists in Client\*.cs.
- Rationale: MoreBotsAPI HuntManager.OnBotCreated calls SpawnParams.Id_spawn.ToLower(); null throws in ActivateBotCallback. "" is what vanilla uses for untriggered waves.
- NOT changed: follower generation, counts, groups, timeouts, exact profile-ID matching, Goons/multi-role design, MoreBotsAPI, exception handling.

## Build
Release, SPTPath=E:\SPT_TRADER_QUEST_MAKER_4.1.X: 0 errors, 60 warnings (same count as previous build 60; all pre-existing CS0649-style, none new).
DLL: C:\Users\ifunH\Desktop\MapLootEditorLite-main\Client\bin\Release\net471\MapEditorLite.Client.dll
Size 19120128 bytes; SHA256 943D6E56263BE9269D85C9D1166B2AE2B681BBD76DC8FD07DE283DDC612FD0A9
(previous build was 44759FDD...449008)

## Status
Compile-verified only. NOT deployed. NOT tested in game.

## Next steps
Deploy manually, then (1) regression: Reshala 3-4 followers must still show sameGroupAsBoss=True; (2) Knight/solo spawn: check `iamBoss` in [MEL movement] and no HuntManager NullReferenceException. Goons design awaits review of this stage.

# Stage 2: Multi-role follower support for all bosses

## Verified SPT behaviour (decompiled Assembly-CSharp, BotBossSpawn / BossLocationSpawn / BotSpawner / WildSpawnTypeExtension)
- BossLocationSpawn.EscortCount = primary escort + sum(Supports), BUT BotBossSpawn.SpawnBossSupports spawns ONLY the Supports (via SpawnEscort, one BotCreationData per support role, shared spawnParams) when a wave has Supports; the primary BossEscortType is then never spawned. So vanilla Goons = Knight + 1 BigPipe + 1 BirdEye (the `exUsec` x2 in the data is not spawned). Waves without Supports spawn the primary escort.
- exUsec is NOT in bossKnight's suitability group (list_6 = bossKnight, followerBirdEye, followerBigPipe), so it would not have joined Knight's group anyway.
- Group join: BotSpawner.GetGroupAndSetEnemies -> BotZoneGroups via IsSuitable(initial boss role, role). Every support role used below is in its boss's suitability list (Boar, Gluhar, Kolontay, Knight lists).
- ShallBeGroup.DescreaseCount runs once per BotCreationData; RemainCount = total+1 >= number of datas, so all datas stay in the group; first activated bot (boss) gets SetBoss.

## Code change (Client\RuntimeBotSpawnSpawner.cs only)
- New private class FollowerGroup {Role, Count}; new GetVanillaSupports(boss): Knight {BigPipe 1, BirdEye 1}; Boar {Boar 4, Close1 1, Close2 1}; Gluhar {Assault 2, Security 2, Scout 2}; Kolontay {Assault 2, Security 2} (zero-amount vanilla entries omitted).
- MapFollowerType now single-role only: Bully, Sanitar, Kojaniy, Zryachiy (Boar/Gluhar/Kolontay moved to supports).
- GetFollowerType -> GetFollowerPlan (returns List<FollowerGroup> or null). Single-role bosses: rolled min..max count as before. Supports bosses: fixed vanilla amounts; configured min..max only enables (rolled >0) / disables. Bosses with no followers (Tagilla, Killa, Partisan, etc.) still warn and spawn solo.
- SpawnBossWithFollowers takes the plan; sums total for ShallBeGroupParams(total+1); loops one BotCreationData per role (same shared spawnParams, Id_spawn=""), generation failure of one role is logged and skipped (no silent success: FINAL RECONCILE covers only submitted ids, shortfall logged); requestedIds combined for ReconcileFollowers/FINAL RECONCILE (exact profile id, foreign-member listing unchanged).
- Unchanged: boss creation/lookup by exact profile id, Id_spawn fixes, timeouts, solo path, zone-group path, Reshala flow (plan = 1 group of followerBully, identical calls, plus a Task.Yield after submit).
- Behaviour note: Supports bosses ignore the configured count value (fixed vanilla sizes: Goons 3 bots, Boar 7, Gluhar 7, Kolontay 5).

## Build
Release, SPTPath=E:\SPT_TRADER_QUEST_MAKER_4.1.X: 0 errors, 60 warnings (same count as before; none new).
DLL: C:\Users\ifunH\Desktop\MapLootEditorLite-main\Client\bin\Release\net471\MapEditorLite.Client.dll
Size 19122688 bytes; SHA256 825FC90281394819D1609F7C3753360C96EFABAFB89E8B47A99B7BAE4AA0B46A
Not deployed. Compile-verified only; NOT tested in game.

## Next tests
1. Reshala 3-4 followers (regression: sameGroupAsBoss=True, FINAL RECONCILE RESULT=OK).
2. Knight (Goons): expect "uses vanilla supports: 1xfollowerBigPipe, 1xfollowerBirdEye", 3 profiles, FINAL RECONCILE live=2 notInBossGroup=[].
3. Sanitar, then Gluhar/Kolontay/Boar. Tagilla/Killa/Partisan are unsupported by vanilla data.

# Stage 3: Preset change kept the old Wild Type ("Sanitar stays Goons")

## Finding (log + pack files)
LogOutput.log: `[MEL diag] ... preset=Sanitar, role=bossKnight` -> the Goons plan ran (1xBigPipe, 1xBirdEye). This also confirms the Stage 2 Goons path works at the submit level (both support requests submitted, bossGroup=4; "followersCreated=4" counts every follower-role bot created that raid and is not authoritative - FINAL RECONCILE was not inspected).
Pack data: packs\Nolan_Quest_Pack.json had preset "Sanitar" but wildSpawnType "bossKnight"; exports\ copy had bossSanitar. Cause: the web Tool's `presetChange` (Tool\src\MarkerLists.tsx) only set a role for Reshala and only when blank, so switching preset left the old Wild Spawn Type. The client uses wildSpawnType before the preset (ResolveWildSpawnType), so the stale role won. The in-game editor is not affected (ApplyPreset overwrites wildSpawnType).

## Change (Tool only; client DLL unchanged, still 825FC902...B46A, not deployed)
Tool\src\MarkerLists.tsx: `presetChange` now sets wildSpawnType from a table mirroring Client BotSpawnPresetMapping.GetWildSpawnType for every mapped preset (Any/generic Boss leave it untouched). `tsc --noEmit` passes. The Tool bundle was not rebuilt/deployed (npm run build not run).

## Action for existing data
Entries already saved with a stale role must be fixed once: re-select the preset in the Tool (rebuilt) or edit Wild Spawn Type by hand, then re-export. Make sure the pack that the game loads (exports\) has the intended wildSpawnType.
Not tested in game.

# Stage 3b: Preset/Wild Type sync check (no further code change)
In-game editor (Client\CustomEditorUI.cs L3355, L3412, L4255): the preset dropdown already calls BotSpawnPresetMapping.ApplyPreset, which overwrites wildSpawnType, then RefreshInspector - point, zone and group all covered. Web Tool: presetChange (Tool\src\MarkerLists.tsx) now does the same for all three entry types (Stage 3). The Tool screenshot still showing Sanitar + bossKnight is data saved before that fix; it needs one re-pick of the preset in a rebuilt/reloaded Tool. Not tested in browser.

# Stage 4: Honor min/max followers for Boar, Gluhar, Kolontay (mixed supports deferred)

## Finding (LogOutput.log L1169-1307, Gluhar test, configured 0-4)
Stage 2 treated Gluhar as fixed vanilla supports: plan 2xAssault + 2xSecurity + 2xScout = 6, ignoring min/max. Everything spawned correctly: all 6 roles are Gluhar roles, all group=5 sameGroupAsBoss=True, FINAL RECONCILE RESULT=OK (live=6, missing/notInBossGroup/foreign empty). No Reshala (followerBully) role was requested or created in the log, so the "Reshala guard" is not a spawned Bully; it was likely a Gluhar follower's look (not verified in game).

## Change (Client\RuntimeBotSpawnSpawner.cs only)
GetVanillaSupports now contains only Knight (Goons: 1 BigPipe + 1 BirdEye; Knight has no single count-based role, so count is not honored there). Boar, Gluhar and Kolontay moved back to MapFollowerType single-role (vanilla BossEscortType): followerBoar, followerGluharSecurity, followerKolontaySecurity; the rolled min..max count is honored. Mixed supports for these bosses are deferred as a later feature. Nothing else changed.

## Build
Release: 0 errors, 60 warnings (unchanged). DLL C:\Users\ifunH\Desktop\MapLootEditorLite-main\Client\bin\Release\net471\MapEditorLite.Client.dll, 19122688 bytes, SHA256 EA58845625C85636A10BF856C72158EC13FE0D697EBC53E499E7CAAAF71F635F. Not deployed, not tested in game.

## Next test
Gluhar min 0 / max 4 several times: followers should be 0-4 followerGluharSecurity, RESULT=OK. Reshala regression unchanged.

# Stage 5: Honor min/max followers for every boss that has followers (Knight/Goons was the last exception)

Client\RuntimeBotSpawnSpawner.cs, GetFollowerPlan only: the rolled min..max count for a Supports boss (only Knight) is now spread round-robin over its support roles (BigPipe, BirdEye, BigPipe, ...) instead of fixed 1+1. Both roles are in Knight's suitability group (join the boss group); exUsec is not and is still not used. Logged as "rolled N (range a-b) spread over supports: ...".
Coverage now: Reshala, Sanitar, Kojaniy, Zryachiy, Boar, Gluhar, Kolontay = single role with rolled count; Knight = rolled count over BigPipe/BirdEye. Tagilla, Killa, Partisan: no vanilla followers/suitability group, still solo with a warning. Mixed vanilla supports for Boar/Gluhar/Kolontay remain a later feature.
Build: Release 0 errors, 60 warnings (unchanged). DLL ...\Client\bin\Release\net471\MapEditorLite.Client.dll, 19123200 bytes, SHA256 F139110CE1EFF93A23A03FF3139E20A411589D1F17C2F04C5BC2A46B66FC3046. Not deployed, not tested in game.
Next test: Knight min 0/max 4 several times (expect 0-4 BigPipe/BirdEye, FINAL RECONCILE RESULT=OK), then Reshala regression.

---

## MEL feature & bug audit — object picker, bot spawn groups, gizmo precision, duplicate previews

Investigation-only audit of the four requested items. The existing 1,571-line history above was retained. Repository files were inspected read-only except for this appended audit. The workspace is not a Git repository (`git rev-parse` reports no `.git` directory), so no branch or working-tree diff could be obtained.

### 1. Executive summary and recommended priority order

1. **Object picker and cloning — confirmed design gap; exact in-game failure mix unknown.** Scene-object picking is a single mouse-ray/collider query. There is no screen-space fallback in that path, and there is no general scene-object clone operation. Marker cloning is a separate JSON-copy workflow. A small visible collider or a collider/render hierarchy mismatch is a likely reason that a close camera helps, but no runtime collider dimensions or user logs establish that as the actual case.
2. **Duplicate item preview — confirmed async safeguards; reported duplicate cause unknown.** A new loose-loot marker starts with one default Salewa template. The real preview is asynchronous and is intended to replace a fallback or old preview. Generation checks prevent stale request completion from replacing the current request. The pool lifecycle and per-instance metadata handling are a concrete risk to verify; static code alone cannot prove that pooling is the cause.
3. **Gizmo precision — confirmed sizing floor.** Gizmo size is camera-relative, but clamped to at least a 0.5-world-unit arm. It is positioned at the marker, not scaled to the preview bounds, so it can dwarf small item previews. Axis picking uses a separate fixed pixel tolerance.
4. **Grouped bot/boss spawn points — confirmed architectural mismatch with loot groups.** The loot-group implementation uses SPT’s server-side forced-loot selection. Bot points are instead instantiated by MEL’s client runtime and submitted through `BotSpawner`; current point markers result in independent requests. Reuse the explicit-ID/editor pattern, not the loot runtime mechanism. This is the largest and riskiest change and should be implemented last.

### 2. Object picker and cloning

#### Findings

- **[Confirmed]** Normal marker editing is handled in `MapEditorController.HandleMouseInput()` (`Client/MapEditorController.cs`): it asks `MarkerRenderer.PickFromScreenPosition()` for a MEL marker with a 50 m limit. `MarkerRenderer.PickFromScreenPosition()` (`Client/MarkerRenderer.cs`) first raycasts MEL marker visuals on layer 2, then falls back to a 40 px screen-space distance from marker centers. This is marker picking, not world-object picking.
- **[Confirmed]** Scene-object picking is `CustomEditorUI.PickSceneObjectAtMouse()` (`Client/CustomEditorUI.cs`), exposed as `TryPickSourceSceneObject()` and `GetSceneObjectHover()`. It calls `Physics.RaycastAll` from the cursor to 100 m, accepts the nearest hit passing `IsValidPick()`, and returns `hit.transform.gameObject`. It has no screen-space/bounds fallback and no explicit trigger query mode. The optional “Use Parent” behavior promotes only to the immediate parent.
- **[Confirmed]** `ScanSceneObjects()` caches objects with a `MeshFilter`/`SkinnedMeshRenderer` mesh and treats an `LODGroup` as one item. It supports the scene-object list/outlines; the `MarkerRenderer.PickSceneObject()` bounds-intersection helper is not called by the current scene-pick path. Thus the bounds cache does not rescue cursor clicks that miss colliders.
- **[Confirmed]** `IsValidPick()` only applies `IsEditorObject()` and `ShouldIgnorePick()`. `IsEditorObject()` filters `MLE_` ancestry, `Player`, and `PreviewLootMarker`; configurable path filters are applied by `ShouldIgnorePick()`. The ordinary picker does not explicitly exclude bots, loose loot, or all trigger/collision-only objects. The separate M1 surface validator has stronger character/loot and renderer checks, but is not the normal object-picker filter chain.
- **[Confirmed]** The picker has a 100 m maximum, not a close-distance minimum. That makes a hard “must be close” distance threshold unlikely. **[Likely, not demonstrated]** Moving closer may make a small collider or a collider offset from the visible mesh easier to hit. The code does not record collider size, selected renderer, target visibility, or distance for ordinary clicks, so large/complex collider and LOD effects remain unverified.
- **[Confirmed]** There is no general “clone arbitrary scene GameObject” workflow in the inspected code. `MarkerManager.DuplicateSelected()`/`DuplicateSelection()` serialize and deserialize editable marker data, assign fresh IDs, and offset duplicates. `CloneVanillaToPack()` (“Write Mode”) converts a vanilla marker definition to an editable pack marker. Static-object previews instantiate a source object for display, but that is not the same as cloning a live world object. `DuplicateSelected()` returns null for vanilla markers, for which `CloneVanillaToPack()` is the supported conversion path. Therefore, a failed selection and an unsupported/failed marker-copy action must be diagnosed separately.
- **[Unknown]** No runtime evidence establishes whether reported door failures come from collider picking, selecting the wrong hierarchy level, using “Duplicate” rather than “Write Mode” on a vanilla marker, or another UI path. Existing M1 history documents frequent dedicated ballistic colliders and ambiguous renderer hierarchies; it also documents a deliberate rejection guard for that separate surface-editing feature. Do not remove that ambiguity guard as a picker shortcut.

#### Smallest safe improvement and risks

First add opt-in, throttled pick diagnostics (ray distance, collider path/type/trigger, chosen object path, filter reason, and whether a usable renderer exists). Then add a narrow screen-space candidate fallback for scene objects only when the ray yields no valid target, using the existing scanned bounds and the same exclusions. Rank candidates by screen-space distance and ray depth; keep the current exact ray hit as first choice. Do not make a broad near-object “select everything” fallback or let MEL previews, bots, dropped loot, triggers, or hidden collision proxies win silently. If the fallback becomes ambiguous, provide deterministic cycling or require another click rather than guessing a renderer.

For cloning, first make the UI distinction clear: world source selection/placement, vanilla marker “Write Mode”, and editable marker duplication are different operations. Only change clone logic if logs show the clone action was invoked with a valid selected marker and then failed; no clone-creation root cause is established by static inspection.

#### Tests and pass/fail criteria

1. At normal free-camera distances, repeatedly click representative ordinary props, LOD props, nested props, doors, and containers. **Pass:** the intended target is selected without moving closer; log identifies the exact collider and selected hierarchy.
2. Test a target with no collider, a small collider, a collider-only child, overlapping colliders, and a trigger in front of a visible object. **Pass:** no unrelated object is silently selected; candidates that cannot be resolved are reported as ambiguous/rejected.
3. Verify MEL marker visuals/previews, player, bots, loose loot, and filtered hierarchy objects remain excluded.
4. Separately test editable-marker duplicate, vanilla “Write Mode”, and static source preview. **Pass:** each operation has the documented effect; vanilla duplicate does not silently pretend to clone, and no selection failure is reported as a clone failure.

### 3. Grouped spawn points for bots and bosses

#### Current architecture

- **[Confirmed]** Loose-loot groups are explicit optional `lootGroupEnabled`, `lootGroupId`, and `lootGroupWeight` fields on each spawn (`Client/MarkerTypes.cs`, `Server/PackData.cs`). `CustomEditorUI.BuildLootGroupControls()` edits them. `Server/LootTransformer` filters members, creates forced spawnpoints with shared `Template.Id` (`MEL_LG_` + group ID), and uses each weight as probability; `WttSpawnConverter` excludes those from the separate WTT forced export. Prior changelog entry 7 records the SPT 4.1.6 `LocationLootGenerator.ChooseForcedPositions` behavior and its caveat: no reroll if later loot limits prevent the selected item from spawning.
- **[Confirmed]** Bot data is persisted on `BotSpawnPoint`/`BotSpawnZone` and loaded by `RuntimeBotSpawnSpawner.LoadPacks()` (`Client/RuntimeBotSpawnSpawner.cs`). The client patches `BotsController.Init`; its prefix gathers map data and creates runtime `SpawnPointMarker`s, and its postfix submits forced requests. There is no bot-spawn execution path in `Server/LootTransformer`.
- **[Confirmed]** Each non-triggered `BotSpawnPoint` passes spawn chance and zone resolution, creates its own `SpawnPointMarker`, and becomes its own `SpawnRequest` (`SpawnCustomMarkers()` / `CreateSpawnMarker()`). `ForceSpawnCustomBots()` submits point requests individually via `SpawnBotAt()`. So two independent point markers can submit two requests; there is no group identity or once-per-group selection in this path.
- **[Confirmed]** Existing `BotSpawnGroup`/`randomGroups` means a random **spawn configuration** (role/side/category/count) within one `BotSpawnZone`, not alternate locations for one logical spawn event. `CreateZoneMarkers()` chooses one such configuration and creates `spawnCount` spawn markers. `SpawnZoneGroup()` groups those requests by the same zone and submits the requested number of bots over all its points; it must not be repurposed as “choose one location.”
- **[Confirmed]** Boss requests go through `SpawnBotAt()` and may call `SpawnBossWithFollowers()`. That path creates the boss profile, preserves profile-ID matching, submits followers after the boss exists, and contains prior `Id_spawn`, group-assignment, and follower-compatibility hardening documented later in this changelog. A new selector must choose one `SpawnRequest` and then call this existing path unchanged; it must not re-identify bots by role or suppress duplicates by visual similarity.
- **[Confirmed]** Trigger points are stored by `triggerZoneName`. Each `ActivateTrigger()` dispatches every point/zone registered for that trigger; `SpawnTriggeredPoint()` creates a request and calls `SpawnBotAt()` when its bot-spawn chance passes. Trigger mode/cooldown is maintained per trigger. Grouping therefore must handle startup and triggered dispatch separately, and repeated trigger activations must have explicit event semantics.
- **[Unknown]** The inspected MEL source shows submission to SPT’s `BotSpawner.TryToSpawnInZoneAndDelay`, but does not establish whether SPT itself prevents duplicate logical requests across different spawn points. Do not assume that it does. Existing profile/follower creation constraints, zone eligibility, and delayed submission remain relevant; a selection should happen before profile creation and bot submission.

#### Minimal design

Add an optional, explicit persistent `spawnGroupId` to point markers; blank/absent means the existing independent behavior. Keep it separate from inherited editor `group`, marker `id`, and `BotSpawnGroup.id`. Scope IDs to a stable pack/map identity to avoid accidental cross-pack collisions. Do not infer membership from role, preset, position, or similar-looking requests.

For v1, group members should describe the same logical spawn event: same boss/bot role, side/category, follower configuration, quest conditions, spawn mode, and activation policy. Validate mismatches in the editor and at runtime. Determine valid members first (quest condition, resolvable `BotZone`, valid spawn-point creation); make one group-level chance roll; choose exactly one valid member (uniform random is the simplest initial rule); create that single request; then run the existing `SpawnBotAt()` path so boss identity and follower logic remain intact. If none are valid, log and spawn none. Do not retry another location after profile/submission failure in v1; record the group event as attempted to prevent a second activation from creating a duplicate. A future retry policy must be explicit and bounded.

Give startup and triggers explicit event IDs: one startup event per group per raid; for trigger-based groups, one event per accepted activation, with existing trigger one-time/cooldown behavior respected. Require all members of a v1 trigger group to use the same activation trigger/policy; otherwise reject with an actionable warning. This prevents a group from accidentally being processed once per member or once by each unrelated trigger. Quest conditions should be checked before candidate selection, not treated as a duplicate-suppression signal.

Use the loot-group model only for optional member fields, UI controls, ID reuse/warnings, and backward-compatible serialization. Do **not** use `SpawnpointsForced`, `Template.Id`, loot weights, or the SPT loot generator to implement bot groups. Ordinary bot support should follow only after boss groups work; its `SpawnBotAt()` and zone/potential paths may need distinct policy.

#### Compatibility risks and tests

- Missing `spawnGroupId` must leave all existing packs and all existing ungrouped markers on the exact current path. Preserve each marker’s unique `id`; group IDs are an additional field, not a replacement.
- Validate collision scope across packs/maps; validate duplicate IDs only within intended scope. Cloning a grouped marker should intentionally preserve membership or let the user detach it; test and document the chosen behavior.
- Test 3–4 Reshala locations, all valid: exactly one boss event, one selected location, one existing follower group, and profile identity/follower reconciliation unchanged. Then test one invalid/blocked location, quest-ineligible members, no eligible members, profile-generation failure, and zone submission failure.
- Test two different group IDs, a group with one member, ungrouped points, same role but different IDs, and repeated BotsController initialization. **Pass:** one event per group/event key; independent groups remain independent; no group is inferred by role; one-member group behaves as one request.
- Test a one-time trigger and a repeatable trigger with cooldown. **Pass:** no more than one selection per accepted activation, and reactivation follows the documented event-key policy.
- Test boss follower variants (including Reshala and Knight) and verify exact boss profile identity, follower role counts, `sameGroupAsBoss`, and final reconciliation; no weakened identification or group assignment.
- Only after the boss path is stable, test ordinary bots and BotSpawnZone/PmcSpawnZone behavior independently.

### 4. Gizmo precision for small items

#### Findings

- **[Confirmed]** `MarkerRenderer.GetGizmoDynamicScale()` computes a perspective camera-distance scale targeting `_gizmoScreenSize = 50` px, but returns `Mathf.Max(targetWorldSize / _gizmoSize, 1f)` with `_gizmoSize = 0.5` world units. At close distances the 1.0 floor fixes each arm at 0.5 world units instead of shrinking with the target pixel size.
- **[Confirmed]** `GetGizmoPosition()` uses the selected marker position (or child transform / selection center), not the bounds or size of the rendered preview. Loose-loot previews can therefore be much smaller than the gizmo. The same renderer path handles large and small marker types.
- **[Confirmed]** `UpdateGizmo()` scales arm length, line width, handles, and rotation rings using the same dynamic scale. `PickGizmoAxis()` uses the projected axis endpoint and a caller-provided pixel tolerance (25 px in `HandleMouseInput`); it does not test the visual mesh collider. Visual size and interaction tolerance are therefore not the same control.
- **[Confirmed]** A previous gizmo fix in changelog section 5 addressed a moving drag plane/feedback loop and preserved axis/rotation behavior. The precision change must not alter that frozen drag plane or snapping/rotation calculations.

#### Smallest targeted change and risks

Replace the fixed 1.0 world-scale floor with a screen-size-consistent scale for small loose-loot previews, optionally gated to loose-loot markers or a precision mode. Keep axis hit tolerance independently usable (25 px) and retain a sensible minimum visual width/handle visibility. Do not globally shrink every marker gizmo based on item-specific bounds or change drag math. If adding size awareness, use the actual preview renderer bounds only as a cap/scale hint; marker position remains the drag pivot. Support orthographic camera behavior explicitly if that camera mode is in use; the current formula assumes perspective.

#### Tests and pass/fail criteria

1. Small Salewa and other compact loot at normal, close, and far camera distances. **Pass:** gizmo no longer overwhelms the item at normal distance; handles remain visible and independently clickable.
2. Large static object, zone, and multi-selection. **Pass:** current usable screen presence is retained; no global shrink or pivot shift.
3. Translate each axis, rotate each axis, and scale; test tilted orientation, snapping, mouse-up, and repeated drags. **Pass:** no jump/feedback loop, drift, wrong-axis selection, or change to saved transforms beyond the requested adjustment.
4. Verify gizmo visual change does not alter the 25 px picking tolerance or cause clicks on MEL previews/markers.

### 5. Stationary duplicate item preview

#### Lifecycle trace

- **[Confirmed]** `MarkerManager.CreateLootSpawn()` creates a new `LooseLootSpawn` with the default Salewa template (`544fb45d4bdc2dee738b4568`). `MapEditorController.CreateLootSpawn()`/`CreateLootSpawnAtLook()` selects the new marker and calls `SpawnPreviewForMarker()`, which dispatches to `SpawnAtMarker()`.
- **[Confirmed]** `SpawnAtMarker()` computes position/rotation and calls `SpawnPreviewInternal(... clear: true, reuseIfSame: true)`. For a new marker, it creates a fallback sphere, attaches `PreviewLootMarker` metadata including marker ID, records a generation/signature, and starts `LoadRealPreviewCoroutine()`.
- **[Confirmed]** The coroutine asynchronously preloads bundles and tries to create the real loot prefab. It checks `IsRequestCurrent()` before replacing. On success it adds metadata to the real preview, tracks it, then removes/destroys matching fallback/old previews. Stale generations or removed fallback references are abandoned. `TryReuseMarkerPreview()` avoids duplicate requests for the same marker/signature.
- **[Confirmed]** `UpdateForMarker()` updates all tracked loot previews whose `PreviewLootMarker.sourceMarkerId` matches the moved marker. `ClearByMarkerId()` clears only previews for that marker and invalidates its current generation. This is appropriately marker-scoped; do not add broad root-wide destruction.
- **[Confirmed]** Fallback previews are destroyed directly. Real item previews are returned through `AssetPoolObject.ReturnToPool(preview, true)`; `AttachMeta()` always adds a new `PreviewLootMarker` component. The code reads metadata with `GetComponent<PreviewLootMarker>()`, not a component reset/rebind helper.
- **[Likely risk; unverified runtime behavior]** If `AssetPoolObject.ReturnToPool()` retains MEL-added components and `ObjectsFactory.CreateLootPrefab()` later reuses the same GameObject, `AttachMeta()` could leave old and new metadata on that instance. `GetComponent()` could then read a stale `sourceMarkerId`, making a newly pooled preview absent from the current marker’s update loop. Whether this pool preserves these components, or whether this explains the reported stationary object, cannot be established from this repository alone.
- **[Unknown]** No current changelog entry mentions Salewa or a duplicate stationary item. The prior preview fixes address ground offsets and marker movement; they do not establish this duplicate bug’s cause. No static path found proves that two visible real Salewas are created for the same marker. The symptom could be a stale pooled object, duplicate preview instances, a preview instance missing/using stale metadata, or another render/parenting artifact.

#### Next diagnostic and targeted fix criteria

Instrument only preview lifecycle boundaries for the affected marker: marker ID, request generation/signature, instance ID, fallback/real flag, active state, parent, position, all attached `PreviewLootMarker` component IDs, and pool return/reuse. Log create, reuse, replacement, movement, and clear events—not every frame. Reproduce: newly add default Salewa, wait for real load, move before and after load completion, then manually select Salewa and repeat; also repeat with a different item. Compare `UpdateForMarker()` updates against visible object instance IDs.

If pooled metadata is confirmed, reset/reuse one metadata component (or remove stale MEL metadata before attaching the current marker metadata) on the exact pooled instance, and ensure one tracked visible preview per marker. If a second instance is confirmed, use generation/signature and marker ID to identify the exact stale owner before destroying it. Do not delete every preview under `_root`, and do not clear another marker’s preview.

#### Tests and pass/fail criteria

1. Repeat new-marker default Salewa creation across immediate and delayed bundle loads; move during loading and after load. **Pass:** exactly one visible preview instance belongs to the marker, and its transform follows each marker move.
2. Manually reselect Salewa and select a different item while the old model is loading. **Pass:** stale completion cannot replace the current item or leave a stationary copy.
3. Reuse pooled previews across two different marker IDs. **Pass:** metadata resolves to the current marker only; moving marker A never moves/clears B and vice versa.
4. Test fallback failure, item change, delete marker, clear/reload map, and gravity on/off. **Pass:** only the affected marker’s preview is removed/replaced; no exception, duplicate, or orphan remains.

### 6. Dependencies and shared components

- Picker and clone are related only through selection: `CustomEditorUI`/`MapEditorController` choose the target, while `MarkerManager` copies marker data. Keep tests and diagnostics separate so a missed click is not reported as clone failure.
- Gizmo and preview movement meet during `MapEditorController` drag updates and `LootPreviewSpawner.UpdateForMarker()`. Preserve the existing live-preview update path and the prior fixed-plane drag fix.
- Bot spawn groups are independent of loose-loot groups at execution time. Their only safe shared precedent is optional explicit member IDs, UI controls, and legacy-compatible serialization. Boss follower creation is a critical existing dependency and must remain on `SpawnBotAt()`/`SpawnBossWithFollowers()`.
- No evidence ties the stationary preview to the gizmo scaling issue; the same move operation can expose it, but that is not evidence of a shared root cause.

### 7. Proposed implementation sequence

1. **Object-picker diagnostics and isolated selector improvement.** Log ray/collider/target/filter context, then add a bounded screen-space fallback only when the exact ray has no valid candidate. Keep source selection, scene-list selection, marker picking, and marker cloning distinct. Validate ordinary props and doors before clone changes.
2. **Gizmo visual scale adjustment.** Change only scale calculation/visual sizing; preserve the drag plane, pivot, axis hit radius, and rotation/snapping logic. Validate small and large markers in the same session.
3. **Preview reproduction and evidence-driven correction.** Instrument instance metadata/pool lifecycle first. Make only the narrow correction supported by the logs, then rerun Salewa/manual selection and cross-marker pooling tests.
4. **Bot group schema/editor, then runtime lifecycle.** Add optional ID and compatibility validation first; implement startup selection and event deduplication next; integrate trigger events after startup behavior; keep zone groups and boss follower creation unchanged. Add ordinary-bot support only as a later, separately tested stage.

### 8. Exact files likely to require modification

- **Object picker/cloning:** `Client/CustomEditorUI.cs` (`PickSceneObjectAtMouse`, `IsValidPick`, `ShouldIgnorePick`, `ScanSceneObjects`, picker UI); `Client/MapEditorController.cs` (`HandleMouseInput`, source-pick dispatch); `Client/MarkerRenderer.cs` (`PickFromScreenPosition`, scene-object outlines); `Client/MarkerManager.cs` (`DuplicateSelected`, `DuplicateSelection`, `CloneVanillaToPack`) only if evidence warrants changing copy behavior.
- **Bot groups:** `Client/MarkerTypes.cs` (`BotSpawnPoint`, potentially `BotSpawnZone`); `Client/CustomEditorUI.cs` bot spawn inspector/group controls; `Client/RuntimeBotSpawnSpawner.cs` (`SpawnCustomMarkers`, `ForceSpawnCustomBots`, `SpawnBotAt`, trigger dispatch and state); `Server/PackData.cs` only to mirror the optional JSON field for schema parity. `Client/JsonStorage.cs` should be verified for compatibility, but likely needs no change if it serializes the existing marker data generically. Do not alter follower identity/creation logic.
- **Gizmo:** `Client/MarkerRenderer.cs` (`PickGizmoAxis`, `UpdateGizmo`, `GetGizmoDynamicScale`); possibly `Client/MapEditorController.cs` only if a separate precision-mode control is explicitly chosen.
- **Duplicate preview:** `Client/LootPreviewSpawner.cs` (`SpawnAtMarker`, `SpawnPreviewInternal`, `TryReuseMarkerPreview`, `LoadRealPreviewCoroutine`, `AttachMeta`, `UpdateForMarker`, `ClearByMarkerId`, `DestroyPreview`); `Client/MapEditorController.cs` only for narrowly scoped diagnostic call context if required.
- **Documentation:** this appended section in `CHANGELOG_SESSION.md`.

### 9. Diagnostics, experiments, and audit scope

- Before changing picker filters, capture one successful and one failed normal scene pick for a door and a nested/LOD prop, including collider and renderer hierarchy data. Preserve the existing M1 ambiguity rejection until a principled association is evidenced.
- Before changing preview cleanup, reproduce both the default Salewa path and manual Salewa selection with lifecycle logs that include all metadata components on pooled instances.
- Before bot implementation, test or inspect the installed SPT 4.1.x `BotSpawner` behavior for invalid/blocked spawnpoints, delayed submissions, and whether multi-point requests choose or use every position. Repository code alone does not establish SPT-side duplicate suppression. Preserve MEL’s existing boss profile-ID and follower-group diagnostics in any runtime validation.
- No source code, configuration, or project files were edited. No build was performed, nothing was deployed, and no in-game testing was performed. The only change for this task is this appended audit entry.

---

## MEL object picker and gizmo precision improvements — 2026-10-23

Implemented the approved focused workstreams for SPT 4.1.6. The preceding audit and all older changelog history were left intact. No bot-spawn or duplicate-preview behavior was changed.

### Object picker / cloning

**Confirmed current implementation:** `CustomEditorUI.PickSceneObjectAtMouse()` (`Client/CustomEditorUI.cs`) raycasted to 100 m and chose the closest hit that passed `IsValidPick()`. It used `hit.transform.gameObject` (or only its immediate parent when “Use Parent” was enabled), so collider child transforms could become the source object. It had no screen-space visible-bounds fallback, and it could skip an invalid nearer hit and select a valid object behind it. The cached scene-object picker/outlines in `Client/MarkerRenderer.cs` existed but were not used to resolve normal click misses. Marker selection remains a separate `MarkerRenderer.PickFromScreenPosition()` path.

**Root cause established from code, not runtime repro:** source-object selection depended on a ray intersecting a collider; the returned target could be the collider child rather than the visible cached object. A slight cursor miss had no tolerance fallback. These are confirmed limitations capable of causing the reported behavior. No in-game logs establish that they were the cause of every reported failed selection or “clone.”

**Changes:**
- `CustomEditorUI.PickSceneObjectAtMouse()` now uses explicit default raycast layers and ignores trigger colliders, sorts hits by distance, and does not select through a nearest non-selectable collider. Existing editor/player/preview filtering and configured path filters remain enforced; the existing character/bot/loose-loot classifier is now also applied in the normal picker (`IsValidPick()`).
- When “Use Parent” is off, an exact hit can resolve to its nearest cached selectable ancestor, addressing collider-child versus visible-object mismatches without changing the explicit immediate-parent option.
- `MarkerRenderer` now caches enabled visible renderer bounds alongside the existing outline bounds and exposes a screen-space fallback: within 12 px of the cached visible bounds, a candidate can be selected only when its nearest estimated depth is in front of the nearest raycast collider. Screen distance is the primary choice; depth breaks near ties. This avoids using collider bounds as a proxy for visible geometry and avoids selecting a bounds candidate behind an opaque collider hit.
- `MapEditorController.HandleMouseInput()` and its UI blocking/cursor-lock behavior were not changed. Hover highlighting and click selection share the picker; the GameObjects-tab surface diagnostic still receives the actual ray hit when available. A bounds-only selection intentionally does not fabricate a `RaycastHit`.
- No marker cloning or write-mode implementation was changed. `MarkerManager.DuplicateSelected()` / `DuplicateSelection()` duplicate MEL marker data; vanilla `Write Mode` uses `CloneVanillaToPack()`; scene-source picking supplies a source object to a marker preview. These are distinct operations, and no code evidence showed a defect in marker-copy creation itself.

**Remaining picker risks:** the 12 px fallback uses bounds captured when the scene-object list is scanned; animated/moved scene objects can have stale cached bounds until rescanned. A combined renderer AABB can cover empty space between complex submeshes, though the small tolerance and nearest-collider depth check limit this risk. A bounds-only hit has no precise collider/renderer surface hit and is unsuitable as evidence of a resolved M1 surface. Test doors and representative collider-child hierarchies in game before widening tolerance or changing renderer association rules.

### Gizmo precision

**Confirmed current implementation:** `MarkerRenderer.GetGizmoDynamicScale()` targeted a 50 px arm but clamped its scale to at least 1.0, keeping every gizmo arm at least `_gizmoSize` (0.5 world units) even at close camera distances. The gizmo pivot is the selected marker/child/selection center, not the preview bounds. `PickGizmoAxis()` projects the same handle endpoint and uses a separate 25 px interaction radius; rendering, hit testing, and drag calculations share the gizmo scale for endpoint positions. The controller’s fixed drag plane and preview update flow are unchanged.

**Changes:** for a single selected `LooseLootSpawn` only, the target arm is now 36 px and its scale is no longer clamped to the 0.5-world-unit minimum. This keeps it compact at close range and screen-readable at distance. Orthographic cameras now use an orthographic world-height calculation. All other marker types and multi-selection retain the existing 50 px target and 1.0 scale minimum. Axis hit radius, handle positions relative to the arm, coordinate systems, snapping, rotation, scale operations, drag-plane capture, and preview movement were not otherwise changed.

**Remaining gizmo risks:** 36 px is a category-based choice, not a measurement of each item’s render bounds; large items stored as loose loot also receive the smaller gizmo. Tiny previews may still be visually smaller than the gizmo, and the fixed 25 px hit tolerance can make projected axes feel close together at some viewing angles. Validate at different distances and orientations before tuning either value.

### Files modified

- `Client/CustomEditorUI.cs` — scene-object hit ordering, cached-ancestor resolution, visible-bounds fallback invocation, and preserved exclusions.
- `Client/MarkerRenderer.cs` — visible-renderer bounds cache/fallback candidate selection and single-loose-loot gizmo sizing.
- `CHANGELOG_SESSION.md` — this appended implementation entry.

No tests or test project were found in the repository, so no automated tests were added. Static review covered the separate marker picker, source-object picker, hover path, UI/cursor guards, marker duplication, vanilla Write Mode, fallback hit data, gizmo hit testing, and unchanged drag/preview update paths.

### Build and deployment

- Release build command: `dotnet build C:\Users\ifunH\Desktop\MapLootEditorLite-main\Client\Client.csproj -c Release -p:SPTPath=E:\SPT_TRADER_QUEST_MAKER_4.1.X --no-incremental`
- Result: **succeeded**, exit code 0; **60 warnings, 0 errors**. Warnings are existing unassigned/unused-field warnings in unrelated client files; no warnings were reported for `CustomEditorUI.cs` or `MarkerRenderer.cs`.
- Built DLL: `Client\bin\Release\net471\MapEditorLite.Client.dll`, 19,125,760 bytes, SHA-256 `4455C9E7DFD8721F7074A2976700EB0B258C6099164AC0E7827E67050243FEEF`.
- Deployed to `E:\SPT_TRADER_QUEST_MAKER_4.1.X\BepInEx\plugins\MapEditorLite.Client.dll`. The deployed file’s SHA-256 and size match the successful build.
- Preserved the previous installed DLL at `E:\SPT_TRADER_QUEST_MAKER_4.1.X\BepInEx\plugins\MapEditorLite.Client.dll.pre-picker-gizmo-20261023.bak`; it was not overwritten or removed. EFT was not running at deployment.
- **Not tested in-game.** A successful build and deployment do not establish runtime behavior.

### Next steps for Serenity

1. In the SPT 4.1.6 editor, test direct and bounds-assisted picks on doors, ordinary props, nested collider children, LOD objects, tight clusters, and ground/terrain-adjacent targets. Confirm the selected object path is the intended visible source and excluded/player/MEL-owned objects are never selected.
2. Test the explicit “Use Parent” option, scene-list selection, marker duplication, and vanilla “Write Mode” independently.
3. For each bounds fallback, verify it selects only an object in front of the nearest collider; note any empty-space picks caused by broad multi-renderer bounds.
4. Test a small Salewa/loose-loot preview and a larger loose-loot item in translate, rotate, and scale modes at near/far distances and oblique angles. Confirm axis grabbing, snapping, no sudden jumps, and gravity ON/OFF preview movement remain correct.
5. If bounds are stale after a door/scene-object move, rescan and report before changing the cache update policy. If any actual picker or gizmo regression appears, restore the preserved DLL backup before further runtime use.

---

## MEL hallway door picking and close-up gizmo follow-up — 2026-10-23

Investigated the reported hallway door-selection and close-up rotation-handle problems after the prior picker/gizmo deployment. Existing history above was preserved. No marker duplication, vanilla-marker Write Mode, bot spawn, or preview movement behavior was changed.

### Object picker findings and changes

**Cause established from current code:** `PickSceneObjectAtMouse()` performed a cached-renderer-bounds fallback even after the physics ray had hit a collider. This allowed the fallback to replace a valid nearest-ray selection, while the fallback's nearest-collider depth gate could also reject a visible candidate behind a frame/other collider. A nearest non-selectable physical collider remains an intentional stop; selecting through it would be unpredictable. The nearest cached ancestor resolver also returned `null` for an excluded ancestor, after which the caller could fall back to the raw collider child and validate that child separately.

**Changes:**
- A physical ray hit is now authoritative: return the nearest hit if it passes existing selection filters, or stop at that collider if it does not. The screen-space bounds fallback runs only when the ray has no physical hits.
- The fallback still requires a valid candidate (including MEL/player/preview, character/loose-loot, and configured path exclusions) and rejects near-equal screen-space bounds for unrelated objects rather than arbitrarily choosing one.
- Collider-child resolution now uses the nearest cached ancestor as the candidate before validation. If that cached ancestor is excluded, selection stops instead of falling back to the raw child.
- Green scene wireframes are editor-only `LineRenderer` visuals on layer 2 and have no collider; they do not participate in physics picking. Their bounds now prefer enabled visible renderer bounds when available, with collider bounds retained only where visible renderer bounds are unavailable. This reduces mismatch between visible objects and the green boxes without changing raycast layers.
- The source-object M1 surface diagnostic is skipped when a successful bounds-only pick has no real collider hit; no synthetic `RaycastHit` is created. A failed pick continues to be logged.
- No semantic door/frame association exists in the cached hierarchy/bounds data. If a door leaf and frame are distinct scene objects and the ray only hits a nearer non-selectable frame, the picker deliberately refuses to guess or select through it. Runtime hierarchy evidence would be needed before a larger door-specific resolver could be justified.

### Gizmo findings and changes

**Cause established from current code:** single loose-loot selection used a 36 px target arm, while other marker types used the 50 px base. Rotation-handle centers used fixed tangent points on each ring, so the point could project onto the preview or become poorly separated in oblique views. Rendering and hit testing shared the same point calculation; the controller's separate hit radius was 25 px. The reported runtime failure was not reproduced here, so the exact contribution of visual overlap versus user input remains unverified.

**Changes:** single loose-loot arms now target a clamped 56–72 px (64 px with current constants), preserving a stable readable screen-space size near the camera and a cap at distance. Rotation handles now use a camera-projected direction in each rotation ring plane, and rendering and hit testing use that same endpoint. The 25 px hit tolerance, drag plane, axis coordinates, snapping, rotation calculations, and preview movement remain unchanged. Other marker types retain the prior 50 px behavior.

### Files modified

- `Client/CustomEditorUI.cs` — authoritative nearest collider behavior, no-hit-only fallback, ambiguity reporting, and nearest cached-ancestor filtering.
- `Client/MarkerRenderer.cs` — ambiguous fallback rejection, visible-renderer-first outline bounds, and camera-oriented rotation handle placement with loose-loot screen-size clamp.
- `Client/MapEditorController.cs` — avoid reporting a successful bounds-only pick as a failed M1 surface hit.
- `CHANGELOG_SESSION.md` — this appended follow-up entry.

No automated test project or suitable test runner was found; no automated tests were added or run. Manual regression checks remain necessary for separate door/frame hierarchies, awkward collider setups, and rotation dragging at near/far and oblique camera views.

### Build and deployment

- Release build command: `dotnet build .\Client\Client.csproj -c Release --no-incremental -p:SPTPath='E:\SPT_TRADER_QUEST_MAKER_4.1.X' --nologo`
- Build result: **succeeded**, exit code 0; **60 warnings, 0 errors**.
- Built DLL: `Client\bin\Release\net471\MapEditorLite.Client.dll`, 19,126,272 bytes, SHA-256 `7652506F4A5C4BEE091D55FB0149A22E136ECE3630CC319A3A7DE400F894382B`.
- Deployed to `E:\SPT_TRADER_QUEST_MAKER_4.1.X\BepInEx\plugins\MapEditorLite.Client.dll`; deployed size and SHA-256 match the build.
- The prior installed DLL was preserved at `E:\SPT_TRADER_QUEST_MAKER_4.1.X\BepInEx\plugins\MapEditorLite.Client.dll.pre-hallway-gizmo-followup-20261023.bak` (19,125,760 bytes; SHA-256 `4455C9E7DFD8721F7074A2976700EB0B258C6099164AC0E7827E67050243FEEF`). The earlier `.pre-picker-gizmo-20261023.bak` was not overwritten or removed.
- **Not tested in-game.** Successful build and deployment do not establish runtime behavior.

### Remaining risks and manual checks for Serenity

1. Test direct selection of hallway doors, door leaves, frames, and child colliders. Confirm the selected object path is correct; verify that non-selectable physical blockers stop selection and that ambiguous bounds never pick an unrelated door behind them.
2. Confirm green wireframes follow visible renderer bounds and remain visualization only; test objects with disabled renderers, child renderers, LODs, and broad collider bounds.
3. Test loose-loot rotation handles close to the preview and at oblique angles. Verify they remain visible, are easy to grab, and do not jump when rotation dragging begins.
4. Regression-test near/far translate and rotation for loose-loot previews, normal/large objects, multi-selection, all three axes, snapping, and gravity ON/OFF behavior.
5. Recheck excluded/player/MEL-owned objects, hover highlighting, scene-source picking with an actual collider hit, and bounds-only source selection. Marker duplication and vanilla Write Mode are unchanged and should be checked independently if those workflows are exercised.
6. If correct door-leaf resolution requires identifying related sibling/frame objects beyond cached hierarchy ancestry, collect in-game transform/collider paths first; do not broaden picking through blockers based only on overlapping bounds.

---

## MEL duplicate-preview diagnostics and grouped bot spawn points — 2026-10-23

Implemented two isolated workstreams. No object-picker/gizmo code or loose-loot group runtime was changed.

### A. Duplicate loose-loot preview diagnostics

**Evidence and current finding:** the preview path already has a per-marker generation guard for asynchronous asset loading, keeps a valid real preview while replacements load, retires old preview objects after a replacement is ready, and updates preview transforms by marker ID during editor movement. Those safeguards make stale async completion less likely, but the stationary duplicate has not been reproduced in SPT and a specific runtime cause is not established.

The pooled `CreateLootPrefab` result receives a `PreviewLootMarker` component in `AttachMeta()`. The code does not itself verify whether the EFT pool strips previously-added MEL metadata when returning an object. If metadata survives pooling, `AddComponent` could leave multiple marker associations, and subsequent `GetComponent` calls could observe the old component. This is a code-supported risk/hypothesis, not a confirmed cause. The new diagnostics report existing metadata count and prior marker/template/request identity when observed.

**Diagnostics added:** set `[Diagnostics] EnablePreviewLifecycleDiagnostics = true` in the BepInEx config to enable bounded transition logs. Logs include marker ID/name, item template, request/generation IDs, GameObject instance IDs, requested and actual positions (at most once per second per updated preview), fallback creation, factory-returned instance observation, replacement/cleanup, stale request rejection, duplicate live previews, and retained pooled metadata. Normal movement does not log every frame. No preview lifecycle behavior was rewritten, and no suspected cause was changed without runtime evidence.

**Reproduction for Serenity:** enable the config flag, start a raid with the default Salewa preview, repeatedly move/rotate its marker, request another preview while loading if possible, switch between two loot markers, then delete a marker. Capture the `[PreviewLifecycle]` lines and note whether duplicate object instance IDs have one or multiple metadata components, whether two active instances share marker/template IDs, and whether the stationary instance's requested/actual positions diverge.

### B. Grouped bot spawn points

**Architecture:** runtime bot spawning is implemented by `Client/RuntimeBotSpawnSpawner.cs`, which builds SPT spawn markers during `BotsController.Init`, submits forced requests in the postfix, and separately activates trigger-based requests. The existing `BotSpawnGroup` type belongs to the random bot-composition settings inside a `BotSpawnZone`; it is not a location-alternative identity and was not reused. The loose-loot group runtime was not modified.

**Identity and persistence:** each `BotSpawnPoint` now has optional `botSpawnGroupId`, separate from marker `id`, hierarchy `group`, loot-group IDs, and bot-zone random-composition group IDs. Empty/missing values preserve legacy independent behavior. A non-empty ID groups points within the same pack and map. The native inspector and Tool editor expose the ID; the native inspector can create a GUID and the ID can be copied to the alternative point(s). JSON serialization preserves it; the Server pack DTO recognizes the field. Legacy packs deserialize with an empty/default field.

**One-event policy and validation:** for a non-trigger raid initialization, a group is processed once per raid, scoped by pack/map/group ID. Among locations with finite configured positions, positive finite radii, and a resolvable BotZone, one candidate is chosen uniformly at random. Spawn Chance is rolled once at the group level. The selected marker's existing Bot Spawn Chance is rolled once for forced/triggered requests; Potential-mode behavior remains with the existing SPT path. Group members must agree on bot identity, side/category/preset/wild type, random spawn type list, spawn mode, chances, delay, trigger configuration, and follower settings. Inconsistent or malformed groups and groups with duplicate member marker IDs are skipped with a reason rather than split into separate spawns. Location-specific position, radius, rotation, and BotZone remain per-candidate. Triggered groups choose once per existing trigger activation; existing OneTime/OncePerPlayer/Repeatable cooldown behavior determines which later activations are legitimate. A selected marker that becomes invalid before dispatch fails without trying another location. No automatic retry is added.

The selected `BotSpawnPoint` continues through existing `ResolveWildSpawnType()`, `SpawnBotAt()`, and `SpawnBossWithFollowers()` logic, preserving boss identification and follower association. A group with unknown/invalid boss role continues to fail through the existing safeguards; it is not converted into a regular bot. The SPT spawn API is request-oriented and does not provide a synchronous acceptance result here, so logs establish candidate selection and request submission, not confirmed bot creation.

### Files modified

- `Client/LootPreviewSpawner.cs` — opt-in preview lifecycle diagnostics and per-instance/request metadata logging.
- `Client/Plugin.cs` — `EnablePreviewLifecycleDiagnostics` config entry, disabled by default.
- `Client/MarkerTypes.cs` — optional persistent `BotSpawnPoint.botSpawnGroupId`.
- `Client/RuntimeBotSpawnSpawner.cs` — group validation, eligibility, selection, event deduplication, trigger grouping, and no-retry failure handling.
- `Client/CustomEditorUI.cs` — native bot group ID field and GUID generation.
- `Server/PackData.cs` — server pack DTO support for the new JSON property.
- `Tool/src/types.ts` — bot group field/default in the Tool model.
- `Tool/src/MarkerLists.tsx` — bot group ID editor field.
- `Tool/dist/index.html`, `Tool/dist/assets/index-DEJpVPVl.js`, and `Tool/dist/assets/index-DcnyQKbI.css` — rebuilt standalone Tool bundle.
- `CHANGELOG_SESSION.md` — this appended entry; prior history remains intact.

No test project was found, so no automated tests were added. Runtime behavior was not tested in SPT.

### Validation and deployment

- Client Release: `dotnet build .\Client\Client.csproj -c Release --no-incremental -p:SPTPath='E:\SPT_TRADER_QUEST_MAKER_4.1.X' --nologo` — **succeeded**, exit code 0, 60 warnings and 0 errors. The 60 warnings were existing unassigned/unused-field warnings in `VanillaImporter.cs`, `RuntimeInteractiveObjectSpawner.cs`, `ItemNameResolver.cs`, and `RuntimeNavMeshBuilder.cs`; no diagnostics were reported for the modified client source files.
- Server Release: `dotnet build .\Server\Server.csproj -c Release --no-restore --nologo` — **succeeded**, exit code 0, 0 warnings and 0 errors.
- Tool: `Set-Location .\Tool; npx tsc --noEmit` — **succeeded**, exit code 0, no diagnostics.
- Tool bundle: `Set-Location Tool; npx vite build` — **succeeded**, 1,585 modules transformed; the generated `Tool\dist` bundle includes the new group-ID field.
- No automated runtime tests were run.
- Client build: `Client\bin\Release\net471\MapEditorLite.Client.dll`, 19,146,752 bytes, SHA-256 `7475CE56D9D45CBB944CD822EBA91C16414FCC3489E1FCB8234F68C8C3A10E18`.
- Deployment tool reported `EscapeFromTarkov.exe` was running, but due to a conditional error it nevertheless copied the DLL. The target file now matches the build hash/size. The prior installed DLL was preserved at `E:\SPT_TRADER_QUEST_MAKER_4.1.X\BepInEx\plugins\MapEditorLite.Client.dll.pre-previewdiag-botgroups-20261023.bak` (19,126,272 bytes; SHA-256 `7652506F4A5C4BEE091D55FB0149A22E136ECE3630CC319A3A7DE400F894382B`). No server DLL, packs, maps, or user configuration files were deployed or modified.
- The game was running during the on-disk replacement. This is not an in-game test; the loaded process may still be using its previously loaded DLL. Do not replace or restore the DLL again while EFT is running.

### Remaining risks and next checks for Serenity

1. After safely closing EFT, verify the installed client DLL hash if desired; start a fresh process before testing. The preserved backup above is the pre-change installed client file.
2. Enable preview lifecycle diagnostics and reproduce the default Salewa stationary duplicate. Report the request/instance/metadata logs before authorizing any lifecycle fix.
3. Put two Reshala `BotSpawnPoint`s in one pack/map under the exact same `botSpawnGroupId`, with matching bot/follower settings and distinct locations. Confirm the log reports one selected location and only one group request path. Then test no eligible BotZone, invalid position, mismatched settings, duplicate marker IDs, and invalid boss role.
4. Test an ungrouped point to confirm its old behavior; test grouped trigger points under OneTime and Repeatable cooldown modes; verify future repeat activations are not suppressed.
5. Test the selected boss's profile identity, follower counts and boss association using the existing `[MEL followers]` diagnostics. Test legacy packs with no `botSpawnGroupId`, then save/export/import a group and confirm the ID remains unchanged.
6. Actual acceptance or rejection of `TryToSpawnInZoneAndDelay` and actual bot creation remain SPT runtime outcomes; the current call returns no synchronous confirmation, so the in-game log and live bot/follower checks are still required.

---

## MEL F8 client UI workspace polish — 2026-10-23

### Goal and scope

Applied a visual and text-legibility polish to the existing docked F8 editor. The central game view, hierarchy/inspector placement, resizable side panels, bottom tabs, panel dimensions, and editing interactions were intentionally left unchanged to preserve the current flow and viewport.

### Changes

- Updated the top bar, side panels, and bottom tab bar to a coordinated dark slate palette with restrained cyan accents.
- Added thin accent lines to the toolbar, panel headers, and bottom tab strip to clarify the workspace zones without placing UI over the game view.
- Reduced the toolbar title's reserved width so common editing actions are less likely to be pushed off-screen; the existing horizontal toolbar scrolling remains available on narrower screens.
- Gave Snap, Duplicate, Delete, and Editor Mode distinct subdued colors, and aligned active gizmo/tab states with the new accent color.
- Added `UIBuilder.CreateWrappedText()` for descriptions that should wrap and grow vertically without changing the single-line behavior of existing button labels and compact form rows. Applied it to the selected-object heading and long source-object details in the Inspector.

### Files modified

- `Client/CustomEditorUI.cs`
- `Client/UIBuilder.cs`
- `CHANGELOG_SESSION.md`

### Validation and remaining checks

- Client Release build: `dotnet build .\Client\Client.csproj -c Release --no-incremental -p:SPTPath='E:\SPT_TRADER_QUEST_MAKER_4.1.X' --nologo` — **succeeded**, 0 errors and 60 warnings. Warnings were in unrelated existing files (`VanillaImporter.cs`, `RuntimeInteractiveObjectSpawner.cs`, `RuntimeNavMeshBuilder.cs`, and `ItemNameResolver.cs`); no diagnostics were reported for `CustomEditorUI.cs` or `UIBuilder.cs`.
- In-game validation has not been performed.
- Manually inspect the F8 menu at the normal display resolution and at a narrow window/UI scale; confirm toolbar scrolling remains usable, long Inspector headings/source details wrap without overlap, active states remain visible, and the center game view remains unobstructed.
- No DLL deployment was performed as part of this change.

## MEL Phase 1: Custom Bot Activity Zone editor — 2026-10-23

Scope: editor and data model only. No runtime bot spawning/AI code (`RuntimeBotSpawnSpawner.cs`), Write Mode, or bot spawn groups were touched. The Phase 1 prompt was truncated at "Verify how unknow…"; I read it as "unknown fields" and checked that unknown fields survive load/save.

### What was added
- New marker `BotActivityZone` (`MarkerKind.BotActivityZone`) with: `zoneId` (stable, `zone_xxxxxxxx`, separate from marker `id`), `enabled`, `shape` (`Circle`/`Square`), `radius` (circle, meters center-to-edge, default 25), `sideLength` (square, full side in meters, default 50), min size 0.5. Its own position/rotation. It does not reuse `botSpawnGroupId`, `BotSpawnGroup`, loot groups, `group`, or vanilla `BotZone`.
- `BotSpawnPoint.botActivityZoneId` (optional, default `""`) on the custom spawn point only. Vanilla spawn points are unchanged.
- Independence: no code recenters a zone when a spawn point moves or vice versa.

### Code paths changed
- `Client\MarkerTypes.cs`: kind, shape enum, `BotActivityZone`, `MapData.botActivityZones`, spawn-point field.
- `Client\MarkerManager.cs`: enumeration, add/delete/duplicate, create, lookup, `NormalizeActivityZones` (called from `LoadMap` and `SetMapData`: fills missing lists, regenerates empty/duplicate zone IDs keeping the first, fixes invalid sizes), fresh `zoneId` on duplicate/paste/prefab.
- `Client\MarkerRenderer.cs`: flat ground-plane visual (cylinder with convex trigger mesh collider for circle, trigger box for square), lime outline/fill, grey when disabled, thicker brighter outline plus a connector line when an assigned spawn point is selected, yaw-only rotation, shape-change rebuild. Picking: zones are lowest priority (any other marker hit or near its center wins), so a zone cannot steal clicks from spawn points. A zone is still selectable by clicking it where nothing else is, or from the hierarchy.
- `Client\MapEditorController.cs`: `CreateBotActivityZone`, clipboard and prefab cases.
- `Client\CustomEditorUI.cs`: Add > Spawn > "Bot Activity Zone"; zone inspector (ID, enabled, shape, size with semantics text, assigned spawn-point count); spawn-point inspector zone dropdown (with `(none)`), assigned label, Clear button, wrapped missing-reference warning (never silently reassigned).
- `Server\PackData.cs`: `BotActivityZone` record, `MapData.BotActivityZones`, `BotSpawnPoint.BotActivityZoneId` (shape is a string so unknown values cannot break pack loading). Nothing on the server reads them.
- `Tool\src\types.ts`, `MarkerLists.tsx`, `App.tsx`: types, defaults, "Activity Zones" tab, spawn-point zone selector with missing-zone warning, load-time defaults, unique `zoneId` enforcement.
- `Tool\dist\*`: regenerated with `npx vite build` (tracked bundle).

### Persistence
- Old packs without `botActivityZones` get an empty list (field initializer); old spawn points get `""`. Export serializes the full client `MapData`, so zones round-trip. The Tool import spreads `...map`, so unknown arrays are preserved.

### Validation
- Client Release build: succeeded, 0 errors, 60 warnings (same pre-existing count). Not deployed.
- Server Release build: succeeded, 0 errors, 3 warnings.
- Tool: `npx tsc --noEmit` 0 errors; `npx vite build` succeeded.
- No automated test framework exists in the repo; no tests added. No scripted JSON round-trip run.
- **Not tested in game.**

### Known limitations / risks
- Scale gizmo mode does nothing for zones; size is set in the inspector. Move and rotate gizmos work.
- Squares honor only yaw (rotation.y); the shared Rotation field still shows pitch/roll.
- Deleting a zone leaves spawn points with a dangling ID (warning shown, not cleared). Copying a zone and its spawn points leaves the copies pointing at the original zone.
- Server does not carry `navMeshAreas`/`blockers` either; the server never writes packs, so nothing is stripped.
- Zone trigger colliders are on layer 2 (as other markers); confirm in game they do not block the player.

### Manual in-game checklist
1. Add a Bot Activity Zone; switch Circle/Square, change size and Y rotation; the outline updates immediately.
2. Move it with the gizmo; assigned spawn points do not move. Move a spawn point; the zone does not move.
3. Select a custom spawn point; assign, change and clear the zone; the assigned zone highlights with a connector line.
4. Delete an assigned zone; the spawn point shows the missing-zone warning.
5. Click a spawn point inside a zone; the spawn point is selected, not the zone.
6. Save, reload and export/import a pack; zones and assignments persist. Load an old pack; no errors.
7. Check inspector text wraps and nothing clips.

Deployment: not deployed (EFT may be running; user to request).

## MEL Phase 2: Observe-only Bot Activity Zone tracking — 2026-11-01

### Ownership strategy
- Added opt-in `Diagnostics.EnableBotActivityZoneDiagnostics` (default `false`). When enabled, MEL stages only profile IDs created directly from MEL's `BotCreationData`, with the originating custom `BotSpawnPoint.id`, assigned `botActivityZoneId`, and a diagnostic source label.
- A `TryToSpawnInZoneAndDelay(...)` call is treated only as a request. A staged profile becomes registered only after its exact profile ID is seen in `BotSpawner.OnBotCreated` or independently found in `GameWorld.AllAlivePlayersList` as an AI bot. No proximity, role, faction, or zone-membership inference is used.
- Supported generated-profile paths: forced custom spawn points, triggered custom spawn points, and the selected custom spawn point for a `botSpawnGroupId` event. MEL-generated bosses and each MEL-generated follower are also tracked against the same originating custom spawn point and zone; each profile ID comes from its own generated `BotCreationData`.
- Intentionally untracked: `BotSpawnZone`/PMC-zone requests (no `botActivityZoneId` field), and Potential/SPT-selected bot spawns for which MEL has no exact generated profile ID. Unsupported/unknown boss and follower paths are not guessed.
- Zone IDs are indexed across packs for the current map. Missing, disabled, malformed, or duplicate/ambiguous IDs are skipped with a reason; duplicate IDs across packs do not choose an arbitrary zone.

### Lifecycle, containment, diagnostics
- Subscribes to SPT 4.1.6 `BotSpawner.OnBotCreated` and `OnBotRemoved` only while candidates exist. Decompilation confirmed `BotSpawner.BotDied(BotOwner)` invokes `OnBotRemoved`; exact-ID live-world reconciliation cleans confirmed registrations when the profile leaves the alive list.
- Clears candidate and registered profile IDs and detaches event handlers on `ResetState` (the existing `GameWorld.OnDestroy` reset path), world/controller replacement, component destruction, and diagnostics disablement. Late async continuations are rejected if their originating request is no longer current. Unobserved candidates time out after 900 seconds with a failure reason.
- Pure containment matches Phase 1: Circle uses `(bot.x-center.x)^2 + (bot.z-center.z)^2 <= radius^2`; Y and spawn-point radius are excluded. Square inverse-rotates the center-relative offset by zone yaw (`rotation.y`) and tests `abs(local.x) <= sideLength/2` and `abs(local.z) <= sideLength/2`. Pitch and roll are ignored.
- Samples are rate-limited to once per 10 seconds per registered bot, never per frame. Logs include profile ID, spawn-point ID, zone ID, path, bot position, zone center, shape/dimensions, inside/outside, enabled/valid state, and raid-relative registration time. Registration, cleanup, invalid-assignment, and timeout reasons are logged. Disabled-by-default diagnostics skip tracking and zone indexing.
- No bot movement commands, AI behavior changes, BigBrain layer, SAIN integration/patch, or activity-zone-related mutation of vanilla `BotZone` data was added. Existing MEL spawn-marker registration and read-only movement diagnostics are unchanged.

### Files changed
- `Client\Plugin.cs`: opt-in diagnostic configuration.
- `Client\RuntimeBotSpawnSpawner.cs`: exact-profile candidate staging, lifecycle callbacks, live-world confirmation/reconciliation, safe zone indexing, containment, and rate-limited samples.
- `Client\MarkerTypes.cs`: corrected comments; Phase 1 data schema is unchanged.

### Validation and status
- Client Release build: `dotnet build Client\Client.csproj -c Release --no-incremental -p:SPTPath=E:\SPT_TRADER_QUEST_MAKER_4.1.X --nologo -v:q` — **succeeded**, 0 errors, 60 warnings, matching the prior Phase 1 warning count. No warnings/errors were reported in changed files.
- No server or Tool files changed. No test project/test files were found; no framework was added. Containment was reviewed against Phase 1 semantics and compiled, but not automated-tested.
- No DLL deployment, installed SPT-file modification, or in-game testing was performed.

### Remaining risks and manual test checklist
1. Enable `EnableBotActivityZoneDiagnostics` before a raid, assign a Circle to a custom point, and confirm only its exact MEL-generated profile ID registers.
2. Repeat with a Square at nonzero yaw; test just inside/outside edges and corners. Verify Y and spawn-point radius do not affect containment.
3. Check forced, triggered, and grouped point paths; test a supported MEL boss and followers, confirming each generated ID shares the originating point/zone.
4. Confirm disabled, missing, malformed, and duplicate zone IDs produce a skip reason without crash or reassignment.
5. Confirm `BotSpawnZone`, PMC-zone, and Potential spawns remain unregistered; verify vanilla and third-party bots are never registered.
6. Observe death/removal and raid-reset cleanup, plus a pending request that never creates a bot. Verify 10-second sampling and cleanup when diagnostics are disabled.
7. Check SAIN/BigBrain remain unaffected and bot movement behavior is unchanged.

The 900-second candidate timeout favors avoiding stale profile associations over tracking a profile that appears after an unusually long delay. In-game verification is still required.
---
## Deployment note - Phase 1 + 2 client (2026-11-01)
- Release client build: 0 errors, 60 warnings (pre-existing).
- Deployed MapEditorLite.Client.dll to E:\SPT_TRADER_QUEST_MAKER_4.1.X\BepInEx\plugins (SHA256 83296F6AE528BF946CEA797534665A9D661A431157690EB49ED8F77A5BF876D1).
- Backup of previous DLL: MapEditorLite.Client.dll.pre-activityzones-20261101.bak (same folder). Older backups untouched.
- Server DLL/Tool not deployed (server mod folder not found; no server changes needed for activity-zone editing beyond optional PackData DTOs).
- Not tested in game.

---
## Phase 3 Step 1 - Ownership registry decoupled from diagnostics (2026-11-01)
- Previously `EnableBotActivityZoneDiagnostics` gated QueueActivityBot (no registration when off), the zone index build, and UpdateActivityZoneTracking (cleared all tracking when off).
- Now the ownership registry (exact profile-ID candidates, OnBotCreated/exact live-profile confirmation, OnBotRemoved, 10 s reconcile, 900 s candidate expiry, raid-reset/world-change/destroy cleanup) always runs. The setting only gates logging via ActivityLogInfo/ActivityLogWarn (exception-safe) and the per-sample containment log.
- Spawn safety: QueueActivityBot now wraps QueueActivityBotCore in try/catch so bookkeeping can never prevent TryToSpawnInZoneAndDelay. Spawn parameters/order unchanged. Zone index build at spawn start is try/caught too (lazy fallback in QueueActivityBotCore).
- Death: BotSpawner.BotDied invokes OnBotRemoved, which is already subscribed; no extra subscription added. Subscriptions are not duplicated (guarded by _activityObservedSpawner).
- No movement, BigBrain, SAIN, NavMesh, or geometry changes. Zone-less bots are still not registered.
- Build: Release client, 0 errors, 60 warnings (same count as before). Built with -p:SPTPath=E:\SPT_TRADER_QUEST_MAKER_4.1.X (read-only references). Not deployed. Not tested in game; no test framework in project.

---
## Phase 3 Step 2B - Bot Activity Zone soft leash (2026-11-01)
- New Client\ActivityZoneLeash.cs: BigBrain 1.5.0 custom layer "MEL Activity Zone Leash" (priority 5) + CustomLogic. Registered in Plugin.Awake only if `xyz.drakia.bigbrain` is loaded (soft BepInDependency; BigBrain types isolated in ActivityZoneLeashRegistrar, NoInlining). Brains: BossBully, BossGluhar, Killa, Tagilla, BossSanitar.
- Priority 5 chosen from installed assemblies: above PatrolAssault 0 / PatrolActions 2, below AdvAssaultTarget/AssaultTarget/SearchLayer 9, vanilla combat 50+, SAIN 69/70/80/85/99.
- Ownership: IsActive uses new read-only RuntimeBotSpawnSpawner.TryGetLeashAssignment (exact profile ID, Confirmed, not "-follower" path, zone exists/enabled/valid/unambiguous). Otherwise inactive (vanilla/third-party bots never leashed).
- Yield: inactive on enemy/under fire/10 s after enemy contact, during retry lock, and automatically whenever any higher layer is selected. No Pause/PatrolWays/SpawnPointMarkers/UnSpawnPoints changes; no teleport.
- Destinations: random footprint points (0.7 approach, 0.85 roam, 0.55 near edge), raycast floors, NavMesh.SamplePosition, XZ containment re-check, NavMesh.CalculatePath complete/reaches/not-detour, roaming floor within 4 m. Zone Y never used as destination height. Max 6 candidates / 4 path checks per round; failed-point memory; 5 s retry; 4 failed rounds -> 60 s*n lock.
- Movement: BotOwner.GoToPoint only when a destination is chosen; arrival 2 m, stall/timeout detection, 4-10 s pause between roams.
- Diagnostics (EnableBotActivityZoneDiagnostics, logging only): [MEL leash] control taken/released, destination selected/failed/arrived, inactive reasons, 10 s samples.
- Client.csproj: BigBrain reference enabled (Private=false). Plugin.cs: soft dependency + guarded registration.
- Build: Release, 0 errors. Not deployed. Not tested in game.

---
## Bot Activity Zone resizing - viewport handles + Inspector (2026-11-01)
- Editor only; runtime leash, BigBrain layer, NavMesh selection, ownership, and bot behavior untouched. No new colliders or ground-height dependencies.
- Viewport: selecting a single, unlocked, non-vanilla Bot Activity Zone shows 4 orange diamond handles on its boundary (zone-local +X/+Z/-X/-Z, yaw only). Handles are separate from the Move/Rotate/Scale gizmo, work in every gizmo mode, and are only picked within 16 px when no gizmo axis is hovered (gizmo wins ties). Circle: handle distance from center = radius. Square: handle distance from center x 2 = full side length (stays square, follows yaw). Drag is projected on the horizontal plane through the zone center with a grab offset (no jump). Center, rotation and assigned spawn points never change.
- Inspector: existing Radius / Side length fields kept; now shown with the valid range, clamped, and kept in sync with viewport drags (not overwritten while typing; out-of-range text is corrected on end-edit). Viewport reads the same values each frame, so Inspector edits update the boundary immediately.
- Limits (BotActivityZone): MinSize 0.5 m; MaxRadius 500 m; MaxSideLength 1000 m; NaN handled. Applied by MarkerManager.SetActivityZoneSize, ClampRadius/ClampSideLength, NormalizeActivityZones on load, and the Tool's zone form.
- Undo/redo: uses the existing MarkerManager snapshot stack (snapshot on drag start; one snapshot per Inspector edit session). Ctrl+Z/Ctrl+Y restore sizes.
- Persistence: radius/sideLength are the existing saved fields; no schema or server change.
- Files: Client\MarkerTypes.cs, MarkerManager.cs, MarkerRenderer.cs, MapEditorController.cs, CustomEditorUI.cs; Tool\src\MarkerLists.tsx.
- Build: Client Release 0 errors, 60 warnings (baseline). Server Release 0 errors/0 warnings (built to temp). Tool: tsc --noEmit and vite build pass (temp outDir). Not deployed. Not tested in game.

## UI/UX polish - Add Component scrolling, text overflow, responsive layout (2026-11-01)
- Add Component dialog: was a fixed 320x400 panel holding up to 50 unmasked 20px buttons in a fixed 300px panel, so results overflowed the panel and screen. Now width/height are clamped to the canvas each frame, with fixed title/search/status/Close and a masked ScrollRect for results (cap 200, 'N of M' status, scroll reset on search, search focus on open, included in editor-UI hit testing).
- Text: new UITextOverflow.cs (ellipsis + hover tooltip for truncated labels/buttons/toggles), measured field label widths, wrapped read-only/status/warning/output texts, masked input fields, wrapped render-distance dialog text.
- New UIFlowLayout.cs: Inspector button/shape rows now wrap instead of clipping.
- Client only. No data model, leash, NavMesh, BigBrain, SAIN or bot code changed. Nothing deployed; no visual/in-game validation performed. Client Release: 0 errors, 60 warnings (same as baseline). Tool not touched.

## Object Picker - selection reliability and camera-radius scan (2026-11-01)
- GameObject tab: list rows for same-named objects now show [parent #instanceID]; sort tie-breaks on instance ID (stable order); destroyed objects are pruned before totals/paging; clicking a destroyed entry shows a rescan message instead of selecting anything else; selecting an object (e.g. from the viewport) jumps the list to its page.
- New radius-limited scan: Radius toggle, custom meters field, 5/10/25/50 presets and Entire button beside Scan Scene (default 10 m when enabled; off by default). Center is the editor camera (free cam when active, else main camera) via MapEditorController.TryGetEditorCameraPosition, never the player. Range (closest point of renderer bounds) is tested during discovery so out-of-range objects are never stored, outlined or sorted. Status label shows snapshot radius/center and flags stale settings or camera movement.
- Only the GameObject tab is affected. No bot, leash, NavMesh, BigBrain, SAIN or data-model changes; nothing deployed. Client Release: 0 errors, 60 warnings (baseline). Not visually/in-game tested. Tab relocation NOT implemented (proposal only).

## Resizable and collapsible bottom workspace (2026-11-01)
- Bottom workspace height is now driven by one value (BottomPanelHeight property in CustomEditorUI.cs) that the bottom panel, hierarchy, inspector and side resize handles all read; clamped 180-420 px and to the screen (leaves room for the top bar + 160 px viewport), re-synced each frame on screen/scale changes.
- Draggable handle on the workspace top edge (orange on hover/drag, grip mark, vertical-resize cursor); it stays visible when collapsed and dragging it opens the workspace. Clicks on it are swallowed by the UI so they don't reach viewport selection.
- Hide/Show button collapses the workspace to a 40 px tab strip (content hidden, viewport space returned); expanding restores previous height and tab. Clicking a tab while collapsed expands it.
- Tabs are larger (30 px row, 84/112 px wide, 12 pt), active tab is bold/highlighted, strip scrolls horizontally if too narrow so all six stay reachable.
- Collapsed state, height and active tab persist in MapEditorLite/workspaceLayout.json next to scenePickFilters.json.
- UI only. No bot, leash, NavMesh, BigBrain, SAIN or data-model changes; nothing deployed. Client Release: 0 errors, 60 warnings (baseline). Not visually/in-game tested.

## Hybrid viewport object picking + hover highlight (2026-11-01)
- Picking (CustomEditorUI.ResolveViewportPick): collects all physics hits via RaycastNonAlloc (buffer grows if full) and ray-vs-renderer-bounds candidates of scanned objects (MarkerRenderer.CollectRendererRayCandidates: cached combined box as cheap test, then refined against each live renderer's own bounds). Both are compared by distance along the camera ray; nearest wins. Terrain colliders and path-filtered hits are skipped; players/bots/loose loot/editor objects occlude anything behind them. Ties (0.1 m): descendant over ancestor, collider over bounds for the same object, smaller volume, then instance ID. Use Parent behaviour preserved. Replaces the old first-hit-only logic and the screen-proximity/ambiguity fallback (PickSceneObjectNearScreen is now unused).
- Hover: runs at ~16 Hz in every object-picking mode (scene GO, source, list target, scatter), never over UI; target switches need 2 consecutive wins; click confirms the hovered object. Colours: green discovered, cyan hover, red selected (selected outranks hover). SetHoveredSceneObject is now change-only.
- Opt-in diagnostics: new config Diagnostics/EnableScenePickDiagnostics logs candidates, distances, rejection reasons and the final pick on clicks only.
- Client only. No data model, scan/radius, bot, leash, NavMesh, BigBrain or SAIN changes; nothing deployed. Client Release: 0 errors, 60 warnings (baseline, none new). Not visually/in-game tested.

## Overlapping-object picking: visible-first ranking + click-to-cycle (2026-11-01)
- ResolveViewportPick now gathers all valid candidates (deduped per object), drops those behind occluders, and ranks them: objects whose own renderer the ray hits rank above collider-only volumes, then nearest/tie rules. Large invisible collider boxes no longer beat small visible objects (e.g. cables on the ground).
- Clicking again within ~6 px / 6 s cycles to the next candidate behind the previous one; the log reports '#n of m'. Diagnostics list the ranked candidates.
- Client only; nothing deployed. Client Release: 0 errors, 60 warnings (baseline). Not visually/in-game tested.
- Refinement: visible-first preference now applies within a 1.5 m window (a renderer-hit candidate beats a collider-only one up to 1.5 m farther); beyond that nearest wins. Collider hits are no longer assumed visible; only objects whose own renderer bounds the ray hits (or that also have a collider hit) count. Build: 0 errors, 60 warnings.

## Door hierarchy diagnostic capture (2026-11-01)
- Diagnostic only; picking behaviour unchanged (ResolveViewportPick, ranking, bounds, physics, Use Parent, scan, terrain, placement untouched).
- New config Diagnostics/DoorDiagnosticKey (default F10). With Diagnostics/EnableScenePickDiagnostics on and the editor open, pressing it while aiming runs the existing resolver once (its [PICK] block shows candidates/rejections) and then DoorDiagnostics.Capture, which logs a [DOOR-DIAG] block: all ray hits incl. triggers, door components (WorldInteractiveObject/Door, found by component type, never by name) hit by or within 1 m of the ray or on the picked object, and the subtree around each door (parent subtree if <=150 nodes, cap 250) with per-node id, layer, transform, colliders (enabled, isTrigger, bounds, ray hit), renderers, MeshFilter mesh info, LODGroup levels, WIO/Door state, other component types, scan-cache owner, validity/rejection and whether it was the final pick.
- One-shot per key press, read-only, exceptions caught. New file Client/DoorDiagnostics.cs; small hooks in CustomEditorUI, MapEditorController, Plugin.
- Client Release: 0 errors, 60 warnings (baseline). Not deployed; not tested in game.

## Scene Pick Filters: segment-based matching (2026-11-01)
- Old: every filter was a case-insensitive substring of the full hierarchy path, so default filter 'LOD' rejected everything under 'SOO_LOD0' (doors, walls, floors). Defaults (SPAWNS, LOD, BornPositions), scenePickFilters.json format, persistence and the dialog are unchanged; defaults are not rewritten.
- New (Client/PickFilterMatcher.cs, used by CustomEditorUI.ShouldIgnorePick): matching is per path segment, case-insensitive. A filter without '/' matches a segment that equals it or starts with it followed by a non-letter (LOD, LOD0, LOD_Objects, 'LOD (1)', 'spawns!'); it no longer matches SOO_LOD0, Wall_LOD1, LODGroup_x or Blod. A filter containing '/' is an explicit path fragment and stays a substring match on the full path.
- Diagnostics: rejections now say which filter matched and which path segment (e.g. filter 'LOD' matched path segment 'LOD0'); DoorDiagnostics validity uses the same text.
- Behaviour note: custom single-word filters that relied on mid-name substrings (e.g. 'tree' matching 'big_tree') now need to match the start of a segment; use a '/' fragment for path substrings.
- No viewport picking, ranking, hover, scan, door or other changes. Matcher verified with a throwaway console test (16 cases incl. SOO_LOD0, LOD/, LOD_Objects, LODGroup_Object; all pass; removed afterwards, repo has no test project). Client Release: 0 errors, 60 warnings (baseline). Not deployed; not tested in game.

## Door locked/unlocked state support (2026-11-01)
- Audit: placed InteractiveObject doors already stored initialDoorState (Locked/Shut/Open) + keyId in the pack and applied them at spawn by setting WorldInteractiveObject.DoorState/InitialDoorState/FallbackState/CurrentAngle after Instantiate (same mechanism as EFT's own WorldInteractiveObject.Lock(); OnEnable has already snapshotted InitialDoorState by then). Server PackData already round-trips both fields. Gaps: child doors inside copied static objects only supported keyId (no state), no locked-without-key warning on doors, and state parsing accepted arbitrary enum names.
- New Client/DoorStateHelper.cs (TryParse Locked/Shut/Open, Apply mirrors EFT sequence) used by RuntimeInteractiveObjectSpawner (behaviour unchanged for valid values; unknown/empty -> Shut as before) and RuntimeStaticObjectSpawner.
- ChildInteractableData.initialDoorState (default empty = leave vanilla untouched; old packs load unchanged). Applied to child Door components after the keyId override; warns if Locked with no key.
- Inspector: placed doors get a locked-without-key warning (same pattern as containers); child doors get an Initial Door State dropdown (Default/Locked/Shut/Open) plus warning. The existing 'Initial Door State' field is reused, no duplicate control. Key Template Id / Key Id untouched; EFT interaction and unlock logic unchanged.
- Preview objects do not apply door state (preview only). Server unchanged (client reads child overrides from the pack JSON). No test project exists; no automated tests added.
- Client Release: 0 errors, 60 warnings (baseline). Server Release: 0 errors, 0 warnings. Not deployed; not tested in game.

## Root door lock state for copied door objects (2026-11-01)
- Static/WTT-clone objects whose copied source is itself a door (door component on the root, e.g. *_door) had no lock controls; only child doors did. Added rootKeyId/rootDoorState to StaticObject and WTTStaticObject (empty = vanilla; old packs unchanged), inspector Key Id + Initial Door State (Default/Locked/Shut/Open) + locked-without-key warning (shown only when the source has a door component), and RuntimeStaticObjectSpawner.ApplyRootDoorOverride. Locking sets DoorState on the door component, so the separate 'Lock' child object is not required.
- Client Release: 0 errors, 60 warnings. Not deployed; not tested in game.

## Boss activity-zone boundary enforcement, all bosses (2026-11-01)
- Audit (installed Assembly-CSharp layer strategies): the leash was only registered for BossBully, BossGluhar, Killa, Tagilla, BossSanitar. Knight (brain 'Knight'), the Goon escorts (BigPipe, BirdEye) and other bosses (KillaAgro, TagillaAgro, BossBoar, BossKolontay, BossZryachiy, BossKojaniy, BossPartisan) had no leash, and every MEL-spawned follower was explicitly excluded ('follower-not-leashed'), so the Goons ran vanilla BirdEyePatrolLayer (30-100 m cover patrol) and could leave the zone.
- Registrar now covers all boss brains plus escort brains. Priority 5 (above vanilla patrol 0-2, below all combat/request/SAIN layers); brains whose lowest vanilla layer is higher get a per-brain priority (BossKojaniy 39, FollowerKojaniy 10, FlBoarCl 21, BossPartisan 68).
- Ownership unchanged: exact MEL profile ID + confirmed + assigned zone. TryGetLeashAssignment now reports isFollower instead of rejecting followers. Followers use boundary-only mode: the layer activates only when they are outside the zone (hysteresis, released at 85% of the edge) and guides them back inside; inside the zone vanilla follow/patrol continues. Bosses keep full roaming.
- Roaming paths are now validated: every NavMesh path corner must stay inside the (convex) zone (8% slack); otherwise the candidate is rejected ('path-leaves-zone'). A roaming destination is abandoned if the bot crosses the boundary and a return-inward destination is chosen immediately.
- Stale destinations: on taking control the leash clears the bot's mover target (StopMove) so a vanilla/SAIN/combat destination outside the zone is not resumed. Retry locks shortened (30 s x min(count,3)) so bosses spend less time on vanilla behavior after path failures.
- No teleporting, colliders or barriers; yields to combat/SAIN as before. Client Release: 0 errors, 60 warnings. Not deployed; not tested in game.

## Boss zone leash diagnostics follow-up (2026-11-01)
- Opt-in diagnostics (Bot Activity Zone diagnostics config) only; no behaviour change. Samples now include role, brain, follower flag, edgeRatio, leash=ACTIVE, returningToZone, destination and destInsideZone. 'Destination ACCEPTED' lines report destEdgeRatio and whether path corners were checked. Yield lines ('YIELDING ... reason=yield-has-enemy / under-fire / recent-enemy-contact / retry-lock / follower-inside-zone') now include role, brain, edgeRatio, inside and the active AI layer (logged once per state change, not per frame). Logging is independent of ownership tracking and exception-safe.
- Client Release: 0 errors, 60 warnings. Not deployed; not tested in game.

## Leash diagnostics: periodic status for followers (2026-11-01)
- Escorts run boundary-only, so inside the zone their leash layer is inactive and only logged one 'YIELDING reason=follower-inside-zone' line, which looked like they were untracked. Every MEL-owned bot that is not being steered (escorts inside the zone, combat holdoff, retry lock) now also logs a throttled (10 s) '[MEL leash] Status ... leash=YIELDING(reason), role, brain, edgeRatio, inside, activeLayer' line when activity-zone diagnostics are on. No behaviour change. Vanilla/non-MEL bots (no zone) never produce Status lines.
- Client Release: 0 errors, 60 warnings. Not deployed; not tested in game.

## Escort pull-in/walk-out loop fix (2026-11-01)
- Log evidence: Big Pipe's vanilla PtrlBirdEye patrol repeatedly chose a point just outside the zone edge (edgeRatio ~1.0); the boundary-only leash pulled him to ~0.5, released him at 0.85, and the patrol sent him straight back out (take/release every few seconds).
- Escorts now stay under the leash whenever they are not in combat/yielding (no release-at-edge). They trail their own boss: candidate destinations are a 3-8 m ring around the boss, kept inside 90% of the zone; an escort within 9 m of its boss waits ('escort-holding'). Boss anchor = live, confirmed, non-follower MEL bot with the SAME spawn point ID (exact ownership, never proximity). If the boss is missing or outside 90% of the zone, escorts use zone-footprint destinations. Combat/SAIN yielding unchanged.
- Client Release: 0 errors, 60 warnings. Not deployed; not tested in game.

## Loose Loot Zone: surface-baked placement (2026-11-01)
- Root cause (investigation): zone positions were picked on the SERVER (LootTransformer/WttSpawnConverter RandomPointInShape) from only the zone's numbers: X/Z random, Y always the marker's Y, no world knowledge, no raycasts, no slope/overlap/spacing checks, items independent (could overlap). Spawn points work because the author places each at a chosen Y; zones had nothing equivalent, so loot floated/sank at the marker height and could land inside geometry; shelves/tables were never found. Zone rotation is also ignored by the server fallback (not changed).
- Fix: editor-time 'Bake Surface Points' (Client/LootZoneSurfaceBaker.cs). Samples the footprint (Circle/Box, zone yaw) with bounded dart-throwing (<= 24 attempts/point, 3000 max), casts down through the zone's vertical range (height + 0.5 m padding) collecting EVERY surface level (floor, shelves, tables, crates), rejects steep (default 40 deg), obstructed/embedded (capsule clearance), outside-height and too-close (default 0.4 m) hits, stores points 2 cm above the surface in zone.surfacePoints. Server gives each item a different baked point per raid (no sharing); empty list = legacy behaviour, so old packs are unchanged.
- Inspector: Bake points, Min spacing, Max slope, Bake/Clear buttons, status line with per-reason reject counts; green/red temporary sphere markers (no colliders, layer 2). Config Diagnostics/EnableLootZoneDiagnostics logs every candidate (ray origin, hit, normal/slope, reason). Editor preview of 'all items in zone' uses baked points. Tool types.ts gained the optional fields.
- Client Release: 0 errors, 60 warnings. Server Release: 0 errors. Not deployed; not tested in game.

## Free camera speed controls (2026-11-01)
- New config Camera/FreeCamSpeed (0.5-300 m/s, default 5) and Camera/FreeCamFastMultiplier (1-20, default 3 = the old 15 m/s Shift speed). In the free camera, [ / ] or Alt+mouse wheel scale speed by 1.25x / 0.8x and persist it to config (logged). Alt+wheel no longer dollies. Defaults match previous behaviour. Client Release: 0 errors, 60 warnings. Not deployed; not tested in game.

## Export As: existing-pack picker (2026-11-01)
- The Export As dialog now has an 'Existing' dropdown listing every .json in the SPT mod exports folder; picking one fills the pack name (export already merges the current map into an existing pack). Typing a new name still works. Client Release: 0 errors, 60 warnings. Not deployed; not tested in game.

## 2026-11-01 - Empty custom safe diagnostics
- Added loot-source and injection diagnostics to RuntimeInteractiveObjectSpawner (InitializeContainerLootCoroutine, InjectMarkerItems). Silent zero-item paths now log a warning. No behavior change; root cause not yet confirmed. Client Release: 0 errors, 60 warnings. Not deployed, not tested in game.

## 2026-11-01 - Fix empty Default-mode container with blank item row
- RuntimeInteractiveObjectSpawner: Default mode no longer treats a blank-template custom item row as real loot, so the template's vanilla staticLoot distribution (with Min/Max) is used. Root cause: pack held one item entry with template ''. Client Release: 0 errors, 60 warnings. Not deployed, not tested in game.

## 2026-11-01 - Suppress duplicate vanilla boss waves
- Client: Harmony prefix on BossSpawnScenario.Run (after BotsController.Init) removes the map's vanilla boss waves whose BossType matches a boss MEL is spawning from a rolled custom spawn point, so e.g. a second vanilla Goons squad no longer spawns elsewhere. New config Bots/SuppressVanillaBossWaves (default true). Only matching boss types are affected; trigger-activated MEL spawns and non-MEL bosses are untouched. Client Release: 0 errors, 60 warnings. Not deployed, not tested in game.

## 2026-11-02 - Sprint back to activity zone
- ActivityZoneLeash.Tick: bots (and escorts) sprint while travelling to their zone from outside it (more than 10% beyond the edge, stamina permitting), then walk once inside. Roaming stays a walk. New config Bots/SprintToZone (default true). Client Release: 0 errors, 60 warnings. Not deployed, not tested in game.

## 2026-11-02 - Strong Leash Phase 1: zone leashMode data model and editor selector
- New BotActivityZone.leashMode (string "Soft" | "Strong"). Missing, blank or unrecognized values normalize to "Soft" (MarkerManager.NormalizeActivityZones on load/import; new zones default to "Soft"). Client BotActivityZone, server PackData.BotActivityZone (LeashMode, default "Soft") and Tool types.ts (BotActivityLeashMode, defaultBotActivityZone) updated so the field round-trips through save/load, pack import/export and the web tool.
- In-game inspector: new "Leash" row (Soft Leash / Strong Leash) under the Shape row; web tool: "Leash mode" select. Strong shows a note that it is not enforced yet.
- Strong runtime enforcement has NOT been implemented: no runtime/AI changes (ActivityZoneLeash.cs untouched); Strong zones behave exactly like Soft.
- Builds: Client Release 0 errors, 60 warnings; Server Release 0 errors, 3 warnings; Tool tsc --noEmit clean; vite build OK. Not deployed, not tested in game.

## 2026-11-02 - Strong Leash Phase 2: runtime policy, red zone visuals, diagnostics
- Runtime: Strong zones (leashMode=Strong) now use the SAME ActivityZoneLeashLayer (priority unchanged, no second layer) with a Strong policy in ActivityZoneLeash.cs. Soft zones take the unchanged code paths (all Strong branches are gated by ActivityZoneLeashCore.IsStrong(zone)).
- Strong differences: combat holdoff 3.5s (Soft 10s); roam targets from an inner area (factor 0.6, 0.4 near the edge; recovery 0.5) and destinations must be within edge ratio 0.85; path corner slack 1.03 (Soft 1.08); bot approaching the edge (>0.9) with a destination that doesn't lead inward clears it and re-plans (3s cooldown hysteresis); a new destination is never within 4m of the previous one; recovery from outside uses the existing NavMesh sampling/path validation and sprint-to-zone. No teleporting; failures use the existing retry/lock logic.
- Interception: NOT added. No Harmony patch on BotMover.GoToPoint/GoToByWay - SAIN patches BotMover and the caller (SAIN vs vanilla) cannot be identified reliably. Containment during SAIN combat is NOT guaranteed; MEL corrects after the higher-priority layers yield.
- Compatibility: no BigBrain/SAIN changes, no brain registration changes (supported: the already-registered boss/escort brains; regular scavs/PMCs/raiders/cultists remain unsupported).
- Editor: unselected Strong zones render red (fill and wire, Circle and Square); selection highlight and disabled grey take priority; Soft unchanged (MarkerRenderer.ApplyColor).
- Diagnostics: reuses the Bot Activity Zone diagnostics toggle; new "[MEL StrongLeash]" lines (control taken/yielding with layer and reason, inside/outside transitions, approaching-boundary clears, roam/recovery accepted/failed with NavMesh/path results, give-up locks, 10s samples). Throttled; exceptions swallowed.
- Builds: Client Release 0 errors, 60 warnings. Server and Tool not changed. Not deployed. In-game validation is still outstanding.

## 2026-11-02 - Strong Leash label fix
- Removed stale 'not enforced yet' text from the web tool Leash mode option and the in-game inspector note (Strong is enforced since Phase 2). Client 0 errors, 60 warnings; Tool tsc and vite build OK. Not deployed.

## 2026-11-02 - Strong Leash combat containment (SAIN movement validation)
- Investigation (SAIN 4.5.1 decompile): SAIN moves bots only via SAINMoverClass.RunToPoint/WalkToPoint/RunToPointByWay/WalkToPointByWay (cover, pursuit, suppress, regroup, shift-cover, grenade, flash all call these; GoToCoverPoint delegates to the ByWay methods; TriggerRecalcPath re-enters Run/WalkToPoint). SAIN has no public API to limit pursuit area (SAINExternal exposes none) and its own Harmony patches target BotMover (SetPosition/ManualUpdate/Sprint), not these methods. The mover only receives a destination, not the reason; the calling action is identifiable via BotComponent.CurrentAction (class name).
- New StrongLeashContainment.cs: Harmony prefix on those four SAIN methods (found by name at runtime, SAIN stays an optional dependency, installed once from the leash layer constructor). Applies only to exact MEL-owned profile IDs whose assigned zone is Strong. Destination inside the zone: untouched. Outside: replaced with a NavMesh point along the zone-center->requested line at 90% (fallback 70%) of the edge, validated with the existing ValidatePath (Strong slack, detour limit); the ByWay variants get a freshly calculated path (SAIN's cover path objects are never mutated). If no reachable inside point exists the request is refused (returns false; SAIN keeps its behavior), cached 1.5s. Results cached 1s per bot (bounded NavMesh work). AvoidGrenadeAction and FlashedAction are never constrained. Soft zones, vanilla and third-party bots are untouched. Exceptions fall back to allowing the original request.
- New config Bots/StrongLeashCombatContainment (default true, kill switch, read at game start). Diagnostics: [MEL StrongLeash] Method=... Action=ALLOWED|REPLACED|REJECTED with requested/replacement destination, edge ratios, reasons and active layer, state-change and 5s throttled, under the existing Bot Activity Zone diagnostics toggle.
- Known limits: path corners en route can still leave the zone if the destination is inside; the replacement is not cover-aware (a boss whose only cover is outside holds at the zone edge); a boss pushed outside is pulled back toward the zone while fighting; SAIN version changes to these method names disable the feature (logged). Soft Leash code paths unchanged.
- Client Release: 0 errors, 60 warnings. Not deployed. In-game validation outstanding.

## 2026-11-02 - Soft Leash BigBrain handoff flapping fix (re-entry debounce + rapid-stop cooldown)
- Cause: MEL's Soft combat holdoff reads vanilla BotOwner.Memory, which can go stale while SAIN still holds a combat decision. MEL took control for a tick, issued a destination, and BigBrain (highest priority active layer wins) immediately stopped it via CustomLayerWrapper.Stop(), cancelling the move.
- Soft zones only (Strong code paths, containment, serialization, UI untouched). Per-bot state in LeashState (ConditionalWeakTable, no shared or stale state). No SAIN reflection, priority unchanged.
- Re-entry debounce 3s: after a stop, MEL needs 3s of continuous IsActive evaluations in which it is otherwise eligible (assignment, no retry-lock, no combat holdoff). Any other eligibility failure, or a gap >1.5s between IsActive calls (IsActive is not called while a higher layer is active), resets the run. No delay once control is regained.
- Rapid-stop cooldown: stop within 2s of taking control by something other than MEL's own IsActive=false (detected as: last evaluation this frame returned false) sets a 5s cooldown. It overlaps with the debounce (no stacking) and is replaced, not extended, by repeated stops.
- Diagnostics (existing Bot Activity Zone toggle, state-change only): "SoftHandoff stopped" (held time, rapidStop, cooldown, cause; the newly selected layer is not accessible in Stop(), so it is reported as unverified), debounce STARTED/RESET/COMPLETED, cooldown ACTIVE, control REGAINED.
- Client Release: 0 errors, 60 warnings. Not deployed; not tested in game.

## 2026-11-02 - Soft Leash: combat yields now count as rapid stops; debounce/cooldown diagnostics
- Audit finding: RecordSoftStop excluded every voluntary yield (IsActive false in the same frame as Stop) from rapid-stop detection, so a take-control followed by a quick combat yield only got the 3s debounce.
- LeashState.LastYieldReason is cleared at the start of every IsActive evaluation and set by SetInactive, so it only ever describes the evaluation that caused the stop (per bot, never stale). A stop now qualifies as rapid when held < 2s and it was either not a self-yield (higher-priority layer, unchanged) or a self-yield with reason yield-under-fire or yield-recent-enemy-contact. Retry-lock, lost assignment, other yields stay excluded; has-enemy is not in the requested set and stays excluded. Timing values unchanged (2s window, 5s cooldown, 3s debounce); the cooldown is still set (replaced, not extended) only at the stop and only delays MEL re-entry.
- Diagnostics: "SoftHandoff stopped" cause now shows the yield reason (mel-isactive-false(yield-under-fire)); SetInactive logs "debounce RESET ... reason=..., eligibleFor=...s" once when it clears a running debounce; "rapid-stop cooldown EXPIRED" is logged once, on the first IsActive evaluation after expiry (evaluations pause while a higher layer holds control).
- Soft only (Strong zones skip all of this); no Harmony/SAIN/serialization/UI changes. Client Release: see build line below.
- Build result: Client Release 0 errors, 60 warnings (baseline). Not deployed; not tested in game.


## 2026-11-02 - Optional BotGen integration (client only)
- New `Client/NpcProviders.cs`: small `INpcProvider` / `NpcProviderRegistry` plus `BotGenNpcProvider`. No BotGen assembly reference; BotGen stays optional.
- Discovery is file based: BotGen's `roles.json` (next to `BotGen.dll`, `BOTGEN_ROLES_PATH` honoured) with `version` 1 required and role-name/id-range validation; `config.json` beside it supplies nicknames and follower links. Missing/malformed/unsupported files disable BotGen options with a `[MEL BotGen]` log line; no identifiers are invented.
- Followers are not a separate BotGen type: an NPC listed in another NPC's `followers` is classed as a follower and shown under "BotGen Followers".
- Editor: bot spawn point, zone and random-group inspectors get "BotGen" / "BotGen Followers" dropdowns (only when BotGen is detected; otherwise a "not detected" note). Picking stores the exact `roleName` in `wildSpawnType` (Savage side, Boss category for boss roles). The "Wild Type" text field is kept, and a warning shows when a stored `botgen_*` value is unavailable.
- Runtime (`RuntimeBotSpawnSpawner.ResolveWildSpawnType`, `GetFollowerPlan`, new `GetBotGenFollowerPlan`): a `botgen_*` type spawns only if it is in roles.json, active, and registered in the running game's `WildSpawnType` enum; otherwise the spawn is skipped with a warning (no vanilla substitution). BotGen boss followers use the follower roles from BotGen's config through the existing follower pipeline.
- Compatibility: no pack/server/Tool format change; `wildSpawnType` stays a string, so old packs and packs with BotGen ids load unchanged when BotGen is absent.
- Build: client Release 0 errors, 60 warnings (baseline). Server and Tool unchanged, not rebuilt. Not deployed or tested in game.


## 2026-11-02 - Quest-driven NPC deployments (V1)
- Data model: per-map `deployments` list (`id`, `name`, `activationQuestId`, `deactivationQuestId`) on `MapData`; optional `deploymentId` on `BotSpawnPoint` and `BotSpawnZone` (not `PmcSpawnZone`). Mirrored in `Client/MarkerTypes.cs`, `Server/PackData.cs` and `Tool/src/types.ts`. Missing fields default to empty, so old packs load unchanged; no migration. The web tool spreads map objects, so it preserves the data without a UI.
- Logic (`Client/DeploymentGate.cs`, new): Active = activation quest Success AND NOT withdrawal quest Success. Distinguishes Missing (empty id or not in profile) from Incomplete. Missing activation, or a configured-but-missing withdrawal quest, fails closed (inactive). Withdrawal wins. Unknown/duplicate/empty deployment ids fail closed with a warning. Quest state comes from the player's profile `QuestsData`, the same source as `QuestConditionsMet`; no MEL progression store.
- Timing: each map's deployments are resolved once per raid start in `RuntimeBotSpawnSpawner.SpawnCustomMarkers` (new `BuildDeploymentGate`) and reused per entry. Quest changes take effect next raid; no polling, no despawning, no new Harmony patches.
- Combination: SpawnEligible = existing quest gate (questOnly OR questCompleted, unchanged) AND deployment gate. Applied to bot spawn points and zones before they enter `_spawnRequests`; random groups and followers inherit their parent's eligibility. Role resolution is untouched, so vanilla and BotGen NPCs behave the same. Force-player-spawn points are not deployment-gated.
- Diagnostics: `[MEL Deployment]` lines per deployment (quest states and result) and per skipped entry.
- Editor (`CustomEditorUI.BuildDeploymentSection`): in the bot spawn point and zone inspectors, a Deployment dropdown (None + named deployments), a warning for a dangling reference (the value is kept), and per-map deployment add/edit/delete.
- Server: preserves the new fields only; no server-side quest evaluation.
- Builds: client Release 0 errors, 60 warnings (baseline); server Release 0 errors; Tool `tsc --noEmit` clean and `vite build` OK.
- Tests: the repo has no test project. Ran a throwaway harness (since deleted) on the real `DeploymentGate.cs`: 15/15 passed, covering the quest-state matrix, unknown/null ids and independent deployments, plus a JSON round-trip.
- Not verified: in-game behaviour, that quest ids match the profile's quest ids, and the editor UI itself. Nothing deployed.

## 2026-11-03 - Universal Activity Zone leash and unified BotGen selection
- Leash registration now discovers loaded concrete `BaseBrain` strategies and registers their exact `ShortName()` values with BigBrain, rather than relying on a fixed boss/escort-only list. State-dependent names are discovered by decoding IL instructions and collecting `ldstr` operands; existing strategy-specific priority overrides remain.
- Strong Leash SAIN destination containment is initialized independently of BigBrain registration. Spawn diagnostics now report the bot's base brain, BigBrain layer registration, active layer, movement API availability, and SAIN adapter status.
- Bot Type is a single workflow for spawn points, zones, and random groups. BotGen bots and followers are labeled distinctly, exact `botgen_*` role identifiers are preserved, legacy values are recognized, and unavailable roles produce warnings without vanilla substitution. Added the BotGen preset to client/server/web serialized enums.
- Build validation: Client Release succeeded with 0 errors and 60 warnings; Server Release succeeded with 0 errors and 3 warnings; Tool `tsc --noEmit` and `vite build` succeeded.
- Not verified in game. The referenced raid log was not available in the accessible workspace, so the specific raid's behavior could not be confirmed from that log. Nothing was deployed and the game was not launched.

## 2026-11-03 - Persistent map packs and duplicate source identity
- Added `Client/RuntimePackLoader.cs` to load JSON packs consistently from both `packs` and `exports`, prefer `packs` when filenames collide, and log invalid files or scan failures without aborting other pack loads. Runtime pack-driven systems reload their data from `GameWorld.OnDestroy`, so pack changes can be applied on the next raid without restarting the game. `Server/PackLoader.cs` also discovers exports and avoids loading a same-named exported copy when the pack copy is present.
- Added `Client/AtomicFileWriter.cs` and routed local saves and pack exports through temporary-file replacement. Exports keep a `.bak` backup, report write failures, and refuse to replace an existing unreadable export. Added Newtonsoft extension-data storage to client pack models so unknown fields survive an edit/save cycle.
- Added optional `sourceWorldObjectId` references for interactive objects, static objects, WTT static objects, and cut volumes. The editor captures the ID when the source has a `WorldInteractiveObject`; runtime matching prefers that exact ID and logs before falling back to legacy name/position matching. Older packs without the field continue to use their existing matching behavior. Mirrored the optional fields in `Server/PackData.cs` and `Tool/src/types.ts`.
- Raid teardown now resets the NavMesh builder alongside the other runtime systems. Related changes are in `Client/JsonStorage.cs`, `Client/MarkerTypes.cs`, `Client/MapEditorController.cs`, `Client/CustomEditorUI.cs`, `Client/VanillaImporter.cs`, `Client/RaidEndResetPatch.cs`, `Client/RuntimeInteractiveObjectSpawner.cs`, `Client/RuntimeStaticObjectSpawner.cs`, `Client/RuntimeCutVolumeSpawner.cs`, and the other pack-driven runtime spawners.
- Validation: Client Release build succeeded with 0 errors and 60 warnings; Server Release build succeeded with 0 errors and 3 warnings; Tool `tsc --noEmit` and Vite production build succeeded. No automated persistence tests were available. Unity reconstruction, door locking, and multi-raid behavior remain unverified in-game. Nothing was deployed and the game was not launched.

## 2026-11-03 - Unified pack discovery and precedence (Phase 1)

- Added equivalent deterministic candidate resolution to `Client/RuntimePackLoader.cs` and `Server/PackLoader.cs`. Both scan the current `exports` directory before `packs`, then retain the supported `MapLoot` and legacy locations at lower priorities. Candidates are sorted by priority and path; parsing must succeed before a candidate can suppress another.
- Duplicate copies are suppressed by filename identity, declared pack name (or filename when absent), and exact content hash. Byte-identical aliases reserve their identities so a different lower-priority file cannot subsequently be selected under that alias. Invalid or unreadable files are logged and skipped.
- Distinct selected packs are aggregated by normalized map identity; all maps in a pack remain available, and map-entry lists avoid repeated non-empty marker IDs. Editor map loading and client runtime consumers use that resolved aggregate. Server continues to use its existing DTOs and WTT conversion path.
- `PackRegistry.Register` now replaces its snapshot rather than appending duplicate registrations.
- The export dialog's pack name remains an export destination, not a source-pack selector. The editor loads the same automatic aggregate as client runtime; no new source-selection setting or pack manifest was introduced. When no resolved pack contains the map, the editor still falls back to local editor data, which is an editor draft and is not runtime input.
- No test project or automated test harness exists in this repository, so selection scenarios were not covered by automated tests. Client Release build succeeded with 0 errors and 60 warnings; Server Release build succeeded with 0 errors and 3 warnings. Warnings are in existing unrelated code. No files were deployed and the game/server were not launched.

## 2026-11-03 - BotGen registry refresh and deleted-role handling

- Replaced the client-side `roles.json`/`config.json` discovery path with BotGen's `POST /botgen/registry` API. The endpoint defaults to `http://127.0.0.1:6969/botgen/registry` and is configurable through the MEL `Integrations.BotGenRegistryUrl` setting.
- MEL now refreshes once during plugin startup and exposes a manual refresh button in BotGen spawn selectors. Requests are bounded by a timeout and are not polled.
- Registry snapshots are validated as version 1 and checked for malformed entries, duplicate role names/enum IDs, and compatible follower/registration fields. MEL publishes a replacement only after complete validation, filtering selectable roles to those marked active and template-registered with a matching client `WildSpawnType`.
- A successful empty response is authoritative and clears all selectable NPCs. Request or schema failures preserve the last successful snapshot, mark it stale, disable selection/spawning until a successful refresh, and retain known labels for saved role references.
- BotGen follower role IDs are resolved from the API's NPC-ID references. Spawn validation continues to require exact current provider availability, and unavailable follower roles are skipped without changing saved map data or substituting vanilla NPCs. Role identifiers remain the serialized `wildSpawnType` values; no name-based remapping was added.
- Verified the API route and response contract by inspecting the installed BotGen 0.1.0 assembly and `roles.json`; BotGen source was not modified. No automated test project exists. The first client Release build succeeded with 0 errors and 60 warnings (none in changed files); the final incremental Release build succeeded with 0 warnings and 0 errors. The live endpoint and in-game UI were not tested; nothing was deployed and SPT was not launched.

## 2026-11-03 - BotGen configured SPT backend transport and bounded startup retry

- Added `Client/BotGenRegistryTransport.cs`. Automatic discovery reads the verified installed SPT API `SPT.Common.Http.RequestHandler.Host`, preserving its configured HTTP/HTTPS scheme, host, port and base path. The current installation resolves to `POST https://127.0.0.1:6970/botgen/registry`; no port or scheme is hardcoded as the new default.
- Requests reuse SPT's `Client.CreateNewHttpRequest`, shared `HttpClient`, session factory and `Zlib` framing helpers. MEL sends the empty `{}` JSON model with JSON content/accept headers and a 12-second cancellation deadline. This deliberately uses the underlying supported SPT facilities instead of `PostJsonAsync`: the installed wrapper hides HTTP status, omits the content type and performs immediate retries. No custom certificate handler or TLS bypass was added, and global SPT transport settings are not changed.
- Added empty-by-default `Integrations.BotGenBackendUrl` for an optional trusted backend override. HTTP(S) only; credentials, query strings, fragments and repeated registry routes are rejected. A pasted full registry URL is normalized to avoid appending the route twice. The deprecated `BotGenRegistryUrl` is consumed and cleared: its known old default migrates to automatic discovery, custom values migrate when the new override is empty, and a non-empty new setting takes precedence. Invalid overrides fail explicitly, without fallback or logging their raw value.
- Startup now waits 2 realtime seconds, then makes at most three single-request attempts with 10- and 30-second waits after failures. A manual inspector refresh supersedes a pending startup retry schedule. Provider loading guards prevent overlapping requests; the existing manual button uses the same provider/transport as startup.
- Diagnostics record method, effective sanitized backend, endpoint path, discovery/override mode, result classification, HTTP status when available and elapsed time. Transport failures preserve exception types/HResults and socket/WebException codes without logging potentially session-bearing exception messages/stacks or sensitive headers. Invalid JSON, incompatible schema, HTTP errors, unavailable backends, connection refusal, timeout, valid empty registries and validated populated responses remain distinguishable.
- Snapshot publication still occurs only after complete response validation. Failed requests/validation preserve the last snapshot but mark it stale and unspawnable; valid empty registries clear available choices. Saved role IDs/known labels, exact follower references, missing/deleted/inactive role checks, warning deduplication and no-vanilla-fallback behavior remain intact. No changes to BotGen, vanilla spawning, pack data, role allocation or retirement.
- Added persistent isolated checks under `Tests/BotGenRegistryChecks`, linking the production transport/provider code with SPT/Unity shims and the installed Newtonsoft.Json assembly. Release console harness: **102 checks passed**, including automatic HTTPS/HTTP and custom-port selection, overrides/migration, POST/JSON/framing, diagnostics, stale/empty/recovery behavior, invalid/duplicate data, exact/missing followers, no overlapping requests, bounded retry and manual supersession. No real network, SPT or game process is used by these checks.
- Final affected-project build: `dotnet build Client\Client.csproj -c Release -p:SPTPath="E:\SPT_TRADER_QUEST_MAKER_4.1.X" --no-restore` succeeded with **0 errors and 60 existing warnings**: 59 CS0649 warnings in `ItemNameResolver`, `RuntimeInteractiveObjectSpawner` and `VanillaImporter`, plus one CS0169 in `RuntimeNavMeshBuilder`. No warnings in changed client files. Server and Tool were unchanged and not rebuilt.
- Updated README with automatic backend selection, trusted overrides, legacy migration, retry policy, diagnostics and validation commands. No DLLs deployed, installed configuration edited, or SPT/game launched. Live TLS/session/middleware compatibility, Unity coroutine timing and in-game selection/spawning still require verification after deployment; the isolated harness does not claim to test the real SPT transport.

## 2026-11-04 - Strong Leash recovery, independent zone handles, and patrol routes

- Strong Leash SAIN interception now tracks repeated equivalent outside-zone destinations together with the bot's position. Recovery is offered only after at least five repeated requests over 1.5 seconds while the bot remains near the boundary and moves less than 0.8 m. It samples a nearby inward NavMesh destination, validates a complete path, caches it for a five-second cooldown and does not teleport or reset AI. Unapproved path corners outside a Strong zone are contained even when the requested endpoint is inside; single requests, moving bots and grenade/flash exemptions retain their existing handling.
- Square activity zones now persist independent `width` and `depth` dimensions. Old `sideLength` data initializes both dimensions, and the legacy field remains synchronized to the larger dimension for older editors. Square resize controls now provide four corner and four edge handles: corners resize both dimensions around the opposite corner; edges resize one dimension around the opposite edge. Circle zones retain one radius handle. The in-game inspector and web tool expose the rectangular dimensions.
- Bot Activity Zones now optionally store an ordered, enabled patrol route of persistent patrol-point IDs, positions, optional destination-zone IDs and dwell durations. The in-game inspector supports enabling routes, adding/removing/reordering points, selecting destination zones and editing position/dwell values. Selected routes render labeled points and connecting lines.
- Patrol points can now be created or repositioned directly in the viewport: choose the activity zone's "Place New Patrol Point" or a point's "Place Point in Viewport" action, then click a world surface. Placement is bound to the selected activity zone, can be cancelled with Esc, and only commits after a valid surface hit.
- Visible patrol-point markers can also be dragged directly in the viewport to move them; each drag is undoable, updates the point's world-surface position, and remains tied to the selected activity zone.
- Runtime patrol state is scoped to the exact confirmed MEL-owned `BotOwner` and assigned zone. Each leg requires a complete NavMesh path; Strong Leash temporarily allows the bot only along that path and within its explicit destination area. The authorization is revoked if the bot leaves both the assigned zone and its authorized route, and cached paths are rebuilt when a live point or destination-zone geometry changes. Arrival is position-based, retries are bounded, dwell starts after arrival and pauses while combat holds higher-priority control. The route returns to its assigned zone after its final point. Bot removal and raid/world teardown revoke runtime authorization; timers are not saved.
- Patrol fields are backward-compatible optional pack data. Client normalization initializes older square dimensions and validates patrol IDs/dwell values; server DTOs and Tool types preserve the new fields. No BotGen source, BigBrain priorities or SAIN combat decisions were changed.
- Validation: Server Release build succeeded with 0 errors and 0 warnings. Tool `tsc --noEmit` succeeded. Extended `Tests/BotGenRegistryChecks` with legacy-zone-default and new-dimension/patrol JSON round-trip checks; Release harness build succeeded with 0 errors and one CS8632 nullable-context warning from linked `Server/PackData.cs`, and **105 checks passed**. Client Release build against `E:\SPT_TRADER_QUEST_MAKER_4.1.X` succeeded with 0 errors and 60 warnings in existing files; none of the changed client files emitted warnings. No isolated tests cover the Unity-dependent leash or patrol runtime. In-game verification remains required. The built DLL remains in the project build output only; no DLLs were deployed and SPT/game were not launched.
