#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

if [ -x "$ROOT_DIR/TypeScript/node_modules/.bin/tsc" ]; then
    TSC="$ROOT_DIR/TypeScript/node_modules/.bin/tsc"
elif command -v tsc >/dev/null 2>&1; then
    TSC=$(command -v tsc)
else
    echo "TypeScript compiler is missing. Run npm install in TypeScript." >&2
    exit 1
fi

"$TSC" -p "$ROOT_DIR/TypeScript/tsconfig.json"
node "$ROOT_DIR/Tools/generate_script_manifest.mjs" "${SCRIPT_PACKAGE_VERSION:-local-dev}"
if [ -n "${SCRIPT_PACKAGE_PRIVATE_KEY:-}" ]; then
    node "$ROOT_DIR/Tools/sign_script_manifest.mjs"
fi
echo "Built TypeScript modules in $ROOT_DIR/TypeScript/dist"
