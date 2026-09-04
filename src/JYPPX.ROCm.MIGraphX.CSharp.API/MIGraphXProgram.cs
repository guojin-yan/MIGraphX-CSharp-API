using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示拥有 MIGraphX program handle 的资源安全同步程序对象。
/// Represents a resource-safe synchronous program object that owns a MIGraphX program handle.
/// </summary>
public sealed class MIGraphXProgram : IDisposable
{
    private readonly NativeResourceOwner<NativeProgramHandle> owner;
    private readonly IReadOnlyDictionary<string, MIGraphXDynamicDimension[]> dynamicOverrides;
    private bool compiled;
    private bool compiledOffloadCopy;

    /// <summary>
    /// 使用显式原生库创建空 program。
    /// Creates an empty program using an explicit native library.
    /// </summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    public MIGraphXProgram(string nativeLibraryPath)
    {
        var runtime = NativeRuntime.Load(nativeLibraryPath);
        owner = new NativeResourceOwner<NativeProgramHandle>(runtime, NativeProgramHandle.Create());
        dynamicOverrides = new Dictionary<string, MIGraphXDynamicDimension[]>(StringComparer.Ordinal);
    }

    private MIGraphXProgram(NativeRuntime runtime, NativeProgramHandle handle, IReadOnlyDictionary<string, MIGraphXDynamicDimension[]>? dynamicOverrides = null)
    {
        owner = new NativeResourceOwner<NativeProgramHandle>(runtime, handle);
        this.dynamicOverrides = dynamicOverrides ?? new Dictionary<string, MIGraphXDynamicDimension[]>(StringComparer.Ordinal);
    }

    /// <summary>获取 program 是否已由此对象成功编译；Dispose 后访问会失败。 Gets whether this object successfully compiled the program; access fails after disposal.</summary>
    public bool IsCompiled => owner.WithHandle(_ => compiled);

    /// <summary>
    /// 从绝对 ONNX 文件路径解析 owned program。
    /// Parses an owned program from an absolute ONNX file path.
    /// </summary>
    /// <param name="modelPath">ONNX 模型绝对路径。 Absolute ONNX model path.</param>
    /// <param name="options">显式 ONNX options 实例。 Explicit ONNX options instance.</param>
    /// <returns>新 owned program。 A new owned program.</returns>
    public static MIGraphXProgram ParseOnnxFile(string modelPath, MIGraphXOnnxOptions options)
    {
        if (options is null) { throw new ArgumentNullException(nameof(options)); }
        if (modelPath is null) { throw new ArgumentNullException(nameof(modelPath)); }
        if (!Path.IsPathRooted(modelPath)) { throw new ArgumentException("The ONNX model path must be absolute.", nameof(modelPath)); }
        var fullPath = Path.GetFullPath(modelPath);
        if (!File.Exists(fullPath)) { throw new FileNotFoundException("The ONNX model file does not exist.", fullPath); }
        using (var path = new StrictUtf8String(fullPath, nameof(modelPath)))
        {
            return options.Owner.WithHandle(handle => new MIGraphXProgram(
                options.Owner.Runtime,
                NativeProgramHandle.ParseFile(path.Pointer, handle),
                options.DynamicOverrides));
        }
    }

