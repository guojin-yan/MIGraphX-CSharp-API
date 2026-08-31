#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: provider-callback-probe.sh --repo DIR --feed DIR --record DIR --native FILE --source-sha SHA --version VERSION --core-sha SHA256 [--fixture none|fake-native-provider-dispatch]" >&2
  exit 2
}

repo=''
feed=''
record=''
native=''
source_sha=''
version=''
core_sha=''
fixture='none'
while [[ $# -gt 0 ]]; do
  [[ $# -ge 2 ]] || usage
  case "$1" in
    --repo) repo="$2" ;;
    --feed) feed="$2" ;;
    --record) record="$2" ;;
    --native) native="$2" ;;
    --source-sha) source_sha="$2" ;;
    --version) version="$2" ;;
    --core-sha) core_sha="$2" ;;
    --fixture) fixture="$2" ;;
    *) usage ;;
  esac
  shift 2
done

for directory in "$repo" "$feed"; do [[ "$directory" = /* && -d "$directory" ]] || usage; done
[[ "$record" = /* && -d "$(dirname "$record")" ]] || usage
[[ "$native" = /* && -f "$native" ]] || usage
[[ "$source_sha" =~ ^[a-f0-9]{40}$ ]] || usage
[[ "$version" = '0.0.0' ]] || usage
[[ "$core_sha" =~ ^[a-f0-9]{64}$ ]] || usage
[[ "$fixture" = 'none' || "$fixture" = 'fake-native-provider-dispatch' ]] || usage

repo="$(realpath "$repo")"
feed="$(realpath "$feed")"
record="$(realpath -m "$record")"
is_same_or_child() { [[ "$1" == "$2" || "$1" == "$2"/* ]]; }
if is_same_or_child "$record" "$repo" || is_same_or_child "$repo" "$record" ||
   is_same_or_child "$record" "$feed" || is_same_or_child "$feed" "$record"; then
  echo 'evidence record must be isolated from repository and package feed' >&2
  exit 1
fi
if [[ -e "$record" ]]; then
  [[ -d "$record" && -z "$(find "$record" -mindepth 1 -print -quit)" ]] || {
    echo 'evidence record directory must be new or empty before a provider callback probe' >&2
    exit 1
  }
else
  mkdir -p "$record"
fi

[[ "$(git -C "$repo" rev-parse HEAD)" = "$source_sha" ]] || { echo 'source SHA mismatch' >&2; exit 1; }
[[ -z "$(git -C "$repo" status --porcelain)" ]] || { echo 'source checkout is dirty' >&2; exit 1; }
[[ -z "$(git -C "$repo" branch --show-current)" ]] || { echo 'source checkout is not detached' >&2; exit 1; }

core="$feed/JYPPX.ROCm.MIGraphX.CSharp.API.$version.nupkg"
[[ -f "$core" ]] || { echo "core package missing: $core" >&2; exit 1; }
[[ "$(sha256sum "$core" | awk '{print $1}')" = "$core_sha" ]] || { echo 'core package hash mismatch' >&2; exit 1; }

mkdir -p "$record/raw" "$record/build" "$record/packages"
cat > "$record/build/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="provider-feed" value="$feed" /></packageSources>
</configuration>
EOF
{
  echo "sourceSha=$source_sha"
  echo "version=$version"
  echo "coreSha256=$core_sha"
  echo "nativeSha256=$(sha256sum "$native" | awk '{print $1}')"
  echo 'cleanDetached=true'
  echo 'promotionRequested=false'
} > "$record/raw/identities.txt"
{ readelf -d "$native"; ldd "$native"; } > "$record/raw/native-library.txt"
! ldd "$native" | grep -q 'not found' || { echo 'native dependency closure is incomplete' >&2; exit 1; }

project="$repo/tools/m12-provider-callback-probe/M12ProviderCallbackProbe.csproj"
dotnet restore "$project" --configfile "$record/build/NuGet.Config" --packages "$record/packages" --no-cache --force-evaluate > "$record/raw/restore.log" 2>&1
dotnet build "$project" -c Release --no-restore -p:M12PackageVersion="$version" > "$record/raw/build.log" 2>&1

set +e
probe_args=(--native "$native" --source-sha "$source_sha" --expected-version "$version" --output "$record/raw/provider-callback.json")
if [[ "$fixture" = 'fake-native-provider-dispatch' ]]; then probe_args+=(--provider-fixture); fi
dotnet run --project "$project" -c Release --no-build -p:M12PackageVersion="$version" -- "${probe_args[@]}" \
  > "$record/raw/provider-callback-stdout.log" 2> "$record/raw/provider-callback-stderr.log"
probe_exit=$?
set -e
cat > "$record/raw/run-metadata.json" <<EOF
{
  "schemaVersion": "1.0.0",
  "evidence": "runtime-candidate-executed-review-required",
  "sourceSha": "$source_sha",
  "version": "$version",
  "providerFixture": "$fixture",
  "probeExitCode": $probe_exit,
  "promotionRequested": false,
  "probeKind": "provider-callback-invocation",
  "controlledRejection": true
}
EOF
find "$record" -type f ! -name artifact-hashes.txt -print0 | sort -z | xargs -0 sha256sum > "$record/raw/artifact-hashes.txt"

set +e
pwsh -NoProfile -File "$repo/tools/m12-provider-callback-probe/review.ps1" \
  -RecordDirectory "$record" -CorePackagePath "$core" -SourceSha "$source_sha" -CoreSha256 "$core_sha" \
  > "$record/raw/review.log" 2>&1
review_exit=$?
set -e
if [[ $probe_exit -ne 0 ]]; then exit $probe_exit; fi
exit $review_exit
