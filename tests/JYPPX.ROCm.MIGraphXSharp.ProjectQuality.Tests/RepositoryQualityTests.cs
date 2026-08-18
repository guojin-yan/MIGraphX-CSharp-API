using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.ProjectQuality.Tests;

public sealed class RepositoryQualityTests
{
    private static readonly string[] ExpectedFrameworks =
    {
        "net46", "net461", "net462", "net47", "net471", "net472", "net48", "net481",
        "netcoreapp3.1", "net5.0", "net6.0", "net7.0", "net8.0", "net9.0", "net10.0",
    };

    private static string RepositoryRoot => FindRepositoryRoot();

    [Fact]
    public void CentralTargetFrameworkMatrixIsExact()
    {
        var props = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        var value = props.Descendants("JYPPXManagedTargetFrameworks").Single().Value;
        Assert.Equal(ExpectedFrameworks, value.Split(';'));

        var project = XDocument.Load(Path.Combine(
            RepositoryRoot,
            "src",
            "JYPPX.ROCm.MIGraphX.CSharp.API",
            "JYPPX.ROCm.MIGraphX.CSharp.API.csproj"));
        Assert.Equal("$(JYPPXManagedTargetFrameworks)", project.Descendants("TargetFrameworks").Single().Value);
    }

    [Fact]
    public void ProjectIdentityIsFrozen()
    {
        var props = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        Assert.Equal("JYPPX.ROCm.MIGraphX.CSharp.API", props.Descendants("MIGraphXSharpPackageId").Single().Value);
        Assert.Equal("https://github.com/guojin-yan/MIGraphX-CSharp-API", props.Descendants("MIGraphXSharpRepositoryUrl").Single().Value);
        Assert.Equal("Guojin Yan", props.Descendants("Authors").Single().Value);
        Assert.Equal("Copyright 2026 Guojin Yan", props.Descendants("Copyright").Single().Value);
        Assert.Equal("Apache-2.0", props.Descendants("PackageLicenseExpression").Single().Value);

        var license = File.ReadAllText(Path.Combine(RepositoryRoot, "LICENSE"));
        Assert.Contains("Apache License", license, StringComparison.Ordinal);
        Assert.Contains("Version 2.0, January 2004", license, StringComparison.Ordinal);
        Assert.Contains("END OF TERMS AND CONDITIONS", license, StringComparison.Ordinal);
        Assert.Equal(
            "MIGraphX-CSharp-API\nCopyright 2026 Guojin Yan\n\nThis product is licensed under the Apache License, Version 2.0.",
            File.ReadAllText(Path.Combine(RepositoryRoot, "NOTICE")).Replace("\r\n", "\n").TrimEnd());

        var assembly = typeof(MIGraphXBuildInfo).Assembly;
        Assert.Equal("JYPPX.ROCm.MIGraphX.CSharp.API", assembly.GetName().Name);
        Assert.All(assembly.GetExportedTypes(), type => Assert.StartsWith("JYPPX.ROCm.MIGraphXSharp", type.Namespace));
    }

    [Fact]
    public void LegacyTopLevelNamespaceDoesNotReturnToSourceOrProjects()
    {
        var legacyCodeNamespace = new Regex(
            @"\b(namespace|using)\s+JYPPX\.ROCm\.MIGraphX(?:\s*[;{]|\.)",
            RegexOptions.CultureInvariant);
        var sourceViolations = Directory.EnumerateFiles(RepositoryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !File.ReadAllText(path).Contains("namespace JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop", StringComparison.Ordinal))
            .Where(path => !File.ReadAllText(path).Contains("using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;", StringComparison.Ordinal))
            .Where(path => legacyCodeNamespace.IsMatch(File.ReadAllText(path)))
            .ToArray();
        Assert.Empty(sourceViolations);

        var projectViolations = Directory.EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(XDocument.Load)
            .SelectMany(document => document.Descendants()
                .Where(element => element.Name.LocalName == "RootNamespace")
                .Select(element => element.Value))
            .Where(value => value == "JYPPX.ROCm.MIGraphX" || value.StartsWith("JYPPX.ROCm.MIGraphX.", StringComparison.Ordinal))
            .Where(value => value != "JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop")
            .ToArray();
        Assert.Empty(projectViolations);
    }

