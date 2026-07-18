#!/usr/bin/env bash

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

dotnet build -p:EnableModDeploy=false -p:EnableModZip=false

dotnet msbuild tests/PelicanCompanions.Tests/PelicanCompanions.Tests.csproj \
    -target:Build \
    -property:EnableModDeploy=false \
    -property:EnableModZip=false \
    -property:UseSharedCompilation=false \
    -nodeReuse:false \
    -maxcpucount:1 \
    -verbosity:minimal
dotnet run --project tests/PelicanCompanions.Tests/PelicanCompanions.Tests.csproj \
    --no-build \
    --no-restore

jq -e . manifest.json assets/NpcConfig.json i18n/default.json i18n/pt-BR.json >/dev/null

default_keys="$(mktemp)"
ptbr_keys="$(mktemp)"
default_tokens="$(mktemp)"
ptbr_tokens="$(mktemp)"
trap 'rm -f "$default_keys" "$ptbr_keys" "$default_tokens" "$ptbr_tokens"' EXIT

jq -r 'keys[]' i18n/default.json | sort >"$default_keys"
jq -r 'keys[]' i18n/pt-BR.json | sort >"$ptbr_keys"

if ! diff -u "$default_keys" "$ptbr_keys"; then
    echo "Translation keys differ between default.json and pt-BR.json." >&2
    exit 1
fi

token_filter='to_entries[] | .key as $key | (.value | tostring | [scan("\\{[A-Za-z_][A-Za-z0-9_]*\\}")] | unique | sort | join(",")) as $tokens | "\($key)\t\($tokens)"'
jq -r "$token_filter" i18n/default.json | sort >"$default_tokens"
jq -r "$token_filter" i18n/pt-BR.json | sort >"$ptbr_tokens"

if ! diff -u "$default_tokens" "$ptbr_tokens"; then
    echo "Translation interpolation tokens differ between default.json and pt-BR.json." >&2
    exit 1
fi

echo "Validation passed: build, 18 tests, JSON syntax, translation keys, and token parity."
