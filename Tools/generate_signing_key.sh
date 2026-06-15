#!/bin/sh
set -eu

OUTPUT_PATH=${1:-script-package-private-key.pem}

if [ -e "$OUTPUT_PATH" ]; then
    echo "Refusing to overwrite existing key: $OUTPUT_PATH" >&2
    exit 1
fi

openssl genpkey -algorithm RSA \
    -pkeyopt rsa_keygen_bits:3072 \
    -out "$OUTPUT_PATH"
chmod 600 "$OUTPUT_PATH"

echo "Generated private signing key at $OUTPUT_PATH"
echo "Keep this key outside source control and application builds"

