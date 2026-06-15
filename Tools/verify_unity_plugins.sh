#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
PLUGIN_ROOT="$ROOT_DIR/UnityPackages/com.ariadnets.runtime/Runtime/Plugins"
REQUIRED_SYMBOLS="
ts_runtime_abi_version
ts_runtime_create
ts_runtime_destroy
ts_runtime_eval
ts_runtime_eval_module
ts_runtime_execute_pending_jobs
ts_runtime_invoke
ts_runtime_last_result
ts_runtime_last_error
"

MACOS_PLUGIN="$PLUGIN_ROOT/macOS/libariadnets.dylib"
IOS_PLUGIN="$PLUGIN_ROOT/iOS/libariadnets.a"
ANDROID_ARM64_PLUGIN="$PLUGIN_ROOT/Android/arm64-v8a/libariadnets.so"
ANDROID_X64_PLUGIN="$PLUGIN_ROOT/Android/x86_64/libariadnets.so"
WINDOWS_PLUGIN="$PLUGIN_ROOT/x86_64/ariadnets.dll"

verify_symbols() {
    COMMAND=$1
    OUTPUT=$(sh -c "$COMMAND")
    for SYMBOL in $REQUIRED_SYMBOLS; do
        if ! printf '%s\n' "$OUTPUT" | grep -q "$SYMBOL"; then
            echo "Missing native plugin symbol: $SYMBOL" >&2
            exit 1
        fi
    done
}

verify_importer() {
    META_FILE="$1.meta"
    PLATFORM_MARKER=$2
    if ! grep -q '^PluginImporter:' "$META_FILE" ||
        ! grep -q "$PLATFORM_MARKER" "$META_FILE"; then
        echo "Missing Unity plugin importer configuration: $META_FILE" >&2
        exit 1
    fi
}

lipo "$MACOS_PLUGIN" -verify_arch x86_64 arm64
verify_symbols "nm -gU $MACOS_PLUGIN"
verify_symbols "nm -gU $IOS_PLUGIN"
verify_importer "$MACOS_PLUGIN" 'OS: OSX'
verify_importer "$IOS_PLUGIN" 'iPhone: iOS'

NDK_NM=${ANDROID_NM:-"$HOME/Library/Android/sdk/ndk/21.3.6528147/toolchains/llvm/prebuilt/darwin-x86_64/bin/llvm-nm"}
verify_symbols "$NDK_NM -D $ANDROID_ARM64_PLUGIN"
verify_symbols "$NDK_NM -D $ANDROID_X64_PLUGIN"
verify_importer "$ANDROID_ARM64_PLUGIN" 'Android: Android'
verify_importer "$ANDROID_X64_PLUGIN" 'Android: Android'

if [ -f "$WINDOWS_PLUGIN" ]; then
    verify_symbols "x86_64-w64-mingw32-objdump -p $WINDOWS_PLUGIN"
    verify_importer "$WINDOWS_PLUGIN" 'OS: Windows'
    if x86_64-w64-mingw32-objdump -p "$WINDOWS_PLUGIN" | grep -q "libwinpthread"; then
        echo "Windows plugin has an unexpected libwinpthread dependency" >&2
        exit 1
    fi
fi

echo "Verified Unity native plugins"
