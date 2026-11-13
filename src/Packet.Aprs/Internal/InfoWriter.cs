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

    private AprsEncodeException? deferred;

    /// <summary>
    /// A refusal that comes from how the parts of a report relate (a symbol that does not fit the
    /// data, two things wanting one field, a companion that is missing) is held until the whole report
    /// has been written, so that a fault in a single field, which is wrong whatever else is given,
    /// is reported first. Held refusals are reported in the vectors' reason-code order (README,
    /// Refusal reasons); of two with the same code, the first met is kept.
    /// </summary>
    public void Defer(AprsRefusalCode code, string message) => Hold(new AprsEncodeException(code, message));

    /// <summary>Holds a refusal already made, as <see cref="Defer"/> does.</summary>
    public void Hold(AprsEncodeException refusal)
    {
        if (deferred is null || Rank(refusal.Code) < Rank(deferred.Code))
        {
            deferred = refusal;
        }
    }

    /// <summary>True once a refusal is held.</summary>
    public bool HasDeferred => deferred is not null;

    /// <summary>True when a field stopped part way, so what is written is not the whole report.</summary>
    public bool Interrupted { get; set; }

    /// <summary>
    /// Writes one field of a report. A refusal for it is held rather than thrown, so the fields
    /// after it are still checked and the refusal finally reported is the first in the reason-code
    /// order, whichever field it is in (vectors interpretations.md, "Which refusal a record with
    /// two faults gets"). Nothing written after a held refusal is ever returned.
    /// </summary>
    public void Field(Action write)
    {
        try
        {
            write();
        }
        catch (AprsEncodeException ex)
        {
            Interrupted = true;
            Hold(ex);
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

    /// <summary>The vectors' README "Refusal reasons" order: the first code that applies is named.</summary>
    internal static int Rank(AprsRefusalCode code) => code switch
    {
        AprsRefusalCode.NotEncodable => 0,
        AprsRefusalCode.InvalidAddress => 1,
        AprsRefusalCode.Ax25CannotCarry => 2,
        AprsRefusalCode.Tnc2CannotCarry => 3,
        AprsRefusalCode.InvalidThirdPartyHeader => 4,
        AprsRefusalCode.InvalidAddressee => 5,
        AprsRefusalCode.InvalidName => 6,
        AprsRefusalCode.InvalidTimestamp => 7,
        AprsRefusalCode.NotANumber => 8,
        AprsRefusalCode.ValueOutOfRange => 9,
        AprsRefusalCode.ValueHasNoForm => 10,
        AprsRefusalCode.InvalidCode => 11,
        AprsRefusalCode.InvalidSymbol => 12,
        AprsRefusalCode.InvalidCharacter => 13,
        AprsRefusalCode.TooLong => 14,
        AprsRefusalCode.PaddingSpace => 15,
        AprsRefusalCode.Malformed => 16,
        AprsRefusalCode.InvalidMessageId => 17,
        AprsRefusalCode.InvalidNmea => 18,
        AprsRefusalCode.ExtensionConflict => 19,
        AprsRefusalCode.FieldNotAllowed => 20,
        AprsRefusalCode.SymbolMismatch => 21,
        AprsRefusalCode.MissingField => 22,
        AprsRefusalCode.ValueGivenTwice => 23,
        AprsRefusalCode.TextReadsAsField => 24,
        _ => 25,
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
