using System.Globalization;

namespace Packet.Aprs.Internal;

/// <summary>
/// The position body shared by position reports, objects and items: an uncompressed
/// (APRS12c §6, §8) or compressed (§9) position with symbol, then the data extension (§7) and
/// the comment.
/// </summary>
internal static class PositionCodec
{
    private const double LatitudeScale = 380926;
    private const double LongitudeScale = 190463;

    public static bool TryReadBody(ReadOnlySpan<byte> info, int pos, DecodeContext ctx, out PositionedFields fields)
    {
        fields = new PositionedFields();
        if (pos >= info.Length)
        {
            ctx.Error(AprsDiagnosticCode.Truncated, "position is missing", pos);
            return false;
        }

        byte first = info[pos];
        if (Text.IsDigit(first))
        {
            if (!TryReadUncompressed(info, pos, ctx, fields))
            {
                return false;
            }

            pos += 19;
            return TryReadExtensionAndComment(info, pos, ctx, fields);
        }

        if (first is (byte)'/' or (byte)'\\' || Text.IsUpper(first) || first is >= (byte)'a' and <= (byte)'j')
        {
            if (!TryReadCompressed(info, pos, ctx, fields, out bool csIsWind))
            {
                return false;
            }

            pos += 13;
            return TryReadCompressedTail(info, pos, ctx, fields, csIsWind);
        }

        ctx.Error(AprsDiagnosticCode.InvalidPosition, $"'{(char)first}' cannot start a position: expected a latitude digit or a compressed-position symbol table", pos);
        return false;
    }

    /// <summary>
    /// Whether a position itself decodes at <paramref name="pos"/>: the latitude, symbol table,
    /// longitude and symbol code, or the 13 bytes of a compressed position, under the options in
    /// force, and not anything after it (vectors interpretations.md, "A packet is read in order").
    /// </summary>
    public static bool PositionDecodesAt(ReadOnlySpan<byte> info, int pos, DecodeContext ctx)
    {
        var probe = new DecodeContext(ctx.Options) { Depth = ctx.Depth };
        if (pos >= info.Length)
        {
            return false;
        }

        byte first = info[pos];
        return Text.IsDigit(first)
            ? TryReadUncompressed(info, pos, probe, new PositionedFields())
            : (first is (byte)'/' or (byte)'\\' || Text.IsUpper(first) || first is >= (byte)'a' and <= (byte)'j')
                && TryReadCompressed(info, pos, probe, new PositionedFields(), out _);
    }

    // ---------------------------------------------------------------- uncompressed

    private static bool TryReadUncompressed(ReadOnlySpan<byte> info, int pos, DecodeContext ctx, PositionedFields fields)
    {
        if (info.Length < pos + 19)
        {
            ctx.Error(AprsDiagnosticCode.Truncated, "uncompressed position needs 19 characters (latitude, table, longitude, symbol)", pos);
            return false;
        }

        if (!TryReadLatitude(info.Slice(pos, 8), pos, ctx, out double latitude, out int ambiguity))
        {
            return false;
        }

        char table = (char)info[pos + 8];
        if (!AprsSymbol.IsValidTable(table))
        {
            ctx.Error(AprsDiagnosticCode.InvalidSymbolTable, $"'{table}' is not a symbol table or overlay (/ \\ 0-9 A-Z)", pos + 8);
            return false;
        }

        if (!TryReadLongitude(info.Slice(pos + 9, 9), pos + 9, ambiguity, ctx, out double longitude))
        {
            return false;
        }

        char code = (char)info[pos + 18];
        if (code is < '!' or > '~')
        {
            ctx.Error(AprsDiagnosticCode.InvalidSymbolCode, $"byte 0x{(int)code:X2} is not a symbol code (! to ~)", pos + 18);
            return false;
        }

        fields.Position = new AprsPosition(latitude, longitude, ambiguity);
        fields.Symbol = new AprsSymbol(table, code);
        return true;
    }

    /// <summary><c>DDMM.hhN</c>, trailing digits optionally blanked for ambiguity.</summary>
    private static bool TryReadLatitude(ReadOnlySpan<byte> s, int offset, DecodeContext ctx, out double latitude, out int ambiguity)
    {
        latitude = 0;
        ambiguity = 0;
        if (!Text.IsDigit(s[0]) || !Text.IsDigit(s[1]) || s[4] != (byte)'.')
        {
            ctx.Error(AprsDiagnosticCode.InvalidLatitude, $"'{Text.Latin1(s)}' is not a latitude: expected DDMM.hhN (APRS12c ch. 6)", offset);
            return false;
        }

        if (!TryReadMinuteDigits(s[2], s[3], s[5], s[6], maxAmbiguity: 4, out double minutes, out ambiguity))
        {
            ctx.Error(AprsDiagnosticCode.InvalidLatitude, $"'{Text.Latin1(s)}' has invalid latitude minutes (digits, with only trailing spaces for ambiguity)", offset);
            return false;
        }

        // The digits and their range are read before the hemisphere letter (vectors interpretations.md, "A packet is read in order").
        int degrees = Text.ParseDigits(s[..2]);
        latitude = degrees + (minutes / 60);
        if (latitude > 90)
        {
            ctx.Error(AprsDiagnosticCode.InvalidLatitude, $"latitude {Text.Latin1(s)} is beyond 90 degrees", offset);
            return false;
        }

        if (!TryHemisphere(s[7], (byte)'N', (byte)'S', offset + 7, ctx, out bool negative))
        {
            return false;
        }

        latitude = negative ? -latitude : latitude;
        return true;
    }

