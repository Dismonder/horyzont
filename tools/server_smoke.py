#!/usr/bin/env python3
"""Smoke test of the dedicated server (K2-10): start, console commands, restart, signal, damaged world.

Usage:
    python tools/server_smoke.py --godot <editor executable>       # editor, --headless --path game (Windows CI)
    python tools/server_smoke.py --export <exported server binary>  # build of the server-linux / server-windows preset

Steps (real time, one limit for the whole run, default 180 s):
 1. write a default configuration, start the server, wait for "SERVER READY world=<id> state hash=<hash>";
 2. console: "players" -> "0 players", "dev-op" (a journaled change of the world, enabled by --dev) -> "dev-op applied", "save" ->
    "checkpoint committed", "stop" -> exit code 0; the state hash at the stop differs from the initial one; world.db and a startup
    backup exist;
 3. second start with the world id from saves/last-world.txt: same world, same state hash as the first shutdown (so not the initial one);
 4. third start: another "dev-op", then SIGTERM (Linux) or CTRL_BREAK (Windows): exit code 0; a checkpoint written after the signal
    and the state hash at the stop differs from the one at the start;
 5. damaged world.db: exit code 3 with the list of backups; --restore-latest-backup: ready again.
"""

from __future__ import annotations

import argparse
import json
import os
import queue
import re
import shutil
import signal
import socket
import subprocess
import sys
import tempfile
import threading
import time
from pathlib import Path

READY = re.compile(r"SERVER READY world=(\S+) state hash=([0-9A-F]{16})")
STOPPED = re.compile(r"Stopped; state hash=([0-9A-F]{16})")


class SmokeFailure(Exception):
    pass


class Server:
    """One server process with its output collected on a thread."""

    def __init__(self, command: list[str], cwd: Path, deadline: float, label: str) -> None:
        flags = subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0
        self.label = label
        self.deadline = deadline
        self.lines: list[str] = []
        self._queue: queue.Queue[str | None] = queue.Queue()
        self.process = subprocess.Popen(
            command,
            cwd=cwd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=flags,
        )
        threading.Thread(target=self._read, daemon=True).start()
        self._seen = 0
        self._eof = False

    def _read(self) -> None:
        assert self.process.stdout is not None
        for line in self.process.stdout:
            self._queue.put(line.rstrip("\r\n"))
        self._queue.put(None)

    def _drain(self, timeout: float) -> bool:
        try:
            line = self._queue.get(timeout=timeout)
        except queue.Empty:
            return False
        if line is None:
            self._eof = True
            return False
        self.lines.append(line)
        print(f"[{self.label}] {line}")
        return True

    def wait_line(self, pattern: re.Pattern[str] | str, timeout: float) -> re.Match[str]:
        """Waits for a line (after the lines already consumed) that matches the pattern."""
        regex = re.compile(pattern) if isinstance(pattern, str) else pattern
        limit = min(time.monotonic() + timeout, self.deadline)
        while True:
            while self._seen < len(self.lines):
                match = regex.search(self.lines[self._seen])
                self._seen += 1
                if match:
                    return match
            remaining = limit - time.monotonic()
            if remaining <= 0:
                raise SmokeFailure(f"{self.label}: no line matching {regex.pattern!r} in time")
            if not self._drain(min(remaining, 0.5)) and self._eof:
                raise SmokeFailure(f"{self.label}: the process ended (exit {self.process.returncode}) before {regex.pattern!r}")

    def send(self, text: str) -> None:
        assert self.process.stdin is not None
        self.process.stdin.write(text + "\n")
        self.process.stdin.flush()

    def wait_exit(self, timeout: float) -> int:
        limit = min(time.monotonic() + timeout, self.deadline)
        while self.process.poll() is None:
            if time.monotonic() > limit:
                raise SmokeFailure(f"{self.label}: the process did not exit in time")
            self._drain(0.2)
        while self._drain(0.2):
            pass
        return int(self.process.returncode)

    def kill(self) -> None:
        if self.process.poll() is None:
            self.process.kill()
            self.process.wait()


