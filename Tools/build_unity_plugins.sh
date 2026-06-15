#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

"$ROOT_DIR/Tools/build_macos_universal.sh"
"$ROOT_DIR/Tools/build_ios.sh"
"$ROOT_DIR/Tools/build_android.sh"

if command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1; then
    "$ROOT_DIR/Tools/build_windows.sh"
else
    echo "Skipped Windows plugin: x86_64-w64-mingw32-gcc was not found"
fi

"$ROOT_DIR/Tools/verify_unity_plugins.sh"
echo "Built locally available Unity native plugins"
