# M2 ONNX Identity sample

Generate the deterministic Identity model, then pass absolute paths for both the native MIGraphX C library and model:

```powershell
.\eng\generate-m2-model.ps1
dotnet run --project .\samples\M2OnnxIdentity\M2OnnxIdentity.csproj -c Release -- `
  C:\absolute\path\to\migraphx_c.dll `
  $PWD\artifacts\models\m2-identity-float32.onnx
```

The sample executes the restricted single-input, single-output, static float32 synchronous workflow. It does not download native binaries, modify loader environment variables, or imply official GPU validation.

中文说明：先生成确定性的 Identity 模型，再显式传入原生库和模型的绝对路径。本示例不下载原生库、不修改加载环境，也不代表官方 GPU 验证已经完成。
