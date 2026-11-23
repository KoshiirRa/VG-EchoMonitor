#!/usr/bin/env python3
"""
Extract playable spaceship prefab data from Vanguard Galaxy's Unity assets (read-only).

Requires: UnityPy, TypeTreeGeneratorAPI
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

DEFAULT_GAME_DATA = Path(
    r"c:\Program Files (x86)\Steam\steamapps\common\Vanguard Galaxy\VanguardGalaxy_Data"
)
DEFAULT_OUTPUT = Path(__file__).resolve().parent.parent / "resources" / "playable_ships.json"

ROLES = {
    0: "Generic",
    1: "Combat",
    2: "Mining",
    3: "Salvaging",
    4: "Cargo",
}

SIZES = {
    0: "Drone",
    1: "Size1",
    2: "Size2",
    3: "Size3",
    4: "Size4",
    5: "Size5",
    6: "Size6",
    7: "Size7",
    8: "Size8",
}

MODULE_SIZES = {
    0: "Tiny",
    1: "Small",
    2: "Medium",
    3: "Large",
}

EQUIPMENT_SLOTS = {
    0: "Reactor",
    1: "ShieldGenerator",
    2: "Armor",
    3: "Engine",
    4: "DroneBay",
    5: "MiningSystem",
    6: "SalvageSystem",
    7: "TractorBeam",
    8: "Scanner",
    9: "Hardpoint",
    10: "Booster",
    11: "TorpedoBay",
    12: "Hull",
    13: "HangarBay",
}

MANUFACTURERS = {
    0: "Gold",
    1: "Red",
    2: "Blue",
    3: "Mining",
    4: "EliteCombat",
    5: "GenericCombat",
    6: "Utility",
    7: "Pirate",
    8: "Police",
    9: "ConsumerGoods",
    10: "MedicalGoods",
    11: "DarkspaceRegular",
    12: "Stellar",
    13: "FanaticsAlien",
    14: "Umbral",
    15: "Malcyte",
}

NON_PLAYABLE_TOP_FOLDERS = {"_base spaceship", "autonomousdrone", "salvageable"}

# Special localization key overrides from Assembly-CSharp.dll (e.g., Puppeteers.get_name() -> @FactionNamePuppeteers2)
FACTION_LOC_KEY_OVERRIDES = {
    "Puppeteers": "FactionNamePuppeteers2",
}


def load_localization(env: Any) -> dict[str, str]:
    """Parse en-US TextAsset localization key=value pairs from resources.assets."""
    loc: dict[str, str] = {}
    for obj in env.objects:
        if obj.type.name == "TextAsset":
            data = obj.read()
            if data.m_Name == "en-US":
                for line in data.m_Script.splitlines():
                    if "=" in line:
                        k, v = line.split("=", 1)
                        loc[k.strip()] = v.strip()
                break
    return loc


def resolve_faction_name(faction_id: str, loc: dict[str, str]) -> str:
    if not faction_id or faction_id == "Any":
        return "Any"
    key = FACTION_LOC_KEY_OVERRIDES.get(faction_id, f"FactionName{faction_id}")
    return loc.get(key, faction_id)


def resolve_manufacturer_name(mfr_id: str, loc: dict[str, str]) -> str:
    return loc.get(f"ManufacturerName{mfr_id}", mfr_id)


def parse_class_from_path(resource_path: str) -> str:
    """Extract ship class label (e.g., 'Courier', 'Cutter', 'Destroyer') from resource path."""
    parts = resource_path.split("/")
    for part in parts:
        m = re.match(r"^size\s+\d+\s*-\s*(.+)$", part, flags=re.IGNORECASE)
        if m:
            return m.group(1).strip().title()
    return ""


def read_hardpoint(res_file: Any, path_id: int) -> dict[str, Any] | None:
    obj = res_file.objects.get(path_id)
    if not obj or obj.type.name != "MonoBehaviour":
        return None
    tree = obj.read_typetree()
    size_val = tree.get("size", 0)
    return {
        "index": tree.get("<index>k__BackingField", 0),
        "size": MODULE_SIZES.get(size_val, str(size_val)),
        "sizeValue": size_val,
        "rotate": bool(tree.get("rotate", 0)),
        "mountUnderHull": bool(tree.get("mountUnderHull", 0)),
    }


def extract_playable_ships(game_data: Path, unity_version: str = "6000.4.7f1") -> dict[str, Any]:
    env = UnityPy.load(str(game_data))
    ttg = TypeTreeGenerator(unity_version)
    ttg.load_local_dll_folder(str(game_data / "Managed"))
    env.typetree_generator = ttg

    loc = load_localization(env)

    ship_paths: dict[int, str] = {}
    for obj in env.objects:
        if obj.type.name == "ResourceManager":
            rm = obj.read()
            for path, pptr in rm.m_Container:
                if path.lower().startswith("spaceships/"):
                    ship_paths[pptr.path_id] = path

    res_file = next(
        f for name, f in env.files.items() if name.lower().endswith("resources.assets")
    )

    ships: list[dict[str, Any]] = []
    skipped: list[dict[str, str]] = []

    for pid, rpath in sorted(ship_paths.items(), key=lambda kv: kv[1]):
        parts = rpath.split("/")
        top_folder = parts[1].lower() if len(parts) > 1 else ""
        if top_folder in NON_PLAYABLE_TOP_FOLDERS:
            skipped.append({"resourcePath": rpath, "reason": f"non-playable folder ({top_folder})"})
            continue

        go_obj = res_file.objects.get(pid)
        if not go_obj or go_obj.type.name != "GameObject":
            continue
        go_tree = go_obj.read_typetree()
        prefab_name = go_tree.get("m_Name", "")

        ship_tree = None
        for comp_ref in go_tree.get("m_Component", []):
            comp_obj = res_file.objects.get(comp_ref["component"]["m_PathID"])
            if comp_obj and comp_obj.type.name == "MonoBehaviour":
                t = comp_obj.read_typetree()
                if "<shipRoleType>k__BackingField" in t:
                    ship_tree = t
                    break

        if not ship_tree:
            continue

        rt = ship_tree["<shipRoleType>k__BackingField"]
        role_val = rt.get("role", 0)
        size_val = rt.get("spaceShipType", 0)
        has_commander = bool(ship_tree.get("<hasCommander>k__BackingField", 0))

        if size_val == 0 or not has_commander:
            skipped.append(
                {
                    "resourcePath": rpath,
                    "reason": f"drone/non-commandable (size={size_val}, hasCommander={has_commander})",
                }
            )
            continue

        # Module slots
        modules = []
        for ms in ship_tree.get("<moduleSlots>k__BackingField", []):
            slot_val = ms.get("<slot>k__BackingField", 0)
            msize_val = ms.get("<size>k__BackingField", 0)
            modules.append(
                {
                    "slot": EQUIPMENT_SLOTS.get(slot_val, str(slot_val)),
                    "size": MODULE_SIZES.get(msize_val, str(msize_val)),
                    "sizeValue": msize_val,
                }
            )

        # Hardpoints
        hardpoints = []
        for hp_ref in ship_tree.get("<hardpointSlots>k__BackingField", []):
            hp_pid = hp_ref.get("m_PathID", 0)
            if hp_pid:
                hp = read_hardpoint(res_file, hp_pid)
                if hp:
                    hardpoints.append(hp)

        hp_by_size: dict[str, int] = {}
        for hp in hardpoints:
            hp_by_size[hp["size"]] = hp_by_size.get(hp["size"], 0) + 1

        # Booster slots
        boosters = []
        for bs in ship_tree.get("<boosterSlots>k__BackingField", []):
            bsize_val = bs.get("<size>k__BackingField", 0)
            boosters.append(
                {
                    "size": MODULE_SIZES.get(bsize_val, str(bsize_val)),
                    "sizeValue": bsize_val,
                }
            )

        # Shop requirements
        shop_raw = ship_tree.get("<shopItemData>k__BackingField", {})
        prereqs = []
        for pr in shop_raw.get("<factionPrereq>k__BackingField", []):
            fac_id = pr.get("<faction>k__BackingField", "") or "Any"
            prereqs.append(
                {
                    "factionId": fac_id,
                    "faction": resolve_faction_name(fac_id, loc),
                    "reputationLevel": pr.get("<reputationLevel>k__BackingField", 0),
                    "conquestRank": pr.get("<conquestRank>k__BackingField", 0),
                }
            )

        manu_val = ship_tree.get("<manufacturer>k__BackingField", 0)
        manu_id = MANUFACTURERS.get(manu_val, str(manu_val))
        display_name = ship_tree.get("<displayName>k__BackingField", "") or prefab_name

        ships.append(
            {
                "displayName": display_name,
                "prefabName": prefab_name,
                "resourcePath": rpath,
                "role": ROLES.get(role_val, str(role_val)),
                "size": SIZES.get(size_val, str(size_val)),
                "sizeNumber": size_val,
                "shipClass": parse_class_from_path(rpath),
                "manufacturerId": manu_id,
                "manufacturer": resolve_manufacturer_name(manu_id, loc),
                "hullHPScale": round(float(ship_tree.get("<hullHPScale>k__BackingField", 0.0)), 4),
                "armorHPScale": round(float(ship_tree.get("<armorHPScale>k__BackingField", 0.0)), 4),
                "shieldHPScale": round(float(ship_tree.get("<shieldHPScale>k__BackingField", 0.0)), 4),
                "cargoCapacity": int(ship_tree.get("<_cargoCapacity>k__BackingField", 0)),
                "tonnage": int(ship_tree.get("<tonnage>k__BackingField", 0)),
                "maxOfficers": int(ship_tree.get("<maxOfficers>k__BackingField", 0)),
                "baseMaxWarpSpeed": round(float(ship_tree.get("baseMaxWarpSpeed", 0.0)), 4),
                "baseWarpAcceleration": round(float(ship_tree.get("baseWarpAcceleration", 0.0)), 4),
                "costMultiplier": round(float(ship_tree.get("<costMultiplier>k__BackingField", 1.0)), 4),
                "pointValue": int(ship_tree.get("<pointValue>k__BackingField", 0)),
                "baseExperienceReward": round(float(ship_tree.get("<baseExperienceReward>k__BackingField", 0.0)), 2),
                "hardpointCount": len(hardpoints),
                "hardpointsBySize": hp_by_size,
                "hardpoints": hardpoints,
                "moduleSlotCount": len(modules),
                "moduleSlots": modules,
                "boosterSlotCount": len(boosters),
                "boosterSlots": boosters,
                "levelRequirement": int(shop_raw.get("levelRequirement", 0)),
                "minAreaLevelRequirement": int(shop_raw.get("minAreaLevelRequirement", 0)),
                "maxAreaLevelRequirement": int(shop_raw.get("maxAreaLevelRequirement", 0)),
                "conquestCommendations": int(shop_raw.get("conquestCommendations", 0)),
                "factionPrereqs": prereqs,
                "hasBlueprint": not bool(ship_tree.get("<noBlueprint>k__BackingField", 0)),
                "noRandomSpawn": bool(ship_tree.get("<noRandomSpawn>k__BackingField", 0)),
                "noBoardable": bool(ship_tree.get("noBoardable", 0)),
                "dockSideways": bool(ship_tree.get("<dockSideways>k__BackingField", 0)),
                "shipyardScale": round(float(ship_tree.get("<shipyardScale>k__BackingField", 1.0)), 4),
                "maxDecalsPerDoor": int(ship_tree.get("<maxDecalsPerDoor>k__BackingField", 0)),
            }
        )

    ships.sort(key=lambda s: (s["role"], s["sizeNumber"], s["displayName"].lower(), s["prefabName"].lower()))

    return {
        "unityVersion": unity_version,
        "source": str(game_data / "resources.assets"),
        "playableShipCount": len(ships),
        "skippedCount": len(skipped),
        "ships": ships,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Extract Vanguard Galaxy playable ship prefabs (read-only).")
    parser.add_argument("--game-data", type=Path, default=DEFAULT_GAME_DATA, help="Path to VanguardGalaxy_Data")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT, help="Output JSON file path")
    parser.add_argument("--unity-version", default="6000.4.7f1", help="Unity version for TypeTreeGenerator")
    args = parser.parse_args()

    result = extract_playable_ships(args.game_data, args.unity_version)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(f"Extracted {result['playableShipCount']} playable ships (skipped {result['skippedCount']}) -> {args.output}")


if __name__ == "__main__":
    main()
