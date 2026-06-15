#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

if [ "$#" -ne 3 ]; then
    echo "Usage: $0 <version> <build-number> <private-key.pem>" >&2
    exit 1
fi

SCRIPT_PACKAGE_VERSION=$1
SCRIPT_PACKAGE_BUILD_NUMBER=$2
SCRIPT_PACKAGE_PRIVATE_KEY=$3
export SCRIPT_PACKAGE_VERSION SCRIPT_PACKAGE_BUILD_NUMBER SCRIPT_PACKAGE_PRIVATE_KEY

"$ROOT_DIR/Tools/build_typescript.sh"
node "$ROOT_DIR/Tools/package_script_update.mjs"
node "$ROOT_DIR/Tools/verify_script_package.mjs" \
    "$ROOT_DIR/Build/script-packages/$SCRIPT_PACKAGE_VERSION/typescript-package.bytes" \
    "$ROOT_DIR/TypeScript/dist/public-key.txt"

echo "Public key for Unity configuration:"
cat "$ROOT_DIR/TypeScript/dist/public-key.txt"
