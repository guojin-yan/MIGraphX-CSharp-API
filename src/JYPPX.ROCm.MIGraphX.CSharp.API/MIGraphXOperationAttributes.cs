using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 强类型 operation 属性构建器；Typed builder for the materialized operation-attribute text.
/// </summary>
/// <remarks>
/// The upstream C entry point is variadic. This type serializes the common scalar and vector
/// values into one complete MIGraphX attribute object, so the managed binding never passes
/// arbitrary C# objects or format arguments through the native ABI. Format placeholders and
/// arbitrary C varargs remain unsupported.
/// </remarks>
public sealed class MIGraphXOperationAttributes
{
    private readonly List<KeyValuePair<string, string>> values = new List<KeyValuePair<string, string>>();
    private readonly HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>添加 32 位有符号整数属性；Adds a signed 32-bit integer attribute.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="value">属性值；Attribute value.</param>
    public MIGraphXOperationAttributes SetInt32(string key, int value) => Add(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>添加 32 位无符号整数属性；Adds an unsigned 32-bit integer attribute.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="value">属性值；Attribute value.</param>
    public MIGraphXOperationAttributes SetUInt32(string key, uint value) => Add(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>添加 64 位有符号整数属性；Adds a signed 64-bit integer attribute.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="value">属性值；Attribute value.</param>
    public MIGraphXOperationAttributes SetInt64(string key, long value) => Add(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>添加 64 位无符号整数属性；Adds an unsigned 64-bit integer attribute.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="value">属性值；Attribute value.</param>
    public MIGraphXOperationAttributes SetUInt64(string key, ulong value) => Add(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>添加单精度浮点属性；Adds a single-precision floating-point attribute.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="value">有限浮点值；Finite attribute value.</param>
    public MIGraphXOperationAttributes SetSingle(string key, float value) => Add(key, FormatFinite(value));

    /// <summary>添加双精度浮点属性；Adds a double-precision floating-point attribute.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="value">有限浮点值；Finite attribute value.</param>
    public MIGraphXOperationAttributes SetDouble(string key, double value) => Add(key, FormatFinite(value));

    /// <summary>添加布尔属性；Adds a Boolean attribute.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="value">属性值；Attribute value.</param>
    public MIGraphXOperationAttributes SetBoolean(string key, bool value) => Add(key, value ? "true" : "false");

    /// <summary>添加字符串或枚举文本属性；Adds a string or enum-text attribute.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="value">字符串或枚举文本；String or enum text.</param>
    public MIGraphXOperationAttributes SetString(string key, string value) => Add(key, Quote(value, nameof(value)));

    /// <summary>添加 null 属性；Adds a null attribute.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    public MIGraphXOperationAttributes SetNull(string key) => Add(key, "null");

    /// <summary>添加 32 位有符号整数数组；Adds a signed 32-bit integer array.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="values">属性数组；Attribute values.</param>
    public MIGraphXOperationAttributes SetInt32Array(string key, IReadOnlyList<int> values) => AddArray(key, values, value => value.ToString(CultureInfo.InvariantCulture), nameof(values));

    /// <summary>添加 32 位无符号整数数组；Adds an unsigned 32-bit integer array.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="values">属性数组；Attribute values.</param>
    public MIGraphXOperationAttributes SetUInt32Array(string key, IReadOnlyList<uint> values) => AddArray(key, values, value => value.ToString(CultureInfo.InvariantCulture), nameof(values));

    /// <summary>添加 64 位有符号整数数组；Adds a signed 64-bit integer array.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="values">属性数组；Attribute values.</param>
    public MIGraphXOperationAttributes SetInt64Array(string key, IReadOnlyList<long> values) => AddArray(key, values, value => value.ToString(CultureInfo.InvariantCulture), nameof(values));

    /// <summary>添加 64 位无符号整数数组；Adds an unsigned 64-bit integer array.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="values">属性数组；Attribute values.</param>
    public MIGraphXOperationAttributes SetUInt64Array(string key, IReadOnlyList<ulong> values) => AddArray(key, values, value => value.ToString(CultureInfo.InvariantCulture), nameof(values));

    /// <summary>添加单精度浮点数组；Adds a single-precision floating-point array.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="values">属性数组；Attribute values.</param>
    public MIGraphXOperationAttributes SetSingleArray(string key, IReadOnlyList<float> values) => AddArray(key, values, FormatFinite, nameof(values));

    /// <summary>添加双精度浮点数组；Adds a double-precision floating-point array.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="values">属性数组；Attribute values.</param>
    public MIGraphXOperationAttributes SetDoubleArray(string key, IReadOnlyList<double> values) => AddArray(key, values, FormatFinite, nameof(values));

    /// <summary>添加字符串或枚举文本数组；Adds a string or enum-text array.</summary>
    /// <param name="key">属性键；Attribute key.</param>
    /// <param name="values">字符串或枚举文本数组；String or enum-text values.</param>
    public MIGraphXOperationAttributes SetStringArray(string key, IReadOnlyList<string> values) => AddArray(key, values, value => Quote(value, nameof(values)), nameof(values));

    /// <summary>构建 MIGraphX 属性对象文本；Builds the MIGraphX attribute-object text.</summary>
    public string Build()
    {
        var builder = new StringBuilder("{");
        for (var index = 0; index < values.Count; index++)
        {
            if (index != 0) builder.Append(", ");
            builder.Append(values[index].Key).Append(": ").Append(values[index].Value);
        }
        return builder.Append("}").ToString();
    }

    private MIGraphXOperationAttributes Add(string key, string serialized)
    {
        ValidateKey(key);
        if (!keys.Add(key)) throw new ArgumentException($"The operation attribute key '{key}' was already set.", nameof(key));
        values.Add(new KeyValuePair<string, string>(key, serialized));
        return this;
    }

    private MIGraphXOperationAttributes AddArray<T>(string key, IReadOnlyList<T> array, Func<T, string> formatter, string parameterName)
    {
        if (array is null) throw new ArgumentNullException(parameterName);
        var serialized = string.Join(", ", array.Select(formatter));
        return Add(key, "[" + serialized + "]");
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("The operation attribute key must not be empty.", nameof(key));
        if (!(IsAsciiLetter(key[0]) || key[0] == '_')) throw new ArgumentException("The operation attribute key must start with an ASCII letter or underscore.", nameof(key));
        for (var index = 1; index < key.Length; index++)
        {
            var character = key[index];
            if (!(IsAsciiLetter(character) || character >= '0' && character <= '9' || character == '_'))
                throw new ArgumentException("The operation attribute key must contain only ASCII letters, digits, or underscores.", nameof(key));
        }
    }

    private static bool IsAsciiLetter(char value) => value >= 'A' && value <= 'Z' || value >= 'a' && value <= 'z';

    private static string FormatFinite(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "Operation attributes must use finite floating-point values.");
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string FormatFinite(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "Operation attributes must use finite floating-point values.");
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Quote(string value, string parameterName)
    {
        if (value is null) throw new ArgumentNullException(parameterName);
        if (value.IndexOf('\0') >= 0) throw new ArgumentException("Operation attribute strings must not contain embedded NUL characters.", parameterName);
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (character < 0x20) builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(character);
                    break;
            }
        }
        return builder.Append('"').ToString();
    }
}
