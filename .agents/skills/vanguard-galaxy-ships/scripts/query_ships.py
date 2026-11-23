#!/usr/bin/env python3
"""
Query playable spaceships from Vanguard Galaxy's pre-extracted prefab catalog.

Pure Python standard library (zero external dependencies).
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path
from typing import Any

DEFAULT_DATA = Path(__file__).resolve().parent.parent / "resources" / "playable_ships.json"

SIZE_ORDER = {"Tiny": 0, "Small": 1, "Medium": 2, "Large": 3}


def parse_size_arg(val: str) -> int:
    s = val.strip().lower()
    if s.startswith("size"):
        s = s[4:].strip()
    return int(s)


def format_hardpoints_compact(ship: dict[str, Any]) -> str:
    by_size = ship.get("hardpointsBySize", {})
    if not by_size:
        return "0"
    parts = []
    for sz in ("Large", "Medium", "Small", "Tiny"):
        if sz in by_size:
            parts.append(f"{by_size[sz]}{sz[0]}")
    return f"{ship['hardpointCount']} ({', '.join(parts)})"


def format_factions_compact(ship: dict[str, Any]) -> str:
    prereqs = ship.get("factionPrereqs", [])
    if not prereqs:
        return "-"
    parts = []
    for pr in prereqs:
        fac = pr.get("faction", "Any")
        rep = pr.get("reputationLevel", 0)
        cr = pr.get("conquestRank", 0)
        suffix = f" R{rep}" + (f"/C{cr}" if cr else "")
        parts.append(f"{fac}{suffix}")
    return "; ".join(parts)


def ship_matches(ship: dict[str, Any], args: argparse.Namespace) -> bool:
    if args.role:
        allowed = {r.lower() for r in args.role}
        if ship["role"].lower() not in allowed:
            return False

    if args.size:
        allowed_sizes = {parse_size_arg(s) for s in args.size}
        if ship["sizeNumber"] not in allowed_sizes:
            return False

    if args.min_size is not None and ship["sizeNumber"] < parse_size_arg(args.min_size):
        return False
    if args.max_size is not None and ship["sizeNumber"] > parse_size_arg(args.max_size):
        return False

    if args.ship_class:
        allowed = {c.lower() for c in args.ship_class}
        if ship["shipClass"].lower() not in allowed:
            return False

    if args.manufacturer:
        wanted = [m.lower() for m in args.manufacturer]
        mfr_name = ship["manufacturer"].lower()
        mfr_id = ship.get("manufacturerId", "").lower()
        if not any(w in mfr_name or w == mfr_id for w in wanted):
            return False

    if args.faction:
        wanted = [f.lower() for f in args.faction]
        prereqs = ship.get("factionPrereqs", [])
        matched_fac = False
        for pr in prereqs:
            fac_name = pr.get("faction", "Any").lower()
            fac_id = pr.get("factionId", "").lower()
            if any(w in fac_name or w == fac_id for w in wanted):
                matched_fac = True
                break
        if not matched_fac:
            return False

    if args.name:
        q = args.name.lower()
        if q not in ship["displayName"].lower() and q not in ship["prefabName"].lower():
            return False

    if args.has_module:
        ship_slots = {m["slot"].lower() for m in ship.get("moduleSlots", [])}
        for req in args.has_module:
            if req.lower() not in ship_slots:
                return False

    if args.hardpoint_size:
        wanted_sz = args.hardpoint_size.strip().lower()
        if not any(hp["size"].lower() == wanted_sz for hp in ship.get("hardpoints", [])):
            return False

    if args.min_hardpoints is not None and ship["hardpointCount"] < args.min_hardpoints:
        return False
    if args.max_hardpoints is not None and ship["hardpointCount"] > args.max_hardpoints:
        return False

    if args.min_boosters is not None and ship["boosterSlotCount"] < args.min_boosters:
        return False

    if args.min_cargo is not None and ship["cargoCapacity"] < args.min_cargo:
        return False
    if args.max_cargo is not None and ship["cargoCapacity"] > args.max_cargo:
        return False

    if args.min_hull is not None and ship["hullHPScale"] < args.min_hull:
        return False
    if args.min_armor is not None and ship["armorHPScale"] < args.min_armor:
        return False
    if args.min_shield is not None and ship["shieldHPScale"] < args.min_shield:
        return False

    if args.min_tonnage is not None and ship["tonnage"] < args.min_tonnage:
        return False
    if args.max_tonnage is not None and ship["tonnage"] > args.max_tonnage:
        return False

    if args.min_officers is not None and ship["maxOfficers"] < args.min_officers:
        return False
    if args.min_warp is not None and ship["baseMaxWarpSpeed"] < args.min_warp:
        return False

    if args.min_level is not None and ship["levelRequirement"] < args.min_level:
        return False
    if args.max_level is not None and ship["levelRequirement"] > args.max_level:
        return False

    if args.min_points is not None and ship["pointValue"] < args.min_points:
        return False
    if args.max_points is not None and ship["pointValue"] > args.max_points:
        return False

    if args.blueprint_only and not ship.get("hasBlueprint", True):
        return False

    return True


SORT_KEYS = {
    "name": lambda s: (s["displayName"].lower(), s["prefabName"].lower()),
    "size": lambda s: (s["sizeNumber"], s["displayName"].lower()),
    "role": lambda s: (s["role"], s["sizeNumber"], s["displayName"].lower()),
    "cargo": lambda s: s["cargoCapacity"],
    "hull": lambda s: s["hullHPScale"],
    "armor": lambda s: s["armorHPScale"],
    "shield": lambda s: s["shieldHPScale"],
    "tonnage": lambda s: s["tonnage"],
    "hardpoints": lambda s: s["hardpointCount"],
    "boosters": lambda s: s["boosterSlotCount"],
    "officers": lambda s: s["maxOfficers"],
    "warp": lambda s: s["baseMaxWarpSpeed"],
    "level": lambda s: s["levelRequirement"],
    "cost": lambda s: s["costMultiplier"],
    "points": lambda s: s["pointValue"],
}


def print_summary(ships: list[dict[str, Any]]) -> None:
    print(f"Total playable ships: {len(ships)}\n")

    def bucket(key_fn) -> dict[str, int]:
        counts: dict[str, int] = {}
        for s in ships:
            k = key_fn(s)
            counts[k] = counts.get(k, 0) + 1
        return counts

    print("By Role:")
    for k, v in sorted(bucket(lambda s: s["role"]).items()):
        print(f"  {k:<12} {v}")

    print("\nBy Size:")
    for k, v in sorted(bucket(lambda s: s["size"]).items()):
        print(f"  {k:<12} {v}")

    print("\nBy Role & Class:")
    for k, v in sorted(bucket(lambda s: f"{s['role']} / {s['size']} ({s['shipClass']})").items()):
        print(f"  {k:<34} {v}")

    print("\nBy Manufacturer:")
    for k, v in sorted(bucket(lambda s: s["manufacturer"]).items()):
        print(f"  {k:<18} {v}")

    fac_counts: dict[str, int] = {}
    for s in ships:
        for pr in s.get("factionPrereqs", []):
            fac = pr.get("faction", "Any")
            fac_counts[fac] = fac_counts.get(fac, 0) + 1
    print("\nBy Faction Prerequisite:")
    for k, v in sorted(fac_counts.items()):
        print(f"  {k:<18} {v}")


def print_detail(ships: list[dict[str, Any]]) -> None:
    for s in ships:
        print(f"=== {s['displayName']} (Prefab: {s['prefabName']}) ===")
        print(f"  Resource Path : {s['resourcePath']}")
        print(f"  Role / Size   : {s['role']} | {s['size']} ({s['shipClass']}) | Manufacturer: {s['manufacturer']}")
        print(f"  HP Scales     : Hull {s['hullHPScale']} | Armor {s['armorHPScale']} | Shield {s['shieldHPScale']}")
        print(f"  Utility       : Cargo {s['cargoCapacity']} | Tonnage {s['tonnage']} | Max Officers {s['maxOfficers']} | Fleet Points {s['pointValue']}")
        print(f"  Warp / Cost   : Warp Speed {s['baseMaxWarpSpeed']} (Accel {s['baseWarpAcceleration']}) | Cost Mult {s['costMultiplier']}x")
        print(f"  Unlock / Shop : Level {s['levelRequirement']} | Area Lvl {s['minAreaLevelRequirement']}-{s['maxAreaLevelRequirement'] or '∞'} | Commendations {s['conquestCommendations']} | Blueprint: {s['hasBlueprint']}")
        print(f"  Factions      : {format_factions_compact(s)}")
        mods = ", ".join(f"{m['slot']} ({m['size']})" for m in s["moduleSlots"])
        print(f"  Module Slots ({s['moduleSlotCount']}): {mods or 'None'}")
        hps = ", ".join(
            f"#{h['index']} {h['size']}" + (" [rot]" if h["rotate"] else "") + (" [under]" if h["mountUnderHull"] else "")
            for h in s["hardpoints"]
        )
        print(f"  Hardpoints ({s['hardpointCount']}): {hps or 'None'}")
        boosts = ", ".join(b["size"] for b in s["boosterSlots"])
        print(f"  Boosters ({s['boosterSlotCount']}): {boosts or 'None'}")
        print()


def print_table(ships: list[dict[str, Any]]) -> None:
    headers = [
        "Name",
        "Prefab",
        "Role",
        "Size",
        "Class",
        "Mfr",
        "Hull",
        "Arm",
        "Shd",
        "Cargo",
        "Ton",
        "HPts",
        "Bst",
        "Off",
        "FPts",
        "Warp",
        "Lvl",
        "Faction",
    ]
    rows = []
    for s in ships:
        rows.append(
            [
                s["displayName"],
                s["prefabName"],
                s["role"],
                s["size"],
                s["shipClass"],
                s["manufacturer"],
                f"{s['hullHPScale']:.2f}",
                f"{s['armorHPScale']:.2f}",
                f"{s['shieldHPScale']:.2f}",
                str(s["cargoCapacity"]),
                str(s["tonnage"]),
                format_hardpoints_compact(s),
                str(s["boosterSlotCount"]),
                str(s["maxOfficers"]),
                str(s["pointValue"]),
                f"{s['baseMaxWarpSpeed']:.2f}",
                str(s["levelRequirement"]),
                format_factions_compact(s),
            ]
        )

    widths = [len(h) for h in headers]
    for r in rows:
        for i, cell in enumerate(r):
            widths[i] = max(widths[i], len(cell))

    def fmt_row(r: list[str]) -> str:
        return " | ".join(cell.ljust(widths[i]) for i, cell in enumerate(r))

    print(fmt_row(headers))
    print("-+-".join("-" * w for w in widths))
    for r in rows:
        print(fmt_row(r))
    print(f"\n({len(ships)} ships matched)")


def print_csv(ships: list[dict[str, Any]]) -> None:
    fields = [
        "displayName",
        "prefabName",
        "resourcePath",
        "role",
        "size",
        "shipClass",
        "manufacturer",
        "hullHPScale",
        "armorHPScale",
        "shieldHPScale",
        "cargoCapacity",
        "tonnage",
        "hardpointCount",
        "boosterSlotCount",
        "moduleSlotCount",
        "maxOfficers",
        "pointValue",
        "baseMaxWarpSpeed",
        "baseWarpAcceleration",
        "costMultiplier",
        "levelRequirement",
        "hasBlueprint",
    ]
    writer = csv.DictWriter(sys.stdout, fieldnames=fields, extrasaction="ignore")
    writer.writeheader()
    for s in ships:
        writer.writerow(s)


def main() -> None:
    parser = argparse.ArgumentParser(description="Query Vanguard Galaxy playable ships.")
    parser.add_argument("--data", type=Path, default=DEFAULT_DATA, help="Path to playable_ships.json")
    parser.add_argument("--summary", action="store_true", help="Show catalog summary counts and exit")
    parser.add_argument("--detail", action="store_true", help="Show full detail view for matched ships")

    # Identity filters
    parser.add_argument("--name", help="Substring match on displayName or prefabName")
    parser.add_argument("--role", nargs="+", help="Filter by role (Combat, Mining, Salvaging, Cargo)")
    parser.add_argument("--size", nargs="+", help="Filter by size (e.g. Size1..Size6 or 1..6)")
    parser.add_argument("--min-size", help="Minimum size (e.g. 3 or Size3)")
    parser.add_argument("--max-size", help="Maximum size (e.g. 5 or Size5)")
    parser.add_argument("--ship-class", nargs="+", help="Filter by class (e.g. Cutter, Frigate, Hauler, Barge)")
    parser.add_argument("--manufacturer", nargs="+", help="Filter by manufacturer (e.g. Gold, Pirate, Police, Utility)")
    parser.add_argument("--faction", nargs="+", help="Filter by faction prerequisite (e.g. Any, Marauders, Fanatics, BountyGuild)")

    # Combat & slots
    parser.add_argument("--has-module", nargs="+", help="Require module slot(s) (e.g. HangarBay, DroneBay, TorpedoBay, ShieldGenerator)")
    parser.add_argument("--hardpoint-size", help="Require at least one hardpoint of size (Tiny, Small, Medium, Large)")
    parser.add_argument("--min-hardpoints", type=int, help="Minimum hardpoint count")
    parser.add_argument("--max-hardpoints", type=int, help="Maximum hardpoint count")
    parser.add_argument("--min-boosters", type=int, help="Minimum booster slot count")
    parser.add_argument("--min-hull", type=float, help="Minimum hullHPScale")
    parser.add_argument("--min-armor", type=float, help="Minimum armorHPScale")
    parser.add_argument("--min-shield", type=float, help="Minimum shieldHPScale")

    # Utility & economy
    parser.add_argument("--min-cargo", type=int, help="Minimum cargo capacity")
    parser.add_argument("--max-cargo", type=int, help="Maximum cargo capacity")
    parser.add_argument("--min-tonnage", type=int, help="Minimum tonnage")
    parser.add_argument("--max-tonnage", type=int, help="Maximum tonnage")
    parser.add_argument("--min-officers", type=int, help="Minimum maxOfficers")
    parser.add_argument("--min-warp", type=float, help="Minimum baseMaxWarpSpeed")
    parser.add_argument("--min-level", type=int, help="Minimum level requirement")
    parser.add_argument("--max-level", type=int, help="Maximum level requirement")
    parser.add_argument("--min-points", type=int, help="Minimum fleet capacity points (pointValue)")
    parser.add_argument("--max-points", type=int, help="Maximum fleet capacity points (pointValue)")
    parser.add_argument("--blueprint-only", action="store_true", help="Exclude ships with noBlueprint=true")

    # Output
    parser.add_argument("--sort", choices=sorted(SORT_KEYS.keys()), default="size", help="Sort field")
    parser.add_argument("--desc", action="store_true", help="Sort descending")
    parser.add_argument("--limit", type=int, help="Limit number of results")
    parser.add_argument("--format", choices=("table", "json", "csv"), default="table", help="Output format")

    args = parser.parse_args()

    payload = json.loads(args.data.read_text(encoding="utf-8"))
    ships = payload["ships"]

    if args.summary:
        print_summary(ships)
        return

    matched = [s for s in ships if ship_matches(s, args)]
    matched.sort(key=SORT_KEYS[args.sort], reverse=args.desc)
    if args.limit is not None:
        matched = matched[: args.limit]

    if args.detail:
        print_detail(matched)
    elif args.format == "json":
        print(json.dumps(matched, indent=2))
    elif args.format == "csv":
        print_csv(matched)
    else:
        print_table(matched)


if __name__ == "__main__":
    main()
