using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
                AssertBilingual(members, $"P:{type.FullName}.{property.Name}");
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(method => !method.IsSpecialName))
            {
                var parameters = method.GetParameters();
                var suffix = parameters.Length == 0 ? string.Empty : $"({string.Join(",", parameters.Select(parameter => XmlTypeName(parameter.ParameterType)))})";
                var id = $"M:{type.FullName}.{method.Name}{suffix}";
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
            Assert.Contains("compatibility/m1-binding-subset.json", text, StringComparison.Ordinal);
            Assert.Contains("a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2", text, StringComparison.Ordinal);
        });
        Assert.True(File.Exists(Path.Combine(generated, "README.md")));
    }

    [Fact]
    public void CompatibilityManifestsUseHonestValidationStates()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "planned", "statically-verified", "fake-native-executed", "runtime-executed", "not-applicable",
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
                if (status == "runtime-executed")
                {
                    Assert.Contains("official", path, StringComparison.OrdinalIgnoreCase);
                }
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
            Assert.Contains("M1", text);
            Assert.Contains("AMD GPU", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("runtime", text, StringComparison.OrdinalIgnoreCase);
        }
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
            "validation/README.md",
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
        Assert.Contains("eng/verify-m1-abi.ps1", buildWorkflow, StringComparison.Ordinal);
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
        Assert.DoesNotContain(@".\docfx.json", docsScript, StringComparison.Ordinal);
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

    private static string XmlTypeName(Type type)
    {
        if (type.IsByRef) { return $"{XmlTypeName(type.GetElementType()!)}@"; }
        return type.FullName?.Replace('+', '.') ?? type.Name;
    }

    private static bool IsGeneratedPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
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
