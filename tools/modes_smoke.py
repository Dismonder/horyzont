#!/usr/bin/env python3
"""Smoke test of the process modes (K4-19, criterion 2): solo, host + client, dedicated server + client.

Usage:
    python tools/modes_smoke.py --godot <editor executable>
    python tools/modes_smoke.py --export <exported game binary>
    python tools/modes_smoke.py --self-test

Each scenario runs headless in real time and waits for the line "CLIENT IN GAME player=<n>" that the client prints after the server's
welcome (see ClientNode.Announce); it also fails when the output holds an unhandled exception or a script error.
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
import tempfile
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from server_smoke import READY, Server, SmokeFailure, free_udp_port

IN_GAME = re.compile(r"CLIENT IN GAME player=(\d+)")
BAD_LINE = re.compile(r"Unhandled exception|SCRIPT ERROR|CONNECTION REFUSED|START FAILED", re.IGNORECASE)


def find_bad_lines(lines: list[str]) -> list[str]:
    return [line for line in lines if BAD_LINE.search(line)]


def self_test() -> int:
    match = IN_GAME.search("CLIENT IN GAME player=3")
    assert match is not None and match.group(1) == "3"
    assert IN_GAME.search("CLIENT IN GAMES") is None
    assert find_bad_lines(["BOOT OK", "CONNECTION REFUSED: Full x"]) == ["CONNECTION REFUSED: Full x"]
    assert find_bad_lines(["Unhandled exception. System.Foo", "fine"]) == ["Unhandled exception. System.Foo"]
    assert find_bad_lines(["SERVER READY world=a state hash=0123456789ABCDEF last op=0"]) == []
    ready = READY.search("SERVER READY world=a state hash=0123456789ABCDEF last op=0")
    assert ready is not None and ready.group(1) == "a"
    print("modes_smoke self-test OK")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--godot", type=Path, help="editor executable; runs the project with --headless --path")
    group.add_argument("--export", type=Path, help="exported game binary")
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parent.parent / "game")
    parser.add_argument("--limit", type=float, default=240.0, help="limit of the whole run in seconds")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--keep", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    if args.godot is None and args.export is None:
        parser.error("one of --godot or --export is required (or --self-test)")

    deadline = time.monotonic() + args.limit
    work = Path(tempfile.mkdtemp(prefix="horyzont-modes-smoke-"))
    procs: list[Server] = []
    godot = args.godot
    if godot is not None and godot.name.endswith(".console.exe"):
        real = godot.with_name(godot.name.removesuffix(".console.exe") + ".exe")
        if real.is_file():
            godot = real

    def start(label: str, *tail: str) -> Server:
        if godot is not None:
            cmd = [str(godot.resolve()), "--headless", "--path", str(args.project.resolve()), "--", *tail]
            cwd = args.project
        else:
            binary = args.export.resolve()
            cmd, cwd = [str(binary), "--headless", "--", *tail], binary.parent
        proc = Server(cmd, cwd, deadline, label)
        procs.append(proc)
        return proc

    def stop(proc: Server) -> None:
        proc.kill()

    results: list[tuple[str, bool, str]] = []

    def scenario(name: str, body) -> None:
        started = time.monotonic()
        try:
            body()
            bad = [line for p in procs for line in find_bad_lines(p.lines)]
            if bad:
                raise SmokeFailure(f"bad output: {bad[0]}")
            results.append((name, True, f"{time.monotonic() - started:.0f} s"))
        except SmokeFailure as failure:
            results.append((name, False, str(failure)))
        finally:
            for proc in procs:
                stop(proc)
            procs.clear()

    def solo() -> None:
        proc = start("solo", "--solo", "--data-dir", str(work / "solo"))
        proc.wait_line(IN_GAME, 120)

    def host_client() -> None:
        port = free_udp_port()
        host = start("host", "--host", "--port", str(port), "--bind", "127.0.0.1", "--data-dir", str(work / "host"), "--profile", str(work / "host-profile.json"))
        host.wait_line(IN_GAME, 120)
        client = start("client", "--connect", f"127.0.0.1:{port}", "--dev", "--profile", str(work / "client-profile.json"))
        client.wait_line(IN_GAME, 120)

    def server_client() -> None:
        config = work / "server.json"
        writer = start("write-config", "--write-default-config", str(config))
        if writer.wait_exit(60) != 0 or not config.is_file():
            raise SmokeFailure("--write-default-config did not write the file")
        settings = json.loads(config.read_text(encoding="utf-8"))
        port = free_udp_port()
        settings.update({"bindAddress": "127.0.0.1", "port": port})
        config.write_text(json.dumps(settings, indent=2), encoding="utf-8")
        server = start("server", "--server", "--config", str(config), "--data-dir", str(work / "server"), "--dev")
        server.wait_line(READY, 120)
        client = start("client", "--connect", f"127.0.0.1:{port}", "--dev", "--profile", str(work / "client-profile.json"))
        client.wait_line(IN_GAME, 120)

    try:
        scenario("solo", solo)
        scenario("host + client", host_client)
        scenario("dedicated server + client", server_client)
    finally:
        for proc in procs:
            stop(proc)
        if not args.keep:
            shutil.rmtree(work, ignore_errors=True)

    for name, passed, detail in results:
        print(f"{'PASS' if passed else 'FAIL'} {name}: {detail}")
    return 0 if all(passed for _name, passed, _detail in results) else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except SmokeFailure as failure:
        print(f"FAIL: {failure}")
        sys.exit(1)