def free_udp_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        probe.bind(("127.0.0.1", 0))
        return int(probe.getsockname()[1])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--godot", type=Path, help="editor executable; runs the project with --headless --path")
    group.add_argument("--export", type=Path, help="exported dedicated server binary")
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parent.parent / "game")
    parser.add_argument("--limit", type=float, default=180.0, help="limit of the whole run in seconds")
    parser.add_argument("--keep", action="store_true", help="keep the temporary directory")
    args = parser.parse_args()

    deadline = time.monotonic() + args.limit
    work = Path(tempfile.mkdtemp(prefix="horyzont-server-smoke-"))
    servers: list[Server] = []

    godot = args.godot
    if godot is not None and godot.name.endswith(".console.exe"):
        # The *.console.exe of a Godot build is a small wrapper that starts the real executable: a console signal ends the wrapper
        # at once and its exit code is not the server's. The real executable gets the signals and returns the exit code itself.
        real = godot.with_name(godot.name.removesuffix(".console.exe") + ".exe")
        if real.is_file():
            godot = real

    def command(*tail: str) -> tuple[list[str], Path]:
        if godot is not None:
            return [str(godot.resolve()), "--headless", "--path", str(args.project.resolve()), "--", *tail], args.project
        binary = args.export.resolve()
        return [str(binary), "--headless", "--", *tail], binary.parent

    def start(label: str, *tail: str) -> Server:
        cmd, cwd = command(*tail)
        server = Server(cmd, cwd, deadline, label)
        servers.append(server)
        return server

    try:
        config = work / "server.json"
        data = work / "data"
        first = start("write-config", "--write-default-config", str(config))
        if first.wait_exit(60) != 0 or not config.is_file():
            raise SmokeFailure("--write-default-config did not write the file")
        settings = json.loads(config.read_text(encoding="utf-8"))
        settings["bindAddress"] = "127.0.0.1"
        settings["port"] = free_udp_port()
        config.write_text(json.dumps(settings, indent=2), encoding="utf-8")

        server_args = ["--server", "--config", str(config), "--data-dir", str(data), "--dev"]

        # 1 and 2
        s1 = start("run1", *server_args)
        ready = s1.wait_line(READY, 120)
        world_id, initial_hash = ready.group(1), ready.group(2)
        s1.send("players")
        s1.wait_line(r"^0 players$", 30)
        s1.send("dev-op")
        s1.wait_line(r"dev-op applied", 30)
        s1.send("save")
        s1.wait_line(r"checkpoint committed", 60)
        s1.send("stop")
        if (code := s1.wait_exit(90)) != 0:
            raise SmokeFailure(f"run1: exit code {code}, expected 0")
        stopped = [m.group(1) for line in s1.lines if (m := STOPPED.search(line))]
        if not stopped:
            raise SmokeFailure("run1: no 'Stopped; state hash=' line")
        hash1 = stopped[-1]
        if hash1 == initial_hash:
            raise SmokeFailure("run1: the state hash did not change after dev-op, the reload check would prove nothing")
        world_dir = data / "saves" / world_id
        if not (world_dir / "world.db").is_file():
            raise SmokeFailure("run1: world.db missing")
        if not list((world_dir / "backups").glob("*-startup.db")):
            raise SmokeFailure("run1: no startup backup")
        last = (data / "saves" / "last-world.txt").read_text(encoding="utf-8").strip()
        if last != world_id:
            raise SmokeFailure(f"last-world.txt holds {last}, expected {world_id}")

        # 3
        settings["worldId"] = last
        config.write_text(json.dumps(settings, indent=2), encoding="utf-8")
        s2 = start("run2", *server_args)
        ready2 = s2.wait_line(READY, 120)
        if ready2.group(1) != world_id or ready2.group(2) != hash1:
            raise SmokeFailure(f"run2: world {ready2.group(1)} hash {ready2.group(2)}, expected {world_id} / {hash1}")
        s2.send("stop")
        if (code := s2.wait_exit(90)) != 0:
            raise SmokeFailure(f"run2: exit code {code}, expected 0")

        # 4
        s3 = start("run3", *server_args)
        ready3 = s3.wait_line(READY, 120)
        s3.send("dev-op")
        s3.wait_line(r"dev-op applied", 30)
        mark = len(s3.lines)  # only what the server prints after this point can come from the shutdown
        if os.name == "nt":
            s3.process.send_signal(signal.CTRL_BREAK_EVENT)
        else:
            s3.process.send_signal(signal.SIGTERM)
        if (code := s3.wait_exit(90)) != 0:
            raise SmokeFailure(f"run3: exit code {code} after the signal, expected 0")
        after = s3.lines[mark:]
        stopped3 = [m.group(1) for line in after if (m := STOPPED.search(line))]
        if not stopped3 or not any("checkpoint committed" in line for line in after):
            raise SmokeFailure("run3: no checkpoint and 'Stopped' line after the signal")
        if stopped3[-1] == ready3.group(2):
            raise SmokeFailure("run3: the state hash at the stop equals the one at the start, the change was not saved")

        # 5
        for name in ("world.db", "world.db-wal", "world.db-shm"):
            path = world_dir / name
            if name == "world.db":
                path.write_bytes(b"this is not a sqlite database" * 64)
            elif path.exists():
                path.unlink()
        s4 = start("run4", *server_args)
        if (code := s4.wait_exit(90)) != 3:
            raise SmokeFailure(f"run4: exit code {code} for a damaged world, expected 3")
        if not any("--restore-latest-backup" in line for line in s4.lines) or not any("startup.db" in line for line in s4.lines):
            raise SmokeFailure("run4: no list of backups and no hint about --restore-latest-backup")
        s5 = start("run5", *server_args, "--restore-latest-backup")
        s5.wait_line(READY, 120)
        s5.send("stop")
        if (code := s5.wait_exit(90)) != 0:
            raise SmokeFailure(f"run5: exit code {code}, expected 0")
        print("SERVER SMOKE OK")
        return 0
    except SmokeFailure as failure:
        print(f"SERVER SMOKE FAILED: {failure}")
        return 1
    finally:
        for server in servers:
            server.kill()
        if not args.keep:
            shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
