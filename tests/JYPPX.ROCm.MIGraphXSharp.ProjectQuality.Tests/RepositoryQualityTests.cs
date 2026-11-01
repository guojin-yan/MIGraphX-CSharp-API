using System.Reflection;
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
            AssertBilingual(members, $"T:{XmlTypeName(type)}");
            // Delegate Invoke/BeginInvoke/EndInvoke methods are runtime-generated
            // members; the public ABI contract is documented on the delegate type.
            if (type.BaseType == typeof(MulticastDelegate))
            {
                continue;
            }
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
                    Assert.True(documentation.Any(character => character >= '\u3400' && character <= '\u9fff'), $"{id} parameter {parameter.Name} must contain Chinese documentation.");
                    Assert.True(documentation.Any(character => (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z')), $"{id} parameter {parameter.Name} must contain English documentation.");
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
            Assert.Contains("eng/contracts/binding-overrides.json", text, StringComparison.Ordinal);
            Assert.Contains("a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2", text, StringComparison.Ordinal);
        });
        Assert.True(File.Exists(Path.Combine(generated, "README.md")));
    }

    [Fact]
    public void ReviewedPublicBaselineMatchesExportedTypesAndManagedShape()
    {
        var assembly = typeof(MIGraphXBuildInfo).Assembly;
        var baseline = File.ReadAllLines(Path.Combine(RepositoryRoot, "eng", "contracts", "public-api.txt"));
        Assert.Contains("# schema-version: 2.0.0", baseline);
        Assert.Contains("# assembly: JYPPX.ROCm.MIGraphX.CSharp.API", baseline);
        var baselineTypes = baseline
            .Where(line => line.StartsWith("T|", StringComparison.Ordinal))
            .Select(line => line.Split('|')[2].Split(';')[0])
            .ToHashSet(StringComparer.Ordinal);
        var exportedTypes = assembly.GetExportedTypes().Select(type => type.FullName!.Replace('+', '.')).ToHashSet(StringComparer.Ordinal);
        Assert.True(baselineTypes.SetEquals(exportedTypes),
            $"Public type baseline drift. Missing: {string.Join(", ", exportedTypes.Except(baselineTypes))}; stale: {string.Join(", ", baselineTypes.Except(exportedTypes))}");
        Assert.Equal(45, baselineTypes.Count);
        Assert.Equal(309, baseline.Count(line => line.Length > 2 && line[1] == '|') - baselineTypes.Count);

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
        Assert.Equal(141, classMemberCount);

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
        var baseline = File.ReadAllLines(Path.Combine(RepositoryRoot, "eng", "contracts", "hip-interop-public-api.txt"));
        Assert.Contains("# schema-version: 2.0.0", baseline);
        Assert.Contains("# assembly: JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop", baseline);
        var baselineTypes = baseline
            .Where(line => line.StartsWith("T|", StringComparison.Ordinal))
            .Select(line => line.Split('|')[2].Split(';')[0])
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(baselineTypes.SetEquals(types.Select(type => type.FullName!)));
        Assert.Equal(12, baseline.Count(line => line.Length > 2 && line[1] == '|') - baselineTypes.Count);

        var memberCount = types.Sum(type =>
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length
            + type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length
            + type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Count(method => !method.IsSpecialName));
        Assert.Equal(12, memberCount);
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
            Assert.Contains("0.0.0", text, StringComparison.Ordinal);
            Assert.Contains("JYPPX.ROCm.MIGraphX.CSharp.API", text, StringComparison.Ordinal);
            Assert.Contains("AMD", text, StringComparison.Ordinal);
            Assert.Contains("ROCm 7.2.1", text, StringComparison.Ordinal);
            Assert.Contains("MIGraphXSharp.Workspace", text, StringComparison.Ordinal);
            Assert.Contains("docs/guides/getting-started.md", text, StringComparison.Ordinal);
            Assert.DoesNotContain("docs/validation/", text, StringComparison.Ordinal);
            Assert.DoesNotContain("docs/articles/", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReleaseCandidateChecksUseSourceBoundPackages()
    {
        var candidate = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "verify-release-candidate.ps1"));
        Assert.Contains("HEAD == origin/main == RepositoryCommit", candidate, StringComparison.Ordinal);
        Assert.Contains("-RepositoryCommit $RepositoryCommit", candidate, StringComparison.Ordinal);
        Assert.Contains("--vulnerable --include-transitive", candidate, StringComparison.Ordinal);
        Assert.Contains("publicationAuthorized = $false", File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "new-release-evidence.ps1")), StringComparison.Ordinal);

        var adapterPack = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "pack-adapter.ps1"));
        var adapterReadme = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop", "README.md"));
        var adapterPackageReadme = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "package", "HIP.Interop.README.md"));
        Assert.Contains("matching MIGraphX core version", adapterReadme, StringComparison.Ordinal);
        Assert.Contains("matching MIGraphX core version", adapterPackageReadme, StringComparison.Ordinal);
        var buildProps = File.ReadAllText(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        Assert.Contains("HIPSHARP_REPOSITORY_ROOT", buildProps, StringComparison.Ordinal);
        foreach (var projectFile in new[] {
            Path.Combine(RepositoryRoot, "src", "JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop", "JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop.csproj"),
            Path.Combine(RepositoryRoot, "tests", "JYPPX.ROCm.MIGraphXSharp.UnitTests", "JYPPX.ROCm.MIGraphXSharp.UnitTests.csproj") })
        {
            Assert.Contains("$(HipSharpRepositoryRoot)/src/JYPPX.ROCm.HipSharp/", File.ReadAllText(projectFile), StringComparison.Ordinal);
        }
        var hipSourceCheck = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "verify-hipsharp-source.ps1"));
        Assert.Contains("HipSharpRepositoryRoot", hipSourceCheck, StringComparison.Ordinal);
        Assert.Contains("HIPSHARP_REPOSITORY_ROOT", hipSourceCheck, StringComparison.Ordinal);
        var buildScript = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "build.ps1"));
        Assert.Contains("verify-hipsharp-source.ps1", buildScript, StringComparison.Ordinal);
        Assert.Contains("verify-binding-contract.ps1", buildScript, StringComparison.Ordinal);
        var buildShell = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "build.sh"));
        Assert.Contains("HIPSHARP_REPOSITORY_ROOT", buildShell, StringComparison.Ordinal);
        Assert.Contains("-HipSharpRepositoryRoot", buildShell, StringComparison.Ordinal);
        foreach (var workflow in new[] { "build.yml", "docs-pages.yml" })
        {
            var workflowText = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", workflow));
            Assert.Contains("guojin-yan/HIP-CSharp-API", workflowText, StringComparison.Ordinal);
            Assert.Contains("0c730005c37bacbd4f143b33d1f56e9ce517ac2f", workflowText, StringComparison.Ordinal);
            Assert.Contains("HipSharpRepositoryRoot", workflowText, StringComparison.Ordinal);
        }
        Assert.Contains("packageSourceMapping", adapterPack, StringComparison.Ordinal);
        Assert.Contains("JYPPX.ROCm.HIP.CSharp.API", adapterPack, StringComparison.Ordinal);
        Assert.Contains("096f82ae8d8ecb1cd3ad0e1f5e213e811656cde10767a0ddf88563747e9a1558", adapterPack, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("release-feed\\$Version\\$repositoryCommit", adapterPack, StringComparison.Ordinal);
        Assert.Contains("adapter-pack-restore\\$Version\\$repositoryCommit", adapterPack, StringComparison.Ordinal);
        Assert.Contains("adapter-pack-obj\\$Version\\$repositoryCommit", adapterPack, StringComparison.Ordinal);
        Assert.Contains("Core package repository commit mismatch", adapterPack, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(adapterPack, "BaseIntermediateOutputPath=\\$restoreIntermediateWithSlash", RegexOptions.CultureInvariant).Count);
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
            "guides/managed-objects.md",
            "guides/operation-attributes.md",
            "guides/dynamic-shapes-cache.md",
            "guides/hip-interop.md",
            "guides/advanced-apis.md",
            "guides/api-versioning.md",
            "guides/runtime-deployment.md",
            "compatibility/frameworks.md",
            "compatibility/platforms.md",
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
        Assert.Contains("eng/verify-native-abi.ps1", buildWorkflow, StringComparison.Ordinal);
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
        Assert.Contains("build.ps1", docsScript, StringComparison.Ordinal);
        Assert.Contains("Get-WorkspaceRoot", docsScript, StringComparison.Ordinal);
        Assert.DoesNotContain("verify-public-api.ps1", docsScript, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"verify-m\d+-", docsScript);
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(Path.Combine(RepositoryRoot, "eng"), "*", SearchOption.AllDirectories),
            path => Regex.IsMatch(Path.GetFileName(path), @"(?:^|-)m\d+(?:-|\.)", RegexOptions.IgnoreCase));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot, "eng", "verify-repository-layout.ps1")));
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot, "docs", "validation")));
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot, "docs", "design")));
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot, "docs", "articles")));
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot, "compatibility")));
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot, "pack")));
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(Path.Combine(RepositoryRoot, "tools")),
            path => Regex.IsMatch(Path.GetFileName(path), @"(?:^|-)m\d+(?:-|\.)", RegexOptions.IgnoreCase));
        Assert.DoesNotContain(@".\docfx.json", docsScript, StringComparison.Ordinal);

        var interopScript = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "test-interop-paths.ps1"));
        Assert.Contains("if ($NoBuild) { $runArguments += '--no-build' }", interopScript, StringComparison.Ordinal);
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

    private static string FindRepositoryRoot() => RepositoryPaths.Root;
}
