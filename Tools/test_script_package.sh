#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
PRIVATE_KEY=$(mktemp "${TMPDIR:-/tmp}/ariadnets-test-key.XXXXXX")
trap 'rm -f "$PRIVATE_KEY"' EXIT

openssl genpkey -quiet -algorithm RSA \
    -pkeyopt rsa_keygen_bits:2048 \
    -out "$PRIVATE_KEY"

"$ROOT_DIR/Tools/package_script_update.sh" test-package 1 "$PRIVATE_KEY"
test -f "$ROOT_DIR/Build/script-packages/test-package/typescript-package.bytes"

echo "Script package build test passed"

