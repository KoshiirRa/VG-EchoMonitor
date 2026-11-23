# EchoMonitor & EchoMonitorBot Roadmap

Planned features and milestone history spanning the in-game **EchoMonitor** BepInEx mod (`EchoMonitor/`) and the **EchoMonitorBot** Discord companion (`EchoMonitorBot/`).

---

## Completed Milestones

- **Milestone 1 (Complete)** ✅ Core BepInEx 5 mod + embedded HTTP/SSE server + responsive Vanguard Galaxy HUD (Shield/Armor/Hull, Crew/Officers with level deltas, Active Fleet + per-ship Drone/Hangar Bays, Loot with exact rarity colors + substats/aspects + Locked Crates, Ship/Module Blueprints, Faction Rep, Session Timeline with monotonic level-up XP & % to next level).
- **Milestone 2 (Complete)** ✅ Stuck ECHO Detection (Destroyed/Hostile Station dock loop + same-mission/POI Emergency Warp loop) + Web UI Recovery Controls (Warp to Safe Station, Abandon Active Mission, Reset/Toggle ECHO) + Running Mission Log.
- **Milestone 3 (Complete)** ✅ Empire Raid & Siege Detection + Warp to Defend + Standalone .NET 8 Discord Companion Bot (`EchoMonitorBot.exe`) with interactive alert buttons, slash commands, crash vs. clean-exit detection, and `AutoLaunchBot`.

---

## Planned Milestones

### Milestone 4 (Planned) ⏳ — Empire Activity, Outpost Storage & Refining Monitoring

Broader empire activity tracking (materials refined, trade station profits, outpost production) alongside ECHO telemetry, plus proactive outpost storage and refining backlog monitoring:

#### 1. Outpost Storage Full Alerts (≥ 95%)
- **Goal**: Alert when any player outpost reaches **95% or higher** storage capacity.
- **EchoMonitor**:
  - Track storage usage (`used / capacity`) across all player outposts in `TelemetryCollector`.
  - Emit an alert event (e.g., `OutpostStorageFull`) via SSE (`/api/events`) and expose outpost storage metrics in the telemetry payload when utilization crosses ≥ 95% (with cooldown/hysteresis to avoid alert spam).
- **EchoMonitorBot**:
  - Listen for outpost storage alerts and post a Discord embed showing the outpost name, sector, current fill percentage, and largest item stack(s).

#### 2. One-Click Outpost Storage Relief Action
- **Goal**: Provide an interactive button to relieve a full outpost by moving **half of the single largest item stack** in that station back to the **depot**.
- **EchoMonitor**:
  - Investigate game internals (`Assembly-CSharp.dll`) for safe programmatic item/stack transfer between an outpost's storage and the empire depot (executing safely on the main thread).
  - Expose an action endpoint (e.g., `POST /api/action/relieve-outpost`) that identifies the single largest stack in the target outpost and moves half (`floor(count / 2)`) to the depot.
- **EchoMonitorBot**:
  - Attach an interactive button (e.g., `[Move Half Largest Stack → Depot]`) alongside `[Dismiss]` on the ≥ 95% outpost storage alert embed, restricted to `DISCORD_ALLOWED_USER_IDS`.
  - Report back the transferred item name, quantity moved, and updated outpost fill percentage.

#### 3. Empire Station Monitoring — Refining Output
- **Goal**: Show how much the empire has refined (plus trade station profits and outpost production).
- **EchoMonitor**:
  - Inspect empire station / refinery telemetry in `Assembly-CSharp.dll` to track refined material quantities (per session and/or totals/rates).
  - Expose refining metrics in the telemetry snapshot and web UI (`EchoMonitor/ui/index.html`).
- **EchoMonitorBot**:
  - Surface refined output metrics in `/status`, periodic session digests, and/or a dedicated empire command.

#### 4. Empire Refining Backlog Monitoring (Depot Raw Materials / Salvage)
- **Goal**: Track the **net change** in un-refined raw materials and salvage sitting at the depot so it's easy to see whether refining is keeping up with intake or falling behind.
- **EchoMonitor**:
  - Baseline depot un-refined raw material & salvage counts at session start (and/or rolling window).
  - Compute and expose current totals, session delta (`+/-`), and rate of change in telemetry and the web UI.
- **EchoMonitorBot**:
  - Display the refining backlog trend (growing vs. draining) in `/status` and periodic session digests.

#### 5. Depot Refined Products Storage Tracking
- **Goal**: Track current depot stockpiles (and session deltas) of refined products.
- **EchoMonitor**:
  - Read refined product quantities stored at the empire depot and baseline them at session start.
  - Expose per-item refined product counts and session deltas (`+/-`) in telemetry and the web UI.
- **EchoMonitorBot**:
  - Surface refined product stockpiles and net gains in `/status`, periodic session digests, and/or an empire inventory command.

---

### Milestone 5 (Planned) ⏳ — Freeform ECHO Shopping Instructions
- LLM-parsed shopping tours via Discord or Web UI with route preview, credit cap, and confirmation before execution.

---

### Milestone 6 (Planned — Possible Separate Mod: "Enhanced ECHO") ⏳ — Automated Drone Loadout Swapping
- Automated in-field drone loadout swapping for drone fleets — allow ECHO to automatically swap drones from a combat setup (laser/missile + target painter + shield repair) to a salvage setup (salvage laser + salvage harpoon) when wrecks are generated.
