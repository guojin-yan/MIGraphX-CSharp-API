using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>缓存输入 shape override 的确定性表示。 A deterministic representation of an input-shape override.</summary>
public sealed class MIGraphXCacheOverride
{
    private readonly long[] dimensions;
    private readonly MIGraphXDynamicDimension[] dynamicDimensions;

    /// <summary>创建静态输入 override 的缓存表示。 Creates a static input-override representation.</summary>
    /// <param name="inputName">输入名称。 The input name.</param>
    /// <param name="dimensions">静态维度。 The static dimensions.</param>
    public MIGraphXCacheOverride(string inputName, IReadOnlyList<long> dimensions)
    {
        inputName = RequireInputName(inputName);
        if (dimensions is null) { throw new ArgumentNullException(nameof(dimensions)); }
        InputName = inputName;
        this.dimensions = dimensions.ToArray();
        dynamicDimensions = Array.Empty<MIGraphXDynamicDimension>();
        IsDynamic = false;
        for (var index = 0; index < this.dimensions.Length; index++)
        {
            if (this.dimensions[index] < 0) { throw new ArgumentOutOfRangeException(nameof(dimensions)); }
            MIGraphXDynamicDimension.ValidateSizeT(this.dimensions[index], nameof(dimensions));
        }
    }

    /// <summary>创建动态 override 的缓存表示。 Creates a dynamic override representation.</summary>
    /// <param name="inputName">输入名称。 The input name.</param>
    /// <param name="dimensions">动态范围。 The dynamic ranges.</param>
    public MIGraphXCacheOverride(string inputName, IReadOnlyList<MIGraphXDynamicDimension> dimensions)
    {
        inputName = RequireInputName(inputName);
        if (dimensions is null) { throw new ArgumentNullException(nameof(dimensions)); }
        var copied = dimensions.ToArray();
        if (copied.Any(value => value is null)) { throw new ArgumentException("Dynamic dimensions must not contain null values.", nameof(dimensions)); }
        InputName = inputName;
        this.dimensions = Array.Empty<long>();
        dynamicDimensions = copied;
        IsDynamic = true;
    }

    /// <summary>获取输入名称。 Gets the input name.</summary>
    public string InputName { get; }
    /// <summary>获取维度。 Gets the dimensions.</summary>
    public IReadOnlyList<long> Dimensions => Array.AsReadOnly(dimensions);
    /// <summary>获取动态维度范围；静态 override 返回空集合。 Gets dynamic ranges; static overrides return an empty collection.</summary>
    public IReadOnlyList<MIGraphXDynamicDimension> DynamicDimensions => Array.AsReadOnly(dynamicDimensions);
    /// <summary>获取该记录是否表示动态 override。 Gets whether this record represents a dynamic override.</summary>
    public bool IsDynamic { get; }

    private static string RequireInputName(string? value)
    {
        if (value is null) { throw new ArgumentNullException(nameof(value)); }
        if (value.Length == 0 || value.IndexOf('\0') >= 0) { throw new ArgumentException("The input name must be non-empty and must not contain NUL.", nameof(value)); }
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); }
        catch (EncoderFallbackException exception) { throw new ArgumentException("The input name must contain valid Unicode scalar values.", nameof(value), exception); }
        return value;
    }
}

/// <summary>
/// 缓存 envelope 的版本化确定性元数据。
/// Versioned deterministic metadata for a cache envelope.
/// </summary>
public sealed class MIGraphXCacheMetadata
{
    private readonly MIGraphXCacheOverride[] inputOverrides;

