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
if [[ -z "${HIPSHARP_REPOSITORY_ROOT:-}" && -d /workspace/HIP-CSharp-API/HIP-CSharp-API ]]; then
  export HIPSHARP_REPOSITORY_ROOT=/workspace/HIP-CSharp-API/HIP-CSharp-API
fi
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
dotnet run --project ./smoke/OnnxWorkflowSmokeRunner/OnnxWorkflowSmokeRunner.csproj -c Release --no-build -- --runtime-options-candidate "${resolved_library}" "${model_path}" 2>&1 | tee "${results}/official-m9-options-smoke.json"

# M12 package-only candidate: keep the managed package boundary explicit and run
# every implemented bounded path in one process. The result remains review-required
# and the independent reviewer below must pass before it is summarized as complete.
m12_fixture_directory="${repo_root}/artifacts/models/m12"
pwsh ./eng/pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild 2>&1 | tee "${results}/m12-package.txt"
pwsh ./eng/generate-m12-fixtures.ps1 -OutputDirectory "${m12_fixture_directory}" 2>&1 | tee "${results}/m12-fixtures.txt"
m12_package="${repo_root}/artifacts/packages/JYPPX.ROCm.MIGraphX.CSharp.API.0.0.0.nupkg"
cloud_record_root="${MIGRAPHX_CLOUD_RECORD_ROOT:-/workspace/migraphx-cloud-records/${COMMIT_SHA}}"
[[ "${cloud_record_root}" = /* ]] || { echo 'MIGRAPHX_CLOUD_RECORD_ROOT must be absolute' >&2; exit 1; }
mkdir -p "${cloud_record_root}"
m12_record="${cloud_record_root}/m12-candidate"
printf '%s\n' "${m12_record}" | tee "${results}/m12-record-path.txt"
m12_core_sha="$(sha256sum "${m12_package}" | awk '{print $1}')"
tools/m12-runtime-probe/run.sh \
  --repo "${repo_root}" \
  --feed "${repo_root}/artifacts/packages" \
  --record "${m12_record}" \
  --identity "${model_path}" \
  --tensorflow-fixture "${m12_fixture_directory}/m12-tensorflow-minimal.pb" \
  --calibration-map "${m12_fixture_directory}/m12-calibration-map.json" \
  --native "${resolved_library}" \
  --header "${header_path}" \
  --source-sha "${COMMIT_SHA}" \
  --version 0.0.0 \
  --core-sha "${m12_core_sha}" \
  --include-deferred 2>&1 | tee "${results}/m12-candidate-run.txt"
pwsh -NoProfile -File ./tools/m12-runtime-probe/review.ps1 \
  -RecordDirectory "${m12_record}" \
  -CorePackagePath "${m12_package}" \
  -TensorFlowFixturePath "${m12_fixture_directory}/m12-tensorflow-minimal.pb" \
  -CalibrationMapPath "${m12_fixture_directory}/m12-calibration-map.json" \
  -SourceSha "${COMMIT_SHA}" \
  -CoreSha256 "${m12_core_sha}" 2>&1 | tee "${results}/m12-review.txt"
printf '{"schemaVersion":"1.5.0","commit":"%s","managedGates":"completed","header":"verified","officialNativeM1":"runtime-executed","officialOnnxM2":"runtime-candidate-executed-review-required","gpuInference":"runtime-candidate-executed-review-required","m9InferenceOptions":"runtime-candidate-executed-review-required","m12Candidate":"candidate-record-verified","m12CaseFilter":"all-candidate","m12ExecutedCases":15,"m12FunctionalCases":14,"m12CrossTargetFrameworks":3,"m12DeferredCases":8}\n' "${COMMIT_SHA}" > "${results}/summary.json"
