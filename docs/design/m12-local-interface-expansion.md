# M12 local interface expansion

M12 is a local-development batch only. It adds managed projections for the remaining generated C declarations without making a package-publication or performance claim. The batch has passed local managed compilation, fake-native fixture compilation, focused M12 tests, representative interop execution, and project-quality tests. A separate cloud candidate probe exists for an earlier source/package identity, but real MIGraphX runtime verification remains review-required; it does not promote any M12 map entry, and the existing M3--M11 compatibility maps therefore remain the evidence baseline.

## Implemented local surface

The managed API now contains the following resource-safe projections:

- `MIGraphXShape.CreateScalar`, `CreateWithStrides`, `Ndim`, `Index`, `GetDimensionLength`, `Clone`, and snapshot equality; `MIGraphXShapeCollection` and `MIGraphXDynamicDimensionCollection` preserve independent managed snapshots, including `GetDynamicDimensionCollection`.
- `MIGraphXArgument.CreateEmpty`, `Generate`, `Load`, `Save`, and `Clone`; host-backed argument and collection/map clone paths preserve independent ownership, while public clone explicitly rejects borrowed device arguments.
- Native assign-to clone paths for `MIGraphXTarget`, `MIGraphXCompileOptions`, `MIGraphXFileOptions`, `MIGraphXOnnxOptions`, and `MIGraphXProgram`.
- `MIGraphXProgram.Print`, `Sort`, `IsEqual`, `GetMainModule`, `CreateModule`, TensorFlow file/buffer parsing, quantization mutation methods, and experimental context acquisition.
- `MIGraphXModule`, `MIGraphXInstruction`, `MIGraphXInstructions`, `MIGraphXModules`, and the construction-restricted `MIGraphXOperation` managed graph handles. Module parameter/literal/return/allocation and instruction-with-module-args entry points retain the parent program lease.
- `MIGraphXTfOptions` with NHWC, input-shape, default-dimension, output-name, clone, and strict path/buffer parsing support.
- `MIGraphXQuantizeOpNames`, `MIGraphXQuantizeInt8Options`, and `MIGraphXQuantizeFp8Options`, including calibration-map forwarding and assign-to clones.
- `MIGraphXContext` with finish/queue access and program-lifetime leasing; context calls and lease release are serialized. `MIGraphXExperimentalCustomOp` keeps copy/delete and user callback delegates rooted for the native registration lifetime, and clones by creating a new object and replaying the current callbacks.

All native `*_assign_to` declarations are treated as assignments into an already-created `T` handle, not as `T*` output-slot constructors. The managed clone helpers therefore create the destination first, invoke assign-to, and dispose that destination on failure. Collections additionally retain element/program leases so borrowed graph pointers cannot outlive their owners.

The local fake-native source now models the added ownership classes, graph views, TensorFlow options, quantization mutations, context access, custom-op callback ownership, targeted failures, and null-output creation. `M12LocalInterfaceTests.cs` records focused success, clone, lease, negative-boundary, concurrent-dispose, and cleanup contracts. The fixture was compiled with the local MSVC toolchain, all six focused M12 tests pass on `net10.0`, and the graph path executes through both generated interop families on the representative `net46`, `netcoreapp3.1`, `net7.0`, and `net10.0` runners.

Generated declarations remain source-of-truth and were not edited by hand. The new wrappers use the existing `NativeResourceOwner`, `OutHandle`, strict UTF-8, size_t, and native-library identity boundaries.

## Local completion boundary

For the frozen declarations that have an ownership-safe public boundary, the local surface now has a managed projection, a value-semantic clone/equality alias, or an explicit immutable snapshot boundary. The one non-variadic declaration intentionally kept out of the public owner surface is `migraphx_module_create`: the frozen C header exposes no matching `migraphx_module_destroy`, and the manifest classifies its returned pointer as having no inferable public managed lifetime. Module views therefore enter through program-owned main/create-module paths. The compatibility maps still remain unchanged because this statement is an implementation inventory, not runtime evidence.

## Local validation record

The following local checks pass for this batch:

- Fake MIGraphX and fake HIP native fixtures build successfully in Release configuration.
- The managed API and solution build successfully for all 15 exact target frameworks, with zero warnings and errors; ProjectQuality also builds successfully for `net10.0`.
- `M12LocalInterfaceTests` passes all six focused tests, covering shape/argument factories, graph/context views, assign-to clones, TensorFlow/quantization paths, custom-op cloning, negative construction boundaries, and concurrent module disposal.
- ProjectQuality tests pass all 24 tests, including the current core API snapshot of 44 types and 282 members, bilingual XML documentation, generated binding traceability, and ownership/audit checks.

These results are local compile and test-substitute evidence. They do not promote M12 declarations in the historical compatibility maps or establish behavior against a real MIGraphX installation.

## Deliberately deferred

The following items still need a separate design or an authorized validation fixture before they can be promoted in the compatibility maps:

- `migraphx_operation_create` remains unsupported because the frozen C declaration is variadic. `MIGraphXOperation` has no public constructor or clone until a safe native graph path can supply an owned operation handle.
- `migraphx_module_create` remains outside the public owner surface because the frozen declaration has no matching module-destroy operation; exposing it would require inventing an ownership contract.
- Native semantic coverage for loop iteration behavior, real external-data payloads, enabled exhaustive tuning, representative fast-math accuracy, quantization numerical results, TensorFlow model variants, custom-op callback execution, and context queue behavior is not established.
- Low-level borrowed collection readback and dynamic-dimension native getter coverage remain represented by immutable managed snapshots rather than exposing dangling pointers.
- The M11 official functional, restart, long-run, timing, and Windows policy records are unchanged and remain validation-deferred.

The next validation batch should follow the [M12 real-runtime validation plan](../validation/m12-runtime-validation-plan.md), exercise semantic workflows against an authorized real MIGraphX installation, and review native ownership/callback behavior before any interface moves from planned/local-development to supported in a compatibility map.

中文摘要：M12 已完成本地接口、fake-native 夹具和测试源码开发，并通过 fake-native 原生编译、15 个目标框架托管编译、6 项 M12 focused tests 与 24 项 ProjectQuality tests；图编辑路径已在 `net46`、`netcoreapp3.1`、`net7.0`、`net10.0` 代表 interop runner 上执行。没有执行云端或真实 native runtime promotion。新增 shape/argument 创建复制、program/module/instruction 图编辑、TensorFlow、量化、context/custom-op 与多个 assign-to clone；host-backed argument 可独立 clone，borrowed device argument 的 public clone 明确拒绝；custom-op clone 采用重新创建并重放回调。C 可变参数 `migraphx_operation_create` 仍 unsupported；`migraphx_module_create` 因冻结头文件没有对应 module destroy、无法推断 public ownership，也不暴露独立 owner；量化数值、TF 模型、custom-op 回调执行、context queue、M11 长跑/计时及官方运行证据均保持延期。
