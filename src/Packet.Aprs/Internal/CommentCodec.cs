using System.Globalization;

namespace Packet.Aprs.Internal;

/// <summary>
/// Structured elements carried in the comment of a positioned report: altitude, <c>!DAO!</c>,
/// base-91 telemetry, voice frequency, signpost / corridor braces, and (when enabled) a data
/// extension found later in the comment.
/// </summary>
internal static class CommentCodec
{
    /// <summary>Decodes a position, object or item comment starting at <paramref name="pos"/>.</summary>
    public static bool TryRead(ReadOnlySpan<byte> info, int pos, DecodeContext ctx, PositionedFields f)
    {
        var comment = new CommentText(info[pos..].ToArray());
        return TryExtract(comment, pos, ctx, f, frequencyAtStart: true) && TryFinish(comment, pos, ctx, f);
    }

    /// <summary>Whatever follows the weather fields: ideally only the software and unit type.</summary>
    public static bool TryReadWeatherTail(ReadOnlySpan<byte> info, int pos, DecodeContext ctx, PositionedFields f)
    {
        ReadOnlySpan<byte> tail = info[pos..];
        if (tail.Length == 0)
        {
            return true;
        }

        // Base-91 telemetry and a !DAO! are lifted out first; what is left may be the software
        // type and unit (vectors README, Weather).
        var comment = new CommentText(tail.ToArray());
        if (!TryExtractTrailer(comment, pos, ctx, f))
        {
            return false;
        }

        if (comment.Count == 0)
        {
            return true;
        }

        // A software type and unit stand whole as sent: one split by a lifted element is text.
        byte[] rest = comment.ToArray();
        if (comment.Whole(0, comment.Count) && IsSoftwareAndUnit(rest))
        {
            f.Weather = f.Weather! with { SoftwareType = (char)rest[0], UnitType = Text.Latin1(rest.AsSpan(1)) };
            return true;
        }

        if (!ctx.Tolerate(
                ctx.Options.AllowWeatherComment,
                AprsDiagnosticCode.WeatherComment,
                "a weather report has no comment field; text after the weather data kept as a comment (APRS12c ch. 12, UAP 2.7.1)",
                pos))
        {
            return false;
        }

        return TryFinish(comment, pos, ctx, f);
    }

