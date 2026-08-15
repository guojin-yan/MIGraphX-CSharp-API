using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

Dictionary<string, string> options = ParseOptions(args);
string assemblyPath = RequirePath(options, "assembly");
string snapshotPath = RequireOption(options, "snapshot");
string packageId = RequireOption(options, "package");
string frameworks = RequireOption(options, "frameworks");
string expectedVersion = RequireOption(options, "version");
string expectedCommit = RequireOption(options, "commit");
if (!System.Text.RegularExpressions.Regex.IsMatch(expectedVersion, @"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$"))
    throw new ArgumentException("--version must be a SemVer release or prerelease value.");
if (!System.Text.RegularExpressions.Regex.IsMatch(expectedCommit, "^[a-f0-9]{40}$"))
    throw new ArgumentException("--commit must be a lowercase 40-character Git SHA.");
bool write = options.ContainsKey("write");
bool check = options.ContainsKey("check");
if (write == check) throw new ArgumentException("Specify exactly one of --write or --check.");

Assembly assembly = Assembly.LoadFrom(assemblyPath);
VerifyIdentity(assembly, assemblyPath, expectedVersion, expectedCommit);
List<string> records = GenerateSurface(assembly, expectedVersion);
string output = string.Join("\n", new[]
{
    "# schema-version: 2.0.0",
    "# package: " + packageId,
    "# assembly: " + assembly.GetName().Name,
    "# target-frameworks: " + frameworks,
    "# tfm-availability-policy: identical-on-all-listed-frameworks",
    "# assembly-version-policy: semantic-core-four-part",
    "# package-version-constant-policy: must-equal-build-version",
    "# nullable-contract: compiler-metadata",
    "# generated-from: exported types and declared public/protected members",
}.Concat(records)) + "\n";

if (write)
{
    string fullPath = Path.GetFullPath(snapshotPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, output, new UTF8Encoding(false));
}
else
{
    string expected = File.ReadAllText(snapshotPath).Replace("\r\n", "\n", StringComparison.Ordinal);
    if (!string.Equals(expected, output, StringComparison.Ordinal))
        throw new InvalidOperationException("Public API drift detected. " + FirstDifference(expected, output));
}

int typeCount = records.Count(line => line.StartsWith("T|", StringComparison.Ordinal));
Console.WriteLine($"API surface {(write ? "write" : "check")} passed: {typeCount} types, {records.Count - typeCount} members; identity {expectedVersion}+{expectedCommit}.");
return 0;

static void VerifyIdentity(Assembly assembly, string path, string expectedVersion, string expectedCommit)
{
    string expectedAssemblyName = Path.GetFileNameWithoutExtension(path);
    if (!string.Equals(assembly.GetName().Name, expectedAssemblyName, StringComparison.Ordinal))
        throw new InvalidOperationException($"Assembly name mismatch: expected {expectedAssemblyName}, actual {assembly.GetName().Name}.");

    string numeric = expectedVersion.Split('-', '+')[0];
    string expectedAssemblyVersion = numeric + ".0";
    string actualAssemblyVersion = assembly.GetName().Version?.ToString() ?? string.Empty;
    if (!string.Equals(actualAssemblyVersion, expectedAssemblyVersion, StringComparison.Ordinal))
        throw new InvalidOperationException($"AssemblyVersion mismatch: expected {expectedAssemblyVersion}, actual {actualAssemblyVersion}.");

    string actualFileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty;
    if (!string.Equals(actualFileVersion, expectedAssemblyVersion, StringComparison.Ordinal))
        throw new InvalidOperationException($"FileVersion mismatch: expected {expectedAssemblyVersion}, actual {actualFileVersion}.");

    string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
    string expectedInformational = expectedVersion + "+" + expectedCommit;
    if (!string.Equals(informational, expectedInformational, StringComparison.Ordinal))
        throw new InvalidOperationException($"InformationalVersion mismatch: expected {expectedInformational}, actual {informational}.");
}

