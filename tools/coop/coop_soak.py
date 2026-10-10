#!/usr/bin/env python3
"""Co-op soak orchestrator (K4-19): a dedicated server and a crowd of bots, with control breaks, a restart and a kill.

Usage:
    python tools/coop/coop_soak.py --godot <editor> --minutes 3 --bots 8 --engine-bots 0 --out coop-smoke
    python tools/coop/coop_soak.py --godot <editor> --profile tick --minutes 10 --bots 8 --engine-bots 0 --out coop-tick
    python -I tools/coop/coop_soak.py --self-test

What it does (real time, one deadline for the whole run):
 1. writes a default server configuration, picks a free UDP port and starts the dedicated server (--server --dev);
 2. starts the bot runner (tests/Horyzont.BotRunner) against it: N bots over UDP, round trip 100 ms by default;
 3. sends "stats" to the server every minute (the line "STATS tick_ms ..." is parsed);
 4. profile "smoke" (default): graceful restart ("stop", start again) at one third of the run and a kill (TerminateProcess) at two
    thirds; profile "tick": neither, and the server may be pinned to some cores (--affinity);
 5. ends the run, stops runner and server, and checks: bots in game, restart keeps state hash and last op, nothing acknowledged
    before the kill (minus 150 ms) is lost, no operation id twice, tick time (profile tick: mean at most 8 ms).
Writes summary.json and summary.md into --out. Exit code 0 only when every check passed.

The state comparison between the server and the bots (digest dumps) needs the replicas of K3-15/K3-16 and is added by phase B.
"""

from __future__ import annotations

import argparse
import json
import os
import queue
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import threading
import time
from dataclasses import dataclass, field
from pathlib import Path

READY = re.compile(r"SERVER READY world=(\S+) state hash=([0-9A-F]{16})(?: last op=(\d+))?")
STOPPED = re.compile(r"Stopped; state hash=([0-9A-F]{16})(?: last op=(\d+))?")
CHECKPOINT = re.compile(r"Checkpoint (started|committed) boundary=(\d+)")
STATS = re.compile(
    r"STATS tick_ms mean=([\d.]+) p95=([\d.]+) p99=([\d.]+) max=([\d.]+) n=(\d+) players=(\d+) last_op=(\d+) checkpoint=(idle|running)"
)
RUNNER_PROGRESS = re.compile(r"RUNNER in_game=(\d+)/(\d+) frame=(-?\d+) skipped=(\d+)")
DURABILITY_MARGIN_SECONDS = 0.150  # one journal batch (100 ms) plus 50 ms of slack
TICK_BUDGET_MS = 8.0


class Failure(Exception):
    pass


@dataclass
class Line:
    at: float  # time.perf_counter(): QPC on Windows, shared with the runner's TimeProvider.System
    text: str


@dataclass
class Proc:
    """A child process whose output is collected on a thread with a timestamp for every line."""

    label: str
    command: list[str]
    cwd: Path
    lines: list[Line] = field(default_factory=list)
    process: subprocess.Popen[str] | None = None
    _queue: queue.Queue[Line | None] = field(default_factory=queue.Queue)
    _seen: int = 0

    def start(self) -> None:
        flags = subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0
        self.process = subprocess.Popen(
            self.command, cwd=self.cwd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding="utf-8", errors="replace", creationflags=flags,
        )
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self) -> None:
        assert self.process is not None and self.process.stdout is not None
        for text in self.process.stdout:
            self._queue.put(Line(time.perf_counter(), text.rstrip("\r\n")))
        self._queue.put(None)

    def pump(self) -> None:
        while True:
            try:
                item = self._queue.get_nowait()
            except queue.Empty:
                return
            if item is not None:
                self.lines.append(item)
                print(f"[{self.label}] {item.text}", flush=True)

    def new_lines(self) -> list[Line]:
        self.pump()
        fresh = self.lines[self._seen:]
        self._seen = len(self.lines)
        return fresh

    def wait_line(self, regex: re.Pattern[str], timeout: float, deadline: float) -> re.Match[str]:
        limit = min(time.monotonic() + timeout, deadline)
        while True:
            for line in self.new_lines():
                match = regex.search(line.text)
                if match:
                    return match
            if self.process is not None and self.process.poll() is not None:
                self.pump()
                for line in self.new_lines():
                    match = regex.search(line.text)
                    if match:
                        return match
                raise Failure(f"{self.label}: the process ended (exit {self.process.returncode}) before {regex.pattern!r}")
            if time.monotonic() > limit:
                raise Failure(f"{self.label}: no line matching {regex.pattern!r} in time")
            time.sleep(0.05)

    def send(self, text: str) -> None:
        if self.process is None or self.process.poll() is not None or self.process.stdin is None:
            return
        try:
            self.process.stdin.write(text + "\n")
            self.process.stdin.flush()
        except OSError:
            pass  # the process is gone; the exit code tells the rest

    def wait_exit(self, timeout: float) -> int:
        assert self.process is not None
        try:
            self.process.wait(timeout)
        except subprocess.TimeoutExpired as exc:
            raise Failure(f"{self.label}: did not exit in {timeout:.0f} s") from exc
        time.sleep(0.2)
        self.pump()
        return int(self.process.returncode)

    def kill(self) -> float:
        """Terminates the process at once (TerminateProcess on Windows) and returns the moment of the kill."""
        assert self.process is not None
        moment = time.perf_counter()
        if self.process.poll() is None:
            self.process.kill()
            self.process.wait()
        self.pump()
        return moment

    @property
    def alive(self) -> bool:
        return self.process is not None and self.process.poll() is None