    /// <summary>创建规范化、版本化的缓存元数据。 Creates normalized, versioned cache metadata.</summary>
    public MIGraphXCacheMetadata(
        string modelSha256,
        string targetName,
        string compileOptions,
        string fileFormat,
        string nativeFingerprint,
        IReadOnlyList<MIGraphXCacheOverride>? inputOverrides = null,
        string? headerSha256 = null,
        string? apiIdentity = null,
        string? managedIdentity = null)
    {
        ModelSha256 = RequireHash(modelSha256, nameof(modelSha256));
        TargetName = RequireToken(targetName, nameof(targetName));
        CompileOptions = RequireToken(compileOptions, nameof(compileOptions));
        FileFormat = RequireToken(fileFormat, nameof(fileFormat));
        NativeFingerprint = RequireHash(nativeFingerprint, nameof(nativeFingerprint));
        HeaderSha256 = RequireHash(headerSha256 ?? "a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2", nameof(headerSha256));
        ApiIdentity = RequireToken(apiIdentity ?? "MIGraphX-C-API/2.15.0.70201-81~24.04", nameof(apiIdentity));
        ManagedIdentity = RequireToken(managedIdentity ?? "JYPPX.ROCm.MIGraphX.CSharp.API", nameof(managedIdentity));
        var copiedOverrides = (inputOverrides ?? Array.Empty<MIGraphXCacheOverride>()).ToArray();
        if (copiedOverrides.Any(value => value is null)) { throw new ArgumentException("Cache input overrides must not contain null values.", nameof(inputOverrides)); }
        this.inputOverrides = copiedOverrides
            .OrderBy(value => value.InputName, StringComparer.Ordinal)
            .ToArray();
        if (this.inputOverrides.GroupBy(value => value.InputName, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            throw new ArgumentException("Cache input overrides must contain unique input names.", nameof(inputOverrides));
        }
    }

    /// <summary>获取 schema 版本。 Gets the schema version.</summary>
    public int SchemaVersion => 1;
    /// <summary>获取模型 hash。 Gets the model hash.</summary>
    public string ModelSha256 { get; }
    /// <summary>获取固定 header hash。 Gets the fixed-header hash.</summary>
    public string HeaderSha256 { get; }
    /// <summary>获取 API identity。 Gets the API identity.</summary>
    public string ApiIdentity { get; }
    /// <summary>获取托管 identity。 Gets the managed identity.</summary>
    public string ManagedIdentity { get; }
    /// <summary>获取 native fingerprint。 Gets the native fingerprint.</summary>
    public string NativeFingerprint { get; }
    /// <summary>获取 target 名称。 Gets the target name.</summary>
    public string TargetName { get; }
    /// <summary>获取 compile options 表示。 Gets the compile-options representation.</summary>
    public string CompileOptions { get; }
    /// <summary>获取文件格式。 Gets the file format.</summary>
    public string FileFormat { get; }
    /// <summary>获取排序后的输入 override。 Gets ordered input overrides.</summary>
    public IReadOnlyList<MIGraphXCacheOverride> InputOverrides => Array.AsReadOnly(inputOverrides);

    /// <summary>从模型字节创建 SHA-256 标识。 Computes the model SHA-256 identity from bytes.</summary>
    /// <param name="model">模型字节。 The model bytes.</param>
    public static string ComputeModelSha256(byte[] model)
    {
        if (model is null) { throw new ArgumentNullException(nameof(model)); }
        return Hex(SHA256.Create().ComputeHash(model));
    }

    /// <summary>计算 native 文件指纹，不把路径写入 metadata。 Computes a native-file fingerprint without persisting its path.</summary>
    /// <param name="nativeLibraryPath">绝对 native 路径。 The absolute native path.</param>
    public static string ComputeNativeFingerprint(string nativeLibraryPath)
    {
        if (nativeLibraryPath is null) { throw new ArgumentNullException(nameof(nativeLibraryPath)); }
        if (!Path.IsPathRooted(nativeLibraryPath)) { throw new ArgumentException("The native library path must be absolute.", nameof(nativeLibraryPath)); }
        using (var stream = File.OpenRead(Path.GetFullPath(nativeLibraryPath)))
        using (var sha = SHA256.Create()) { return Hex(sha.ComputeHash(stream)); }
    }

    internal string CanonicalJsonWithoutPayload()
    {
        var builder = new StringBuilder();
        builder.Append("{\"schemaVersion\":1");
        Append(builder, "modelSha256", ModelSha256);
        Append(builder, "headerSha256", HeaderSha256);
        Append(builder, "apiIdentity", ApiIdentity);
        Append(builder, "managedIdentity", ManagedIdentity);
        Append(builder, "nativeFingerprint", NativeFingerprint);
        Append(builder, "targetName", TargetName);
        Append(builder, "compileOptions", CompileOptions);
        Append(builder, "fileFormat", FileFormat);
        builder.Append(",\"inputOverrides\":[");
        for (var index = 0; index < InputOverrides.Count; index++)
        {
            if (index != 0) { builder.Append(','); }
            builder.Append("{\"inputName\":").Append(Json(InputOverrides[index].InputName)).Append(",\"dimensions\":[");
            for (var dimension = 0; dimension < InputOverrides[index].Dimensions.Count; dimension++)
            {
                if (dimension != 0) { builder.Append(','); }
                builder.Append(InputOverrides[index].Dimensions[dimension].ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            builder.Append("]");
            if (InputOverrides[index].IsDynamic)
            {
                builder.Append(",\"dynamicDimensions\":[");
                for (var dimension = 0; dimension < InputOverrides[index].DynamicDimensions.Count; dimension++)
                {
                    if (dimension != 0) { builder.Append(','); }
                    var value = InputOverrides[index].DynamicDimensions[dimension];
                    builder.Append("{\"minimum\":").Append(value.Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\"maximum\":").Append(value.Maximum.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\"optimals\":[");
                    for (var optimal = 0; optimal < value.Optimals.Count; optimal++) { if (optimal != 0) { builder.Append(','); } builder.Append(value.Optimals[optimal].ToString(System.Globalization.CultureInfo.InvariantCulture)); }
                    builder.Append("]}");
                }
                builder.Append(']');
            }
            builder.Append('}');
        }
        return builder.Append("]}").ToString();
    }

    internal string CanonicalJsonWithPayload(string payloadSha256)
    {
        var withoutEnd = CanonicalJsonWithoutPayload();
        return withoutEnd.Substring(0, withoutEnd.Length - 1) + ",\"payloadSha256\":" + Json(payloadSha256) + "}";
    }

    /// <summary>依据规范化 metadata 生成缓存 key。 Computes the cache key from normalized metadata.</summary>
    public string ComputeKey()
    {
        using (var sha = SHA256.Create()) { return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(CanonicalJsonWithoutPayload()))); }
    }

    private static void Append(StringBuilder builder, string name, string value) => builder.Append(',').Append(Json(name)).Append(':').Append(Json(value));
    private static string Json(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('\"');
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '\"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (character < ' ') { builder.Append("\\u").Append(((int)character).ToString("x4", System.Globalization.CultureInfo.InvariantCulture)); }
                    else { builder.Append(character); }
                    break;
            }
        }
        return builder.Append('\"').ToString();
    }
    internal static string Hex(byte[] bytes)
    {
        var chars = new char[bytes.Length * 2];
        const string digits = "0123456789abcdef";
        for (var index = 0; index < bytes.Length; index++) { chars[index * 2] = digits[bytes[index] >> 4]; chars[index * 2 + 1] = digits[bytes[index] & 15]; }
        return new string(chars);
    }
    private static string RequireToken(string? value, string name)
    {
        if (value is null || value.Length == 0 || value.IndexOf('\0') >= 0) { throw new ArgumentException("The value must be non-empty and must not contain NUL.", name); }
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); }
        catch (EncoderFallbackException exception) { throw new ArgumentException("The value must contain valid Unicode scalar values.", name, exception); }
        return value;
    }
    private static string RequireHash(string value, string name)
    {
        if (value is null || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character))) { throw new ArgumentException("The value must be a 64-character SHA-256 hex string.", name); }
        return value.ToLowerInvariant();
    }
}