    /// <summary>
    /// One software-type letter then a 2-4 character unit type, e.g. <c>wRSW</c>, <c>xDvs</c>, <c>tU2k</c>.
    /// A letter followed only by digits is a malformed weather field (e.g. a 4-digit <c>b0990</c>), not this,
    /// and text that starts like a field is text: a letter APRS12c defines followed by a digit, or <c>t</c>
    /// followed by <c>-</c> and a digit (<c>t2h35</c>, <c>s12a</c>, <c>t-5ab</c>; vectors interpretations.md,
    /// "Text after the weather fields that starts like a field").
    /// </summary>
    internal static bool IsSoftwareAndUnit(ReadOnlySpan<byte> tail)
    {
        if (tail.Length is < 3 or > 5 || !(Text.IsUpper(tail[0]) || Text.IsLower(tail[0])) || Text.AllDigits(tail[1..])
            || StartsLikeAField(tail))
        {
            return false;
        }

        foreach (byte b in tail)
        {
            if (!Text.IsAlphanumeric(b) && b is not ((byte)'-' or (byte)'_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A letter APRS12c defines as a weather field followed by a digit, or <c>t</c> followed by <c>-</c> and a digit.</summary>
    private static bool StartsLikeAField(ReadOnlySpan<byte> tail) =>
        (tail[0] == (byte)'t' && tail[1] == (byte)'-' && Text.IsDigit(tail[2]))
        || ("csgtrpPhbLl"u8.Contains(tail[0]) && Text.IsDigit(tail[1]));

    /// <summary>
    /// Lifts structured elements out of the comment bytes, in the order that avoids one being
    /// mistaken for another: DAO and telemetry from the end, then altitude, braces, a late data
    /// extension, and finally a voice frequency at the start.
    /// </summary>
    internal static bool TryExtract(CommentText c, int offset, DecodeContext ctx, PositionedFields f, bool frequencyAtStart)
    {
        return TryExtractTrailer(c, offset, ctx, f) && ExtractRest(c, offset, ctx, f, frequencyAtStart);
    }

    /// <summary>What <see cref="TryExtract"/> lifts once the trailer is out: altitude, braces, a late data extension and a voice frequency.</summary>
    internal static bool ExtractRest(CommentText c, int offset, DecodeContext ctx, PositionedFields f, bool frequencyAtStart)
    {
        ExtractAltitude(c, f);
        ExtractBraces(c, f);
        if (f.Phg is null && f.RangeMiles is null && f.DfSignalStrength is null)
        {
            ExtractLateExtension(c, offset, ctx, f);
        }

        if (frequencyAtStart)
        {
            ExtractFrequency(c, f);
        }

        return true;
    }

    /// <summary>DAO then base-91 telemetry, both expected at the end of the comment (APRS12c §13).</summary>
    internal static bool TryExtractTrailer(CommentText c, int offset, DecodeContext ctx, PositionedFields f)
    {
        // Telemetry is located first so that a DAO-shaped run of base-91 characters inside it
        // (e.g. "|!m2n!Q[P|") is not mistaken for a !DAO!; the spec puts the DAO after it.
        (int tStart, int tEnd) = FindTelemetry(c);
        int daoAt = ExtractDao(c, offset, ctx, f, tStart, tEnd, out bool ok);
        if (!ok)
        {
            return false;
        }

        if (daoAt >= 0 && daoAt < tStart)
        {
            tStart -= 5;
            tEnd -= 5;
        }

        if (tStart >= 0)
        {
            ExtractTelemetry(c, f, tStart, tEnd);
        }

        return true;
    }

    internal static bool TryFinish(CommentText c, int offset, DecodeContext ctx, PositionedFields f)
    {
        // "Any free text field that begins with [a space or a slash] ... they can be ignored and the
        // beginning of the text field begins after them" (APRS12c §18): once the structured elements
        // are lifted out, one leading delimiter is not part of the comment.
        if (c.Count > 0 && c[0] is (byte)' ' or (byte)'/')
        {
            c.Remove(0, 1);
        }

        if (!Text.TryDecode(c.ToArray(), ctx, offset, out string text))
        {
            return false;
        }

        f.Comment = text;
        return true;
    }

    /// <summary>The byte ranges a decoder lifts from <paramref name="c"/> as base-91 telemetry and the <c>!DAO!</c>, in its own positions.</summary>
    internal static List<(int Start, int Length)> TrailerSpans(CommentText c)
    {
        (int tStart, int tEnd) = FindTelemetry(c);
        var spans = new List<(int, int)>();
        int dao = ExtractDao(new CommentText(c), 0, new DecodeContext(AprsParseOptions.Lenient), new PositionedFields(), tStart, tEnd, out _);
        if (dao >= 0)
        {
            spans.Add((dao, 5));
        }

        if (tStart >= 0)
        {
            spans.Add((tStart, tEnd - tStart + 1));
        }

        return spans;
    }

    private static int ExtractDao(CommentText c, int offset, DecodeContext ctx, PositionedFields f, int skipFrom, int skipTo, out bool ok)
    {
        ok = true;
        Span<byte> window = stackalloc byte[5];
        for (int i = c.Count - 5; i >= 0; i--)
        {
            if (c[i] != (byte)'!' || c[i + 4] != (byte)'!' || (skipFrom >= 0 && i + 4 >= skipFrom && i <= skipTo) || !c.Whole(i, 5))
            {
                continue;
            }

            for (int k = 0; k < 5; k++)
            {
                window[k] = c[i + k];
            }

            if (!DaoCodec.TryParse(window, out AprsDao dao, out double lat, out double lon))
            {
                continue;
            }

            f.Dao = dao;
            if (f.Position.Ambiguity != 0 && !f.IsCompressed)
            {
                // Whatever its precision, !W  ! included (vectors interpretations.md, "!DAO! on an
                // ambiguous position").
                if (!ctx.Tolerate(
                        ctx.Options.AllowDaoWithAmbiguity,
                        AprsDiagnosticCode.DaoWithAmbiguity,
                        "!DAO! on an ambiguous position; any extra precision is ignored",
                        offset + i))
                {
                    // Lifted all the same, so a caller judging what is left sees the real text.
                    ok = false;
                    c.Lift(i, 5, Lifted.Other);
                    return i;
                }
            }
            else if (dao.Precision != AprsDaoPrecision.None && !f.IsCompressed)
            {
                f.Position = DaoCodec.Apply(f.Position, lat, lon);
            }

            c.Lift(i, 5, Lifted.Other);
            return i;
        }

        return -1;
    }

    /// <summary>
    /// Where the telemetry is: the last pair of neighbouring bars, as sent, that encloses a valid
    /// <c>|ss11..|</c> block (4 to 14 base-91 characters, an even number, APRS12c §13), or (-1, -1).
    /// Bars around anything else are text (vectors interpretations.md, "Base-91 telemetry is the
    /// last valid block"). One pass from the end, each pair judged on at most 14 bytes, so the time
    /// is linear in the comment however many bars it holds.
    /// </summary>
    private static (int Start, int End) FindTelemetry(CommentText c)
    {
        int end = LastIndexOf(c, (byte)'|', c.Count - 1);
        while (end >= 0)
        {
            int start = LastIndexOf(c, (byte)'|', end - 1);
            if (start < 0)
            {
                break;
            }

            int length = end - start - 1;
            if (length is >= 4 and <= 14 && length % 2 == 0 && c.Whole(start, length + 2) && AllBase91(c, start + 1, end))
            {
                return (start, end);
            }

            end = start;
        }

        return (-1, -1);
    }

    private static bool AllBase91(CommentText c, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (!Base91.IsDigit(c[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static int LastIndexOf(CommentText c, byte value, int from)
    {
        for (int i = from; i >= 0; i--)
        {
            if (c[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    private static void ExtractTelemetry(CommentText c, PositionedFields f, int start, int end)
    {
        var values = new List<int>();
        for (int i = start + 1; i < end; i += 2)
        {
            values.Add(((c[i] - 33) * 91) + (c[i + 1] - 33));
        }

        f.Telemetry = new AprsCommentTelemetry
        {
            Sequence = values[0],
            Analog = values.Skip(1).Take(5).ToArray(),
            Digital = values.Count == 7 ? (byte)(values[6] & 0xFF) : null,
        };
        c.Lift(start, end - start + 1, Lifted.Other);
    }

    /// <summary>The bytes of a <c>/A=nnnnnn</c> (or <c>/A=-nnnnn</c>) altitude.</summary>
    internal const int AltitudeLength = 9;

    /// <summary>
    /// True when a whole <c>/A=</c> altitude stands at <paramref name="i"/>: <c>/A=</c> and six
    /// digits, or <c>-</c> and five, with nothing lifted from between them. The value is its feet.
    /// </summary>
    internal static bool IsSlashAltitudeAt(CommentText c, int i, out int feet)
    {
        feet = 0;
        if (i < 0 || i + AltitudeLength > c.Count || c[i] != (byte)'/' || c[i + 1] != (byte)'A' || c[i + 2] != (byte)'=' || !c.Whole(i, AltitudeLength))
        {
            return false;
        }

        bool negative = c[i + 3] == (byte)'-';
        int value = 0;
        for (int k = negative ? i + 4 : i + 3; k < i + AltitudeLength; k++)
        {
            if (!Text.IsDigit(c[k]))
            {
                return false;
            }

            value = (value * 10) + (c[k] - '0');
        }

        feet = negative ? -value : value;
        return true;
    }

    /// <summary><c>/A=nnnnnn</c> anywhere in the comment (APRS12c §6), or the de facto <c>/A=-nnnnn</c>.</summary>
    private static void ExtractAltitude(CommentText c, PositionedFields f)
    {
        for (int i = 0; i + AltitudeLength <= c.Count; i++)
        {
            if (IsSlashAltitudeAt(c, i, out int feet))
            {
                f.AltitudeFeet = feet;
                f.AltitudeFromComment = true;
                c.Lift(i, AltitudeLength, Lifted.Altitude);
                return;
            }
        }
    }

    /// <summary>Signpost text on <c>\m</c>, or a line corridor width on <c>\l</c> lines (APRS12c §11).</summary>
    private static void ExtractBraces(CommentText c, PositionedFields f)
    {
        bool signpost = f.Symbol is { Table: '\\', Code: 'm' };
        bool corridor = f.AreaObject is { Shape: AprsAreaShape.LineDownRight or AprsAreaShape.LineDownLeft };
        if (!signpost && !corridor)
        {
            return;
        }

        // The overlay is "specified by enclosing the 1-3 characters in braces" (APRS12c ch. 11): the
        // first well-formed braces wherever they are, a {, 1-3 characters that are not braces and
        // a }, holding printable ASCII for a signpost or digits for a corridor. Braces that do not
        // qualify are comment text and do not stop the search, as a malformed data extension does
        // not (vectors README, Comments).
        for (int open = 0; open < c.Count; open++)
        {
            if (c[open] != (byte)'{')
            {
                continue;
            }

            int close = open + 1;
            while (close < c.Count && c[close] is not ((byte)'{' or (byte)'}'))
            {
                close++;
            }

            if (close >= c.Count || c[close] != (byte)'}' || close - open - 1 is < 1 or > 3 || !c.Whole(open, close - open + 1))
            {
                continue;
            }

            byte[] inner = c.GetRange(open + 1, close - open - 1);
            if (signpost && inner.All(Text.IsPrintableAscii))
            {
                f.SignpostText = Text.Latin1(inner);
            }
            else if (corridor && Text.AllDigits(inner))
            {
                f.AreaObject = f.AreaObject!.Value with { CorridorWidthMiles = Text.ParseDigits(inner) };
            }
            else
            {
                continue;
            }

            c.Lift(open, close - open + 1, Lifted.Other);
            return;
        }
    }

    /// <summary>
    /// PHG / RNG / DFS that appear after other comment text instead of straight after the symbol.
    /// Strictly that is free text (UAP §5.15); recognising it is a named tolerance.
    /// </summary>
    private static void ExtractLateExtension(CommentText c, int offset, DecodeContext ctx, PositionedFields f)
    {
        if (!ctx.Options.AllowDataExtensionInComment)
        {
            return;
        }

        byte[] bytes = c.ToArray();
        ReadOnlySpan<byte> s = bytes;
        foreach (string tagText in (string[])["PHG", "RNG", "DFS"])
        {
            // The first well-formed one: a malformed PHG12 is comment text and does not stop the
            // search (vectors interpretations.md, "A late data extension is the first well-formed one").
            ReadOnlySpan<byte> tag = System.Text.Encoding.ASCII.GetBytes(tagText);
            int at = -1;
            for (int from = 0; at < 0 && from + 7 <= s.Length;)
            {
                int found = s[from..].IndexOf(tag);
                if (found < 0 || from + found + 7 > s.Length)
                {
                    break;
                }

                ReadOnlySpan<byte> candidate = s.Slice(from + found, 7);
                bool wellFormed = tag[0] == (byte)'R'
                    ? Text.AllDigits(candidate[3..])
                    : Text.IsDigit(candidate[3]) && Text.IsDigit(candidate[5]) && Text.IsDigit(candidate[6]) && candidate[4] is >= (byte)'0' and <= (byte)'~';
                at = wellFormed && c.Whole(from + found, 7) ? from + found : -1;
                from += found + 1;
            }

            if (at < 0)
            {
                continue;
            }

            ReadOnlySpan<byte> ext = s.Slice(at, 7);
            if (tag[0] == (byte)'R')
            {
                f.RangeMiles = Text.ParseDigits(ext[3..]);
            }
            else if (tag[0] == (byte)'D')
            {
                f.DfSignalStrength = new AprsDfSignalStrength(ext[3] - '0', ext[4] - '0', ext[5] - '0', ext[6] - '0');
            }

            int length = 7;
            if (tag[0] == (byte)'P')
            {
                // PHGR: a beacon-rate character and '/' after the digits, as at the start of a comment.
                int? rate = s.Length >= at + 9 && s[at + 8] == (byte)'/' && c.Whole(at, 9) ? PositionCodec.BeaconRate(s[at + 7]) : null;
                f.Phg = new AprsPhg(ext[3] - '0', ext[4] - '0', ext[5] - '0', ext[6] - '0', rate);
                length = rate is null ? 7 : 9;
            }

            ctx.Warn(
                AprsDiagnosticCode.DataExtensionInComment,
                $"{Text.Latin1(tag)} found later in the comment; the spec puts data extensions straight after the symbol (UAP 5.15)",
                offset + at);
            c.Lift(at, length, Lifted.Other);
            return;
        }
    }

    /// <summary>
    /// A voice frequency at the start of the comment (APRS12c §18): <c>FFF.FFFMHz</c> or
    /// <c>FFF.FF MHz</c>, then optional space-separated tone, offset and range fields.
    /// A single leading space or slash delimiter is allowed before it.
    /// </summary>
    private static void ExtractFrequency(CommentText c, PositionedFields f)
    {
        byte[] bytes = c.ToArray();
        ReadOnlySpan<byte> s = bytes;
        int start = s.Length > 0 && s[0] is (byte)' ' or (byte)'/' ? 1 : 0;
        if (!FrequencyCodec.TryRead(s[start..], c.CutsFrom(start), out AprsVoiceFrequency? frequency, out int consumed))
        {
            return;
        }

        f.Frequency = frequency;
        int remove = start + consumed;
        if (remove < c.Count && c[remove] == (byte)' ')
        {
            remove++;
        }

        c.Remove(0, remove);
    }

    // ---------------------------------------------------------------- encoding

    /// <summary>
    /// Writes the comment of a position, object or item report after its data extension, in the
    /// one order the vectors' Encoding rule names (interpretations.md, "Where a voice frequency
    /// goes"): the voice frequency and its fields, the signpost or corridor braces, the <c>/A=</c>
    /// altitude, the free text (after a space when a frequency comes before it), base-91 telemetry
    /// and the <c>!DAO!</c>. The frequency is in the first 10 bytes, where radios read it
    /// (APRS12c ch. 18), and the braces come before any the text holds, so they are the first
    /// well-formed ones a decoder finds.
    /// </summary>
    public static void Write(InfoWriter w, AprsPositionedData d, bool altitudeInCs)
    {
        bool altitude = d.AltitudeFeet is not null && !altitudeInCs;
        if (altitude)
        {
            // An altitude written as /A= must fit it: a fault of its own, before the comment's text.
            w.Field(() => CheckAltitude(d.AltitudeFeet!.Value));
        }

        w.Field(() => CheckComment(d.Comment));
        w.Field(() => WriteFreeText(w, d, altitude));
        WriteTrailer(w, d, includeWeatherSoftware: false);
    }

    private static void WriteFreeText(InfoWriter w, AprsPositionedData d, bool altitude)
    {
        bool braces = d.SignpostText is not null || d.AreaObject is { CorridorWidthMiles: not null };
        if (d.Frequency is { } freq)
        {
            // Straight after a 7-byte extension the frequency follows a '/', the first form the
            // spec lists ("$PHGphgd/FFF.FFFMHz", APRS12c §18); a PHGR extension already ends in '/'.
            // A DF bearing or storm data extends the course/speed, so one follows a '/' too
            // (vectors ruling F9).
            bool afterExtension = !d.IsCompressed && d.Weather is null
                && (d.CourseDegrees is not null || d.SpeedKnots is not null || d.RangeMiles is not null
                    || d.DfSignalStrength is not null || d.AreaObject is not null || d.Phg is { BeaconsPerHour: null }
                    || d.DfBearing is not null || d.Storm is not null);
            if (afterExtension)
            {
                w.Char('/');
            }

            FrequencyCodec.WriteWithComment(w, freq, d.Comment, w => WriteBracesAndAltitude(w, d, altitude), slashDelimiter: w.Delimiter == CommentDelimiter.Slash);
        }
        else
        {
            WriteBracesAndAltitude(w, d, altitude);

            // A delimiter only where the text would not read back as itself without one: it starts
            // with a space or '/', or straight after the symbol or extension it would read as an
            // extension (interpretations.md, "Delimiters at the start of free text").
            bool extensionJustWritten = !braces && !altitude && !d.IsCompressed
                && (d.CourseDegrees is not null || d.SpeedKnots is not null || d.RangeMiles is not null || d.DfSignalStrength is not null
                    || d.AreaObject is not null || d.DfBearing is not null || d.Storm is not null || d.Phg is not null);
            bool nothingBefore = !braces && !altitude && !extensionJustWritten;
            bool needsSlash = NeedsDelimiter(d.Comment, extensionJustWritten ? d.Phg : null) || (nothingBefore && !d.IsCompressed && LooksLikeExtension(d.Comment, d));
            if (w.Delimiter != CommentDelimiter.Auto && d.Comment.Length > 0)
            {
                // The rules' choice did not read back: the next form the Encoding rule names, a '/' then a space.
                w.Char(w.Delimiter == CommentDelimiter.Slash ? '/' : ' ');
            }
            else if (needsSlash)
            {
                w.Char('/');
            }

            w.Utf8(d.Comment);
        }
    }

    private static void WriteBracesAndAltitude(InfoWriter w, AprsPositionedData d, bool altitude)
    {
        WriteBraces(w, d);
        if (altitude)
        {
            WriteAltitude(w, d.AltitudeFeet!.Value);
        }
    }

    /// <summary>The end of a report: weather software/unit (weather reports), telemetry, DAO.</summary>
    public static void WriteTrailer(InfoWriter w, AprsPositionedData d) => WriteTrailer(w, d, includeWeatherSoftware: true);

    private static void WriteTrailer(InfoWriter w, AprsPositionedData d, bool includeWeatherSoftware)
    {
        if (includeWeatherSoftware && (d.Telemetry is not null || d.Frequency is not null || d.SignpostText is not null
            || (d.AltitudeFeet is not null && !(d.IsCompressed && d.CompressionType?.Source == AprsCompressionSource.Gga))))
        {
            // A compressed weather report's GGA altitude goes in the cs bytes as the nearest step,
            // with no /A= beside it (vectors ruling F14).
            w.Defer(AprsRefusalCode.FieldNotAllowed, "a weather report has no comment, so it cannot carry telemetry, frequency, signpost or /A= altitude (APRS12c ch. 12)");
        }

        if (d.Telemetry is { } t && !includeWeatherSoftware)
        {
            // A weather report has no place for telemetry, so its own checks do not run.
            w.Field(() => WriteTelemetry(w, t));
        }

        if (d.Dao is { } dao)
        {
            // On a compressed position the digits are not applied, so they are written to agree
            // with the position the compressed bytes report (vectors ruling E2).
            w.Field(() => DaoCodec.Write(w, d.IsCompressed ? PositionCodec.CompressedReported(d.Position) : d.Position, dao));
        }
    }

    internal static void WriteTelemetry(InfoWriter w, AprsCommentTelemetry t)
    {
        t.Validate(w);
        w.Char('|').Base91(t.Sequence, 2);
        foreach (int v in t.Analog)
        {
            w.Base91(v, 2);
        }

        if (t.Digital is { } bits)
        {
            w.Base91(bits, 2);
        }

        w.Char('|');
    }

    /// <summary>
    /// True when comment text would be read back differently without a delimiter in front: a leading
    /// space or slash (the decoder drops one as a delimiter), or, straight after plain PHG, a
    /// character and slash that would read as the PHGR beacon rate.
    /// </summary>
    internal static bool NeedsDelimiter(string comment, AprsPhg? phg) =>
        comment.Length > 0 && (comment[0] is ' ' or '/'
            || (phg is { BeaconsPerHour: null } && comment.Length >= 2 && comment[1] == '/' && comment[0] is (>= '1' and <= '9') or (>= 'A' and <= 'Z')));

    /// <summary>Rounded to whole feet, halves away from zero, then checked to fit /A= (vectors ruling F2).</summary>
    internal static double CheckAltitude(double feet)
    {
        NotANumber.Refuse(feet, "altitude");
        double rounded = Steps.Whole(feet);
        return rounded is < -99999 or > 999999
            ? throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "/A= altitude must fit in 6 digits (or - and 5 digits)")
            : rounded;
    }

    internal static void WriteAltitude(InfoWriter w, double feet)
    {
        int alt = (int)CheckAltitude(feet);
        w.Ascii("/A=");
        _ = alt < 0 ? w.Char('-').Digits(-alt, 5) : w.Digits(alt, 6);
    }

    private static void WriteBraces(InfoWriter w, AprsPositionedData d)
    {
        if (d.SignpostText is { } sign)
        {
            if (d.Symbol is not { Table: '\\', Code: 'm' })
            {
                w.Defer(AprsRefusalCode.SymbolMismatch, "signpost text is 1-3 printable characters on the \\m symbol (APRS12c ch. 11)");
            }

            if (sign.Length is < 1 or > 3)
            {
                throw new AprsEncodeException(AprsRefusalCode.Malformed, "signpost text is 1-3 printable characters on the \\m symbol (APRS12c ch. 11)");
            }

            if (sign.Any(ch => ch is < ' ' or > '~' or '{' or '}'))
            {
                throw new AprsEncodeException(AprsRefusalCode.InvalidCharacter, "signpost text is 1-3 printable characters on the \\m symbol (APRS12c ch. 11)");
            }

            w.Char('{').Ascii(sign).Char('}');
        }

        if (d.AreaObject is { CorridorWidthMiles: { } corridor } area)
        {
            if (area.Shape is not (AprsAreaShape.LineDownRight or AprsAreaShape.LineDownLeft))
            {
                w.Defer(AprsRefusalCode.FieldNotAllowed, "a corridor width only applies to line area objects (APRS12c ch. 11)");
            }

            w.Char('{').Ascii(corridor.ToString(CultureInfo.InvariantCulture)).Char('}');
        }
    }

    /// <summary>
    /// A comment holds no control character. Whether its text would read back as something else (a
    /// structured element, a frequency field, a data extension) is not judged here: the read-back
    /// of the finished field decides (<see cref="ReadBack"/>), and tries the next delimiter form.
    /// </summary>
    internal static void CheckComment(string? comment)
    {
        ArgumentNullException.ThrowIfNull(comment);
        Text.RequireNoControlCharacters(comment);
    }

    /// <summary>True if text written straight after the symbol (or after course/speed) would be read back as an extension.</summary>
    private static bool LooksLikeExtension(string comment, AprsPositionedData d)
    {
        byte[] c = System.Text.Encoding.UTF8.GetBytes(comment);
        bool hasCourseSpeed = d.CourseDegrees is not null || d.SpeedKnots is not null || d.DfBearing is not null || d.Storm is not null;
        bool hasExtension = hasCourseSpeed || d.Phg is not null || d.RangeMiles is not null || d.DfSignalStrength is not null || d.AreaObject is not null;
        if (d.AltitudeFeet is not null || d.Frequency is not null)
        {
            return false;
        }

        if (!hasExtension)
        {
            var probe = new PositionedFields { Position = d.Position, Symbol = d.Symbol };
            int pos = 0;
            PositionCodec.TryReadDataExtension(c, ref pos, new DecodeContext(AprsParseOptions.Lenient), probe);
            return pos > 0;
        }

        if (hasCourseSpeed && d.DfBearing is null && d.Storm is null)
        {
            int pos = 0;
            bool bearing = d.Symbol is { Table: '/', Code: '\\' } && c.Length >= 8 && c[0] == (byte)'/' && c[4] == (byte)'/'
                && Text.AllDigits(c.AsSpan(1, 3)) && Text.AllDigits(c.AsSpan(5, 3));
            return bearing || (d.Symbol.Code == '@' && StormCodec.TryRead(c, ref pos, out _));
        }

        return false;
    }
}
