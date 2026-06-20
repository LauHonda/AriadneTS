#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
BUILD_DIR="$ROOT_DIR/Build/native"
QUICKJS_DIR="$ROOT_DIR/ThirdParty/quickjs"
HOST_OS=$(uname -s)

if [ ! -f "$QUICKJS_DIR/quickjs.c" ]; then
    echo "QuickJS source is missing at $QUICKJS_DIR" >&2
    echo "See ThirdParty/README.md" >&2
    exit 1
fi

mkdir -p "$BUILD_DIR"

CFLAGS=-fPIC make -C "$QUICKJS_DIR" libquickjs.a

case "$HOST_OS" in
    Darwin)
        SHARED_LIBRARY="$BUILD_DIR/libariadnets.dylib"
        SHARED_FLAGS="-dynamiclib"
        RUNTIME_PATH_FLAGS="-Wl,-rpath,@loader_path"
        THREAD_FLAGS=""
        ;;
    Linux)
        SHARED_LIBRARY="$BUILD_DIR/libariadnets.so"
        SHARED_FLAGS="-shared"
        RUNTIME_PATH_FLAGS="-Wl,-rpath,\$ORIGIN"
        THREAD_FLAGS="-pthread"
        ;;
    *)
        echo "Shared library build is not configured for $HOST_OS" >&2
        exit 1
        ;;
esac

clang -std=c11 -Wall -Wextra -Werror -Wno-unused-parameter -O2 -g \
    -I"$ROOT_DIR/Native/include" \
    -I"$QUICKJS_DIR" \
    "$ROOT_DIR/Native/src/ts_runtime.c" \
    "$ROOT_DIR/Native/tests/smoke_test.c" \
    "$QUICKJS_DIR/libquickjs.a" \
    -lm $THREAD_FLAGS \
    -o "$BUILD_DIR/ariadnets_smoke_test"

clang -std=c11 -Wall -Wextra -Werror -Wno-unused-parameter -O2 -g \
    -DTSRUNTIME_BUILD_SHARED \
    -fvisibility=hidden \
    -I"$ROOT_DIR/Native/include" \
    -I"$QUICKJS_DIR" \
    $SHARED_FLAGS \
    "$ROOT_DIR/Native/src/ts_runtime.c" \
    "$QUICKJS_DIR/libquickjs.a" \
    -lm $THREAD_FLAGS \
    -o "$SHARED_LIBRARY"

clang -std=c11 -Wall -Wextra -Werror -O2 -g \
    -I"$ROOT_DIR/Native/include" \
    "$ROOT_DIR/Native/tests/smoke_test.c" \
    -L"$BUILD_DIR" \
    -lariadnets \
    $RUNTIME_PATH_FLAGS \
    $THREAD_FLAGS \
    -o "$BUILD_DIR/ariadnets_shared_smoke_test"

echo "Built $BUILD_DIR/ariadnets_smoke_test"
echo "Built $SHARED_LIBRARY"
echo "Built $BUILD_DIR/ariadnets_shared_smoke_test"
