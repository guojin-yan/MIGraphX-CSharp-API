using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JYPPX.ROCm.MIGraphXSharp.BindingGenerator;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = Arguments.Parse(args);
            BindingGenerator.Generate(options);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}

internal sealed record Arguments(
    string HeaderPath,
    string PreprocessedPath,
    string M2ManifestPath,
    string ClassificationPath,
    string UnsupportedPath,
    string OutputDirectory)
{
    internal static Arguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("BindingGenerator arguments must be --name value pairs.");
            }

            values.Add(args[index], args[index + 1]);
        }

        string Required(string name) => values.TryGetValue(name, out var value)
            ? Path.GetFullPath(value)
            : throw new ArgumentException($"Missing required argument {name}.");

        return new Arguments(
            Required("--header"),
            Required("--preprocessed"),
            Required("--m2-manifest"),
            Required("--classification"),
            Required("--unsupported"),
            Required("--output"));
    }
}

public static class FrozenInputValidator
{
    public static void Validate(string path, string expectedSha256, long expectedLength)
    {
        var bytes = File.ReadAllBytes(path);
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Frozen header SHA-256 mismatch. Expected {expectedSha256}, got {actualHash}.");
        }

        if (bytes.LongLength != expectedLength)
        {
            throw new InvalidDataException($"Frozen header length mismatch. Expected {expectedLength}, got {bytes.LongLength}.");
        }
    }
}

public sealed record SourceLocation(int Line, int Column);

public sealed class CParameter
{
    public required string Name { get; init; }
    public required string CType { get; init; }
    public required string BaseType { get; init; }
    public required int PointerDepth { get; init; }
    public required bool IsConst { get; init; }
    public string Direction { get; set; } = "in";
    public bool Nullable { get; set; }
    public string? ArrayLengthParameter { get; set; }
    public string? Encoding { get; set; }
    public string Ownership { get; set; } = "value";
    public string? ManagedType { get; set; }
    public string? ManagedModifier { get; set; }
}

public sealed class CFunction
{
    public required string Name { get; init; }
    public required string ReturnType { get; init; }
    public required IReadOnlyList<CParameter> Parameters { get; init; }
    public required SourceLocation Source { get; init; }
    public required string CanonicalDeclaration { get; init; }
}

public sealed class CCallback
{
    public required string Name { get; init; }
    public required string ReturnType { get; init; }
    public required IReadOnlyList<CParameter> Parameters { get; init; }
    public required SourceLocation Source { get; init; }
    public required string CanonicalDeclaration { get; init; }
}

public sealed record CEnumValue(string Name, int Value);

public sealed class CEnum
{
    public required string Name { get; init; }
    public required IReadOnlyList<CEnumValue> Values { get; init; }
    public required SourceLocation Source { get; init; }
    public required string CanonicalDeclaration { get; init; }
}

public sealed class CHandle
{
    public required string Name { get; init; }
    public required string ConstName { get; init; }
    public required string StructTag { get; init; }
    public required SourceLocation Source { get; init; }
    public required string CanonicalDeclaration { get; init; }
}

public sealed class ParsedHeader
{
    public required IReadOnlyList<CFunction> Functions { get; init; }
    public required IReadOnlyList<CEnum> Enums { get; init; }
    public required IReadOnlyList<CHandle> Handles { get; init; }
    public required IReadOnlyList<CCallback> Callbacks { get; init; }
}

internal sealed class InventoryItem
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string CName { get; init; }
    public required string Classification { get; init; }
    public required string Milestone { get; init; }
    public required string ValidationLevel { get; init; }
    public required SourceLocation Source { get; init; }
}

internal readonly record struct CToken(string Text);

public static class CHeaderParser
{
    public static ParsedHeader Parse(string preprocessedSource, string originalSource)
    {
        var tokens = Tokenize(preprocessedSource);
        var statements = SplitStatements(tokens);
        var enums = new List<CEnum>();
        var mutableHandles = new List<(string Name, string Tag, string Declaration)>();
        var constHandles = new Dictionary<string, string>(StringComparer.Ordinal);
        var callbacks = new List<CCallback>();
        var functions = new List<CFunction>();

        foreach (var statement in statements)
        {
            if (statement.Count == 0)
            {
                continue;
            }

            if (IsEnum(statement))
            {
                enums.Add(ParseEnum(statement, originalSource));
                continue;
            }

            if (IsCallback(statement))
            {
                callbacks.Add(ParseCallback(statement, originalSource));
                continue;
            }

            if (TryParseHandle(statement, out var handleName, out var structTag, out var isConst, out var declaration))
            {
                if (isConst)
                {
                    constHandles[structTag] = handleName;
                }
                else
                {
                    mutableHandles.Add((handleName, structTag, declaration));
                }
                continue;
            }

            if (IsFunction(statement))
            {
                functions.Add(ParseFunction(statement, originalSource));
            }
        }

        var handles = mutableHandles.Select(handle => new CHandle
        {
            Name = handle.Name,
            ConstName = constHandles.TryGetValue(handle.Tag, out var constName)
                ? constName
                : throw new InvalidDataException($"Opaque handle {handle.Name} has no const typedef."),
            StructTag = handle.Tag,
            Source = Locate(originalSource, handle.Name, SourceKind.Handle),
            CanonicalDeclaration = handle.Declaration,
        }).ToArray();

        InferParameterRelationships(functions.SelectMany(function => function.Parameters));
        InferParameterRelationships(callbacks.SelectMany(callback => callback.Parameters));

        return new ParsedHeader
        {
            Functions = functions,
            Enums = enums,
            Handles = handles,
            Callbacks = callbacks,
        };
    }

