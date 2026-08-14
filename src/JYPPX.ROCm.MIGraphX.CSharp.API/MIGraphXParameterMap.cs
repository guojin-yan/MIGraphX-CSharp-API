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
    {
        var runtime = NativeRuntime.Load(nativeLibraryPath);
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

    internal NativeResourceOwner<NativeProgramParametersHandle> Owner => owner;

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
