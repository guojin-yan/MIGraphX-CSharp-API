using System;
using System.Collections.Generic;
using System.Linq;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示拥有 native program-parameters map 和独立 argument 副本的资源安全参数映射。
/// Represents a resource-safe parameter map that owns a native program-parameters map and independent argument copies.
/// </summary>
public sealed class MIGraphXParameterMap : IDisposable
{
    private readonly NativeResourceOwner<NativeProgramParametersHandle> owner;
    private readonly List<KeyValuePair<string, MIGraphXArgument>> entries = new List<KeyValuePair<string, MIGraphXArgument>>();

    /// <summary>
    /// 使用显式原生库创建空参数映射。
    /// Creates an empty parameter map using an explicit native library.
    /// </summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    public MIGraphXParameterMap(string nativeLibraryPath)
        : this(NativeRuntime.Load(nativeLibraryPath))
    {
    }

    internal MIGraphXParameterMap(NativeRuntime runtime)
    {
        owner = new NativeResourceOwner<NativeProgramParametersHandle>(runtime, NativeProgramParametersHandle.Create());
    }

    /// <summary>获取当前映射数量；Dispose 后访问会失败。 Gets the current mapping count; access fails after disposal.</summary>
    public int Count => owner.WithHandle(_ => entries.Count);

    /// <summary>获取按添加顺序排列的不可变名称快照。 Gets an immutable name snapshot in insertion order.</summary>
    public IReadOnlyList<string> Names => owner.WithHandle(_ => Array.AsReadOnly(entries.Select(entry => entry.Key).ToArray()));

    /// <summary>
    /// 按严格 UTF-8 名称添加 argument 的深副本；重复名称被拒绝。
    /// Adds a deep copy of an argument under a strict UTF-8 name; duplicate names are rejected.
    /// </summary>
    /// <param name="name">模型参数名称。 Model parameter name.</param>
    /// <param name="argument">将被深拷贝的 typed argument。 Typed argument to deep-copy.</param>
    public void Add(string name, MIGraphXArgument argument)
    {
        if (argument is null) { throw new ArgumentNullException(nameof(argument)); }
        using (var utf8 = new StrictUtf8String(name, nameof(name)))
        {
            lock (owner.Sync)
            {
                _ = owner.HandleUnderLock;
                if (entries.Any(entry => string.Equals(entry.Key, name, StringComparison.Ordinal)))
                {
                    throw new ArgumentException($"A parameter named '{name}' already exists.", nameof(name));
                }
                owner.Runtime.RequireSame(argument.Owner.Runtime, nameof(argument));
                var copy = argument.CloneForMap();
                try
                {
                    NativeStatus.ThrowIfFailed(
                        NativeMethods.ProgramParametersAdd(owner.HandleUnderLock, utf8.Pointer, copy.Owner.HandleUnderLock),
                        "migraphx_program_parameters_add");
                    entries.Add(new KeyValuePair<string, MIGraphXArgument>(name, copy));
                }
                catch
                {
                    copy.Dispose();
                    throw;
                }
            }
        }
    }

    /// <summary>复制参数 map 与所有 argument；对应 native assign-to 的托管值语义。 Creates an independent parameter-map snapshot with cloned arguments.</summary>
    public MIGraphXParameterMap Clone()
    {
        return owner.WithHandle(_ =>
        {
            var result = new MIGraphXParameterMap(owner.Runtime);
            try
            {
                foreach (var entry in entries) result.Add(entry.Key, entry.Value);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        });
    }

    internal NativeResourceOwner<NativeProgramParametersHandle> Owner => owner;

    internal void AddExternal(string name, MIGraphXShape shape, IntPtr pointer)
    {
        using (var argument = MIGraphXArgument.CreateExternal(owner.Runtime, shape, pointer))
        {
            Add(name, argument);
        }
    }

    internal IDisposable AcquireAsyncLease()
    {
        lock (owner.Sync)
        {
            _ = owner.HandleUnderLock;
            var leases = new List<IDisposable>();
            try
            {
                leases.Add(owner.AcquireLease());
                foreach (var entry in entries) leases.Add(entry.Value.AcquireAsyncLease());
                return new NativeLeaseSet(leases);
            }
            catch
            {
                for (var index = leases.Count - 1; index >= 0; index--) leases[index].Dispose();
                throw;
            }
        }
    }

    internal string[] NamesUnderLock
    {
        get
        {
            _ = owner.HandleUnderLock;
            return entries.Select(entry => entry.Key).ToArray();
        }
    }

    /// <summary>先释放 native map，再释放其 owned argument 副本；重复调用安全。 Releases the native map before its owned argument copies; repeated calls are safe.</summary>
    public void Dispose()
    {
        lock (owner.Sync)
        {
            owner.Dispose();
            foreach (var entry in entries)
            {
                entry.Value.Dispose();
            }
            entries.Clear();
        }
    }
}
