using System.Globalization;

namespace Packet.Aprs.Internal;

/// <summary>Status reports (APRS12c §16).</summary>
internal static class StatusCodec
{
    public static AprsData? Decode(ReadOnlySpan<byte> info, DecodeContext ctx)
    {
        int pos = 1;
        AprsTimestamp? timestamp = null;
        string? locator = null;
        AprsSymbol? symbol = null;

        if (info.Length >= 8 && Text.AllDigits(info.Slice(1, 6)) && info[7] == (byte)'z')
        {
            if (!TimestampCodec.TryRead(info, 1, ctx, out AprsTimestamp ts))
            {
                return null;
            }

            timestamp = ts;
            pos = 8;
        }
        else if (TryLocatorWithSymbol(info[1..], out int locatorLength))
        {
            // Letters may be received in either case but are sent in upper case (APRS12c §16).
            locator = Text.Latin1(info.Slice(1, locatorLength)).ToUpperInvariant();
            symbol = new AprsSymbol((char)info[1 + locatorLength], (char)info[2 + locatorLength]);
            pos = 3 + locatorLength;
            if (pos < info.Length)
            {
                if (info[pos] == (byte)' ')
                {
                    pos++;
                }
                else if (!ctx.Tolerate(
                             ctx.Options.AllowMissingSpaceAfterLocator,
                             AprsDiagnosticCode.MissingSpaceAfterLocator,
                             "status text after a grid locator must start with a space (APRS12c ch. 16, UAP 5.17)",
                             pos))
                {
                    return null;
                }
            }
        }

        ReadOnlySpan<byte> text = info[pos..];
        AprsBeamHeading? beam = null;
        if (text.Length >= 3 && text[^3] == (byte)'^')
        {
            var candidate = new AprsBeamHeading((char)text[^2], (char)text[^1]);
            if (candidate.IsValid)
            {
                beam = candidate;
                text = text[..^3];
            }
        }

        if (!Text.TryDecode(text, ctx, pos, out string decoded))
        {
            return null;
        }

        return new AprsStatusReport
        {
            Timestamp = timestamp,
            Text = decoded,
            Locator = locator,
            Symbol = symbol,
            BeamHeading = beam,
        };
    }

    /// <summary>A 4 or 6 character locator then a symbol table and code, then end or a space
    /// (or, tolerated, other text). Letters may be received in either case (APRS12c §16).</summary>
    private static bool TryLocatorWithSymbol(ReadOnlySpan<byte> s, out int length)
    {
        // ">JN55VD Powered by..." is a bare 6-character locator: plain text, not "JN55" with symbol "VD".
        bool sixCharacterLocator = s.Length >= 6 && Maidenhead.IsLocator(s[..6]);
        foreach (int candidate in sixCharacterLocator ? (int[])[6] : [6, 4])
        {
            length = candidate;
            if (s.Length >= candidate + 2 && Maidenhead.IsLocator(s[..candidate]) && AprsSymbol.IsValidTable((char)s[candidate])
                && s[candidate + 1] is >= (byte)'!' and <= (byte)'~')
            {
                return true;
            }
        }

        length = 0;
        return false;
    }

    public static void Write(InfoWriter w, AprsStatusReport r)
    {
        Text.RequireNoLineBreaks(r.Text);
        w.Char('>');
        if (r.Locator is { } locator)
        {
            if (r.Timestamp is not null)
            {
                throw new AprsEncodeException(AprsRefusalCode.FieldNotAllowed, "a status report with a grid locator cannot have a timestamp (APRS12c ch. 16)");
            }

            if (r.Symbol is not { } symbol)
            {
                throw new AprsEncodeException(AprsRefusalCode.MissingField, "a grid locator status needs a symbol");
            }

            if (!Maidenhead.IsLocator(System.Text.Encoding.ASCII.GetBytes(locator)) || locator.Length is not (4 or 6))
            {
                throw new AprsEncodeException(AprsRefusalCode.Malformed, $"'{locator}' is not a 4 or 6 character Maidenhead locator");
            }

            symbol.Validate();
            w.Ascii(locator.ToUpperInvariant()).Char(symbol.Table).Char(symbol.Code);
            if (r.Text.Length > 0 || r.BeamHeading is not null)
            {
                w.Char(' ');
            }
        }
        else if (r.Symbol is not null)
        {
            throw new AprsEncodeException(AprsRefusalCode.FieldNotAllowed, "a status symbol only goes with a grid locator");
        }
        else if (r.Timestamp is { } ts)
        {
            if (ts.Format != AprsTimestampFormat.DayHoursMinutesUtc)
            {
                throw new AprsEncodeException(AprsRefusalCode.InvalidTimestamp, "a status report timestamp must be DHM UTC (APRS12c ch. 16)");
            }

            TimestampCodec.Write(w, ts, allowMonthDay: false);
        }

        byte[] text = System.Text.Encoding.UTF8.GetBytes(r.Text);
        if (r.Locator is null && r.Timestamp is null && TryLocatorWithSymbol(text, out _))
        {
            throw new AprsEncodeException(AprsRefusalCode.TextReadsAsField, "status text starts with what reads as a grid locator and symbol; set Locator instead");
        }

        if (r.Locator is null && r.Timestamp is null && text.Length >= 7 && Text.AllDigits(text.AsSpan(0, 6)) && text[6] == (byte)'z')
        {
            throw new AprsEncodeException(AprsRefusalCode.TextReadsAsField, "status text starts with what reads as a timestamp; set Timestamp instead");
        }

        w.Bytes(text);
        if (r.BeamHeading is { } beam)
        {
            if (!beam.IsValid)
            {
                throw new AprsEncodeException(AprsRefusalCode.InvalidCode, "beam heading code must be 0-9 or A-Z and power code 1-K");
            }

            w.Char('^').Char(beam.HeadingCode).Char(beam.PowerCode);
        }
        else if (text.Length >= 3 && text[^3] == (byte)'^' && new AprsBeamHeading((char)text[^2], (char)text[^1]).IsValid)
        {
            throw new AprsEncodeException(AprsRefusalCode.TextReadsAsField, "status text ends with what reads as ^HP beam heading/power; set BeamHeading instead");
        }
    }
}

