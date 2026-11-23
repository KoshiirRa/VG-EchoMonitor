---
name: vanguard-galaxy-ships
description: >-
  Query, filter, compare, and inspect Vanguard Galaxy playable spaceship prefabs
  by role, size, ship class, manufacturer, faction, module slots, hardpoints,
  boosters, HP scales, cargo, tonnage, warp speed, and unlock requirements.
  Use when searching for playable ships, comparing ship stats, or finding ship
  prefab names and Unity resource paths for modding.
---

# Vanguard Galaxy Playable Ships Lookup

This skill provides a pre-extracted catalog of all **151 playable spaceship prefabs** from Vanguard Galaxy's `resources.assets` (`Resources/SpaceShips/...`), along with a zero-dependency query script and a read-only re-extraction script.

> **Read-Only Steam Policy**: Never modify anything in `c:\Program Files (x86)\Steam\steamapps\common\Vanguard Galaxy`. All scripts read game assets strictly read-only.

## Quick Start: Querying Ships

Run [query_ships.py](./scripts/query_ships.py) with Python 3 (standard library only; queries [playable_ships.json](./resources/playable_ships.json)):

```powershell
# Show catalog counts by role, size/class, manufacturer, and faction
python ".agents\skills\vanguard-galaxy-ships\scripts\query_ships.py" --summary

# Find Combat ships of Size4 or Size5 with a DroneBay, sorted by hardpoints
python ".agents\skills\vanguard-galaxy-ships\scripts\query_ships.py" --role Combat --size 4 5 --has-module DroneBay --sort hardpoints --desc

# Find top 10 Cargo or Mining ships by cargo capacity under level 30
python ".agents\skills\vanguard-galaxy-ships\scripts\query_ships.py" --role Cargo Mining --max-level 30 --sort cargo --desc --limit 10

# Inspect full prefab details (module slots, hardpoints, boosters, resource path)
python ".agents\skills\vanguard-galaxy-ships\scripts\query_ships.py" --name "Oxlo" --detail

# Export a filtered subset as JSON or CSV
python ".agents\skills\vanguard-galaxy-ships\scripts\query_ships.py" --role Salvaging --format json
```

## Available Query Filters

### Core Identity
- `--name <text>`: Substring match against `displayName` or `prefabName`
- `--role <role...>`: `Combat`, `Mining`, `Salvaging`, `Cargo`
- `--size <size...>`: `1`–`6` (or `Size1`–`Size6`); also `--min-size`, `--max-size`
- `--ship-class <class...>`:
  - **Cargo**: `Courier` (1), `Ferry` (2), `Hauler` (3), `Freighter` (4), `Carrack` (5)
  - **Combat**: `Cutter` (1), `Gunship` (2), `Corvette` (3), `Frigate` (4), `Destroyer` (5), `Battlecruiser` (6)
  - **Mining**: `Skiff` (1), `Hewer` (2), `Dredger` (3), `Breaker` (4), `Harvester` (5), `Barge` (6)
  - **Salvaging**: `Skiff` (1), `Scow` (2), `Scrapper` (3), `Wrecker` (4), `Ravager` (5), `Barge` (6)
- `--manufacturer <mfr...>`: Matches in-game manufacturer name (substring) or internal ID — e.g. `Spirit Design` (`Gold`), `RedStar Forge` (`Red`), `Stellar Industries` (`Stellar`), `Akai Armory` (`EliteCombat`), `Frontier` (`GenericCombat`), `Utixon Corp` (`Utility`), `Obrix` (`DarkspaceRegular`), `Mindus Manufacturing` (`Mining`), `Marade Wharf` (`Pirate`), `Varlis Construct` (`FanaticsAlien`), `Custos Arms` (`Police`), `Malcyte`, `Kharon Forgeworks` (`Umbral`)
- `--faction <faction...>`: Matches in-game faction name (substring) or internal ID — e.g. `Any`, `Luminate Combine` (`Gold`), `Kolyatov Collective` (`Red`), `Stellar Industries` (`Blue`), `Corsair Syndicate` (`Marauders`), `Meridia's Chosen` (`Fanatics`), `Orsanon Security` (`BountyGuild`), `Mindus Holdings` (`MiningGuild`), `Darkspace Compact` (`Darkspacers`), `Steel Vultures` (`SalvageGuild`), `Canisec` (`PoliceGuild`), `Malcyte` (`Amalgam`), `Stranded`, `Intertrade Network` (`TradingGuild`), `Umbral Reach` (`Puppeteers`)

### Combat, Defense & Slots
- `--has-module <slot...>`: Require all listed module slots (`Reactor`, `ShieldGenerator`, `Armor`, `Engine`, `DroneBay`, `MiningSystem`, `SalvageSystem`, `TractorBeam`, `Scanner`, `TorpedoBay`, `Hull`, `HangarBay`)
- `--hardpoint-size <size>`: Require at least one hardpoint of size `Tiny`, `Small`, `Medium`, or `Large`
- `--min-hardpoints <n>` / `--max-hardpoints <n>`
- `--min-boosters <n>`
- `--min-hull <f>` / `--min-armor <f>` / `--min-shield <f>` (base HP scales from `AbstractUnit`)

### Utility & Economy
- `--min-cargo <n>` / `--max-cargo <n>`
- `--min-tonnage <n>` / `--max-tonnage <n>`
- `--min-officers <n>` / `--min-warp <f>`
- `--min-level <n>` / `--max-level <n>`
- `--min-points <n>` / `--max-points <n>` (fleet capacity points / `pointValue`)
- `--blueprint-only`: Exclude special ships with `noBlueprint = true`

### Sorting & Output
- `--sort <field>`: `size` (default), `name`, `role`, `cargo`, `hull`, `armor`, `shield`, `tonnage`, `hardpoints`, `boosters`, `officers`, `points`, `warp`, `level`, `cost`
- `--desc`: Reverse sort order
- `--limit <n>`: Return top `n` results
- `--detail`: Full multi-line breakdown including every module slot, hardpoint, booster, and Unity resource path
- `--format <table|json|csv>`: Output format (default `table`)

## Refreshing Data After a Game Update

If Vanguard Galaxy updates on Steam, re-extract [playable_ships.json](./resources/playable_ships.json) (strictly read-only) using [extract_ships.py](./scripts/extract_ships.py) in a Python virtual environment with `UnityPy` and `TypeTreeGeneratorAPI`:

```powershell
python -m venv .venv
.\.venv\Scripts\pip install UnityPy TypeTreeGeneratorAPI
.\.venv\Scripts\python ".agents\skills\vanguard-galaxy-ships\scripts\extract_ships.py"
```

Non-playable prefabs (`_base spaceship`, `autonomousdrone/*`, and `salvageable/*`) are automatically excluded during extraction.
