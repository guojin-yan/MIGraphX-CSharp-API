# MIGraphXSharp documentation

MIGraphXSharp `0.0.0` contains the M1 Direct P/Invoke subset for six target/program lifecycle functions from the official AMD MIGraphX C API. The declarations, strict UTF-8 path, status/exception semantics, loader diagnostics, and internal SafeHandles are locally verified across the 15-TFM build.

Local fake-native execution is a test substitute. It does not prove that the official `libmigraphx_c.so.3`, an AMD GPU, ONNX parsing, compilation, or inference works. Official M1 runtime execution remains planned until an authorized Radeon Cloud session checks a pushed SHA.

Start with [Getting started](guides/getting-started.md), then review [M1 Direct P/Invoke](design/m1-direct-pinvoke.md), [platform evidence](compatibility/platforms.md), and [validation evidence](validation/README.md).

中文摘要：M1 已完成六个基础入口的同源 Direct P/Invoke、本地 loader/fake-native/ABI/package 门禁；本地替身不能代替官方 MIGraphX runtime。由于尚未获得 Radeon Cloud 当次授权，阶段状态仍为 blocked。
