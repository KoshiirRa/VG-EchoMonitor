# EchoMonitorBot (.NET 8 Standalone Companion)

Zero-dependency C# / .NET 8 Discord companion bot for **EchoMonitor** (Vanguard Galaxy). Built with native `ClientWebSocket`, `HttpClient`, and `System.Text.Json` against Discord Gateway v10 + REST API v10 — publishes as a single-file, self-contained `EchoMonitorBot.exe` with no Python runtime or PyInstaller packaging.

## Features

- **Crash vs. Clean Shutdown Detection**: Listens to `{ECHOMONITOR_URL}/api/events` (SSE). Clean game exits (`event: shutdown`) post a neutral closed notice without triggering false crash alarms; unexpected disconnects lasting `OFFLINE_ALERT_SECONDS` post a red **Possible Crash** alert, followed by a green **Back Online** embed on reconnect.
- **Real-Time Interactive Alerts**:
  - **Stuck ECHO** (`DestroyedOrHostileStation` / `EmergencyWarpLoop`): `[Warp Safe Station]`, `[Abandon Mission]`, `[Reset ECHO]`, `[Dismiss]`.
  - **Empire Raid / Siege** (`EmpireRaidOrSiege`): `[Warp to Defend]`, `[Dismiss]`.
  - **Rare Loot & Ready Ship Blueprints**: Alerts on new `Exotic`/`Legendary` modules (`MIN_LOOT_ALERT_RARITY`) and ship blueprints reaching `canComplete: true`.
- **Slash Commands**: `/status`, `/fleet`, `/loot`, `/missions`, `/recover`, `/reset_session`.
- **Periodic Session Digest**: Posts a compact progress summary every `DIGEST_INTERVAL_MINUTES` while in-game.

## Quick Setup (Auto-Launch with Vanguard Galaxy)

1. Create a Discord Bot in the [Discord Developer Portal](https://discord.com/developers/applications) (copy the Bot Token and invite it with `bot` + `applications.commands` scopes and `Send Messages` / `Embed Links` permissions).
2. Copy `EchoMonitorBot.exe` into your `BepInEx/plugins/EchoMonitor/` folder (right next to `EchoMonitor.dll`).
3. Copy `echomonitor_bot.example.json` to `echomonitor_bot.json` in the same folder and fill in `DISCORD_BOT_TOKEN`, `DISCORD_CHANNEL_ID`, and `DISCORD_ALLOWED_USER_IDS`.
4. In `BepInEx/config/com.echomonitor.cfg`, enable auto-launch:

```ini
[DiscordBot]
AutoLaunchBot = true
BotExePath =
```

When `BotExePath` is blank, EchoMonitor automatically launches `EchoMonitorBot.exe` from the same directory as `EchoMonitor.dll` (and skips launching if it is already running).

## Configuration

Settings are loaded in order (later sources override earlier ones):
1. `echomonitor_bot.json` (next to `EchoMonitorBot.exe` or current working directory)
2. `.env` file
3. Process environment variables

| Key | Default | Description |
| :--- | :--- | :--- |
| `DISCORD_BOT_TOKEN` | *(required)* | Discord bot token |
| `DISCORD_CHANNEL_ID` | *(required)* | Target channel ID for alerts & digests |
| `DISCORD_ALLOWED_USER_IDS` | *(empty)* | Comma-separated Discord user IDs allowed to click recovery buttons / run `/recover` & `/reset_session` |
| `ECHOMONITOR_URL` | `http://127.0.0.1:8778` | EchoMonitor HTTP server base URL |
| `ECHOMONITOR_TOKEN` | *(empty)* | Bearer token if `RequireAuth = true` |
| `OFFLINE_ALERT_SECONDS` | `30` | Seconds unreachable before firing a Crash/Offline alert |
| `DIGEST_INTERVAL_MINUTES` | `60` | Minutes between periodic session digests (`0` disables) |
| `MIN_LOOT_ALERT_RARITY` | `Exotic` | `Exotic`, `Legendary`, or `Off` |

## Build & Publish (`win-x64`)

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
