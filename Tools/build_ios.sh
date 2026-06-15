#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
QUICKJS_DIR="$ROOT_DIR/ThirdParty/quickjs"
BUILD_DIR="$ROOT_DIR/Build/unity/ios/arm64"
PLUGIN_DIR="$ROOT_DIR/UnityPackages/com.ariadnets.runtime/Runtime/Plugins/iOS"
. "$ROOT_DIR/Tools/native_sources.sh"

SDK=$(xcrun --sdk iphoneos --show-sdk-path)
mkdir -p "$BUILD_DIR" "$PLUGIN_DIR"

OBJECTS=""
for SOURCE in $QUICKJS_SOURCES $RUNTIME_SOURCE; do
    OBJECT="$BUILD_DIR/$(basename "$SOURCE" .c).o"
    xcrun --sdk iphoneos clang -arch arm64 -isysroot "$SDK" \
        -miphoneos-version-min=13.0 \
        -std=gnu11 -O2 -fPIC -fvisibility=hidden \
        $COMMON_DEFINES $COMMON_INCLUDES \
        -DTSRUNTIME_BUILD_SHARED \
        -c "$SOURCE" -o "$OBJECT"
    OBJECTS="$OBJECTS $OBJECT"
done

xcrun ar rcs "$PLUGIN_DIR/libariadnets.a" $OBJECTS
echo "Built Unity iOS plugin at $PLUGIN_DIR/libariadnets.a"
