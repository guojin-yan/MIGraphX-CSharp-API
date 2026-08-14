using System.Buffers.Binary;
using System.Text;

var classify = args.Length == 2 && args[0] == "--classify";
if (args.Length != 1 && !classify)
{
    Console.Error.WriteLine("Usage: ElfExportReader [--classify] <ELF-file>");
    return 2;
}

var bytes = File.ReadAllBytes(args[^1]);
if (bytes.Length < 64 || bytes[0] != 0x7f || bytes[1] != (byte)'E' || bytes[2] != (byte)'L' || bytes[3] != (byte)'F')
{
    Console.Error.WriteLine("Input is not an ELF file.");
    return 2;
}
if (bytes[4] != 2 || bytes[5] != 1)
{
    Console.Error.WriteLine("Only little-endian ELF64 is supported by this gate.");
    return 2;
}

var sectionOffset = checked((int)ReadUInt64(bytes, 40));
var sectionEntrySize = ReadUInt16(bytes, 58);
var sectionCount = ReadUInt16(bytes, 60);
var sections = new Section[sectionCount];
for (var index = 0; index < sectionCount; index++)
{
    var offset = checked(sectionOffset + index * sectionEntrySize);
    sections[index] = new Section(
        ReadUInt32(bytes, offset + 4),
        ReadUInt32(bytes, offset + 40),
        checked((int)ReadUInt64(bytes, offset + 24)),
        checked((int)ReadUInt64(bytes, offset + 32)),
        checked((int)ReadUInt64(bytes, offset + 56)));
}

var exports = new SortedDictionary<string, string>(StringComparer.Ordinal);
foreach (var section in sections.Where(item => item.Type == 11))
{
    if (section.Link >= sections.Length || section.EntrySize < 24)
    {
        continue;
    }
    var strings = sections[section.Link];
    for (var offset = section.Offset; offset + section.EntrySize <= section.Offset + section.Size; offset += section.EntrySize)
    {
        var nameOffset = ReadUInt32(bytes, offset);
        var info = bytes[offset + 4];
        var visibility = bytes[offset + 5] & 0x3;
        var sectionIndex = ReadUInt16(bytes, offset + 6);
        var binding = info >> 4;
        if (nameOffset == 0 || sectionIndex == 0 || visibility != 0 || binding == 0)
        {
            continue;
        }
        var name = ReadString(bytes, strings.Offset + checked((int)nameOffset), strings.Offset + strings.Size);
        if (name.StartsWith("migraphx_", StringComparison.Ordinal))
        {
            var type = (info & 0xf) switch
            {
                0 => "notype",
                1 => "object",
                2 => "function",
                3 => "section",
                4 => "file",
                5 => "common",
                6 => "tls",
                10 => "gnu-ifunc",
                var value => $"type-{value}",
            };
            exports[name] = type;
        }
    }
}

foreach (var export in exports)
{
    Console.WriteLine(classify ? $"{export.Value}|{export.Key}" : export.Key);
}

return 0;

static ushort ReadUInt16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
static uint ReadUInt32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
static ulong ReadUInt64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, 8));

static string ReadString(byte[] bytes, int offset, int limit)
{
    var end = offset;
    while (end < limit && bytes[end] != 0)
    {
        end++;
    }
    return Encoding.ASCII.GetString(bytes, offset, end - offset);
}

internal readonly record struct Section(uint Type, uint Link, int Offset, int Size, int EntrySize);
