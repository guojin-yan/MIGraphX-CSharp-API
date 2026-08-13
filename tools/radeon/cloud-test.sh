#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
test "${repo_root}" = "/workspace/MIGraphX-CSharp-API"
cd "${repo_root}"
test -z "$(git status --porcelain)"
test -z "$(git symbolic-ref -q HEAD || true)"
test "$(git rev-parse HEAD)" = "${COMMIT_SHA:?Set COMMIT_SHA to the pushed 40-character commit under test}"
test "${#COMMIT_SHA}" -eq 40

header_path="${MIGRAPHX_HEADER_PATH:-/opt/rocm-7.2.1/include/migraphx/migraphx.h}"
library_path="${MIGRAPHX_C_LIBRARY_PATH:-/opt/rocm-7.2.1/lib/libmigraphx_c.so.3}"
test -f "${header_path}"
test -f "${library_path}"
test "$(sha256sum "${header_path}" | awk '{print $1}')" = "a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2"
resolved_library="$(readlink -f "${library_path}")"
test -n "${resolved_library}"
dependencies="$(ldd "${resolved_library}")"
! grep -q 'not found' <<< "${dependencies}"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
results=/workspace/MIGraphX-CSharp-API/test-results
mkdir -p "${results}"
./tools/radeon/env-report.sh | tee "${results}/environment.txt"
{
  printf 'requested-library=%s\n' "${library_path}"
  printf 'resolved-library=%s\n' "${resolved_library}"
  printf 'library-sha256=%s\n' "$(sha256sum "${resolved_library}" | awk '{print $1}')"
  readelf -d "${resolved_library}" | grep '(SONAME)' | grep '\[libmigraphx_c.so.3\]'
  printf '%s\n' "${dependencies}"
} | tee "${results}/official-library.txt"
./eng/build.sh 2>&1 | tee "${results}/managed-gates.txt"
pwsh ./eng/generate-interop.ps1 -HeaderPath "${header_path}" -Verify 2>&1 | tee "${results}/generator.txt"
pwsh ./eng/verify-m2-abi.ps1 -HeaderPath "${header_path}" -OfficialElfPath "${resolved_library}" 2>&1 | tee "${results}/abi-exports-model.txt"
dotnet run --project ./smoke/EnvironmentSmokeRunner/EnvironmentSmokeRunner.csproj -c Release --no-build -- --utf8-probe "${resolved_library}" 2>&1 | tee "${results}/official-m1-smoke.json"
model_path="${results}/m2-identity-float32.onnx"
pwsh ./eng/generate-m2-model.ps1 -OutputPath "${model_path}" 2>&1 | tee "${results}/model.txt"
dotnet run --project ./smoke/OnnxWorkflowSmokeRunner/OnnxWorkflowSmokeRunner.csproj -c Release --no-build -- --runtime-candidate "${resolved_library}" "${model_path}" 2>&1 | tee "${results}/official-m2-smoke.json"
printf '{"schemaVersion":"1.1.0","commit":"%s","managedGates":"completed","header":"verified","officialNativeM1":"runtime-executed","officialOnnxM2":"runtime-candidate-executed-review-required","gpuInference":"runtime-candidate-executed-review-required"}\n' "${COMMIT_SHA}" > "${results}/summary.json"
