using System.Security.Cryptography;
using System.Diagnostics;
using JYPPX.ROCm.MIGraphXSharp.BindingGenerator;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.ProjectQuality.Tests;

public sealed class BindingGeneratorTests
{
    [Fact]
    public void StructuredParserPreservesEnumHandleCallbackAndAbiParameterSemantics()
    {
        const string source = """
            typedef enum { migraphx_test_zero = 0, migraphx_test_four = 4 } migraphx_test_enum;
            typedef struct migraphx_test_handle* migraphx_test_handle_t;
            typedef const struct migraphx_test_handle* const_migraphx_test_handle_t;
            typedef migraphx_status (*migraphx_test_callback)(void* state, const char* text, size_t size, bool enabled);
            migraphx_status migraphx_test_call(const char* text, size_t size, bool enabled, migraphx_test_handle_t* out);
            migraphx_status migraphx_test_variadic(const char* attributes, ...);
            """;

        var parsed = CHeaderParser.Parse(source, source);

        var value = Assert.Single(parsed.Enums);
        Assert.Equal("migraphx_test_enum", value.Name);
        Assert.Equal(4, value.Values[1].Value);

        var handle = Assert.Single(parsed.Handles);
        Assert.Equal("migraphx_test_handle_t", handle.Name);
        Assert.Equal("const_migraphx_test_handle_t", handle.ConstName);

        var callback = Assert.Single(parsed.Callbacks);
        Assert.Equal("migraphx_test_callback", callback.Name);
        Assert.Equal("size", callback.Parameters[1].ArrayLengthParameter);
        Assert.Equal("UTF-8 or byte buffer; function semantics required", callback.Parameters[1].Encoding);
        Assert.Equal("bool", callback.Parameters[3].BaseType);

        var call = parsed.Functions.Single(item => item.Name == "migraphx_test_call");
        Assert.Equal("out", call.Parameters[3].Direction);
        Assert.Equal(1, call.Parameters[3].PointerDepth);
        Assert.Equal("size", call.Parameters[0].ArrayLengthParameter);

        var variadic = parsed.Functions.Single(item => item.Name == "migraphx_test_variadic");
        Assert.Equal("variadic", variadic.Parameters[^1].Direction);
        Assert.Equal("...", variadic.Parameters[^1].CType);
    }

    [Fact]
    public void FrozenInputValidatorRejectsHashAndLengthDrift()
    {
        var path = Path.Combine(Path.GetTempPath(), $"migraphx-m3-hash-{Guid.NewGuid():N}.h");
        try
        {
            var bytes = "fixed-input"u8.ToArray();
            File.WriteAllBytes(path, bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            FrozenInputValidator.Validate(path, hash, bytes.Length);
            Assert.Throws<InvalidDataException>(() => FrozenInputValidator.Validate(path, new string('0', 64), bytes.Length));
            Assert.Throws<InvalidDataException>(() => FrozenInputValidator.Validate(path, hash, bytes.Length + 1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GeneratedM3JsonUsesSchemaKeysAndClosedClassifications()
    {
        foreach (var name in new[] { "m3-normalized-api.json", "m3-api-inventory.json", "m3-coverage-summary.json" })
        {
            var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "compatibility", name));
            Assert.Contains("\"$schema\":", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\"schema\":", text, StringComparison.Ordinal);
        }

        var model = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "compatibility", "m3-normalized-api.json"));
        Assert.Contains("\"typedefChain\":", model, StringComparison.Ordinal);
        Assert.Contains("one-byte C _Bool", model, StringComparison.Ordinal);
        Assert.Contains("unsigned 64-bit size_t", model, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedJsonUsesTheRepositoryLfPolicy()
    {
        foreach (var name in new[] { "m3-normalized-api.json", "m3-api-inventory.json", "m3-coverage-summary.json" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "compatibility", name));
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.Equal((byte)'\n', bytes[^1]);
        }
    }

    [Fact]
    public void GeneratedCSharpUsesTheRepositoryCrlfPolicy()
    {
        var generatedDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "JYPPX.ROCm.MIGraphX.CSharp.API",
            "Generated");

        foreach (var name in new[]
        {
            "NativeMethods.g.cs",
            "NativeMethods.LibraryImport.g.cs",
            "NativeMethods.DllImport.g.cs",
        })
        {
            var text = File.ReadAllText(Path.Combine(generatedDirectory, name));
            Assert.Contains("\r\n", text, StringComparison.Ordinal);
            Assert.DoesNotMatch("(?<!\\r)\\n", text);
        }
    }

    [Fact]
    public void VerifyModeDetectsByteDriftAndGenerationUsesTransactionalStaging()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "eng", "generate-interop.ps1"));
        Assert.Contains("SequenceEqual[byte]", script, StringComparison.Ordinal);
        Assert.Contains("Generated output drifted", script, StringComparison.Ordinal);
        Assert.Contains(".backup", script, StringComparison.Ordinal);
        Assert.Contains("catch {", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectedHeaderDoesNotPolluteTrackedGeneratedOutputs()
    {
        var root = FindRepositoryRoot();
        var outputs = new[]
        {
            Path.Combine(root, "compatibility", "m3-normalized-api.json"),
            Path.Combine(root, "compatibility", "m3-api-inventory.json"),
            Path.Combine(root, "compatibility", "m3-coverage-summary.json"),
            Path.Combine(root, "src", "JYPPX.ROCm.MIGraphX.CSharp.API", "Generated", "NativeMethods.g.cs"),
            Path.Combine(root, "src", "JYPPX.ROCm.MIGraphX.CSharp.API", "Generated", "NativeMethods.LibraryImport.g.cs"),
            Path.Combine(root, "src", "JYPPX.ROCm.MIGraphX.CSharp.API", "Generated", "NativeMethods.DllImport.g.cs"),
        };
        var before = outputs.ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        var badHeader = Path.Combine(Path.GetTempPath(), $"migraphx-m3-rejected-{Guid.NewGuid():N}.h");
        try
        {
            File.WriteAllText(badHeader, "migraphx_status drift(void);\n");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pwsh",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList =
                {
                    "-NoProfile",
                    "-File",
                    Path.Combine(root, "eng", "generate-interop.ps1"),
                    "-HeaderPath",
                    badHeader,
                },
            })!;
            process.WaitForExit();
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("SHA-256 mismatch", process.StandardError.ReadToEnd(), StringComparison.Ordinal);
            foreach (var path in outputs)
            {
                Assert.Equal(before[path], File.ReadAllBytes(path));
            }
        }
        finally
        {
            File.Delete(badHeader);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MIGraphXSharp.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the MIGraphXSharp repository root.");
    }
}
