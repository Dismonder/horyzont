#!/usr/bin/env python3
"""Compare generator reports of Horyzont (K2-08): do platforms and runtimes generate the same terrain blocks?

Usage:
    compare_reports.py <a.json> <b.json> [...] --out <report.md> [--raw <a.bin> <b.bin>]
    compare_reports.py --self-test

Reports come from `--generator-report` of the game (game/src/Planet/Diagnostics/GeneratorReport.cs). The input is treated as
untrusted (it is a CI artifact): schema, types, hash format, block count and file size are checked before anything is compared.

Exit codes: 0 = LOD 0 identical, 1 = LOD 0 differs (collisions would disagree), 2 = invalid input or arguments.
Differences at LOD > 0 are reported but do not change the exit code (visual only).

Standard library only (Python 3.12).
"""

from __future__ import annotations

import argparse
import io
import json
import re
import struct
import sys
import tempfile
from contextlib import redirect_stderr, redirect_stdout
from dataclasses import dataclass
from pathlib import Path

SCHEMA = 1
MAX_BLOCKS = 100_000
MAX_FILE_BYTES = 50 * 1024 * 1024
MAX_DIFFS_SHOWN = 20
BLOCK_BYTES_SDF = 16 * 16 * 16 * 2
BLOCK_BYTES_MATERIAL = 16 * 16 * 16
RAW_HEADER = struct.Struct("<4i")
HASH_RE = re.compile(r"^[0-9a-f]{16}$")
HEX_RE = re.compile(r"^[0-9a-f]{1,16}$")
MAX_TEXT = 200
MAX_LOD = 15
COORD_LIMIT = 1 << 24


class ReportError(Exception):
    """The input is not a valid report."""


@dataclass(frozen=True)
class Block:
    lod: int
    x: int
    y: int
    z: int
    sdf: str
    mat: str

    @property
    def key(self) -> tuple[int, int, int, int]:
        return (self.lod, self.x, self.y, self.z)


@dataclass(frozen=True)
class Report:
    name: str
    profile: str
    seed: str
    radius: float
    platform: str
    runtime: str
    engine: str
    voxel: str
    commit: str
    median_us: str
    blocks: dict[tuple[int, int, int, int], Block]

    @property
    def label(self) -> str:
        return f"{self.platform}-{self.runtime}"


def _text(data: dict, field: str, pattern: re.Pattern[str] | None = None) -> str:
    value = data.get(field)
    if not isinstance(value, str) or len(value) > MAX_TEXT:
        raise ReportError(
            f"field '{field}' must be a string of at most {MAX_TEXT} characters"
        )
    if pattern is not None and not pattern.match(value):
        raise ReportError(f"field '{field}' has a wrong format")
    return value


def _int(value: object, field: str, low: int, high: int) -> int:
    if (
        isinstance(value, bool)
        or not isinstance(value, int)
        or not low <= value <= high
    ):
        raise ReportError(f"field '{field}' must be an integer in [{low}, {high}]")
    return value


def parse_report(text: str, name: str) -> Report:
    """Validate and parse the JSON text of a report."""
    try:
        data = json.loads(text)
    except (json.JSONDecodeError, RecursionError) as error:
        raise ReportError(f"{name}: not valid JSON ({error})") from error
    try:
        if not isinstance(data, dict):
            raise ReportError("the top level must be an object")
        if data.get("schema") != SCHEMA or isinstance(data.get("schema"), bool):
            raise ReportError(f"unsupported schema (expected {SCHEMA})")
        radius = data.get("radius")
        if (
            isinstance(radius, bool)
            or not isinstance(radius, int | float)
            or not 0 < radius < 1e7
        ):
            raise ReportError("field 'radius' must be a positive number")
        raw_blocks = data.get("blocks")
        if not isinstance(raw_blocks, list):
            raise ReportError("field 'blocks' must be an array")
        if not raw_blocks:
            raise ReportError("field 'blocks' must not be empty")
        if len(raw_blocks) > MAX_BLOCKS:
            raise ReportError(f"too many blocks ({len(raw_blocks)} > {MAX_BLOCKS})")
        blocks: dict[tuple[int, int, int, int], Block] = {}
        for index, item in enumerate(raw_blocks):
            if not isinstance(item, dict):
                raise ReportError(f"block {index} must be an object")
            try:
                block = Block(
                    lod=_int(item.get("lod"), "lod", 0, MAX_LOD),
                    x=_int(item.get("x"), "x", -COORD_LIMIT, COORD_LIMIT),
                    y=_int(item.get("y"), "y", -COORD_LIMIT, COORD_LIMIT),
                    z=_int(item.get("z"), "z", -COORD_LIMIT, COORD_LIMIT),
                    sdf=_text(item, "sdf", HASH_RE),
                    mat=_text(item, "mat", HASH_RE),
                )
            except ReportError as error:
                raise ReportError(f"block {index}: {error}") from error
            if block.key in blocks:
                raise ReportError(f"block {index}: duplicate key {block.key}")
            blocks[block.key] = block
        if not any(key[0] == 0 for key in blocks):
            raise ReportError("report must contain LOD 0 samples")
        return Report(
            name=name,
            profile=_text(data, "profile"),
            seed=_text(data, "seed", HEX_RE),
            radius=float(radius),
            platform=_text(data, "platform"),
            runtime=_text(data, "runtime"),
            engine=_text(data, "engine"),
            voxel=_text(data, "voxel"),
            commit=_text(data, "commit"),
            median_us=_text(data, "median_us"),
            blocks=blocks,
        )
    except ReportError as error:
        raise ReportError(f"{name}: {error}") from error


