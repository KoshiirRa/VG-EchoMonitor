using System;
using System.Security.Cryptography;
using BepInEx.Configuration;

namespace EchoMonitor
{
    public sealed class EchoConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<int> Port { get; }
        public ConfigEntry<bool> AllowRemote { get; }
        public ConfigEntry<string> BindAddress { get; }
        public ConfigEntry<bool> RequireAuth { get; }
        public ConfigEntry<string> AuthToken { get; }
        public ConfigEntry<bool> OpenBrowserOnStart { get; }
        public ConfigEntry<int> SnapshotIntervalMinutes { get; }
        public ConfigEntry<bool> AutoLaunchBot { get; }
        public ConfigEntry<string> BotExePath { get; }

        public EchoConfig(ConfigFile config)
        {
            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                "Enable or disable the EchoMonitor embedded web server and telemetry tracker.");

            Port = config.Bind(
                "Server",
                "Port",
                8778,
                "HTTP port for the EchoMonitor dashboard and API.");

            AllowRemote = config.Bind(
                "Server",
                "AllowRemote",
                false,
                "Allow non-localhost connections. When true and BindAddress is 127.0.0.1, binds to all interfaces (+).");

            BindAddress = config.Bind(
                "Server",
                "BindAddress",
                "127.0.0.1",
                "IP address or host to bind the HTTP listener to (e.g. 127.0.0.1, 0.0.0.0, or +).");

            RequireAuth = config.Bind(
                "Security",
                "RequireAuth",
                false,
                "Require Bearer token or ?token= authentication. Independent of AllowRemote so a reverse proxy (e.g. Pangolin) can handle auth.");

            AuthToken = config.Bind(
                "Security",
                "AuthToken",
                string.Empty,
                "Authentication token used when RequireAuth is true. Auto-generated on startup if empty.");

            OpenBrowserOnStart = config.Bind(
                "General",
                "OpenBrowserOnStart",
                false,
                "Automatically open the EchoMonitor dashboard in the default browser on game start.");

            SnapshotIntervalMinutes = config.Bind(
                "Tracking",
                "SnapshotIntervalMinutes",
                5,
                "Interval in minutes between periodic session history snapshots written to history.jsonl.");

            AutoLaunchBot = config.Bind(
                "DiscordBot",
                "AutoLaunchBot",
                false,
                "Automatically launch EchoMonitorBot.exe in the background when the game starts (if not already running).");

            BotExePath = config.Bind(
                "DiscordBot",
                "BotExePath",
                string.Empty,
                "Optional path to EchoMonitorBot.exe. When empty, looks for EchoMonitorBot.exe in the same directory as EchoMonitor.dll.");

            if (string.IsNullOrWhiteSpace(AuthToken.Value))
            {
                AuthToken.Value = GenerateToken();
                config.Save();
            }
        }

        public string GetListenerPrefix()
        {
            var host = (BindAddress.Value ?? "127.0.0.1").Trim();
            if (AllowRemote.Value)
            {
                if (string.IsNullOrEmpty(host) || host == "127.0.0.1" || host == "localhost" || host == "0.0.0.0" || host == "*")
                {
                    host = "+";
                }
            }
            else
            {
                if (string.IsNullOrEmpty(host) || host == "+" || host == "*" || host == "0.0.0.0")
                {
                    host = "127.0.0.1";
                }
            }

            return $"http://{host}:{Port.Value}/";
        }

        private static string GenerateToken()
        {
            var bytes = new byte[16];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
