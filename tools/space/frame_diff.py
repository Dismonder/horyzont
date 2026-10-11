#!/usr/bin/env python3
"""Frame difference metric for the manual tests of K5-22 (impostor cross-fade, triplanar on planet C).

Usage:
    frame_diff.py <dir> --band START:END [--json out.json]     # Manual_ImpostorTransition_NoVisibleJump
    frame_diff.py <dir> --still [--json out.json]              # Manual_TriplanarPlanetC_NoFlicker (standing part)
    frame_diff.py --self-test

<dir> holds the frames written by the screenshot hook (PNG, 8-bit RGB or RGBA, not interlaced), compared in file name order.
For each pair of consecutive frames the mean absolute difference of the luminance (0..255) is computed.

--band START:END   frames START..END (indices, inclusive, of the sorted list) are the cross-fade band (0.8-2.2 R); passes when the
                   largest difference inside the band is at most 2 x the median difference outside it (no visible jump).
--still            the camera stands still; passes when the mean difference is at most 1/255 (no flicker).

Exit codes: 0 = pass, 1 = fail, 2 = invalid input or arguments. Frames are treated as untrusted input (size limits, PNG checks).
Standard library only (Python 3.12).
"""

from __future__ import annotations

import argparse
import json
import statistics
import struct
import sys
import tempfile
import zlib
from pathlib import Path

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
MAX_FILE_BYTES = 64 * 1024 * 1024
MAX_PIXELS = 4096 * 4096
MAX_FRAMES = 20_000
JUMP_FACTOR = 2.0
STILL_LIMIT = 1.0


class FrameError(Exception):
    """An invalid frame or argument."""


def read_png_luma(path: Path) -> tuple[int, int, list[float]]:
    """Decodes an 8-bit RGB/RGBA non-interlaced PNG into luminance values (Rec. 601)."""
    data = path.read_bytes()
    if len(data) > MAX_FILE_BYTES:
        raise FrameError(f"{path.name}: file too large")
    if not data.startswith(PNG_SIGNATURE):
        raise FrameError(f"{path.name}: not a PNG")
    pos = len(PNG_SIGNATURE)
    width = height = channels = 0
    idat = bytearray()
    while pos + 8 <= len(data):
        length, kind = struct.unpack(">I4s", data[pos : pos + 8])
        chunk = data[pos + 8 : pos + 8 + length]
        if len(chunk) != length:
            raise FrameError(f"{path.name}: truncated chunk")
        pos += 12 + length
        if kind == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(
                ">IIBBBBB", chunk
            )
            if depth != 8 or color not in (2, 6) or interlace != 0:
                raise FrameError(
                    f"{path.name}: only 8-bit RGB/RGBA non-interlaced PNG is supported"
                )
            if width == 0 or height == 0 or width * height > MAX_PIXELS:
                raise FrameError(f"{path.name}: bad size {width}x{height}")
            channels = 3 if color == 2 else 4
        elif kind == b"IDAT":
            idat += chunk
        elif kind == b"IEND":
            break
    if channels == 0:
        raise FrameError(f"{path.name}: no IHDR")
    stride = width * channels
    try:
        raw = zlib.decompressobj().decompress(bytes(idat), (stride + 1) * height + 1)
    except zlib.error as error:
        raise FrameError(f"{path.name}: {error}") from None
    if len(raw) < (stride + 1) * height:
        raise FrameError(f"{path.name}: truncated image data")
    pixels = unfilter(raw, stride, height, channels, path.name)
    luma = [0.0] * (width * height)
    for i in range(width * height):
        r, g, b = (
            pixels[i * channels],
            pixels[i * channels + 1],
            pixels[i * channels + 2],
        )
        luma[i] = 0.299 * r + 0.587 * g + 0.114 * b
    return width, height, luma


def unfilter(raw: bytes, stride: int, height: int, bpp: int, name: str) -> bytearray:
    out = bytearray(stride * height)
    prior = bytearray(stride)
    for y in range(height):
        kind = raw[y * (stride + 1)]
        line = bytearray(raw[y * (stride + 1) + 1 : (y + 1) * (stride + 1)])
        for x in range(stride):
            left = line[x - bpp] if x >= bpp else 0
            up = prior[x]
            corner = prior[x - bpp] if x >= bpp else 0
            if kind == 0:
                pass
            elif kind == 1:
                line[x] = (line[x] + left) & 0xFF
            elif kind == 2:
                line[x] = (line[x] + up) & 0xFF
            elif kind == 3:
                line[x] = (line[x] + ((left + up) >> 1)) & 0xFF
            elif kind == 4:
                p = left + up - corner
                pa, pb, pc = abs(p - left), abs(p - up), abs(p - corner)
                predictor = (
                    left if pa <= pb and pa <= pc else (up if pb <= pc else corner)
                )
                line[x] = (line[x] + predictor) & 0xFF
            else:
                raise FrameError(f"{name}: bad filter {kind}")
        out[y * stride : (y + 1) * stride] = line
        prior = line
    return out


def write_png(path: Path, width: int, height: int, rgb: bytes) -> None:
    """Writes an 8-bit RGB PNG (self test)."""
    rows = b"".join(
        b"\x00" + rgb[y * width * 3 : (y + 1) * width * 3] for y in range(height)
    )

    def chunk(kind: bytes, body: bytes) -> bytes:
        return (
            struct.pack(">I", len(body))
            + kind
            + body
            + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)
        )

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    path.write_bytes(
        PNG_SIGNATURE
        + chunk(b"IHDR", header)
        + chunk(b"IDAT", zlib.compress(rows))
        + chunk(b"IEND", b"")
    )