    /// <summary>
    /// 从非空 ONNX protobuf 字节解析 owned program。
    /// Parses an owned program from non-empty ONNX protobuf bytes.
    /// </summary>
    /// <param name="model">解析调用期间固定的 ONNX 字节。 ONNX bytes pinned for the parse call.</param>
    /// <param name="options">显式 ONNX options 实例。 Explicit ONNX options instance.</param>
    /// <returns>新 owned program。 A new owned program.</returns>
    public static MIGraphXProgram ParseOnnxBuffer(byte[] model, MIGraphXOnnxOptions options)
    {
        if (options is null) { throw new ArgumentNullException(nameof(options)); }
        if (model is null) { throw new ArgumentNullException(nameof(model)); }
        if (model.Length == 0) { throw new ArgumentException("The ONNX model buffer must not be empty.", nameof(model)); }
        var pinned = GCHandle.Alloc(model, GCHandleType.Pinned);
        try
        {
            return options.Owner.WithHandle(handle => new MIGraphXProgram(
                options.Owner.Runtime,
                NativeProgramHandle.ParseBuffer(pinned.AddrOfPinnedObject(), new UIntPtr((uint)model.Length), handle),
                options.DynamicOverrides));
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>从绝对 TensorFlow GraphDef 文件解析 program。 Parses a program from an absolute TensorFlow GraphDef file.</summary>
    /// <param name="modelPath">绝对模型文件路径。 Absolute model-file path.</param>
    /// <param name="options">TensorFlow 解析选项。 TensorFlow parsing options.</param>
    public static MIGraphXProgram ParseTfFile(string modelPath, MIGraphXTfOptions options)
    {
        if (options is null) { throw new ArgumentNullException(nameof(options)); }
        var fullPath = MIGraphXTfOptions.ValidateInputPath(modelPath, nameof(modelPath));
        using (var path = new StrictUtf8String(fullPath, nameof(modelPath)))
        {
            return options.Owner.WithHandle(handle => new MIGraphXProgram(options.Owner.Runtime, NativeProgramHandle.ParseTfFile(path.Pointer, options.Owner.HandleUnderLock)));
        }
    }

    /// <summary>从非空 TensorFlow GraphDef 字节解析 program。 Parses a program from a non-empty TensorFlow GraphDef buffer.</summary>
    /// <param name="model">GraphDef 字节。 GraphDef bytes.</param>
    /// <param name="options">TensorFlow 解析选项。 TensorFlow parsing options.</param>
    public static MIGraphXProgram ParseTfBuffer(byte[] model, MIGraphXTfOptions options)
    {
        if (options is null) { throw new ArgumentNullException(nameof(options)); }
        if (model is null) { throw new ArgumentNullException(nameof(model)); }
        if (model.Length == 0) { throw new ArgumentException("The TensorFlow model buffer must not be empty.", nameof(model)); }
        var pinned = GCHandle.Alloc(model, GCHandleType.Pinned);
        try
        {
            return options.Owner.WithHandle(handle => new MIGraphXProgram(
                options.Owner.Runtime,
                NativeProgramHandle.ParseTfBuffer(pinned.AddrOfPinnedObject(), NativeSizeTArray.Count(model.Length), options.Owner.HandleUnderLock)));
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>获取 program 的 main module 视图；返回对象保持 program 存活。 Gets the main module view while keeping the program alive.</summary>
    public MIGraphXModule GetMainModule()
    {
        var module = owner.WithHandle(program =>
        {
            var slot = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(slot, IntPtr.Zero);
                NativeStatus.ThrowIfFailed(NativeMethods.ProgramGetMainModule(slot, program), "migraphx_program_get_main_module");
                var pointer = Marshal.ReadIntPtr(slot);
                if (pointer == IntPtr.Zero) throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, "migraphx_program_get_main_module (success with null module)");
                var lease = owner.AcquireLease();
                try { return new MIGraphXModule(owner.Runtime, lease, pointer); }
                catch { lease.Dispose(); throw; }
            }
            finally { Marshal.FreeHGlobal(slot); }
        });
        return module;
    }

    /// <summary>在 program 中创建命名 module；返回对象保持 program 存活。 Creates a named module while keeping the program alive through the returned view.</summary>
    /// <param name="name">module 名称。 Module name.</param>
    public MIGraphXModule CreateModule(string name)
    {
        using (var utf8 = new StrictUtf8String(name, nameof(name)))
        {
            var module = owner.WithHandle(program =>
            {
                var slot = Marshal.AllocHGlobal(IntPtr.Size);
                try
                {
                    Marshal.WriteIntPtr(slot, IntPtr.Zero);
                    NativeStatus.ThrowIfFailed(NativeMethods.ProgramCreateModule(slot, program, utf8.Pointer), "migraphx_program_create_module");
                    var pointer = Marshal.ReadIntPtr(slot);
                    if (pointer == IntPtr.Zero) throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, "migraphx_program_create_module (success with null module)");
                    var lease = owner.AcquireLease();
                    try
                    {
                        var result = new MIGraphXModule(owner.Runtime, lease, pointer);
                        compiled = false;
                        return result;
                    }
                    catch { lease.Dispose(); throw; }
                }
                finally { Marshal.FreeHGlobal(slot); }
            });
            return module;
        }
    }

    /// <summary>
    /// 使用显式 target 与 compile options 同步编译 program。
    /// Synchronizes compilation of the program with explicit target and compile options.
    /// </summary>
    /// <param name="target">同一原生库创建的 target。 Target created from the same native library.</param>
    /// <param name="options">同一原生库创建的 compile options。 Compile options created from the same native library.</param>
    public void Compile(MIGraphXTarget target, MIGraphXCompileOptions options)
    {
        if (target is null) { throw new ArgumentNullException(nameof(target)); }
        if (options is null) { throw new ArgumentNullException(nameof(options)); }
        owner.Runtime.RequireSame(target.Owner.Runtime, nameof(target));
        owner.Runtime.RequireSame(options.Owner.Runtime, nameof(options));
        NativeResourceLock.With(
            new[]
            {
                NativeResourceLock.Target(owner.Id, owner.Sync),
                NativeResourceLock.Target(target.Owner.Id, target.Owner.Sync),
                NativeResourceLock.Target(options.Owner.Id, options.Owner.Sync),
            },
            () =>
            {
                NativeStatus.ThrowIfFailed(
                    NativeMethods.ProgramCompile(owner.HandleUnderLock, target.Owner.HandleUnderLock, options.Owner.HandleUnderLock),
                    "migraphx_program_compile");
                compiled = true;
                compiledOffloadCopy = options.OffloadCopy;
                return 0;
            });
    }

    /// <summary>调用 native program print。 Prints the native program representation.</summary>
    public void Print() => owner.WithHandle(handle => NativeStatus.ThrowIfFailed(NativeMethods.ProgramPrint(handle), "migraphx_program_print"));

    /// <summary>对 program 执行 native sort，并使已编译标志失效。 Sorts the native program and invalidates its compiled state.</summary>
    public void Sort()
    {
        owner.WithHandle(handle =>
        {
            NativeStatus.ThrowIfFailed(NativeMethods.ProgramSort(handle), "migraphx_program_sort");
            compiled = false;
        });
    }

    /// <summary>使用 native <c>program_equal</c> 比较两个 program。 Compares two programs using native <c>program_equal</c> semantics.</summary>
    /// <param name="other">待比较的 program。 Program to compare.</param>
    public bool IsEqual(MIGraphXProgram other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        owner.Runtime.RequireSame(other.owner.Runtime, nameof(other));
        owner.Runtime.RequireM10Equality();
        return NativeResourceLock.With(
            new[] { NativeResourceLock.Target(owner.Id, owner.Sync), NativeResourceLock.Target(other.owner.Id, other.owner.Sync) },
            () => NativeM10Methods.ProgramContentEquals(owner.HandleUnderLock, other.owner.HandleUnderLock));
    }

    /// <summary>将 program 量化为 FP16；传入名称集合时仅处理指定 operator。 Quantizes the program to FP16, optionally limiting processing to named operators.</summary>
    /// <param name="opNames">可选 operator 名称集合。 Optional operator-name collection.</param>
    /// <exception cref="InvalidOperationException">program 已编译；量化必须在编译前执行。 The program is compiled; quantization must run before compilation.</exception>
    public void QuantizeFp16(MIGraphXQuantizeOpNames? opNames = null)
    {
        if (opNames is null)
        {
            owner.WithHandle(handle =>
            {
                RequireUncompiledForQuantizationUnderLock();
                NativeStatus.ThrowIfFailed(NativeMethods.QuantizeFp16(handle), "migraphx_quantize_fp16");
                compiled = false;
            });
        }
        else
        {
            owner.Runtime.RequireSame(opNames.Owner.Runtime, nameof(opNames));
            NativeResourceLock.With(
                new[] { NativeResourceLock.Target(owner.Id, owner.Sync), NativeResourceLock.Target(opNames.Owner.Id, opNames.Owner.Sync) },
                () =>
                {
                    RequireUncompiledForQuantizationUnderLock();
                    NativeStatus.ThrowIfFailed(NativeMethods.QuantizeFp16WithOpNames(owner.HandleUnderLock, opNames.Owner.HandleUnderLock), "migraphx_quantize_fp16_with_op_names");
                    compiled = false;
                });
        }
    }

    /// <summary>将 program 量化为 BF16；传入名称集合时仅处理指定 operator。 Quantizes the program to BF16, optionally limiting processing to named operators.</summary>
    /// <param name="opNames">可选 operator 名称集合。 Optional operator-name collection.</param>
    /// <exception cref="InvalidOperationException">program 已编译；量化必须在编译前执行。 The program is compiled; quantization must run before compilation.</exception>
    public void QuantizeBf16(MIGraphXQuantizeOpNames? opNames = null)
    {
        if (opNames is null)
        {
            owner.WithHandle(handle =>
            {
                RequireUncompiledForQuantizationUnderLock();
                NativeStatus.ThrowIfFailed(NativeMethods.QuantizeBf16(handle), "migraphx_quantize_bf16");
                compiled = false;
            });
        }
        else
        {
            owner.Runtime.RequireSame(opNames.Owner.Runtime, nameof(opNames));
            NativeResourceLock.With(
                new[] { NativeResourceLock.Target(owner.Id, owner.Sync), NativeResourceLock.Target(opNames.Owner.Id, opNames.Owner.Sync) },
                () =>
                {
                    RequireUncompiledForQuantizationUnderLock();
                    NativeStatus.ThrowIfFailed(NativeMethods.QuantizeBf16WithOpNames(owner.HandleUnderLock, opNames.Owner.HandleUnderLock), "migraphx_quantize_bf16_with_op_names");
                    compiled = false;
                });
        }
    }

    /// <summary>使用 INT8 选项量化 program。 Quantizes the program using INT8 options.</summary>
    /// <param name="target">量化目标。 Quantization target.</param>
    /// <param name="options">INT8 量化选项。 INT8 quantization options.</param>
    /// <exception cref="InvalidOperationException">program 已编译；量化必须在编译前执行。 The program is compiled; quantization must run before compilation.</exception>
    public void QuantizeInt8(MIGraphXTarget target, MIGraphXQuantizeInt8Options options)
    {
        if (target is null) { throw new ArgumentNullException(nameof(target)); }
        if (options is null) { throw new ArgumentNullException(nameof(options)); }
        owner.Runtime.RequireSame(target.Owner.Runtime, nameof(target));
        owner.Runtime.RequireSame(options.Owner.Runtime, nameof(options));
        NativeResourceLock.With(
            new[]
            {
                NativeResourceLock.Target(owner.Id, owner.Sync),
                NativeResourceLock.Target(target.Owner.Id, target.Owner.Sync),
                NativeResourceLock.Target(options.Owner.Id, options.Owner.Sync),
            },
            () =>
            {
                RequireUncompiledForQuantizationUnderLock();
                NativeStatus.ThrowIfFailed(NativeMethods.QuantizeInt8(owner.HandleUnderLock, target.Owner.HandleUnderLock, options.Owner.HandleUnderLock), "migraphx_quantize_int8");
                compiled = false;
            });
    }

    /// <summary>使用 FP8 选项量化 program。 Quantizes the program using FP8 options.</summary>
    /// <param name="target">量化目标。 Quantization target.</param>
    /// <param name="options">FP8 量化选项。 FP8 quantization options.</param>
    /// <exception cref="InvalidOperationException">program 已编译；量化必须在编译前执行。 The program is compiled; quantization must run before compilation.</exception>
    public void QuantizeFp8(MIGraphXTarget target, MIGraphXQuantizeFp8Options options)
    {
        if (target is null) { throw new ArgumentNullException(nameof(target)); }
        if (options is null) { throw new ArgumentNullException(nameof(options)); }
        owner.Runtime.RequireSame(target.Owner.Runtime, nameof(target));
        owner.Runtime.RequireSame(options.Owner.Runtime, nameof(options));
        NativeResourceLock.With(
            new[]
            {
                NativeResourceLock.Target(owner.Id, owner.Sync),
                NativeResourceLock.Target(target.Owner.Id, target.Owner.Sync),
                NativeResourceLock.Target(options.Owner.Id, options.Owner.Sync),
            },
            () =>
            {
                RequireUncompiledForQuantizationUnderLock();
                NativeStatus.ThrowIfFailed(NativeMethods.QuantizeFp8(owner.HandleUnderLock, target.Owner.HandleUnderLock, options.Owner.HandleUnderLock), "migraphx_quantize_fp8");
                compiled = false;
            });
    }

    /// <summary>将 program 保存到固定版本支持的文件格式。 Saves this program using a fixed-version supported file format.</summary>
    /// <param name="path">绝对输出路径。 The absolute output path.</param>
    /// <param name="options">文件格式选项。 The file-format options.</param>
    public void Save(string path, MIGraphXFileOptions options)
    {
        if (options is null) { throw new ArgumentNullException(nameof(options)); }
        var fullPath = ValidateOutputPath(path, nameof(path));
        owner.Runtime.RequireSame(options.Owner.Runtime, nameof(options));
        using (var utf8 = new StrictUtf8String(fullPath, nameof(path)))
        {
            NativeResourceLock.With(
                new[] { NativeResourceLock.Target(owner.Id, owner.Sync), NativeResourceLock.Target(options.Owner.Id, options.Owner.Sync) },
                () => NativeStatus.ThrowIfFailed(NativeMethods.Save(owner.HandleUnderLock, utf8.Pointer, options.Owner.HandleUnderLock), "migraphx_save"));
        }
    }

    /// <summary>从保存文件载入新的 owned program；载入后需按需重新编译。 Loads a new owned program; recompile it as needed after loading.</summary>
    /// <param name="path">绝对输入路径。 The absolute input path.</param>
    /// <param name="options">文件格式选项。 The file-format options.</param>
    public static MIGraphXProgram Load(string path, MIGraphXFileOptions options)
    {
        if (options is null) { throw new ArgumentNullException(nameof(options)); }
        var fullPath = ValidateInputPath(path, nameof(path));
        var runtime = options.Owner.Runtime;
        using (var utf8 = new StrictUtf8String(fullPath, nameof(path)))
        {
            return NativeResourceLock.With(
                new[] { NativeResourceLock.Target(options.Owner.Id, options.Owner.Sync) },
                () => new MIGraphXProgram(runtime, NativeProgramHandle.Load(utf8.Pointer, options.Owner.HandleUnderLock)));
        }
    }

    /// <summary>
    /// 复制参数名称和 static shape，返回确定顺序的只读托管快照。
    /// Copies parameter names and static shapes into a deterministically ordered read-only managed snapshot.
    /// </summary>
    /// <returns>按 native name 顺序排列的参数 shape 映射。 Parameter-shape map ordered by native name order.</returns>
    public IReadOnlyDictionary<string, MIGraphXShape> GetParameterShapes() => owner.WithHandle(GetParameterShapesUnderLock);

    /// <summary>
    /// 复制输出 static shape，返回按 native 索引排序的只读托管快照。
    /// Copies static output shapes into a read-only managed snapshot ordered by native index.
    /// </summary>
    /// <returns>与原生 collection 生命周期独立的 shape 列表。 Shape list independent of the native collection lifetime.</returns>
    public IReadOnlyList<MIGraphXShape> GetOutputShapes() => owner.WithHandle(GetOutputShapesUnderLock);

    /// <summary>复制输出 shape 为独立集合对象；对应 native shapes collection 的托管快照。 Creates an independent managed collection snapshot of native output shapes.</summary>
    public MIGraphXShapeCollection GetOutputShapeCollection() => new MIGraphXShapeCollection(GetOutputShapes());

    /// <summary>
    /// 使用显式 parameter map 同步运行，并在 native output collection 释放前复制所有输出。
    /// Runs synchronously with an explicit parameter map and copies every output before releasing the native output collection.
    /// </summary>
    /// <param name="parameters">同一原生库创建且名称完整匹配的参数映射。 Parameter map from the same native library with an exact name match.</param>
    /// <returns>拥有独立 host buffer 的只读输出集合。 Read-only output collection owning independent host buffers.</returns>
    public MIGraphXArgumentCollection Run(MIGraphXParameterMap parameters)
    {
        if (parameters is null) { throw new ArgumentNullException(nameof(parameters)); }
        owner.Runtime.RequireSame(parameters.Owner.Runtime, nameof(parameters));
        return NativeResourceLock.With(
            new[]
            {
                NativeResourceLock.Target(owner.Id, owner.Sync),
                NativeResourceLock.Target(parameters.Owner.Id, parameters.Owner.Sync),
            },
            () =>
            {
                if (!compiled) { throw new InvalidOperationException("The program must be compiled before Run."); }
                var required = GetParameterShapesUnderLock(owner.HandleUnderLock).Keys.ToArray();
                var supplied = parameters.NamesUnderLock;
                var requiredSet = new HashSet<string>(required, StringComparer.Ordinal);
                if (required.Length != supplied.Length || !requiredSet.SetEquals(supplied))
                {
                    throw new ArgumentException(
                        $"Parameter names must exactly match the native set. Required: [{string.Join(", ", required)}]; supplied: [{string.Join(", ", supplied)}].",
                        nameof(parameters));
                }

                using (var outputs = NativeArgumentsHandle.Run(owner.HandleUnderLock, parameters.Owner.HandleUnderLock))
                {
                    var count = ReadStableSize(
                        () => GetArgumentsSize(outputs.DangerousGetHandle()),
                        "run output count");
                    var copied = new List<MIGraphXArgument>(count);
                    try
                    {
                        for (var index = 0; index < count; index++)
                        {
                            NativeStatus.ThrowIfFailed(
                                NativeMethods.ArgumentsGet(out var argument, outputs.DangerousGetHandle(), new UIntPtr((uint)index)),
                                "migraphx_arguments_get");
                            argument = NativeBorrowedOutput.RequireHandle(argument, "migraphx_arguments_get");
                            copied.Add(MIGraphXArgument.CopyFromNative(owner.Runtime, argument, $"run output {index}"));
                        }
                        EnsureStableSize(() => GetArgumentsSize(outputs.DangerousGetHandle()), count, "run output count");
                        return new MIGraphXArgumentCollection(copied);
                    }
                    catch
                    {
                        foreach (var argument in copied) { argument.Dispose(); }
                        throw;
                    }
                }
            });
    }

    /// <summary>
    /// 使用固定原生 <c>program::operator==</c> 比较 program 的打印结构内容，不改变任一对象的所有权。
    /// Compares printed structural program content through the fixed native <c>program::operator==</c> without changing ownership of either object.
    /// </summary>
    /// <param name="other">由同一已加载 MIGraphX 原生库创建的另一个 program。 Another program created by the same loaded MIGraphX native library.</param>
    /// <returns>固定原生实现的 program 文本结构比较相同则为 <see langword="true"/>。 <see langword="true"/> when the fixed native implementation reports equal printed program structure.</returns>
    /// <remarks>
    /// 该方法不比较模型文件 hash，不证明推理语义、输出或编译结果等价，也不定义 <see cref="object.Equals(object)"/>、hash 或运算符语义。ROCm 7.2.1 的实现比较 program 打印文本；parse、compile、sort 或其他 graph mutation 可能改变结果，未打印的 runtime/context 状态和托管 <see cref="IsCompiled"/> 标志本身不属于比较契约。反向并发比较按稳定资源顺序加锁，Dispose 会等待正在进行的比较。
    /// This method does not compare model-file hashes, prove equivalent inference semantics, outputs, or compilation results, or define <see cref="object.Equals(object)"/>, hashing, or operator semantics. ROCm 7.2.1 compares printed program text; parse, compile, sort, or other graph mutation can change the result, while unprinted runtime/context state and the managed <see cref="IsCompiled"/> flag itself are outside the comparison contract. Reverse concurrent comparisons use a stable resource lock order, and Dispose waits for an in-progress comparison.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> 为 null。 <paramref name="other"/> is null.</exception>
    /// <exception cref="ArgumentException">对象不属于同一原生 library root。 The objects do not belong to the same native library root.</exception>
    /// <exception cref="ObjectDisposedException">任一对象已释放。 Either object has been disposed.</exception>
    /// <exception cref="MIGraphXNativeLoadException">原生 equality 导出不可用。 The native equality export is unavailable.</exception>
    /// <exception cref="MIGraphXException">原生比较失败或返回非法 C bool。 Native comparison fails or returns an invalid C bool.</exception>
    public bool HasSameNativeContent(MIGraphXProgram other)
    {
        if (other is null) { throw new ArgumentNullException(nameof(other)); }
        owner.Runtime.RequireSame(other.owner.Runtime, nameof(other));
        owner.Runtime.RequireM10Equality();
        return NativeResourceLock.With(
            new[]
            {
                NativeResourceLock.Target(owner.Id, owner.Sync),
                NativeResourceLock.Target(other.owner.Id, other.owner.Sync),
            },
            () => NativeM10Methods.ProgramContentEquals(owner.HandleUnderLock, other.owner.HandleUnderLock));
    }

    /// <summary>使用 native assign-to 创建独立 program 副本。 Creates an independent program clone through native assign-to.</summary>
    public MIGraphXProgram Clone()
    {
        return owner.WithHandle(handle =>
        {
            var overrides = dynamicOverrides.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal);
            var result = new MIGraphXProgram(owner.Runtime, NativeProgramHandle.CloneFrom(handle), overrides)
            {
                compiled = compiled,
                compiledOffloadCopy = compiledOffloadCopy,
            };
            return result;
        });
    }

