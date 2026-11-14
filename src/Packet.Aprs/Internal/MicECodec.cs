namespace Packet.Aprs.Internal;

/// <summary>Mic-E (APRS12c §10).</summary>
internal static class MicECodec
{
    private static readonly AprsMicEMessage[] Standard =
    [
        AprsMicEMessage.Emergency, AprsMicEMessage.Priority, AprsMicEMessage.Special, AprsMicEMessage.Committed,
        AprsMicEMessage.Returning, AprsMicEMessage.InService, AprsMicEMessage.EnRoute, AprsMicEMessage.OffDuty,
    ];

    private static readonly AprsMicEMessage[] Custom =
    [
        AprsMicEMessage.Emergency, AprsMicEMessage.Custom6, AprsMicEMessage.Custom5, AprsMicEMessage.Custom4,
        AprsMicEMessage.Custom3, AprsMicEMessage.Custom2, AprsMicEMessage.Custom1, AprsMicEMessage.Custom0,
    ];

    // ------------------------------------------------------------------ decoding

    public static AprsData? Decode(ReadOnlySpan<byte> info, AprsAddress destination, DecodeContext ctx)
    {
        // The info describes the data type identifier, the first byte read, so it is given whatever follows.
        byte dti = info[0];
        if (dti is 0x1c or 0x1d)
        {
            ctx.Info(AprsDiagnosticCode.ObsoleteFormat, "Mic-E data type 0x1C/0x1D is from Rev 0 beta units and obsolete (APRS12c ch. 10)", 0);
        }

        if (!TryDecodeDestination(destination, ctx, out Destination dest))
        {
            return null;
        }

        if (info.Length < 9)
        {
            ctx.Error(AprsDiagnosticCode.InvalidMicEInformation, "Mic-E information field is shorter than 9 bytes (APRS12c ch. 10)", 0);
            return null;
        }

        int d = info[1] - 28;
        int m = info[2] - 28;
        int h = info[3] - 28;
        if (info[1] is < 38 or > 127 || info[2] is < 38 or > 97 || info[3] is < 28 or > 127)
        {
            ctx.Error(AprsDiagnosticCode.InvalidMicEInformation, "Mic-E longitude bytes are out of range (APRS12c ch. 10)", 1);
            return null;
        }

        if (dest.LongitudeOffset)
        {
            d += 100;
        }

        if (d is >= 180 and <= 189)
        {
            d -= 80;
        }
        else if (d is >= 190 and <= 199)
        {
            d -= 190;
        }

        if (m >= 60)
        {
            m -= 60;
        }

        if (d > 179 || m > 59 || h > 99)
        {
            ctx.Error(AprsDiagnosticCode.InvalidMicEInformation, "Mic-E longitude decodes out of range", 1);
            return null;
        }

        for (int i = 4; i <= 6; i++)
        {
            if (info[i] is < 28 or > 127)
            {
                ctx.Error(AprsDiagnosticCode.InvalidMicEInformation, "Mic-E speed/course bytes are out of range", i);
                return null;
            }
        }

        int sp = info[4] - 28;
        int dc = info[5] - 28;
        int se = info[6] - 28;
        int speed = (sp * 10) + (dc / 10);
        int course = ((dc % 10) * 100) + se;
        if (speed >= 800)
        {
            speed -= 800;
        }

        if (course >= 400)
        {
            course -= 400;
        }

        if (course > 360)
        {
            if (!ctx.Tolerate(ctx.Options.AllowOutOfRangeValue, AprsDiagnosticCode.OutOfRangeValue, "Mic-E course is over 360 degrees; ignored", 5))
            {
                return null;
            }

            course = 0;
        }

        char code = (char)info[7];
        char table = (char)info[8];
        if (!AprsSymbol.IsValidTable(table))
        {
            ctx.Error(AprsDiagnosticCode.InvalidSymbolTable, $"'{table}' is not a symbol table or overlay", 8);
            return null;
        }

        if (code is < '!' or > '~')
        {
            ctx.Error(AprsDiagnosticCode.InvalidSymbolCode, $"byte 0x{(int)code:X2} is not a symbol code", 7);
            return null;
        }

        // Longitude minutes with the latitude's ambiguity applied, centred like any ambiguous position.
        int[] lonDigits = [m / 10, m % 10, h / 10, h % 10];
        double lonMinutes = Minutes(lonDigits, dest.Ambiguity);
        double longitude = d + (lonMinutes / 60);
        if (dest.West)
        {
            longitude = -longitude;
        }

        var fields = new PositionedFields
        {
            Position = new AprsPosition(dest.Latitude, longitude, dest.Ambiguity),
            Symbol = new AprsSymbol(table, code),
            CourseDegrees = course == 0 ? null : course,
            SpeedKnots = speed,
        };

        var report = new AprsMicEReport
        {
            Position = fields.Position,
            Symbol = fields.Symbol,
            MicEMessage = dest.Message,
            IsOldData = dti is not ((byte)'`' or 0x1c),
            DestinationSsid = destination.NumericSsid ?? 0,
        };

        if (!TryReadStatus(info, ctx, fields, ref report))
        {
            return null;
        }

        return fields.ApplyTo(report with { AltitudeInComment = fields.AltitudeFeet is not null && (fields.AltitudeFromComment || !fields.AltitudeInBytes) });
    }