# ----- pure helpers (covered by --self-test) -----


def parse_stats(text: str) -> dict[str, float] | None:
    match = STATS.search(text)
    if not match:
        return None
    mean, p95, p99, peak, n, players, last_op, checkpoint = match.groups()
    return {
        "mean": float(mean), "p95": float(p95), "p99": float(p99), "max": float(peak), "n": int(n),
        "players": int(players), "last_op": int(last_op), "checkpoint_running": checkpoint == "running",
    }


def aggregate_ticks(windows: list[dict[str, float]]) -> dict[str, float]:
    """Mean weighted by the number of ticks, the worst p99 and the worst tick."""
    total = sum(w["n"] for w in windows)
    if total == 0:
        return {"ticks": 0, "mean": 0.0, "p99_worst": 0.0, "max": 0.0}
    return {
        "ticks": total,
        "mean": sum(w["mean"] * w["n"] for w in windows) / total,
        "p99_worst": max(w["p99"] for w in windows),
        "max": max(w["max"] for w in windows),
    }


def affinity_cpus(mask: int) -> list[int]:
    return [bit for bit in range(mask.bit_length()) if mask >> bit & 1]


def durability_bound(
    kill_at: float, lines: list[tuple[float, str]], margin: float = DURABILITY_MARGIN_SECONDS
) -> float:
    """
    The moment up to which an acknowledged operation must have survived a kill at kill_at.
    Normally kill_at - margin. When a checkpoint was started and not committed before the kill, the journal of that stretch is
    what the checkpoint was about to cover, so the bound goes back to the start of that checkpoint - margin.
    """
    open_started: float | None = None
    for at, text in lines:
        if at > kill_at:
            break
        match = CHECKPOINT.search(text)
        if not match:
            continue
        if match.group(1) == "started":
            open_started = at
        else:
            open_started = None
    return (open_started if open_started is not None else kill_at) - margin


def check_durability(
    intents: list[tuple[float, int]], kill_at: float, last_op_after: int, lines: list[tuple[float, str]]
) -> tuple[bool, str]:
    """intents: (time of the reply in seconds, applied op id) of accepted intents. Nothing acknowledged before the bound may be lost."""
    bound = durability_bound(kill_at, lines)
    must_survive = [op for at, op in intents if at <= bound]
    lost = [op for op in must_survive if op > last_op_after]
    if lost:
        return False, f"{len(lost)} operations acknowledged before the kill are lost: first {min(lost)}, last op after the restart {last_op_after}"
    newest = max(must_survive, default=0)
    return True, f"{len(must_survive)} acknowledged operations survive (newest {newest}, last op after the restart {last_op_after})"


def duplicate_ops(ops: list[int]) -> list[int]:
    seen: set[int] = set()
    duplicates: list[int] = []
    for op in ops:
        if op in seen:
            duplicates.append(op)
        seen.add(op)
    return duplicates


