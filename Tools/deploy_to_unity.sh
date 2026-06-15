#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

if [ "$#" -ne 1 ]; then
    echo "Usage: $0 <unity-project-path>" >&2
    exit 1
fi

UNITY_PROJECT=$(CDPATH= cd -- "$1" && pwd)

if [ ! -f "$UNITY_PROJECT/ProjectSettings/ProjectVersion.txt" ]; then
    echo "Not a Unity project: $UNITY_PROJECT" >&2
    exit 1
fi

EMBEDDED_PACKAGE="$UNITY_PROJECT/Packages/com.ariadnets.runtime"

if [ -e "$EMBEDDED_PACKAGE" ]; then
    echo "Refusing to overwrite an existing runtime package." >&2
    echo "Review and remove the existing deployment before running this script again." >&2
    exit 1
fi

mkdir -p "$UNITY_PROJECT/Packages" "$EMBEDDED_PACKAGE"
rsync -a --exclude '.DS_Store' \
    "$ROOT_DIR/UnityPackages/com.ariadnets.runtime/" "$EMBEDDED_PACKAGE/"

echo "Deployed UPM runtime package to $EMBEDDED_PACKAGE"