    /// <summary><c>DDDMM.hhW</c>. Ambiguity comes from the latitude; blanked digits here are optional.</summary>
    private static bool TryReadLongitude(ReadOnlySpan<byte> s, int offset, int ambiguity, DecodeContext ctx, out double longitude)
    {
        longitude = 0;
        if (!Text.IsDigit(s[0]) || !Text.IsDigit(s[1]) || !Text.IsDigit(s[2]) || s[5] != (byte)'.')
        {
            ctx.Error(AprsDiagnosticCode.InvalidLongitude, $"'{Text.Latin1(s)}' is not a longitude: expected DDDMM.hhW (APRS12c ch. 6)", offset);
            return false;
        }

        // Digits the latitude's ambiguity covers may be anything numeric or a space; the rest must be digits.
        Span<byte> digits = [s[3], s[4], s[6], s[7]];
        for (int i = 0; i < 4; i++)
        {
            bool masked = i >= 4 - ambiguity;
            if (masked)
            {
                if (digits[i] == (byte)' ' || Text.IsDigit(digits[i]))
                {
                    digits[i] = (byte)'0';
                    continue;
                }
            }
            else if (Text.IsDigit(digits[i]))
            {
                continue;
            }

            ctx.Error(AprsDiagnosticCode.InvalidLongitude, $"'{Text.Latin1(s)}' has invalid longitude minutes", offset);
            return false;
        }

        if (!TryReadMinuteDigits(digits[0], digits[1], digits[2], digits[3], maxAmbiguity: 0, out double minutes, out _))
        {
            ctx.Error(AprsDiagnosticCode.InvalidLongitude, $"'{Text.Latin1(s)}' has longitude minutes of 60 or more", offset);
            return false;
        }

        minutes += AmbiguityCentre(ambiguity);
        longitude = Text.ParseDigits(s[..3]) + (minutes / 60);
        if (longitude > 180)
        {
            ctx.Error(AprsDiagnosticCode.InvalidLongitude, $"longitude {Text.Latin1(s)} is beyond 180 degrees", offset);
            return false;
        }

        if (!TryHemisphere(s[8], (byte)'E', (byte)'W', offset + 8, ctx, out bool negative))
        {
            return false;
        }

        longitude = negative ? -longitude : longitude;
        return true;
    }

    /// <summary>Reads MM.hh from four digit positions (tens, units, tenths, hundredths). Trailing spaces
    /// up to <paramref name="maxAmbiguity"/> are ambiguity; the result is the centre of the range.</summary>
    private static bool TryReadMinuteDigits(byte tens, byte units, byte tenths, byte hundredths, int maxAmbiguity, out double minutes, out int ambiguity)
    {
        minutes = 0;
        ReadOnlySpan<byte> d = [tens, units, tenths, hundredths];
        ambiguity = 0;
        for (int i = 3; i >= 0 && d[i] == (byte)' '; i--)
        {
            ambiguity++;
        }

        if (ambiguity > maxAmbiguity)
        {
            return false;
        }

        for (int i = 0; i < 4 - ambiguity; i++)
        {
            if (!Text.IsDigit(d[i]))
            {
                return false;
            }
        }

        double value = 0;
        double[] weights = [10, 1, 0.1, 0.01];
        for (int i = 0; i < 4 - ambiguity; i++)
        {
            value += (d[i] - '0') * weights[i];
        }

        minutes = value + AmbiguityCentre(ambiguity);
        return minutes < 60;
    }

    /// <summary>Half the resolution left by an ambiguity level, in minutes, so an ambiguous
    /// position decodes to the centre of its range (interpretations.md in packet-net/aprs-vectors).</summary>
    private static double AmbiguityCentre(int ambiguity) => ambiguity switch
    {
        0 => 0,
        1 => 0.05,
        2 => 0.5,
        3 => 5,
        _ => 30,
    };

    private static bool TryHemisphere(byte b, byte positive, byte negative, int offset, DecodeContext ctx, out bool isNegative)
    {
        isNegative = false;
        if (b == positive || b == negative)
        {
            isNegative = b == negative;
            return true;
        }

        byte upper = (byte)(b & ~0x20);
        if ((upper == positive || upper == negative) && Text.IsLower(b))
        {
            isNegative = upper == negative;
            return ctx.Tolerate(
                ctx.Options.AllowLowercaseHemisphere,
                AprsDiagnosticCode.LowercaseHemisphere,
                $"hemisphere '{(char)b}' must be upper case (UAP 5.9)",
                offset);
        }

        ctx.Error(
            positive == (byte)'N' ? AprsDiagnosticCode.InvalidLatitude : AprsDiagnosticCode.InvalidLongitude,
            $"'{(char)b}' is not a hemisphere: expected {(char)positive} or {(char)negative}",
            offset);
        return false;
    }

    // ---------------------------------------------------------------- compressed