def split_duplicates(
    intents: list[tuple[float, int]], kill_at: float | None, last_op_after: int | None
) -> tuple[list[int], int]:
    """
    Duplicated op ids, and the number of operations that a kill may legitimately have lost.
    After a kill the restarted server reuses the op ids above the last op it recovered, so an operation acknowledged before the
    kill with an id above that last op was lost (allowed within the durability margin) and is not a duplicate of its reuse.
    """
    if kill_at is None or last_op_after is None:
        return duplicate_ops([op for _at, op in sorted(intents)]), 0
    kept: list[int] = []
    lost = 0
    for at, op in sorted(intents):
        if at <= kill_at and op > last_op_after:
            lost += 1
        else:
            kept.append(op)
    return duplicate_ops(kept), lost


def accepted_intents(report: dict) -> list[tuple[float, int]]:
    """(reply time in seconds on the clock of perf_counter, op id) of every accepted intent of a bots.json."""
    frequency = float(report["frequency"])
    result: list[tuple[float, int]] = []
    for bot in report["bots"]:
        for _seq, _kind, _sent, replied, reason, op in bot["intents"]:
            if reason == "None" and op:
                result.append((replied / frequency, int(op)))
    return result


def merge_reports(reports: list[dict]) -> dict:
    """Several bots.json files of one run (the runner may have been restarted) as one."""
    if not reports:
        return {"frequency": 1, "bots": []}
    merged = dict(reports[0])
    merged["bots"] = [bot for report in reports for bot in report["bots"]]
    return merged


def rejection_totals(report: dict) -> dict[str, int]:
    totals: dict[str, int] = {}
    for bot in report["bots"]:
        for reason, count in bot["rejections"].items():
            totals[reason] = totals.get(reason, 0) + int(count)
    return totals


@dataclass
class Check:
    name: str
    passed: bool
    detail: str


def render_markdown(summary: dict) -> str:
    rows = "\n".join(f"| {c['name']} | {'PASS' if c['passed'] else 'FAIL'} | {c['detail']} |" for c in summary["checks"])
    ticks = summary.get("tick", {})
    return (
        f"# Co-op soak: {summary['verdict']}\n\n"
        f"- minutes: {summary['minutes']}, bots: {summary['bots']}, profile: {summary['profile']}\n"
        f"- tick mean {ticks.get('mean', 0):.3f} ms, worst p99 {ticks.get('p99_worst', 0):.3f} ms, max {ticks.get('max', 0):.3f} ms over {ticks.get('ticks', 0)} ticks\n"
        f"- rejections: {summary.get('rejections', {})}\n\n"
        f"| check | result | detail |\n| --- | --- | --- |\n{rows}\n"
    )


