# M0 native input baseline

## Frozen target

- ROCm release: `7.2.1`
- MIGraphX package: `2.15.0.70201-81~24.04`
- Upstream tag: `rocm-7.2.1`
- Peeled source commit: `de19b73ad280476e646512b847885eda100ec35e`
- Acquisition date: 2026-08-13

The baseline uses AMD's official Ubuntu 24.04 repository packages and a fixed release tag. It does not use the moving `develop` branch.

## Official inputs

| Input | Official URL | SHA-256 |
| --- | --- | --- |
| `migraphx-dev_2.15.0.70201-81~24.04_amd64.deb` | [AMD repository](https://repo.radeon.com/rocm/apt/7.2.1/pool/main/m/migraphx-dev/migraphx-dev_2.15.0.70201-81~24.04_amd64.deb) | `ba930c986539015f4a7a651e1c89ade6c7e2d1cf695c3e1ff89903c0601d9019` |
| Installed `/opt/rocm-7.2.1/include/migraphx/migraphx.h` | Extracted from the development package | `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2` |
| Same header at fixed commit | [AMD source](https://raw.githubusercontent.com/ROCm/AMDMIGraphX/de19b73ad280476e646512b847885eda100ec35e/src/api/include/migraphx/migraphx.h) | `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2` |
| `migraphx_2.15.0.70201-81~24.04_amd64.deb` | [AMD repository](https://repo.radeon.com/rocm/apt/7.2.1/pool/main/m/migraphx/migraphx_2.15.0.70201-81~24.04_amd64.deb) | `cf0381824856c7181cfc45db415c1d25a98625090cf06de98de564693c02a01e` |
| `libmigraphx_c.so.3.0.70201` | Extracted from the runtime package | `3b012a738306e2d4499d0aa0dce7b73f96a96209ade45369ad9194c208801aff` |

Both package hashes matched the AMD repository index. The installed header and the fixed source-commit header are byte-identical.

## Header inventory

Static parsing found 159 public `migraphx_status` functions, 2 enums, and 25 opaque handles. The header contains ONNX, program, shape, argument, target, options, program-parameter map, synchronous run, and asynchronous run families.

This inventory is a generation input, not a managed binding claim. Managed coverage is zero in M0.

## Native library facts

- Logical C API library: `migraphx_c`
- Package file: `/opt/rocm-7.2.1/lib/libmigraphx_c.so.3.0.70201`
- ELF SONAME: `libmigraphx_c.so.3`
- `migraphx_` ELF exports: 160, including the 159 public-header functions plus a private test export
- Key exports present: `migraphx_parse_onnx`, `migraphx_parse_onnx_buffer`, `migraphx_program_create`, `migraphx_program_run`, `migraphx_program_run_async`

Direct ELF dependencies are `libmigraphx_tf.so.2015000`, `libmigraphx_onnx.so.2015000`, `libmigraphx.so.2015000`, `libstdc++.so.6`, `libm.so.6`, `libgcc_s.so.1`, `libc.so.6`, and `ld-linux-x86-64.so.2`. These are not a complete redistributable closure.

## ONNX evidence boundary

The installed header contains ONNX declarations, the C library directly depends on `libmigraphx_onnx.so.2015000`, and ONNX parse exports exist. These are static facts. No native loader was invoked and no ONNX model was parsed, compiled, or run, so runtime ONNX status remains `planned`.

## Redistribution boundary

The managed project is licensed under Apache-2.0. That decision does not license AMD components: the official package contains AMD license material, but file-by-file redistribution obligations, the complete transitive native closure, package size, and clean-runtime behavior have not been audited. No AMD binary is stored in this repository or candidate core package. Runtime packaging remains disabled and fail closed.