    private static bool TryReadCompressed(ReadOnlySpan<byte> info, int pos, DecodeContext ctx, PositionedFields fields, out bool csIsWind)
    {
        csIsWind = false;
        if (info.Length < pos + 13)
        {
            ctx.Error(AprsDiagnosticCode.Truncated, "compressed position needs 13 characters", pos);
            return false;
        }

        ReadOnlySpan<byte> c = info.Slice(pos, 13);
        char table = (char)c[0];
        if (table is >= 'a' and <= 'j')
        {
            table = (char)('0' + (table - 'a'));
        }

        if (!Base91.TryDecode(c.Slice(1, 4), out long y) || !Base91.TryDecode(c.Slice(5, 4), out long x))
        {
            ctx.Error(AprsDiagnosticCode.InvalidCompressedPosition, "compressed latitude/longitude must be base-91 characters ! to { (APRS12c ch. 9)", pos + 1);
            return false;
        }

        double latitude = 90 - (y / LatitudeScale);
        double longitude = -180 + (x / LongitudeScale);
        if (latitude < -90 || longitude > 180)
        {
            ctx.Error(AprsDiagnosticCode.InvalidCompressedPosition, "compressed position is outside the globe", pos + 1);
            return false;
        }

        char code = (char)c[9];
        if (code is < '!' or > '~')
        {
            ctx.Error(AprsDiagnosticCode.InvalidSymbolCode, $"byte 0x{(int)code:X2} is not a symbol code (! to ~)", pos + 9);
            return false;
        }

        fields.IsCompressed = true;
        fields.Position = new AprsPosition(latitude, longitude);
        fields.Symbol = new AprsSymbol(table, code);

        byte cByte = c[10];
        byte sByte = c[11];
        byte tByte = c[12];
        if (cByte == (byte)' ')
        {
            // No course/speed, range or altitude; the s and T bytes are filler (APRS12c §9).
            return true;
        }

        if (!Base91.IsDigit(cByte) || !Base91.IsDigit(sByte) || !Base91.IsDigit(tByte))
        {
            ctx.Error(AprsDiagnosticCode.InvalidCompressedPosition, "compressed cs and T bytes must be base-91 characters", pos + 10);
            return false;
        }

        int cv = cByte - 33;
        int sv = sByte - 33;
        int tv = tByte - 33;
        if (tv > 63)
        {
            if (!ctx.Tolerate(
                    ctx.Options.AllowCompressionTypeReservedBits,
                    AprsDiagnosticCode.CompressionTypeReservedBits,
                    "compression type byte sets its unused high bits (APRS12c ch. 9); ignored",
                    pos + 12))
            {
                return false;
            }

            tv &= 0x3F;
        }

        var type = AprsCompressionType.FromBits(tv);
        fields.CompressionType = type;
        if (fields.Symbol.IsWeatherStation && cByte != (byte)'{' && type.Source != AprsCompressionSource.Gga)
        {
            csIsWind = true;
            fields.Weather = new AprsWeather
            {
                WindDirectionDegrees = cv * 4,
                WindSpeedMph = Units.KnotsToMph(Math.Pow(1.08, sv) - 1),
            };
        }
        else if (type.Source == AprsCompressionSource.Gga)
        {
            fields.AltitudeFeet = Math.Pow(1.002, (cv * 91) + sv);
        }
        else if (cByte == (byte)'{')
        {
            fields.RangeMiles = 2 * Math.Pow(1.08, sv);
        }
        else if (cv <= 89)
        {
            // The compressed course is 0-356 in 4-degree steps with no "unknown" value, so 0 is
            // north: reported as 360, the convention for north (as Ham::APRS::FAP does).
            fields.CourseDegrees = cv == 0 ? 360 : cv * 4;
            fields.SpeedKnots = Math.Pow(1.08, sv) - 1;
        }
        else
        {
            ctx.Error(AprsDiagnosticCode.InvalidCompressedPosition, "compressed course byte is out of range", pos + 10);
            return false;
        }

        return true;
    }

    private static bool TryReadCompressedTail(ReadOnlySpan<byte> info, int pos, DecodeContext ctx, PositionedFields fields, bool csIsWind)
    {
        if (fields.Symbol.IsWeatherStation)
        {
            var weather = fields.Weather ?? new AprsWeather();
            bool windAsFields = false;

            // LoRa trackers send the uncompressed DDD/SSS wind extension after a compressed
            // position, whose cs bytes already carry wind (UAP §5.33).
            if (info.Length >= pos + 7 && IsCourseSpeed(info.Slice(pos, 7)))
            {
                if (!ctx.Tolerate(
                        ctx.Options.AllowWindExtensionAfterCompressed,
                        AprsDiagnosticCode.WindExtensionAfterCompressed,
                        "an uncompressed DDD/SSS wind extension follows a compressed position (UAP 5.33)",
                        pos)
                    || !WeatherCodec.TryReadWindExtension(info, ref pos, ctx, ref weather))
                {
                    return false;
                }

                // Re-encoding puts this wind in the cs bytes, which then need a compression type.
                if (weather.WindDirectionDegrees is not null || weather.WindSpeedMph is not null)
                {
                    fields.CompressionType ??= AprsCompressionType.Default;
                }
            }
            else if (!csIsWind && WeatherCodec.WindComesAsFields(info, pos, ctx))
            {
                // Others leave the cs bytes blank and send positionless-style c/s wind fields.
                if (!ctx.Tolerate(
                        ctx.Options.AllowWindFieldsInsteadOfExtension,
                        AprsDiagnosticCode.WindFieldsInsteadOfExtension,
                        "wind sent as c/s fields after a compressed position with no wind in its cs bytes (APRS12c ch. 12)",
                        pos))
                {
                    return false;
                }

                windAsFields = true;
            }

            if (!WeatherCodec.TryReadFields(info, ref pos, ctx, ref weather, positionless: false, windAsFields))
            {
                return false;
            }

            if (windAsFields && (weather.WindDirectionDegrees is not null || weather.WindSpeedMph is not null))
            {
                fields.CompressionType ??= AprsCompressionType.Default;
            }

            fields.Weather = weather;
            return CommentCodec.TryReadWeatherTail(info, pos, ctx, fields);
        }

        return CommentCodec.TryRead(info, pos, ctx, fields);
    }

    // ---------------------------------------------------------------- data extension

    private static bool TryReadExtensionAndComment(ReadOnlySpan<byte> info, int pos, DecodeContext ctx, PositionedFields fields)
    {
        if (fields.Symbol.IsWeatherStation)
        {
            var weather = new AprsWeather();
            if (!WeatherCodec.TryReadWind(info, ref pos, ctx, ref weather, out bool windAsFields) ||
                !WeatherCodec.TryReadFields(info, ref pos, ctx, ref weather, positionless: false, windAsFields, windMustBeComplete: true))
            {
                return false;
            }

            fields.Weather = weather;
            return CommentCodec.TryReadWeatherTail(info, pos, ctx, fields);
        }

        if (!TryReadDataExtension(info, ref pos, ctx, fields))
        {
            return false;
        }

        return CommentCodec.TryRead(info, pos, ctx, fields);
    }

