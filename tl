#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -f "$DIR/bin/cli/tl" ]; then
    exec "$DIR/bin/cli/tl" "$@"
else
    exec dotnet "$DIR/bin/cli/tl.dll" "$@"
fi