    [Fact]
    public void PublicApiXmlDocumentationIsBilingual()
    {
        var assemblyPath = typeof(MIGraphXBuildInfo).Assembly.Location;
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        Assert.True(File.Exists(xmlPath), $"Missing XML documentation: {xmlPath}");

        var members = XDocument.Load(xmlPath).Descendants("member").ToDictionary(
            element => element.Attribute("name")!.Value,
            element => element);

        foreach (var type in typeof(MIGraphXBuildInfo).Assembly.GetExportedTypes())
        {
            AssertBilingual(members, $"T:{type.FullName}");
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (field.IsSpecialName) { continue; }
                AssertBilingual(members, $"F:{type.FullName}.{field.Name}");
            }
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var indexParameters = property.GetIndexParameters();
                var indexSuffix = indexParameters.Length == 0
                    ? string.Empty
                    : $"({string.Join(",", indexParameters.Select(parameter => XmlTypeName(parameter.ParameterType)))})";
                AssertBilingual(members, $"P:{type.FullName}.{property.Name}{indexSuffix}");
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(method => !method.IsSpecialName))
            {
                var parameters = method.GetParameters();
                var suffix = parameters.Length == 0 ? string.Empty : $"({string.Join(",", parameters.Select(parameter => XmlTypeName(parameter.ParameterType)))})";
                var genericSuffix = method.IsGenericMethodDefinition ? $"``{method.GetGenericArguments().Length}" : string.Empty;
                var id = $"M:{type.FullName}.{method.Name}{genericSuffix}{suffix}";
                AssertBilingual(members, id);
                var member = members[id];
                foreach (var parameter in parameters)
                {
                    var documentation = member.Elements("param").SingleOrDefault(element => element.Attribute("name")?.Value == parameter.Name)?.Value ?? string.Empty;
                    Assert.Matches("[\\u3400-\\u9fff]", documentation);
                    Assert.Matches("[A-Za-z]", documentation);
                }
            }
        }
    }

    [Fact]
    public void GeneratedBindingsAreTraceableToTheFrozenManifest()
    {
        var generated = Path.Combine(RepositoryRoot, "src", "JYPPX.ROCm.MIGraphX.CSharp.API", "Generated");
        var files = Directory.EnumerateFiles(generated, "*.cs", SearchOption.AllDirectories).ToArray();
        Assert.Equal(3, files.Length);
        Assert.All(files, path =>
        {
            var text = File.ReadAllText(path);
            Assert.Contains("<auto-generated />", text, StringComparison.Ordinal);
            Assert.Contains("compatibility/m2-binding-subset.json", text, StringComparison.Ordinal);
            Assert.Contains("a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2", text, StringComparison.Ordinal);
        });
        Assert.True(File.Exists(Path.Combine(generated, "README.md")));
    }

    [Fact]
    public void CompatibilityManifestsUseHonestValidationStates()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "planned", "statically-verified", "fake-native-executed", "runtime-deferred", "runtime-executed", "not-applicable",
        };
        var compatibility = Path.Combine(RepositoryRoot, "compatibility");

        foreach (var path in Directory.EnumerateFiles(compatibility, "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(document.RootElement.TryGetProperty("$schema", out _), $"Missing $schema in {path}");
            Assert.True(document.RootElement.TryGetProperty("schemaVersion", out _), $"Missing schemaVersion in {path}");

            foreach (var status in EnumerateStatuses(document.RootElement))
            {
                Assert.Contains(status, allowed);
            }
        }

        var runtimeMatrixPath = Path.Combine(compatibility, "runtime-validation-matrix.json");
        using var runtimeMatrix = JsonDocument.Parse(File.ReadAllText(runtimeMatrixPath));
        var runtimeExecuted = runtimeMatrix.RootElement.GetProperty("validations")
            .EnumerateArray()
            .Where(item => item.GetProperty("status").GetString() == "runtime-executed")
            .ToArray();
        var expectedRuntimeIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "official-native-loader",
            "official-target-program-lifecycle",
            "official-onnx-parse-compile-run",
            "amd-gpu-execution",
            "m9-official-option-smoke",
            "m10-official-onnx-registry",
            "m10-official-argument-comparison",
            "m10-official-program-comparison",
        };

        Assert.True(
            expectedRuntimeIds.SetEquals(runtimeExecuted.Select(item => item.GetProperty("id").GetString()!)),
            "Only the independently reviewed M1/M2/M9/M10 official runtime entries may be runtime-executed.");
        Assert.All(runtimeExecuted, item =>
        {
            var expectedCommit = item.GetProperty("id").GetString()!.StartsWith("m10-", StringComparison.Ordinal)
                ? "e2386dc69e7640f8ff12d95284e56c3f02c87938"
                : "346cdd0b01a7f8039f5deb93058928403fccc7dd";
            Assert.Contains(expectedCommit, item.GetProperty("evidence").GetString(), StringComparison.Ordinal);
        });
        Assert.Equal(
            "not-applicable",
            runtimeMatrix.RootElement.GetProperty("validations")
                .EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == "m7-runtime-package")
                .GetProperty("status").GetString());
    }

    [Fact]
    public void M5HighLevelCoverageAndOwnershipAreClosed()
    {
        var compatibility = Path.Combine(RepositoryRoot, "compatibility");
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(compatibility, "m3-api-inventory.json")));
        using var map = JsonDocument.Parse(File.ReadAllText(Path.Combine(compatibility, "m4-high-level-api-map.json")));
        using var ownership = JsonDocument.Parse(File.ReadAllText(Path.Combine(compatibility, "m4-public-ownership.json")));

        var inventoryIds = inventory.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)
            .ToArray();
        var mappings = map.RootElement.GetProperty("mappings").EnumerateArray().ToArray();
        Assert.Equal(192, mappings.Length);
        Assert.Equal(inventoryIds, mappings.Select(item => item.GetProperty("id").GetString()));
        Assert.Equal(74, mappings.Count(item => item.GetProperty("supportStatus").GetString() == "supported"));
        Assert.Equal(117, mappings.Count(item => item.GetProperty("supportStatus").GetString() == "planned"));
        Assert.Single(mappings, item => item.GetProperty("supportStatus").GetString() == "unsupported");
        Assert.All(mappings.Where(item => item.GetProperty("supportStatus").GetString() == "supported"), item =>
        {
            Assert.NotEmpty(item.GetProperty("publicMembers").EnumerateArray());
            Assert.NotEmpty(item.GetProperty("tests").EnumerateArray());
            Assert.Equal("fake-native-executed", item.GetProperty("validationLevel").GetString());
        });

        var ownershipTypes = ownership.RootElement.GetProperty("types").EnumerateArray().ToArray();
        Assert.Equal(13, ownershipTypes.Length);
        Assert.Equal(13, ownershipTypes.Select(item => item.GetProperty("type").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            "unsupported",
            mappings.Single(item => item.GetProperty("id").GetString() == "function:migraphx_operation_create")
                .GetProperty("supportStatus").GetString());
    }

    [Fact]
    public void M10DecisionsAggregateCountsAndOwnershipAreClosed()
    {
        var compatibility = Path.Combine(RepositoryRoot, "compatibility");
        using var map = JsonDocument.Parse(File.ReadAllText(Path.Combine(compatibility, "m10-high-level-api-map.json")));
        using var ownership = JsonDocument.Parse(File.ReadAllText(Path.Combine(compatibility, "m10-public-ownership.json")));
        var root = map.RootElement;
        Assert.Equal("M10", root.GetProperty("stage").GetString());
        Assert.Equal(84, root.GetProperty("counts").GetProperty("supported").GetInt32());
        Assert.Equal(107, root.GetProperty("counts").GetProperty("planned").GetInt32());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("unsupported").GetInt32());

        var mappings = root.GetProperty("mappings").EnumerateArray().ToArray();
        Assert.Equal(5, mappings.Length);
        Assert.Equal(4, mappings.Count(item => item.GetProperty("decision").GetString() == "adopted"));
        var shape = mappings.Single(item => item.GetProperty("id").GetString() == "function:migraphx_shape_equal");
        Assert.Equal("retained-planned", shape.GetProperty("decision").GetString());
        Assert.Equal("statically-verified", shape.GetProperty("validationLevel").GetString());
        Assert.False(string.IsNullOrWhiteSpace(shape.GetProperty("notAdoptedReason").GetString()));
        Assert.Equal(4, ownership.RootElement.GetProperty("records").GetArrayLength());
    }

    [Fact]
    public void M11RuntimePlanIsPackageOnlyReviewRequiredAndAuthorized()
    {
        var compatibility = Path.Combine(RepositoryRoot, "compatibility");
        using var matrix = JsonDocument.Parse(File.ReadAllText(Path.Combine(compatibility, "m11-runtime-cases.json")));
        var root = matrix.RootElement;
        Assert.Equal("M11", root.GetProperty("stage").GetString());
        Assert.Equal("0.9.0-rc.9", root.GetProperty("candidateVersion").GetString());
        Assert.True(root.GetProperty("authorization").GetProperty("officialFunctionalAuthorized").GetBoolean());
        Assert.True(root.GetProperty("authorization").GetProperty("longRunAuthorized").GetBoolean());
        Assert.True(root.GetProperty("authorization").GetProperty("timingAuthorized").GetBoolean());
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.True(cases.Length >= 20);
        Assert.All(cases, item => Assert.Contains(item.GetProperty("officialEvidence").GetString(), new[] { "runtime-deferred", "not-applicable" }));
        Assert.Equal(
            "not-applicable",
            cases.Single(item => item.GetProperty("id").GetString() == "m11-windows-native-policy").GetProperty("officialEvidence").GetString());

        using var promotion = JsonDocument.Parse(File.ReadAllText(Path.Combine(compatibility, "m10-post-build-runtime-evidence.json")));
        Assert.Equal("e2386dc69e7640f8ff12d95284e56c3f02c87938", promotion.RootElement.GetProperty("sourceSha").GetString());
        Assert.Equal(4, promotion.RootElement.GetProperty("promotions").GetArrayLength());
        Assert.True(promotion.RootElement.GetProperty("historicalCandidateImmutable").GetBoolean());

        var probeRoot = Path.Combine(RepositoryRoot, "tools", "m11-runtime-probe");
        var project = File.ReadAllText(Path.Combine(probeRoot, "M11RuntimeProbe.csproj"));
        var runner = File.ReadAllText(Path.Combine(probeRoot, "Program.cs"));
        var review = File.ReadAllText(Path.Combine(probeRoot, "review.ps1"));
        Assert.DoesNotContain("ProjectReference", project, StringComparison.Ordinal);
        Assert.Contains("PackageReference", project, StringComparison.Ordinal);
        Assert.Contains("runtime-candidate-executed-review-required", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("runtime-executed", runner, StringComparison.Ordinal);
        Assert.Contains("reviewedEvidence = 'runtime-executed'", review, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewedPublicBaselineMatchesExportedTypesAndM4Shape()
    {
        var assembly = typeof(MIGraphXBuildInfo).Assembly;
        var baseline = File.ReadAllLines(Path.Combine(RepositoryRoot, "compatibility", "managed-public-api.txt"));
        Assert.Contains("# schema-version: 2.0.0", baseline);
        Assert.Contains("# assembly: JYPPX.ROCm.MIGraphX.CSharp.API", baseline);
        var baselineTypes = baseline
            .Where(line => line.StartsWith("T|", StringComparison.Ordinal))
            .Select(line => line.Split('|')[2].Split(';')[0])
            .ToHashSet(StringComparer.Ordinal);
        var exportedTypes = assembly.GetExportedTypes().Select(type => type.FullName!).ToHashSet(StringComparer.Ordinal);
        Assert.True(baselineTypes.SetEquals(exportedTypes),
            $"Public type baseline drift. Missing: {string.Join(", ", exportedTypes.Except(baselineTypes))}; stale: {string.Join(", ", baselineTypes.Except(exportedTypes))}");
        Assert.Equal(27, baselineTypes.Count);
        Assert.Equal(160, baseline.Count(line => line.Length > 2 && line[1] == '|') - baselineTypes.Count);

        var m5Types = new[]
        {
            typeof(MIGraphXShapeDataType), typeof(MIGraphXShape), typeof(MIGraphXTarget),
            typeof(MIGraphXProgram), typeof(MIGraphXArgument), typeof(MIGraphXOnnxOptions),
            typeof(MIGraphXCompileOptions), typeof(MIGraphXParameterMap), typeof(MIGraphXArgumentCollection),
            typeof(MIGraphXDynamicDimension), typeof(MIGraphXFileOptions), typeof(MIGraphXCacheOverride),
            typeof(MIGraphXCacheMetadata), typeof(MIGraphXCacheResult), typeof(MIGraphXCacheLookupKind),
            typeof(MIGraphXModelCache), typeof(MIGraphXProgramCache),
        };
        Assert.Equal(17, m5Types.Length);
        Assert.Equal(10, typeof(MIGraphXShapeDataType).GetFields(BindingFlags.Public | BindingFlags.Static).Length);

        var classMemberCount = m5Types.Where(type => !type.IsEnum).Sum(type =>
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length
            + type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Count(field => !field.IsSpecialName)
            + type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length
            + type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Count(method => !method.IsSpecialName));
        Assert.Equal(106, classMemberCount);

        foreach (var type in m5Types)
        {
            Assert.DoesNotContain(".Interop", type.FullName, StringComparison.Ordinal);
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(method.GetParameters(), parameter => ContainsRawPointerType(parameter.ParameterType));
                Assert.False(ContainsRawPointerType(method.ReturnType));
            }
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.False(ContainsRawPointerType(property.PropertyType));
            }
        }
    }

    [Fact]
    public void M6AdapterPublicBaselineIsSmallBilingualAndPointerFree()
    {
        var assembly = typeof(MIGraphXHipAsyncRun).Assembly;
        var types = assembly.GetExportedTypes();
        Assert.Equal(3, types.Length);
        Assert.All(types, type => Assert.StartsWith("JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop", type.Namespace));
        var baseline = File.ReadAllLines(Path.Combine(RepositoryRoot, "compatibility", "m6-adapter-public-api.txt"));
        Assert.Contains("# schema-version: 2.0.0", baseline);
        Assert.Contains("# assembly: JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop", baseline);
        var baselineTypes = baseline
            .Where(line => line.StartsWith("T|", StringComparison.Ordinal))
            .Select(line => line.Split('|')[2].Split(';')[0])
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(baselineTypes.SetEquals(types.Select(type => type.FullName!)));
        Assert.Equal(11, baseline.Count(line => line.Length > 2 && line[1] == '|') - baselineTypes.Count);

        var memberCount = types.Sum(type =>
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length
            + type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length
            + type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Count(method => !method.IsSpecialName));
        Assert.Equal(11, memberCount);
        foreach (var type in types)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(method.GetParameters(), parameter => ContainsRawPointerType(parameter.ParameterType));
                Assert.False(ContainsRawPointerType(method.ReturnType));
            }
            Assert.All(type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                property => Assert.False(ContainsRawPointerType(property.PropertyType)));
        }

        var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
        var xmlMembers = XDocument.Load(xmlPath).Descendants("member").ToDictionary(
            element => element.Attribute("name")!.Value,
            element => element);
        foreach (var type in types)
        {
            AssertBilingual(xmlMembers, $"T:{type.FullName}");
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                AssertBilingual(xmlMembers, $"P:{type.FullName}.{property.Name}");
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(method => !method.IsSpecialName))
            {
                var parameters = method.GetParameters();
                var suffix = parameters.Length == 0 ? string.Empty : $"({string.Join(",", parameters.Select(parameter => XmlTypeName(parameter.ParameterType)))})";
                AssertBilingual(xmlMembers, $"M:{type.FullName}.{method.Name}{suffix}");
            }
            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var parameters = constructor.GetParameters();
                var suffix = parameters.Length == 0 ? string.Empty : $"({string.Join(",", parameters.Select(parameter => XmlTypeName(parameter.ParameterType)))})";
                AssertBilingual(xmlMembers, $"M:{type.FullName}.#ctor{suffix}");
            }
        }
    }

    [Fact]
    public void ReadmesHaveCorrespondingStructureAndNoCapabilityOverstatement()
    {
        var english = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));
        var chinese = File.ReadAllText(Path.Combine(RepositoryRoot, "README.zh-CN.md"));
        Assert.Equal(HeadingLevels(english), HeadingLevels(chinese));

        foreach (var text in new[] { english, chinese })
        {
            Assert.Contains("0.0.0", text);
            Assert.Contains("0.9.0-rc.1", text);
            Assert.Contains("0.9.0-rc.2", text);
            Assert.Contains("release-candidate-local", text, StringComparison.Ordinal);
            Assert.Contains("M1", text);
            Assert.Contains("AMD", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("GPU", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("runtime", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("346cdd0b01a7f8039f5deb93058928403fccc7dd", text, StringComparison.Ordinal);
            Assert.Contains("gfx1100", text, StringComparison.Ordinal);
            Assert.Contains("system-native", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("84", text, StringComparison.Ordinal);
            Assert.Contains("runtime-deferred", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void M8ReleaseCandidatePolicyIsSourceBoundAndFailClosed()
    {
        using var matrix = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "compatibility", "runtime-validation-matrix.json")));
        Assert.Equal("release-candidate-local", matrix.RootElement.GetProperty("m8Status").GetString());
        var validations = matrix.RootElement.GetProperty("validations").EnumerateArray().ToArray();
        Assert.Contains(validations, item => item.GetProperty("id").GetString() == "m8-public-api-freeze" && item.GetProperty("status").GetString() == "statically-verified");
        Assert.Contains(validations, item => item.GetProperty("id").GetString() == "m8-official-system-native-session" && item.GetProperty("status").GetString() == "runtime-deferred");

        var candidate = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "verify-release-candidate.ps1"));
        Assert.Contains("HEAD == origin/main == RepositoryCommit", candidate, StringComparison.Ordinal);
        Assert.Contains("--vulnerable --include-transitive", candidate, StringComparison.Ordinal);
        Assert.Contains("publicationAuthorized = $false", File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "new-release-evidence.ps1")), StringComparison.Ordinal);

        var adapterPack = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "pack-adapter.ps1"));
        Assert.Contains("packageSourceMapping", adapterPack, StringComparison.Ordinal);
        Assert.Contains("JYPPX.ROCm.HIP.CSharp.API", adapterPack, StringComparison.Ordinal);
        Assert.Contains("e71398538d7ff5db91c018cac3a2ff57c4d89e71aa77b50942182bd90a2a5fd2", adapterPack, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkdownLocalLinksResolve()
    {
        var markdownFiles = Directory.EnumerateFiles(RepositoryRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .ToArray();
        var linkPattern = new Regex(@"(?<!!)\[[^\]]+\]\((?<target><[^>]+>|[^)]+)\)", RegexOptions.CultureInvariant);
        var failures = new List<string>();

        foreach (var markdownPath in markdownFiles)
        {
            var markdown = File.ReadAllText(markdownPath);
            foreach (Match match in linkPattern.Matches(markdown))
            {
                var target = match.Groups["target"].Value.Trim('<', '>');
                var pathPart = target.Split('#')[0];
                if (string.IsNullOrEmpty(pathPart)
                    || Uri.TryCreate(pathPart, UriKind.Absolute, out _)
                    || pathPart.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(markdownPath)!, Uri.UnescapeDataString(pathPart)));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    failures.Add($"{Path.GetRelativePath(RepositoryRoot, markdownPath)} -> {pathPart}");
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void TrackedTextContainsNoSensitiveOrMachineSpecificPaths()
    {
        var markers = new[]
        {
            "C:" + @"\Users\" + "guoji",
            "E:" + @"\Git" + "Space",
            "/home/" + "guoji",
            "BEGIN OPENSSH " + "PRIVATE KEY",
            "BEGIN " + "PRIVATE KEY",
        };
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csproj", ".props", ".targets", ".ps1", ".sh", ".json", ".md", ".txt", ".yml", ".yaml",
        };
        var violations = Directory.EnumerateFiles(RepositoryRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .SelectMany(path => markers.Where(marker => File.ReadAllText(path).Contains(marker, StringComparison.OrdinalIgnoreCase)).Select(marker => $"{Path.GetRelativePath(RepositoryRoot, path)}: {marker}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void NativeTestSourcesAvoidGccMisleadingIndentationPattern()
    {
        var violations = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "native"), "*.c", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, Number: index + 1))
                .Where(item => Regex.IsMatch(item.Line, @"^\s*if\s*\([^\r\n]*\)\s+[^\{\r\n]+;\s+\S")))
            .Select(item => $"{Path.GetRelativePath(RepositoryRoot, item.Path)}:{item.Number}")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void DocumentationNavigationAndWorkflowsArePresent()
    {
        var toc = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "toc.yml"));
        foreach (var href in new[]
        {
            "guides/getting-started.md",
            "compatibility/frameworks.md",
            "compatibility/platforms.md",
            "design/m0-native-baseline.md",
            "design/m1-direct-pinvoke.md",
            "design/m2-onnx-workflow.md",
            "design/m3-binding-generator.md",
            "design/m4-managed-object-model.md",
            "design/m5-dynamic-shape-cache.md",
            "design/m6-hip-async-interop.md",
            "design/m7-runtime-packaging.md",
            "validation/README.md",
            "validation/m2-local-validation.md",
            "validation/m3-local-validation.md",
            "validation/m4-local-validation.md",
            "validation/m5-local-validation.md",
            "validation/m6-local-validation.md",
            "validation/m7-local-validation.md",
            "guides/runtime-deployment.md",
            "guides/managed-objects.md",
            "guides/api-versioning.md",
            "design/m8-api-release-readiness.md",
            "design/m9-inference-options.md",
            "design/m10-onnx-registry-native-comparison.md",
            "validation/m8-local-validation.md",
            "validation/m8-runtime-methodology.md",
            "validation/m9-cloud-validation.md",
            "validation/m10-local-validation.md",
            "validation/m10-runtime-plan.md",
            "validation/m11-runtime-hardening-plan.md",
            "articles/m0-m8-evidence-driven-wrapper.md",
            "articles/m9-interface-options-cloud-record.md",
            "articles/m10-explainable-c-api-introspection.md",
            "releases/0.9.0-rc.2.md",
            "releases/0.9.0-rc.8.md",
            "releases/0.9.0-rc.7.md",
            "releases/0.9.0-rc.6.md",
            "releases/0.9.0-rc.5.md",
            "releases/0.9.0-rc.4.md",
            "releases/0.9.0-rc.3.md",
            "releases/0.9.0-rc.1.md",
            "articles/m4-resource-safe-dotnet.md",
            "articles/m5-dynamic-shape-cache.md",
            "articles/m6-hip-async-copy-boundary.md",
            "releases/0.0.0.md",
            "api/index.md",
        })
        {
            Assert.Contains($"href: {href}", toc, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(RepositoryRoot, "docs", href.Replace('/', Path.DirectorySeparatorChar))));
        }

        var buildWorkflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "build.yml"));
        Assert.Contains("actions/checkout@v7", buildWorkflow, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@v6", buildWorkflow, StringComparison.Ordinal);
        Assert.Contains("eng/verify-package.ps1", buildWorkflow, StringComparison.Ordinal);
        Assert.Contains("eng/generate-interop.ps1", buildWorkflow, StringComparison.Ordinal);
        Assert.Contains("eng/verify-m2-abi.ps1", buildWorkflow, StringComparison.Ordinal);
        Assert.Contains("eng/verify-m3-abi.ps1", buildWorkflow, StringComparison.Ordinal);
        Assert.Contains("eng/verify-m4-coverage.ps1", buildWorkflow, StringComparison.Ordinal);
        Assert.Contains("eng/verify-m10-coverage.ps1", buildWorkflow, StringComparison.Ordinal);
        Assert.Contains("eng/verify-m11-coverage.ps1", buildWorkflow, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", buildWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request:", buildWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request_target", buildWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("push:", buildWorkflow, StringComparison.Ordinal);

        var pagesWorkflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "docs-pages.yml"));
        Assert.Contains("actions/upload-pages-artifact@v5", pagesWorkflow, StringComparison.Ordinal);
        Assert.Contains("actions/deploy-pages@v5", pagesWorkflow, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", pagesWorkflow, StringComparison.Ordinal);
        Assert.Contains("default: false", pagesWorkflow, StringComparison.Ordinal);
        Assert.Contains("if: inputs.deploy && github.ref == 'refs/heads/main'", pagesWorkflow, StringComparison.Ordinal);
        Assert.Contains("pages: write", pagesWorkflow, StringComparison.Ordinal);
        Assert.Contains("id-token: write", pagesWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("push:", pagesWorkflow, StringComparison.Ordinal);

        var docsScript = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "docs.ps1"));
        Assert.Contains("Join-Path $root 'docfx.json'", docsScript, StringComparison.Ordinal);
        Assert.Contains("verify-m9-coverage.ps1", docsScript, StringComparison.Ordinal);
        Assert.Contains("verify-m10-coverage.ps1", docsScript, StringComparison.Ordinal);
        Assert.Contains("verify-m11-coverage.ps1", docsScript, StringComparison.Ordinal);
        Assert.DoesNotContain(@".\docfx.json", docsScript, StringComparison.Ordinal);

        var interopScript = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "test-interop-paths.ps1"));
        Assert.Contains("if ($NoBuild) { $runArguments += '--no-build' }", interopScript, StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateStatuses(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if ((property.NameEquals("status") || property.NameEquals("validationLevel"))
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    yield return property.Value.GetString()!;
                }

                foreach (var nested in EnumerateStatuses(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in EnumerateStatuses(item))
                {
                    yield return nested;
                }
            }
        }
    }

    private static void AssertBilingual(IReadOnlyDictionary<string, XElement> members, string id)
    {
        Assert.True(members.TryGetValue(id, out var member), $"Missing XML documentation for {id}");
        var summary = member!.Element("summary")?.Value ?? string.Empty;
        Assert.Matches("[\\u3400-\\u9fff]", summary);
        Assert.Matches("[A-Za-z]", summary);
    }

    private static int[] HeadingLevels(string markdown) => markdown
        .Split('\n')
        .Where(line => Regex.IsMatch(line, @"^#{1,6}\s"))
        .Select(line => line.TakeWhile(character => character == '#').Count())
        .ToArray();

    private static bool ContainsRawPointerType(Type type)
    {
        if (type == typeof(IntPtr) || type == typeof(UIntPtr) || type.IsPointer) { return true; }
        if (type.IsArray || type.IsByRef) { return ContainsRawPointerType(type.GetElementType()!); }
        return type.IsGenericType && type.GetGenericArguments().Any(ContainsRawPointerType);
    }

    private static string XmlTypeName(Type type)
    {
        if (type.IsByRef) { return $"{XmlTypeName(type.GetElementType()!)}@"; }
        if (type.IsArray) { return $"{XmlTypeName(type.GetElementType()!)}[]"; }
        if (type.IsGenericParameter)
        {
            return $"{(type.DeclaringMethod is null ? "`" : "``")}{type.GenericParameterPosition}";
        }
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition().FullName!.Split('`')[0];
            return $"{definition}{{{string.Join(",", type.GetGenericArguments().Select(XmlTypeName))}}}";
        }
        return type.FullName?.Replace('+', '.') ?? type.Name;
    }

    private static bool IsGeneratedPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}.cache{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}api{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !path.Contains($"{Path.DirectorySeparatorChar}docs{Path.DirectorySeparatorChar}api{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

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