def load_report(path: Path) -> Report:
    """Read a report file (size limited before reading)."""
    try:
        size = path.stat().st_size
    except OSError as error:
        raise ReportError(f"{path}: cannot read ({error})") from error
    if size > MAX_FILE_BYTES:
        raise ReportError(f"{path}: file is larger than {MAX_FILE_BYTES} bytes")
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError) as error:
        raise ReportError(f"{path}: cannot read ({error})") from error
    return parse_report(text, path.name)


@dataclass
class Difference:
    key: tuple[int, int, int, int]
    channel: str
    left: str
    right: str


@dataclass
class Comparison:
    reference: Report
    other: Report
    lod0_total: int
    lod0_equal: int
    far_total: int
    far_equal: int
    differences: list[Difference]

    @property
    def lod0_differs(self) -> bool:
        return self.lod0_equal != self.lod0_total


def compare(reference: Report, other: Report) -> Comparison:
    """Compare two parsed reports; they must describe the same planet and block set."""
    for field in ("profile", "seed", "radius"):
        if getattr(reference, field) != getattr(other, field):
            raise ReportError(
                f"{other.name}: {field} differs from {reference.name} ({getattr(other, field)} != {getattr(reference, field)})"
            )
    if set(reference.blocks) != set(other.blocks):
        missing = len(set(reference.blocks) ^ set(other.blocks))
        raise ReportError(
            f"{other.name}: the block sets differ from {reference.name} ({missing} keys not in both)"
        )
    lod0_total = lod0_equal = far_total = far_equal = 0
    differences: list[Difference] = []
    for key in sorted(reference.blocks):
        a, b = reference.blocks[key], other.blocks[key]
        same = a.sdf == b.sdf and a.mat == b.mat
        if key[0] == 0:
            lod0_total += 1
            lod0_equal += same
        else:
            far_total += 1
            far_equal += same
        if not same:
            if a.sdf != b.sdf:
                differences.append(Difference(key, "sdf", a.sdf, b.sdf))
            if a.mat != b.mat:
                differences.append(Difference(key, "mat", a.mat, b.mat))
    return Comparison(
        reference, other, lod0_total, lod0_equal, far_total, far_equal, differences
    )


def format_markdown(
    reports: list[Report],
    comparisons: list[Comparison],
    raw_lod0_differs: bool = False,
    raw_far_differs: bool = False,
) -> str:
    """Render the result as a Markdown report."""
    first = reports[0]
    if raw_lod0_differs or any(c.lod0_differs for c in comparisons):
        overall = "LOD 0 DIFFERS"
    elif raw_far_differs or any(c.differences for c in comparisons):
        overall = "LOD > 0 differs (visual only)"
    else:
        overall = "IDENTICAL"
    lines = [
        "# Generator consistency report",
        "",
        f"**Overall result: {overall}.**",
        "",
        f"Profile `{first.profile}`, galaxy seed `{first.seed}`, radius {first.radius:g} m, {len(first.blocks)} blocks.",
        "",
        "## Runtimes",
        "",
        "| Runtime | Engine | Voxel Tools | Commit | Median block time (us) |",
        "| --- | --- | --- | --- | --- |",
    ]
    for report in reports:
        lines.append(
            f"| {report.label} | {report.engine} | {report.voxel} | `{report.commit}` | {report.median_us} |"
        )
    lines += [
        "",
        "## Comparison with " + first.label,
        "",
        "| Runtime | LOD 0 equal | LOD > 0 equal | JSON result |",
        "| --- | --- | --- | --- |",
    ]
    for c in comparisons:
        result = (
            "IDENTICAL"
            if not c.differences
            else (
                "LOD 0 DIFFERS" if c.lod0_differs else "LOD > 0 differs (visual only)"
            )
        )
        lines.append(
            f"| {c.other.label} | {c.lod0_equal}/{c.lod0_total} | {c.far_equal}/{c.far_total} | {result} |"
        )
    for c in comparisons:
        if not c.differences:
            continue
        lines += [
            "",
            f"### First differences: {c.reference.label} vs {c.other.label}",
            "",
            "| lod | x | y | z | channel | left | right |",
            "| --- | --- | --- | --- | --- | --- | --- |",
        ]
        for d in c.differences[:MAX_DIFFS_SHOWN]:
            lod, x, y, z = d.key
            lines.append(
                f"| {lod} | {x} | {y} | {z} | {d.channel} | `{d.left}` | `{d.right}` |"
            )
        if len(c.differences) > MAX_DIFFS_SHOWN:
            lines.append("")
            lines.append(f"... and {len(c.differences) - MAX_DIFFS_SHOWN} more.")
    lines.append("")
    return "\n".join(lines)


