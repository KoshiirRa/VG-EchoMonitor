#!/usr/bin/env python3
"""Read-only inspector for Vanguard Galaxy GZip-compressed JSON save files."""

from __future__ import annotations

import argparse
import gzip
import json
from datetime import datetime
from pathlib import Path
from typing import Any

DEFAULT_SAVES_DIR = Path.home() / "AppData" / "LocalLow" / "Bat Roost Games" / "VanguardGalaxy" / "Saves"


def resolve_save_path(target: str, saves_dir: Path) -> Path:
    candidate = Path(target)
    if candidate.is_file():
        return candidate
    if not target.endswith(".save"):
        candidate = saves_dir / f"{target}.save"
    else:
        candidate = saves_dir / target
    if candidate.is_file():
        return candidate
    raise SystemExit(f"Error: save file not found: {target} (searched in {saves_dir})")


def load_save(path: Path) -> dict[str, Any]:
    with gzip.open(path, "rt", encoding="utf-8") as fh:
        return json.load(fh)


def cmd_list(saves_dir: Path) -> int:
    if not saves_dir.is_dir():
        raise SystemExit(f"Error: saves directory not found: {saves_dir}")

    saves = sorted(saves_dir.glob("*.save"), key=lambda p: p.stat().st_mtime, reverse=True)
    if not saves:
        print(f"No .save files found in {saves_dir}")
        return 0

    print(f"Saves in {saves_dir} ({len(saves)} total):\n")
    print(f"{'Name':<24} {'Version':<12} {'Size (KB)':>10}  {'Modified'}")
    print("-" * 72)
    for save_path in saves:
        meta_path = save_path.with_suffix(".meta")
        version = "?"
        if meta_path.is_file():
            try:
                version = json.loads(meta_path.read_text(encoding="utf-8")).get("Version", "?")
            except (OSError, json.JSONDecodeError):
                version = "err"
        stat = save_path.stat()
        mtime = datetime.fromtimestamp(stat.st_mtime).strftime("%Y-%m-%d %H:%M:%S")
        print(f"{save_path.stem:<24} {version:<12} {stat.st_size / 1024:>10.1f}  {mtime}")
    return 0


def format_number_str(val: Any) -> str:
    try:
        return f"{int(str(val)):,}"
    except (ValueError, TypeError):
        return str(val)


def cmd_summary(path: Path) -> int:
    data = load_save(path)
    player = data.get("Player", {})
    if not isinstance(player, dict):
        raise SystemExit("Unexpected save format: missing 'Player' object.")

    empire = player.get("empireLevel")
    empire_lvl = empire.get("level", "?") if isinstance(empire, dict) else empire

    elapsed = player.get("elapsedTime")
    elapsed_str = f"{elapsed / 3600:.1f} hrs" if isinstance(elapsed, (int, float)) else str(elapsed)

    print(f"Save File        : {path.name}")
    print(f"Game Version     : {data.get('Version', '?')}")
    print(f"Elapsed Time     : {elapsed_str}")
    print(f"Credits          : {format_number_str(player.get('credits', 0))}")
    print(f"Vanguard Marks   : {format_number_str(player.get('vanguardMarks', 0))}")
    print(f"Empire Level     : {empire_lvl}")
    print(
        f"Guild Ranks      : Patrol={player.get('patrolRank', 0)}, "
        f"Bounty={player.get('bountyRank', 0)}, Industry={player.get('industryRank', 0)}"
    )
    print(f"Starter Ship     : {player.get('starterSpaceshipName', '?')}")
    print(
        f"Ships / Fleet    : {len(player.get('spaceShips') or [])} owned, "
        f"{len(player.get('activeFleet') or [])} in active fleet, "
        f"{len(player.get('capturedShips') or [])} captured"
    )
    print(
        f"Crew             : {len(player.get('crewMembers') or [])} assigned, "
        f"{len(player.get('unassignedCrew') or [])} unassigned"
    )
    print(
        f"Blueprints       : {len(player.get('blueprints') or {})} recipes, "
        f"{len(player.get('unlockedShipBlueprints') or [])} unlocked ship blueprints"
    )
    print(f"Active Missions  : {len(player.get('missions') or [])}")

    standings = []
    for entry in player.get("factionData") or []:
        if not isinstance(entry, dict):
            continue
        f1, f2, rep = entry.get("f1"), entry.get("f2"), entry.get("reputation")
        if f1 == "Player":
            standings.append((str(f2), rep))
        elif f2 == "Player":
            standings.append((str(f1), rep))

    if standings:
        standings.sort(key=lambda x: (x[1] is None, -(x[1] or 0)))
        print("\nPlayer Faction Standings:")
        for faction, rep in standings:
            print(f"  - {faction:<20} {rep}")

    return 0


