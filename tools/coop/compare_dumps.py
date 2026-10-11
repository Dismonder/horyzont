"""Compares the digest dumps of one check break (K4-19): the server dump and the dump of every bot.

    python -I tools/coop/compare_dumps.py --server break001/server.jsonl break001/bot0.jsonl ... [--out result.json]
    python -I tools/coop/compare_dumps.py --self-test

A dump has one JSON object per line: {"kind": "construct"|"terrain", "key", "version", "hash"} (constructs add "pos": [x, y, z]);
a bot dump starts with {"kind": "bot", "key": name, "role", "in_game", "feet": [x, y, z]}. Checks:

1. equal: every entry of a bot equals the entry of the server (version and hash). A terrain block of version 0 the server does not
   list is a generator block and is fine.
2. coverage: every server entry in the shared zone is held by every builder in game. The zone is the sphere of --zone-radius metres
   (100) around the centroid of the feet of the builders; a construct is in it by its origin, a terrain block by its centre.
3. blobs: bots without voxels compare terrain by (key, version); bots that hold the same (key, version) also hold the same hash.

Exit code 0 when every check passed. The result (also written to --out) is {"passed", "checks": [{"name", "passed", "detail"}],
"entries": {...}, "first_difference"}.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from dataclasses import dataclass, field
from pathlib import Path

ZONE_RADIUS = 100.0
BLOCK_SIZE = 16


@dataclass
class Dump:
    name: str
    role: str = "server"
    in_game: bool = True
    feet: tuple[float, float, float] | None = None
    entries: dict[tuple[str, str], dict] = field(default_factory=dict)


def parse_dump(name: str, lines: list[str]) -> Dump:
    dump = Dump(name)
    for number, raw in enumerate(lines, 1):
        line = raw.strip()
        if not line:
            continue
        try:
            item = json.loads(line)
        except json.JSONDecodeError as error:
            raise ValueError(f"{name}:{number}: not JSON: {error}") from error
        kind = item.get("kind")
        if kind == "bot":
            dump.name = item.get("key", name)
            dump.role = item.get("role", "")
            dump.in_game = bool(item.get("in_game", False))
            feet = item.get("feet")
            dump.feet = (float(feet[0]), float(feet[1]), float(feet[2])) if feet else None
            continue
        if kind not in ("construct", "terrain") or "key" not in item or "version" not in item or "hash" not in item:
            raise ValueError(f"{name}:{number}: not an entry: {line}")
        dump.entries[(kind, str(item["key"]))] = item
    return dump


def load_dump(path: Path) -> Dump:
    return parse_dump(path.stem, path.read_text(encoding="utf-8").splitlines())


def position(kind: str, key: str, entry: dict, voxel: float) -> tuple[float, float, float] | None:
    if kind == "construct":
        pos = entry.get("pos")
        return (float(pos[0]), float(pos[1]), float(pos[2])) if pos else None
    parts = key.split("/")
    if len(parts) != 5 or parts[1] != "0":
        return None
    edge = BLOCK_SIZE * voxel
    x, y, z = (int(parts[2]), int(parts[3]), int(parts[4]))
    return ((x + 0.5) * edge, (y + 0.5) * edge, (z + 0.5) * edge)


def distance(a: tuple[float, float, float], b: tuple[float, float, float]) -> float:
    return math.sqrt(sum((p - q) ** 2 for p, q in zip(a, b)))


def compare(server: Dump, bots: list[Dump], zone_radius: float = ZONE_RADIUS, voxel: float = 1.0) -> dict:
    differences: list[str] = []
    for bot in bots:
        for key, entry in sorted(bot.entries.items()):
            expected = server.entries.get(key)
            if expected is None:
                if key[0] == "terrain" and int(entry["version"]) == 0:
                    continue
                differences.append(f"{bot.name}: {key[0]} {key[1]} v{entry['version']} is not on the server")
            elif int(expected["version"]) != int(entry["version"]) or str(expected["hash"]) != str(entry["hash"]):
                differences.append(
                    f"{bot.name}: {key[0]} {key[1]} v{entry['version']}/{entry['hash']}, server v{expected['version']}/{expected['hash']}"
                )
            elif "voxels" in expected and "voxels" in entry and str(expected["voxels"]) != str(entry["voxels"]):
                # Bots in the engine hold voxels: their blocks must also equal the server voxel for voxel.
                differences.append(f"{bot.name}: terrain {key[1]} voxels {entry['voxels']}, server {expected['voxels']}")

    builders = [b for b in bots if b.role == "builder" and b.in_game and b.feet is not None]
    missing: list[str] = []
    zone = None
    if builders:
        zone = tuple(sum(b.feet[i] for b in builders) / len(builders) for i in range(3))  # type: ignore[index]
        for key, entry in sorted(server.entries.items()):
            where = position(key[0], key[1], entry, voxel)
            if where is None or distance(where, zone) > zone_radius:  # type: ignore[arg-type]
                continue
            for bot in builders:
                if key not in bot.entries:
                    missing.append(f"{bot.name}: {key[0]} {key[1]} in the zone is missing")

    blobs: dict[tuple[str, int], tuple[str, str]] = {}
    conflicts: list[str] = []
    for bot in bots:
        for key, entry in bot.entries.items():
            if key[0] != "terrain":
                continue
            pair = (key[1], int(entry["version"]))
            seen = blobs.setdefault(pair, (bot.name, str(entry["hash"])))
            if seen[1] != str(entry["hash"]):
                conflicts.append(f"{key[1]} v{pair[1]}: {seen[0]} {seen[1]}, {bot.name} {entry['hash']}")

    checks = [
        {"name": "entries_equal_server", "passed": not differences, "detail": f"{len(differences)} differences"},
        {
            "name": "builders_cover_zone", "passed": bool(builders) and not missing,
            "detail": f"{len(builders)} builders, {len(missing)} missing" + ("" if builders else "; no builder in game"),
        },
        {"name": "same_version_same_blob", "passed": not conflicts, "detail": f"{len(conflicts)} conflicts"},
    ]
    first = (differences + missing + conflicts)[:1]
    return {
        "passed": all(c["passed"] for c in checks),
        "checks": checks,
        "entries": {"server": len(server.entries), **{b.name: len(b.entries) for b in bots}},
        "zone": list(zone) if zone else None,
        "first_difference": first[0] if first else None,
        "differences": (differences + missing + conflicts)[:50],
    }


def self_test() -> int:
    server = parse_dump("server", [
        '{"kind":"construct","key":"5","version":3,"hash":"00000000000000aa","pos":[4000.5,1,2]}',
        '{"kind":"construct","key":"9","version":1,"hash":"00000000000000bb","pos":[4000,900,0]}',
        '{"kind":"terrain","key":"1/0/250/0/0","version":2,"hash":"00000000000000cc"}',
        '{"kind":"terrain","key":"1/0/250/40/0","version":1,"hash":"00000000000000dd"}',
    ])
    header = '{"kind":"bot","key":"bot0","role":"builder","in_game":true,"feet":[4000.1,3,0]}'
    good = parse_dump("x", [
        header,
        '{"kind":"construct","key":"5","version":3,"hash":"00000000000000aa","pos":[4000.5,1,2]}',
        '{"kind":"terrain","key":"1/0/250/0/0","version":2,"hash":"00000000000000cc"}',
        '{"kind":"terrain","key":"1/0/251/0/0","version":0,"hash":"0000000000000011"}',
    ])
    assert good.name == "bot0" and good.role == "builder" and good.feet == (4000.1, 3.0, 0.0)
    result = compare(server, [good])
    assert result["passed"], result
    assert result["entries"] == {"server": 4, "bot0": 3}

    # A different version, an entry the server does not have, a hole in the zone, two blobs for one version.
    stale = parse_dump("y", [
        header.replace("bot0", "bot1"),
        '{"kind":"construct","key":"5","version":2,"hash":"00000000000000aa"}',
        '{"kind":"terrain","key":"1/0/251/0/0","version":0,"hash":"0000000000000022"}',
        '{"kind":"construct","key":"77","version":1,"hash":"0000000000000001"}',
    ])
    result = compare(server, [good, stale])
    names = {c["name"]: c["passed"] for c in result["checks"]}
    assert names == {"entries_equal_server": False, "builders_cover_zone": False, "same_version_same_blob": False}, result
    assert result["first_difference"].startswith("bot1: construct 5 v2"), result
    assert any("construct 77" in d for d in result["differences"])
    assert "bot1: terrain 1/0/250/0/0 in the zone is missing" in result["differences"], result

    # Far constructs and blocks are outside the zone; an explorer is checked for equality only.
    explorer = parse_dump("z", ['{"kind":"bot","key":"bot5","role":"explorer","in_game":true,"feet":[4000,1500,0]}'])
    assert compare(server, [good, explorer])["passed"]

    # No builder in game: coverage cannot be shown.
    assert not compare(server, [explorer])["passed"]

    for bad in ('{"kind":"terrain","key":"1/0/0/0/0"}', "not json", '{"kind":"other","key":"1","version":1,"hash":"0"}'):
        try:
            parse_dump("bad", [bad])
        except ValueError:
            continue
        raise AssertionError("accepted " + bad)

    assert position("terrain", "1/0/-1/2/3", {}, 1.0) == (-8.0, 40.0, 56.0)

    # Voxels are compared where both sides hold them (the game server and bots in the engine).
    voxel_server = parse_dump("server", ['{"kind":"terrain","key":"1/0/250/0/0","version":2,"hash":"00000000000000cc","voxels":"0000000000000001"}'])
    engine = parse_dump("e", [
        '{"kind":"bot","key":"bot8","role":"builder","in_game":true,"feet":[4000,0,0]}',
        '{"kind":"terrain","key":"1/0/250/0/0","version":2,"hash":"00000000000000cc","voxels":"0000000000000002"}',
    ])
    assert not compare(voxel_server, [engine])["passed"]
    dotnet = parse_dump("d", [header, '{"kind":"terrain","key":"1/0/250/0/0","version":2,"hash":"00000000000000cc"}'])
    assert compare(voxel_server, [dotnet])["passed"]
    print("compare_dumps self-test OK")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--server", type=Path)
    parser.add_argument("bots", nargs="*", type=Path)
    parser.add_argument("--zone-radius", type=float, default=ZONE_RADIUS)
    parser.add_argument("--voxel", type=float, default=1.0, help="voxel size of the planet in metres")
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    if args.server is None or not args.bots:
        parser.error("--server and at least one bot dump are required (or --self-test)")
    result = compare(load_dump(args.server), [load_dump(p) for p in args.bots], args.zone_radius, args.voxel)
    text = json.dumps(result, indent=2)
    if args.out:
        args.out.write_text(text, encoding="utf-8")
    print(text)
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