static List<string> GenerateSurface(Assembly assembly, string expectedVersion)
{
    var lines = new List<string>();
    var nullability = new NullabilityInfoContext();
    const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    foreach (Type type in assembly.GetExportedTypes().OrderBy(item => item.FullName, StringComparer.Ordinal))
    {
        string typeName = FormatType(type);
        string baseType = type.BaseType is null || type.BaseType == typeof(object) || type.BaseType == typeof(ValueType) || type.BaseType == typeof(Enum)
            ? string.Empty : ";base=" + FormatType(type.BaseType);
        string interfaces = type.IsEnum ? string.Empty : string.Join(',', type.GetInterfaces().Select(item => FormatType(item)).OrderBy(value => value, StringComparer.Ordinal));
        lines.Add($"T|{TypeModifiers(type)}|{typeName}{baseType};interfaces={interfaces}{FormatGenericConstraints(type.GetGenericArguments())}");

        foreach (ConstructorInfo constructor in type.GetConstructors(declared).Where(IsVisible))
            lines.Add($"C|{Visibility(constructor)}|{typeName}({FormatParameters(constructor.GetParameters(), nullability)})");

        foreach (MethodInfo method in type.GetMethods(declared).Where(IsVisible).Where(method => !IsAccessor(method)))
        {
            string generic = method.IsGenericMethodDefinition ? "<" + string.Join(",", method.GetGenericArguments().Select(argument => argument.Name)) + ">" : string.Empty;
            lines.Add($"M|{MethodModifiers(method)}|{FormatType(method.ReturnType, nullability.Create(method.ReturnParameter))} {typeName}.{method.Name}{generic}({FormatParameters(method.GetParameters(), nullability)}){FormatGenericConstraints(method.GetGenericArguments())}");
        }

        foreach (PropertyInfo property in type.GetProperties(declared).Where(property => property.GetAccessors(true).Any(IsVisible)))
        {
            string access = $"get:{AccessorVisibility(property.GetMethod)},set:{AccessorVisibility(property.SetMethod)}";
            string index = property.GetIndexParameters().Length == 0 ? string.Empty : "[" + FormatParameters(property.GetIndexParameters(), nullability) + "]";
            lines.Add($"P|{access}|{FormatType(property.PropertyType, nullability.Create(property))} {typeName}.{property.Name}{index}");
        }

        foreach (EventInfo eventInfo in type.GetEvents(declared).Where(item => item.GetAddMethod(true) is MethodInfo add && IsVisible(add)))
            lines.Add($"E|{Visibility(eventInfo.GetAddMethod(true)!)}|{FormatType(eventInfo.EventHandlerType!)} {typeName}.{eventInfo.Name}");

        foreach (FieldInfo field in type.GetFields(declared).Where(field => field.Name != "value__").Where(field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly))
        {
            object? raw = field.IsLiteral ? field.GetRawConstantValue() : null;
            string value = field.IsLiteral
                ? "=" + (type.FullName == "JYPPX.ROCm.MIGraphXSharp.MIGraphXBuildInfo" && field.Name == "PackageVersion"
                    ? JsonSerializer.Serialize("<package-version>")
                    : FormatConstant(raw))
                : string.Empty;
            if (type.FullName == "JYPPX.ROCm.MIGraphXSharp.MIGraphXBuildInfo" && field.Name == "PackageVersion" && !string.Equals(raw as string, expectedVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"MIGraphXBuildInfo.PackageVersion must equal {expectedVersion}, actual {raw}.");
            lines.Add($"F|{FieldModifiers(field)}|{FormatType(field.FieldType, nullability.Create(field))} {typeName}.{field.Name}{value}");
        }
    }
    return lines.OrderBy(line => line, StringComparer.Ordinal).ToList();
}

static string TypeModifiers(Type type)
{
    string kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class";
    var values = new List<string> { type.IsNested ? TypeVisibility(type) : "public" };
    if (type.IsAbstract && type.IsSealed) values.Add("static");
    else { if (type.IsAbstract) values.Add("abstract"); if (type.IsSealed) values.Add("sealed"); }
    if (type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute")) values.Add("readonly");
    values.Add(kind);
    return string.Join(" ", values);
}

static string MethodModifiers(MethodInfo method)
{
    var values = new List<string> { Visibility(method) };
    if (method.IsStatic) values.Add("static");
    if (method.IsAbstract) values.Add("abstract");
    else if (method.IsVirtual && method.GetBaseDefinition() != method) values.Add("override");
    else if (method.IsVirtual) values.Add("virtual");
    return string.Join(" ", values);
}

static string FieldModifiers(FieldInfo field)
{
    var values = new List<string> { field.IsPublic ? "public" : field.IsFamily ? "protected" : "protected-internal" };
    if (field.IsLiteral) values.Add("const");
    else { if (field.IsStatic) values.Add("static"); if (field.IsInitOnly) values.Add("readonly"); }
    return string.Join(" ", values);
}

static string FormatParameters(IEnumerable<ParameterInfo> parameters, NullabilityInfoContext context) => string.Join(",", parameters.Select(parameter =>
{
    Type type = parameter.ParameterType;
    string prefix = type.IsByRef ? parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref " : string.Empty;
    string optional = parameter.HasDefaultValue ? "=" + FormatConstant(parameter.DefaultValue) : string.Empty;
    return prefix + FormatType(type, context.Create(parameter)) + " " + parameter.Name + optional;
}));

static string FormatType(Type type, NullabilityInfo? nullable = null)
{
    if (type.IsByRef) return FormatType(type.GetElementType()!, nullable?.ElementType);
    if (type.IsPointer) return FormatType(type.GetElementType()!) + "*";
    Type? nullableValue = Nullable.GetUnderlyingType(type);
    if (nullableValue is not null) return FormatType(nullableValue, nullable?.GenericTypeArguments.FirstOrDefault()) + "?";
    if (type.IsArray)
    {
        string array = FormatType(type.GetElementType()!, nullable?.ElementType) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        return AppendNullable(array, type, nullable);
    }
    if (type.IsGenericParameter) return AppendNullable(type.Name, type, nullable);
    string name = (type.FullName ?? type.Name).Replace('+', '.');
    int marker = name.IndexOf('`');
    if (marker >= 0) name = name[..marker];
    if (type.IsGenericType)
    {
        Type[] arguments = type.GetGenericArguments();
        NullabilityInfo[] nullableArguments = nullable?.GenericTypeArguments ?? Array.Empty<NullabilityInfo>();
        name += "<" + string.Join(",", arguments.Select((argument, index) => FormatType(argument, index < nullableArguments.Length ? nullableArguments[index] : null))) + ">";
    }
    return AppendNullable(name, type, nullable);
}

static string AppendNullable(string value, Type type, NullabilityInfo? info) => !type.IsValueType && info?.ReadState == NullabilityState.Nullable ? value + "?" : value;

static string FormatGenericConstraints(Type[] arguments)
{
    var result = new List<string>();
    foreach (Type argument in arguments.Where(item => item.IsGenericParameter))
    {
        var values = new List<string>();
        GenericParameterAttributes attributes = argument.GenericParameterAttributes;
        bool unmanaged = argument.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsUnmanagedAttribute");
        if (unmanaged) values.Add("unmanaged");
        else if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0) values.Add("struct");
        else if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0) values.Add("class");
        values.AddRange(argument.GetGenericParameterConstraints().Where(type => type != typeof(ValueType)).Select(type => FormatType(type)));
        if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 && !unmanaged && !values.Contains("struct", StringComparer.Ordinal)) values.Add("new()");
        if (values.Count != 0) result.Add(argument.Name + ":" + string.Join("&", values));
    }
    return result.Count == 0 ? string.Empty : ";where=" + string.Join(",", result);
}

