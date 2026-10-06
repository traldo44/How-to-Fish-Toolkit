# Solo How to Fish Toolkit

A Windows desktop helper for offline single-player sessions in **How to Fish**. It enables the game's built-in solo cheats and restores the original game assembly after the game closes.

## Features

- Invincibility and one-hit toggles
- Cash and euro shortcuts
- Island, boat, grill, creature, boss, and item commands
- F8 panel visibility hotkey
- Offline-only patch: online lobby creation, online joining, friend joining, and invite joining entry points are disabled while the patch is active. The game's localhost solo path remains available. Steam itself is not forced offline.
- Automatic backup and restoration of `Assembly-CSharp.dll`

## Requirements

- Windows x64
- How to Fish installed through Steam
- .NET 8 Windows Desktop Runtime

## Run the packaged build

1. Copy `release/SoloHowToFishToolkit-Windows-x64/game-path.example.txt` to `game-path.txt` in the same folder.
2. Edit `game-path.txt` so it contains the full path to the game's install directory (the directory containing `How to Fish.exe` and `How to Fish_Data`).
3. Keep the entire package folder together and launch `HowToFishTrainer-InputFix.exe`.
4. Use **Enable Cheats and Launch Game**, then enter an offline single-player world.

Do not launch the game first. The patch must be applied before the game starts. The game assembly backup is restored when the game process exits. If the panel reports a version mismatch, do not force the patch; restore the backup or verify the game files through Steam.

## Build from source

With the .NET 8 SDK installed, run:

```powershell
dotnet publish .\SoloHowToFishToolkit.csproj -c Release -r win-x64 --self-contained false -o .\publish
```

## Package contents

The `release` folder contains the panel and its .NET runtime dependencies. It does not include or redistribute any game files.

## Limitations

The online block disables the game's known online lobby and invite entry points while the panel patch is active. It does not disable Steam networking globally. Game updates may change the methods the patch expects; in that case the tool should refuse to patch rather than apply an unsafe change.