/// <summary>缓存查找结果。 A cache lookup result.</summary>
public sealed class MIGraphXCacheResult : IDisposable
{
    internal MIGraphXCacheResult(MIGraphXCacheLookupKind kind, MIGraphXProgram program, string key, MIGraphXCacheLookupKind? previousLookup = null)
    {
        Kind = kind;
        Program = program;
        Key = key;
        PreviousLookup = previousLookup;
    }

    /// <summary>获取结果状态。 Gets the result state.</summary>
    public MIGraphXCacheLookupKind Kind { get; }
    /// <summary>获取缓存 key。 Gets the cache key.</summary>
    public string Key { get; }
    /// <summary>获取拥有的 program。 Gets the owned program.</summary>
    public MIGraphXProgram Program { get; }
    /// <summary>重建前的命中状态；首次构建为 Miss，损坏条目为 Corrupt。 The pre-build state, if this result was rebuilt.</summary>
    public MIGraphXCacheLookupKind? PreviousLookup { get; }
    /// <summary>释放拥有的 program。 Releases the owned program.</summary>
    public void Dispose() => Program.Dispose();
}

/// <summary>缓存查找状态。 Cache lookup states.</summary>
public enum MIGraphXCacheLookupKind
{
    /// <summary>完整校验后命中。 Fully validated hit.</summary>
    Hit,
    /// <summary>没有已有条目。 No existing entry.</summary>
    Miss,
    /// <summary>已有条目校验失败。 Existing entry failed validation.</summary>
    Corrupt,
    /// <summary>已从 miss 或 corrupt 重建。 Rebuilt from a miss or corrupt entry.</summary>
    Rebuilt,
}

