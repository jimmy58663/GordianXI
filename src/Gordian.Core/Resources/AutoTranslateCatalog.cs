using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Gordian.Core.Resources;

public sealed record AutoTranslatePhrase(uint Key, string Category, string Label);

/// <summary>
/// English fixed dictionary, ROM/168/25.DAT. Format reference:
/// https://github.com/vekien/xi-tools/blob/main/docs/dats/ROM_168_25.md .
/// Independently observed correction: header is 76 bytes, with two 32-byte names,
/// count at 68 and payload size at 72. No reference source code is used.
/// Reference schema: Windower/ResourceExtractor Program.cs (linked auto-translate names):
/// @J jobs, @A zones, @C spells, @Y actions; suffix is a hexadecimal resource index.
/// Unresolved resource references and blank placeholders are deliberately excluded.
/// </summary>
public sealed class AutoTranslateCatalog
{
    public static AutoTranslateCatalog Empty { get; } = new([], 0, 0);
    public IReadOnlyList<AutoTranslatePhrase> Phrases { get; }
    public int UnresolvedCount { get; }
    public int BlankCount { get; }
    private readonly Dictionary<uint, string> _labels = new();
    private AutoTranslateCatalog(List<AutoTranslatePhrase> phrases, int unresolved, int blank)
    {
        Phrases = phrases.AsReadOnly(); UnresolvedCount = unresolved; BlankCount = blank;
        foreach (var phrase in phrases) _labels.Add(phrase.Key, phrase.Label);
    }
    public string? Resolve(uint key)
    {
        // Language selects the sender's dictionary; render the same group/id in this English catalog.
        uint language = (key >> 8) & 255;
        return (key & 255) == 2 && language is >= 1 and <= 4
            ? _labels.GetValueOrDefault((key & 0xFFFF00FF) | 0x0200) : null;
    }
    public static AutoTranslateCatalog Parse(ReadOnlySpan<byte> data, Func<char, int, string?>? resolve = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var phrases = new List<AutoTranslatePhrase>();
        var keys = new HashSet<uint>(); var groups = new HashSet<uint>();
        int unresolved = 0, blank = 0;
        try
        {
            while (!data.IsEmpty)
            {
                if (data.Length < 76) throw new InvalidDataException("Truncated dictionary header.");
                uint group = BinaryPrimitives.ReadUInt32LittleEndian(data);
                if (data[0] != 2 || data[1] != 2 || data[2] == 0 || data[3] != 0 || !groups.Add(group))
                    throw new InvalidDataException("Unsupported or duplicate dictionary category.");
                var titleBytes = data.Slice(4, 32);
                int nul = titleBytes.IndexOf((byte)0);
                if (nul < 0) throw new InvalidDataException("Unterminated category.");
                string title = encoding.GetString(titleBytes[..nul]);
                uint count = BinaryPrimitives.ReadUInt32LittleEndian(data[68..]);
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[72..]);
                if (length > data.Length - 76 || count > length / 6) throw new InvalidDataException("Invalid category bounds.");
                var entries = data.Slice(76, (int)length);
                for (uint i = 0; i < count; i++)
                {
                    if (entries.Length < 6) throw new InvalidDataException("Truncated entry.");
                    uint key = BinaryPrimitives.ReadUInt32LittleEndian(entries);
                    int size = entries[4];
                    if ((key & 0xFFFFFF) != group || entries[3] == 0 || !keys.Add(key)
                        || size < 1 || size > entries.Length - 5) throw new InvalidDataException("Invalid dictionary entry.");
                    var label = entries.Slice(5, size);
                    if (label[^1] != 0 || label[..^1].Contains((byte)0)) throw new InvalidDataException("Invalid entry terminator.");
                    string text = encoding.GetString(label[..^1]);
                    if (text.StartsWith('@'))
                    {
                        string? resolved = null;
                        if (text.Length > 2 && text[1] is 'J' or 'A' or 'C' or 'Y'
                            && int.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.AllowHexSpecifier,
                                System.Globalization.CultureInfo.InvariantCulture, out int id)) resolved = resolve?.Invoke(text[1], id);
                        if (string.IsNullOrWhiteSpace(resolved) || resolved.StartsWith('@')) unresolved++;
                        else phrases.Add(new(key, title, resolved));
                    }
                    else if (string.IsNullOrWhiteSpace(text)) blank++;
                    else phrases.Add(new(key, title, text));
                    entries = entries[(5 + size)..];
                }
                if (!entries.IsEmpty) throw new InvalidDataException("Category count/size mismatch.");
                data = data[(76 + (int)length)..];
            }
        }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid CP932 dictionary text.", ex); }
        return new(phrases, unresolved, blank);
    }
}