    /// <summary>获取由已编译 program 所有且由返回对象保持存活的实验 context。 Gets an experimental context owned by a compiled program and kept alive by the returned object.</summary>
    /// <exception cref="InvalidOperationException">program 尚未编译或编译状态已因 graph 变更失效。 The program has not been compiled or graph mutation invalidated its compiled state.</exception>
    public MIGraphXContext GetExperimentalContext()
    {
        return owner.WithHandle(program =>
        {
            if (!compiled) throw new InvalidOperationException("The program must be compiled before acquiring its experimental context.");
            var slot = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(slot, IntPtr.Zero);
                NativeStatus.ThrowIfFailed(NativeMethods.ProgramExperimentalGetContext(slot, program), "migraphx_program_experimental_get_context");
                var context = Marshal.ReadIntPtr(slot);
                if (context == IntPtr.Zero) throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, "migraphx_program_experimental_get_context (success with null context)");
                var lease = owner.AcquireLease();
                try { return new MIGraphXContext(lease, context); }
                catch { lease.Dispose(); throw; }
            }
            finally { Marshal.FreeHGlobal(slot); }
        });
    }

    internal NativeRuntime Runtime => owner.Runtime;

    internal MIGraphXNativeAsyncRun EnqueueNativeAsync(
        MIGraphXParameterMap parameters,
        IntPtr stream,
        bool requireOffloadCopy,
        IReadOnlyList<IDisposable>? externalLeases = null)
    {
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));
        if (stream == IntPtr.Zero) throw new ArgumentException("The HIP stream pointer must not be null.", nameof(stream));
        owner.Runtime.RequireSame(parameters.Owner.Runtime, nameof(parameters));
        owner.Runtime.RequireM6();

        return NativeResourceLock.With(
            new[]
            {
                NativeResourceLock.Target(owner.Id, owner.Sync),
                NativeResourceLock.Target(parameters.Owner.Id, parameters.Owner.Sync),
            },
            () =>
            {
                if (!compiled) throw new InvalidOperationException("The program must be compiled before asynchronous execution.");
                if (compiledOffloadCopy != requireOffloadCopy)
                {
                    throw new InvalidOperationException(requireOffloadCopy
                        ? "Host async execution requires a program compiled with offloadCopy=true."
                        : "Device-input async execution requires a program compiled with offloadCopy=false.");
                }
                ValidateParameterNamesUnderLock(parameters);

                var leases = new List<IDisposable>();
                try
                {
                    if (externalLeases is not null) leases.AddRange(externalLeases);
                    leases.Add(owner.AcquireLease());
                    leases.Add(parameters.AcquireAsyncLease());
                    var nativeOutputs = NativeArgumentsHandle.RunAsync(
                        owner.HandleUnderLock,
                        parameters.Owner.HandleUnderLock,
                        stream,
                        "ihipStream_t");
                    return new MIGraphXNativeAsyncRun(owner.Runtime, nativeOutputs, new NativeLeaseSet(leases));
                }
                catch
                {
                    for (var index = leases.Count - 1; index >= (externalLeases?.Count ?? 0); index--) leases[index].Dispose();
                    throw;
                }
            });
    }

    /// <summary>确定性释放 owned program handle；重复调用安全。 Deterministically releases the owned program handle; repeated calls are safe.</summary>
    public void Dispose() => owner.Dispose();

    private OrderedReadOnlyDictionary<MIGraphXShape> GetParameterShapesUnderLock(IntPtr program)
    {
        using (var nativeShapes = NativeProgramParameterShapesHandle.Create(program))
        {
            var count = ReadStableSize(
                () => GetParameterShapeCount(nativeShapes.DangerousGetHandle()),
                "parameter count");
            var namesBuffer = count == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(checked(count * IntPtr.Size));
            try
            {
                for (var index = 0; index < count; index++) { Marshal.WriteIntPtr(namesBuffer, index * IntPtr.Size, IntPtr.Zero); }
                NativeStatus.ThrowIfFailed(
                    NativeMethods.ProgramParameterShapesNames(namesBuffer, nativeShapes.DangerousGetHandle()),
                    "migraphx_program_parameter_shapes_names");
                EnsureStableSize(() => GetParameterShapeCount(nativeShapes.DangerousGetHandle()), count, "parameter count");

                var result = new List<KeyValuePair<string, MIGraphXShape>>(count);
                var unique = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < count; index++)
                {
                    const string operation = "migraphx_program_parameter_shapes_names";
                    var name = StrictUtf8String.Decode(Marshal.ReadIntPtr(namesBuffer, index * IntPtr.Size), operation);
                    if (name.Length == 0)
                    {
                        throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, $"{operation} (success with empty UTF-8 string)");
                    }
                    if (!unique.Add(name)) { throw new InvalidOperationException($"Native parameter name '{name}' is duplicated."); }
                    using (var utf8 = new StrictUtf8String(name, nameof(name)))
                    {
                        NativeStatus.ThrowIfFailed(
                            NativeMethods.ProgramParameterShapesGet(out var shape, nativeShapes.DangerousGetHandle(), utf8.Pointer),
                            "migraphx_program_parameter_shapes_get");
                        shape = NativeBorrowedOutput.RequireHandle(shape, "migraphx_program_parameter_shapes_get");
                        dynamicOverrides.TryGetValue(name, out var fallback);
                        result.Add(new KeyValuePair<string, MIGraphXShape>(name, MIGraphXShape.FromNative(shape, $"parameter '{name}'", fallback)));
                    }
                }
                return new OrderedReadOnlyDictionary<MIGraphXShape>(result);
            }
            finally
            {
                if (namesBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(namesBuffer); }
            }
        }
    }

    private void RequireUncompiledForQuantizationUnderLock()
    {
        if (compiled)
        {
            throw new InvalidOperationException("Quantization must be applied before program compilation.");
        }
    }

    private void ValidateParameterNamesUnderLock(MIGraphXParameterMap parameters)
    {
        var required = GetParameterShapesUnderLock(owner.HandleUnderLock).Keys.ToArray();
        var supplied = parameters.NamesUnderLock;
        var requiredSet = new HashSet<string>(required, StringComparer.Ordinal);
        if (required.Length != supplied.Length || !requiredSet.SetEquals(supplied))
        {
            throw new ArgumentException(
                $"Parameter names must exactly match the native set. Required: [{string.Join(", ", required)}]; supplied: [{string.Join(", ", supplied)}].",
                nameof(parameters));
        }
    }

    private IReadOnlyList<MIGraphXShape> GetOutputShapesUnderLock(IntPtr program)
    {
        using (var nativeShapes = NativeShapesHandle.Create(program))
        {
            var count = ReadStableSize(() => GetShapeCount(nativeShapes.DangerousGetHandle()), "output shape count");
            var result = new MIGraphXShape[count];
            for (var index = 0; index < count; index++)
            {
                NativeStatus.ThrowIfFailed(
                    NativeMethods.ShapesGet(out var shape, nativeShapes.DangerousGetHandle(), new UIntPtr((uint)index)),
                    "migraphx_shapes_get");
                shape = NativeBorrowedOutput.RequireHandle(shape, "migraphx_shapes_get");
                result[index] = MIGraphXShape.FromNative(shape, $"output shape {index}");
            }
            EnsureStableSize(() => GetShapeCount(nativeShapes.DangerousGetHandle()), count, "output shape count");
            return Array.AsReadOnly(result);
        }
    }

    private static int ReadStableSize(Func<UIntPtr> read, string name)
    {
        var first = NativeShapeSnapshot.ToInt(read(), name);
        EnsureStableSize(read, first, name);
        return first;
    }

    private static void EnsureStableSize(Func<UIntPtr> read, int expected, string name)
    {
        var actual = NativeShapeSnapshot.ToInt(read(), name);
        if (actual != expected)
        {
            throw new InvalidOperationException($"Native {name} changed from {expected} to {actual} while creating a snapshot.");
        }
    }

    private static UIntPtr GetParameterShapeCount(IntPtr shapes)
        => NativeValueOutput.ReadSizeT(
            output => NativeMethods.ProgramParameterShapesSizeRaw(output, shapes),
            "migraphx_program_parameter_shapes_size");

    private static UIntPtr GetShapeCount(IntPtr shapes)
        => NativeValueOutput.ReadSizeT(
            output => NativeMethods.ShapesSizeRaw(output, shapes),
            "migraphx_shapes_size");

    private static UIntPtr GetArgumentsSize(IntPtr arguments)
        => NativeValueOutput.ReadSizeT(
            output => NativeMethods.ArgumentsSizeRaw(output, arguments),
            "migraphx_arguments_size");

    private static string ValidateInputPath(string path, string parameterName)
    {
        if (path is null) { throw new ArgumentNullException(parameterName); }
        if (!Path.IsPathRooted(path)) { throw new ArgumentException("The path must be absolute.", parameterName); }
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) { throw new FileNotFoundException("The file does not exist.", fullPath); }
        return fullPath;
    }

    private static string ValidateOutputPath(string path, string parameterName)
    {
        if (path is null) { throw new ArgumentNullException(parameterName); }
        if (!Path.IsPathRooted(path)) { throw new ArgumentException("The path must be absolute.", parameterName); }
        return Path.GetFullPath(path);
    }
}