    internal static bool TryReadDataExtension(ReadOnlySpan<byte> info, ref int pos, DecodeContext ctx, PositionedFields fields)
    {
        if (info.Length < pos + 7)
        {
            return true;
        }

        ReadOnlySpan<byte> ext = info.Slice(pos, 7);
        if (fields.Symbol is { Table: '\\', Code: 'l' } && TryReadAreaObject(ext, out AprsAreaObject area))
        {
            fields.AreaObject = area;
            pos += 7;
            return true;
        }

        if (IsCourseSpeed(ext))
        {
            int at = pos;
            pos += 7;
            if (!TryCourseSpeedValues(ext, at, ctx, out int? course, out int? speed))
            {
                return false;
            }

            fields.CourseDegrees = course;
            fields.SpeedKnots = speed;
            if (fields.Symbol is { Table: '/', Code: '\\' } && info.Length >= pos + 8 && IsBearing(info.Slice(pos, 8)))
            {
                // A bearing is degrees, so one over 360 is out of range and the whole /BRG/NRQ is
                // dropped, as an out-of-range course is (vectors README, Positions).
                int bearingAt = pos;
                pos += 8;
                if (TryReadBearing(info.Slice(bearingAt, 8), out AprsDfBearing bearing))
                {
                    fields.DfBearing = bearing;
                }
                else if (!ctx.Tolerate(ctx.Options.AllowOutOfRangeValue, AprsDiagnosticCode.OutOfRangeValue, "DF bearing is over 360 degrees; ignored", bearingAt + 1))
                {
                    return false;
                }
            }
            else if (fields.Symbol.Code == '@' && StormCodec.TryRead(info, ref pos, out AprsStorm? storm))
            {
                fields.Storm = storm;
            }

            return true;
        }

        if (ext.StartsWith("PHG"u8) && Text.IsDigit(ext[3]) && ext[4] >= (byte)'0' && ext[4] <= (byte)'~' && Text.IsDigit(ext[5]) && Text.IsDigit(ext[6]))
        {
            int? rate = null;
            if (info.Length >= pos + 9 && info[pos + 8] == (byte)'/' && BeaconRate(info[pos + 7]) is { } r)
            {
                rate = r;
            }

            fields.Phg = new AprsPhg(ext[3] - '0', ext[4] - '0', ext[5] - '0', ext[6] - '0', rate);
            pos += rate is null ? 7 : 9;
            return true;
        }

        if (ext.StartsWith("RNG"u8) && Text.AllDigits(ext[3..]))
        {
            fields.RangeMiles = Text.ParseDigits(ext[3..]);
            pos += 7;
            return true;
        }

        if (ext.StartsWith("DFS"u8) && Text.IsDigit(ext[3]) && ext[4] >= (byte)'0' && ext[4] <= (byte)'~' && Text.IsDigit(ext[5]) && Text.IsDigit(ext[6]))
        {
            fields.DfSignalStrength = new AprsDfSignalStrength(ext[3] - '0', ext[4] - '0', ext[5] - '0', ext[6] - '0');
            pos += 7;
            return true;
        }

        return true;
    }

    /// <summary>PHGR beacon rate character: 1-9, then A = 10 up to Z = 35.</summary>
    internal static int? BeaconRate(byte b) => b switch
    {
        >= (byte)'1' and <= (byte)'9' => b - '0',
        >= (byte)'A' and <= (byte)'Z' => b - 'A' + 10,
        _ => null,
    };

    /// <summary>Three characters, <c>/</c>, three characters, where each group is all digits, all dots or all spaces.</summary>
    internal static bool IsCourseSpeed(ReadOnlySpan<byte> ext) =>
        ext.Length >= 7 && ext[3] == (byte)'/' && IsValueGroup(ext[..3]) && IsValueGroup(ext.Slice(4, 3));

    internal static bool IsValueGroup(ReadOnlySpan<byte> g) =>
        Text.AllDigits(g) || g.SequenceEqual("..."u8) || g.SequenceEqual("   "u8);

    private static bool TryCourseSpeedValues(ReadOnlySpan<byte> ext, int offset, DecodeContext ctx, out int? course, out int? speed)
    {
        course = Text.AllDigits(ext[..3]) ? Text.ParseDigits(ext[..3]) : null;
        speed = Text.AllDigits(ext.Slice(4, 3)) ? Text.ParseDigits(ext.Slice(4, 3)) : null;
        if (course > 360)
        {
            course = null;
            return ctx.Tolerate(ctx.Options.AllowOutOfRangeValue, AprsDiagnosticCode.OutOfRangeValue, "course is over 360 degrees; ignored", offset);
        }

        return true;
    }

    /// <summary><c>/BRG/NRQ</c>: a slash, three digits, a slash and three digits (APRS12c ch. 7).</summary>
    private static bool IsBearing(ReadOnlySpan<byte> s) =>
        s[0] == (byte)'/' && s[4] == (byte)'/' && Text.AllDigits(s.Slice(1, 3)) && Text.AllDigits(s.Slice(5, 3));

    private static bool TryReadBearing(ReadOnlySpan<byte> s, out AprsDfBearing bearing)
    {
        bearing = default;
        if (!IsBearing(s))
        {
            return false;
        }

        int brg = Text.ParseDigits(s.Slice(1, 3));
        if (brg > 360)
        {
            return false;
        }

        bearing = new AprsDfBearing(brg, s[5] - '0', s[6] - '0', s[7] - '0');
        return true;
    }

    /// <summary><c>Tyy/Cxx</c>, where <c>/C</c> is <c>/0</c>-<c>/9</c> or <c>10</c>-<c>15</c> for colours 10-15.</summary>
    private static bool TryReadAreaObject(ReadOnlySpan<byte> s, out AprsAreaObject area)
    {
        area = default;
        if (!Text.IsDigit(s[0]) || !Text.AllDigits(s.Slice(1, 2)) || !Text.AllDigits(s.Slice(5, 2)) || !Text.IsDigit(s[4]))
        {
            return false;
        }

        int color;
        if (s[3] == (byte)'/')
        {
            color = s[4] - '0';
        }
        else if (s[3] == (byte)'1' && s[4] <= (byte)'5')
        {
            color = 10 + (s[4] - '0');
        }
        else
        {
            return false;
        }

        area = new AprsAreaObject((AprsAreaShape)(s[0] - '0'), (AprsAreaColor)color, Text.ParseDigits(s.Slice(1, 2)), Text.ParseDigits(s.Slice(5, 2)));
        return true;
    }

