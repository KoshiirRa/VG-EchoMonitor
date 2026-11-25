# EchoMonitor & EchoMonitorBot Roadmap

Planned features and milestone history spanning the in-game **EchoMonitor** BepInEx mod (`EchoMonitor/`) and the **EchoMonitorBot** Discord companion (`EchoMonitorBot/`).

---

## Completed Milestones

- **Milestone 1 (Complete)** ✅ Core BepInEx 5 mod + embedded HTTP/SSE server + responsive Vanguard Galaxy HUD (Shield/Armor/Hull, Crew/Officers with level deltas, Active Fleet + per-ship Drone/Hangar Bays, Loot with exact rarity colors + substats/aspects + Locked Crates, Ship/Module Blueprints, Faction Rep, Session Timeline with monotonic level-up XP & % to next level).
- **Milestone 2 (Complete)** ✅ Stuck ECHO Detection (Destroyed/Hostile Station dock loop + same-mission/POI Emergency Warp loop) + Web UI Recovery Controls (Warp to Safe Station, Abandon Active Mission, Reset/Toggle ECHO) + Running Mission Log.
- **Milestone 3 (Complete)** ✅ Empire Raid & Siege Detection + Warp to Defend + Standalone .NET 8 Discord Companion Bot (`EchoMonitorBot.exe`) with interactive alert buttons, slash commands, crash vs. clean-exit detection, and `AutoLaunchBot`.
- **Milestone 4 (Implemented)** ✅ Empire Activity, Outpost Storage & Refining Monitoring — ≥ 95% outpost storage alerts (with 90% hysteresis), one-click `Move Half Largest Stack → Depot` relief (`POST /api/action/relieve-outpost` + Web UI & Discord buttons), Harmony-hooked session refining/trade/production counters, depot raw/salvage backlog & refined product stockpile tracking, **Empire & Refining** Web UI tab, and `/empire` Discord command.
- **Milestone 8 (Implemented)** ✅ Outpost Job Status — Real-time outpost operational state (`Working`, `Idle`, `Upgrading`, `MissingMaterials`, `StorageFull`, `Unset`, `Raided`) and active job tracking (Production, Forge, Refinery, Construction, Upgrade) with progress, remaining counts, ETA, and stalled/paused/awaiting-supplies indicators in the **Empire & Refining** Web UI tab and `/empire` Discord command.

---

## Planned Milestones

### Milestone 5 (Planned — ECHO Enhanced) ⏳ — Freeform ECHO Shopping Instructions
- LLM-parsed shopping tours via Discord or Web UI with route preview, credit cap, and confirmation before execution.

---

### Milestone 6 (Planned — ECHO Enhanced) ⏳ — Automated Drone Loadout Swapping
- Automated in-field drone loadout swapping for drone fleets — allow ECHO to automatically swap drones from a combat setup (laser/missile + target painter + shield repair) to a salvage setup (salvage laser + salvage harpoon) when wrecks are generated.

---

### Milestone 7 (Planned — ECHO Enhanced) ⏳ — Auto-Favorite Modules
- Automatically flag modules as favorites when they meet user-defined conditions (e.g., rarity, tier/iLvl, substats/aspects, or module type/name) so they are protected from accidental selling or aspect extraction (Milestones 9 & 10).
- **Criteria Examples:**
  - *Wishlist / Fleet Goal:* `item matches ["Microcell Swarm", "Hailfire", "Missile Launcher"] AND rarity >= Exotic AND iLvl >= 90` (e.g., *Exotic Microcell Swarm Mk.XX, iLvl 93, Punch Through*).
  - *Workshop Reroll-Aware Substats ("N of M"):* Match items with at least `N` desired substats (e.g., `>= 2 of [Turret Rotation Speed, Critical Damage, Energy Damage, Reload Speed]`), so 2-of-3 rolls with one dead substat (like Torpedo Power) that are one Workshop reroll away from a god-roll still get auto-favorited.

---

### Milestone 9 (Planned — ECHO Enhanced) ⏳ — Auto-Sell Modules
- Automatically sell modules that meet user-defined criteria (e.g., rarity, tier/level, substats/aspects, or module type).

---

### Milestone 10 (Planned — ECHO Enhanced) ⏳ — Auto-Extract Aspects
- Automatically extract aspects from modules that meet user-defined criteria (e.g., rarity, tier/level, target aspect/substats, or module type).