/// <summary>
/// 显式根目录的 MIGraphX program cache；不会读取全局目录、环境变量或搜索路径。
/// An explicit-root MIGraphX program cache; it does not read global directories, environment variables, or search paths.
/// </summary>
public sealed class MIGraphXModelCache
{
    private static readonly ConcurrentDictionary<string, object> KeyLocks = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
    private readonly string rootDirectory;

    /// <summary>使用显式绝对根目录创建缓存。 Creates a cache with an explicit absolute root.</summary>
    /// <param name="rootDirectory">缓存根目录。 The cache root directory.</param>
    public MIGraphXModelCache(string rootDirectory)
    {
        if (rootDirectory is null) { throw new ArgumentNullException(nameof(rootDirectory)); }
        if (!Path.IsPathRooted(rootDirectory)) { throw new ArgumentException("The cache root must be absolute.", nameof(rootDirectory)); }
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(this.rootDirectory);
    }

    /// <summary>获取显式缓存根目录。 Gets the explicit cache root.</summary>
    public string RootDirectory => rootDirectory;

    /// <summary>按 metadata 命中缓存或调用 builder 重建。 Loads a cache hit or rebuilds it with builder.</summary>
    /// <param name="metadata">规范化缓存元数据。 The normalized cache metadata.</param>
    /// <param name="fileOptions">固定文件格式选项。 The fixed file-format options.</param>
    /// <param name="builder">缓存未命中时的构建函数。 The build function used on a miss.</param>
    public MIGraphXCacheResult GetOrBuild(
        MIGraphXCacheMetadata metadata,
        MIGraphXFileOptions fileOptions,
        Func<MIGraphXProgram> builder)
    {
        if (metadata is null) { throw new ArgumentNullException(nameof(metadata)); }
        if (fileOptions is null) { throw new ArgumentNullException(nameof(fileOptions)); }
        if (builder is null) { throw new ArgumentNullException(nameof(builder)); }
        if (!string.Equals(metadata.FileFormat, fileOptions.FileFormat, StringComparison.Ordinal)) { throw new ArgumentException("Metadata and file options use different file formats.", nameof(fileOptions)); }
        var key = metadata.ComputeKey();
        var gate = KeyLocks.GetOrAdd(rootDirectory + "\0" + key, _ => new object());
        lock (gate)
        {
            var lookup = TryLoad(metadata, fileOptions, key, out var corrupt);
            if (lookup is not null) { return lookup; }
            using (var built = builder())
            {
                if (built is null) { throw new InvalidOperationException("The cache builder returned null."); }
                var payload = Path.Combine(rootDirectory, key + ".migraphx");
                var sidecar = Path.Combine(rootDirectory, key + ".json");
                var temporaryPayload = Path.Combine(rootDirectory, key + "." + Guid.NewGuid().ToString("N") + ".tmp");
                var temporarySidecar = Path.Combine(rootDirectory, key + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    built.Save(temporaryPayload, fileOptions);
                    var payloadHash = HashFile(temporaryPayload);
                    File.WriteAllText(temporarySidecar, metadata.CanonicalJsonWithPayload(payloadHash), new UTF8Encoding(false));
                    AtomicReplace(temporaryPayload, payload);
                    AtomicReplace(temporarySidecar, sidecar);
                }
                finally
                {
                    DeleteIfExists(temporaryPayload);
                    DeleteIfExists(temporarySidecar);
                }
                return new MIGraphXCacheResult(MIGraphXCacheLookupKind.Rebuilt, MIGraphXProgram.Load(payload, fileOptions), key, corrupt ? MIGraphXCacheLookupKind.Corrupt : MIGraphXCacheLookupKind.Miss);
            }
        }
    }

