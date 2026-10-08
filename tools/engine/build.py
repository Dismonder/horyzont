#!/usr/bin/env python3
"""Build Godot with Voxel Tools, double precision and C# (.NET) for Horyzont.

The only inputs are engine/engine.json and this file; their SHA-256 gives the
output key, so engine/out/<key>/ is rebuilt only when one of them changes.
See docs/plans/K1-01-pipeline-silnika.md.

Standard library only (SCons is run as a separate process).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform as host_platform
import shutil
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ENGINE = REPO / "engine"
CONFIG_PATH = ENGINE / "engine.json"
SRC = ENGINE / "src"
GODOT_SRC = SRC / "godot"
VOXEL_SRC = GODOT_SRC / "modules" / "voxel"
STAMP_PATH = SRC / "STAMP.json"
OUT_ROOT = ENGINE / "out"
NUGET_LOCAL = ENGINE / "nuget-local"
ACTIVE_PATH = NUGET_LOCAL / "ACTIVE.json"
NUGET_PACKAGES = REPO / ".nuget" / "packages"
SMOKE_DIR = ENGINE / "smoke"
SMOKE_PROJECT = SMOKE_DIR / "Smoke.csproj"
# Outside engine/src so that --clean keeps the compilation cache (/cache/ is git-ignored).
DEFAULT_SCONS_CACHE = REPO / "cache" / "scons"

# Commit the official Voxel Tools 1.7 release was built from (branch 4.7, 4.7.2.stable).
VOXEL_RELEASE_GODOT_COMMIT = "ed1daf0bf"

PACKAGE_IDS = ("Godot.NET.Sdk", "GodotSharp", "GodotSharpEditor", "Godot.SourceGenerators")
TEST_VARIANTS = {"single": "test-single"}
SMOKE_TIMEOUT_S = 120
STEPS = ("fetch", "editor", "glue", "assemblies", "activate", "templates", "smoke", "all")


class BuildError(Exception):
    """A step failed; the message is shown to the user and the exit code is 1."""


# --------------------------------------------------------------------------- config


def load_config() -> dict:
    with CONFIG_PATH.open(encoding="utf-8") as f:
        return json.load(f)


def config_key() -> str:
    digest = hashlib.sha256()
    digest.update(CONFIG_PATH.read_bytes())
    digest.update(Path(__file__).resolve().read_bytes())
    return digest.hexdigest()[:12]


def host_runner() -> str:
    system = host_platform.system()
    if system == "Windows":
        return "windows"
    if system == "Linux":
        return "linux"
    raise BuildError(f"Unsupported host system: {system}")


def find_target(config: dict, name: str) -> dict:
    for target in config["targets"]:
        if target["name"] == name:
            return target
    names = ", ".join(t["name"] for t in config["targets"])
    raise BuildError(f"Unknown target '{name}'. Targets in engine.json: {names}")


def binary_name(target: dict, console: bool = False) -> str:
    name = f"godot.{target['platform']}.{target['target']}.double.x86_64.mono"
    if target["platform"] == "windows":
        name += ".console.exe" if console else ".exe"
    return name


def editor_target(config: dict) -> dict:
    for target in config["targets"]:
        if target["target"] == "editor":
            return target
    raise BuildError("engine.json has no editor target.")


def scons_flags(config: dict, target: dict, args: argparse.Namespace) -> list[str]:
    build = config["build"]
    if build.get("precision") != "double" or not build.get("dotnet"):
        raise BuildError("engine.json must request precision=double and dotnet=true.")
    flags = [
        f"platform={target['platform']}",
        f"target={target['target']}",
        "precision=double",
        "module_mono_enabled=yes",
    ]
    flags += build["sconsFlags"].get("all", [])
    flags += build["sconsFlags"].get(target["target"], [])
    flags += [f"cache_path={args.scons_cache}", f"cache_limit={args.cache_limit}"]
    return flags


# --------------------------------------------------------------------------- context


class Context:
    def __init__(self, args: argparse.Namespace) -> None:
        self.args = args
        self.config = load_config()
        self.key = config_key()
        self.out = OUT_ROOT / self.key
        self.logs = self.out / "logs"
        self.dry_run: bool = args.dry_run

    # ---- process helpers

    def run(self, step: str, cmd: list[str | Path], cwd: Path, timeout: float | None = None) -> str:
        """Run a command, tee its output to the console and logs/<step>.log, return the output."""
        printable = " ".join(str(c) for c in cmd)
        print(f"[{step}] (cwd {cwd}) {printable}", flush=True)
        if self.dry_run:
            return ""
        self.logs.mkdir(parents=True, exist_ok=True)
        log_path = self.logs / f"{step}.log"
        captured: list[str] = []
        with log_path.open("a", encoding="utf-8") as log:
            log.write(f"\n=== {time.strftime('%Y-%m-%d %H:%M:%S')} {printable}\n")
            proc = subprocess.Popen(
                [str(c) for c in cmd],
                cwd=cwd,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                encoding="utf-8",
                errors="replace",
            )
            assert proc.stdout is not None
            deadline = None if timeout is None else time.monotonic() + timeout
            try:
                for line in proc.stdout:
                    sys.stdout.write(line)
                    log.write(line)
                    captured.append(line)
                    if deadline is not None and time.monotonic() > deadline:
                        raise subprocess.TimeoutExpired(cmd, timeout or 0)
                proc.wait(timeout=None if deadline is None else max(1.0, deadline - time.monotonic()))
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()
                log.write(f"=== killed after {timeout} s\n")
                raise BuildError(f"[{step}] timed out after {timeout} s: {printable}") from None
            log.write(f"=== exit code {proc.returncode}\n")
        if proc.returncode != 0:
            raise BuildError(f"[{step}] exit code {proc.returncode}: {printable} (log: {log_path})")
        return "".join(captured)

    def scons(self, step: str, target: dict) -> None:
        if not self.dry_run:
            # SCons creates the cache directory atomically next to it, so the parent must exist.
            Path(self.args.scons_cache).parent.mkdir(parents=True, exist_ok=True)
        cmd: list[str | Path] = [sys.executable, "-m", "SCons", f"-j{self.args.jobs}"] + scons_flags(self.config, target, self.args)
        self.run(step, cmd, GODOT_SRC)

    # ---- manifest

    def update_manifest(self, step: str, extra: dict | None = None) -> None:
        if self.dry_run:
            return
        self.out.mkdir(parents=True, exist_ok=True)
        path = self.out / "manifest.json"
        manifest = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
        previous = {f["path"]: f for f in manifest.get("files", [])}
        stamp = json.loads(STAMP_PATH.read_text(encoding="utf-8")) if STAMP_PATH.exists() else {}
        manifest["key"] = self.key
        manifest["godot"] = stamp.get("godot", {})
        manifest["voxel"] = stamp.get("voxel", {})
        manifest["sconsFlags"] = {
            t["name"]: scons_flags(self.config, t, self.args) for t in self.config["targets"]
        }
        steps = manifest.setdefault("steps", {})
        entry = {"finished": time.strftime("%Y-%m-%dT%H:%M:%S")}
        entry.update(extra or {})
        steps[step] = entry
        manifest["files"] = [describe_file(p, self.out, previous) for p in manifest_files(self.out)]
        path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


def manifest_files(out: Path) -> list[Path]:
    files = [p for p in out.iterdir() if p.is_file() and p.name != "manifest.json"]
    for nupkgs in [out / "nupkgs", *(out / v / "nupkgs" for v in TEST_VARIANTS.values())]:
        if nupkgs.is_dir():
            files += [p for p in nupkgs.iterdir() if p.suffix == ".nupkg"]
    return sorted(files)


def describe_file(path: Path, out: Path, previous: dict) -> dict:
    rel = path.relative_to(out).as_posix()
    stat = path.stat()
    old = previous.get(rel)
    if old and old.get("size") == stat.st_size and old.get("mtime") == stat.st_mtime_ns:
        sha = old["sha256"]
    else:
        sha = sha256_file(path)
    return {"path": rel, "size": stat.st_size, "mtime": stat.st_mtime_ns, "sha256": sha}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


# --------------------------------------------------------------------------- steps


def git_head(repo: Path) -> str:
    result = subprocess.run(
        ["git", "rev-parse", "HEAD"], cwd=repo, capture_output=True, text=True, check=True
    )
    return result.stdout.strip()


def step_fetch(ctx: Context) -> None:
    godot = ctx.config["godot"]
    voxel = ctx.config["voxel"]
    stamp = json.loads(STAMP_PATH.read_text(encoding="utf-8")) if STAMP_PATH.exists() else None
    up_to_date = (
        stamp is not None
        and stamp.get("godot", {}).get("ref") == godot["ref"]
        and stamp.get("voxel", {}).get("ref") == voxel["ref"]
        and (GODOT_SRC / "SConstruct").exists()
        and (VOXEL_SRC / "SCsub").exists()
    )
    if up_to_date and stamp is not None:
        print(f"[fetch] sources up to date (godot {stamp['godot']['commit'][:10]}, voxel {stamp['voxel']['commit'][:10]})")
    else:
        if GODOT_SRC.exists() and not ctx.dry_run:
            print(f"[fetch] removing stale {GODOT_SRC}")
            remove_tree(GODOT_SRC)
        ctx.run("fetch", ["git", "clone", "--depth", "1", "--branch", godot["ref"], godot["repository"], GODOT_SRC], REPO)
        ctx.run("fetch", ["git", "clone", "--depth", "1", "--branch", voxel["ref"], voxel["repository"], VOXEL_SRC], REPO)
        if not ctx.dry_run:
            stamp = {
                "godot": {"ref": godot["ref"], "commit": git_head(GODOT_SRC)},
                "voxel": {"ref": voxel["ref"], "commit": git_head(VOXEL_SRC)},
            }
            STAMP_PATH.write_text(json.dumps(stamp, indent=2) + "\n", encoding="utf-8")

    if not ctx.dry_run:
        # MSBuild barriers: Godot's C# projects must not pick up the solution's Directory.*.props.
        (SRC / "Directory.Packages.props").write_text(
            "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>"
            "</PropertyGroup></Project>\n",
            encoding="utf-8",
        )
        (SRC / "Directory.Build.props").write_text("<Project />\n", encoding="utf-8")
        assert stamp is not None
        commit = stamp["godot"]["commit"]
        if commit.startswith(VOXEL_RELEASE_GODOT_COMMIT):
            print(f"[fetch] Godot {godot['ref']} is {commit[:10]}, same as the Voxel Tools release build")
        else:
            print(
                f"[fetch] NOTE: Godot {godot['ref']} is {commit[:10]}, the Voxel Tools release was built "
                f"from {VOXEL_RELEASE_GODOT_COMMIT}; keep the tag if the module compiles and record this in the PR"
            )
        ctx.update_manifest("fetch")


def copy_binary(ctx: Context, target: dict) -> None:
    for console in (False, True):
        name = binary_name(target, console)
        if console and target["platform"] != "windows":
            continue
        source = GODOT_SRC / "bin" / name
        if not source.exists():
            if console:
                continue
            raise BuildError(f"SCons finished but {source} does not exist.")
        shutil.copy2(source, ctx.out / name)


def step_editor(ctx: Context) -> None:
    target = editor_target(ctx.config)
    if host_runner() != target["runner"]:
        raise BuildError("The editor is built only on Windows.")
    if (ctx.out / binary_name(target)).exists() and not ctx.args.force:
        print("[editor] already built for this key")
        return
    ctx.scons("editor", target)
    if not ctx.dry_run:
        ctx.out.mkdir(parents=True, exist_ok=True)
        copy_binary(ctx, target)
        ctx.update_manifest("editor", {"sconsFlags": scons_flags(ctx.config, target, ctx.args)})


def glue_marker(ctx: Context) -> Path:
    return ctx.out / "logs" / "glue.done"


def step_glue(ctx: Context) -> None:
    if glue_marker(ctx).exists() and not ctx.args.force:
        print("[glue] already generated for this key")
        return
    editor = GODOT_SRC / "bin" / binary_name(editor_target(ctx.config))
    ctx.run("glue", [editor, "--headless", "--generate-mono-glue", "modules/mono/glue"], GODOT_SRC)
    if not ctx.dry_run:
        glue_marker(ctx).write_text(time.strftime("%Y-%m-%dT%H:%M:%S") + "\n", encoding="utf-8")
        ctx.update_manifest("glue")


def step_assemblies(ctx: Context) -> None:
    precision = ctx.args.assemblies_precision
    variant = TEST_VARIANTS.get(precision)
    if variant is None:
        dest = ctx.out
        output_dir = GODOT_SRC / "bin"
    else:
        dest = ctx.out / variant
        output_dir = dest / "godot-output"
    nupkgs = dest / "nupkgs"
    done = nupkgs.is_dir() and len(list(nupkgs.glob("*.nupkg"))) >= len(PACKAGE_IDS)
    if done and (variant is not None or (ctx.out / "GodotSharp").is_dir()) and not ctx.args.force:
        print(f"[assemblies] {precision} precision packages already built for this key")
    else:
        if not ctx.dry_run:
            if nupkgs.exists():
                remove_tree(nupkgs)
            nupkgs.mkdir(parents=True)
            output_dir.mkdir(parents=True, exist_ok=True)
        ctx.run(
            "assemblies" if variant is None else f"assemblies-{variant}",
            [
                sys.executable,
                "modules/mono/build_scripts/build_assemblies.py",
                f"--godot-output-dir={output_dir}",
                f"--precision={precision}",
                "--push-nupkgs-local",
                nupkgs,
            ],
            GODOT_SRC,
        )
        if not ctx.dry_run:
            if variant is None:
                target_dir = ctx.out / "GodotSharp"
                if target_dir.exists():
                    remove_tree(target_dir)
                shutil.copytree(output_dir / "GodotSharp", target_dir)
            ctx.update_manifest("assemblies" if variant is None else f"assemblies-{variant}")
    if variant is None:
        step_activate(ctx)


def package_version(nupkg: Path) -> tuple[str, str]:
    stem = nupkg.stem
    for package_id in sorted(PACKAGE_IDS, key=len, reverse=True):
        prefix = package_id + "."
        if stem.startswith(prefix) and stem[len(prefix)].isdigit():
            return package_id, stem[len(prefix):]
    raise BuildError(f"Unexpected package {nupkg.name}; expected one of {', '.join(PACKAGE_IDS)}.")


def activation_source(ctx: Context) -> tuple[Path, str]:
    if ctx.args.source_dir:
        return Path(ctx.args.source_dir).resolve(), "from:" + Path(ctx.args.source_dir).name
    if ctx.args.variant:
        return ctx.out / ctx.args.variant / "nupkgs", ctx.args.variant
    return ctx.out / "nupkgs", "default"


def step_activate(ctx: Context) -> None:
    source, variant = activation_source(ctx)
    print(f"[activate] {source} -> {NUGET_LOCAL} (variant {variant})")
    if ctx.dry_run:
        return
    packages = sorted(source.glob("*.nupkg")) if source.is_dir() else []
    found = {package_version(p)[0] for p in packages}
    missing = [p for p in PACKAGE_IDS if p not in found]
    if missing:
        raise BuildError(f"[activate] {source} lacks packages: {', '.join(missing)}")

    hashes = {p.name: sha256_file(p) for p in packages}
    active = json.loads(ACTIVE_PATH.read_text(encoding="utf-8")) if ACTIVE_PATH.exists() else {}
    if NUGET_LOCAL.exists():
        remove_tree(NUGET_LOCAL)
    NUGET_LOCAL.mkdir(parents=True)
    for package in packages:
        shutil.copy2(package, NUGET_LOCAL / package.name)

    if active.get("packages") != hashes:
        # Same version number as before means NuGet would keep the old extracted copy.
        for package in packages:
            package_id, version = package_version(package)
            extracted = NUGET_PACKAGES / package_id.lower() / version
            if extracted.exists():
                print(f"[activate] removing cached {extracted}")
                remove_tree(extracted)
    ACTIVE_PATH.write_text(
        json.dumps({"key": ctx.key, "variant": variant, "packages": hashes}, indent=2) + "\n", encoding="utf-8"
    )


def template_targets(ctx: Context) -> list[dict]:
    if ctx.args.target:
        target = find_target(ctx.config, ctx.args.target)
        return [] if target["target"] == "editor" else [target]
    runner = host_runner()
    return [t for t in ctx.config["targets"] if t["runner"] == runner and t["target"] != "editor"]


def step_templates(ctx: Context) -> None:
    for target in template_targets(ctx):
        if target["runner"] != host_runner():
            raise BuildError(f"Target {target['name']} builds on {target['runner']}, this host is {host_runner()}.")
        if (ctx.out / binary_name(target)).exists() and not ctx.args.force:
            print(f"[templates] {target['name']} already built for this key")
            continue
        ctx.scons(f"templates-{target['name']}", target)
        if not ctx.dry_run:
            ctx.out.mkdir(parents=True, exist_ok=True)
            copy_binary(ctx, target)
            ctx.update_manifest(
                f"templates-{target['name']}", {"sconsFlags": scons_flags(ctx.config, target, ctx.args)}
            )


def check_smoke_framework(ctx: Context) -> None:
    expected = ctx.config["build"]["dotnetTargetFramework"]
    text = SMOKE_PROJECT.read_text(encoding="utf-8")
    if f"<TargetFramework>{expected}</TargetFramework>" not in text:
        raise BuildError(f"{SMOKE_PROJECT} must target {expected} (engine.json build.dotnetTargetFramework).")


def check_package_sources() -> None:
    expected = os.path.normcase(str(NUGET_LOCAL.resolve()))
    problems = []
    for package in sorted(NUGET_LOCAL.glob("*.nupkg")):
        package_id, version = package_version(package)
        metadata = NUGET_PACKAGES / package_id.lower() / version / ".nupkg.metadata"
        if not metadata.exists():
            problems.append(f"{package_id} {version}: not restored ({metadata} missing)")
            continue
        source = json.loads(metadata.read_text(encoding="utf-8")).get("source", "")
        resolved = os.path.normcase(str((REPO / source).resolve())) if source else ""
        if resolved.rstrip("\\/") != expected.rstrip("\\/"):
            problems.append(f"{package_id} {version}: source is '{source}', expected engine/nuget-local")
        else:
            print(f"[smoke] {package_id} {version} restored from engine/nuget-local")
    if problems:
        raise BuildError("[smoke] package source check failed:\n  " + "\n  ".join(problems))


def step_smoke(ctx: Context) -> None:
    active = json.loads(ACTIVE_PATH.read_text(encoding="utf-8")) if ACTIVE_PATH.exists() else {}
    if active.get("key") != ctx.key:
        step_activate(ctx)
    else:
        print(f"[smoke] active packages: key {ctx.key}, variant {active.get('variant')}")
    check_smoke_framework(ctx)
    # Packages of every variant share one version number and keep the timestamps from the archive, so an incremental
    # build would keep a Smoke.dll compiled against the previously active packages.
    ctx.run("smoke", ["dotnet", "build", SMOKE_PROJECT, "-nologo", "--no-incremental"], REPO)
    if not ctx.dry_run:
        check_package_sources()

    target = editor_target(ctx.config)
    console = ctx.out / binary_name(target, console=True)
    editor = console if console.exists() else ctx.out / binary_name(target)
    if not ctx.dry_run and not editor.exists():
        raise BuildError(f"[smoke] editor {editor} not found; run 'build.py editor' first.")
    # Import once so that the script class cache exists before the scene loads.
    ctx.run("smoke", [editor, "--headless", "--path", SMOKE_DIR, "--import"], REPO, timeout=SMOKE_TIMEOUT_S)
    try:
        output = ctx.run(
            "smoke", [editor, "--headless", "--path", SMOKE_DIR, "--quit-after", "600"], REPO, timeout=SMOKE_TIMEOUT_S
        )
    except BuildError as error:
        raise BuildError(f"SMOKE FAIL: {error}") from None
    if not ctx.dry_run and "SMOKE OK" not in output:
        raise BuildError("SMOKE FAIL: 'SMOKE OK' missing from the output.")
    if not ctx.dry_run:
        print("SMOKE OK")
        ctx.update_manifest("smoke", {"variant": active.get("variant", "default")})


def step_all(ctx: Context) -> None:
    runner = host_runner()
    if ctx.args.target:
        target = find_target(ctx.config, ctx.args.target)
        if target["runner"] != runner:
            raise BuildError(f"Target {target['name']} builds on {target['runner']}, this host is {runner}.")
        step_fetch(ctx)
        if target["target"] == "editor":
            step_editor(ctx)
            step_glue(ctx)
            step_assemblies(ctx)
            step_smoke(ctx)
        else:
            step_templates(ctx)
        return
    step_fetch(ctx)
    if runner == "windows":
        step_editor(ctx)
        step_glue(ctx)
        step_assemblies(ctx)
        step_templates(ctx)
        step_smoke(ctx)
    else:
        step_templates(ctx)


# --------------------------------------------------------------------------- main


def remove_tree(path: Path) -> None:
    def on_error(func, failed_path, _exc):  # read-only files in .git objects on Windows
        os.chmod(failed_path, 0o700)
        func(failed_path)

    shutil.rmtree(path, onexc=on_error)


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=STEPS)
    parser.add_argument("--jobs", type=int, default=os.cpu_count() or 1, help="parallel SCons jobs")
    parser.add_argument("--clean", action="store_true", help="remove engine/src and engine/out/<key> first")
    parser.add_argument("--dry-run", action="store_true", help="print commands without running them")
    parser.add_argument("--target", help="target name from engine.json")
    parser.add_argument("--force", action="store_true", help="rebuild a step even if its output exists")
    parser.add_argument("--variant", help="activate: package variant under engine/out/<key>/ (e.g. test-single)")
    parser.add_argument("--from", dest="source_dir", help="activate: directory with .nupkg files")
    parser.add_argument(
        "--assemblies-precision",
        choices=("double", *TEST_VARIANTS),
        default="double",
        help="assemblies: 'single' builds test packages into engine/out/<key>/test-single/",
    )
    parser.add_argument("--scons-cache", type=Path, default=Path(os.environ.get("HORYZONT_SCONS_CACHE", DEFAULT_SCONS_CACHE)))
    parser.add_argument("--cache-limit", type=float, default=float(os.environ.get("HORYZONT_SCONS_CACHE_LIMIT", "8")),
                        help="SCons cache limit in GiB")
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    if args.variant and args.source_dir:
        print("error: --variant and --from are mutually exclusive", file=sys.stderr)
        return 2
    try:
        ctx = Context(args)
        print(f"[build.py] key {ctx.key}, output {ctx.out}")
        if args.clean:
            for path in (SRC, ctx.out):
                print(f"[clean] removing {path}")
                if path.exists() and not ctx.dry_run:
                    remove_tree(path)
        steps = {
            "fetch": step_fetch,
            "editor": step_editor,
            "glue": step_glue,
            "assemblies": step_assemblies,
            "activate": step_activate,
            "templates": step_templates,
            "smoke": step_smoke,
            "all": step_all,
        }
        started = time.monotonic()
        steps[args.command](ctx)
        print(f"[build.py] {args.command} finished in {time.monotonic() - started:.1f} s")
        return 0
    except BuildError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    except subprocess.CalledProcessError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
