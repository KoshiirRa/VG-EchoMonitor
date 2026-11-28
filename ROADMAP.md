# EchoMonitor, EchoEnhanced & EchoMonitorBot Roadmap

Planned features and milestone history spanning the in-game **EchoMonitor** BepInEx mod (`EchoMonitor/`), the **EchoEnhanced** automation companion (`EchoEnhanced/`), and the **EchoMonitorBot** Discord companion (`EchoMonitorBot/`).

---

## Completed Milestones

- **Milestone 1 (Complete)** ✅ Core BepInEx 5 mod + embedded HTTP/SSE server + responsive Vanguard Galaxy HUD (Shield/Armor/Hull, Crew/Officers with level deltas, Active Fleet + per-ship Drone/Hangar Bays, Loot with exact rarity colors + substats/aspects + Locked Crates, Ship/Module Blueprints, Faction Rep, Session Timeline with monotonic level-up XP & % to next level).
- **Milestone 2 (Complete)** ✅ Stuck ECHO Detection (Destroyed/Hostile Station dock loop + same-mission/POI Emergency Warp loop) + Web UI Recovery Controls (Warp to Safe Station, Abandon Active Mission, Reset/Toggle ECHO) + Running Mission Log.
- **Milestone 3 (Complete)** ✅ Empire Raid & Siege Detection + Warp to Defend + Standalone .NET 8 Discord Companion Bot (`EchoMonitorBot.exe`) with interactive alert buttons, slash commands, crash vs. clean-exit detection, and `AutoLaunchBot`.
- **Milestone 4 (Complete)** ✅ Empire Activity, Outpost Storage & Refining Monitoring — ≥ 95% outpost storage alerts (with 90% hysteresis), one-click `Move Half Largest Stack → Depot` relief (`POST /api/action/relieve-outpost` + Web UI & Discord buttons), Harmony-hooked session refining/trade/production counters, depot raw/salvage backlog & refined product stockpile tracking, per-station `/depot` viewer, **Empire & Refining** Web UI tab, and `/empire` Discord command.
- **Milestone 6 (Complete — v0.5)** ✅ Fleet Drone Loadouts & Auto-Swapping (`EchoEnhanced`) — Save and apply fleet-wide drone presets via in-game overlay (`Alt+D`), quick-apply hotkeys (`Alt+1` Combat, `Alt+2` Salvage, `Alt+3` Mining), automatic ECHO mission-type drone swapping, and the **Drone Loadouts** Web UI sub-tab.
- **Milestone 7 (Complete — v0.5)** ✅ Auto-Favorite Modules (`EchoEnhanced`) — Hot-reloading JSON rules (`auto_favorite_rules.json`) with rarity, item level, category, name, aspect, and Workshop reroll-aware "N of M" substat matching, Dry Run mode, `Ctrl+F9` rescan, and Web UI rule editor.
- **Milestone 8 (Complete — v0.5)** ✅ Outpost Job Status — Real-time outpost operational state (`Working`, `Idle`, `Upgrading`, `MissingMaterials`, `StorageFull`, `Unset`, `Raided`) and active job tracking (Production, Forge, Refinery, Construction, Upgrade) with progress bars, remaining counts, ETAs, and stalled/paused/awaiting-supplies indicators in the **Empire & Refining** Web UI tab and `/empire` Discord command.
- **Milestone 9 (Complete — v0.5)** ✅ Auto-Sell & Auto-Buy Engines (`EchoEnhanced`) — Rule-based automatic selling of non-favourited equipment (protecting favourites, mission items, and Auto-Favorite matches; includes **Data Fragments** preset and `Ctrl+F10` scan) and automatic store purchasing of unknown blueprints, **Officer Upgrade Items (Usable)**, and qualifying gear (enforcing credit reserve, rep, and level; `Ctrl+F11` scan), with Web UI sub-tabs, visual Rule Editor, and batched Discord SSE alerts.

---

## In Progress

### Natural Language Rule Generation (WIP — Backend Only) 🚧
- Core Direct LLM rule-compiler engine (`DirectLlmClient` + `NaturalLanguageRuleEngine`, grounded by the live in-game item/aspect/substat catalog) and `/api/enhanced/nl/rule/parse`, `/confirm`, and `/cancel` HTTP endpoints are included in `v0.5`.
- **Remaining:** Wire up the Web UI prompt/preview/confirm interface in the **ECHO Enhanced** tab and a `/rule` slash command with Confirm/Cancel buttons in `EchoMonitorBot`.

---

## Planned Milestones

### Milestone 5 (Planned — ECHO Enhanced) ⏳ — Freeform ECHO Shopping Instructions
- LLM-parsed shopping tours via Discord or Web UI with route preview, credit cap, and confirmation before execution.

---

### Milestone 10 (Planned — ECHO Enhanced) ⏳ — Auto-Extract Aspects
- Automatically extract aspects from modules that meet user-defined criteria (e.g., rarity, tier/level, target aspect/substats, or module type).
