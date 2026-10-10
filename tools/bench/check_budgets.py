#!/usr/bin/env python3
"""Compares benchmark results with tests/Horyzont.Benchmarks/budgets.json (K4-19).

Usage:
    python tools/bench/check_budgets.py --bench-dir <BenchmarkDotNet.Artifacts/results> [--baseline docs/reports/K4-19/bench-baseline.json]
        [--engine-report engine-bench.json] [--coop-summary coop-tick/summary.json] [--write-baseline out.json] [--summary summary.md]
    python tools/bench/check_budgets.py --self-test

Allocation budgets, the engine frame limit and the server tick mean are hard (exit code 1). A time regression above
regressionWarnPercent against the baseline only prints a GitHub ::warning:: line (P9: shared runners are noisy).
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

DEFAULT_BUDGETS = Path(__file__).resolve().parent.parent.parent / "tests" / "Horyzont.Benchmarks" / "budgets.json"


def load_results(bench_dir: Path) -> dict[str, dict[str, float]]:
    """Reads every BenchmarkDotNet *-report-full*.json; keys are 'Type.Method' plus the parameters when there are any."""
    results: dict[str, dict[str, float]] = {}
    for path in sorted(bench_dir.glob("*-report-full*.json")):
        results.update(parse_report(json.loads(path.read_text(encoding="utf-8"))))
    return results


def parse_report(report: dict) -> dict[str, dict[str, float]]:
    out: dict[str, dict[str, float]] = {}
    for bench in report.get("Benchmarks", []):
        name = f"{bench['Type']}.{bench['Method']}"
        parameters = bench.get("Parameters") or ""
        if parameters:
            name += f"({parameters})"
        out[name] = {
            "meanNs": float(bench["Statistics"]["Mean"]),
            "allocBytes": float(bench["Memory"]["BytesAllocatedPerOperation"]),
        }
    return out


def check(
    budgets: dict,
    results: dict[str, dict[str, float]],
    baseline: dict[str, dict[str, float]] | None,
    engine: dict | None,
    coop: dict | None,
) -> tuple[list[str], list[str], list[str]]:
    """Returns (errors, warnings, info lines)."""
    errors: list[str] = []
    warnings: list[str] = []
    info: list[str] = []
    for rule in budgets.get("benchmarks", []):
        matching = {k: v for k, v in results.items() if k == rule["name"] or k.startswith(rule["name"] + "(")}
        if not matching:
            errors.append(f"no result for {rule['name']}")
            continue
        for key, value in matching.items():
            info.append(f"{key}: {value['meanNs'] / 1000:.2f} us, {value['allocBytes']:.0f} B")
            if "maxAllocBytes" in rule and value["allocBytes"] > rule["maxAllocBytes"]:
                errors.append(f"{key}: allocates {value['allocBytes']:.0f} B, budget {rule['maxAllocBytes']} B")
    limit = budgets.get("regressionWarnPercent", 25)
    if baseline:
        for key, value in results.items():
            old = baseline.get(key)
            if old and old["meanNs"] > 0 and value["meanNs"] > old["meanNs"] * (1 + limit / 100):
                change = (value["meanNs"] / old["meanNs"] - 1) * 100
                warnings.append(f"{key}: {change:.0f}% slower than the baseline ({old['meanNs'] / 1000:.2f} us -> {value['meanNs'] / 1000:.2f} us)")
    if engine is not None:
        max_frame = float(engine.get("maxFrameMs", 0))
        hit = float(engine.get("rayHitRatio", 0))
        info.append(f"engine: max frame {max_frame:.1f} ms, ray hit ratio {hit:.4f}")
        if max_frame > budgets["engine"]["maxFrameMs"]:
            errors.append(f"engine: max frame {max_frame:.1f} ms exceeds {budgets['engine']['maxFrameMs']} ms")
        if hit < budgets["engine"]["minRayHitRatio"]:
            errors.append(f"engine: ray hit ratio {hit:.4f} below {budgets['engine']['minRayHitRatio']}")
    if coop is not None:
        mean = float(coop.get("tick", {}).get("mean", float("nan")))
        info.append(f"coop tick mean {mean:.3f} ms")
        if not mean <= budgets["coopTick"]["maxMeanMs"]:
            errors.append(f"coop tick mean {mean:.3f} ms exceeds {budgets['coopTick']['maxMeanMs']} ms")
    return errors, warnings, info


def self_test() -> int:
    budgets = {
        "regressionWarnPercent": 25,
        "benchmarks": [{"name": "A.Zero", "maxAllocBytes": 0}, {"name": "B.Param", "maxAllocBytes": 10}],
        "engine": {"maxFrameMs": 50, "minRayHitRatio": 1.0},
        "coopTick": {"maxMeanMs": 8.0},
    }
    results = {"A.Zero": {"meanNs": 100.0, "allocBytes": 0.0}, "B.Param(X=1)": {"meanNs": 100.0, "allocBytes": 10.0}}
    errors, warnings, _ = check(budgets, results, None, {"maxFrameMs": 49.9, "rayHitRatio": 1.0}, {"tick": {"mean": 7.9}})
    assert errors == [] and warnings == [], (errors, warnings)

    bad = {"A.Zero": {"meanNs": 100.0, "allocBytes": 8.0}, "B.Param(X=1)": {"meanNs": 100.0, "allocBytes": 11.0}}
    errors, _, _ = check(budgets, bad, None, None, None)
    assert len(errors) == 2, errors

    errors, _, _ = check(budgets, {"A.Zero": results["A.Zero"]}, None, None, None)
    assert errors == ["no result for B.Param"], errors

    errors, _, _ = check(budgets, results, None, {"maxFrameMs": 50.1, "rayHitRatio": 0.99}, {"tick": {"mean": 8.5}})
    assert len(errors) == 3, errors
    errors, _, _ = check(budgets, results, None, None, {"tick": {}})
    assert len(errors) == 1, "a missing tick mean must not pass"

    baseline = {"A.Zero": {"meanNs": 70.0, "allocBytes": 0.0}, "B.Param(X=1)": {"meanNs": 90.0, "allocBytes": 10.0}}
    errors, warnings, _ = check(budgets, results, baseline, None, None)
    assert errors == [] and len(warnings) == 1 and warnings[0].startswith("A.Zero"), warnings

    report = {"Benchmarks": [{"Type": "T", "Method": "M", "Parameters": "P=1", "Statistics": {"Mean": 5.0}, "Memory": {"BytesAllocatedPerOperation": 3}}]}
    assert parse_report(report) == {"T.M(P=1)": {"meanNs": 5.0, "allocBytes": 3.0}}
    print("check_budgets self-test OK")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--bench-dir", type=Path)
    parser.add_argument("--budgets", type=Path, default=DEFAULT_BUDGETS)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--engine-report", type=Path)
    parser.add_argument("--coop-summary", type=Path)
    parser.add_argument("--write-baseline", type=Path)
    parser.add_argument("--summary", type=Path, help="Markdown file to append to (for $GITHUB_STEP_SUMMARY)")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    if args.bench_dir is None:
        parser.error("--bench-dir is required (or --self-test)")

    budgets = json.loads(args.budgets.read_text(encoding="utf-8"))
    results = load_results(args.bench_dir)
    baseline = json.loads(args.baseline.read_text(encoding="utf-8")) if args.baseline and args.baseline.is_file() else None
    engine = json.loads(args.engine_report.read_text(encoding="utf-8")) if args.engine_report and args.engine_report.is_file() else None
    coop = json.loads(args.coop_summary.read_text(encoding="utf-8")) if args.coop_summary and args.coop_summary.is_file() else None
    if engine is not None:
        for lod, row in sorted(engine.get("generation", {}).items(), key=lambda item: int(item[0])):
            print(f"engine generation LOD {lod}: {row['blocksPerSecond']} blocks/s, mean {row['meanMs']} ms")
        engine = engine.get("orbitToSurface")  # absent in the headless report: the flight needs a rendered scene
    errors, warnings, info = check(budgets, results, baseline, engine, coop)

    if args.write_baseline:
        args.write_baseline.write_text(json.dumps(results, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    lines = ["## Benchmark budgets", "", *[f"- {line}" for line in info]]
    lines += [f"- WARNING {w}" for w in warnings] + [f"- ERROR {e}" for e in errors]
    text = "\n".join(lines) + "\n"
    print(text)
    for w in warnings:
        print(f"::warning::{w}")
    for e in errors:
        print(f"::error::{e}")
    if args.summary:
        with args.summary.open("a", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
