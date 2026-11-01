namespace MIGraphXSharp.Samples.PPStructure.Ocr;

public sealed class Arguments
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    public Arguments(string[] args)
    {
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 == args.Length || !args[index].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[index][2..], args[index + 1]))
                throw new ArgumentException("Use unique --name value options.");
        }
    }
    public string Required(string name) => values.TryGetValue(name, out var value) ? value : throw new ArgumentException($"Missing --{name}.");
    public string Get(string name, string fallback) => values.TryGetValue(name, out var value) ? value : fallback;
    public bool Has(string name) => values.ContainsKey(name);
    public int Integer(string name, int fallback, int minimum = 1, int maximum = 8192)
    {
        var value = int.Parse(Get(name, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
        return value >= minimum && value <= maximum ? value : throw new ArgumentOutOfRangeException(name);
    }
    public float Probability(string name, float fallback)
    {
        var value = float.Parse(Get(name, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
        return float.IsFinite(value) && value >= 0 && value <= 1 ? value : throw new ArgumentOutOfRangeException(name);
    }
}
