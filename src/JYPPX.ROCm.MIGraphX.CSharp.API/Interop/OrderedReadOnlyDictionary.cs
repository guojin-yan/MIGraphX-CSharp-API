using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class OrderedReadOnlyDictionary<TValue> : IReadOnlyDictionary<string, TValue>
{
    private readonly KeyValuePair<string, TValue>[] entries;
    private readonly Dictionary<string, TValue> lookup;

    internal OrderedReadOnlyDictionary(IEnumerable<KeyValuePair<string, TValue>> entries)
    {
        this.entries = entries.ToArray();
        lookup = this.entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    }

    public TValue this[string key] => lookup[key];

    public IEnumerable<string> Keys => entries.Select(entry => entry.Key);

    public IEnumerable<TValue> Values => entries.Select(entry => entry.Value);

    public int Count => entries.Length;

    public bool ContainsKey(string key) => lookup.ContainsKey(key);

    public bool TryGetValue(string key, out TValue value) => lookup.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, TValue>>)entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => entries.GetEnumerator();
}