# --------------------------------------------------------------------------- raw dumps


def read_raw(path: Path) -> dict[tuple[int, int, int, int], tuple[bytes, bytes]]:
    """Read a raw block dump (`--dump-keys`): int32 lod, x, y, z, SDF bytes, material bytes per block."""
    try:
        size = path.stat().st_size
        if size > MAX_FILE_BYTES:
            raise ReportError(f"{path}: file is larger than {MAX_FILE_BYTES} bytes")
        data = path.read_bytes()
    except OSError as error:
        raise ReportError(f"{path}: cannot read ({error})") from error
    record = RAW_HEADER.size + BLOCK_BYTES_SDF + BLOCK_BYTES_MATERIAL
    if len(data) % record != 0:
        raise ReportError(f"{path}: size is not a whole number of blocks")
    blocks: dict[tuple[int, int, int, int], tuple[bytes, bytes]] = {}
    for offset in range(0, len(data), record):
        key = RAW_HEADER.unpack_from(data, offset)
        if key in blocks:
            raise ReportError(f"{path}: duplicate raw key {key}")
        start = offset + RAW_HEADER.size
        blocks[key] = (
            data[start : start + BLOCK_BYTES_SDF],
            data[start + BLOCK_BYTES_SDF : offset + record],
        )
    return blocks


def compare_raw(a: dict, b: dict) -> list[str]:
    """Per block: voxels with a sign change of the SDF, maximum SDF difference (raw 16 bit units), material differences."""
    if not a or not b:
        raise ReportError("raw block sets must not be empty")
    if set(a) != set(b):
        raise ReportError("raw block sets differ (incomplete key sets)")
    lines = [
        "",
        "## Raw block comparison",
        "",
        "| lod | x | y | z | sign changes | max SDF diff | material diffs |",
        "| --- | --- | --- | --- | --- | --- | --- |",
    ]
    for key in sorted(a):
        sdf_a = struct.unpack(f"<{BLOCK_BYTES_SDF // 2}h", a[key][0])
        sdf_b = struct.unpack(f"<{BLOCK_BYTES_SDF // 2}h", b[key][0])
        sign_changes = sum(
            1 for p, q in zip(sdf_a, sdf_b, strict=True) if (p < 0) != (q < 0)
        )
        max_diff = max(
            (abs(p - q) for p, q in zip(sdf_a, sdf_b, strict=True)), default=0
        )
        material = sum(1 for p, q in zip(a[key][1], b[key][1], strict=True) if p != q)
        lines.append(
            f"| {key[0]} | {key[1]} | {key[2]} | {key[3]} | {sign_changes} | {max_diff} | {material} |"
        )
    lines.append("")
    return lines


# --------------------------------------------------------------------------- self test


def _sample(
    platform: str,
    sdf: str = "00000000000000aa",
    extra: list[dict] | None = None,
    schema: int = 1,
) -> str:
    blocks = [
        {"lod": 0, "x": 1, "y": 2, "z": 3, "sdf": sdf, "mat": "00000000000000bb"},
        {
            "lod": 2,
            "x": 0,
            "y": 0,
            "z": 0,
            "sdf": "00000000000000cc",
            "mat": "00000000000000dd",
        },
    ] + (extra or [])
    return json.dumps(
        {
            "schema": schema,
            "profile": "core:planet/1",
            "seed": "ff",
            "radius": 4000,
            "platform": platform,
            "runtime": "editor",
            "engine": "4.7.2",
            "voxel": "1.7.0",
            "commit": "abc",
            "median_us": "1.0",
            "blocks": blocks,
        }
    )


