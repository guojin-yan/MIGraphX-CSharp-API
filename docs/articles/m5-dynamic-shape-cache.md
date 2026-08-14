# 动态 Shape 与模型缓存：MIGraphXSharp 的工程化推理路径

动态输入不是把一个 `long[]` 换成另一个 `long[]`。范围、optimal 值、native collection 的 borrowed 元素和最终 concrete argument 处于不同生命周期。M5 将范围建模为不可变托管值；只有选择 concrete shape 后，typed host buffer 才能验证元素数和字节数。

缓存同样不是“文件存在就命中”。固定 header/API identity、托管构建、native 文件 fingerprint、target、compile options、格式和有序 override 共同决定内容身份。JSON sidecar 记录这些字段并绑定 payload hash；不完整或不匹配的条目会重建。显式根目录和同目录原子替换让部署可以审计，也避免进程全局状态。

当前证据边界是 fake-native：它验证了资源释放、失败清理、动态 override 快照和 Save/Load 生命周期，但没有把结论扩大到官方 ONNX frontend、跨版本缓存或新的官方 runtime 执行。