def differences(directory: Path) -> list[float]:
    frames = sorted(p for p in directory.iterdir() if p.suffix.lower() == ".png")
    if len(frames) < 2:
        raise FrameError("need at least two PNG frames")
    if len(frames) > MAX_FRAMES:
        raise FrameError("too many frames")
    result: list[float] = []
    previous = read_png_luma(frames[0])
    for frame in frames[1:]:
        current = read_png_luma(frame)
        if current[:2] != previous[:2]:
            raise FrameError(f"{frame.name}: size differs from the previous frame")
        a, b = previous[2], current[2]
        result.append(sum(abs(x - y) for x, y in zip(a, b)) / len(a))
        previous = current
    return result


def evaluate_band(diffs: list[float], start: int, end: int) -> dict:
    # Difference i is between frames i and i + 1; it belongs to the band when both frames do.
    inside = [d for i, d in enumerate(diffs) if start <= i and i + 1 <= end]
    outside = [d for i, d in enumerate(diffs) if not (start <= i and i + 1 <= end)]
    if not inside or not outside:
        raise FrameError("the band must leave frames both inside and outside it")
    median = statistics.median(outside)
    peak = max(inside)
    limit = JUMP_FACTOR * median
    return {
        "mode": "band",
        "peak_in_band": peak,
        "median_outside": median,
        "limit": limit,
        "pass": peak <= limit,
    }


def evaluate_still(diffs: list[float]) -> dict:
    mean = statistics.fmean(diffs)
    return {
        "mode": "still",
        "mean": mean,
        "limit": STILL_LIMIT,
        "pass": mean <= STILL_LIMIT,
    }


def run(directory: Path, band: str | None, still: bool) -> dict:
    diffs = differences(directory)
    if still:
        report = evaluate_still(diffs)
    else:
        try:
            start_text, end_text = (band or "").split(":")
            start, end = int(start_text), int(end_text)
        except ValueError:
            raise FrameError("--band must be START:END") from None
        if not 0 <= start < end <= len(diffs):
            raise FrameError("--band out of range")
        report = evaluate_band(diffs, start, end)
    report["frames"] = len(diffs) + 1
    report["differences"] = [round(d, 4) for d in diffs]
    return report


def self_test() -> int:
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        smooth, jump, still = root / "smooth", root / "jump", root / "still"
        for folder in (smooth, jump, still):
            folder.mkdir()
        width, height = 8, 6
        for i in range(12):
            # A slow pan: every frame shifts the gradient by five levels.
            write_png(
                smooth / f"f{i:03}.png",
                width,
                height,
                bytes(
                    ((x * 20 + 5 * i) % 256)
                    for x in range(width)
                    for _ in range(height)
                    for _ in range(3)
                ),
            )
            level = 100 + i + (60 if i >= 6 else 0)
            write_png(
                jump / f"f{i:03}.png",
                width,
                height,
                bytes([level] * width * height * 3),
            )
            write_png(
                still / f"f{i:03}.png", width, height, bytes([90] * width * height * 3)
            )
        checks = [
            ("smooth band passes", run(smooth, "4:8", False)["pass"] is True),
            ("jump inside band fails", run(jump, "4:8", False)["pass"] is False),
            ("still passes", run(still, None, True)["pass"] is True),
            ("pan is not still", run(smooth, None, True)["pass"] is False),
        ]
        bad = root / "bad"
        bad.mkdir()
        (bad / "a.png").write_bytes(b"not a png")
        (bad / "b.png").write_bytes(b"not a png")
        try:
            run(bad, None, True)
            checks.append(("invalid PNG rejected", False))
        except FrameError:
            checks.append(("invalid PNG rejected", True))
    failed = [name for name, ok in checks if not ok]
    for name, ok in checks:
        print(f"{'ok  ' if ok else 'FAIL'} {name}")
    return 1 if failed else 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Frame difference metric for the K5-22 manual tests."
    )
    parser.add_argument("directory", nargs="?", type=Path)
    parser.add_argument("--band", help="START:END frame indices of the cross-fade band")
    parser.add_argument(
        "--still",
        action="store_true",
        help="camera standing still: mean difference <= 1/255",
    )
    parser.add_argument("--json", type=Path, help="write the report as JSON")
    parser.add_argument(
        "--self-test", action="store_true", help="check the metric on generated frames"
    )
    args = parser.parse_args(argv)
    if args.self_test:
        return self_test()
    if args.directory is None or (args.band is None) == (not args.still):
        parser.print_usage(sys.stderr)
        print(
            "frame_diff: give <dir> and exactly one of --band / --still",
            file=sys.stderr,
        )
        return 2
    try:
        report = run(args.directory, args.band, args.still)
    except (FrameError, OSError) as error:
        print(f"frame_diff: {error}", file=sys.stderr)
        return 2
    summary = {k: v for k, v in report.items() if k != "differences"}
    print(json.dumps(summary))
    if args.json is not None:
        args.json.write_text(json.dumps(report, indent=2), encoding="utf-8")
    return 0 if report["pass"] else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