def resolve_dot_path(obj: Any, dot_path: str | None) -> Any:
    if not dot_path:
        return obj
    cur = obj
    for part in dot_path.split("."):
        if isinstance(cur, dict) and part in cur:
            cur = cur[part]
        elif isinstance(cur, list) and part.isdigit():
            idx = int(part)
            if 0 <= idx < len(cur):
                cur = cur[idx]
            else:
                raise SystemExit(f"Error: list index out of range at '{part}' (len={len(cur)})")
        else:
            raise SystemExit(f"Error: path segment '{part}' not found.")
    return cur


def cmd_keys(path: Path, dot_path: str | None) -> int:
    data = load_save(path)
    node = resolve_dot_path(data, dot_path)
    label = dot_path or "<root>"
    print(f"Path: {label} ({type(node).__name__})")
    if isinstance(node, dict):
        for k, v in node.items():
            if isinstance(v, dict):
                print(f"  {k}: dict[{len(v)} keys]")
            elif isinstance(v, list):
                print(f"  {k}: list[{len(v)}]")
            else:
                preview = repr(v)
                if len(preview) > 80:
                    preview = preview[:77] + "..."
                print(f"  {k}: {type(v).__name__} = {preview}")
    elif isinstance(node, list):
        print(f"  list length: {len(node)}")
        for i, item in enumerate(node[:15]):
            preview = repr(item)
            if len(preview) > 100:
                preview = preview[:97] + "..."
            print(f"  [{i}]: {type(item).__name__} = {preview}")
        if len(node) > 15:
            print(f"  ... ({len(node) - 15} more items)")
    else:
        print(f"  {repr(node)}")
    return 0


def cmd_search(path: Path, query: str, limit: int) -> int:
    data = load_save(path)
    needle = query.lower()
    matches: list[tuple[str, str]] = []

    def walk(node: Any, prefix: str) -> None:
        if len(matches) >= limit:
            return
        if isinstance(node, dict):
            for k, v in node.items():
                child_path = f"{prefix}.{k}" if prefix else str(k)
                if needle in str(k).lower():
                    preview = repr(v)
                    matches.append((child_path, preview[:100] + ("..." if len(preview) > 100 else "")))
                    if len(matches) >= limit:
                        return
                walk(v, child_path)
        elif isinstance(node, list):
            for i, item in enumerate(node):
                walk(item, f"{prefix}.{i}")
                if len(matches) >= limit:
                    return
        elif isinstance(node, str) and needle in node.lower():
            matches.append((prefix, repr(node[:100])))

    walk(data, "")
    print(f"Search '{query}' in {path.name}: {len(matches)} match(es) (limit={limit})")
    for match_path, preview in matches:
        print(f"  {match_path} = {preview}")
    return 0


def cmd_dump(path: Path, out_path: Path) -> int:
    # Guard against accidentally writing inside the game's Saves directory
    if DEFAULT_SAVES_DIR in out_path.resolve().parents or out_path.resolve() == DEFAULT_SAVES_DIR:
        raise SystemExit("Error: refusing to write dump inside the game's Saves directory.")

    data = load_save(path)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(data, indent=2), encoding="utf-8", newline="\n")
    print(f"Decompressed {path.name} -> {out_path} ({out_path.stat().st_size / 1024:.1f} KB)")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Inspect Vanguard Galaxy GZip JSON save files (read-only).")
    parser.add_argument("--saves-dir", type=Path, default=DEFAULT_SAVES_DIR, help="Override Saves directory")

    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--list", action="store_true", help="List available save files")
    group.add_argument("--summary", metavar="SAVE", help="Show summary of a save file")
    group.add_argument("--keys", nargs="+", metavar=("SAVE", "DOT_PATH"), help="Inspect keys at optional dot-path")
    group.add_argument("--search", nargs=2, metavar=("SAVE", "QUERY"), help="Search keys/strings in a save file")
    group.add_argument("--dump", metavar="SAVE", help="Decompress a save file to pretty-printed JSON (requires --out)")

    parser.add_argument("--out", type=Path, help="Output JSON path for --dump")
    parser.add_argument("--limit", type=int, default=40, help="Max matches for --search (default: 40)")
    args = parser.parse_args()

    if args.list:
        return cmd_list(args.saves_dir)
    if args.summary:
        return cmd_summary(resolve_save_path(args.summary, args.saves_dir))
    if args.keys:
        save_name = args.keys[0]
        dot_path = args.keys[1] if len(args.keys) > 1 else None
        return cmd_keys(resolve_save_path(save_name, args.saves_dir), dot_path)
    if args.search:
        save_name, query = args.search
        return cmd_search(resolve_save_path(save_name, args.saves_dir), query, args.limit)
    if args.dump:
        if not args.out:
            raise SystemExit("Error: --dump requires --out <path.json>")
        return cmd_dump(resolve_save_path(args.dump, args.saves_dir), args.out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
