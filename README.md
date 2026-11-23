# VG-EchoMonitor

**VG-EchoMonitor** is an embedded live telemetry web dashboard, stuck-ECHO recovery tool, and optional C# Discord companion bot for **Vanguard Galaxy** (BepInEx 5, Unity 6 Mono).

Monitor your overnight ECHO autopilot sessions from desktop or mobile, get alerted if ECHO gets stuck or an Empire station is raided/under siege, and trigger safe recovery actions remotely.

## Features

### `EchoMonitor/` — BepInEx 5 Mod (`netstandard2.1`)
- **Embedded Web Dashboard (`http://127.0.0.1:8778`)**:
  - Single-file Vanguard Galaxy HUD-styled web UI (zero CDN dependencies, responsive desktop/mobile, SSE live updates with 2s polling fallback).
  - **ECHO Status Banner**: live ECHO active/manual state, translated system/POI/mission, Shield (blue) / Armor (orange) / Hull (red) bars, and quick recovery controls.
  - **Session Progress KPIs**: Credits (+ Cr/hr), Vanguard Marks + Commendation Dog Tags VM value, Commander & Empire levels (`+X Lv`, `% to next`, cumulative `+XP` / `XP/hr`), and Guild Ranks (Patrol / Bounty / Industry).
  - **Ship & Fleet**: Active fleet ships (flagship + wingmen) with stance/activity, Shield/Armor/Hull bars, per-ship Drone Bay & Hangar Bay status, Crew complement, and Assigned Officers (`+X` session level deltas).
  - **Loot & Modules**: Session module drops with exact in-game rarity colors (`Standard` / `Enhanced` / `HighGrade` / `Exotic` / `Legendary`), rolled substats, formatted aspects, locked crates, and captured ships.
  - **Blueprints, Faction Rep, Mission Log & Session Timeline**: Ship blueprint completion progress bars, module blueprints, faction standing deltas, running mission log (`Accepted`/`Completed`/`Abandoned`/`Failed`), and historical snapshot charts.
  - **Per-Save Persistent Sessions**: Auto-resumes across game restarts (`BepInEx/config/EchoMonitor/<saveId>/`).
- **Stuck ECHO & Empire Raid Detection + Remote Recovery**:
  - Detects **Destroyed / Hostile Station** dock loops, **Emergency Warp Loops** (repeated emergency warps on the same mission/POI), and active **Empire Station Raids / Sieges**.
  - Main-thread-safe recovery actions: **Warp to Safe Station** (adjacent system), **Abandon Active Mission** (protects story quests), **Warp to Defend** (Empire raids), **Reset ECHO**, and **Pause/Resume ECHO**.
- **Reverse-Proxy Ready**:
  - Strictly relative `./api/...` paths, SSE `Cache-Control: no-cache` & `X-Accel-Buffering: no`, and independent `RequireAuth` toggle for clean hosting behind **Pangolin**, **Tailscale**, or Nginx/Caddy.

### `EchoMonitorBot/` — Standalone C# / .NET 8 Companion Discord Bot
- Zero third-party NuGet dependencies (native `ClientWebSocket`, `HttpClient`, `System.Text.Json`), publishes as a self-contained single-file `win-x64` `EchoMonitorBot.exe`.
- Connects to EchoMonitor's `/api/events` SSE stream with **Crash vs. Clean Shutdown** detection (`event: shutdown`).
- Real-time Discord alert embeds with interactive recovery buttons (`[Warp Safe Station]`, `[Abandon Mission]`, `[Warp to Defend]`, `[Reset ECHO]`, `[Dismiss]`), rare loot / ready blueprint alerts, periodic session digests, and slash commands (`/status`, `/fleet`, `/loot`, `/missions`, `/recover`, `/reset_session`).
- Optional `AutoLaunchBot = true` in `EchoMonitor.cfg` launches `EchoMonitorBot.exe` automatically when the game starts.

## Installation

1. Install **BepInEx 5 (x64)** into your Vanguard Galaxy game folder.
2. Build `EchoMonitor/EchoMonitor.csproj` (targeting `netstandard2.1`) and copy `EchoMonitor.dll` into `BepInEx/plugins/EchoMonitor/`.
3. Launch Vanguard Galaxy once to generate `BepInEx/config/EchoMonitor.cfg`.
4. Open `http://127.0.0.1:8778/` in your browser.
5. *(Optional)* See [`EchoMonitorBot/README.md`](EchoMonitorBot/README.md) to set up the Discord companion bot.

## Building from Source

```bash
# Build the BepInEx 5 mod
cd EchoMonitor
dotnet build -c Release -p:GameManagedDir="/path/to/VanguardGalaxy_Data/Managed"

# Publish the standalone win-x64 Discord bot
cd ../EchoMonitorBot
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## AI / LLM Disclosure

`EchoMonitor`, `EchoMonitorBot`, and the accompanying modding skills under `.agents/skills/` were developed with AI assistance (**Google Antigravity / Gemini**) and verified directly against Vanguard Galaxy's `Assembly-CSharp.dll` and live in-game testing.

## Disclaimer

This is an unofficial community mod for **Vanguard Galaxy** and is not affiliated with or endorsed by **Bat Roost Games**.

## License

MIT License — see [LICENSE](LICENSE).