    private static bool TryReadStatus(ReadOnlySpan<byte> info, DecodeContext ctx, PositionedFields fields, ref AprsMicEReport report)
    {
        var text = new List<byte>(info[9..].ToArray());
        const int offset = 9;
        if (text.Count == 0)
        {
            return true;
        }

        if (text.Contains(0xFF))
        {
            if (!ctx.Tolerate(ctx.Options.AllowKenwoodFfPadding, AprsDiagnosticCode.KenwoodFfPadding, "0xFF padding removed from Mic-E status text (UAP 5.10)", offset))
            {
                return false;
            }

            text.RemoveAll(b => b == 0xFF);
        }

        // Rev 0 binary telemetry, looked for once the 0xFF padding is gone (vectors interpretations.md).
        if (text.Count >= 6 && text[0] == 0x1d)
        {
            ctx.Info(AprsDiagnosticCode.ObsoleteFormat, "obsolete Mic-E binary telemetry (APRS12c ch. 10)", offset);
            report = report with { LegacyTelemetry = text.GetRange(1, 5).Select(b => (int)b).ToArray() };
            text.RemoveRange(0, 6);
        }

        char? typeCode = null;
        if (text.Count > 0 && text[0] is (byte)'`' or (byte)'\'' or (byte)'>' or (byte)']' or (byte)' ')
        {
            typeCode = (char)text[0];
            text.RemoveAt(0);
        }
        else if (text.Count > 0)
        {
            ctx.Info(AprsDiagnosticCode.MicEMissingDeviceType, "Mic-E status text has no device type code (UAP 5.4)", offset);
        }

        string suffix = MatchSuffix(typeCode, text);
        if (suffix.Length > 0)
        {
            text.RemoveRange(text.Count - suffix.Length, suffix.Length);
        }

        report = report with { TypeCode = typeCode, DeviceSuffix = suffix };

        // Altitude: xxx} straight after the type code, metres relative to 10 km below sea level.
        bool altitudeFirst = IsAltitudeAt(text, 0);
        if (altitudeFirst)
        {
            ReadAltitude(text, 0, fields);
        }

        // A Maidenhead locator with the /G grid symbol, then a space before any text (APRS12c §10).
        // It is read only here, at the start or straight after an altitude (vectors ruling F17).
        int locatorLength = LocatorLengthAt(text);
        if (locatorLength > 0)
        {
            report = report with { Locator = Text.Latin1(text.GetRange(0, locatorLength).ToArray()).ToUpperInvariant() };
            text.RemoveRange(0, locatorLength + 2);
            // Base-91 telemetry and a !DAO! count as lifted before the locator is read, so a
            // locator followed straight by either has nothing after it (vectors ruling F16). They
            // are only lifted later, once a later altitude has been looked for (README Mic-E).
            if (text.Count > 0 && !NothingButTrailer(text))
            {
                if (text[0] == (byte)' ')
                {
                    text.RemoveAt(0);
                }
                else if (!ctx.Tolerate(
                             ctx.Options.AllowMissingSpaceAfterLocator,
                             AprsDiagnosticCode.MissingSpaceAfterLocator,
                             "text after a Mic-E grid locator must start with a space (APRS12c ch. 10)",
                             offset))
                {
                    return false;
                }
            }
        }

        // "The Mic-E text field can contain any normal position comment field too, such as PHG" (APRS 1.2).
        if (text.Count >= 7)
        {
            byte[] head = text.GetRange(0, Math.Min(text.Count, 9)).ToArray();
            int pos = 0;
            var probe = new PositionedFields { Position = fields.Position, Symbol = fields.Symbol };
            if (!PositionCodec.IsCourseSpeed(head) && PositionCodec.TryReadDataExtension(head, ref pos, ctx, probe) && pos > 0)
            {
                fields.Phg = probe.Phg;
                fields.RangeMiles = probe.RangeMiles;
                fields.DfSignalStrength = probe.DfSignalStrength;
                fields.AreaObject = probe.AreaObject;
                text.RemoveRange(0, pos);
            }
        }

        // An altitude later in the text (tolerated) is looked for only once what comes first has
        // been lifted: bytes belong to the element that reaches them first, so 0PH} inside
        // PHG3330PH} is not an altitude (vectors interpretations.md, "A packet is read in order").
        // It is looked for before telemetry and a !DAO! are lifted, so bytes either side of a !DAO!
        // never join into one, and a !DAO! sharing a byte with it is not one (vectors README,
        // Mic-E). Strictly it is text and telemetry and the !DAO! are lifted as usual, unless the
        // text left would join bytes either side of them into an altitude: that is rejected.
        if (!altitudeFirst)
        {
            for (int i = 0; i + 4 <= text.Count; i++)
            {
                if (IsAltitudeAt(text, i))
                {
                    if (ctx.Options.AllowMicEAltitudeNotFirst)
                    {
                        ctx.Warn(AprsDiagnosticCode.MicEAltitudeNotFirst, "Mic-E altitude xxx} found after other status text; it should come first (APRS12c ch. 10)", offset + i);
                        ReadAltitude(text, i, fields);
                        fields.DaoBarrier = i;
                    }
                    else if (TrailerLiftJoinsAltitude(text))
                    {
                        ctx.Error(AprsDiagnosticCode.MicEAltitudeNotFirst, "lifting telemetry and the !DAO! would join the bytes either side into a Mic-E altitude xxx} (APRS12c ch. 10)", offset + i);
                        return false;
                    }

                    break;
                }
            }
        }

        return CommentCodec.TryExtract(text, offset, ctx, fields, frequencyAtStart: true) && CommentCodec.TryFinish(text, offset, ctx, fields);
    }