    private MIGraphXCacheResult? TryLoad(MIGraphXCacheMetadata metadata, MIGraphXFileOptions options, string key, out bool corrupt)
    {
        corrupt = false;
        var payload = Path.Combine(rootDirectory, key + ".migraphx");
        var sidecar = Path.Combine(rootDirectory, key + ".json");
        if (!File.Exists(payload) && !File.Exists(sidecar)) { return null; }
        if (!File.Exists(payload) || !File.Exists(sidecar)) { corrupt = true; return null; }
        try
        {
            var envelope = File.ReadAllText(sidecar, new UTF8Encoding(false));
            var prefix = metadata.CanonicalJsonWithoutPayload();
            var marker = prefix.Substring(0, prefix.Length - 1) + ",\"payloadSha256\":\"";
            if (!envelope.StartsWith(marker, StringComparison.Ordinal) || !envelope.EndsWith("\"}", StringComparison.Ordinal)) { corrupt = true; return null; }
            var payloadHash = envelope.Substring(marker.Length, envelope.Length - marker.Length - 2);
            if (payloadHash.Length != 64 || !string.Equals(payloadHash, HashFile(payload), StringComparison.OrdinalIgnoreCase)) { corrupt = true; return null; }
            return new MIGraphXCacheResult(MIGraphXCacheLookupKind.Hit, MIGraphXProgram.Load(payload, options), key);
        }
        catch
        {
            corrupt = true;
            return null;
        }
    }

    private static string HashFile(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create()) { return MIGraphXCacheMetadata.Hex(sha.ComputeHash(stream)); }
    }

    private static void AtomicReplace(string temporary, string destination)
    {
        if (File.Exists(destination))
        {
            File.Replace(temporary, destination, null);
        }
        else
        {
            File.Move(temporary, destination);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) { File.Delete(path); }
    }
}

/// <summary>与模型缓存相同语义的 program cache 别名。 Alias for the model-cache service.</summary>
public sealed class MIGraphXProgramCache
{
    private readonly MIGraphXModelCache inner;
    /// <summary>使用显式绝对根目录创建 program cache。 Creates a program cache with an explicit absolute root.</summary>
    /// <param name="rootDirectory">缓存根目录。 The cache root directory.</param>
    public MIGraphXProgramCache(string rootDirectory) { inner = new MIGraphXModelCache(rootDirectory); }
    /// <summary>获取显式缓存根目录。 Gets the explicit cache root.</summary>
    public string RootDirectory => inner.RootDirectory;
    /// <summary>加载或重建 program。 Loads or rebuilds a program.</summary>
    /// <param name="metadata">缓存元数据。 The cache metadata.</param>
    /// <param name="fileOptions">文件选项。 The file options.</param>
    /// <param name="builder">重建函数。 The rebuild function.</param>
    public MIGraphXCacheResult GetOrBuild(MIGraphXCacheMetadata metadata, MIGraphXFileOptions fileOptions, Func<MIGraphXProgram> builder) => inner.GetOrBuild(metadata, fileOptions, builder);
}
