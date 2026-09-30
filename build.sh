#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== [1/4] Building Unified Solution ==="
dotnet build Thallium.slnx

echo "=== [2/4] Running Unit & Integration Tests ==="
dotnet test Thallium.Tests/Thallium.Tests.fsproj

echo "=== [3/4] Publishing CLI & JSON-RPC Binaries ==="
dotnet publish Thallium.Cli/Thallium.Cli.csproj -c Release -r linux-x64 -o bin/cli/
dotnet publish Thallium.JsonRpc/Thallium.JsonRpc.csproj -c Release -r linux-x64 -o bin/rpc/

echo "=== [4/4] Setting Up Root Executable Launchers ==="
cat << 'LAUNCHER' > tl
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -f "$DIR/bin/cli/tl" ]; then
    exec "$DIR/bin/cli/tl" "$@"
else
    exec dotnet "$DIR/bin/cli/tl.dll" "$@"
fi
LAUNCHER
chmod +x tl

cat << 'LAUNCHER' > tl-rpc
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -f "$DIR/bin/rpc/tl-rpc" ]; then
    exec "$DIR/bin/rpc/tl-rpc" "$@"
else
    exec dotnet "$DIR/bin/rpc/tl-rpc.dll" "$@"
fi
LAUNCHER
chmod +x tl-rpc

mkdir -p bin
cp tl bin/tl
cp tl-rpc bin/tl-rpc

echo "✓ Build, Test, and Packaging Completed Successfully!"
