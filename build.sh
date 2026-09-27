#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== [1/4] Building Unified Solution ==="
dotnet build Thallium.slnx

echo "=== [2/4] Running Unit & Integration Tests ==="
dotnet test Thallium.Tests/Thallium.Tests.fsproj

echo "=== [3/4] Publishing CLI & JSON-RPC Binaries ==="
dotnet publish Thallium.Cli/Thallium.Cli.csproj -c Release -o bin/cli/
dotnet publish Thallium.JsonRpc/Thallium.JsonRpc.csproj -c Release -o bin/rpc/

echo "=== [4/4] Setting Up Root Executable Launchers ==="
cat << 'EOF' > tl
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec dotnet "$DIR/bin/cli/tl.dll" "$@"
EOF
chmod +x tl

cat << 'EOF' > tl-rpc
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec dotnet "$DIR/bin/rpc/tl-rpc.dll" "$@"
EOF
chmod +x tl-rpc

mkdir -p bin
cat << 'EOF' > bin/tl
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec dotnet "$DIR/bin/cli/tl.dll" "$@"
EOF
chmod +x bin/tl

cat << 'EOF' > bin/tl-rpc
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec dotnet "$DIR/bin/rpc/tl-rpc.dll" "$@"
EOF
chmod +x bin/tl-rpc

echo "✓ Build, Test, and Packaging Completed Successfully!"
