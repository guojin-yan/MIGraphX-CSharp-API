using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>拥有 instruction handle 的图节点。 Graph instruction wrapper owning a native handle.</summary>
public sealed class MIGraphXInstruction : IDisposable
{
    private readonly NativeResourceOwner<NativeInstructionHandle> owner;
    internal MIGraphXInstruction(NativeRuntime runtime, NativeInstructionHandle handle) => owner = new NativeResourceOwner<NativeInstructionHandle>(runtime, handle);
    internal NativeResourceOwner<NativeInstructionHandle> Owner => owner;
    /// <summary>释放 native instruction。 Releases the native instruction handle.</summary>
    public void Dispose() => owner.Dispose();
}

/// <summary>拥有 native instructions collection 的参数集合。 Managed wrapper for a native instruction list.</summary>
public sealed class MIGraphXInstructions : IDisposable
{
    private readonly NativeResourceOwner<NativeInstructionsHandle> owner;
    private readonly MIGraphXInstruction[] instructions;
    private NativeHandleLease[]? instructionLeases;

    /// <summary>从 instruction 列表创建集合。 Creates a native instruction list from managed instructions.</summary>
    /// <param name="nativeLibraryPath">native 库绝对路径。 Path to the MIGraphX native library.</param>
    /// <param name="instructions">instruction 列表；Instructions to include.</param>
    public MIGraphXInstructions(string nativeLibraryPath, IReadOnlyList<MIGraphXInstruction> instructions)
    {
        if (instructions is null) throw new ArgumentNullException(nameof(instructions));
        var copied = instructions.ToArray();
        if (copied.Any(value => value is null)) throw new ArgumentException("Instructions must not contain null values.", nameof(instructions));
        var runtime = NativeRuntime.Load(nativeLibraryPath);
        for (var index = 0; index < copied.Length; index++) runtime.RequireSame(copied[index].Owner.Runtime, nameof(instructions));
        var created = CreateWithLeases(copied);
        owner = new NativeResourceOwner<NativeInstructionsHandle>(runtime, created.Handle);
        instructionLeases = created.Leases;
        this.instructions = copied;
    }

    private MIGraphXInstructions(NativeRuntime runtime, NativeInstructionsHandle handle, MIGraphXInstruction[] instructions, NativeHandleLease[] leases)
    {
        owner = new NativeResourceOwner<NativeInstructionsHandle>(runtime, handle);
        this.instructions = instructions;
        instructionLeases = leases;
    }

    /// <summary>获取 instruction 数量。 Gets the number of instructions.</summary>
    public int Count => owner.WithHandle(_ => instructions.Length);
    /// <summary>按索引获取 instruction。 Gets an instruction by zero-based index.</summary>
    /// <param name="index">从零开始的索引；Zero-based instruction index.</param>
    public MIGraphXInstruction this[int index] => owner.WithHandle(_ => instructions[index]);
    /// <summary>复制 instruction 集合。 Creates an independent native instruction-list clone.</summary>
    public MIGraphXInstructions Clone()
    {
        var resources = new[] { NativeResourceLock.Target(owner.Id, owner.Sync) }
            .Concat(instructions.Select(value => NativeResourceLock.Target(value.Owner.Id, value.Owner.Sync)))
            .ToArray();
        return NativeResourceLock.With(resources, () =>
        {
            var leases = AcquireInstructionLeases(instructions);
            try
            {
                var pointers = instructions.Select(value => value.Owner.HandleUnderLock).ToArray();
                var handle = NativeInstructionsHandle.CloneFrom(owner.HandleUnderLock, pointers);
                return new MIGraphXInstructions(owner.Runtime, handle, instructions.ToArray(), leases);
            }
            catch
            {
                DisposeLeases(leases);
                throw;
            }
        });
    }
    internal NativeResourceOwner<NativeInstructionsHandle> Owner => owner;
    /// <summary>释放 native instruction 集合。 Releases the native instruction-list handle.</summary>
    public void Dispose()
    {
        lock (owner.Sync)
        {
            owner.Dispose();
            DisposeLeases(System.Threading.Interlocked.Exchange(ref instructionLeases, null));
        }
    }

    private static LeasedInstructions CreateWithLeases(MIGraphXInstruction[] values)
    {
        var resources = values.Select(value => NativeResourceLock.Target(value.Owner.Id, value.Owner.Sync)).ToArray();
        return NativeResourceLock.With(resources, () =>
        {
            var leases = AcquireInstructionLeases(values);
            try
            {
                var pointers = values.Select(value => value.Owner.HandleUnderLock).ToArray();
                return new LeasedInstructions(NativeInstructionsHandle.Create(pointers), leases);
            }
            catch
            {
                DisposeLeases(leases);
                throw;
            }
        });
    }

