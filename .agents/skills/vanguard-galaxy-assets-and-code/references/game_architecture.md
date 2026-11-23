# Vanguard Galaxy Game Architecture Reference

Snapshot of verified technical facts for **Vanguard Galaxy** (Bat Roost Games).

## Engine & Runtime

- **Executable**: `c:\Program Files (x86)\Steam\steamapps\common\Vanguard Galaxy\VanguardGalaxy.exe`
- **Unity Version**: `6000.4.7f1` (`f3c3c4248748`)
- **Runtime**: Mono (`MonoBleedingEdge`, `mono-2.0-bdwgc.dll`, x64)
- **API Compatibility**: `.NET Standard 2.1` (`netstandard.dll` 2.1.0.0, `mscorlib.dll` 4.6.57.0)
- **Non-Unity Managed Assemblies**:
  - `VanguardGalaxy_Data\Managed\Assembly-CSharp.dll` (~6.86 MB) — primary game code
  - `VanguardGalaxy_Data\Managed\com.rlabrecque.steamworks.net.dll` — Steamworks integration

## Key Data Files (`VanguardGalaxy_Data\`)

| File | Purpose |
| :--- | :--- |
| `resources.assets` + `.resS` | Main `Resources/` prefabs (ships, modules, items, recipes), `ResourceManager`, and `en-US` localization `TextAsset` |
| `globalgamemanagers` / `.assets` | Unity project settings, build scenes, tag/layer managers |
| `level0`–`level17` & `sharedassets0`–`17` | Scene hierarchies and scene-specific assets |
| `ScriptingAssemblies.json` | List of loaded managed assemblies |
| `RuntimeInitializeOnLoads.json` | Methods invoked via `[RuntimeInitializeOnLoadMethod]` at startup |

## Known Enum Mappings (from `Assembly-CSharp.dll`)

- **Roles**: `0=Generic`, `1=Combat`, `2=Mining`, `3=Salvaging`, `4=Cargo`
- **Ship Sizes**: `0=Drone`, `1=Size1` .. `8=Size8` (playable ships span `Size1`–`Size6`)
- **Module/Hardpoint Sizes**: `0=Tiny`, `1=Small`, `2=Medium`, `3=Large`
- **Equipment Slots**: `0=Reactor`, `1=ShieldGenerator`, `2=Armor`, `3=Engine`, `4=DroneBay`, `5=MiningSystem`, `6=SalvageSystem`, `7=TractorBeam`, `8=Scanner`, `9=Hardpoint`, `10=Booster`, `11=TorpedoBay`, `12=Hull`, `13=HangarBay`
- **Manufacturers**: `0=Gold` (Spirit Design), `1=Red` (RedStar Forge), `2=Blue`, `3=Mining` (Mindus Manufacturing), `4=EliteCombat` (Akai Armory), `5=GenericCombat` (Frontier), `6=Utility` (Utixon Corp), `7=Pirate` (Marade Wharf), `8=Police` (Custos Arms), `9=ConsumerGoods`, `10=MedicalGoods`, `11=DarkspaceRegular` (Obrix), `12=Stellar` (Stellar Industries), `13=FanaticsAlien` (Varlis Construct), `14=Umbral` (Kharon Forgeworks), `15=Malcyte`

## User Data Directory

- Path: `%USERPROFILE%\AppData\LocalLow\Bat Roost Games\VanguardGalaxy\`
- `Saves\*.save` — GZip-compressed UTF-8 JSON (`Version`, `Player`)
- `Saves\*.meta` — Plain UTF-8 JSON (`{"Version":"..."}`)
- `Player.log` / `Player-prev.log` — Unity runtime logs
