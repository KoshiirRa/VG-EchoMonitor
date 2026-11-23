# Vanguard Galaxy Modding — Agent Guidance

This workspace (`c:\Users\<user>\Documents\Vanguard Galaxy Modding`) is the development, reverse-engineering, and asset-catalog project for modding **Vanguard Galaxy** by Bat Roost Games.

## Workspace & Game Paths

| Role | Path | Access Policy |
| :--- | :--- | :--- |
| **Modding Workspace** | `c:\Users\<user>\Documents\Vanguard Galaxy Modding` | Read / Write |
| **Steam Game Install** | `c:\Program Files (x86)\Steam\steamapps\common\Vanguard Galaxy` | **READ-ONLY** (see policy below) |
| **Game Data & Managed DLLs** | `c:\Program Files (x86)\Steam\steamapps\common\Vanguard Galaxy\VanguardGalaxy_Data` | **READ-ONLY** |
| **Saves & Runtime Logs** | `%USERPROFILE%\AppData\LocalLow\Bat Roost Games\VanguardGalaxy` | **READ-ONLY** unless explicitly requested |

## Safety Policies

1. **Read-Only Steam Policy**: Never modify, overwrite, or delete files inside `c:\Program Files (x86)\Steam\steamapps\common\Vanguard Galaxy` unless the user explicitly asks to install/configure BepInEx or deploy a built mod DLL into `BepInEx\plugins\`. All analysis, decompilation, and asset-extraction scripts must read game files strictly read-only.
2. **Save-File Safety**: Never modify or overwrite `*.save` or `*.meta` files in `AppData\LocalLow\Bat Roost Games\VanguardGalaxy\Saves` in place without explicit user confirmation and a timestamped backup copied outside `Saves/` first.
3. **No Global `pip install`**: When Python packages beyond the standard library are needed (such as `UnityPy` or `TypeTreeGeneratorAPI`), install them inside a project-local virtual environment (`.venv`) rather than globally.

## Game Technical Profile

- **Engine**: Unity `6000.4.7f1` (Unity 6)
- **Scripting Backend**: **Mono** (`MonoBleedingEdge`, x64) — not IL2CPP
- **Managed Target**: `.NET Standard 2.1` (`netstandard.dll` 2.1.0.0 / `mscorlib.dll` 4.6.57.0)
- **Primary Game Assembly**: `VanguardGalaxy_Data\Managed\Assembly-CSharp.dll`
- **Mod Framework**: **BepInEx 5** (x64) + **HarmonyX** (`HarmonyLib`)
- **Asset Archives**: `VanguardGalaxy_Data\resources.assets`, `sharedassets*.assets`, `globalgamemanagers`
- **Localization**: `en-US` `TextAsset` in `resources.assets` (`key=value` lines; `@Key` references in code/prefabs)
- **Save Format**: GZip-compressed UTF-8 JSON (`*.save`) paired with a JSON version sidecar (`*.meta`)
- **Runtime Logs**: `Player.log`, `Player-prev.log` (in `AppData\LocalLow\Bat Roost Games\VanguardGalaxy\`), plus `BepInEx\LogOutput.log` when BepInEx is installed

## Project Conventions

- **Suggested Layout**:
  - `mods/<ModName>/` — Individual BepInEx 5 C# mod projects (`.csproj`, `Plugin.cs`, `Patches/`)
  - `tools/` — Shared reverse-engineering or data-processing scripts
  - `exports/` — Decompiled code dumps, extracted JSON/CSV tables, or decompressed save snapshots (keep out of Steam dir)
  - `.agents/skills/` — Project-specific agent skills and helper scripts
- **Line Endings**: Use LF (`\n`) for all source code, scripts, and markdown files.
- **Build & Decompilation Offloading**: The local Windows host (`<windows-host>`) does not have a modern .NET SDK on `PATH` (only x86 runtimes and x64 .NET Core 3.1). Offload heavy `.NET` builds (`dotnet build`) and `Assembly-CSharp.dll` decompilation (`ilspycmd`) to `dev-vm` (`<user>@<dev-host>`, `/path/to/projects/...`) when available, or use a configured .NET 6/8+ SDK targeting `netstandard2.1`.

## Project Skills

Use the project skills under `.agents/skills/` whenever their domain applies:

- **`vanguard-galaxy-ships`** — Query, filter, compare, and re-extract the catalog of all 151 playable spaceship prefabs (`resources/playable_ships.json`).
- **`vanguard-galaxy-bepinex-mod`** — Scaffold, configure, build, and deploy BepInEx 5 + Harmony C# mods for Vanguard Galaxy.
- **`vanguard-galaxy-save-inspector`** — List, summarize, explore, search, and decompress Vanguard Galaxy GZip JSON `.save` files read-only.
- **`vanguard-galaxy-assets-and-code`** — Inspect/decompile `Assembly-CSharp.dll`, extract Unity 6 assets with `UnityPy` + `TypeTreeGeneratorAPI`, resolve localization keys, and triage runtime logs.

## Verified Game Mechanics (Do Not Assume Otherwise)

- **Drones Tree vs. Hangar Bay Craft**: The **Drones** skill tree (`dronesSpeed`, `dronesDefenses`, `dronesFasterDeploy`, `dronesRebuildIncrease`/`2`, `dronesAmount`/`2`) and `EquipStat.DronePower` apply **only** to `Drone` units and `DroneBayModule`. They do **not** affect Size 1 ships deployed from a `HangarBayModule`, which are spawned as full `SpaceShip` instances (`isCarrierFighter = true`). Hangar bays only read `EquipStat.HangarBayRepairRate` and the Engineering unique node `T3HangarBayStella` (`uniqueEngineeringHangarBaySlot`, +1 hangar slot); the deployed ships themselves use their own hull/equipment stats and standard `SpaceShip` skill trees.