/// <summary>Maidenhead locator syntax: field A-R, square 0-9, optional subsquare A-X (either case).</summary>
internal static class Maidenhead
{
    public static bool IsLocator(ReadOnlySpan<byte> s)
    {
        if (s.Length is not (4 or 6))
        {
            return false;
        }

        bool field = Upper(s[0]) is >= (byte)'A' and <= (byte)'R' && Upper(s[1]) is >= (byte)'A' and <= (byte)'R';
        bool square = Text.IsDigit(s[2]) && Text.IsDigit(s[3]);
        bool sub = s.Length == 4 || (Upper(s[4]) is >= (byte)'A' and <= (byte)'X' && Upper(s[5]) is >= (byte)'A' and <= (byte)'X');
        return field && square && sub;
    }

    private static byte Upper(byte b) => Text.IsLower(b) ? (byte)(b - 32) : b;
}

/// <summary>Telemetry reports, data type <c>T</c> (APRS12c §13).</summary>
internal static class TelemetryCodec
{
    public static AprsData? Decode(ReadOnlySpan<byte> info, DecodeContext ctx)
    {
        if (info.Length < 3 || info[1] != (byte)'#')
        {
            ctx.Error(AprsDiagnosticCode.InvalidTelemetry, "telemetry report must start T# (APRS12c ch. 13)", 1);
            return null;
        }

        int pos = 2;
        string sequence;
        if (info[2..].StartsWith("MIC"u8))
        {
            sequence = "MIC";
            pos = 5;
            if (pos < info.Length && info[pos] == (byte)',')
            {
                pos++;
            }
        }
        else
        {
            int comma = info[2..].IndexOf((byte)',');
            if (comma < 1)
            {
                ctx.Error(AprsDiagnosticCode.InvalidTelemetry, "telemetry sequence number is not followed by ','", 2);
                return null;
            }

            sequence = Text.Latin1(info.Slice(2, comma));
            if (!sequence.All(char.IsAsciiLetterOrDigit))
            {
                ctx.Error(AprsDiagnosticCode.InvalidTelemetry, "telemetry sequence number must be letters or digits", 2);
                return null;
            }

            pos = 2 + comma + 1;
        }

        var analog = new List<double?>(5);
        var sent = new List<string>(5);
        while (analog.Count < 5)
        {
            int end = pos;
            while (end < info.Length && info[end] != (byte)',')
            {
                end++;
            }

            ReadOnlySpan<byte> field = info[pos..end];
            sent.Add(Text.Latin1(field));
            if (field.Length == 0)
            {
                analog.Add(null);
            }
            else if (Text.IsTelemetryNumber(Text.Latin1(field))
                && double.TryParse(Text.Latin1(field), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double v))
            {
                analog.Add(v);
            }
            else
            {
                ctx.Error(AprsDiagnosticCode.InvalidTelemetry, $"telemetry value '{Text.Latin1(field)}' is not a number", pos);
                return null;
            }

            pos = end;
            if (pos >= info.Length)
            {
                break;
            }

            pos++; // the comma
        }

        string? digital = null;
        if (analog.Count == 5 && pos < info.Length)
        {
            if (info.Length >= pos + 8 && info.Slice(pos, 8).IndexOfAnyExcept((byte)'0', (byte)'1') < 0)
            {
                digital = Text.Latin1(info.Slice(pos, 8));
                pos += 8;
            }
        }

        if (analog.Count < 5 || digital is null)
        {
            if (!ctx.Tolerate(
                    ctx.Options.AllowInvalidTelemetry,
                    AprsDiagnosticCode.InvalidTelemetry,
                    "telemetry report should carry 5 analog values and 8 digital bits (APRS12c ch. 13)",
                    2))
            {
                return null;
            }
        }

        if (!Text.TryDecode(info[pos..], ctx, pos, out string comment))
        {
            return null;
        }

        // Each value keeps its text as sent (073, 190.0, .53), so it re-encodes identical (vectors
        // README, "Numbers as sent").
        return new AprsTelemetryReport { Sequence = sequence, Analog = analog, AnalogText = sent, Bits = digital, Comment = comment };
    }

