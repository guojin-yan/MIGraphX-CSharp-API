using System;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示固定版本支持的 program 文件格式选项。
/// Represents file-format options supported by the pinned MIGraphX version.
/// </summary>
public sealed class MIGraphXFileOptions : IDisposable
{
    private readonly NativeResourceOwner<NativeFileOptionsHandle> owner;

    /// <summary>创建文件选项；当前固定版本只承诺 `msgpack`。 Creates file options; the pinned version only promises `msgpack`.</summary>
    public MIGraphXFileOptions(string nativeLibraryPath, string fileFormat = "msgpack")
    {
        if (fileFormat is null) { throw new ArgumentNullException(nameof(fileFormat)); }
        if (!string.Equals(fileFormat, "msgpack", StringComparison.Ordinal))
        {
            throw new NotSupportedException("Only the tested MIGraphX file format 'msgpack' is supported.");
        }
        var runtime = NativeRuntime.LoadM5(nativeLibraryPath);
        owner = new NativeResourceOwner<NativeFileOptionsHandle>(runtime, NativeFileOptionsHandle.Create(fileFormat));
        FileFormat = fileFormat;
    }

    /// <summary>获取固定文件格式名称。 Gets the fixed file-format name.</summary>
    public string FileFormat { get; }

    internal NativeResourceOwner<NativeFileOptionsHandle> Owner => owner;

    /// <summary>释放 owned file-options handle。 Releases the owned file-options handle.</summary>
    public void Dispose() => owner.Dispose();
}