    public static IReadOnlyList<string> TokenTexts(string source) => Tokenize(source).Select(token => token.Text).ToArray();

    private static List<CToken> Tokenize(string source)
    {
        var result = new List<CToken>();
        for (var index = 0; index < source.Length;)
        {
            var current = source[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                index += 2;
                while (index < source.Length && source[index] != '\n') { index++; }
                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (end < 0) { throw new InvalidDataException("Unterminated C comment."); }
                index = end + 2;
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                var start = index++;
                while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] == '_')) { index++; }
                result.Add(new CToken(source[start..index]));
                continue;
            }

            if (char.IsDigit(current))
            {
                var start = index++;
                while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is 'x' or 'X')) { index++; }
                result.Add(new CToken(source[start..index]));
                continue;
            }

            if (current is '"' or '\'')
            {
                var quote = current;
                var start = index++;
                while (index < source.Length)
                {
                    if (source[index] == '\\') { index += 2; continue; }
                    if (source[index++] == quote) { break; }
                }
                result.Add(new CToken(source[start..index]));
                continue;
            }

            result.Add(new CToken(current.ToString()));
            index++;
        }
        return result;
    }

    private static IReadOnlyList<IReadOnlyList<CToken>> SplitStatements(IReadOnlyList<CToken> tokens)
    {
        var statements = new List<IReadOnlyList<CToken>>();
        var current = new List<CToken>();
        var parentheses = 0;
        var braces = 0;
        foreach (var token in tokens)
        {
            current.Add(token);
            if (token.Text == "(") { parentheses++; }
            else if (token.Text == ")") { parentheses--; }
            else if (token.Text == "{") { braces++; }
            else if (token.Text == "}") { braces--; }
            else if (token.Text == ";" && parentheses == 0 && braces == 0)
            {
                statements.Add(current.ToArray());
                current.Clear();
            }
        }

        return statements;
    }

    private static bool IsEnum(IReadOnlyList<CToken> statement) =>
        statement.Count > 4 && statement[0].Text == "typedef" && statement[1].Text == "enum" && statement.Any(token => token.Text == "{");

    private static bool IsCallback(IReadOnlyList<CToken> statement) =>
        statement.Count > 7
        && statement[0].Text == "typedef"
        && statement[1].Text == "migraphx_status"
        && statement[2].Text == "("
        && statement[3].Text == "*";

    private static bool IsFunction(IReadOnlyList<CToken> statement) =>
        statement.Count > 4
        && statement[0].Text == "migraphx_status"
        && statement[1].Text.StartsWith("migraphx_", StringComparison.Ordinal)
        && statement[2].Text == "(";

    private static CEnum ParseEnum(IReadOnlyList<CToken> statement, string originalSource)
    {
        var open = IndexOf(statement, "{");
        var close = LastIndexOf(statement, "}");
        var name = statement[close + 1].Text;
        var entries = SplitByComma(statement.Skip(open + 1).Take(close - open - 1).ToArray());
        var values = new List<CEnumValue>();
        var nextValue = 0;
        foreach (var entry in entries.Where(entry => entry.Count > 0))
        {
            var value = nextValue;
            var equals = IndexOf(entry, "=");
            if (equals >= 0)
            {
                value = ParseInteger(entry[equals + 1].Text);
            }
            values.Add(new CEnumValue(entry[0].Text, value));
            nextValue = checked(value + 1);
        }

        return new CEnum
        {
            Name = name,
            Values = values,
            Source = Locate(originalSource, name, SourceKind.Enum),
            CanonicalDeclaration = Canonicalize(statement),
        };
    }

    private static CCallback ParseCallback(IReadOnlyList<CToken> statement, string originalSource)
    {
        var name = statement[4].Text;
        var parameterOpen = 6;
        var parameterClose = FindMatchingParenthesis(statement, parameterOpen);
        return new CCallback
        {
            Name = name,
            ReturnType = statement[1].Text,
            Parameters = ParseParameters(statement.Skip(parameterOpen + 1).Take(parameterClose - parameterOpen - 1).ToArray()),
            Source = Locate(originalSource, name, SourceKind.Callback),
            CanonicalDeclaration = Canonicalize(statement),
        };
    }

    private static CFunction ParseFunction(IReadOnlyList<CToken> statement, string originalSource)
    {
        var name = statement[1].Text;
        var parameterClose = FindMatchingParenthesis(statement, 2);
        return new CFunction
        {
            Name = name,
            ReturnType = statement[0].Text,
            Parameters = ParseParameters(statement.Skip(3).Take(parameterClose - 3).ToArray()),
            Source = Locate(originalSource, name, SourceKind.Function),
            CanonicalDeclaration = Canonicalize(statement),
        };
    }

    private static bool TryParseHandle(
        IReadOnlyList<CToken> statement,
        out string name,
        out string structTag,
        out bool isConst,
        out string declaration)
    {
        name = string.Empty;
        structTag = string.Empty;
        isConst = false;
        declaration = string.Empty;
        if (statement.Count < 6 || statement[0].Text != "typedef") { return false; }

        var structIndex = IndexOf(statement, "struct");
        var pointerIndex = IndexOf(statement, "*");
        if (structIndex < 0 || pointerIndex != structIndex + 2) { return false; }

        structTag = statement[structIndex + 1].Text;
        name = statement[pointerIndex + 1].Text;
        isConst = statement.Take(structIndex).Any(token => token.Text == "const");
        declaration = Canonicalize(statement);
        return name.EndsWith("_t", StringComparison.Ordinal);
    }

    private static IReadOnlyList<CParameter> ParseParameters(IReadOnlyList<CToken> tokens)
    {
        if (tokens.Count == 0 || tokens.Count == 1 && tokens[0].Text == "void") { return []; }
        var result = new List<CParameter>();
        foreach (var part in SplitByComma(tokens))
        {
            if (part.Count == 3 && part.All(token => token.Text == "."))
            {
                result.Add(new CParameter
                {
                    Name = "__varargs",
                    CType = "...",
                    BaseType = "...",
                    PointerDepth = 0,
                    IsConst = false,
                    Direction = "variadic",
                    Nullable = false,
                    Ownership = "C variadic arguments are not representable by the generated managed declaration path",
                });
                continue;
            }
            if (part.Count < 2) { throw new InvalidDataException($"Cannot parse C parameter: {Canonicalize(part)}"); }
            var nameIndex = -1;
            for (var index = part.Count - 1; index >= 0; index--)
            {
                if (IsIdentifier(part[index].Text)) { nameIndex = index; break; }
            }
            if (nameIndex <= 0) { throw new InvalidDataException($"Cannot identify C parameter name: {Canonicalize(part)}"); }

            var typeTokens = part.Take(nameIndex).ToArray();
            var pointerDepth = typeTokens.Count(token => token.Text == "*");
            var baseType = string.Join(" ", typeTokens
                .Where(token => token.Text is not "const" and not "volatile" and not "*")
                .Select(token => token.Text));
            var parameter = new CParameter
            {
                Name = part[nameIndex].Text,
                CType = Canonicalize(typeTokens),
                BaseType = baseType,
                PointerDepth = pointerDepth,
                IsConst = typeTokens.Any(token => token.Text == "const") || baseType.StartsWith("const_migraphx_", StringComparison.Ordinal),
                Direction = pointerDepth == 0 ? "in" : part[nameIndex].Text == "out" ? "out" : "inout-raw-pointer",
                Nullable = pointerDepth > 0,
                Encoding = baseType == "char" && pointerDepth > 0 ? "UTF-8 or byte buffer; function semantics required" : null,
                Ownership = pointerDepth == 0 ? "value or borrowed opaque handle" : "ABI-only pointer; no public managed lifetime inferred",
            };
            result.Add(parameter);
        }
        return result;
    }

    private static void InferParameterRelationships(IEnumerable<CParameter> parameters)
    {
        var list = parameters as IReadOnlyList<CParameter> ?? parameters.ToArray();
        foreach (var parameter in list.Where(parameter => parameter.PointerDepth > 0))
        {
            var length = list.FirstOrDefault(candidate =>
                candidate.PointerDepth == 0
                && candidate.BaseType == "size_t"
                && (candidate.Name == $"{parameter.Name}_size"
                    || candidate.Name == "size"
                    || candidate.Name == "length"
                    || candidate.Name == "count"));
            parameter.ArrayLengthParameter = length?.Name;
        }
    }

    private static IReadOnlyList<IReadOnlyList<CToken>> SplitByComma(IReadOnlyList<CToken> tokens)
    {
        var result = new List<IReadOnlyList<CToken>>();
        var current = new List<CToken>();
        var depth = 0;
        foreach (var token in tokens)
        {
            if (token.Text is "(" or "[") { depth++; }
            else if (token.Text is ")" or "]") { depth--; }
            if (token.Text == "," && depth == 0)
            {
                result.Add(current.ToArray());
                current.Clear();
            }
            else
            {
                current.Add(token);
            }
        }
        result.Add(current.ToArray());
        return result;
    }

    private static int FindMatchingParenthesis(IReadOnlyList<CToken> tokens, int open)
    {
        var depth = 0;
        for (var index = open; index < tokens.Count; index++)
        {
            if (tokens[index].Text == "(") { depth++; }
            else if (tokens[index].Text == ")" && --depth == 0) { return index; }
        }
        throw new InvalidDataException("Unbalanced C parameter list.");
    }

    private static int ParseInteger(string text) => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? Convert.ToInt32(text[2..], 16)
        : int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private static int IndexOf(IReadOnlyList<CToken> tokens, string text)
    {
        for (var index = 0; index < tokens.Count; index++) { if (tokens[index].Text == text) { return index; } }
        return -1;
    }

    private static int LastIndexOf(IReadOnlyList<CToken> tokens, string text)
    {
        for (var index = tokens.Count - 1; index >= 0; index--) { if (tokens[index].Text == text) { return index; } }
        return -1;
    }

    private static bool IsIdentifier(string text) => text.Length > 0 && (char.IsLetter(text[0]) || text[0] == '_');

    private static string Canonicalize(IEnumerable<CToken> tokens)
    {
        var text = string.Join(" ", tokens.Select(token => token.Text));
        return text.Replace(" *", "*", StringComparison.Ordinal)
            .Replace("* ", "*", StringComparison.Ordinal)
            .Replace("( ", "(", StringComparison.Ordinal)
            .Replace(" )", ")", StringComparison.Ordinal)
            .Replace(" ,", ",", StringComparison.Ordinal)
            .Replace(" ;", ";", StringComparison.Ordinal)
            .Replace(" {", "{", StringComparison.Ordinal)
            .Replace(" }", "}", StringComparison.Ordinal);
    }

    private enum SourceKind { Function, Callback, Handle, Enum }

    private static SourceLocation Locate(string source, string name, SourceKind kind)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var column = lines[index].IndexOf(name, StringComparison.Ordinal);
            if (column < 0) { continue; }
            var line = lines[index];
            var matches = kind switch
            {
                SourceKind.Function => line.Contains($"{name}(", StringComparison.Ordinal) || line.Contains($"{name} (", StringComparison.Ordinal),
                SourceKind.Callback => line.Contains($"(*{name})", StringComparison.Ordinal),
                SourceKind.Handle => line.Contains($" {name};", StringComparison.Ordinal),
                SourceKind.Enum => line.Contains($"}} {name};", StringComparison.Ordinal),
                _ => false,
            };
            if (matches) { return new SourceLocation(index + 1, column + 1); }
        }
        throw new InvalidDataException($"Cannot locate {kind} {name} in the frozen header.");
    }
}

