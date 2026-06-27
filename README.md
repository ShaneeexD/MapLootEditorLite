# MapLootEditorLite

A lightweight SPTarkov framework that lets players and modders place custom loot spawns, loot zones, and static objects in raid, then export them as shareable loot packs. The client handles the editor and preview; the server mod injects the loot into the SPT raid loot tables so it works like regular server loot. Forced spawns are handled by WTT-CommonLib, making them ideal for quest items.

## What it does

- **Editor (client)**: in-raid BepInEx tool for placing markers, previewing items, and exporting loot packs.
- **Web tool**: React app for editing packs, item TPL mapping, and importing spawns copied from the in-game editor.
- **Server mod**: reads the pack JSON and injects the loot into the server's raid loot tables.
- **WTT-CommonLib integration**: forced/quest spawns are registered through WTT-CommonLib's `CustomLootspawnService`.
- **Modder friendly**: other mods ship pack JSON files; the server mod loads them automatically.

## Structure

```
MapLootEditorLite/
├── Client/                 # BepInEx client plugin (editor + preview)
├── Server/                 # SPT server mod (loot injection)
├── Tool/                   # Web app (Vite + React + TypeScript + Tailwind)
├── MapLootEditorLite.sln
└── README.md
```

## Setup

1. **Set SPT Path**

   ```
   SPTPath=C:\Games\SPT-4.0.1
   ```

2. **Build the plugin**

   ```powershell
   dotnet build "MapLootEditorLite.sln" -c Release /p:SPTPath=%SPTPath%
   ```

   Output:
   - `Client/bin/Release/net471/MapLootEditorLite.Client.dll`
   - `Server/bin/Release/net9.0/MapLootEditorLite.Server.dll`

3. **Build the web tool**

   ```powershell
   cd Tool
   npm install
   npm run build
   ```

## Installation

### Client plugin

Copy `MapLootEditorLite.Client.dll` to `SPT/BepInEx/plugins/`.

### Server mod

Create `SPT/user/mods/MapLootEditorLite/` and copy `MapLootEditorLite.Server.dll` and `package.json` into it.

```
SPT/user/mods/MapLootEditorLite/
├── MapLootEditorLite.Server.dll
└── package.json
```

The server mod requires **WTT-CommonLib** (`com.wtt.commonlib`) to be installed as a server mod. The SPT server mod loader will load it automatically because of the declared dependency.

On first launch the client mod creates:

- `SPT/BepInEx/config/com.maplooteditorlite.client.cfg` — BepInEx configuration (editor toggle, hotkeys, etc.)
- `SPT/BepInEx/config/MapLootEditorLite/` — client data folder
  - `editor/`          # in-raid editor saves
  - `spawns/`          # built-in spawn directory
  - `imports/`         # files imported into the tool
  - `cache/`           # cached item data
- `SPT/user/mods/MapLootEditorLite/exports/` — packs exported from the in-raid editor (temporary)
- `SPT/user/mods/MapLootEditorLite/packs/` — final user packs for the server to load

## Configuration

Edit `SPT/BepInEx/config/com.maplooteditorlite.client.cfg`:

```ini
[General]
EnableEditor = true
EnableDebugVisuals = false
```

- `EnableEditor` — set to `true` while developing. When `false`, the F8 editor is disabled.
- `EnableDebugVisuals` — show extra debug visuals in raid.

## Usage

### For players

1. Install the client plugin and the server mod.
2. Place final loot packs in any of these locations:
   - `SPT/user/mods/MapLootEditorLite/MapLoot/`
   - `SPT/user/mods/MapLootEditorLite/packs/`
   - `SPT/user/mods/MyCustomMod/MapLoot/`
   - `SPT/BepInEx/config/MapLootEditorLite/exports/` (legacy)
   - `SPT/BepInEx/config/MapLootEditorLite/spawns/` (legacy)

The server mod loads every `.json` pack in those folders and injects the loot into the matching map.

### For creators

1. Make sure `EnableEditor` is `true` and restart the client.
2. In raid, press `F8` to open the editor.
3. Place markers with hotkeys or the F12 buttons.
4. Enter a pack name and click **Export Pack** in the editor window.
5. The pack is written to `SPT/user/mods/MapLootEditorLite/exports/`.
6. Open the **MapLootEditorLite Tool**, import the pack, tune spawn chances, and mark any quest items as **Forced (Quest)**.
7. Move the final pack into `SPT/user/mods/MapLootEditorLite/packs/` (or your own mod's `MapLoot/` directory).

Forced spawns are handled by **WTT-CommonLib** and are guaranteed to spawn, making them ideal for quest items.

## Pack format

Packs are JSON files that can contain multiple maps:

```json
{
  "name": "My Loot Pack",
  "author": "Your Name",
  "version": "1.0.0",
  "maps": {
    "customs": {
      "map": "customs",
      "lootSpawns": [
        {
          "id": "abc123",
          "name": "loot_spawn",
          "position": { "x": 0, "y": 0, "z": 0 },
          "rotation": { "x": 0, "y": 0, "z": 0 },
          "items": [
            { "template": "544fb45d4bdc2dee738b4568", "chance": 100 },
            { "template": "5c052e6986f7746b207bc3c9", "chance": 50 }
          ],
          "spawnChance": 100,
          "respawnable": false,
          "forced": false
        }
      ],
      "lootZones": [ ... ],
      "objects": [ ... ]
    }
  }
}
```

Each spawn/zone has an `items` list. Every item has its own `chance` (0–100) and is rolled independently, so the total does not need to add up to 100 and the spawn can still end up empty. Old packs using `itemTpls` are automatically migrated.

## Resources

- **SPT Discord**: https://discord.gg/Xn9ms4saGa
- **BepInEx Docs**: https://docs.bepinex.dev/

## License

Use this template freely for your SPT mods.