    /// <summary>
    /// True when the text left once base-91 telemetry and the <c>!DAO!</c> are both lifted has an
    /// altitude <c>xxx}</c> made of bytes from either side of a lifted one (vectors README, Mic-E).
    /// </summary>
    private static bool TrailerLiftJoinsAltitude(List<byte> text)
    {
        List<(int Start, int Length)> spans = CommentCodec.TrailerSpans(text);
        var lifted = new List<byte>();
        var joins = new List<int>();
        bool removed = false;
        for (int k = 0; k < text.Count; k++)
        {
            if (spans.Any(sp => k >= sp.Start && k < sp.Start + sp.Length))
            {
                removed = true;
                continue;
            }

            if (removed && lifted.Count > 0)
            {
                joins.Add(lifted.Count);
            }

            removed = false;
            lifted.Add(text[k]);
        }

        for (int j = 0; j + 4 <= lifted.Count; j++)
        {
            if (IsAltitudeAt(lifted, j) && joins.Any(p => p > j && p <= j + 3))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the text is only base-91 telemetry and a <c>!DAO!</c>, as a decoder lifts them.</summary>
    private static bool NothingButTrailer(List<byte> text)
    {
        var copy = new List<byte>(text);
        var probe = new PositionedFields();
        return CommentCodec.TryExtractTrailer(copy, 0, new DecodeContext(AprsParseOptions.Lenient), probe) && copy.Count == 0;
    }

    private static void ReadAltitude(List<byte> text, int at, PositionedFields fields)
    {
        int metres = ((text[at] - 33) * 91 * 91) + ((text[at + 1] - 33) * 91) + (text[at + 2] - 33) - 10000;
        fields.AltitudeFeet = metres * Units.FeetPerMetre;
        fields.AltitudeInBytes = true;
        text.RemoveRange(at, 4);
    }

    /// <summary>
    /// True when text written straight after digits (an <c>/A=</c> altitude's) would end a Mic-E
    /// altitude <c>xxx}</c> with them: it starts <c>}</c>, or one or two base-91 digits and <c>}</c>.
    /// </summary>
    private static bool JoinsAltitudeDigits(byte[] text)
    {
        for (int i = 0; i < Math.Min(text.Length, 3); i++)
        {
            if (text[i] == (byte)'}')
            {
                return true;
            }

            if (!Base91.IsDigit(text[i]))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>6 or 4 when the text starts with a locator of that length followed by the grid
    /// symbol <c>/G</c>; 0 otherwise. A valid 6-character locator is never re-read as 4.</summary>
    internal static int LocatorLengthAt(IReadOnlyList<byte> text)
    {
        foreach (int length in (int[])[6, 4])
        {
            if (text.Count >= length + 2 && text[length] == (byte)'/' && text[length + 1] == (byte)'G'
                && Maidenhead.IsLocator(text.Take(length).ToArray()))
            {
                return length;
            }
        }

        return 0;
    }

    private static bool IsAltitudeAt(List<byte> text, int i) =>
        text.Count >= i + 4 && Base91.IsDigit(text[i]) && Base91.IsDigit(text[i + 1]) && Base91.IsDigit(text[i + 2]) && text[i + 3] == (byte)'}';

    private static string MatchSuffix(char? typeCode, List<byte> text)
    {
        IEnumerable<string> candidates = typeCode switch
        {
            '`' or '\'' => AprsDeviceIdentification.MicESuffixes,
            '>' or ']' => AprsDeviceIdentification.MicELegacySuffixes(typeCode.Value),
            _ => [],
        };

        foreach (string s in candidates.OrderByDescending(s => s.Length))
        {
            if (text.Count < s.Length)
            {
                continue;
            }

            bool match = true;
            for (int i = 0; i < s.Length; i++)
            {
                if (text[text.Count - s.Length + i] != s[i])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return s;
            }
        }

        return "";
    }

    private readonly record struct Destination(double Latitude, int Ambiguity, AprsMicEMessage Message, bool LongitudeOffset, bool West);

    private static bool TryDecodeDestination(AprsAddress destination, DecodeContext ctx, out Destination result)
    {
        result = default;
        string call = destination.Base;
        if (call.Length != 6)
        {
            ctx.Error(AprsDiagnosticCode.InvalidMicEDestination, $"Mic-E destination '{destination}' is not 6 characters (APRS12c ch. 10)");
            return false;
        }

        int[] digits = new int[6];
        int ambiguity = 0;
        bool sawDigitAfterSpace = false;
        int[] bits = new int[3];
        bool anyStandard = false;
        bool anyCustom = false;
        for (int i = 0; i < 6; i++)
        {
            char c = call[i];
            int digit;
            bool flag;
            bool custom = false;
            switch (c)
            {
                case >= '0' and <= '9':
                    digit = c - '0';
                    flag = false;
                    break;
                case 'L':
                    digit = -1;
                    flag = false;
                    break;
                case >= 'P' and <= 'Y':
                    digit = c - 'P';
                    flag = true;
                    break;
                case 'Z':
                    digit = -1;
                    flag = true;
                    break;
                case >= 'A' and <= 'J' when i < 3:
                    digit = c - 'A';
                    flag = true;
                    custom = true;
                    break;
                case 'K' when i < 3:
                    digit = -1;
                    flag = true;
                    custom = true;
                    break;
                default:
                    ctx.Error(AprsDiagnosticCode.InvalidMicEDestination, $"'{c}' cannot appear at position {i + 1} of a Mic-E destination (APRS12c ch. 10)");
                    return false;
            }

            if (digit < 0)
            {
                ambiguity++;
            }
            else if (ambiguity > 0)
            {
                sawDigitAfterSpace = true;
            }

            digits[i] = Math.Max(digit, 0);
            if (i < 3)
            {
                bits[i] = flag ? 1 : 0;
                anyStandard |= flag && !custom;
                anyCustom |= custom;
            }
            else if (i == 3)
            {
                result = result with { Latitude = flag ? 1 : -1 };
            }
            else if (i == 4)
            {
                result = result with { LongitudeOffset = flag };
            }
            else
            {
                result = result with { West = flag };
            }
        }

        if (sawDigitAfterSpace || ambiguity > 4)
        {
            ctx.Error(AprsDiagnosticCode.InvalidMicEDestination, "Mic-E latitude ambiguity must blank only trailing digits");
            return false;
        }

        int index = (bits[0] << 2) | (bits[1] << 1) | bits[2];
        AprsMicEMessage message = anyStandard && anyCustom ? AprsMicEMessage.Unknown : anyCustom ? Custom[index] : Standard[index];
        int degrees = (digits[0] * 10) + digits[1];
        double minutes = Minutes([digits[2], digits[3], digits[4], digits[5]], ambiguity);
        if (minutes >= 60)
        {
            ctx.Error(AprsDiagnosticCode.InvalidMicEDestination, "Mic-E latitude minutes are 60 or more");
            return false;
        }

        double latitude = degrees + (minutes / 60);
        if (latitude > 90)
        {
            ctx.Error(AprsDiagnosticCode.InvalidMicEDestination, "Mic-E latitude is beyond 90 degrees");
            return false;
        }

        result = result with { Latitude = result.Latitude * latitude, Ambiguity = ambiguity, Message = message };
        return true;
    }

    /// <summary>MM.hh from four digits, blanking the last <paramref name="ambiguity"/> and centring the range.</summary>
    private static double Minutes(int[] d, int ambiguity)
    {
        double[] weights = [10, 1, 0.1, 0.01];
        double value = 0;
        for (int i = 0; i < 4 - ambiguity; i++)
        {
            value += d[i] * weights[i];
        }

        return value + ambiguity switch { 0 => 0, 1 => 0.05, 2 => 0.5, 3 => 5, _ => 30 };
    }

    // ------------------------------------------------------------------ encoding

    public static AprsAddress EncodeDestination(AprsMicEReport r)
    {
        r.Position.Validate();
        if (r.DestinationSsid is < 0 or > 15)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "Mic-E destination SSID must be 0-15");
        }

        string lat = PositionCodec.FormatCoordinate(Math.Abs(r.Position.Latitude), 2, r.Dao, r.Position.Ambiguity).Replace(".", "", StringComparison.Ordinal);
        if (r.Position.Ambiguity > 0 && lat.StartsWith("90", StringComparison.Ordinal))
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueHasNoForm, "an ambiguous latitude of 90 degrees is centred past the pole, which a decoder rejects");
        }

        _ = CheckMotion(r);
        (int[] bits, bool custom) = MessageBits(r.MicEMessage);
        int lonDegrees = int.Parse(PositionCodec.FormatCoordinate(Math.Abs(r.Position.Longitude), 3, r.Dao, r.Position.Ambiguity)[..3], System.Globalization.CultureInfo.InvariantCulture);
        bool[] flags =
        [
            bits[0] == 1, bits[1] == 1, bits[2] == 1,
            !double.IsNegative(r.Position.Latitude),
            lonDegrees is < 10 or >= 100,
            double.IsNegative(r.Position.Longitude),
        ];

        var chars = new char[6];
        for (int i = 0; i < 6; i++)
        {
            bool blank = i >= 6 - r.Position.Ambiguity;
            int digit = lat[i] - '0';
            if (!flags[i])
            {
                chars[i] = blank ? 'L' : (char)('0' + digit);
            }
            else if (custom && i < 3)
            {
                chars[i] = blank ? 'K' : (char)('A' + digit);
            }
            else
            {
                chars[i] = blank ? 'Z' : (char)('P' + digit);
            }
        }

        string call = new(chars);
        return AprsAddress.CreateUnchecked(r.DestinationSsid == 0 ? call : $"{call}-{r.DestinationSsid}");
    }

