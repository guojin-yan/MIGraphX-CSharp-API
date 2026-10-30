#!/usr/bin/env bash
# Source after setting repo_root. Keep local/cloud output outside the checkout.
workspace_root="${MIGRAPHX_WORKSPACE_ROOT:-$(dirname "${repo_root}")/MIGraphXSharp.Workspace}"
[[ "${workspace_root}" = /* ]] || { echo 'MIGRAPHX_WORKSPACE_ROOT must be absolute' >&2; exit 1; }
workspace_root="$(realpath -m "${workspace_root}")"
case "${workspace_root}/" in
  "${repo_root}/"*) echo 'MIGRAPHX_WORKSPACE_ROOT must be outside the source repository' >&2; exit 1 ;;
esac
export MIGRAPHX_WORKSPACE_ROOT="${workspace_root}"
