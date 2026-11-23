---
name: vanguard-galaxy-save-inspector
description: >-
  Safely inspect, list, summarize, explore, search, and decompress Vanguard
  Galaxy GZip-compressed JSON save files (*.save / *.meta) in read-only mode.
  Use when analyzing player state, fleet/ship data, blueprints, faction
  reputation, or save-file schemas for modding or debugging.
---

# Vanguard Galaxy Save File Inspector

Vanguard Galaxy stores saves under `%USERPROFILE%\AppData\LocalLow\Bat Roost Games\VanguardGalaxy\Saves\`. Each save consists of:

- `<name>.meta` — Plain UTF-8 JSON containing `{"Version":"x.y.z.w"}`.
- `<name>.save` — **GZip-compressed UTF-8 JSON** with top-level keys `Version` and `Player` (90+ fields covering ships, fleet, crew, inventory, blueprints, missions, factions, map, and progression).

> **Save-File Safety**: [inspect_save.py](./scripts/inspect_save.py) is strictly read-only against the game's `Saves/` directory. Never modify or overwrite `.save` or `.meta` files in place without explicit user confirmation and a timestamped backup first.

## Quick Start

Run [inspect_save.py](./scripts/inspect_save.py) with Python 3 (standard library only):

```powershell
# List all available saves with version, size, and modified timestamp
python ".agents\skills\vanguard-galaxy-save-inspector\scripts\inspect_save.py" --list

# Print a high-level summary of a save (accepts save name without .save or full path)
python ".agents\skills\vanguard-galaxy-save-inspector\scripts\inspect_save.py" --summary autosave-0

# Inspect nested JSON structure at a dot-separated path
python ".agents\skills\vanguard-galaxy-save-inspector\scripts\inspect_save.py" --keys autosave-0 Player.spaceShips.0

# Search keys and string values (case-insensitive) across a save
python ".agents\skills\vanguard-galaxy-save-inspector\scripts\inspect_save.py" --search autosave-0 "Oxlo"

# Decompress a save to pretty-printed JSON inside exports/ for inspection or diffing
python ".agents\skills\vanguard-galaxy-save-inspector\scripts\inspect_save.py" --dump autosave-0 --out exports/autosave-0.json
```

## Key `Player` Sections for Modding

- **Fleet & Ships**: `spaceShips` (full ship objects with GUIDs, HP, fitted modules), `activeFleet` (list of ship GUIDs), `capturedShips`, `fleetLoadouts`, `currentSpaceShip`
- **Economy & Progression**: `credits` (string), `vanguardMarks` (string), `empireLevel` (`level`, `experience`), `patrolRank`, `bountyRank`, `industryRank`, `escalationLevel`
- **Crew & Personnel**: `commander`, `crewMembers`, `unassignedCrew`, `stationPrisoners`
- **Crafting & Inventory**: `globalInventory`, `dataInventory`, `blueprints` (dict of recipe -> count), `unlockedShipBlueprints` (list of prefab/ship names)
- **World & Diplomacy**: `factionData` (list of `{f1, f2, reputation}` pairs — filter `f1 == "Player"` or `f2 == "Player"` for player standings), `map`, `routes`, `supplyRoutes`, `missions`, `currentSystem`
