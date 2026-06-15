#!/bin/sh

QUICKJS_SOURCES="
$QUICKJS_DIR/quickjs.c
$QUICKJS_DIR/dtoa.c
$QUICKJS_DIR/libregexp.c
$QUICKJS_DIR/libunicode.c
$QUICKJS_DIR/cutils.c
"

RUNTIME_SOURCE="$ROOT_DIR/Native/src/ts_runtime.c"
COMMON_DEFINES="-D_GNU_SOURCE -include $ROOT_DIR/Native/include/ariadnets/quickjs_build_config.h"
COMMON_INCLUDES="-I$ROOT_DIR/Native/include -I$QUICKJS_DIR"
