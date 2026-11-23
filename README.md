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

## Discord Bot Setup & Configuration

You can optionally run `EchoMonitorBot.exe` alongside `EchoMonitor.dll` to receive real-time Discord alerts (Stuck ECHO, Empire Raids/Sieges, Crash vs. Clean Exit, Rare Loot, Ready Ship Blueprints) and trigger recovery actions remotely via interactive buttons and slash commands.

### 1. Create a Discord Application & Bot Token
1. Open the [Discord Developer Portal](https://discord.com/developers/applications) and click **New Application** (e.g. `EchoMonitor`).
2. Go to the **Bot** tab, click **Reset Token** (or **Add Bot**), and copy the generated bot token. *(Keep this secret — never commit it.)*
3. No privileged gateway intents are required (the bot uses slash commands, interactive buttons, and channel embeds only).

### 2. Invite the Bot to Your Server
1. Go to **OAuth2 → URL Generator**.
2. Under **Scopes**, check:
   - `bot`
   - `applications.commands`
3. Under **Bot Permissions**, check:
   - `View Channels`
   - `Send Messages`
   - `Embed Links`
4. Open the generated URL in your browser and authorize the bot on your server.
5. If you plan to use a **private channel**, open the channel's **Edit Channel → Permissions** and explicitly add the bot role/member with **View Channel**, **Send Messages**, and **Embed Links**.

### 3. Copy Your Channel ID & User ID
1. In Discord, open **User Settings → Advanced** and enable **Developer Mode**.
2. Right-click your target alerts channel → **Copy Channel ID** (`discordChannelId`).
3. Right-click your own Discord username → **Copy User ID** (`allowedUserIds`) so only you (and any other listed user IDs) can click recovery buttons or run control commands.

### 4. Create `echomonitor_bot.json`
Copy `EchoMonitorBot.exe` into `BepInEx/plugins/EchoMonitor/` (next to `EchoMonitor.dll`), then create `echomonitor_bot.json` in the same folder:

```json
{
  "discordBotToken": "YOUR_DISCORD_BOT_TOKEN",
  "discordChannelId": "123456789012345678",
  "allowedUserIds": ["987654321098765432"],
  "echoMonitorUrl": "http://127.0.0.1:8778",
  "echoMonitorToken": "",
  "offlineAlertSeconds": 30,
  "digestIntervalMinutes": 60,
  "minLootAlertRarity": "Exotic"
}
```

### 5. Enable Auto-Launch (or Run Manually)
- **Auto-launch with the game**: Open `BepInEx/config/EchoMonitor.cfg` and set:
  ```ini
  [DiscordBot]
  AutoLaunchBot = true
  ```
  When Vanguard Galaxy starts, `EchoMonitor.dll` will automatically launch `EchoMonitorBot.exe` in the background (if not already running).
- **Manual / Remote**: You can also run `EchoMonitorBot.exe` manually (or on another machine/server pointed at your `echoMonitorUrl`).

See [`EchoMonitorBot/README.md`](EchoMonitorBot/README.md) for all environment variable overrides, slash commands, and configuration options.

## Building from Source

```bash
# Build the BepInEx 5 mod
cd EchoMonitor
dotnet build -c Release -p:GameManagedDir="/path/to/VanguardGalaxy_Data/Managed"

# Publish the standalone Discord bot (Windows or Linux)
cd ../EchoMonitorBot
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

## Linux / Steam Deck (Proton)

- **BepInEx under Proton:** Add the standard Steam launch option for Vanguard Galaxy:
  ```bash
  WINEDLLOVERRIDES="winhttp=n,b" %command%
  ```
- **Dashboard & Networking:** Proton shares the host network namespace, so `http://127.0.0.1:8778/` is directly accessible from your native Linux browser.
- **Companion Bot:** Run the native `linux-x64` `EchoMonitorBot` binary on the Linux host (with `AutoLaunchBot = false` in EchoMonitor config). See [EchoMonitorBot/README.md](EchoMonitorBot/README.md) for XDG config paths and the `systemd --user` service template.

## Roadmap

- **Milestone 1 (Complete)** ✅ Core BepInEx 5 mod + embedded HTTP/SSE server + responsive Vanguard Galaxy HUD (Shield/Armor/Hull, Crew/Officers with level deltas, Active Fleet + per-ship Drone/Hangar Bays, Loot with exact rarity colors + substats/aspects + Locked Crates, Ship/Module Blueprints, Faction Rep, Session Timeline with monotonic level-up XP & % to next level).
- **Milestone 2 (Complete)** ✅ Stuck ECHO Detection (Destroyed/Hostile Station dock loop + same-mission/POI Emergency Warp loop) + Web UI Recovery Controls (Warp to Safe Station, Abandon Active Mission, Reset/Toggle ECHO) + Running Mission Log.
- **Milestone 3 (Complete)** ✅ Empire Raid & Siege Detection + Warp to Defend + Standalone .NET 8 Discord Companion Bot (`EchoMonitorBot.exe`) with interactive alert buttons, slash commands, crash vs. clean-exit detection, and `AutoLaunchBot`.
- **Milestone 4 (Planned)** ⏳ Empire Activity Tracking (materials refined, trade station profits, outpost production broken out alongside ECHO telemetry).
- **Milestone 5 (Planned)** ⏳ Freeform ECHO Shopping Instructions (LLM-parsed shopping tours via Discord or Web UI with route preview, credit cap, and confirmation before execution).
- **Milestone 6 (Planned — Possible Separate Mod: "Enhanced ECHO")** ⏳ Automated in-field drone loadout swapping for drone fleets — allow ECHO to automatically swap drones from a combat setup (laser/missile + target painter + shield repair) to a salvage setup (salvage laser + salvage harpoon) when wrecks are generated.

## AI / LLM Disclosure

`EchoMonitor`, `EchoMonitorBot`, and the accompanying modding skills under `.agents/skills/` were developed with AI assistance (**Google Antigravity / Gemini**) and verified directly against Vanguard Galaxy's `Assembly-CSharp.dll` and live in-game testing.

## Disclaimer

This is an unofficial community mod for **Vanguard Galaxy** and is not affiliated with or endorsed by **Bat Roost Games**.

## License

MIT License — see [LICENSE](LICENSE).