    private static NativeHandleLease[] AcquireInstructionLeases(IEnumerable<MIGraphXInstruction> values)
    {
        var leases = new List<NativeHandleLease>();
        try
        {
            foreach (var value in values) leases.Add(value.Owner.AcquireLease());
            return leases.ToArray();
        }
        catch
        {
            DisposeLeases(leases);
            throw;
        }
    }

    private static void DisposeLeases(IEnumerable<IDisposable>? leases)
    {
        if (leases is null) return;
        foreach (var lease in leases.Reverse()) lease.Dispose();
    }

    private sealed class LeasedInstructions
    {
        internal LeasedInstructions(NativeInstructionsHandle handle, NativeHandleLease[] leases) { Handle = handle; Leases = leases; }
        internal NativeInstructionsHandle Handle { get; }
        internal NativeHandleLease[] Leases { get; }
    }
}

/// <summary>可读 operation 名称的 native operation 对象；Native operation wrapper with readable name.</summary>
public sealed class MIGraphXOperation : IDisposable
{
    private readonly NativeResourceOwner<NativeOperationHandle> owner;
    private MIGraphXOperation(NativeRuntime runtime, NativeOperationHandle handle) => owner = new NativeResourceOwner<NativeOperationHandle>(runtime, handle);
    internal static MIGraphXOperation FromNative(NativeRuntime runtime, NativeOperationHandle handle) => new MIGraphXOperation(runtime, handle);
    internal NativeResourceOwner<NativeOperationHandle> Owner => owner;

    /// <summary>
    /// 创建不带属性的 operation。
    /// Creates an operation through the constrained upstream call with no attributes or variadic values.
    /// </summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    /// <param name="name">operation 名称。 Operation name.</param>
    /// <remarks>
    /// C declaration is variadic. This factory intentionally supports only the proven
    /// <c>migraphx_operation_create(&amp;op, name, NULL)</c> form; attribute formatting and
    /// additional variadic values remain unsupported until a typed ABI is available.
    /// </remarks>
    public static MIGraphXOperation Create(string nativeLibraryPath, string name)
    {
        var runtime = NativeRuntime.Load(nativeLibraryPath);
        runtime.RequireOperationCreate();
        using (var utf8 = new StrictUtf8String(name, nameof(name)))
        {
            return new MIGraphXOperation(runtime, NativeOperationHandle.CreateNoAttributes(utf8.Pointer));
        }
    }

    /// <summary>
    /// 创建带强类型、已物化属性的 operation。
    /// Creates an operation with strongly typed, fully materialized attributes.
    /// </summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    /// <param name="name">operation 名称。 Operation name.</param>
    /// <param name="attributes">属性构建器；Typed operation attributes.</param>
    /// <remarks>
    /// The builder emits one complete attribute object and this binding supplies no C variadic
    /// values. Literal percent signs are escaped for the upstream formatter. Arbitrary format
    /// placeholders and general C varargs are intentionally unsupported.
    /// </remarks>
    public static MIGraphXOperation Create(string nativeLibraryPath, string name, MIGraphXOperationAttributes attributes)
    {
        if (attributes is null) throw new ArgumentNullException(nameof(attributes));
        var runtime = NativeRuntime.Load(nativeLibraryPath);
        runtime.RequireOperationCreate();
        using (var utf8Name = new StrictUtf8String(name, nameof(name)))
        using (var utf8Attributes = new StrictUtf8String(attributes.Build().Replace("%", "%%"), nameof(attributes)))
        {
            return new MIGraphXOperation(runtime, NativeOperationHandle.CreateWithMaterializedAttributes(utf8Name.Pointer, utf8Attributes.Pointer));
        }
    }

    /// <summary>获取 operation 名称。 Gets the native operation name.</summary>
    public string Name
    {
        get => owner.WithHandle(ReadName);
    }

    /// <summary>复制 operation。 Clones the native operation.</summary>
    public MIGraphXOperation Clone()
    {
        return owner.WithHandle(handle =>
        {
            var name = ReadName(handle);
            using (var utf8 = new StrictUtf8String(name, nameof(name)))
            {
                return new MIGraphXOperation(owner.Runtime, NativeOperationHandle.CloneFrom(handle, utf8.Pointer));
            }
        });
    }

