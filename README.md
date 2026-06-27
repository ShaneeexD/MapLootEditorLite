# MapLootEditorLite

A lightweight client-side BepInEx mod for SPTarkov that lets you place and edit loot spawn, loot zone, and static object markers directly in raid.

## Structure

```
MapLootEditorLite/
├── Client/                 # BepInEx client plugin
│   ├── Client.csproj
│   ├── Plugin.cs
│   ├── MapEditorController.cs
│   ├── MarkerManager.cs
│   ├── MarkerRenderer.cs
│   ├── EditorUI.cs
│   └── ...
├── MapLootEditorLite.sln
└── README.md
```

## Setup

1. **Set SPT Path**

   Make the `SPTPath` environment variable point to your SPT installation:
   ```
   SPTPath=C:\Games\SPT-4.0.1
   ```

2. **Build**

   ```powershell
   dotnet build "MapLootEditorLite.sln" -c Release /p:SPTPath=%SPTPath%
   ```

   Output: `Client/bin/Release/net471/MapLootEditorLite.Client.dll`

## Installation

Copy `MapLootEditorLite.Client.dll` to `SPT/BepInEx/plugins/`.

## Usage

- Press the configured hotkey (default `F8`) to open the editor GUI.
- Use the hotkeys or the F12 BepInEx Configuration Manager buttons to place markers.
- Markers are saved automatically every 30 seconds and on close.

## Resources

- **SPT Discord**: https://discord.gg/Xn9ms4saGa
- **BepInEx Docs**: https://docs.bepinex.dev/

## License

Use this template freely for your SPT mods.
