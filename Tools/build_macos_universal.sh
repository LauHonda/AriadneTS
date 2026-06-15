#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
QUICKJS_DIR="$ROOT_DIR/ThirdParty/quickjs"
BUILD_DIR="$ROOT_DIR/Build/unity/macos"
PLUGIN_DIR="$ROOT_DIR/UnityPackages/com.ariadnets.runtime/Runtime/Plugins/macOS"
. "$ROOT_DIR/Tools/native_sources.sh"

SDK=$(xcrun --sdk macosx --show-sdk-path)

build_arch() {
    ARCH=$1
    ARCH_DIR="$BUILD_DIR/$ARCH"
    mkdir -p "$ARCH_DIR"

    OBJECTS=""
    for SOURCE in $QUICKJS_SOURCES $RUNTIME_SOURCE; do
        OBJECT="$ARCH_DIR/$(basename "$SOURCE" .c).o"
        xcrun --sdk macosx clang -arch "$ARCH" -isysroot "$SDK" \
            -std=gnu11 -O2 -fPIC -fvisibility=hidden \
            $COMMON_DEFINES $COMMON_INCLUDES \
            -DTSRUNTIME_BUILD_SHARED \
            -c "$SOURCE" -o "$OBJECT"
        OBJECTS="$OBJECTS $OBJECT"
    done

    xcrun --sdk macosx clang -arch "$ARCH" -isysroot "$SDK" \
        -dynamiclib -install_name @rpath/libariadnets.dylib $OBJECTS -lm \
        -o "$ARCH_DIR/libariadnets.dylib"
}

build_arch x86_64
build_arch arm64

mkdir -p "$PLUGIN_DIR"
xcrun lipo -create \
    "$BUILD_DIR/x86_64/libariadnets.dylib" \
    "$BUILD_DIR/arm64/libariadnets.dylib" \
    -output "$PLUGIN_DIR/libariadnets.dylib"

echo "Built Unity macOS universal plugin at $PLUGIN_DIR/libariadnets.dylib"
