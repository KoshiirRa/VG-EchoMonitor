# EchoEnhanced (BepInEx 5 Mod for Vanguard Galaxy)

Automation and quality-of-life companion plugin for **Vanguard Galaxy** (Unity `6000.4.7f1`, Mono, `.NET Standard 2.1`).

## Features

### Milestone 7 — Auto-Favorite Modules
Automatically marks newly acquired equipment/modules in player inventories (active ship cargo, fleet ship cargo, and global/station storage) as **Favourite** (`Inventory.InventoryItem.favourite = true`) so they are protected from ECHO auto-sell and accidental selling/extraction.

- **Hot-reloading JSON rules**: `BepInEx\config\EchoEnhanced\auto_favorite_rules.json` (auto-created with starter examples on first run).
- **Criteria supported per rule**:
  - `categories`: `Turret`, `Module`, `Booster`, `Drone`, `DefensiveTurret` (defaults to all equippable categories if omitted).
  - `nameContains` / `nameExcludes`: Case-insensitive substring matching against localized display name or item identifier.
  - `minRarity` / `maxRarity`: `Standard`, `Enhanced`, `HighGrade`, `Exotic`, `Legendary`.
  - `minItemLevel` / `maxItemLevel`: Item level (`iLvl`) bounds.
  - `hasAspect` / `aspectContains`: Filter by aspect presence or aspect name/description.
  - `substats` (**Workshop Reroll-Aware "N of M"**):
    - `minMatches`: Minimum number of patterns from `anyOf` that must match rolled substats.
    - `anyOf`: List of desired substat substrings (e.g., `["Turret Rotation Speed", "Critical Damage", "Energy Damage", "Reload Speed"]`).
    - `requireAll`: Optional substats that must all be present.
- **Dry-Run Mode**: Set `DryRun = true` in `BepInEx\config\EchoEnhanced.cfg` to test rules in the log without modifying items.
- **On-Demand Rescan**: Press `Ctrl+F9` (configurable via `RescanHotkey`) to reload rules and scan all player inventories immediately.

### Milestone 9 — Auto-Sell Modules
Automatically sells non-favourited equipment/modules matching user-defined rules in `BepInEx\config\EchoEnhanced\auto_sell_rules.json` (auto-created with disabled starter examples on first run).

- **Safety Guards**:
  - **Never sells favourited items** (`entry.favourite == true`).
  - **Auto-Favorite priority**: Auto-Favorite always runs before Auto-Sell, and Auto-Sell skips any item matching an enabled Auto-Favorite rule.
  - **Safe defaults**: `AutoSell.Enabled = false`, `AutoSell.DryRun = true`, `AutoSell.ArmoryOnly = true`.
  - **Mission item protection**: Respects `GamePlayer.RequiredItemCountForMissions(...)`.
  - **Buyback support**: When docked at a space station, sold items are added to the station's shop inventory for buyback.
  - **Deferred execution**: Newly added items are queued and sold on the next `Update()` frame to avoid mutating inventories inside `Inventory.Add`.
- **On-Demand Scan**: Press `Ctrl+F10` (configurable via `AutoSell.ScanHotkey`) to reload rules and run an Auto-Sell scan immediately.
- **HTTP API (via EchoMonitor)**:
  - `GET /api/enhanced/status` (includes `autoFavorite` and `autoSell` status, session sold count, credits earned, and recent events)
  - `GET/POST /api/enhanced/autosell/rules`
  - `POST /api/enhanced/autosell/config`
  - `POST /api/enhanced/autosell/preview`
  - `POST /api/enhanced/autosell/scan`

### Auto-Buy (Blueprints & Modules/Turrets)
Automatically purchases matching **blueprints** and **equipment/modules/turrets** from docked station stores (`BepInEx\config\EchoEnhanced\auto_buy_rules.json`, auto-created with disabled starter examples).

- **Blueprint-aware**:
  - Supports `"Blueprint"` in `categories` alongside `Turret`, `Module`, `Booster`, `Drone`, `DefensiveTurret`.
  - `onlyUnknownBlueprints` (default `true`): skips ship blueprints (`GamePlayer.HasShipBlueprint`) and crafting blueprints (`GamePlayer.HasBlueprint`) you already own, and for leveled Module/Turret blueprints checks `GamePlayer.GetBlueprintMaxLevel(...) < state.blueprintLevel` so higher-level upgrades are still bought.
