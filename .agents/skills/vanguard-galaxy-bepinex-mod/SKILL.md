---
name: vanguard-galaxy-bepinex-mod
description: >-
  Scaffold, configure, build, and deploy BepInEx 5 + Harmony C# mods for
  Vanguard Galaxy (Unity 6000.4.7f1 Mono, netstandard2.1). Use when creating a
  new Vanguard Galaxy mod project, writing Harmony patches or BaseUnityPlugin
  code, configuring a mod .csproj, or deploying/debugging a plugin in BepInEx.
---

# Vanguard Galaxy BepInEx 5 Mod Development

Vanguard Galaxy runs on **Unity 6000.4.7f1 (Mono, x64)** with `.NET Standard 2.1` managed assemblies (`VanguardGalaxy_Data\Managed\Assembly-CSharp.dll`). Community mods use **BepInEx 5 (x64)** and **HarmonyX** (`HarmonyLib`).

> **Read-Only Steam Policy**: Never copy files into or modify `c:\Program Files (x86)\Steam\steamapps\common\Vanguard Galaxy` unless the user explicitly asks to install BepInEx or deploy a built mod DLL.

## 1. Scaffold a New Mod Project

Use [scaffold_mod.py](./scripts/scaffold_mod.py) (Python 3 standard library only) to create a clean BepInEx 5 mod under `mods/<ModName>/`:

```powershell
# Preview files without writing
python ".agents\skills\vanguard-galaxy-bepinex-mod\scripts\scaffold_mod.py" MyAwesomeMod --author YourName --dry-run

# Create the mod project under mods/MyAwesomeMod/
python ".agents\skills\vanguard-galaxy-bepinex-mod\scripts\scaffold_mod.py" MyAwesomeMod --author YourName --description "Short description of the mod"
```

Generated structure:

```text
mods/<ModName>/
├── <ModName>.csproj        # netstandard2.1, BepInEx 5 NuGet + local game Managed DLL refs
├── Plugin.cs               # BaseUnityPlugin entry point, ConfigEntry example, Harmony wiring
├── Patches/
│   └── ExamplePatch.cs     # Harmony patch template (disabled/commented until target is chosen)
└── README.md               # Build & install instructions
```

## 2. Project Configuration Notes

- **Target Framework**: `<TargetFramework>netstandard2.1</TargetFramework>` (matches Vanguard Galaxy's `netstandard.dll` 2.1.0.0).
- **NuGet Feed**: BepInEx 5 packages (`BepInEx.Core` 5.x, `BepInEx.Analyzers`, `BepInEx.PluginInfoProps`) are hosted on the BepInEx NuGet feed (`https://nuget.bepinex.dev/v3/index.json`). Add a `NuGet.Config` if the feed is not already configured globally.
- **Game Assembly References**: Reference `Assembly-CSharp.dll` and needed `UnityEngine*.dll` files directly from `VanguardGalaxy_Data\Managed\` with `<Private>false</Private>` so game DLLs are never copied into the build output.
- **Accessing Private Members**: Use `HarmonyLib.AccessTools` (`AccessTools.Field`, `AccessTools.Property`, `AccessTools.Method`) or `BepInEx.AssemblyPublicizer.MSBuild` if patching non-public game internals.

## 3. Building the Mod

Because the local Windows host (`<windows-host>`) lacks a modern .NET SDK on `PATH`, prefer building on **`dev-vm` (`<user>@<dev-host>`)** when compiling:

1. Sync or copy the mod project and only the required read-only reference DLLs (`Assembly-CSharp.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, etc.) to a local `libs/` folder in the build workspace on `dev-vm`, passing `-p:GameManagedDir=./libs` to `dotnet build`.
2. Build in Release mode:
   ```bash
   dotnet build -c Release -p:GameManagedDir=./libs
   ```
3. Verify the output DLL is produced at `bin/Release/netstandard2.1/<ModName>.dll` and does **not** bundle `Assembly-CSharp.dll` or `UnityEngine*.dll`.

## 4. Harmony Patching Best Practices for Vanguard Galaxy

- **Verify signatures first**: Always confirm exact class names, method names, parameter types, and return types against `Assembly-CSharp.dll` before writing `[HarmonyPatch]` attributes (see the `vanguard-galaxy-assets-and-code` skill).
- **Prefer Postfix / Prefix over Transpiler**: Vanguard Galaxy updates frequently; Prefix/Postfix patches survive minor game updates much better than IL transpilers.
- **Log cleanly**: Use `Plugin.Log.LogInfo(...)` / `LogWarning(...)` / `LogError(...)` via `BepInEx.Logging.ManualLogSource` rather than `Debug.Log`.
- **Expose settings**: Bind user-tunable values with `Config.Bind("Section", "Key", defaultValue, "Description")` so players can configure the mod via `BepInEx\config\<GUID>.cfg`.

## 5. Deployment & Verification (Only When Requested)

Only when the user asks to install/test the mod:

1. Confirm BepInEx 5 (x64) is installed in `c:\Program Files (x86)\Steam\steamapps\common\Vanguard Galaxy\BepInEx\`.
2. Copy `bin/Release/netstandard2.1/<ModName>.dll` to `BepInEx\plugins\<ModName>\<ModName>.dll`.
3. Launch the game and verify loading in:
   - `c:\Program Files (x86)\Steam\steamapps\common\Vanguard Galaxy\BepInEx\LogOutput.log`
   - `%USERPROFILE%\AppData\LocalLow\Bat Roost Games\VanguardGalaxy\Player.log`
4. Confirm `[Info   : <ModName>] Plugin <GUID> is loaded!` appears with no `HarmonyException` or `MissingMethodException`.
