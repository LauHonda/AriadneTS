#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
QUICKJS_DIR="$ROOT_DIR/ThirdParty/quickjs"
BUILD_DIR="$ROOT_DIR/Build/unity/windows/x86_64"
PLUGIN_DIR="$ROOT_DIR/UnityPackages/com.ariadnets.runtime/Runtime/Plugins/x86_64"
. "$ROOT_DIR/Tools/native_sources.sh"

CC=${MINGW_CC:-x86_64-w64-mingw32-gcc}
if ! command -v "$CC" >/dev/null 2>&1; then
    echo "MinGW compiler '$CC' was not found." >&2
    exit 1
fi

mkdir -p "$BUILD_DIR" "$PLUGIN_DIR"
"$CC" -std=gnu11 -O2 -shared -fvisibility=hidden \
    $COMMON_DEFINES $COMMON_INCLUDES \
    -DTSRUNTIME_BUILD_SHARED \
    $QUICKJS_SOURCES "$RUNTIME_SOURCE" \
    -lm -static -static-libgcc \
    -o "$PLUGIN_DIR/ariadnets.dll"
"${CC%gcc}strip" --strip-unneeded "$PLUGIN_DIR/ariadnets.dll"

echo "Built Unity Windows plugin at $PLUGIN_DIR/ariadnets.dll"
