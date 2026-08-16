# M9 article draft: from declarations to cloud evidence

MIGraphX already exposed the five M9 C declarations, and M3 had generated their exact dual interop signatures. That was only ABI coverage. M9 adds the missing managed decisions: which values are valid, which paths must be absolute, how UTF-8 is scoped, how partial option construction unwinds, and which optimization flags are visible to callers.

The local substitute is useful because it can deterministically inject a failure at every setter and count native owners. It answers whether the wrapper forwards the requested value and releases a partially configured object. It cannot answer whether ROCm accepts the option, whether a Loop model obeys the limit, whether external tensors are resolved correctly, or whether fast-math stays within a useful accuracy bound.

The cloud runner deliberately reported a candidate, not a conclusion. At pushed SHA `346cdd0b01a7f8039f5deb93058928403fccc7dd`, it bound the clean checkout, header and ELF identity, generated model hash, public option values, GPU compile/run, output, and reference comparison into JSON. After transferred-hash verification and independent field review, setter acceptance and the Identity integration path were promoted to `runtime-executed`. Separate Loop and external-data fixtures, an exhaustive-tune budget, and a representative accuracy corpus remain visible follow-up work instead of disappearing behind one green Identity run.

中文摘要：本批次展示了“已有 P/Invoke 声明”与“可公开、可测试、可写文章的高层接口”之间仍需要参数、ownership、失败清理和证据边界设计。local substitute 与 `346cdd0...` 的云端 official runtime 记录各自回答不同问题，不能相互冒充。
