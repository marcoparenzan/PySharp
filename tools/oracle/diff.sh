#!/usr/bin/env bash
# Usage: tools/oracle/diff.sh numpy P1_float32   -> diff of PySharp's output vs real CPython's golden
# (build the CLI first: dotnet build src/PySharp)
root="$(cd "$(dirname "$0")/../.." && pwd)"
lib="$1"; name="$2"
dir="$root/src/PySharp.Tests/Oracle/$lib"
cd "$dir" && dotnet "$root/src/PySharp/bin/Debug/net10.0/PySharp.dll" run "$name.py" 2>&1 | diff - "$name.expected" | head -${3:-40}
