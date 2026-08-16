# M3 binding generator and low-level coverage

M3 turns the frozen ROCm 7.2.1 MIGraphX C header into a reproducible internal binding pipeline. The source is generated `migraphx.h` SHA-256 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2`, 36,635 bytes, from MIGraphX `2.15.0.70201-81~24.04`.

## Pipeline

`eng/generate-interop.ps1` validates the input hash and length, preprocesses it with a recorded C configuration and token-only standard-header stubs, then invokes the .NET generator. Its lexer and balanced declaration parser create a normalized model before any C# is emitted; regular expressions are not used as a complete C declaration parser.

The normalized model records stable IDs, canonical C declarations and typedef shapes, enum values, pointer/const depth, inferred array-length relations, nullability, encoding, ownership/lifetime annotations, callback convention and retention policy, and frozen source locations. M1/M2's reviewed 41-function semantic projection overlays that parsed model so the existing EntryPoint, direction, UTF-8, bool, ownership, and error behavior does not drift.

The same model emits both declaration paths:

- .NET 7 and later: generated `LibraryImport`;
- older TFMs: generated `DllImport`;
- both: Cdecl, identical EntryPoint sets, `UIntPtr` for `size_t`, one-byte C `bool`, and raw `IntPtr` for unpromoted pointer/handle ABI.

M3 keeps every new declaration internal. It does not expose M4's planned `Program`, `Shape`, `Argument`, `Target`, options, or collection object model.

## Closed classification

| Kind | Total | Generated | Handwritten policy | Unsupported | Configuration unavailable |
| --- | ---: | ---: | ---: | ---: | ---: |
| Functions | 159 | 117 | 41 | 1 | 0 |
| Enums | 2 | 2 | 0 | 0 | 0 |
| Opaque handles | 25 | 25 | 0 | 0 | 0 |
| Callbacks | 6 | 0 | 6 | 0 | 0 |
| Overall | 192 | 144 | 47 | 1 | 0 |

The one unsupported function is `migraphx_operation_create`. Its frozen declaration is C variadic; neither `LibraryImport` nor a guessed fixed `DllImport` signature can represent the complete ABI safely. It remains present in inventory and official ELF parity but has no managed EntryPoint.

All six callbacks receive generated Cdecl delegate signatures plus explicit handwritten retention and exception-boundary policy. The policy does not turn experimental custom operations into a public supported feature.

## Determinism and failure behavior

Generated JSON uses a fixed injectable stamp, relative paths, sorted stable inputs, and UTF-8 without a BOM. Generation writes into a system temporary directory, stages every target, and replaces targets with rollback backups. Verify mode compares all six products byte for byte without writing tracked files. A bad header, incomplete callback policy, missing unsupported classification, count drift, enum drift, or output drift exits nonzero.

The machine-readable sources are `compatibility/m3-normalized-api.json`, `compatibility/m3-api-inventory.json`, `compatibility/m3-coverage-summary.json`, `compatibility/m3-handwritten-overrides.json`, and `compatibility/m3-unsupported.json`.

## Evidence boundary

The hash-verified official ELF SHA-256 is `3b012a738306e2d4499d0aa0dce7b73f96a96209ade45369ad9194c208801aff`. It exports all 159 header functions plus one separately classified private test symbol. The two 158-EntryPoint managed paths and the ABI probe are `statically-verified`. The minimal callback/bool/size_t/UTF-8/pointer substitute is `fake-native-executed`.

Official execution remains declaration-specific: at `346cdd0b01a7f8039f5deb93058928403fccc7dd`, the reviewed M1/M2 paths and five later M9 setters ran. This does not promote the rest of the generated M3 inventory.

中文摘要：M3 用结构化解析器把固定头归一化为 192 项闭合 inventory，再同源生成 158 个双路径 EntryPoint。唯一 C 可变参数函数显式 unsupported；新增低层声明只有静态或 fake-native 证据，不扩大 M1/M2 官方 runtime 结论。