- **Store & Safety Guards**:
  - Runs automatically when docking at a `SpaceStation` with a store (`shopInventory != null`), and on demand via `Ctrl+F11` (`AutoBuy.ScanHotkey`).
  - Checks faction reputation (`EnoughRepWithFaction`), player level requirement, credit-only purchases (`costItem == null`), `MinCreditsReserve` (default `100,000`), optional per-rule `maxPrice`, and Armory/cargo space.
  - Safe defaults: `AutoBuy.Enabled = false`, `AutoBuy.DryRun = true`.
- **HTTP API (via EchoMonitor)**:
  - `GET/POST /api/enhanced/autobuy/rules`
  - `POST /api/enhanced/autobuy/config`
  - `POST /api/enhanced/autobuy/preview`
  - `POST /api/enhanced/autobuy/scan`

### Fleet Drone Loadouts
Save, manage, and apply fleet-wide drone presets across your entire fleet (`BepInEx\config\EchoEnhanced\drone_loadouts.json`).

- **In-Game Overlay**: Press `Alt+D` to open the Fleet Drone Loadouts overlay to capture current loadouts, apply presets, or delete presets.
- **Quick-Apply Hotkeys**: `Alt+1` (Combat), `Alt+2` (Salvage), `Alt+3` (Mining).
- **Auto-Swap by ECHO Mission Type**: Automatically switches your fleet to the matching drone preset when ECHO changes mission type.
- **Web UI**: Managed from the **Drone Loadouts** sub-tab in the EchoMonitor dashboard.
- **HTTP API (via EchoMonitor)**:
  - `GET /api/enhanced/drones/store`
  - `POST /api/enhanced/drones/config`
  - `POST /api/enhanced/drones/apply`
  - `POST /api/enhanced/drones/capture`
  - `POST /api/enhanced/drones/delete`

### Natural Language Rule Generation (WIP — Backend Only)
> **Note:** The core rule-compiler engine and HTTP API endpoints are in place, but Web UI and Discord bot wiring are not implemented yet.

Compile plain-English instructions (e.g., *"Auto-buy any unknown ship blueprints under 500k credits"* or *"Favorite Legendary turrets iLvl 90+ with Punch Through or 2+ of Crit Damage, Reload Speed"*) into validated JSON rules for **Auto-Favorite**, **Auto-Sell**, or **Auto-Buy**.

#### LLM Configuration
`NaturalLanguageRuleEngine` uses any OpenAI-compatible Chat Completions endpoint grounded by the live in-game Vanguard Galaxy item/aspect/substat catalog.

Configure `BepInEx\config\EchoEnhanced\llm.json` (an `llm.example.json` template is auto-created on first run):
```json
{
  "endpoint": "https://api.openai.com/v1/chat/completions",
  "apiKey": "PASTE_YOUR_API_KEY_HERE",
  "model": "gpt-4o-mini"
}
```
- Works with **OpenAI**, **Gemini** (`https://generativelanguage.googleapis.com/v1beta/openai/chat/completions`), **Groq**, **OpenRouter**, or local **Ollama** (`http://localhost:11434/v1/chat/completions` with an empty `apiKey`).
- Environment variable overrides: `ECHO_LLM_ENDPOINT`, `ECHO_LLM_API_KEY`, `ECHO_LLM_MODEL` (values in `llm.json` take precedence if present).

#### Draft -> Confirm Workflow & HTTP API
Parsed rules are **never applied automatically** — they are stored as a temporary pending draft (`draftId`) with a human-readable summary and preview JSON until explicitly confirmed:

- `POST /api/enhanced/nl/rule/parse` — Body: `{"instruction": "...", "target": "autofav|autosell|autobuy"}` (target is optional; inferred if omitted). Returns `{ok, draftId, target, summary, rule, sources}`.
- `POST /api/enhanced/nl/rule/confirm` — Body: `{"draftId": "..."}`. Commits the drafted rule to the target rule file (`auto_favorite_rules.json`, `auto_sell_rules.json`, or `auto_buy_rules.json`).
- `POST /api/enhanced/nl/rule/cancel` — Body: `{"draftId": "..."}`. Discards the pending draft.

## Building (via `dev-vm`)

```bash
dotnet build -c Release -p:GameManagedDir=/home/marty/projects/vanguard-galaxy/Managed
```

Output: `bin/Release/netstandard2.1/EchoEnhanced.dll`