def self_test() -> int:
    """Check the parser and the comparison on built-in cases (agreeing, differing, corrupt)."""
    failures: list[str] = []

    def check(name: str, condition: bool) -> None:
        if not condition:
            failures.append(name)

    a = parse_report(_sample("windows"), "a")
    b = parse_report(_sample("linux"), "b")
    same = compare(a, b)
    check(
        "identical reports compare equal",
        not same.differences and not same.lod0_differs and same.lod0_equal == 1,
    )

    far_only = compare(
        a,
        parse_report(
            _sample("linux", extra=None).replace(
                "00000000000000cc", "00000000000000ce"
            ),
            "c",
        ),
    )
    check(
        "a difference at LOD > 0 is not a LOD 0 difference",
        bool(far_only.differences) and not far_only.lod0_differs,
    )

    different = compare(a, parse_report(_sample("linux", sdf="00000000000000ab"), "d"))
    check(
        "a difference at LOD 0 is detected",
        different.lod0_differs and different.differences[0].channel == "sdf",
    )
    check(
        "markdown mentions LOD 0 DIFFERS",
        "LOD 0 DIFFERS"
        in format_markdown(
            [a, parse_report(_sample("linux", sdf="00000000000000ab"), "d")],
            [different],
        ),
    )

    corrupt_cases = {
        "empty blocks": json.dumps(dict(json.loads(_sample("windows")), blocks=[])),
        "no LOD 0 samples": _sample("windows").replace('"lod": 0', '"lod": 1', 1),
        "not json": "{",
        "wrong schema": _sample("windows", schema=2),
        "not an object": "[]",
        "bad hash": _sample("windows", sdf="XYZ"),
        "uppercase hash": _sample("windows", sdf="00000000000000AA"),
        "bad type": _sample("windows").replace('"lod": 0', '"lod": "0"', 1),
        "negative lod": _sample("windows").replace('"lod": 0', '"lod": -1', 1),
        "duplicate key": _sample(
            "windows",
            extra=[
                {
                    "lod": 0,
                    "x": 1,
                    "y": 2,
                    "z": 3,
                    "sdf": "00000000000000aa",
                    "mat": "00000000000000bb",
                }
            ],
        ),
        "missing blocks": json.dumps({"schema": 1}),
        "too many blocks": json.dumps(
            {
                "schema": 1,
                "profile": "p",
                "seed": "1",
                "radius": 1,
                "platform": "w",
                "runtime": "e",
                "engine": "e",
                "voxel": "v",
                "commit": "c",
                "median_us": "1",
                "blocks": [
                    {"lod": 0, "x": i, "y": 0, "z": 0, "sdf": "0" * 16, "mat": "0" * 16}
                    for i in range(MAX_BLOCKS + 1)
                ],
            }
        ),
    }
    for name, text in corrupt_cases.items():
        try:
            parse_report(text, name)
            failures.append(f"corrupt input accepted: {name}")
        except ReportError:
            pass

    other_seed = parse_report(
        _sample("linux").replace('"seed": "ff"', '"seed": "fe"'), "e"
    )
    try:
        compare(a, other_seed)
        failures.append("different seeds accepted")
    except ReportError:
        pass

    with tempfile.TemporaryDirectory() as directory:
        big = Path(directory) / "big.json"
        with big.open("wb") as handle:
            handle.truncate(MAX_FILE_BYTES + 1)
        try:
            load_report(big)
            failures.append("oversized file accepted")
        except ReportError:
            pass

    record_a = (
        RAW_HEADER.pack(0, 1, 2, 3)
        + bytes(BLOCK_BYTES_SDF)
        + bytes(BLOCK_BYTES_MATERIAL)
    )
    changed_sdf = bytearray(BLOCK_BYTES_SDF)
    changed_sdf[0:2] = struct.pack("<h", -5)
    record_b = (
        RAW_HEADER.pack(0, 1, 2, 3)
        + bytes(changed_sdf)
        + b"\x01"
        + bytes(BLOCK_BYTES_MATERIAL - 1)
    )
    with tempfile.TemporaryDirectory() as directory:
        raw_a, raw_b = Path(directory) / "a.bin", Path(directory) / "b.bin"
        raw_a.write_bytes(record_a)
        raw_b.write_bytes(record_b)
        table = "\n".join(compare_raw(read_raw(raw_a), read_raw(raw_b)))
        check(
            "raw comparison counts sign change, max diff and material diff",
            "| 0 | 1 | 2 | 3 | 1 | 5 | 1 |" in table,
        )
        report_a, report_b = Path(directory) / "a.json", Path(directory) / "b.json"
        report_a.write_text(_sample("windows"), encoding="utf-8")
        report_b.write_text(_sample("linux"), encoding="utf-8")
        output_path = Path(directory) / "comparison.md"
        # Targeted dumps may include blocks outside the JSON sample manifest.
        extra_key = RAW_HEADER.pack(0, 4, 5, 6)
        extra = extra_key + bytes(BLOCK_BYTES_SDF + BLOCK_BYTES_MATERIAL)
        sdf_only = extra_key + struct.pack("<h", 5) + extra[RAW_HEADER.size + 2 :]
        material_only = (
            extra[: RAW_HEADER.size + BLOCK_BYTES_SDF]
            + b"\x01"
            + bytes(BLOCK_BYTES_MATERIAL - 1)
        )
        far = RAW_HEADER.pack(2, 4, 5, 6) + extra[RAW_HEADER.size :]
        far_changed = (
            far[: RAW_HEADER.size + BLOCK_BYTES_SDF]
            + material_only[RAW_HEADER.size + BLOCK_BYTES_SDF :]
        )
        cases = [
            ("identical raw blocks", extra, extra, 0, "IDENTICAL"),
            ("raw LOD 0 SDF magnitude mismatch", extra, sdf_only, 1, "LOD 0 DIFFERS"),
            ("raw LOD 0 material mismatch", extra, material_only, 1, "LOD 0 DIFFERS"),
            ("raw LOD 0 sign mismatch", record_a, record_b, 1, "LOD 0 DIFFERS"),
            (
                "raw far difference is visual only",
                far,
                far_changed,
                0,
                "LOD > 0 differs",
            ),
            ("raw key missing on right", extra + record_a, extra, 2, ""),
            ("raw key missing on left", extra, extra + record_a, 2, ""),
            ("empty raw dumps", b"", b"", 2, ""),
            ("duplicate raw keys", extra + extra, extra, 2, ""),
            ("truncated raw record", extra, extra[:-1], 2, ""),
        ]
        for name, left, right, expected_exit, expected_result in cases:
            raw_a.write_bytes(left)
            raw_b.write_bytes(right)
            output = io.StringIO()
            with redirect_stdout(output), redirect_stderr(io.StringIO()):
                exit_code = main(
                    [
                        str(report_a),
                        str(report_b),
                        "--raw",
                        str(raw_a),
                        str(raw_b),
                        "--out",
                        str(output_path),
                    ]
                )
            check(f"{name}: exit {expected_exit}", exit_code == expected_exit)
            if expected_exit != 2:
                expected = f"**Overall result: {expected_result}"
                check(f"{name}: overall result", expected in output.getvalue())
                check(
                    f"{name}: saved overall result",
                    expected in output_path.read_text(encoding="utf-8"),
                )

    if failures:
        for failure in failures:
            print(f"SELF-TEST FAILED: {failure}", file=sys.stderr)
        return 1
    print("SELF-TEST OK")
    return 0