    // ---------------------------------------------------------------- encoding

    public static void WriteBody(InfoWriter w, AprsPositionedData d)
    {
        w.Field(d.Position.Validate);
        w.Field(d.Symbol.Validate);
        if (d.Symbol.IsWeatherStation && d.Weather is null)
        {
            // A report with the weather station symbol is weather even with no fields (vectors
            // README, Weather), so without them it would read back as incomplete weather.
            w.Defer(AprsRefusalCode.SymbolMismatch, "the weather station symbol (_) makes the report a weather report; set Weather (APRS12c ch. 12)");
        }

        if (d.IsCompressed)
        {
            w.Field(() => WriteCompressed(w, d));
        }
        else
        {
            w.Field(() => WriteUncompressed(w, d.Position, d.Symbol, d.Dao));
            w.Field(() => WriteDataExtension(w, d));
        }

        if (d.Weather is { } weather)
        {
            if (!d.Symbol.IsWeatherStation)
            {
                w.Defer(AprsRefusalCode.SymbolMismatch, "weather data needs a weather-station symbol (_) (APRS12c ch. 12)");
            }

            w.Field(() => WeatherCodec.WriteFields(w, weather, positionless: false));
            if (d.Comment.Length > 0)
            {
                w.Defer(AprsRefusalCode.FieldNotAllowed, "a weather report has no comment field (APRS12c ch. 12, UAP 2.7.1)");
            }

            CommentCodec.WriteTrailer(w, d);
            return;
        }

        CommentCodec.Write(w, d, altitudeInCs: AltitudeFitsInCs(d));
    }

    /// <summary>
    /// With a GGA compression type the cs bytes carry altitude, but only to 0.2% (1.002^cs feet).
    /// An altitude that is not exactly representable there is also written as <c>/A=</c>, which
    /// the decoder prefers, so it round-trips.
    /// </summary>
    internal static bool AltitudeFitsInCs(AprsPositionedData d)
    {
        if (!d.IsCompressed || d.CompressionType?.Source != AprsCompressionSource.Gga || d.AltitudeFeet is not { } alt || alt < 1)
        {
            return false;
        }

        long cs = Steps.Count(Math.Log(alt) / Math.Log(1.002));
        return Math.Abs(Math.Pow(1.002, cs) - alt) <= 1e-9 * alt;
    }

    internal static void WriteUncompressed(InfoWriter w, AprsPosition position, AprsSymbol symbol, AprsDao? dao)
    {
        (string lat, string lon) = FormatUncompressed(position, dao);
        w.Ascii(lat).Char(symbol.Table).Ascii(lon).Char(symbol.Code);
    }

    /// <summary>Formats DDMM.hhN / DDDMM.hhW with ambiguity blanks. With a DAO the position is
    /// rounded to the DAO's finer unit first so the DAO digits and these digits stay consistent.</summary>
    internal static (string Latitude, string Longitude) FormatUncompressed(AprsPosition position, AprsDao? dao)
    {
        string lat = FormatCoordinate(Math.Abs(position.Latitude), 2, dao, position.Ambiguity) + (double.IsNegative(position.Latitude) ? "S" : "N");
        string lon = FormatCoordinate(Math.Abs(position.Longitude), 3, dao, position.Ambiguity) + (double.IsNegative(position.Longitude) ? "W" : "E");
        if (position.Ambiguity > 0 && (lat.StartsWith("90", StringComparison.Ordinal) || lon.StartsWith("180", StringComparison.Ordinal)))
        {
            // A decoder reports the centre of the blanked box, which from 90 or 180 degrees lies past
            // the pole or the date line, and rejects it (vectors README, Positions).
            throw new AprsEncodeException(AprsRefusalCode.ValueHasNoForm, "an ambiguous position at 90 degrees latitude or 180 degrees longitude is centred past it, which a decoder rejects");
        }

        return (Blank(lat, position.Ambiguity), Blank(lon, position.Ambiguity));

        static string Blank(string coordinate, int ambiguity)
        {
            // Blank from the hundredths backwards, skipping the decimal point: hh then mm.
            char[] c = coordinate.ToCharArray();
            int[] order = [c.Length - 2, c.Length - 3, c.Length - 5, c.Length - 6];
            for (int i = 0; i < ambiguity; i++)
            {
                c[order[i]] = ' ';
            }

            return new string(c);
        }
    }

