#!/usr/bin/env bash
# Usage: tools/ci/godot_import.sh <editor-exe> [project-dir=game]
# Runs `<editor> --headless --path <project> --import --quit-after 1000` and tolerates the known Godot 4.7.2 shutdown
# segfault (docs/plans/K2-12-budowle.md, Odstepstwa): if the editor help cache thread finishes after --import has quit,
# its deferred call runs after EditorNode is freed and the editor dies with exit 139 (Windows: 0xC0000005 = 3221225477).
# ._sc_ next to the exe gives the editor its own data dir (avoids a stale shared doc cache that makes the thread slow);
# --quit-after (overrides the 1 frame set by --import) keeps the editor up until the thread has handed back.
# The marker is removed on exit so later steps start the editor in the normal mode.
# The crash exit code is accepted ONLY when the import demonstrably completed (files under <project>/.godot written
# after this script started); any other failure, or a crash without that evidence, still fails the step.
set -u
editor="${1:?editor path required}"
project="${2:-game}"

marker="$(dirname "$editor")/._sc_"
stamp="$(mktemp)"
touch "$marker"
trap 'rm -f "$marker" "$stamp"' EXIT

"$editor" --headless --path "$project" --import --quit-after 1000
rc=$?

if [ "$rc" -eq 0 ]; then
  exit 0
fi

if [ "$rc" -eq 139 ] || [ "$rc" -eq 3221225477 ]; then
  if [ -d "$project/.godot" ] && [ -n "$(find "$project/.godot" -type f -newer "$stamp" -print -quit 2>/dev/null)" ]; then
    echo "godot_import: editor exited $rc after a completed import (known shutdown segfault), tolerated"
    exit 0
  fi
  echo "godot_import: editor exited $rc and no import output was written, failing" >&2
  exit "$rc"
fi

echo "godot_import: editor exited $rc, failing" >&2
exit "$rc"
