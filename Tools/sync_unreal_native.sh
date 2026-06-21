#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
UNITY_PLUGIN_ROOT="$ROOT_DIR/UnityPackages/com.ariadnets.runtime/Runtime/Plugins"
UNREAL_NATIVE_ROOT="$ROOT_DIR/UnrealPlugins/AriadneTS/Source/ThirdParty/AriadneTSNative"

mkdir -p "$UNREAL_NATIVE_ROOT/Include/ariadnets"
mkdir -p "$UNREAL_NATIVE_ROOT/Lib/Mac"
mkdir -p "$UNREAL_NATIVE_ROOT/Lib/Win64"
mkdir -p "$UNREAL_NATIVE_ROOT/Lib/Android/arm64-v8a"
mkdir -p "$UNREAL_NATIVE_ROOT/Lib/Android/x86_64"
mkdir -p "$UNREAL_NATIVE_ROOT/Lib/IOS"

cp "$ROOT_DIR/Native/include/ariadnets/quickjs_build_config.h" \
    "$UNREAL_NATIVE_ROOT/Include/ariadnets/quickjs_build_config.h"
cp "$ROOT_DIR/Native/include/ariadnets/ts_export.h" \
    "$UNREAL_NATIVE_ROOT/Include/ariadnets/ts_export.h"
cp "$ROOT_DIR/Native/include/ariadnets/ts_runtime.h" \
    "$UNREAL_NATIVE_ROOT/Include/ariadnets/ts_runtime.h"

cp "$UNITY_PLUGIN_ROOT/macOS/libariadnets.dylib" \
    "$UNREAL_NATIVE_ROOT/Lib/Mac/libariadnets.dylib"
cp "$UNITY_PLUGIN_ROOT/x86_64/ariadnets.dll" \
    "$UNREAL_NATIVE_ROOT/Lib/Win64/ariadnets.dll"
cp "$UNITY_PLUGIN_ROOT/Android/arm64-v8a/libariadnets.so" \
    "$UNREAL_NATIVE_ROOT/Lib/Android/arm64-v8a/libariadnets.so"
cp "$UNITY_PLUGIN_ROOT/Android/x86_64/libariadnets.so" \
    "$UNREAL_NATIVE_ROOT/Lib/Android/x86_64/libariadnets.so"
cp "$UNITY_PLUGIN_ROOT/iOS/libariadnets.a" \
    "$UNREAL_NATIVE_ROOT/Lib/IOS/libariadnets.a"

echo "Synchronized Unreal native headers and libraries from Unity package plugins."