    /// <summary>
    /// DDMM.hh or DDDMM.hh for an absolute coordinate. On an ambiguous position the digits are cut,
    /// not rounded, so the box they leave is the one that holds the position (vectors ruling B3:
    /// 03.596 minutes with one level is the box 03.5x); a decoder's centre cuts to its own box. As
    /// for a half, a value within the tolerance of a step counts as that step (132.89 degrees is
    /// 53.40 minutes, though 132.89 x 6000 is 797339.99999 in binary).
    /// </summary>
    internal static string FormatCoordinate(double degrees, int degreeDigits, AprsDao? dao, int ambiguity = 0)
    {
        long hundredths = ambiguity > 0 ? Steps.Cut(degrees, 6000) : DaoCodec.SplitForDao(degrees, dao, out _);
        long whole = hundredths / 6000;
        long rem = hundredths % 6000;
        return whole.ToString(CultureInfo.InvariantCulture).PadLeft(degreeDigits, '0')
            + (rem / 100).ToString("00", CultureInfo.InvariantCulture) + "."
            + (rem % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The position compressed bytes report for <paramref name="position"/>: the nearest step,
    /// as <see cref="WriteCompressed"/> writes it and a decoder reads it back.
    /// </summary>
    internal static AprsPosition CompressedReported(AprsPosition position)
    {
        long y = Math.Min(Steps.Count(90 - position.Latitude, LatitudeScale), Base91.MaxValue(4));
        long x = Math.Min(Steps.Count(180 + position.Longitude, LongitudeScale), Base91.MaxValue(4));
        return new AprsPosition(90 - (y / LatitudeScale), -180 + (x / LongitudeScale));
    }

    private static void WriteCompressed(InfoWriter w, AprsPositionedData d)
    {
        if (d.Position.Ambiguity != 0)
        {
            w.Defer(AprsRefusalCode.FieldNotAllowed, "a compressed position cannot carry position ambiguity (APRS12c ch. 9)");
        }

        char table = d.Symbol.Table is >= '0' and <= '9' ? (char)('a' + (d.Symbol.Table - '0')) : d.Symbol.Table;
        long y = Steps.Count(90 - d.Position.Latitude, LatitudeScale);
        long x = Steps.Count(180 + d.Position.Longitude, LongitudeScale);
        w.Char(table).Base91(Math.Min(y, Base91.MaxValue(4)), 4).Base91(Math.Min(x, Base91.MaxValue(4)), 4).Char(d.Symbol.Code);

        if (d.Phg is not null || d.DfSignalStrength is not null || d.AreaObject is not null || d.DfBearing is not null || d.Storm is not null)
        {
            w.Defer(AprsRefusalCode.FieldNotAllowed, "a compressed position cannot carry a 7-byte data extension (PHG, DFS, area, DF bearing, storm) (APRS12c ch. 9)");
        }

        var type = d.CompressionType;
        if (d.RangeMiles is { } rangeMiles)
        {
            // A range outside the format is a fault of its own, whatever else is given.
            w.Field(() => RangeStep(rangeMiles));
        }

        // So are a course and a speed, and a GGA altitude, before anything else is found to want
        // the cs bytes or a companion is found missing (vectors README, Refusal reasons).
        w.Field(() => CheckCompressedCourseSpeed(d.CourseDegrees, d.SpeedKnots));
        if (type?.Source == AprsCompressionSource.Gga && d.AltitudeFeet is { } ggaAltitude)
        {
            w.Field(() => GgaStep(ggaAltitude));
        }

        bool windInCs = d.Weather is { } weather && (weather.WindDirectionDegrees is not null || weather.WindSpeedMph is not null);
        // A course and speed beside weather want the bytes that hold its wind even when no wind is
        // given; a range is written there when they hold no wind (vectors encode/compressed-weather-
        // with-a-course, corpus/1149).
        if ((windInCs && (type?.Source == AprsCompressionSource.Gga || d.RangeMiles is not null))
            || (d.Weather is not null && (d.CourseDegrees is not null || d.SpeedKnots is not null)))
        {
            // One pair of cs bytes carries wind, course/speed, a range or a GGA altitude, never
            // two of them (APRS12c ch. 9); writing one would drop the other.
            w.Defer(AprsRefusalCode.ExtensionConflict, "a compressed weather position carries its wind in the cs bytes, so it cannot also carry course/speed, a range or a GGA altitude (APRS12c ch. 9)");
        }

        if (windInCs && d.Weather is { } wx)
        {
            // The cs bytes carry a direction and a speed together, with no way to say either is
            // unknown, so wind with only one of them known is refused rather than written as 0.
            if (wx.WindDirectionDegrees is null || wx.WindSpeedMph is null)
            {
                NotANumber.Refuse(wx.WindDirectionDegrees, "wind direction");
                NotANumber.Refuse(wx.WindSpeedMph, "wind speed");

                // The one given is still checked on its own (vectors README, Refusal reasons).
                if (wx.WindDirectionDegrees is { } direction && Steps.Whole(direction) is < 0 or > 360)
                {
                    throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "a wind direction is 0-360 degrees");
                }

                if (wx.WindSpeedMph is { } speed)
                {
                    _ = SpeedCode(Units.MphToKnots(speed));
                }

                w.Defer(AprsRefusalCode.MissingField, "the cs bytes of a compressed weather position carry wind direction and speed together; one without the other cannot be written (APRS12c ch. 9)");
                w.Ascii(" sT");
                return;
            }

            // Checked rounded to whole degrees, then rounded once to the 4-degree step below.
            NotANumber.Refuse(wx.WindDirectionDegrees, "wind direction");
            NotANumber.Refuse(wx.WindSpeedMph, "wind speed");
            if (Steps.Whole(wx.WindDirectionDegrees.Value) is < 0 or > 360)
            {
                throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "a wind direction is 0-360 degrees");
            }

            // Rounded to the nearest 4 degrees, halves away from zero; 360 is written as north,
            // c = 0 (vectors ruling E9).
            int c = (int)(Steps.Count(wx.WindDirectionDegrees.Value / 4.0) % 90);
            int s = SpeedCode(Units.MphToKnots(wx.WindSpeedMph.Value));
            w.Byte((byte)(c + 33)).Byte((byte)(s + 33));
            WriteType(w, type ?? AprsCompressionType.Default);
        }
        else if (type?.Source == AprsCompressionSource.Gga && d.AltitudeFeet is { } alt)
        {
            if (d.CourseDegrees is not null || d.SpeedKnots is not null || d.RangeMiles is not null)
            {
                w.Defer(AprsRefusalCode.ExtensionConflict, "with a GGA compression type the cs bytes carry altitude, so course/speed and range cannot also be sent");
            }

            w.Base91(GgaStep(alt), 2);
            WriteType(w, type.Value);
        }
        else if (type?.Source == AprsCompressionSource.Gga && d.AltitudeFeet is null)
        {
            w.Defer(AprsRefusalCode.MissingField, "a GGA compression type means the cs bytes carry altitude; set AltitudeFeet or choose another source");
            w.Ascii(" sT");
        }
        else if (d.CourseDegrees is not null || d.SpeedKnots is not null)
        {
            if (d.RangeMiles is not null)
            {
                w.Defer(AprsRefusalCode.ExtensionConflict, "a compressed position carries either course/speed or radio range, not both (APRS12c ch. 9)");
            }

            if (d.CourseDegrees is not { } course)
            {
                NotANumber.Refuse(d.SpeedKnots, "speed");
                w.Defer(AprsRefusalCode.MissingField, "a compressed course/speed needs a course 1-360; compressed form has no 'unknown' course (APRS12c ch. 9)");
                w.Ascii(" sT");
                return;
            }

            NotANumber.Refuse(course, "course");
            NotANumber.Refuse(d.SpeedKnots, "speed");
            if (Steps.Whole(course) is < 0 or > 360)
            {
                throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "a compressed course/speed needs a course 1-360; compressed form has no 'unknown' course (APRS12c ch. 9)");
            }

            if (Steps.Whole(course) < 1)
            {
                throw new AprsEncodeException(AprsRefusalCode.ValueHasNoForm, "a compressed course/speed needs a course 1-360; compressed form has no 'unknown' course (APRS12c ch. 9)");
            }

            if (d.SpeedKnots is null)
            {
                // The cs bytes hold a course and a speed together, so a course alone would add a
                // speed the data does not have (vectors ruling F5).
                w.Defer(AprsRefusalCode.MissingField, "the cs bytes of a compressed position hold a course and a speed together; a course without a speed cannot be written (APRS12c ch. 9)");
                w.Ascii(" sT");
                return;
            }

            // Rounded to the nearest 4 degrees, halves away from zero; a course that rounds to 360
            // is north, c = 0 (vectors ruling E9).
            w.Byte((byte)((int)(Steps.Count(course / 4.0) % 90) + 33)).Byte((byte)(SpeedCode(d.SpeedKnots.Value) + 33));
            WriteType(w, type ?? AprsCompressionType.Default);
        }
        else if (d.RangeMiles is { } range)
        {
            // 2 x 1.08^s miles (APRS12c ch. 9): a range is rounded to the nearest step, but one
            // outside the format is refused, not moved to its edge (vectors interpretations.md,
            // "Re-encoding into compressed bytes rounds").
            int s = RangeStep(range);

            w.Char('{').Byte((byte)(s + 33));
            WriteType(w, type ?? AprsCompressionType.Default);
        }
        else if (type is not null)
        {
            // Blank cs bytes carry no T byte, so the compression type would be lost (vectors
            // interpretations.md, "Re-encoding into compressed bytes rounds": a value the cs bytes
            // cannot hold is refused).
            w.Defer(AprsRefusalCode.MissingField, "a compression type needs cs data to carry it: course/speed, range, a GGA altitude or wind (APRS12c ch. 9)");
            w.Ascii(" sT");
        }
        else
        {
            // No cs data: a space, then filler (the spec's example uses "sT").
            w.Ascii(" sT");
        }
    }

    private static long GgaStep(double alt)
    {
        long cs = alt <= 1 ? 0 : Steps.Count(Math.Log(alt) / Math.Log(1.002));
        return cs > (91 * 91) - 1
            ? throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "altitude too high for compressed form")
            : cs;
    }

    /// <summary>A compressed course and speed checked on their own: each in its range, then a course that has a form.</summary>
    private static void CheckCompressedCourseSpeed(double? course, double? speed)
    {
        NotANumber.Refuse(course, "course");
        NotANumber.Refuse(speed, "speed");
        if (course is { } c && Steps.Whole(c) is < 0 or > 360)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "a compressed course/speed needs a course 1-360; compressed form has no 'unknown' course (APRS12c ch. 9)");
        }

        if (speed is { } s)
        {
            _ = SpeedCode(s);
        }

        if (course is { } c1 && Steps.Whole(c1) < 1)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueHasNoForm, "a compressed course/speed needs a course 1-360; compressed form has no 'unknown' course (APRS12c ch. 9)");
        }
    }

    private static int RangeStep(double range)
    {
        NotANumber.Refuse(range, "range");
        int s = range <= 0 ? -1 : (int)Math.Max(-1, Steps.Count(Math.Log(range / 2) / Math.Log(1.08)));
        return s is < 0 or > 90
            ? throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "a compressed range is 2 to 2 x 1.08^90 miles (APRS12c ch. 9)")
            : s;
    }

    private static void WriteType(InfoWriter w, AprsCompressionType type) => w.Byte((byte)(type.Bits + 33));

    /// <summary>
    /// The s byte's value for a speed of 1.08^s - 1 knots, rounded to the nearest step, halves away
    /// from zero (vectors ruling E9). Every s from 0 to 90 (<c>!</c> to <c>{</c>) is a speed
    /// (APRS12c ch. 9 limits only the course byte, vectors ruling E10).
    /// </summary>
    private static int SpeedCode(double knots)
    {
        // The steps follow 1.08^s - 1 for every whole s; a speed rounds to the nearest, and one
        // whose s is outside the byte is refused: -0.03 knots is s = 0, -0.3 is s = -5 (vectors
        // ruling G3).
        long s = knots == 0 ? 0 : knots > -1 ? Steps.Count(Math.Log(knots + 1) / Math.Log(1.08)) : -1;
        NotANumber.Refuse(knots, "speed");
        if (s < 0)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "a compressed speed is 0 to 1.08^90 - 1 knots");
        }

        return s > 90 ? throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "speed too high for compressed form") : (int)s;
    }

    private static void WriteDataExtension(InfoWriter w, AprsPositionedData d)
    {
        bool courseSpeed = d.CourseDegrees is not null || d.SpeedKnots is not null || d.DfBearing is not null || d.Storm is not null;
        // A course or speed outside its field is a fault of its own, whatever else is given, the
        // wind of a weather report wanting the same place included.
        w.Field(() => CheckCourseSpeed(d.CourseDegrees, d.SpeedKnots));
        int count = (courseSpeed ? 1 : 0) + (d.Phg is null ? 0 : 1) + (d.RangeMiles is null ? 0 : 1)
            + (d.DfSignalStrength is null ? 0 : 1) + (d.AreaObject is null ? 0 : 1);
        if (count > 1 || (count > 0 && d.Weather is not null) || (d.DfBearing is not null && d.Storm is not null))
        {
            // Each extension has its place, so each is checked on its own before the two are found
            // to want it at once: a PHG digit of 10 beside a course is value-out-of-range (vectors
            // README, Refusal reasons).
            CheckExtensions(w, d);
        }

        if (count > 1)
        {
            w.Defer(AprsRefusalCode.ExtensionConflict, "only one data extension (course/speed, PHG, RNG, DFS or area object) fits in a report (APRS12c ch. 7)");
        }

        if (d.Weather is not null)
        {
            if (count > 0)
            {
                w.Defer(AprsRefusalCode.ExtensionConflict, "in a weather report the data extension carries wind; course/speed, PHG, RNG, DFS and area cannot be sent");
            }

            WeatherCodec.WriteWind(w, d.Weather);
            return;
        }

        if (d.AreaObject is { } area)
        {
            area.Validate();
            if (d.Symbol is not { Table: '\\', Code: 'l' })
            {
                w.Defer(AprsRefusalCode.SymbolMismatch, "an area object needs the \\l symbol (APRS12c ch. 11)");
            }

            int color = (int)area.Color;
            w.Digits((int)area.Shape, 1).Digits(area.LatitudeOffsetCode, 2);
            _ = color < 10 ? w.Char('/').Digits(color, 1) : w.Digits(color, 2);
            w.Digits(area.LongitudeOffsetCode, 2);
        }
        else if (courseSpeed)
        {
            if (d.Symbol is { Table: '\\', Code: 'l' } && d.CourseDegrees is not null)
            {
                // After the area symbol, ddd/ddd reads as an area object (APRS12c ch. 11).
                w.Defer(AprsRefusalCode.SymbolMismatch, "a course and speed after the area symbol (\\l) would read back as an area object (APRS12c ch. 11)");
            }

            if (d.DfBearing is not null && d.Storm is not null)
            {
                // Both extend the one course/speed, and only one can follow it.
                w.Defer(AprsRefusalCode.ExtensionConflict, "a DF bearing and storm data both extend the course/speed; only one can follow it");
            }

            WriteCourseSpeed(w, d.CourseDegrees, d.SpeedKnots);
            if (d.DfBearing is { } bearing)
            {
                bearing.Validate();
                if (d.Symbol is not { Table: '/', Code: '\\' })
                {
                    w.Defer(AprsRefusalCode.SymbolMismatch, "a DF bearing needs the /\\ DF symbol (APRS12c ch. 8)");
                }

                w.Char('/').Digits(bearing.BearingDegrees, 3).Char('/').Digits(bearing.Number, 1).Digits(bearing.Range, 1).Digits(bearing.Quality, 1);
            }

            if (d.Storm is { } storm)
            {
                if (d.Symbol.Code != '@')
                {
                    w.Defer(AprsRefusalCode.SymbolMismatch, "storm data needs a hurricane symbol (@) (APRS12c ch. 12)");
                }

                StormCodec.Write(w, storm);
            }
        }
        else if (d.Phg is { } phg)
        {
            WritePhg(w, phg);
        }
        else if (d.RangeMiles is { } range)
        {
            w.Ascii("RNG").Digits((int)RangeMilesUncompressed(range), 4);
        }
        else if (d.DfSignalStrength is { } dfs)
        {
            dfs.Validate();
            w.Ascii("DFS").Digits(dfs.StrengthCode, 1).Char((char)('0' + dfs.HeightCode)).Digits(dfs.GainCode, 1).Digits(dfs.DirectivityCode, 1);
        }
    }

    /// <summary>The checks each 7-byte data extension given has of its own, whatever else is given.</summary>
    private static void CheckExtensions(InfoWriter w, AprsPositionedData d)
    {
        if (d.Phg is { } phg)
        {
            w.Field(phg.Validate);
        }

        if (d.DfSignalStrength is { } dfs)
        {
            w.Field(dfs.Validate);
        }

        if (d.AreaObject is { } area)
        {
            w.Field(area.Validate);
        }

        if (d.DfBearing is { } bearing)
        {
            w.Field(bearing.Validate);
        }

        if (d.Storm is { } storm)
        {
            // Each value must fit its digits; written to a scratch writer only to check them.
            w.Field(() => StormCodec.Write(new InfoWriter(), storm));
        }

        if (d.RangeMiles is { } range)
        {
            w.Field(() => _ = RangeMilesUncompressed(range));
        }
    }

    private static double RangeMilesUncompressed(double range)
    {
        // Rounded to whole miles, then checked (vectors ruling F2).
        NotANumber.Refuse(range, "RNG");
        double miles = Steps.Whole(range);
        return miles is < 0 or > 9999
            ? throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "RNG carries whole miles 0-9999")
            : miles;
    }

    internal static void WritePhg(InfoWriter w, AprsPhg phg)
    {
        phg.Validate();
        w.Ascii("PHG").Digits(phg.PowerCode, 1).Char((char)('0' + phg.HeightCode)).Digits(phg.GainCode, 1).Digits(phg.DirectivityCode, 1);
        if (phg.BeaconsPerHour is { } rate)
        {
            w.Char(rate <= 9 ? (char)('0' + rate) : (char)('A' + rate - 10)).Char('/');
        }
    }

    private static (int? Degrees, double? Knots) CheckCourseSpeed(double? course, double? speed)
    {
        NotANumber.Refuse(course, "course");
        NotANumber.Refuse(speed, "speed");

        // Rounded to whole degrees, then checked (vectors ruling F2).
        int? degrees = course is { } cd ? (Steps.Whole(cd) is < 0 or > 360 ? throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "course must be 0-360") : (int)Steps.Whole(cd)) : null;

        // Rounded to whole knots, then checked (vectors ruling F2).
        double? knots = speed is { } s ? Steps.Whole(s) : null;
        return knots is < 0 or > 999
            ? throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "an uncompressed speed is whole knots 0-999")
            : (degrees, knots);
    }

    internal static void WriteCourseSpeed(InfoWriter w, double? course, double? speed)
    {
        (int? degrees, double? knots) = CheckCourseSpeed(course, speed);

        _ = degrees is { } c ? w.Digits(c, 3) : w.Ascii("...");
        w.Char('/');
        _ = knots is { } v ? w.Digits((int)v, 3) : w.Ascii("...");
    }
}