static string FormatConstant(object? value)
{
    if (value is null || value == DBNull.Value || value == Missing.Value) return "null";
    if (value is string text) return JsonSerializer.Serialize(text);
    if (value is char character) return "'" + character.ToString().Replace("'", "\\'", StringComparison.Ordinal) + "'";
    if (value is bool boolean) return boolean ? "true" : "false";
    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
}

static bool IsVisible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
static string Visibility(MethodBase method) => method.IsPublic ? "public" : method.IsFamily ? "protected" : "protected-internal";
static string TypeVisibility(Type type) => type.IsNestedPublic ? "public" : type.IsNestedFamily ? "protected" : type.IsNestedFamORAssem ? "protected-internal" : "public";
static bool IsAccessor(MethodInfo method) => method.IsSpecialName && (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal) || method.Name.StartsWith("add_", StringComparison.Ordinal) || method.Name.StartsWith("remove_", StringComparison.Ordinal));
static string AccessorVisibility(MethodInfo? method) => method is null || !IsVisible(method) ? "none" : Visibility(method);

static string FirstDifference(string expected, string actual)
{
    string[] left = expected.Split('\n'); string[] right = actual.Split('\n');
    for (int index = 0; index < Math.Min(left.Length, right.Length); index++)
        if (!string.Equals(left[index], right[index], StringComparison.Ordinal)) return $"First difference at line {index + 1}: expected '{left[index]}', actual '{right[index]}'.";
    return $"Line counts differ: expected {left.Length}, actual {right.Length}.";
}

static Dictionary<string, string> ParseOptions(string[] arguments)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int index = 0; index < arguments.Length; index++)
    {
        string argument = arguments[index];
        if (!argument.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Unexpected argument: " + argument);
        string name = argument[2..];
        if (name is "check" or "write") result[name] = "true";
        else { if (++index >= arguments.Length) throw new ArgumentException("Missing value for " + argument); result[name] = arguments[index]; }
    }
    return result;
}

static string RequireOption(IReadOnlyDictionary<string, string> options, string name) => options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Missing --" + name);
static string RequirePath(IReadOnlyDictionary<string, string> options, string name) { string path = Path.GetFullPath(RequireOption(options, name)); return File.Exists(path) ? path : throw new FileNotFoundException("Input file is missing.", path); }
