# M2 restricted ONNX workflow

M2 extends the frozen ROCm 7.2.1 C API subset from 6 to 41 cumulative functions. `compatibility/m2-binding-subset.json` remains the single semantic source for both `LibraryImport` and `DllImport` declarations and records C enum, `size_t`, one-byte C `bool`, pointer, UTF-8, and ownership decisions.

## Public boundary

`MIGraphXOnnxWorkflow` exposes file and byte-buffer entry points for exactly one static, standard, float32 input and one static, standard, float32 output. It creates ONNX and compile options, parses a program, inspects shapes, compiles for an explicit target with offload-copy enabled, pins the input only through synchronous `program_run`, and copies output before destroying the native result collection.

Dynamic dimensions, multiple inputs or outputs, non-float32 tensors, non-standard layouts, async/stream APIs, device buffers, save/load, and a general public Program/Shape/Argument object model are rejected or remain outside M2.

## Ownership

ONNX options, program, target, compile options, parameter-shape collection, input argument, parameter map, output-shape collection, and run-result collection are owned and disposed in reverse lifetime order. Shapes returned from collections and arguments returned from run results are borrowed and never destroyed independently. Parameter-map add copies the native argument value; the managed input array stays pinned until the synchronous run returns.

## Reproducible model and evidence

`eng/generate-m2-model.ps1` writes a 128-byte ONNX IR 8, opset 13 Identity graph with `float32[1,4]` input/output. Its SHA-256 is `0b6fa0302a08a3fccf375d8ce4f84b7da59ccfa742fc59a0baa5f31722ae75f9`. The generated binary is ignored and excluded from packages.

Local fake-native execution validates the managed ABI and behavior but is not official MIGraphX or AMD GPU evidence. The official session at `346cdd0b01a7f8039f5deb93058928403fccc7dd` revalidated file and buffer parse, static shape inspection, `gpu` compile with offload-copy, synchronous run, and exact Identity reference comparison on one gfx1100 device. The result is bounded to the recorded environment and model.

中文摘要：M2 公开面仅覆盖单输入、单输出、静态且连续的 float32 ONNX 同步 offload-copy 路径。所有 owned/borrowed 生命周期都按固定实现证据处理；本地 fake 与精确 SHA 的官方 MIGraphX/gfx1100 证据分层记录。