internal static class BindingGenerator
{
    private const string FrozenStamp = "2026-08-14T00:00:00Z";
    private const string RuntimeSha = "f1a11cfd1701a041cee29188f7600c85b34ae260";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static void Generate(Arguments options)
    {
        using var m2Document = JsonDocument.Parse(File.ReadAllText(options.M2ManifestPath));
        using var classificationDocument = JsonDocument.Parse(File.ReadAllText(options.ClassificationPath));
        using var unsupportedDocument = JsonDocument.Parse(File.ReadAllText(options.UnsupportedPath));
        var m2 = m2Document.RootElement;
        var classification = classificationDocument.RootElement;
        var unsupported = unsupportedDocument.RootElement;
        var source = m2.GetProperty("source");
        var expectedHash = source.GetProperty("headerSha256").GetString()!;
        var expectedLength = source.GetProperty("headerByteLength").GetInt64();
        FrozenInputValidator.Validate(options.HeaderPath, expectedHash, expectedLength);

        var parsed = CHeaderParser.Parse(
            File.ReadAllText(options.PreprocessedPath),
            File.ReadAllText(options.HeaderPath));
        ValidateCounts(parsed);

        var m2Functions = m2.GetProperty("functions").EnumerateArray().ToDictionary(
            item => item.GetProperty("cName").GetString()!,
            item => item.Clone(),
            StringComparer.Ordinal);
        if (m2Functions.Count != classification.GetProperty("functionProjection").GetProperty("expectedCount").GetInt32())
        {
            throw new InvalidDataException("M2 handwritten projection count differs from the M3 classification manifest.");
        }
        var callbackPolicies = classification.GetProperty("callbackOverrides").GetProperty("items").EnumerateArray().ToDictionary(
            item => item.GetProperty("cName").GetString()!,
            item => item.GetProperty("retention").GetString()!,
            StringComparer.Ordinal);
        if (!callbackPolicies.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(parsed.Callbacks.Select(callback => callback.Name)))
        {
            throw new InvalidDataException("Callback classification does not close over the frozen header callbacks.");
        }
        var unsupportedFunctions = unsupported.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("kind").GetString() == "function")
            .ToDictionary(item => item.GetProperty("cName").GetString()!, item => item.Clone(), StringComparer.Ordinal);
        if (unsupported.GetProperty("configurationUnavailable").GetArrayLength() != 0)
        {
            throw new InvalidDataException("The frozen generated header unexpectedly contains configuration-unavailable declarations.");
        }
        if (!unsupportedFunctions.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "migraphx_operation_create" }))
        {
            throw new InvalidDataException("The M3 unsupported function classification is incomplete or unexpected.");
        }

        var m2Enums = m2.GetProperty("enums").EnumerateArray().ToDictionary(
            item => item.GetProperty("cName").GetString()!,
            item => item.Clone(),
            StringComparer.Ordinal);
        var normalizedEnums = parsed.Enums.Select(item => NormalizeEnum(item, m2Enums)).ToArray();
        var normalizedHandles = parsed.Handles.Select(item => new
        {
            id = $"handle:{item.Name}",
            cName = item.Name,
            constCName = item.ConstName,
            structTag = item.StructTag,
            canonicalDeclaration = item.CanonicalDeclaration,
            pointerDepth = 1,
            managedAbiType = "IntPtr",
            classification = "generated",
            validationLevel = "statically-verified",
            source = item.Source,
        }).ToArray();
        var normalizedCallbacks = parsed.Callbacks.Select(item => new
        {
            id = $"callback:{item.Name}",
            cName = item.Name,
            managedName = ToManagedCallbackName(item.Name),
            returnType = item.ReturnType,
            canonicalDeclaration = item.CanonicalDeclaration,
            callingConvention = "cdecl",
            classification = "handwritten",
            retention = callbackPolicies[item.Name],
            exceptionBoundary = "Managed exceptions must be caught before returning across the unmanaged boundary and converted to migraphx_status.",
            parameters = item.Parameters.Select(parameter => NormalizeRawParameter(parameter, parsed)).ToArray(),
            validationLevel = "statically-verified",
            source = item.Source,
        }).ToArray();
        var normalizedFunctions = parsed.Functions.Select(item => NormalizeFunction(item, m2Functions, unsupportedFunctions, parsed)).ToArray();

        var model = new
        {
            schema = "./schemas/m3-normalized-api.schema.json",
            schemaVersion = "1.0.0",
            modelVersion = "m3-1",
            generatedAtUtc = FrozenStamp,
            validationLevel = "statically-verified",
            source = new
            {
                vendor = source.GetProperty("vendor").GetString(),
                rocmVersion = source.GetProperty("rocmVersion").GetString(),
                migraphxVersion = source.GetProperty("migraphxVersion").GetString(),
                releaseTag = source.GetProperty("releaseTag").GetString(),
                peeledCommit = source.GetProperty("peeledCommit").GetString(),
                headerSha256 = expectedHash,
                headerByteLength = expectedLength,
                path = "src/api/include/migraphx/migraphx.h",
            },
            configuration = new
            {
                language = "C11 preprocessing with structured declaration parsing",
                cplusplus = false,
                defines = new[] { "MIGRAPHX_C_EXPORT=" },
                standardIncludes = "token-only deterministic stubs",
                targetAbi = "x86-64; size_t=UIntPtr; C _Bool=one byte; enum=signed 32-bit for the verified ABI",
                frontendDeclarations = "present in the frozen generated header",
                volatileFields = "none",
                typeSystem = new
                {
                    sizeT = "unsigned 64-bit size_t on the verified x86-64 ABI",
                    cBool = "stdbool.h bool preprocesses to one-byte _Bool",
                    enums = "signed 32-bit C enum on the verified x86-64 ABI",
                    opaqueHandles = "typedef chain resolves to pointer-to-incomplete-struct",
                },
            },
            counts = new { functions = 159, enums = 2, handles = 25, callbacks = 6 },
            enums = normalizedEnums,
            handles = normalizedHandles,
            callbacks = normalizedCallbacks,
            functions = normalizedFunctions,
        };

        var inventoryItems = normalizedFunctions.Select(item => new InventoryItem
        {
            Id = item.id,
            Kind = "function",
            CName = item.cName,
            Classification = item.classification,
            Milestone = item.milestone,
            ValidationLevel = item.validationLevel,
            Source = item.source,
        }).Concat(normalizedEnums.Select(item => new InventoryItem
        {
            Id = item.id,
            Kind = "enum",
            CName = item.cName,
            Classification = item.classification,
            Milestone = item.milestone,
            ValidationLevel = item.validationLevel,
            Source = item.source,
        })).Concat(normalizedHandles.Select(item => new InventoryItem
        {
            Id = item.id,
            Kind = "handle",
            CName = item.cName,
            Classification = item.classification,
            Milestone = "M3",
            ValidationLevel = item.validationLevel,
            Source = item.source,
        })).Concat(normalizedCallbacks.Select(item => new InventoryItem
        {
            Id = item.id,
            Kind = "callback",
            CName = item.cName,
            Classification = item.classification,
            Milestone = "M3",
            ValidationLevel = item.validationLevel,
            Source = item.source,
        })).ToArray();

        var inventory = new
        {
            schema = "./schemas/m3-inventory.schema.json",
            schemaVersion = "1.0.0",
            inventoryVersion = "m3-1",
            generatedAtUtc = FrozenStamp,
            sourceHeaderSha256 = expectedHash,
            counts = ClassificationCounts.Create(inventoryItems.Select(item => (item.Kind, item.Classification))),
            classificationClosed = inventoryItems.Length == 192,
            items = inventoryItems,
        };
        var counts = ClassificationCounts.Create(inventoryItems.Select(item => (item.Kind, item.Classification)));
        var coverage = new
        {
            schema = "./schemas/m3-coverage-summary.schema.json",
            schemaVersion = "1.0.0",
            summaryVersion = "m3-1",
            generatedAtUtc = FrozenStamp,
            validationLevel = "statically-verified",
            sourceHeaderSha256 = expectedHash,
            counts,
            classificationClosed = counts.Overall.Total == counts.Overall.Generated + counts.Overall.Handwritten + counts.Overall.Unsupported + counts.Overall.ConfigurationUnavailable,
            m1FunctionCount = 6,
            m2CumulativeFunctionCount = 41,
            m3DeclaredFunctionCount = 159,
            managedEntryPointCount = 158,
            unsupportedFunctionCount = 1,
            fullHeaderFunctionCoveragePercent = 100.0,
            officialElf = new
            {
                expectedPublicFunctions = 159,
                observedMigraphxExports = 160,
                knownPrivateExtras = new[] { "migraphx_test_private_disable_exception_catch" },
                evidence = "statically-verified by eng/verify-m3-abi.ps1; no new runtime execution",
            },
            runtimeBoundary = $"Only the bounded M1/M2 workflow at {RuntimeSha} is runtime-executed; M3 additions are static ABI declarations.",
        };

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["compatibility/m3-normalized-api.json"] = Serialize(model),
            ["compatibility/m3-api-inventory.json"] = Serialize(inventory),
            ["compatibility/m3-coverage-summary.json"] = Serialize(coverage),
        };
        foreach (var file in CSharpEmitter.Emit(parsed, normalizedFunctions, normalizedEnums, normalizedCallbacks, m2Functions.Keys.ToArray()))
        {
            files[file.Key] = file.Value;
        }

        Directory.CreateDirectory(options.OutputDirectory);
        foreach (var file in files)
        {
            var path = Path.Combine(options.OutputDirectory, file.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Value, new UTF8Encoding(false));
        }
    }

    private static void ValidateCounts(ParsedHeader parsed)
    {
        if (parsed.Functions.Count != 159 || parsed.Enums.Count != 2 || parsed.Handles.Count != 25 || parsed.Callbacks.Count != 6)
        {
            throw new InvalidDataException(
                $"Frozen header inventory mismatch: functions={parsed.Functions.Count}, enums={parsed.Enums.Count}, handles={parsed.Handles.Count}, callbacks={parsed.Callbacks.Count}.");
        }
        foreach (var duplicate in parsed.Functions.GroupBy(item => item.Name, StringComparer.Ordinal).Where(group => group.Count() != 1))
        {
            throw new InvalidDataException($"Duplicate function declaration {duplicate.Key}.");
        }
    }

    private static dynamic NormalizeEnum(CEnum item, IReadOnlyDictionary<string, JsonElement> m2Enums)
    {
        if (!m2Enums.TryGetValue(item.Name, out var mapped))
        {
            throw new InvalidDataException($"Enum {item.Name} has no frozen managed projection.");
        }
        var mappedValues = mapped.GetProperty("values").EnumerateArray().ToArray();
        if (mappedValues.Length != item.Values.Count) { throw new InvalidDataException($"Enum {item.Name} value count drifted."); }
        var values = item.Values.Select((value, index) =>
        {
            var expected = mappedValues[index].GetProperty("value").GetInt32();
            if (expected != value.Value) { throw new InvalidDataException($"Enum {item.Name}.{value.Name} value drifted."); }
            return new
            {
                cName = value.Name,
                managedName = mappedValues[index].GetProperty("managedName").GetString(),
                value = value.Value,
            };
        }).ToArray();
        return new
        {
            id = $"enum:{item.Name}",
            cName = item.Name,
            managedName = mapped.GetProperty("managedName").GetString(),
            canonicalDeclaration = item.CanonicalDeclaration,
            underlyingAbi = "signed 32-bit C enum on the verified x86-64 ABI",
            classification = "generated",
            milestone = item.Name == "migraphx_status" ? "M1" : "M2",
            validationLevel = "statically-verified",
            values,
            source = item.Source,
        };
    }

    private static dynamic NormalizeFunction(
        CFunction item,
        IReadOnlyDictionary<string, JsonElement> m2Functions,
        IReadOnlyDictionary<string, JsonElement> unsupportedFunctions,
        ParsedHeader parsedHeader)
    {
        if (m2Functions.TryGetValue(item.Name, out var mapped))
        {
            var mappedParameters = mapped.GetProperty("parameters").EnumerateArray().ToDictionary(
                parameter => parameter.GetProperty("cName").GetString()!,
                parameter => parameter.Clone(),
                StringComparer.Ordinal);
            if (mappedParameters.Count != item.Parameters.Count) { throw new InvalidDataException($"M2 parameter count drifted for {item.Name}."); }
            var parameters = item.Parameters.Select(parameter => OverlayParameter(parameter, mappedParameters[parameter.Name], parsedHeader)).ToArray();
            return new
            {
                id = $"function:{item.Name}",
                cName = item.Name,
                managedName = mapped.GetProperty("managedName").GetString(),
                returnType = item.ReturnType,
                managedReturnType = "NativeMIGraphXStatus",
                canonicalDeclaration = item.CanonicalDeclaration,
                classification = "handwritten",
                milestone = Array.IndexOf(m2Functions.Keys.ToArray(), item.Name) < 6 ? "M1" : "M2",
                validationLevel = "statically-verified",
                parameters,
                source = item.Source,
            };
        }

        if (unsupportedFunctions.TryGetValue(item.Name, out var unsupported))
        {
            return new
            {
                id = $"function:{item.Name}",
                cName = item.Name,
                managedName = (string?)null,
                returnType = item.ReturnType,
                managedReturnType = (string?)null,
                canonicalDeclaration = item.CanonicalDeclaration,
                classification = "unsupported",
                milestone = "M3",
                validationLevel = "statically-verified",
                reason = unsupported.GetProperty("reason").GetString(),
                risk = unsupported.GetProperty("risk").GetString(),
                plannedStage = unsupported.GetProperty("plannedStage").GetString(),
                coverage = unsupported.GetProperty("coverage").GetString(),
                parameters = item.Parameters.Select(parameter => NormalizeRawParameter(parameter, parsedHeader)).ToArray(),
                source = item.Source,
            };
        }

        return new
        {
            id = $"function:{item.Name}",
            cName = item.Name,
            managedName = ToManagedName(item.Name),
            returnType = item.ReturnType,
            managedReturnType = "NativeMIGraphXStatus",
            canonicalDeclaration = item.CanonicalDeclaration,
            classification = "generated",
            milestone = "M3",
            validationLevel = "statically-verified",
            parameters = item.Parameters.Select(parameter => NormalizeRawParameter(parameter, parsedHeader)).ToArray(),
            source = item.Source,
        };
    }

    private static object OverlayParameter(CParameter parsed, JsonElement mapped, ParsedHeader parsedHeader)
    {
        string? OptionalString(string name) => mapped.TryGetProperty(name, out var property) ? property.GetString() : null;
        bool? OptionalBoolean(string name) => mapped.TryGetProperty(name, out var property) ? property.GetBoolean() : null;
        return new
        {
            cName = parsed.Name,
            cType = parsed.CType,
            baseType = parsed.BaseType,
            typedefChain = ResolveTypedefChain(parsed, parsedHeader),
            pointerDepth = parsed.PointerDepth,
            isConst = parsed.IsConst,
            direction = OptionalString("direction") ?? (OptionalString("managedModifier") == "out" ? "out" : parsed.Direction),
            nullable = OptionalBoolean("nullable") ?? parsed.Nullable,
            arrayLengthParameter = parsed.ArrayLengthParameter,
            encoding = OptionalString("encoding") ?? parsed.Encoding,
            ownership = OptionalString("ownership") ?? parsed.Ownership,
            managedType = mapped.GetProperty("managedType").GetString(),
            managedModifier = OptionalString("managedModifier"),
            abi = OptionalString("abi"),
        };
    }

    private static object NormalizeRawParameter(CParameter parameter, ParsedHeader parsedHeader) => new
    {
        cName = parameter.Name,
        cType = parameter.CType,
        baseType = parameter.BaseType,
        typedefChain = ResolveTypedefChain(parameter, parsedHeader),
        pointerDepth = parameter.PointerDepth,
        isConst = parameter.IsConst,
        direction = parameter.Direction,
        nullable = parameter.Nullable,
        arrayLengthParameter = parameter.ArrayLengthParameter,
        encoding = parameter.Encoding,
        ownership = parameter.Ownership,
        managedType = parameter.BaseType == "..." ? null : MapManagedType(parameter),
        managedModifier = (string?)null,
    };

    private static IReadOnlyList<string> ResolveTypedefChain(CParameter parameter, ParsedHeader parsedHeader)
    {
        var chain = new List<string> { parameter.CType };
        if (!string.Equals(parameter.CType, parameter.BaseType, StringComparison.Ordinal))
        {
            chain.Add(parameter.BaseType);
        }

        var handle = parsedHeader.Handles.FirstOrDefault(item =>
            item.Name == parameter.BaseType || item.ConstName == parameter.BaseType);
        if (handle is not null)
        {
            var prefix = handle.ConstName == parameter.BaseType ? "const struct" : "struct";
            chain.Add($"{prefix} {handle.StructTag}*");
        }
        else if (parsedHeader.Enums.FirstOrDefault(item => item.Name == parameter.BaseType) is { } enumType)
        {
            chain.Add($"C enum {enumType.Name}");
        }
        else if (parsedHeader.Callbacks.FirstOrDefault(item => item.Name == parameter.BaseType) is { } callback)
        {
            chain.Add(callback.CanonicalDeclaration);
        }
        else if (parameter.BaseType == "size_t")
        {
            chain.Add("unsigned 64-bit size_t on the verified x86-64 ABI");
        }
        else if (parameter.BaseType is "_Bool" or "bool")
        {
            chain.Add("one-byte C _Bool (original stdbool.h token: bool)");
        }

        return chain.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static string MapManagedType(CParameter parameter)
    {
        if (parameter.PointerDepth > 0) { return "IntPtr"; }
        if (parameter.BaseType.StartsWith("migraphx_", StringComparison.Ordinal)
            || parameter.BaseType.StartsWith("const_migraphx_", StringComparison.Ordinal))
        {
            return parameter.BaseType switch
            {
                "migraphx_status" => "NativeMIGraphXStatus",
                "migraphx_shape_datatype_t" => "NativeMIGraphXShapeDataType",
                _ => "IntPtr",
            };
        }
        return parameter.BaseType switch
        {
            "size_t" => "UIntPtr",
            "_Bool" or "bool" => "byte",
            "uint64_t" => "ulong",
            "int64_t" => "long",
            "uint32_t" => "uint",
            "int32_t" or "int" => "int",
            "uint16_t" => "ushort",
            "int16_t" => "short",
            "uint8_t" => "byte",
            "int8_t" => "sbyte",
            "float" => "float",
            "double" => "double",
            _ => throw new InvalidDataException($"No managed ABI mapping for C type {parameter.CType}."),
        };
    }

    private static string ToManagedName(string cName) => string.Concat(cName["migraphx_".Length..]
        .Split('_', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string ToManagedCallbackName(string cName) => $"Native{ToManagedName(cName)}Callback";

    private static string Serialize(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        const string schemaProperty = "\"schema\":";
        var schemaIndex = json.IndexOf(schemaProperty, StringComparison.Ordinal);
        if (schemaIndex >= 0)
        {
            json = string.Concat(json.AsSpan(0, schemaIndex), "\"$schema\":", json.AsSpan(schemaIndex + schemaProperty.Length));
        }

        return json + "\n";
    }

    private sealed class ClassificationCounts
    {
        public required KindCounts Functions { get; init; }
        public required KindCounts Enums { get; init; }
        public required KindCounts Handles { get; init; }
        public required KindCounts Callbacks { get; init; }
        public required KindCounts Overall { get; init; }

        internal static ClassificationCounts Create(IEnumerable<(string Kind, string Classification)> source)
        {
            var items = source.ToArray();
            KindCounts Count(string? kind)
            {
                var selected = kind is null ? items : items.Where(item => item.Kind == kind).ToArray();
                return new KindCounts
                {
                    Total = selected.Length,
                    Generated = selected.Count(item => item.Classification == "generated"),
                    Handwritten = selected.Count(item => item.Classification == "handwritten"),
                    Unsupported = selected.Count(item => item.Classification == "unsupported"),
                    ConfigurationUnavailable = selected.Count(item => item.Classification == "configuration-unavailable"),
                };
            }
            return new ClassificationCounts
            {
                Functions = Count("function"),
                Enums = Count("enum"),
                Handles = Count("handle"),
                Callbacks = Count("callback"),
                Overall = Count(null),
            };
        }
    }

    private sealed class KindCounts
    {
        public int Total { get; init; }
        public int Generated { get; init; }
        public int Handwritten { get; init; }
        public int Unsupported { get; init; }
        public int ConfigurationUnavailable { get; init; }
    }

    private static class CSharpEmitter
    {
        internal static IReadOnlyDictionary<string, string> Emit(
            ParsedHeader parsed,
            IReadOnlyList<dynamic> functions,
            IReadOnlyList<dynamic> enums,
            IReadOnlyList<dynamic> callbacks,
            IReadOnlyList<string> m2Names)
        {
            var banner = "// <auto-generated />\n"
                + "// Source: compatibility/m3-normalized-api.json\n"
                + "// M1/M2 overrides: compatibility/m2-binding-subset.json\n"
                + "// Frozen migraphx.h SHA-256: a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2\n"
                + "// Regenerate with eng/generate-interop.ps1; do not edit by hand.\n";
            var common = new StringBuilder(banner);
            common.AppendLine("using System;");
            common.AppendLine("using System.Runtime.InteropServices;");
            common.AppendLine();
            common.AppendLine("namespace JYPPX.ROCm.MIGraphXSharp.Interop;");
            common.AppendLine();
            foreach (var item in enums)
            {
                common.AppendLine($"internal enum {item.managedName}");
                common.AppendLine("{");
                foreach (var value in item.values) { common.AppendLine($"    {value.managedName} = {value.value},"); }
                common.AppendLine("}");
                common.AppendLine();
            }
            foreach (var callback in callbacks)
            {
                common.AppendLine("[UnmanagedFunctionPointer(CallingConvention.Cdecl)]");
                common.AppendLine($"internal delegate NativeMIGraphXStatus {callback.managedName}({ManagedParameters(callback.parameters)});");
                common.AppendLine();
            }
            common.AppendLine("internal static partial class NativeMethods");
            common.AppendLine("{");
            common.AppendLine("    internal const string LibraryName = \"migraphx_c\";");
            AppendArray(common, "M1RequiredExports", m2Names.Take(6));
            AppendArray(common, "M2RequiredExports", m2Names);
            AppendArray(common, "M3DeclaredExports", parsed.Functions.Select(function => function.Name));
            AppendArray(common, "M3OpaqueHandleNames", parsed.Handles.Select(handle => handle.Name));
            AppendArray(common, "M3CallbackNames", parsed.Callbacks.Select(callback => callback.Name));
            common.AppendLine("}");

            var libraryImport = new StringBuilder(banner);
            libraryImport.AppendLine("#if MIGRAPHX_LIBRARYIMPORT_PATH");
            libraryImport.AppendLine("using System;");
            libraryImport.AppendLine("using System.Runtime.CompilerServices;");
            libraryImport.AppendLine("using System.Runtime.InteropServices;");
            libraryImport.AppendLine();
            libraryImport.AppendLine("namespace JYPPX.ROCm.MIGraphXSharp.Interop;");
            libraryImport.AppendLine();
            libraryImport.AppendLine("internal static partial class NativeMethods");
            libraryImport.AppendLine("{");
            foreach (var function in functions)
            {
                if (function.classification == "unsupported") { continue; }
                libraryImport.AppendLine($"    [LibraryImport(LibraryName, EntryPoint = \"{function.cName}\")]");
                libraryImport.AppendLine("    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]");
                libraryImport.AppendLine($"    internal static partial NativeMIGraphXStatus {function.managedName}({ManagedParameters(function.parameters)});");
                libraryImport.AppendLine();
            }
            libraryImport.AppendLine("}");
            libraryImport.AppendLine("#endif");

            var dllImport = new StringBuilder(banner);
            dllImport.AppendLine("#if MIGRAPHX_DLLIMPORT_PATH");
            dllImport.AppendLine("using System;");
            dllImport.AppendLine("using System.Runtime.InteropServices;");
            dllImport.AppendLine();
            dllImport.AppendLine("namespace JYPPX.ROCm.MIGraphXSharp.Interop;");
            dllImport.AppendLine();
            dllImport.AppendLine("internal static partial class NativeMethods");
            dllImport.AppendLine("{");
            foreach (var function in functions)
            {
                if (function.classification == "unsupported") { continue; }
                dllImport.AppendLine($"    [DllImport(LibraryName, EntryPoint = \"{function.cName}\", CallingConvention = CallingConvention.Cdecl)]");
                dllImport.AppendLine($"    internal static extern NativeMIGraphXStatus {function.managedName}({ManagedParameters(function.parameters)});");
                dllImport.AppendLine();
            }
            dllImport.AppendLine("}");
            dllImport.AppendLine("#endif");

            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/JYPPX.ROCm.MIGraphX.CSharp.API/Generated/NativeMethods.g.cs"] = Normalize(common),
                ["src/JYPPX.ROCm.MIGraphX.CSharp.API/Generated/NativeMethods.LibraryImport.g.cs"] = Normalize(libraryImport),
                ["src/JYPPX.ROCm.MIGraphX.CSharp.API/Generated/NativeMethods.DllImport.g.cs"] = Normalize(dllImport),
            };
        }

        private static void AppendArray(StringBuilder builder, string name, IEnumerable<string> values)
        {
            builder.AppendLine();
            builder.AppendLine($"    internal static readonly string[] {name} =");
            builder.AppendLine("    {");
            foreach (var value in values) { builder.AppendLine($"        \"{value}\","); }
            builder.AppendLine("    };");
        }

        private static string ManagedParameters(IEnumerable<dynamic> parameters) => string.Join(", ", parameters.Select(parameter =>
        {
            string name = parameter.cName;
            if (CSharpKeywords.Contains(name)) { name = $"@{name}"; }
            string? modifier = parameter.managedModifier;
            return $"{(modifier is null ? string.Empty : modifier + " ")}{parameter.managedType} {name}";
        }));

        private static string Normalize(StringBuilder builder) => builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);

        private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
            "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
            "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void",
            "volatile", "while",
        };
    }
}
