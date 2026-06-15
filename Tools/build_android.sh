#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
QUICKJS_DIR="$ROOT_DIR/ThirdParty/quickjs"
BUILD_DIR="$ROOT_DIR/Build/unity/android"
PLUGIN_ROOT="$ROOT_DIR/UnityPackages/com.ariadnets.runtime/Runtime/Plugins/Android"
. "$ROOT_DIR/Tools/native_sources.sh"

ANDROID_NDK_ROOT=${ANDROID_NDK_ROOT:-"$HOME/Library/Android/sdk/ndk/21.3.6528147"}
TOOLCHAIN="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/darwin-x86_64/bin"

if [ ! -d "$TOOLCHAIN" ]; then
    echo "Android NDK toolchain was not found at $TOOLCHAIN" >&2
    exit 1
fi

build_abi() {
    ABI=$1
    TARGET=$2
    CC="$TOOLCHAIN/$TARGET-clang"
    ABI_BUILD_DIR="$BUILD_DIR/$ABI"
    ABI_PLUGIN_DIR="$PLUGIN_ROOT/$ABI"
    mkdir -p "$ABI_BUILD_DIR" "$ABI_PLUGIN_DIR"

    OBJECTS=""
    for SOURCE in $QUICKJS_SOURCES $RUNTIME_SOURCE; do
        OBJECT="$ABI_BUILD_DIR/$(basename "$SOURCE" .c).o"
        "$CC" -std=gnu11 -O2 -fPIC -fvisibility=hidden \
            $COMMON_DEFINES $COMMON_INCLUDES \
            -DTSRUNTIME_BUILD_SHARED \
            -c "$SOURCE" -o "$OBJECT"
        OBJECTS="$OBJECTS $OBJECT"
    done

    "$CC" -shared $OBJECTS -lm -o "$ABI_PLUGIN_DIR/libariadnets.so"
    "$TOOLCHAIN/llvm-strip" --strip-unneeded "$ABI_PLUGIN_DIR/libariadnets.so"
}

build_abi arm64-v8a aarch64-linux-android24
build_abi x86_64 x86_64-linux-android24

echo "Built Unity Android plugins under $PLUGIN_ROOT"
