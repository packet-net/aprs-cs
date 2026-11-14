using System.Text;

namespace Packet.Aprs.Internal;

/// <summary>Converts information-field bytes to and from text.</summary>
internal static class Text
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Decodes free text. APRS 1.2 text is ASCII or UTF-8 (APRS12c §5 Comment Field). Bytes that
    /// are not valid UTF-8 (a Latin-1 or code page 437 degree sign, Kenwood 0xFF bursts) are a real
    /// defect (UAP §5.16); when tolerated they are mapped one byte per character as Latin-1 so no
    /// information is lost.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> bytes, DecodeContext ctx, int offset, out string text, bool judgeControlCharacters = true)
    {
        bool ok;
        if (IsAscii(bytes))
        {
            text = Encoding.ASCII.GetString(bytes);
            ok = true;
        }
        else
        {
            try
            {
                text = StrictUtf8.GetString(bytes);
                ok = true;
            }
            catch (DecoderFallbackException)
            {
                text = Encoding.Latin1.GetString(bytes);
                ok = ctx.Tolerate(
                    ctx.Options.AllowNonUtf8Text,
                    AprsDiagnosticCode.NonUtf8Text,
                    "text is not valid UTF-8; decoded byte-for-byte as Latin-1",
                    offset);
            }
        }

        return ok && (!judgeControlCharacters || JudgeControlCharacters(bytes, ctx, offset));
    }

    /// <summary>
    /// The control-character rule (vectors README, "Control characters in text"): text that holds a
    /// byte below 0x20 or 0x7F gets one warning, however many it holds, and is kept as sent. It is
    /// judged last for its text, after the text's structure and its encoding.
    /// </summary>
    public static bool JudgeControlCharacters(ReadOnlySpan<byte> bytes, DecodeContext ctx, int offset) =>
        !bytes.ContainsAnyInRange((byte)0, (byte)0x1F) && !bytes.Contains((byte)0x7F)
        || ctx.Tolerate(
            ctx.Options.AllowControlCharacterInText,
            AprsDiagnosticCode.ControlCharacterInText,
            "text holds a control character (a byte below 0x20, or 0x7F); APRS12c gives text as printable characters",
            offset);

    /// <summary>The same for text already read as a string.</summary>
    public static bool JudgeControlCharacters(string text, DecodeContext ctx, int offset) =>
        !text.AsSpan().ContainsAnyInRange('\0', '\u001F') && !text.Contains('\u007F', StringComparison.Ordinal)
        || ctx.Tolerate(
            ctx.Options.AllowControlCharacterInText,
            AprsDiagnosticCode.ControlCharacterInText,
            "text holds a control character (a byte below 0x20, or 0x7F); APRS12c gives text as printable characters",
            offset);

    /// <summary>UTF-8 if valid, otherwise Latin-1; never fails. For display and diagnostics.</summary>
    public static string ForDisplay(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>Decodes bytes that the format defines as printable ASCII; anything else is Latin-1 mapped
    /// and left for the caller to validate.</summary>
    public static string Latin1(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    public static bool IsAscii(ReadOnlySpan<byte> bytes) => System.Text.Ascii.IsValid(bytes);

    public static bool IsPrintableAscii(byte b) => b is >= 0x20 and <= 0x7E;

    public static bool IsDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    public static bool IsUpper(byte b) => b is >= (byte)'A' and <= (byte)'Z';

    public static bool IsLower(byte b) => b is >= (byte)'a' and <= (byte)'z';

    public static bool IsAlphanumeric(byte b) => IsDigit(b) || IsUpper(b) || IsLower(b);

    public static bool AllDigits(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            if (!IsDigit(b))
            {
                return false;
            }
        }

        return bytes.Length > 0;
    }

    public static int ParseDigits(ReadOnlySpan<byte> bytes)
    {
        int n = 0;
        foreach (byte b in bytes)
        {
            n = (n * 10) + (b - '0');
        }

        return n;
    }

    /// <summary>
    /// A telemetry value: an optional <c>-</c>, then digits with an optional decimal point, with at
    /// least one digit, and nothing else (APRS12c ch. 13; vectors interpretations.md, "Numbers in
    /// telemetry"). No <c>+</c>, no spaces, no NUL.
    /// </summary>
    public static bool IsTelemetryNumber(ReadOnlySpan<char> s)
    {
        ReadOnlySpan<char> digits = s.StartsWith('-') ? s[1..] : s;
        int dot = digits.IndexOf('.');
        ReadOnlySpan<char> whole = dot < 0 ? digits : digits[..dot];
        ReadOnlySpan<char> fraction = dot < 0 ? [] : digits[(dot + 1)..];
        return whole.Length + fraction.Length > 0 && !whole.ContainsAnyExceptInRange('0', '9') && !fraction.ContainsAnyExceptInRange('0', '9');
    }

    /// <summary>For encoders: rejects text holding a control character, a byte below 0x20 or 0x7F (a
    /// TAB, and a CR or LF inside the text, included). APRS12c gives text as printable characters, and
    /// a CR or LF would end the packet on APRS-IS (vectors README, "Control characters in text").</summary>
    public static void RequireNoControlCharacters(string text)
    {
        if (text.AsSpan().ContainsAnyInRange('\0', '\u001F') || text.Contains('\u007F', StringComparison.Ordinal))
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidCharacter, "text must not contain a control character (a byte below 0x20, or 0x7F)");
        }
    }

    /// <summary>For encoders: rejects text containing CR or LF, which would end the packet on
    /// APRS-IS (APRS12c §5 Comment Field). Used where the data is not judged as text (test data).</summary>
    public static void RequireNoLineBreaks(string text)
    {
        if (text.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidCharacter, "text must not contain carriage return or line feed");
        }
    }
}
