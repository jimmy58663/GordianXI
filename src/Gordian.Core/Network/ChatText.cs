using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gordian.Core.Resources;

namespace Gordian.Core.Network;

public readonly record struct ChatPhraseSpan(int Start, int Length, uint Key);

/// <summary>
/// Display text plus explicit phrase identities, never magic Unicode markers. Wire format:
/// FD/type/language/group/id/FD (https://github.com/jimmy58663/GordianXI/issues/388).
/// Ordinary text uses CP932; selected phrases alone become six-byte codes.
/// </summary>
public sealed class ChatText
{
    // LSB C2S 0x0B5 Str[128] / 0x0B6 Mes[128]: keep the terminating NUL inside the field.
    public const int MaxEncodedBytes = 127;
    public string Text { get; }
    public IReadOnlyList<ChatPhraseSpan> Phrases { get; }
    private static readonly Encoding Encoding = CreateEncoding();
    private static Encoding CreateEncoding()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
    }
    public ChatText(string text, IEnumerable<ChatPhraseSpan>? phrases = null)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        var spans = (phrases ?? []).ToArray();
        int end = 0;
        foreach (var p in spans)
        {
            if (p.Start < end || p.Length <= 0 || p.Start > text.Length - p.Length ||
                (p.Key & 0xFFFF) != 0x0202 || (p.Key >> 24) == 0 || (p.Key & 0xFF0000) == 0)
                throw new ArgumentException("Invalid phrase span.", nameof(phrases));
            end = p.Start + p.Length;
        }
        Phrases = Array.AsReadOnly(spans);
    }
    public ChatText Slice(int start, int length)
    {
        var spans = Phrases.Where(p => p.Start >= start && p.Start + p.Length <= start + length)
            .Select(p => p with { Start = p.Start - start });
        return new(Text.Substring(start, length), spans);
    }
    public ChatText TransformPlain(Func<string, string> transform)
    {
        var text = new StringBuilder(); var spans = new List<ChatPhraseSpan>(); int pos = 0;
        foreach (var p in Phrases)
        {
            text.Append(transform(Text[pos..p.Start]));
            spans.Add(p with { Start = text.Length }); text.Append(Text.AsSpan(p.Start, p.Length)); pos = p.Start + p.Length;
        }
        text.Append(transform(Text[pos..])); return new(text.ToString(), spans);
    }
    public string Display => Decorate();
    private string Decorate()
    {
        var text = new StringBuilder(Text);
        foreach (var p in Phrases.Reverse()) { text.Insert(p.Start + p.Length, '}'); text.Insert(p.Start, '{'); }
        return text.ToString();
    }
    /// <summary>Atomic encoding: validates the entire 127-byte content budget before writing any destination bytes.</summary>
    public int Encode(Span<byte> destination)
    {
        int size = 0, pos = 0;
        foreach (var p in Phrases) { size += PlainSize(Text.AsSpan(pos, p.Start - pos)) + 6; pos = p.Start + p.Length; }
        size += PlainSize(Text.AsSpan(pos));
        if (size > MaxEncodedBytes || destination.Length < size) throw new ArgumentException("Chat exceeds the encoded byte budget.");
        int offset = 0; pos = 0;
        foreach (var p in Phrases)
        {
            offset += Encoding.GetBytes(Text.AsSpan(pos, p.Start - pos), destination[offset..]);
            destination[offset++] = 0xFD;
            BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], p.Key); offset += 4;
            destination[offset++] = 0xFD; pos = p.Start + p.Length;
        }
        offset += Encoding.GetBytes(Text.AsSpan(pos), destination[offset..]); return offset;
    }
    internal static int EncodePlain(ReadOnlySpan<char> text, Span<byte> destination)
    {
        int size = PlainSize(text);
        if (size > MaxEncodedBytes || destination.Length < size) throw new ArgumentException("Chat exceeds the encoded byte budget.");
        return Encoding.GetBytes(text, destination);
    }
    private static int PlainSize(ReadOnlySpan<char> text)
    {
        foreach (char c in text) if (char.IsControl(c)) throw new ArgumentException("Chat contains a control character.");
        int size = Encoding.GetByteCount(text);
        if (size <= 128)
        {
            Span<byte> check = stackalloc byte[128];
            int written = Encoding.GetBytes(text, check);
            if (check[..written].Contains((byte)0xFD)) throw new ArgumentException("Plain text cannot contain a wire marker.");
        }
        return size;
    }
    public static string Decode(ReadOnlySpan<byte> wire, AutoTranslateCatalog catalog)
    {
        var text = new StringBuilder(); int i = 0;
        while (i < wire.Length && wire[i] != 0)
        {
            if (wire[i] == 0xFD)
            {
                if (wire.Length - i >= 6 && wire[i + 5] == 0xFD)
                {
                    uint key = BinaryPrimitives.ReadUInt32LittleEndian(wire[(i + 1)..]);
                    text.Append('{').Append(catalog.Resolve(key) ?? "Unknown phrase").Append('}'); i += 6;
                }
                else { text.Append("[invalid phrase]"); i++; }
                continue;
            }
            int start = i;
            while (i < wire.Length && wire[i] != 0 && wire[i] != 0xFD) i++;
            text.Append(Encoding.GetString(wire[start..i]));
        }
        return text.ToString();
    }
}