# --------------------------------------------------------------------------- main


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Compare Horyzont generator reports.")
    parser.add_argument(
        "reports",
        nargs="*",
        type=Path,
        help="two or more generator-*.json files (the first is the reference)",
    )
    parser.add_argument("--out", type=Path, help="write the Markdown report here")
    parser.add_argument(
        "--raw",
        nargs=2,
        type=Path,
        metavar=("A.BIN", "B.BIN"),
        help="also compare two raw dumps (--dump-keys)",
    )
    parser.add_argument(
        "--self-test", action="store_true", help="check the parser on built-in cases"
    )
    args = parser.parse_args(argv)

    if args.self_test:
        return self_test()
    if len(args.reports) < 1:
        parser.error("give at least one report (two to compare)")
    try:
        reports = [load_report(path) for path in args.reports]
        comparisons = [compare(reports[0], other) for other in reports[1:]]
        raw_lines: list[str] = []
        raw_lod0_differs = raw_far_differs = False
        if args.raw:
            raw_a, raw_b = read_raw(args.raw[0]), read_raw(args.raw[1])
            raw_lines = compare_raw(raw_a, raw_b)
            raw_lod0_differs = any(
                key[0] == 0 and raw_a[key] != raw_b[key] for key in raw_a
            )
            raw_far_differs = any(
                key[0] > 0 and raw_a[key] != raw_b[key] for key in raw_a
            )
        text = format_markdown(reports, comparisons, raw_lod0_differs, raw_far_differs)
        text += "\n".join(raw_lines)
    except ReportError as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 2
    if args.out:
        args.out.write_text(text, encoding="utf-8")
    print(text)
    return 1 if raw_lod0_differs or any(c.lod0_differs for c in comparisons) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
