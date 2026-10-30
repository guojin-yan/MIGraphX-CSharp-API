# Reproducible test fixtures

Run the generators from the repository root with PowerShell 7:

| Script | Output |
| --- | --- |
| `./tests/fixtures/generate-onnx-identity.ps1` | Static float32 Identity ONNX model |
| `./tests/fixtures/generate-onnx-workflows.ps1` | Static Identity, ordered multi-output and dynamic Identity ONNX models |
| `./tests/fixtures/generate-tensorflow-quantization.ps1` | Minimal TensorFlow GraphDef and calibration map |

Outputs default to the external workspace's `artifacts/models/` tree. Use `-OutputPath` for a single model or `-OutputDirectory` for a fixture set. Supply an absolute location outside the checkout when overriding output.

Fixture bytes, filenames and original generator metadata remain fixed so existing hash-bound runtime records stay comparable. Metadata identifies the original artifact format; use the commands above for current invocation paths. Local generation does not qualify TensorFlow, quantization, dynamic shapes or GPU runtime behavior.

The `package-consumers/` and `adapter-package-consumer/` directories contain source-only consumer projects for package checks.

中文说明：本目录保存测试输入的生成源码。生成的模型、校准数据及消费项目输出写入仓库外；固定模型字节和元数据用于保持既有哈希契约，不代表真实运行时已经通过验证。
