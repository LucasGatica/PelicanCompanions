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
profile_text_keys="$(mktemp)"
missing_profile_text_keys="$(mktemp)"
gmcm_translation_keys="$(mktemp)"
missing_gmcm_translation_keys="$(mktemp)"
trap 'rm -f "$default_keys" "$ptbr_keys" "$default_tokens" "$ptbr_tokens" "$profile_text_keys" "$missing_profile_text_keys" "$gmcm_translation_keys" "$missing_gmcm_translation_keys"' EXIT

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

if ! jq -e '[.. | objects | select(has("TextKey")) | select((.TextKey | type) != "string" or (.TextKey | length) == 0)] | length == 0' assets/NpcConfig.json >/dev/null; then
    echo "NpcConfig.json contains a missing or invalid TextKey." >&2
    exit 1
fi

jq -r '.. | objects | .TextKey? // empty' assets/NpcConfig.json | sort -u >"$profile_text_keys"
comm -23 "$profile_text_keys" "$default_keys" >"$missing_profile_text_keys"
if [[ -s "$missing_profile_text_keys" ]]; then
    echo "NpcConfig.json references missing default translation keys:" >&2
    sed 's/^/ - /' "$missing_profile_text_keys" >&2
    exit 1
fi

sed -nE 's/.*this\.Add(Keybind|Bool|Enum|BoundedInt|Int)Option\(gmcm, "([^"]+)".*/\2/p' \
    ModEntry.ConfigMenu.cs \
    | while IFS= read -r option_key; do
        printf 'config.%s.name\nconfig.%s.description\n' "$option_key" "$option_key"
    done \
    | sort -u >"$gmcm_translation_keys"
comm -23 "$gmcm_translation_keys" "$default_keys" >"$missing_gmcm_translation_keys"
if [[ -s "$missing_gmcm_translation_keys" ]]; then
    echo "GMCM options reference missing default translation keys:" >&2
    sed 's/^/ - /' "$missing_gmcm_translation_keys" >&2
    exit 1
fi

echo "Validation passed: build, regression tests, JSON syntax, translation/profile/GMCM coverage, and token parity."
