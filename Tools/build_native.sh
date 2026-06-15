#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
BUILD_DIR="$ROOT_DIR/Build/native"
QUICKJS_DIR="$ROOT_DIR/ThirdParty/quickjs"

if [ ! -f "$QUICKJS_DIR/quickjs.c" ]; then
    echo "QuickJS source is missing at $QUICKJS_DIR" >&2
    echo "See ThirdParty/README.md" >&2
    exit 1
fi

mkdir -p "$BUILD_DIR"

make -C "$QUICKJS_DIR" CFLAGS=-fPIC libquickjs.a

clang -std=c11 -Wall -Wextra -Werror -Wno-unused-parameter -O2 -g \
    -I"$ROOT_DIR/Native/include" \
    -I"$QUICKJS_DIR" \
    "$ROOT_DIR/Native/src/ts_runtime.c" \
    "$ROOT_DIR/Native/tests/smoke_test.c" \
    "$QUICKJS_DIR/libquickjs.a" \
    -lm \
    -o "$BUILD_DIR/ariadnets_smoke_test"

case "$(uname -s)" in
    Darwin)
        SHARED_LIBRARY="$BUILD_DIR/libariadnets.dylib"
        SHARED_FLAGS="-dynamiclib"
        ;;
    Linux)
        SHARED_LIBRARY="$BUILD_DIR/libariadnets.so"
        SHARED_FLAGS="-shared"
        ;;
    *)
        echo "Shared library build is not configured for $(uname -s)" >&2
        exit 1
        ;;
esac

clang -std=c11 -Wall -Wextra -Werror -Wno-unused-parameter -O2 -g \
    -DTSRUNTIME_BUILD_SHARED \
    -fvisibility=hidden \
    -I"$ROOT_DIR/Native/include" \
    -I"$QUICKJS_DIR" \
    $SHARED_FLAGS \
    "$ROOT_DIR/Native/src/ts_runtime.c" \
    "$QUICKJS_DIR/libquickjs.a" \
    -lm \
    -o "$SHARED_LIBRARY"

case "$(uname -s)" in
    Darwin)
        RUNTIME_PATH_FLAGS="-Wl,-rpath,@loader_path"
        ;;
    Linux)
        RUNTIME_PATH_FLAGS="-Wl,-rpath,\$ORIGIN"
        ;;
esac

clang -std=c11 -Wall -Wextra -Werror -O2 -g \
    -I"$ROOT_DIR/Native/include" \
    "$ROOT_DIR/Native/tests/smoke_test.c" \
    -L"$BUILD_DIR" \
    -lariadnets \
    $RUNTIME_PATH_FLAGS \
    -o "$BUILD_DIR/ariadnets_shared_smoke_test"

echo "Built $BUILD_DIR/ariadnets_smoke_test"
echo "Built $SHARED_LIBRARY"
echo "Built $BUILD_DIR/ariadnets_shared_smoke_test"