def self_test() -> int:
    stats = parse_stats("STATS tick_ms mean=1.250 p95=2.000 p99=3.500 max=7.000 n=3600 players=8 last_op=1234 checkpoint=idle")
    assert stats == {"mean": 1.25, "p95": 2.0, "p99": 3.5, "max": 7.0, "n": 3600, "players": 8, "last_op": 1234, "checkpoint_running": False}
    assert parse_stats("nothing here") is None
    running = parse_stats("STATS tick_ms mean=0.000 p95=0.000 p99=0.000 max=0.000 n=0 players=0 last_op=0 checkpoint=running")
    assert running is not None and running["checkpoint_running"]

    ticks = aggregate_ticks([
        {"mean": 2.0, "p99": 4.0, "max": 9.0, "n": 100}, {"mean": 4.0, "p99": 6.0, "max": 8.0, "n": 300},
    ])
    assert abs(ticks["mean"] - 3.5) < 1e-9 and ticks["p99_worst"] == 6.0 and ticks["max"] == 9.0 and ticks["ticks"] == 400
    assert aggregate_ticks([])["ticks"] == 0
    assert affinity_cpus(0x3F) == [0, 1, 2, 3, 4, 5] and affinity_cpus(0b1010) == [1, 3]

    # Boundary of the durability: normally the kill minus 150 ms ...
    assert abs(durability_bound(100.0, []) - 99.85) < 1e-9
    # ... after a checkpoint that finished, the same ...
    done = [(50.0, "Checkpoint started boundary=10"), (50.4, "Checkpoint committed boundary=10")]
    assert abs(durability_bound(100.0, done) - 99.85) < 1e-9
    # ... and with a checkpoint in flight at the kill, back to its start minus 150 ms.
    open_checkpoint = done + [(99.0, "Checkpoint started boundary=40")]
    assert abs(durability_bound(100.0, open_checkpoint) - 98.85) < 1e-9
    # Lines after the kill do not count.
    assert abs(durability_bound(100.0, [(101.0, "Checkpoint started boundary=1")]) - 99.85) < 1e-9

    intents = [(10.0, 1), (20.0, 2), (99.9, 3), (100.5, 4)]
    ok, _ = check_durability(intents, 100.0, last_op_after=2, lines=[])
    assert ok, "ops 3 and 4 were acknowledged inside the last 150 ms or after the kill and may be lost"
    ok, detail = check_durability(intents, 100.0, last_op_after=1, lines=[])
    assert not ok and "first 2" in detail
    ok, _ = check_durability(intents, 100.0, last_op_after=1, lines=[(19.0, "Checkpoint started boundary=1")])
    assert ok, "with a checkpoint open since 19 s only the operations before 18.85 s must survive"
    assert duplicate_ops([1, 2, 3, 2, 3]) == [2, 3] and duplicate_ops([1, 2, 3]) == []
    # A kill: ops 3 and 4 were acked before it but not recovered (last op 2); the restart hands out 3 and 4 again.
    after_kill = [(10.0, 1), (20.0, 2), (99.9, 3), (99.95, 4), (101.0, 3), (102.0, 4), (103.0, 5)]
    assert split_duplicates(after_kill, 100.0, 2) == ([], 2)
    assert split_duplicates(after_kill, None, None)[0] == [3, 4]
    assert split_duplicates(after_kill + [(104.0, 5)], 100.0, 2) == ([5], 2), "a real duplicate after the restart still counts"
    assert split_duplicates([(10.0, 1), (11.0, 1)], 100.0, 5) == ([1], 0)

    report = {
        "frequency": 10_000_000,
        "bots": [
            {"intents": [[1, "Dig", 0, 5_000_000, "None", 7], [2, "Dig", 0, 6_000_000, "NoHit", 0]], "rejections": {"NoHit": 1}},
            {"intents": [[1, "Fill", 0, 7_000_000, "None", 8]], "rejections": {"NoHit": 2, "RateLimited": 4}},
        ],
    }
    assert accepted_intents(report) == [(0.5, 7), (0.7, 8)]
    assert rejection_totals(report) == {"NoHit": 3, "RateLimited": 4}
    assert len(merge_reports([report, report])["bots"]) == 4

    summary = {
        "verdict": "PASS", "minutes": 3, "bots": 8, "profile": "smoke", "tick": ticks, "rejections": {},
        "checks": [{"name": "x", "passed": True, "detail": "d"}],
    }
    assert "| x | PASS | d |" in render_markdown(summary)
    ready = READY.search("SERVER READY world=abc state hash=0123456789ABCDEF last op=42")
    stopped = STOPPED.search("Stopped; state hash=0123456789ABCDEF last op=9")
    progress = RUNNER_PROGRESS.search("RUNNER in_game=8/8 frame=100 skipped=0 accepted=1")
    assert ready is not None and ready.group(3) == "42"
    assert stopped is not None and stopped.group(2) == "9"
    assert progress is not None and progress.group(1) == "8"

    assert clean_exit(0, False) and clean_exit(3221225477, True) and not clean_exit(3221225477, False)
    assert not clean_exit(3, True) and not clean_exit(-1, True)
    assert is_access_violation(3221225477) and not is_access_violation(0)
    assert plan_events("tick", None, None, 600.0) == (None, None) and plan_events("tick", 1.0, 2.0, 600.0) == (None, None)
    assert plan_events("smoke", None, None, 600.0) == (200.0, 400.0) and plan_events("smoke", 0, 0, 600.0) == (None, None)
    assert plan_events("smoke", None, None, 120.0) == (None, None)
    assert av_budget_check(0)[0] and av_budget_check(1)[0] and not av_budget_check(2)[0]
    server_lines = [Line(1.0, "x"), Line(2.0, "Stopped; state hash=0123456789ABCDEF last op=9")]
    assert stopped_in(server_lines) and not stopped_in([Line(1.0, "Stopped")])
    print("coop_soak self-test OK")
    return 0


# ----- the run -----


ACCESS_VIOLATION = 0xC0000005
ENGINE_EXIT_WARNING = "Godot crashed at exit after a complete shutdown (access violation); state is safe, engine shutdown defect to follow up"


ENGINE_EXIT_AV_LIMIT = 1  # per run; documented exception in the K4-19 report