    /// <summary>释放 native operation。 Releases the native operation handle.</summary>
    public void Dispose() => owner.Dispose();

    private static string ReadName(IntPtr handle)
    {
        const int capacity = 1024;
        var buffer = Marshal.AllocHGlobal(capacity);
        try
        {
            for (var index = 0; index < capacity; index++) Marshal.WriteByte(buffer, index, byte.MaxValue);
            NativeStatus.ThrowIfFailed(NativeMethods.OperationName(buffer, new UIntPtr((uint)capacity), handle), "migraphx_operation_name");
            return StrictUtf8String.DecodeRequiredBuffer(buffer, capacity, "migraphx_operation_name");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}

/// <summary>与 program 生命周期绑定的 module 视图。 Module view whose lifetime is tied to a program.</summary>
public sealed class MIGraphXModule : IDisposable
{
    private readonly NativeRuntime runtime;
    private NativeHandleLease? programLease;
    private readonly object sync = new object();
    private readonly IntPtr handle;
    private readonly long id = NativeResourceIds.Next();

    internal MIGraphXModule(NativeRuntime runtime, NativeHandleLease programLease, IntPtr handle)
    {
        if (handle == IntPtr.Zero) throw new ArgumentException("The native module handle must not be null.", nameof(handle));
        this.runtime = runtime;
        this.programLease = programLease;
        this.handle = handle;
    }

    internal NativeRuntime Runtime => runtime;
    internal long Id => id;
    internal object Sync => sync;
    internal IntPtr HandleUnderLock { get { _ = programLease ?? throw new ObjectDisposedException(nameof(MIGraphXModule)); return handle; } }
    internal NativeHandleLease AcquireProgramLeaseUnderLock()
        => (programLease ?? throw new ObjectDisposedException(nameof(MIGraphXModule))).Duplicate();

    /// <summary>打印 module。 Prints the native module representation.</summary>
    public void Print()
    {
        lock (sync) { NativeStatus.ThrowIfFailed(NativeMethods.ModulePrint(HandleUnderLock), "migraphx_module_print"); }
    }

    /// <summary>向 module 添加参数。 Adds a parameter instruction to the module.</summary>
    /// <param name="name">参数名称；Parameter name.</param>
    /// <param name="shape">参数 shape；Parameter shape.</param>
    public MIGraphXInstruction AddParameter(string name, MIGraphXShape shape)
    {
        if (shape is null) throw new ArgumentNullException(nameof(shape));
        using (var utf8 = new StrictUtf8String(name, nameof(name)))
        using (var nativeShape = CreateNativeShape(shape))
        {
            return AddInstructionCore((slot, _) => NativeMethods.ModuleAddParameter(slot, HandleUnderLock, utf8.Pointer, nativeShape.DangerousGetHandle()), "migraphx_module_add_parameter");
        }
    }

    /// <summary>向 module 添加 allocation。 Adds an allocation instruction to the module.</summary>
    /// <param name="shape">allocation 的 shape；Shape of the allocation.</param>
    public MIGraphXInstruction AddAllocation(MIGraphXShape shape)
    {
        if (shape is null) throw new ArgumentNullException(nameof(shape));
        using (var nativeShape = CreateNativeShape(shape))
        {
            return AddInstructionCore((slot, _) => NativeMethods.ModuleAddAllocation(slot, HandleUnderLock, nativeShape.DangerousGetHandle()), "migraphx_module_add_allocation");
        }
    }

    /// <summary>向 module 添加 literal。 Adds a literal argument instruction to the module.</summary>
    /// <param name="argument">literal 参数；Argument containing literal data.</param>
    public MIGraphXInstruction AddLiteral(MIGraphXArgument argument)
    {
        if (argument is null) throw new ArgumentNullException(nameof(argument));
        runtime.RequireSame(argument.Owner.Runtime, nameof(argument));
        using (var nativeShape = CreateNativeShape(argument.Shape))
        {
            return NativeResourceLock.With(
                new[] { NativeResourceLock.Target(id, sync), NativeResourceLock.Target(argument.Owner.Id, argument.Owner.Sync) },
                () => AddInstructionCore((slot, _) =>
                {
                    NativeStatus.ThrowIfFailed(NativeMethods.ArgumentBuffer(out var buffer, argument.Owner.HandleUnderLock), "migraphx_argument_buffer");
                    if (argument.Shape.ByteCount != 0 && buffer == IntPtr.Zero)
                    {
                        throw new MIGraphXException(
                            (int)NativeMIGraphXStatus.UnknownError,
                            "migraphx_argument_buffer (success with null buffer)");
                    }
                    return NativeMethods.ModuleAddLiteral(slot, HandleUnderLock, nativeShape.DangerousGetHandle(), buffer);
                }, "migraphx_module_add_literal"));
        }
    }

    /// <summary>向 module 添加 return。 Adds a return instruction using the supplied arguments.</summary>
    /// <param name="arguments">return 参数集合；Instruction arguments returned by the module.</param>
    public MIGraphXInstruction AddReturn(MIGraphXInstructions arguments)
    {
        if (arguments is null) throw new ArgumentNullException(nameof(arguments));
        runtime.RequireSame(arguments.Owner.Runtime, nameof(arguments));
        return NativeResourceLock.With(
            new[] { NativeResourceLock.Target(id, sync), NativeResourceLock.Target(arguments.Owner.Id, arguments.Owner.Sync) },
            () => AddInstructionCore((slot, _) => NativeMethods.ModuleAddReturn(slot, HandleUnderLock, arguments.Owner.HandleUnderLock), "migraphx_module_add_return"));
    }

    /// <summary>向 module 添加 instruction。 Adds an operation instruction to the module.</summary>
    /// <param name="operation">要调用的 operation；Operation to invoke.</param>
    /// <param name="arguments">操作参数；Arguments passed to the operation.</param>
    public MIGraphXInstruction AddInstruction(MIGraphXOperation operation, MIGraphXInstructions arguments)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));
        if (arguments is null) throw new ArgumentNullException(nameof(arguments));
        runtime.RequireSame(operation.Owner.Runtime, nameof(operation));
        runtime.RequireSame(arguments.Owner.Runtime, nameof(arguments));
        return NativeResourceLock.With(
            new[] { NativeResourceLock.Target(id, sync), NativeResourceLock.Target(operation.Owner.Id, operation.Owner.Sync), NativeResourceLock.Target(arguments.Owner.Id, arguments.Owner.Sync) },
            () => AddInstructionCore((slot, _) => NativeMethods.ModuleAddInstruction(slot, HandleUnderLock, operation.Owner.HandleUnderLock, arguments.Owner.HandleUnderLock), "migraphx_module_add_instruction"));
    }

    /// <summary>使用 module 引用添加 instruction。 Adds an instruction with referenced submodules.</summary>
    /// <param name="operation">要调用的 operation；Operation to invoke.</param>
    /// <param name="arguments">操作参数；Arguments passed to the operation.</param>
    /// <param name="moduleRefs">module 引用；Referenced submodules.</param>
    public MIGraphXInstruction AddInstructionWithModuleArgs(MIGraphXOperation operation, MIGraphXInstructions arguments, MIGraphXModules moduleRefs)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));
        if (arguments is null) throw new ArgumentNullException(nameof(arguments));
        if (moduleRefs is null) throw new ArgumentNullException(nameof(moduleRefs));
        runtime.RequireSame(operation.Owner.Runtime, nameof(operation));
        runtime.RequireSame(arguments.Owner.Runtime, nameof(arguments));
        runtime.RequireSame(moduleRefs.Owner.Runtime, nameof(moduleRefs));
        return NativeResourceLock.With(
            new[] { NativeResourceLock.Target(id, sync), NativeResourceLock.Target(operation.Owner.Id, operation.Owner.Sync), NativeResourceLock.Target(arguments.Owner.Id, arguments.Owner.Sync), NativeResourceLock.Target(moduleRefs.Owner.Id, moduleRefs.Owner.Sync) },
            () => AddInstructionCore((slot, _) => NativeMethods.ModuleAddInstructionWithModArgs(slot, HandleUnderLock, operation.Owner.HandleUnderLock, arguments.Owner.HandleUnderLock, moduleRefs.Owner.HandleUnderLock), "migraphx_module_add_instruction_with_mod_args"));
    }

    /// <summary>释放 module 视图。 Releases the program lease held by this module view.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            System.Threading.Interlocked.Exchange(ref programLease, null)?.Dispose();
        }
    }

    private MIGraphXInstruction AddInstructionCore(Func<IntPtr, IntPtr, NativeMIGraphXStatus> create, string operation)
    {
        lock (sync)
        {
            var output = OutHandle<NativeInstructionHandle>.Create(operation);
            try
            {
                output.Complete(create(output.OutSlot, HandleUnderLock));
                return new MIGraphXInstruction(runtime, output.Handle);
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }

    private static NativeShapeHandle CreateNativeShape(MIGraphXShape shape)
        => shape.IsDynamic ? NativeShapeHandle.CreateDynamic(shape) : shape.IsStandard ? NativeShapeHandle.Create(shape) : NativeShapeHandle.CreateWithStrides(shape);
}

/// <summary>拥有 native module collection 的集合。 Managed wrapper for a native module list.</summary>
public sealed class MIGraphXModules : IDisposable
{
    private readonly NativeResourceOwner<NativeModulesHandle> owner;
    private readonly MIGraphXModule[] modules;
    private NativeHandleLease[]? programLeases;

    /// <summary>从 module 列表创建集合。 Creates a native module list from managed modules.</summary>
    /// <param name="nativeLibraryPath">native 库绝对路径。 Path to the MIGraphX native library.</param>
    /// <param name="modules">module 列表；Modules to include.</param>
    public MIGraphXModules(string nativeLibraryPath, IReadOnlyList<MIGraphXModule> modules)
    {
        if (modules is null) throw new ArgumentNullException(nameof(modules));
        this.modules = modules.ToArray();
        var runtime = NativeRuntime.Load(nativeLibraryPath);
        if (this.modules.Any(module => module is null)) throw new ArgumentException("Modules must not contain null values.", nameof(modules));
        foreach (var module in this.modules) runtime.RequireSame(module.Runtime, nameof(modules));
        var created = CreateWithLeases(this.modules);
        owner = new NativeResourceOwner<NativeModulesHandle>(runtime, created.Handle);
        programLeases = created.Leases;
    }

    internal NativeResourceOwner<NativeModulesHandle> Owner => owner;
    /// <summary>获取 module 数量。 Gets the number of modules.</summary>
    public int Count => owner.WithHandle(_ => modules.Length);
    /// <summary>复制 module 集合。 Creates an independent native module-list clone.</summary>
    public MIGraphXModules Clone()
    {
        var resources = new[] { NativeResourceLock.Target(owner.Id, owner.Sync) }
            .Concat(modules.Select(module => NativeResourceLock.Target(module.Id, module.Sync)))
            .ToArray();
        return NativeResourceLock.With(resources, () =>
        {
            var leases = AcquireProgramLeases(modules);
            try
            {
                var pointers = modules.Select(module => module.HandleUnderLock).ToArray();
                var handle = NativeModulesHandle.CloneFrom(owner.HandleUnderLock, pointers);
                return new MIGraphXModules(owner.Runtime, handle, modules.ToArray(), leases);
            }
            catch
            {
                DisposeProgramLeases(leases);
                throw;
            }
        });
    }
    private MIGraphXModules(NativeRuntime runtime, NativeModulesHandle handle, MIGraphXModule[] modules, NativeHandleLease[] leases)
    {
        owner = new NativeResourceOwner<NativeModulesHandle>(runtime, handle);
        this.modules = modules;
        programLeases = leases;
    }
    /// <summary>释放 native module 集合。 Releases the native module-list handle.</summary>
    public void Dispose()
    {
        lock (owner.Sync)
        {
            owner.Dispose();
            DisposeProgramLeases(System.Threading.Interlocked.Exchange(ref programLeases, null));
        }
    }

    private static LeasedModules CreateWithLeases(MIGraphXModule[] values)
    {
        var resources = values.Select(module => NativeResourceLock.Target(module.Id, module.Sync)).ToArray();
        return NativeResourceLock.With(resources, () =>
        {
            var leases = AcquireProgramLeases(values);
            try
            {
                var pointers = values.Select(module => module.HandleUnderLock).ToArray();
                return new LeasedModules(NativeModulesHandle.Create(pointers), leases);
            }
            catch
            {
                DisposeProgramLeases(leases);
                throw;
            }
        });
    }

    private static NativeHandleLease[] AcquireProgramLeases(IEnumerable<MIGraphXModule> values)
    {
        var leases = new List<NativeHandleLease>();
        try
        {
            foreach (var value in values) leases.Add(value.AcquireProgramLeaseUnderLock());
            return leases.ToArray();
        }
        catch
        {
            DisposeProgramLeases(leases);
            throw;
        }
    }

    private static void DisposeProgramLeases(IEnumerable<IDisposable>? leases)
    {
        if (leases is null) return;
        foreach (var lease in leases.Reverse()) lease.Dispose();
    }

    private sealed class LeasedModules
    {
        internal LeasedModules(NativeModulesHandle handle, NativeHandleLease[] leases) { Handle = handle; Leases = leases; }
        internal NativeModulesHandle Handle { get; }
        internal NativeHandleLease[] Leases { get; }
    }
}