    private static (int[] Bits, bool Custom) MessageBits(AprsMicEMessage message)
    {
        int index = Array.IndexOf(Standard, message);
        bool custom = false;
        if (index < 0)
        {
            index = Array.IndexOf(Custom, message);
            custom = true;
        }

        if (index < 0)
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidCode, "an unknown Mic-E message type cannot be encoded");
        }

        return ([(index >> 2) & 1, (index >> 1) & 1, index & 1], custom && message != AprsMicEMessage.Emergency);
    }

    /// <summary>The speed and course in whole knots and degrees; refuses one the field cannot hold (vectors ruling F2).</summary>
    private static bool IsWholeMetres(double feet)
    {
        if (!double.IsFinite(feet))
        {
            return false;
        }

        double metres = feet / Units.FeetPerMetre;
        return Math.Abs(metres - Math.Round(metres)) <= 1e-6;
    }

    /// <summary>
    /// Whether the altitude is written as a <c>/A=</c> in the status text rather than in the Mic-E
    /// altitude bytes (vectors interpretations.md, "A Mic-E altitude goes in the Mic-E altitude
    /// bytes"). Decoded data knows where its altitude came from. A builder's altitude is always in
    /// the bytes. Neutral data does not say, so its value does: a <c>/A=</c> only when only a
    /// <c>/A=</c> holds it exactly, that is a whole number of feet within its six digits that the
    /// bytes do not hold exactly (not a whole number of metres, or beyond their range), or one the
    /// bytes hold but could only write as a type code.
    /// </summary>
    private static bool AltitudeAsFeet(AprsMicEReport r)
    {
        if (r.AltitudeFeet is not { } feet)
        {
            return false;
        }

        if (r.AltitudeInComment)
        {
            return true;
        }

        if (r.AltitudeInBytes || !double.IsFinite(feet))
        {
            return false;
        }

        bool wholeFeet = Math.Abs(feet - Math.Round(feet)) <= 1e-6 && Math.Round(feet) is >= -99999 and <= 999999;
        if (!wholeFeet)
        {
            return false;
        }

        bool bytesHold = IsWholeMetres(feet) && Math.Round(feet / Units.FeetPerMetre) is >= -10000 and <= 743570;
        if (!bytesHold)
        {
            return true;
        }

        // Where the bytes have no clean form (a first byte that is a type code character, with no
        // type code before it) and a /A= holds the value exactly, it goes in the /A=.
        long value = (long)Math.Round(feet / Units.FeetPerMetre) + 10000;
        return r.TypeCode is null && (char)((value / (91 * 91)) + 33) is '`' or '\'' or '>' or ']';
    }

    private static (int Speed, int Course) CheckMotion(AprsMicEReport r)
    {
        NotANumber.Refuse(r.SpeedKnots, "Mic-E speed");
        NotANumber.Refuse(r.CourseDegrees, "Mic-E course");
        double speedKnots = r.SpeedKnots is { } s ? Steps.Whole(s) : 0;
        int speed = speedKnots is < 0 or > 799 ? -1 : (int)speedKnots;
        double courseDegrees = r.CourseDegrees is { } cd ? Steps.Whole(cd) : 0;
        if (speed is < 0 or > 799 || courseDegrees is < 0 or > 360)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "Mic-E speed must be 0-799 knots and course 0-360");
        }

        int course = (int)courseDegrees;
        if (r.CourseDegrees is not null && course == 0)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueHasNoForm, "a Mic-E course of 0 reads back as no course; north is 360");
        }

        return (speed, course);
    }

    public static void WriteInformation(InfoWriter w, AprsMicEReport r)
    {
        // Each field is checked on its own, and the refusal named is the first in the shared
        // order whichever field it is in (InfoWriter.Field), before any relation between fields.
        w.Field(r.Position.Validate);
        w.Field(r.Symbol.Validate);
        w.Field(() =>
        {
            string firstLatitude = PositionCodec.FormatCoordinate(Math.Abs(r.Position.Latitude), 2, r.Dao, r.Position.Ambiguity);
            if (r.Position.Ambiguity > 0 && firstLatitude.StartsWith("90", StringComparison.Ordinal))
            {
                throw new AprsEncodeException(AprsRefusalCode.ValueHasNoForm, "an ambiguous latitude of 90 degrees is centred past the pole, which a decoder rejects");
            }
        });

        (int speed, int course) = (0, 0);
        w.Field(() => (speed, course) = CheckMotion(r));
        if (r.AltitudeFeet is { } altitudeFeet && AltitudeAsFeet(r))
        {
            // Written as /A=, so it must fit that, before the later value checks.
            w.Field(() => CommentCodec.CheckAltitude(altitudeFeet));
        }

        if (r.IsCompressed || r.Weather is not null || r.Storm is not null || r.DfBearing is not null || r.AreaObject is not null || r.SignpostText is not null)
        {
            w.Defer(AprsRefusalCode.FieldNotAllowed, "a Mic-E report cannot carry compression, weather, storm, DF bearing, area object or signpost data");
        }

        // The nine fixed bytes, from the longitude digits (a longitude out of range has none, so
        // this is one field like the others).
        w.Field(() =>
        {
            string lon = PositionCodec.FormatCoordinate(Math.Abs(r.Position.Longitude), 3, r.Dao, r.Position.Ambiguity);
            int deg = int.Parse(lon[..3], System.Globalization.CultureInfo.InvariantCulture);
            int min = int.Parse(lon.AsSpan(3, 2), System.Globalization.CultureInfo.InvariantCulture);
            int hun = int.Parse(lon.AsSpan(6, 2), System.Globalization.CultureInfo.InvariantCulture);

            // The longitude digits the latitude's ambiguity covers are ignored by a decoder, which
            // reports the centre of the box; they are written as that centre (vectors README, Mic-E).
            (min, hun) = r.Position.Ambiguity switch
            {
                1 => (min, (hun / 10 * 10) + 5),
                2 => (min, 50),
                3 => ((min / 10 * 10) + 5, 0),
                4 => (30, 0),
                _ => (min, hun),
            };

            int d = deg switch
            {
                < 10 => deg + 90,
                < 100 => deg,
                < 110 => deg - 20,
                _ => deg - 100,
            };

            if (deg >= 180)
            {
                // Degrees 180-189 would be written as 100-109 are, so 180 itself has no Mic-E form (APRS12c ch. 10).
                w.Defer(AprsRefusalCode.ValueHasNoForm, "a Mic-E longitude is under 180 degrees east or west (APRS12c ch. 10)");
            }

            // The printable encoding schemes: speeds under 200 knots use SP + 80, except 190-199, where
            // that would be DEL and the table's other character is '/' (vectors ruling E5), and DC always
            // adds 4 to the course hundreds, so only the SE byte can be a control character (APRS12c §10).
            int sp = speed < 190 ? (speed / 10) + 80 : speed / 10;
            int dc = ((speed % 10) * 10) + (course / 100) + 4;
            int se = course % 100;

            w.Char(r.IsOldData ? '\'' : '`')
                .Byte((byte)(d + 28))
                .Byte((byte)((min < 10 ? min + 60 : min) + 28))
                .Byte((byte)(hun + 28))
                .Byte((byte)(sp + 28))
                .Byte((byte)(dc + 28))
                .Byte((byte)(se + 28))
                .Char(r.Symbol.Code)
                .Char(r.Symbol.Table);
        });

        if (r.LegacyTelemetry is { } legacy)
        {
            w.Field(() => WriteLegacyTelemetry(w, legacy));
        }

        WriteAfterTelemetry(w, r);
    }

    private static void WriteLegacyTelemetry(InfoWriter w, IReadOnlyList<int> legacy)
    {
        {
            // A 255 would be taken for Kenwood 0xFF padding and removed on the way back in.
            if (legacy.Count != 5)
            {
                throw new AprsEncodeException(AprsRefusalCode.Malformed, "obsolete Mic-E binary telemetry is 5 values, each 0-254");
            }

            if (legacy.Any(v => v is < 0 or > 255))
            {
                throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "obsolete Mic-E binary telemetry is 5 values, each 0-254");
            }

            if (legacy.Any(v => v == 255))
            {
                throw new AprsEncodeException(AprsRefusalCode.ValueHasNoForm, "obsolete Mic-E binary telemetry is 5 values, each 0-254");
            }

            w.Byte(0x1d);
            foreach (int v in legacy)
            {
                w.Byte((byte)v);
            }
        }
    }

    private static void WriteAfterTelemetry(InfoWriter w, AprsMicEReport r)
    {
        // An unknown message type comes after the value checks above and before the device suffix.
        w.Field(() => MessageBits(r.MicEMessage));
        if (r.TypeCode is { } type)
        {
            if (type is not ('`' or '\'' or '>' or ']' or ' '))
            {
                w.Defer(AprsRefusalCode.InvalidCode, "Mic-E type code must be ` ' > ] or space");
            }

            w.Char(type);
        }
        else if (r.DeviceSuffix.Length > 0)
        {
            w.Defer(AprsRefusalCode.MissingField, "a Mic-E device suffix needs a type code");
        }

        if (r.DeviceSuffix.Length > 0 && r.TypeCode is not null)
        {
            // A decoder matches only the suffixes the device database lists for the type code; any
            // other is comment text. Whether the text before it joins a longer suffix is checked
            // by decoding the finished field.
            IEnumerable<string> known = r.TypeCode switch
            {
                '`' or '\'' => AprsDeviceIdentification.MicESuffixes,
                '>' or ']' => AprsDeviceIdentification.MicELegacySuffixes(r.TypeCode.Value),
                _ => [],
            };
            if (!known.Contains(r.DeviceSuffix, StringComparer.Ordinal))
            {
                w.Defer(AprsRefusalCode.InvalidCode, $"'{r.DeviceSuffix}' is not a Mic-E device suffix for the type code '{r.TypeCode}' (aprs-deviceid), so it would read back as comment text");
            }

            w.CommentCheck ??= "the Mic-E status text would read back with a different device suffix";
        }

        // Mic-E carries altitude in whole metres (xxx}); a /A= only where the data came from one
        // or only a /A= holds the value exactly (AltitudeAsFeet).
        bool altitudeAsFeet = AltitudeAsFeet(r);
        if (r.AltitudeFeet is { } feet)
        {
            w.Field(() => WriteAltitudeBytes(w, r, feet, altitudeAsFeet));
        }

        WriteStatusText(w, r, altitudeAsFeet);
    }

    private static void WriteAltitudeBytes(InfoWriter w, AprsMicEReport r, double feet, bool altitudeAsFeet)
    {
        {
            long metres = Steps.Count(feet / Units.FeetPerMetre);
            long value = metres + 10000;
            if (!altitudeAsFeet)
            {
                if (value is < 0 or > (91 * 91 * 91) - 1)
                {
                    throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "Mic-E altitude is out of range");
                }

                // Without a type code, an altitude whose first character is itself a type code
                // character would be read back as one.
                char first = (char)((value / (91 * 91)) + 33);
                if (r.TypeCode is null && first is '`' or '\'' or '>' or ']')
                {
                    // A /A= that held it exactly was chosen before this point, so what reaches here
                    // has no form: the bytes read back as a type code (vectors README, Refusal reasons).
                    w.Defer(AprsRefusalCode.ValueHasNoForm, "without a TypeCode this Mic-E altitude would decode as a device type code, and no /A= holds it exactly");
                }

                w.Base91(value, 3).Char('}');
            }
        }
    }

    private static void WriteStatusText(InfoWriter w, AprsMicEReport r, bool altitudeAsFeet)
    {
        if (r.Locator is { } locator)
        {
            // A locator in lower case reads back in upper case, so it is not the fixed form either.
            if (locator.Length is not (4 or 6) || !Maidenhead.IsLocator(System.Text.Encoding.ASCII.GetBytes(locator))
                || !locator.Equals(locator.ToUpperInvariant(), StringComparison.Ordinal))
            {
                w.Defer(AprsRefusalCode.Malformed, $"'{locator}' is not a 4 or 6 character Maidenhead locator in upper case");
            }

            w.Ascii(locator.ToUpperInvariant()).Ascii("/G");
            // Telemetry and a !DAO! are lifted before the locator is read, so they follow it
            // straight on (vectors ruling F16).
            bool more = altitudeAsFeet || r.Phg is not null || r.RangeMiles is not null || r.DfSignalStrength is not null
                || r.Frequency is not null || r.Comment.Length > 0;
            if (more)
            {
                w.Char(' ');
            }
        }

        if ((r.Phg is not null ? 1 : 0) + (r.RangeMiles is not null ? 1 : 0) + (r.DfSignalStrength is not null ? 1 : 0) > 1)
        {
            // A decoder lifts one data extension from the status text (vectors interpretations.md,
            // "A late data extension is the first well-formed one"); a second would stay as text.
            w.Defer(AprsRefusalCode.ExtensionConflict, "a Mic-E report carries at most one of PHG, RNG and DFS");
        }

        // Each extension given has its own checks, even beside another (vectors README, Refusal reasons).
        if (r.Phg is { } givenPhg)
        {
            w.Field(givenPhg.Validate);
        }

        if (r.DfSignalStrength is { } givenDfs)
        {
            w.Field(givenDfs.Validate);
        }

        if (r.RangeMiles is { } givenRange)
        {
            w.Field(() =>
            {
                NotANumber.Refuse(givenRange, "RNG");
                if (Steps.Whole(givenRange) is < 0 or > 9999)
                {
                    throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "RNG carries whole miles 0-9999");
                }
            });
        }

        w.Field(() => WriteExtension(w, r));
        w.Field(() => CommentCodec.CheckMicEComment(w, r));
        if (altitudeAsFeet)
        {
            w.Field(() => CommentCodec.WriteAltitude(w, r.AltitudeFeet!.Value));
        }

        WriteComment(w, r, altitudeAsFeet);
    }

    private static void WriteExtension(InfoWriter w, AprsMicEReport r)
    {
        if (r.Phg is { } phg)
        {
            PositionCodec.WritePhg(w, phg);
        }
        else if (r.RangeMiles is { } range)
        {
            // Rounded to whole miles, then checked (vectors ruling F2).
            NotANumber.Refuse(range, "RNG");
            double miles = Steps.Whole(range);
            if (miles is < 0 or > 9999)
            {
                throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "RNG carries whole miles 0-9999");
            }

            w.Ascii("RNG").Digits((int)miles, 4);
        }
        else if (r.DfSignalStrength is { } dfs)
        {
            w.Ascii("DFS").Digits(dfs.StrengthCode, 1).Char((char)('0' + dfs.HeightCode)).Digits(dfs.GainCode, 1).Digits(dfs.DirectivityCode, 1);
        }
    }

    private static void WriteComment(InfoWriter w, AprsMicEReport r, bool altitudeAsFeet)
    {
        // Status text holds no control character, a leading 0x1D included (vectors README, "Control
        // characters in text").
        w.Field(() => Text.RequireNoControlCharacters(r.Comment));
        if (r.Frequency is { } freq)
        {
            // Straight after a PHG, RNG or DFS the frequency follows a '/', as in a position
            // comment; a PHGR already ends in one (vectors interpretations.md, "Where a voice
            // frequency goes").
            if (!altitudeAsFeet && (r.Phg is { BeaconsPerHour: null } || r.RangeMiles is not null || r.DfSignalStrength is not null))
            {
                w.Char('/');
            }

            w.Field(() => FrequencyCodec.WriteWithComment(w, freq, r.Comment));
        }

        bool extensionJustWritten = r.Frequency is null && !altitudeAsFeet && (r.Phg is not null || r.RangeMiles is not null || r.DfSignalStrength is not null);
        bool commentFirst = r.Frequency is null && !altitudeAsFeet && r.Phg is null && r.RangeMiles is null && r.DfSignalStrength is null;
        byte[] commentBytes = System.Text.Encoding.UTF8.GetBytes(r.Comment);
        bool readsAsLocator = commentFirst && r.Locator is null && LocatorLengthAt(commentBytes) > 0;

        // "The Mic-E status text must not start with ` , ' or 0x1d, otherwise it will be confused
        // with [now obsolete] telemetry data" (APRS12c ch. 10). A comment that would start it with a
        // type code character goes after a '/' delimiter, which the decoder drops
        // (interpretations.md, "Delimiters at the start of free text"); one that starts with 0x1D is
        // a control character and is refused.
        bool readsAsTypeCode = commentFirst && r.TypeCode is null && r.Locator is null && r.AltitudeFeet is null
            && commentBytes.Length > 0 && commentBytes[0] is (byte)'`' or (byte)'\'' or (byte)'>' or (byte)']';
        if (r.Frequency is null && (CommentCodec.NeedsDelimiter(r.Comment, extensionJustWritten ? r.Phg : null) || readsAsLocator || readsAsTypeCode))
        {
            // A '/' delimiter can itself start a Mic-E altitude run (//x}, or 0/x} after /A=
            // digits); a space delimiter, which a decoder drops as well, cannot (vectors ruling E12).
            var after = new List<byte> { (byte)'0', (byte)'/' };
            after.AddRange(commentBytes.Take(3));
            // Only where a later altitude is looked for: after one in the altitude bytes, none is.
            bool laterAltitudeRead = r.AltitudeFeet is null || altitudeAsFeet;
            bool slashJoins = laterAltitudeRead && ((altitudeAsFeet && IsAltitudeAt(after, 0)) || IsAltitudeAt(after, 1));
            w.Char(slashJoins ? ' ' : '/');
        }
        else if (r.Frequency is null && altitudeAsFeet && JoinsAltitudeDigits(commentBytes))
        {
            // Straight after /A=000010 a comment starting "4} would make the run 0"4} a Mic-E
            // altitude. A separating space, which a decoder drops, is the clean form, so it is
            // written rather than the data refused (vectors ruling E12).
            w.Char(' ');
        }

        if (r.Frequency is null)
        {
            w.Utf8(r.Comment);
        }

        if (r.Telemetry is { } t)
        {
            w.Field(() => CommentCodec.WriteTelemetry(w, t));
        }

        if (r.Dao is { } dao)
        {
            w.Field(() => DaoCodec.Write(w, r.Position, dao));
        }

        w.Ascii(r.DeviceSuffix);
    }
}