def is_access_violation(code: int) -> bool:
    return (code & 0xFFFFFFFF) == ACCESS_VIOLATION


def clean_exit(code: int, stopped: bool) -> bool:
    """A zero exit, or the access violation that Godot raises while tearing down, but only after this same process wrote 'Stopped'."""
    return code == 0 or (is_access_violation(code) and stopped)


def stopped_in(lines: list[Line]) -> list[re.Match[str]]:
    """The 'Stopped' lines of one process (never those of an earlier one)."""
    return [m for line in lines if (m := STOPPED.search(line.text))]


def event_time(value: float | None, total: float, fraction: float) -> float | None:
    if value is not None:
        return value * 60 if value > 0 else None
    return total * fraction if total >= 150 else None


def plan_events(profile: str, restart_arg: float | None, kill_arg: float | None, total: float) -> tuple[float | None, float | None]:
    """(restart_at, kill_at) in seconds. Profile tick measures the steady state: neither, whatever the arguments say."""
    if profile == "tick":
        return None, None
    return event_time(restart_arg, total, 1 / 3), event_time(kill_arg, total, 2 / 3)


def av_budget_check(count: int) -> tuple[bool, str]:
    return count <= ENGINE_EXIT_AV_LIMIT, f"{count} engine exits with an access violation after Stopped (limit {ENGINE_EXIT_AV_LIMIT} per run)"


def free_udp_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        probe.bind(("127.0.0.1", 0))
        return int(probe.getsockname()[1])


def set_affinity(proc: Proc, mask: int) -> None:
    assert proc.process is not None
    if os.name == "nt":
        import ctypes

        handle = proc.process._handle  # type: ignore[attr-defined]
        if not ctypes.windll.kernel32.SetProcessAffinityMask(int(handle), ctypes.c_size_t(mask)):
            print(f"warning: SetProcessAffinityMask({mask:#x}) failed")
    else:
        os.sched_setaffinity(proc.process.pid, affinity_cpus(mask))


