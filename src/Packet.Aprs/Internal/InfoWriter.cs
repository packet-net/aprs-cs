using System.Buffers;
using System.Globalization;
using System.Text;

namespace Packet.Aprs.Internal;

/// <summary>Builds information-field bytes.</summary>
internal sealed class InfoWriter
{
    private readonly ArrayBufferWriter<byte> buffer = new(64);

    public int Length => buffer.WrittenCount;

    /// <summary>
    /// Set by a comment check that cannot tell from the text alone whether the comment will read
    /// back unchanged, because that depends on what is written around it. <see cref="AprsData.ToInformationField"/>
    /// then decodes the finished field to find out, and refuses with this message if it does not.
    /// </summary>
    public string? CommentCheck { get; set; }

    /// <summary>The code a refusal from the read-back of the finished field carries.</summary>
    public AprsRefusalCode ReadBackCode { get; set; } = AprsRefusalCode.TextReadsAsField;

    private AprsEncodeException? deferred;

    /// <summary>
    /// A refusal that comes from how the parts of a report relate (a symbol that does not fit the
    /// data, two things wanting one field, a companion that is missing) is held until the whole report
    /// has been written, so that a fault in a single field, which is wrong whatever else is given,
    /// is reported first, and the held ones are then reported in a fixed order (the vectors'
    /// reason-code order). The first of the highest-ranked code is kept.
    /// </summary>
    public void Defer(AprsRefusalCode code, string message)
    {
        if (deferred is null || Rank(code) < Rank(deferred.Code))
        {
            deferred = new AprsEncodeException(code, message);
        }
    }

    /// <summary>Throws the refusal held by <see cref="Defer"/>, if there is one.</summary>
    public void ThrowDeferred()
    {
        if (deferred is not null)
        {
            throw deferred;
        }
    }

    private static int Rank(AprsRefusalCode code) => code switch
    {
        AprsRefusalCode.ExtensionConflict => 0,
        AprsRefusalCode.FieldNotAllowed => 1,
        AprsRefusalCode.SymbolMismatch => 2,
        AprsRefusalCode.MissingField => 3,
        AprsRefusalCode.ValueGivenTwice => 4,
        _ => 5,
    };

    public ReadOnlySpan<byte> Written => buffer.WrittenSpan;

    public InfoWriter Byte(byte b)
    {
        buffer.GetSpan(1)[0] = b;
        buffer.Advance(1);
        return this;
    }

    public InfoWriter Char(char c)
    {
        if (c > 0xFF)
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidCharacter, $"'{c}' is not a single-byte character");
        }

        return Byte((byte)c);
    }

    public InfoWriter Bytes(ReadOnlySpan<byte> bytes)
    {
        buffer.Write(bytes);
        return this;
    }

    /// <summary>Writes ASCII; throws on anything outside 0x20-0x7E.</summary>
    public InfoWriter Ascii(string text)
    {
        foreach (char c in text)
        {
            if (c is < ' ' or > '~')
            {
                throw new AprsEncodeException(AprsRefusalCode.InvalidCharacter, $"'{text}' contains a character that is not printable ASCII");
            }

            Byte((byte)c);
        }

        return this;
    }

    /// <summary>Writes free text as UTF-8 (APRS 1.2 allows UTF-8 in comments and message text).</summary>
    public InfoWriter Utf8(string text)
    {
        buffer.Write(Encoding.UTF8.GetBytes(text));
        return this;
    }

    /// <summary>Writes a non-negative integer zero-padded to exactly <paramref name="width"/> digits.</summary>
    public InfoWriter Digits(int value, int width)
    {
        string s = value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        if (value < 0 || s.Length != width)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, $"{value} does not fit in {width} digits");
        }

        return Ascii(s);
    }

    public InfoWriter Base91(long value, int digits)
    {
        Span<byte> tmp = stackalloc byte[digits];
        Internal.Base91.Encode(value, tmp);
        return Bytes(tmp);
    }

    public byte[] ToArray() => buffer.WrittenSpan.ToArray();
}
