# Graph, TensorFlow, quantization and experimental APIs

These APIs are implemented, but full real-runtime semantics remain partially qualified. Consult [platform support](../compatibility/platforms.md) and validate the selected model and native target before deployment.

## Graph views

Create a `MIGraphXProgram`, obtain its `GetMainModule()` or `CreateModule(name)` view, add parameters, literals and instructions, and add a return instruction before compilation. Use `MIGraphXOperation` with [typed materialized attributes](operation-attributes.md).

`MIGraphXModule`, `MIGraphXInstruction` and their collections retain program leases. Dispose views when finished. The wrapper exposes program-owned modules because the fixed C ABI provides no matching destroy/ownership contract for an independently created `migraphx_module_create` owner.

Module views from the same program share its lock for graph edits and native calls. Adding parameters, allocations, literals, returns or instructions invalidates `IsCompiled`; compile again before running or acquiring a context. Native mutation failures also invalidate the compiled flag because the provider may have partially changed the program. A cloned program has independent state. This serialization covers managed API calls; wait for pending asynchronous work before editing or recompiling its program.

## TensorFlow

`MIGraphXTfOptions` configures input shapes, layout and output names. Pass it to `MIGraphXProgram.ParseTfFile` or `ParseTfBuffer`. File paths must be absolute; buffer inputs must be non-empty. Options own native resources and must be disposed. Declared parsing entry points do not guarantee support for every TensorFlow graph.

## Quantization

`MIGraphXProgram` exposes `QuantizeFp16`, `QuantizeBf16`, `QuantizeInt8` and `QuantizeFp8`. Configure operation-name filters and the appropriate quantization options, including calibration inputs where required, before compiling the modified program. Quantization changes numeric behavior; check accuracy on representative data and confirm the target supports the selected format.

## Context

`GetExperimentalContext` exposes a program-bound context with `Finish` and queue access. The context retains its parent lease. Queue access does not transfer ownership or provide general raw-pointer interoperation with the HIP adapter.

A context belongs to the program revision from which it was acquired. Graph edits, sorting, module creation and compilation attempts invalidate previously acquired contexts. After recompiling, acquire a new context; old views reject access even when the new compilation succeeds. Disposing the caller's program alone does not invalidate a retained context for an otherwise unchanged revision.

## Custom operations

`MIGraphXExperimentalCustomOp` registers managed callbacks for native custom-operation behavior. Callback roots remain alive while native ownership requires them; callback exceptions are translated to `UnknownError` with bounded UTF-8 error text when the ABI supplies a buffer.

This is an experimental raw callback API. Follow each callback's XML documentation for pointer and ownership contracts. Host-reference callback diagnostics do not establish GPU/device-buffer handling, every callback sequence, or complete provider lifecycle support.

中文提示：图视图依附 program，不能独立创建没有销毁契约的 module owner。TensorFlow、量化与自定义算子的完整真实语义仍需验证，尤其要确认设备内存与回调生命周期。

修改图后必须重新编译；原生修改失败时也不再沿用旧的已编译标志。旧 context 随图修改、排序、创建 module 或重新编译失效，需要从重新编译的 program 获取新视图。instruction 及集合持有的租约会使父 program 保持存活，直到最后一个引用释放。
