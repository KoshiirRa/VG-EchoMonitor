#!/usr/bin/env python3
"""Scaffold a new BepInEx 5 + Harmony mod project for Vanguard Galaxy."""

from __future__ import annotations

import argparse
import re
from pathlib import Path

WORKSPACE_ROOT = Path(__file__).resolve().parents[4]
DEFAULT_GAME_MANAGED = (
    r"c:\Program Files (x86)\Steam\steamapps\common\Vanguard Galaxy\VanguardGalaxy_Data\Managed"
)


def sanitize_identifier(name: str) -> str:
    cleaned = re.sub(r"[^A-Za-z0-9_]", "", name)
    if not cleaned or cleaned[0].isdigit():
        raise ValueError(f"Mod name '{name}' must produce a valid C# identifier.")
    return cleaned


def render_nuget_config() -> str:
    return """<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="BepInEx" value="https://nuget.bepinex.dev/v3/index.json" />
  </packageSources>
</configuration>
"""


def render_csproj(mod_name: str, guid: str, version: str, description: str) -> str:
    return f"""<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>{mod_name}</AssemblyName>
    <Product>{mod_name}</Product>
    <Version>{version}</Version>
    <Description>{description}</Description>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <LangVersion>latest</LangVersion>
    <RestoreAdditionalProjectSources>
      https://api.nuget.org/v3/index.json;
      https://nuget.bepinex.dev/v3/index.json
    </RestoreAdditionalProjectSources>
    <RootNamespace>{mod_name}</RootNamespace>
    <GameManagedDir Condition="'$(GameManagedDir)' == ''">{DEFAULT_GAME_MANAGED}</GameManagedDir>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Analyzers" Version="1.*" PrivateAssets="all" />
    <PackageReference Include="BepInEx.Core" Version="5.*" />
    <PackageReference Include="BepInEx.PluginInfoProps" Version="2.*" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(GameManagedDir)/Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$(GameManagedDir)/UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(GameManagedDir)/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.UI">
      <HintPath>$(GameManagedDir)/UnityEngine.UI.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

</Project>
"""


def render_plugin_cs(mod_name: str, guid: str, version: str) -> str:
    return f"""using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace {mod_name};

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{{
    public const string PluginGuid = "{guid}";
    public const string PluginName = "{mod_name}";
    public const string PluginVersion = "{version}";

    internal static ManualLogSource Log = null!;
    internal static ConfigEntry<bool> ModEnabled = null!;

    private Harmony? _harmony;

    private void Awake()
    {{
        Log = Logger;
        ModEnabled = Config.Bind("General", "Enabled", true, "Enable or disable {mod_name}.");

        if (!ModEnabled.Value)
        {{
            Log.LogInfo($"{{PluginName}} is disabled via config.");
            return;
        }}

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(Plugin).Assembly);

        Log.LogInfo($"Plugin {{PluginGuid}} v{{PluginVersion}} is loaded!");
    }}

    private void OnDestroy()
    {{
        _harmony?.UnpatchSelf();
    }}
}}
"""


def render_example_patch_cs(mod_name: str) -> str:
    return f"""using HarmonyLib;

namespace {mod_name}.Patches;

// Replace TargetType and TargetMethod with verified symbols from Assembly-CSharp.dll.
// [HarmonyPatch(typeof(TargetType), nameof(TargetType.TargetMethod))]
internal static class ExamplePatch
{{
    // [HarmonyPostfix]
    // private static void Postfix(TargetType __instance)
    // {{
    //     Plugin.Log.LogInfo("ExamplePatch ran.");
    // }}
}}
"""


def render_readme(mod_name: str, guid: str, description: str) -> str:
    return f"""# {mod_name}

{description}

- **Plugin GUID**: `{guid}`
- **Target Game**: Vanguard Galaxy (Unity 6000.4.7f1 Mono / `netstandard2.1`)
- **Framework**: BepInEx 5 (x64)

## Build

```bash
dotnet build -c Release -p:GameManagedDir="/path/to/Managed"
```

## Install

Copy `bin/Release/netstandard2.1/{mod_name}.dll` into:

```text
<Vanguard Galaxy>/BepInEx/plugins/{mod_name}/{mod_name}.dll
```
"""


def main() -> int:
    parser = argparse.ArgumentParser(description="Scaffold a Vanguard Galaxy BepInEx 5 mod project.")
    parser.add_argument("name", help="C# project/assembly name (e.g. ShipYardTweaks)")
    parser.add_argument("--author", default="local", help="Author handle used in plugin GUID")
    parser.add_argument("--guid", default=None, help="Explicit BepInEx GUID (default: com.<author>.<name>)")
    parser.add_argument("--version", default="0.1.0", help="Initial semantic version (default: 0.1.0)")
    parser.add_argument("--description", default="A BepInEx 5 mod for Vanguard Galaxy.", help="Short description")
    parser.add_argument("--output-dir", type=Path, default=None, help="Parent directory (default: <workspace>/mods)")
    parser.add_argument("--dry-run", action="store_true", help="Print planned files without writing")
    args = parser.parse_args()

    mod_name = sanitize_identifier(args.name)
    author_slug = re.sub(r"[^a-z0-9]", "", args.author.lower()) or "local"
    guid = args.guid or f"com.{author_slug}.{mod_name.lower()}"

    parent_dir = args.output_dir or (WORKSPACE_ROOT / "mods")
    mod_dir = parent_dir / mod_name

    files: dict[Path, str] = {
        mod_dir / "NuGet.Config": render_nuget_config(),
        mod_dir / f"{mod_name}.csproj": render_csproj(mod_name, guid, args.version, args.description),
        mod_dir / "Plugin.cs": render_plugin_cs(mod_name, guid, args.version),
        mod_dir / "Patches" / "ExamplePatch.cs": render_example_patch_cs(mod_name),
        mod_dir / "README.md": render_readme(mod_name, guid, args.description),
    }

    if args.dry_run:
        print(f"[DRY RUN] Would scaffold {mod_name} ({guid}) at: {mod_dir}")
        for path in files:
            print(f"  - {path}")
        return 0

    if mod_dir.exists() and any(mod_dir.iterdir()):
        raise SystemExit(f"Error: target directory already exists and is not empty: {mod_dir}")

    for path, content in files.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="\n")

    print(f"Scaffolded {mod_name} ({guid}) at: {mod_dir}")
    for path in files:
        print(f"  + {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
