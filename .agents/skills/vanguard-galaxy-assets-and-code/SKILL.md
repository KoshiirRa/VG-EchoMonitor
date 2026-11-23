---
name: vanguard-galaxy-assets-and-code
description: >-
  Reverse-engineer Vanguard Galaxy's Mono C# assembly (Assembly-CSharp.dll),
  extract Unity 6 serialized assets (resources.assets, prefabs, ScriptableObjects,
  en-US localization TextAsset) read-only using UnityPy + TypeTreeGeneratorAPI,
  and triage Unity/BepInEx runtime logs. Use when investigating game internals,
  finding Harmony patch targets, extracting new asset catalogs, or debugging mod
  exceptions.
---

# Vanguard Galaxy Assets, Code & Log Reverse Engineering

Use this skill to inspect Vanguard Galaxy's C# logic, Unity 6 assets, localization tables, and runtime logs strictly **read-only**.

For detailed engine/layout facts, see [game_architecture.md](./references/game_architecture.md).

## 1. Inspecting & Decompiling `Assembly-CSharp.dll`

Vanguard Galaxy uses **Unity 6000.4.7f1 Mono** (`VanguardGalaxy_Data\Managed\Assembly-CSharp.dll`, ~6.8 MB, `.NET Standard 2.1`).

- **Decompilation (Preferred via `dev-vm`)**: Copy `Assembly-CSharp.dll` (and referenced `UnityEngine*.dll` files if needed) to a temporary working folder on `dev-vm` (`<dev-host>`) and decompile with `ilspycmd`:
  ```bash
  # Decompile full project for grepping / code navigation
  ilspycmd -p -o ./decompiled/Assembly-CSharp ./Managed/Assembly-CSharp.dll

  # Or decompile a single type quickly
  ilspycmd -t Namespace.TypeName ./Managed/Assembly-CSharp.dll
  ```
- **Local Quick Inspection**: On Windows, avoid modifying the Steam folder. Copy `Assembly-CSharp.dll` into `exports/` before opening in ILSpy / dnSpyEx if needed.

## 2. Extracting Unity 6 Serialized Assets (`UnityPy`)

Vanguard Galaxy strips MonoBehaviour TypeTrees from `resources.assets`, so plain `UnityPy` cannot deserialize MonoBehaviour fields unless paired with **`TypeTreeGeneratorAPI`** pointed at `VanguardGalaxy_Data\Managed\`.

### Environment Setup (Project-Local `.venv`)

```powershell
python -m venv .venv
.\.venv\Scripts\pip install UnityPy TypeTreeGeneratorAPI
```

### Standard Read-Only Extraction Pattern

Follow the proven pattern from `vanguard-galaxy-ships/scripts/extract_ships.py`:

```python
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

GAME_DATA = Path(r"c:\Program Files (x86)\Steam\steamapps\common\Vanguard Galaxy\VanguardGalaxy_Data")

env = UnityPy.load(str(GAME_DATA / "resources.assets"))
gen = TypeTreeGenerator("6000.4.7f1")
gen.load_local_dll_folder(str(GAME_DATA / "Managed"))
env.typetree_generator = gen

for obj in env.objects:
    if obj.type.name == "MonoBehaviour":
        tree = obj.read_typetree()  # Deserializes C# fields using Managed DLLs
```

### Resolving Resource Paths & Localization

- **ResourceManager**: Read the `ResourceManager` asset inside `resources.assets` (`obj.type.name == "ResourceManager"`) to map `m_Container` paths (e.g., `spaceships/...`) to `PPtr` `m_PathID` values.
- **Localization (`en-US`)**: Find `TextAsset` with `m_Name == "en-US"` in `resources.assets`. It contains `key=value` lines. Many fields in prefabs/code store `@KeyName` — strip the leading `@` and look up `KeyName` in `en-US` (e.g., `FactionName<Id>`, `ManufacturerName<Id>`). Note special overrides in C# (for example, `Puppeteers` maps to `FactionNamePuppeteers2`).

## 3. Triage & Debugging Runtime Logs

Check both Unity and BepInEx logs after launching the game:

- **Unity Player Log**:
  - Current run: `%USERPROFILE%\AppData\LocalLow\Bat Roost Games\VanguardGalaxy\Player.log`
  - Previous run: `%USERPROFILE%\AppData\LocalLow\Bat Roost Games\VanguardGalaxy\Player-prev.log`
- **BepInEx Log** (when BepInEx is installed):
  - `c:\Program Files (x86)\Steam\steamapps\common\Vanguard Galaxy\BepInEx\LogOutput.log`

Quick PowerShell triage for exceptions and Harmony errors:

```powershell
Select-String -Path "$env:USERPROFILE\AppData\LocalLow\Bat Roost Games\VanguardGalaxy\Player.log" `
  -Pattern "Exception|Error|Harmony|BepInEx|MissingMethod|NullReference" -Context 1,3
```
