using System;
using System.Collections.Generic;
using UnityEngine;

namespace EchoEnhanced.DroneLoadouts
{
    public sealed class DroneLoadoutOverlay
    {
        private readonly DroneLoadoutEngine _engine;
        private readonly EchoEnhancedConfig _config;

        public bool Visible { get; set; }

        private Rect _windowRect = new Rect(24f, 120f, 360f, 450f);
        private string _newPresetName = "Combat";
        private string _statusMessage = string.Empty;
        private Vector2 _scrollPos = Vector2.zero;
        private Texture2D _opaqueBg;
        private GUIStyle _opaqueWindowStyle;

        public DroneLoadoutOverlay(DroneLoadoutEngine engine, EchoEnhancedConfig config)
        {
            _engine = engine;
            _config = config;
        }

        private GUIStyle GetOpaqueWindowStyle()
        {
            if (_opaqueBg == null)
            {
                _opaqueBg = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _opaqueBg.SetPixel(0, 0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
                _opaqueBg.Apply();
            }

            if (_opaqueWindowStyle == null)
            {
                _opaqueWindowStyle = new GUIStyle(GUI.skin.window);
                _opaqueWindowStyle.normal.background = _opaqueBg;
                _opaqueWindowStyle.onNormal.background = _opaqueBg;
                _opaqueWindowStyle.focused.background = _opaqueBg;
                _opaqueWindowStyle.onFocused.background = _opaqueBg;
                _opaqueWindowStyle.active.background = _opaqueBg;
                _opaqueWindowStyle.onActive.background = _opaqueBg;
                _opaqueWindowStyle.hover.background = _opaqueBg;
                _opaqueWindowStyle.onHover.background = _opaqueBg;
            }

            return _opaqueWindowStyle;
        }

        private float GetEffectiveScale()
        {
            float configured = _config?.DroneLoadoutUiScale?.Value ?? 0f;
            if (configured > 0.1f)
            {
                return Mathf.Clamp(configured, 0.75f, 3.0f);
            }

            // Auto-scale relative to 1080p baseline (e.g. 1.33x at 1440p, 2.0x at 4K)
            return Mathf.Clamp(Screen.height / 1080f, 1.0f, 2.5f);
        }

        public void Draw()
        {
            if (!Visible || _engine == null) return;

            float scale = GetEffectiveScale();
            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            try
            {
                // Keep window clamped within scaled screen bounds
                float maxX = Mathf.Max(0f, (Screen.width / scale) - _windowRect.width);
                float maxY = Mathf.Max(0f, (Screen.height / scale) - _windowRect.height);
                _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, maxX);
                _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, maxY);

                _windowRect = GUILayout.Window(
                    49206,
                    _windowRect,
                    DrawWindow,
                    $"Fleet Drone Loadouts ({scale:0.00}x)",
                    GetOpaqueWindowStyle());
            }
            finally
            {
                GUI.matrix = prevMatrix;
            }
        }

        private void DrawWindow(int windowId)
        {
            bool canSwap = DroneLoadoutEngine.CanSwapRightNow(out var swapStatus);
            var ships = DroneLoadoutEngine.GetActiveFleetDroneShips();

            GUILayout.Label($"Status: {(canSwap ? swapStatus : "LOCKED - " + swapStatus)}");
            GUILayout.Label($"Active Drone Ships: {ships.Count} | Last: {(_engine.LastAppliedPreset ?? "-")}");

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUILayout.Label(_statusMessage);
            }

            GUILayout.Space(6);
            GUILayout.Label("--- Presets (Click to Apply to Fleet) ---");

            var presets = _engine.GetPresetNames();
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(140f));
            if (presets.Count == 0)
            {
                GUILayout.Label("No presets saved yet. Configure your fleet's drones in-game and click Capture below.");
            }
            else
            {
                foreach (var name in presets)
                {
                    GUILayout.BeginHorizontal();
                    GUI.enabled = canSwap;
                    if (GUILayout.Button(name, GUILayout.ExpandWidth(true)))
                    {
                        var res = _engine.ApplyPreset(name, "Player UI");
                        _statusMessage = res.Message;
                    }
                    GUI.enabled = true;

                    if (GUILayout.Button("X", GUILayout.Width(26f)))
                    {
                        if (_engine.DeletePreset(name))
                        {
                            _statusMessage = $"Deleted '{name}'.";
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            GUILayout.Label("--- Capture Current Fleet Loadout ---");
            GUILayout.BeginHorizontal();
            _newPresetName = GUILayout.TextField(_newPresetName ?? string.Empty, 32, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Capture", GUILayout.Width(76f)))
            {
                var res = _engine.CaptureCurrentFleetAsPreset(_newPresetName);
                _statusMessage = res.Message;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("--- ECHO Autopilot Auto-Swap ---");
            var bindings = _engine.Store.EchoBindings;
            bool newAuto = GUILayout.Toggle(bindings.AutoSwapEnabled, "Enable ECHO Auto-Swap");
            bool newWrecks = GUILayout.Toggle(bindings.SwapToSalvageForWrecks, "Swap to Salvage when wrecks remain");
            bool newHoldMission = GUILayout.Toggle(bindings.HoldForPostMissionSalvage, "Hold combat mission turn-in to salvage wrecks");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Combat:", GUILayout.Width(64f));
            string newCombat = GUILayout.TextField(bindings.CombatPreset ?? string.Empty, 32);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Salvage:", GUILayout.Width(64f));
            string newSalvage = GUILayout.TextField(bindings.SalvagePreset ?? string.Empty, 32);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Mining:", GUILayout.Width(64f));
            string newMining = GUILayout.TextField(bindings.MiningPreset ?? string.Empty, 32);
            GUILayout.EndHorizontal();

            if (newAuto != bindings.AutoSwapEnabled
                || newWrecks != bindings.SwapToSalvageForWrecks
                || newHoldMission != bindings.HoldForPostMissionSalvage
                || newCombat != bindings.CombatPreset
                || newSalvage != bindings.SalvagePreset
                || newMining != bindings.MiningPreset)
            {
                bindings.AutoSwapEnabled = newAuto;
                bindings.SwapToSalvageForWrecks = newWrecks;
                bindings.HoldForPostMissionSalvage = newHoldMission;
                bindings.CombatPreset = newCombat;
                bindings.SalvagePreset = newSalvage;
                bindings.MiningPreset = newMining;
                _engine.Save();
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("UI -", GUILayout.Width(52f)))
            {
                float cur = GetEffectiveScale();
                _config.DroneLoadoutUiScale.Value = Mathf.Clamp(cur - 0.15f, 0.75f, 3.0f);
            }
            if (GUILayout.Button("Auto", GUILayout.Width(52f)))
            {
                _config.DroneLoadoutUiScale.Value = 0f;
            }
            if (GUILayout.Button("UI +", GUILayout.Width(52f)))
            {
                float cur = GetEffectiveScale();
                _config.DroneLoadoutUiScale.Value = Mathf.Clamp(cur + 0.15f, 0.75f, 3.0f);
            }
            if (GUILayout.Button("Close", GUILayout.ExpandWidth(true)))
            {
                Visible = false;
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }
    }
}
