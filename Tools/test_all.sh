#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

"$ROOT_DIR/Tools/build_native.sh"
"$ROOT_DIR/Tools/build_typescript.sh"
"$ROOT_DIR/Tools/test_script_package.sh"
"$ROOT_DIR/Build/native/ariadnets_smoke_test"
"$ROOT_DIR/Build/native/ariadnets_shared_smoke_test"
"$ROOT_DIR/Tools/verify_unity_plugins.sh"

dotnet build "$ROOT_DIR/ManagedTests/ManagedTests.csproj"
dotnet build "$ROOT_DIR/UnityCompileTests/UnityCompileTests.csproj"

case "$(uname -s)" in
    Darwin)
        DYLD_LIBRARY_PATH="$ROOT_DIR/Build/native" \
            dotnet run --project "$ROOT_DIR/ManagedTests/ManagedTests.csproj" --no-build
        ;;
    Linux)
        LD_LIBRARY_PATH="$ROOT_DIR/Build/native" \
            dotnet run --project "$ROOT_DIR/ManagedTests/ManagedTests.csproj" --no-build
        ;;
    *)
        echo "Managed native-library test is not configured for $(uname -s)" >&2
        exit 1
        ;;
esac

echo "All runtime tests passed"