    /// <summary>Whether <paramref name="sent"/> is a telemetry number that reads as <paramref name="value"/>.</summary>
    private static bool TextMeans(string sent, double value) =>
        Text.IsTelemetryNumber(sent)
        && double.TryParse(sent, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double parsed)
        && parsed == value;

    public static void Write(InfoWriter w, AprsTelemetryReport r)
    {
        if (!r.Sequence.All(char.IsAsciiLetterOrDigit))
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidCharacter, "telemetry sequence is letters or digits");
        }

        if (r.Sequence.Length == 0)
        {
            throw new AprsEncodeException(AprsRefusalCode.TooLong, "telemetry sequence is letters or digits");
        }

        if (r.Analog.Count != 5 || r.Bits is not { Length: 8 } bits || bits.Any(c => c is not ('0' or '1')))
        {
            throw new AprsEncodeException(AprsRefusalCode.Malformed, "a telemetry report carries 5 analog values and the 8 digital bits (APRS12c ch. 13)");
        }

        Text.RequireNoLineBreaks(r.Comment);
        if (r.Sequence == "MIC" && r.Analog[0] is null)
        {
            // No form reads back with the first channel empty after MIC: T#MIC,,033 is read
            // otherwise (vectors ruling F12).
            throw new AprsEncodeException(AprsRefusalCode.ValueHasNoForm, "a MIC telemetry report with an empty first channel has no form that reads back as it (APRS12c ch. 13)");
        }

        w.Ascii("T#");
        w.Ascii(r.Sequence);
        for (int i = 0; i < r.Analog.Count; i++)
        {
            double? v = r.Analog[i];

            // "MIC may or may not be followed by a comma" (APRS12c §13); the spec's example has none,
            // but an empty first value needs it, or the second would be read in its place.
            if (i > 0 || r.Sequence != "MIC" || v is null)
            {
                w.Char(',');
            }

            if (v is not { } value)
            {
                continue;
            }

            if (i < r.AnalogText.Count && TextMeans(r.AnalogText[i], value))
            {
                // A decoded value is written as sent: a number is free-form (vectors ruling E1).
                w.Ascii(r.AnalogText[i]);
            }
            else
            {
                // Built in code: a whole number 0-999 in the spec's three digits (APRS12c ch. 13,
                // whose aaa field and examples write it so), however it is scaled (190.0 is 190),
                // and any other number the plain shortest decimal (vectors ruling F11).
                bool canonical = value == Math.Truncate(value) && value is >= 0 and <= 999;
                w.Ascii(canonical ? ((int)value).ToString("000", CultureInfo.InvariantCulture) : PlainNumber.Format(value));
            }
        }

        w.Char(',');
        w.Ascii(bits);

        w.Utf8(r.Comment);
    }
}

/// <summary>Positionless weather reports, data type <c>_</c> (APRS12c §12).</summary>
internal static class WeatherReportCodec
{

    public static AprsData? Decode(ReadOnlySpan<byte> info, DecodeContext ctx)
    {
        ctx.Info(AprsDiagnosticCode.ObsoleteFormat, "positionless weather reports are not recommended; senders should use a position report with the weather symbol (APRS12c ch. 12)", 0);
        if (!TimestampCodec.TryReadMonthDay(info, 1, ctx, out AprsTimestamp timestamp))
        {
            return null;
        }

        int pos = 9;
        var weather = new AprsWeather();
        if (!WeatherCodec.TryReadFields(info, ref pos, ctx, ref weather, positionless: true))
        {
            return null;
        }

        ReadOnlySpan<byte> tail = info[pos..];
        string comment = "";
        if (CommentCodec.IsSoftwareAndUnit(tail))
        {
            weather = weather with { SoftwareType = (char)tail[0], UnitType = Text.Latin1(tail[1..]) };
        }
        else if (tail.Length > 0)
        {
            if (!ctx.Tolerate(ctx.Options.AllowWeatherComment, AprsDiagnosticCode.WeatherComment, "text after the weather data kept as a comment (APRS12c ch. 12)", pos)
                || !Text.TryDecode(tail, ctx, pos, out comment))
            {
                return null;
            }
        }

        return new AprsWeatherReport { Timestamp = timestamp, Weather = weather, Comment = comment };
    }
}