class Run:
    def __init__(self, args: argparse.Namespace) -> None:
        self.args = args
        self.repo = Path(__file__).resolve().parent.parent.parent
        self.deadline = time.monotonic() + args.limit_minutes * 60
        self.work = Path(tempfile.mkdtemp(prefix="horyzont-coop-"))
        self.out = Path(args.out).resolve()
        self.out.mkdir(parents=True, exist_ok=True)
        self.procs: list[Proc] = []
        self.checks: list[Check] = []
        self.tick_windows: list[dict[str, float]] = []
        self.server: Proc | None = None
        self.runner: Proc | None = None
        self.runner_reports: list[Path] = []
        self.port = 0
        self.settings: dict = {}
        self.server_lines: list[tuple[float, str]] = []  # every line of every server process, with its time
        self.kill_at: float | None = None
        self.last_op_after_kill: int | None = None
        self.runner_generation = 0
        self.engine_exit_av = 0

    # --- commands ---

    def godot_command(self, *tail: str) -> tuple[list[str], Path]:
        godot: Path | None = self.args.godot
        if godot is not None and godot.name.endswith(".console.exe"):
            real = godot.with_name(godot.name.removesuffix(".console.exe") + ".exe")
            if real.is_file():
                godot = real
        if godot is not None:
            project = self.args.project.resolve()
            return [str(godot.resolve()), "--headless", "--path", str(project), "--", *tail], project
        binary = self.args.export.resolve()
        return [str(binary), "--headless", "--", *tail], binary.parent

    def start_proc(self, label: str, command: list[str], cwd: Path) -> Proc:
        proc = Proc(label, command, cwd)
        proc.start()
        self.procs.append(proc)
        return proc

    def start_server(self, label: str) -> Proc:
        command, cwd = self.godot_command("--server", "--config", str(self.work / "server.json"), "--data-dir", str(self.work / "data"), "--dev")
        proc = self.start_proc(label, command, cwd)
        if self.args.profile == "tick" and self.args.affinity:
            set_affinity(proc, self.args.affinity)
        return proc

    def runner_command(self, epoch_ms: int, generation: int) -> tuple[list[str], Path]:
        out = self.out / f"runner{generation}"
        out.mkdir(parents=True, exist_ok=True)
        self.runner_reports.append(out / "bots.json")
        dll = self.repo / "tests" / "Horyzont.BotRunner" / "bin" / "Release" / "net10.0" / "Horyzont.BotRunner.dll"
        base = [
            "dotnet", str(dll), "--connect", f"127.0.0.1:{self.port}", "--bots", str(self.args.bots), "--seed", str(self.args.seed),
            "--epoch", str(epoch_ms), "--latency-ms", str(self.args.latency_ms), "--jitter-ms", str(self.args.jitter_ms),
            "--out", str(out),
        ]
        return base, self.repo

    def build_runner(self) -> None:
        print("building the bot runner ...", flush=True)
        result = subprocess.run(
            ["dotnet", "build", str(self.repo / "tests" / "Horyzont.BotRunner"), "-c", "Release", "--nologo", "-v", "q"],
            cwd=self.repo, check=False,
        )
        if result.returncode != 0:
            raise Failure("dotnet build of the bot runner failed")

    # --- phases ---

    def collect_server_lines(self) -> None:
        if self.server is None:
            return
        for line in self.server.new_lines():
            self.server_lines.append((line.at, line.text))

    def stats_now(self) -> dict[str, float] | None:
        """Asks the server for the window since the previous stats and waits for its line."""
        assert self.server is not None
        self.collect_server_lines()
        mark = len(self.server_lines)
        self.server.send("stats")
        limit = time.monotonic() + 10
        while time.monotonic() < limit:
            time.sleep(0.05)
            self.collect_server_lines()
            for _at, text in self.server_lines[mark:]:
                parsed = parse_stats(text)
                if parsed is not None:
                    self.tick_windows.append(parsed)
                    return parsed
        return None

    def start_server_waiting(self, label: str, retry_same_port: bool) -> tuple[Proc, re.Match[str]]:
        """Starts the server and waits for READY; a port that the OS has not released yet is retried a few times."""
        last: Failure | None = None
        for attempt in range(6):
            proc = self.start_server(f"{label}.{attempt}" if attempt else label)
            self.server = proc
            try:
                ready = proc.wait_line(READY, 120, self.deadline)
                return proc, ready
            except Failure as failure:
                last = failure
                proc.kill()
                if not retry_same_port:
                    break
                time.sleep(2.0)
        raise Failure(f"{label}: the server did not start: {last}")

    def run(self) -> int:
        args = self.args
        try:
            if args.engine_bots != 0:
                raise Failure("--engine-bots > 0 needs the engine bots of phase C; use --engine-bots 0")
            if args.bots + args.engine_bots > 8:
                raise Failure("the server holds 8 players: --bots plus --engine-bots must be at most 8")
            if args.build_runner:
                self.build_runner()

            command, cwd = self.godot_command("--write-default-config", str(self.work / "server.json"))
            writer = self.start_proc("write-config", command, cwd)
            if writer.wait_exit(90) != 0:
                raise Failure("--write-default-config failed")
            self.settings = json.loads((self.work / "server.json").read_text(encoding="utf-8"))
            self.port = free_udp_port()
            self.settings.update({"bindAddress": "127.0.0.1", "port": self.port})
            (self.work / "server.json").write_text(json.dumps(self.settings, indent=2), encoding="utf-8")

            _server, ready = self.start_server_waiting("server", retry_same_port=False)
            world_id = ready.group(1)
            # Later starts (restart, after the kill) must open this world and not create a new one.
            self.settings["worldId"] = world_id
            (self.work / "server.json").write_text(json.dumps(self.settings, indent=2), encoding="utf-8")
            self.checks.append(Check("server_started", True, f"world {world_id}, port {self.port}"))
            self.collect_server_lines()

            epoch_ms = int(time.time() * 1000) + 15_000
            self.start_runner(epoch_ms)
            run_start = time.monotonic()
            total = args.minutes * 60.0
            restart_at, kill_at = plan_events(args.profile, args.restart_at, args.kill_at, total)
            next_stats = run_start + 60
            restarted = killed = False
            while time.monotonic() - run_start < total:
                elapsed = time.monotonic() - run_start
                self.collect_server_lines()
                if self.runner is not None:
                    self.runner.new_lines()
                    if not self.runner.alive:
                        raise Failure(f"the bot runner ended early with exit {self.runner.process.returncode}")  # type: ignore[union-attr]
                if self.server is not None and not self.server.alive:
                    raise Failure(f"the server ended early with exit {self.server.process.returncode}")  # type: ignore[union-attr]
                if time.monotonic() >= next_stats:
                    next_stats += 60
                    self.stats_now()
                if restart_at is not None and not restarted and elapsed >= restart_at:
                    restarted = True
                    self.graceful_restart()
                if kill_at is not None and not killed and elapsed >= kill_at:
                    killed = True
                    self.kill_and_restart()
                time.sleep(0.2)

            self.finish()
            return self.report()
        except Failure as failure:
            self.checks.append(Check("run", False, str(failure)))
            print(f"COOP SOAK FAILED: {failure}", flush=True)
            try:
                self.finish(quiet=True)
            except Failure:
                pass
            return self.report()
        finally:
            for proc in self.procs:
                try:
                    proc.kill()
                except OSError as error:
                    print(f"cleanup: {proc.label}: {error}", flush=True)
            if not self.args.keep:
                shutil.rmtree(self.work, ignore_errors=True)

    def start_runner(self, epoch_ms: int) -> None:
        self.runner_generation += 1
        command, cwd = self.runner_command(epoch_ms, self.runner_generation)
        self.runner = self.start_proc(f"runner{self.runner_generation}", command, cwd)
        self.epoch_ms = epoch_ms
        self.runner.wait_line(re.compile(r"RUNNER READY"), 60, self.deadline)

    def graceful_restart(self) -> None:
        assert self.server is not None
        self.stats_now()
        self.server.send("stop")
        code = self.server.wait_exit(120)
        self.collect_server_lines()
        stopped = stopped_in(self.server.lines)
        if not clean_exit(code, bool(stopped)) or not stopped:
            self.checks.append(Check("graceful_stop", False, f"exit {code}, stopped lines {len(stopped)}"))
            raise Failure("the graceful stop failed")
        if code != 0:
            self.engine_exit_av += 1
            print(f"warning: {ENGINE_EXIT_WARNING} (exit {code:#x}) after the stop line", flush=True)
        hash_stop, last_op_stop = stopped[-1].group(1), stopped[-1].group(2)
        self.server, ready = self.start_server_waiting("server-restart", retry_same_port=True)
        same = ready.group(2) == hash_stop and (last_op_stop is None or ready.group(3) == last_op_stop)
        self.checks.append(Check(
            "restart_preserves_state", same,
            f"stopped hash {hash_stop} last op {last_op_stop}; started hash {ready.group(2)} last op {ready.group(3)}",
        ))

    def kill_and_restart(self) -> None:
        assert self.server is not None
        self.stats_now()
        self.collect_server_lines()
        self.kill_at = self.server.kill()
        self.collect_server_lines()
        self.server, ready = self.start_server_waiting("server-after-kill", retry_same_port=True)
        self.collect_server_lines()
        self.last_op_after_kill = int(ready.group(3)) if ready.group(3) is not None else None
        detail = f"server killed; restarted at last op {self.last_op_after_kill}"
        self.checks.append(Check("server_restarts_after_kill", self.last_op_after_kill is not None, detail))

    def finish(self, quiet: bool = False) -> None:
        if self.server is not None and self.server.alive and not quiet:
            self.stats_now()
        if self.runner is not None and self.runner.alive:
            self.runner.send("stop")
            try:
                self.runner.wait_exit(60)
            except Failure:
                self.runner.kill()
        if self.server is not None and self.server.alive:
            self.server.send("stop")
            try:
                code = self.server.wait_exit(120)
                stopped = bool(stopped_in(self.server.lines))
                if code != 0 and clean_exit(code, stopped):
                    self.engine_exit_av += 1
                self.checks.append(Check("server_exits_clean", clean_exit(code, stopped), f"exit {code:#x}" + ("; " + ENGINE_EXIT_WARNING if code else "")))
            except Failure as failure:
                self.checks.append(Check("server_exits_clean", False, str(failure)))
        self.collect_server_lines()

    def load_reports(self) -> dict:
        reports = []
        for path in self.runner_reports:
            if path.is_file():
                reports.append(json.loads(path.read_text(encoding="utf-8")))
        return merge_reports(reports)

    def report(self) -> int:
        args = self.args
        report = self.load_reports()
        if report["bots"]:
            last_progress = None
            for proc in reversed(self.procs):
                if proc.label.startswith("runner"):
                    for line in reversed(proc.lines):
                        last_progress = RUNNER_PROGRESS.search(line.text)
                        if last_progress:
                            break
                    if last_progress:
                        break
            in_game = [bot["in_game"] for bot in report["bots"]]
            self.checks.append(Check("bots_reached_the_game", all(bot["joins"] >= 1 for bot in report["bots"]), f"{sum(bot['joins'] for bot in report['bots'])} joins"))
            accepted = accepted_intents(report)
            self.checks.append(Check("edits_accepted", len(accepted) > 0, f"{len(accepted)} accepted intents"))
            dups, allowed_losses = split_duplicates(accepted, self.kill_at, self.last_op_after_kill)
            self.checks.append(Check(
                "no_operation_acknowledged_twice", not dups,
                (f"{len(dups)} duplicated op ids" if dups else "every op id once") + f"; {allowed_losses} acknowledged ops lost to the kill and not counted",
            ))
            if self.kill_at is not None and self.last_op_after_kill is not None:
                ok, detail = check_durability(accepted, self.kill_at, self.last_op_after_kill, self.server_lines)
                self.checks.append(Check("kill_loses_at_most_the_last_batch", ok, detail))
            if last_progress is not None:
                self.checks.append(Check("bots_in_game_at_end", int(last_progress.group(1)) == int(last_progress.group(2)), last_progress.group(0)))
            del in_game
        else:
            self.checks.append(Check("runner_report", False, "no bots.json was written"))
        ticks = aggregate_ticks(self.tick_windows)
        if args.profile == "tick":
            self.checks.append(Check("tick_mean_within_budget", 0 < ticks["ticks"] and ticks["mean"] <= TICK_BUDGET_MS, f"mean {ticks['mean']:.3f} ms (budget {TICK_BUDGET_MS} ms)"))
        av_ok, av_detail = av_budget_check(self.engine_exit_av)
        self.checks.append(Check("engine_exit_av_within_limit", av_ok, av_detail))
        passed = all(c.passed for c in self.checks)
        summary = {
            "engine_exit_av": self.engine_exit_av,
            "verdict": "PASS" if passed else "FAIL", "minutes": args.minutes, "bots": args.bots, "profile": args.profile,
            "port": self.port, "tick": ticks, "rejections": rejection_totals(report), "checks": [c.__dict__ for c in self.checks],
        }
        (self.out / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
        (self.out / "summary.md").write_text(render_markdown(summary), encoding="utf-8")
        print(f"COOP SOAK {summary['verdict']}", flush=True)
        for c in self.checks:
            print(f"  {'PASS' if c.passed else 'FAIL'} {c.name}: {c.detail}", flush=True)
        return 0 if passed else 1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--self-test", action="store_true")
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--godot", type=Path, help="editor executable; runs the project with --headless --path")
    group.add_argument("--export", type=Path, help="exported dedicated server binary")
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parent.parent.parent / "game")
    parser.add_argument("--minutes", type=float, default=3.0)
    parser.add_argument("--bots", type=int, default=8, help="bots of the .NET runner")
    parser.add_argument("--engine-bots", type=int, default=0, help="bots in the engine (phase C); must be 0 for now")
    parser.add_argument("--profile", choices=("smoke", "tick"), default="smoke")
    parser.add_argument("--affinity", type=lambda text: int(text, 0), default=0x3F, help="core mask of the server in profile tick (6 cores by default)")
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--latency-ms", type=int, default=50, help="one way; the round trip is twice that")
    parser.add_argument("--jitter-ms", type=int, default=10)
    parser.add_argument("--restart-at", type=float, default=None, help="minute of the graceful restart (0 = never); default a third of the run when it lasts 2.5 minutes or more")
    parser.add_argument("--kill-at", type=float, default=None, help="minute of the kill (0 = never); default two thirds of the run")
    parser.add_argument("--out", default="coop-out")
    parser.add_argument("--limit-minutes", type=float, default=None, help="deadline of the whole run; default --minutes plus 15")
    parser.add_argument("--no-build-runner", dest="build_runner", action="store_false")
    parser.add_argument("--keep", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    if args.godot is None and args.export is None:
        parser.error("one of --godot or --export is required (or --self-test)")
    if args.limit_minutes is None:
        args.limit_minutes = args.minutes + 15
    if args.profile == "tick":
        args.restart_at = args.kill_at = 0
    return Run(args).run()


if __name__ == "__main__":
    sys.exit(main())
