using System.Globalization;

namespace Packet.Aprs.Internal;

/// <summary>Weather data fields (APRS12c §12 Weather Data).</summary>
internal static class WeatherCodec
{
    /// <summary>
    /// Decides the wind where the DIR/SPD extension of an uncompressed complete weather report
    /// belongs, before any field is read (vectors README, The order of checks): the extension
    /// itself; wind sent as <c>c</c>/<c>s</c> fields instead (tolerated), in which case
    /// <paramref name="windAsFields"/> tells the field reader to take them; or no wind at all.
    /// </summary>
    public static bool TryReadWind(ReadOnlySpan<byte> info, ref int pos, DecodeContext ctx, ref AprsWeather weather, out bool windAsFields)
    {
        windAsFields = false;
        if (info.Length >= pos + 7 && PositionCodec.IsCourseSpeed(info.Slice(pos, 7)))
        {
            return TryReadWindExtension(info, ref pos, ctx, ref weather);
        }

        if (WindComesAsFields(info, pos, ctx))
        {
            windAsFields = ctx.Tolerate(
                ctx.Options.AllowWindFieldsInsteadOfExtension,
                AprsDiagnosticCode.WindFieldsInsteadOfExtension,
                "wind sent as c/s fields instead of the DIR/SPD extension (APRS12c ch. 12)",
                pos);
            return windAsFields;
        }

        return ctx.Tolerate(
            ctx.Options.AllowIncompleteWeather,
            AprsDiagnosticCode.IncompleteWeather,
            "weather report has no wind direction/speed extension (APRS12c ch. 12)",
            pos);
    }

    /// <summary>
    /// Whether the field run at <paramref name="pos"/> carries a wind direction: a <c>c</c> with a
    /// value after it, wherever it comes before the run ends (vectors interpretations.md, "Which
    /// weather field a letter is"). Judged on the fields alone, whatever the options, so that
    /// lenient and strict decoding agree on what the report is.
    /// </summary>
    public static bool WindComesAsFields(ReadOnlySpan<byte> info, int pos, DecodeContext ctx)
    {
        var probe = new DecodeContext(AprsParseOptions.Lenient) { Depth = ctx.Depth };
        var weather = new AprsWeather();
        return TryReadRun(info, ref pos, probe, ref weather, windAsFields: true, positionless: false, out bool direction, out _, out _) && direction;
    }

    /// <summary>Reads a 7-byte <c>DDD/SSS</c> wind extension at <paramref name="pos"/>.</summary>
    public static bool TryReadWindExtension(ReadOnlySpan<byte> info, ref int pos, DecodeContext ctx, ref AprsWeather weather)
    {
        ReadOnlySpan<byte> ext = info.Slice(pos, 7);
        int? dir = Text.AllDigits(ext[..3]) ? Text.ParseDigits(ext[..3]) : null;
        int? speed = Text.AllDigits(ext.Slice(4, 3)) ? Text.ParseDigits(ext.Slice(4, 3)) : null;
        if (dir > 360)
        {
            if (!ctx.Tolerate(ctx.Options.AllowOutOfRangeValue, AprsDiagnosticCode.OutOfRangeValue, "wind direction is over 360 degrees; ignored", pos))
            {
                return false;
            }

            dir = null;
        }

        weather = weather with { WindDirectionDegrees = dir, WindSpeedMph = speed };
        pos += 7;
        return true;
    }

    /// <summary>
    /// Reads letter-plus-value weather fields and checks the report has the ones it must. With
    /// <paramref name="windAsFields"/> (always for a positionless report) the wind comes as fields:
    /// <c>c</c> is the wind direction until it is read, wherever it comes, and <c>s</c> the wind
    /// speed until it is read, then snowfall. A positionless report's wind is fields from the
    /// start; a position, object or item report's only once a <c>c</c> has been read, so an
    /// <c>s</c> before any <c>c</c> there is snowfall. Otherwise the wind is already known (or
    /// missing), so <c>c</c> ends the fields and <c>s</c> is snowfall. With <paramref name="windMustBeComplete"/>
    /// a wind sent as fields needs both <c>c</c> and <c>s</c> (APRS12c ch. 12: the report "must
    /// include at least ... wind direction, wind speed, gust and temperature").
    /// </summary>
    public static bool TryReadFields(ReadOnlySpan<byte> info, ref int pos, DecodeContext ctx, ref AprsWeather weather, bool positionless, bool windAsFields = false, bool windMustBeComplete = false)
    {
        int start = pos;
        if (!TryReadRun(info, ref pos, ctx, ref weather, positionless || windAsFields, positionless, out bool direction, out bool speed, out HashSet<char> seen))
        {
            return false;
        }

        if (positionless)
        {
            return seen.Contains('c') && seen.Contains('s') && seen.Contains('g') && seen.Contains('t')
                || ctx.Tolerate(ctx.Options.AllowIncompleteWeather, AprsDiagnosticCode.IncompleteWeather, "positionless weather report must start with c, s, g and t fields (APRS12c ch. 12)", start);
        }

        if (windAsFields && windMustBeComplete && !(direction && speed) && !ctx.Tolerate(
                ctx.Options.AllowIncompleteWeather,
                AprsDiagnosticCode.IncompleteWeather,
                "wind sent as fields needs both the direction (c) and the speed (s) (APRS12c ch. 12)",
                start))
        {
            return false;
        }

        return seen.Contains('g') && seen.Contains('t')
            || ctx.Tolerate(ctx.Options.AllowIncompleteWeather, AprsDiagnosticCode.IncompleteWeather, "weather report must include gust (g) and temperature (t) fields (APRS12c ch. 12)", start);
    }

    /// <summary>
    /// The fields are one contiguous run, which ends at the first thing that is not a field, at a
    /// defined field already read (<c>L</c> and <c>l</c> are one field, luminosity), and at <c>c</c>
    /// once the wind is known. Extra fields are kept as a list, so a repeated extra letter does not
    /// end it (vectors interpretations.md, "Which weather field a letter is").
    /// </summary>
    private static bool TryReadRun(ReadOnlySpan<byte> info, ref int pos, DecodeContext ctx, ref AprsWeather weather, bool windAsFields, bool positionless, out bool direction, out bool speed, out HashSet<char> seen)
    {
        seen = [];
        direction = false;
        speed = false;
        var additional = new List<AprsWeatherField>();

        decimal? fieldValue = null;
        int fieldLength = 0;
        while (pos < info.Length)
        {
            char letter = (char)info[pos];
            bool snow = letter == 's' && (!windAsFields || speed || !(positionless || direction));
            char key = letter switch
            {
                'l' => 'L',
                's' when snow => 'S',
                _ => letter,
            };
            int width = letter switch
            {
                'c' when windAsFields => 3,
                's' or 'g' or 't' or 'r' or 'p' or 'P' or 'L' or 'l' or '#' => 3,
                'h' => 2,
                'b' => 5,
                _ => 0,
            };

            // A fixed-width value whose digits run on by two or more, then the end of the information
            // field or a defined field the run goes on to read: out of range, dropped, and the fields
            // carry on after the run (vectors README, Weather). The dropped value was sent, so it counts
            // as present.
            if (width > 0 && !snow && !seen.Contains(key) && RunOnLength(info, pos, width, letter) is var runOn and > 0
                && RunOnIsFollowedByAField(info, pos + 1 + runOn, seen, key, windAsFields, positionless, direction || letter == 'c', speed || letter == 's'))
            {
                if (!ctx.Tolerate(
                        ctx.Options.AllowOutOfRangeValue,
                        AprsDiagnosticCode.OutOfRangeValue,
                        $"weather field '{letter}' has {runOn} characters instead of {width}, then more fields; the value is ignored (APRS12c ch. 12)",
                        pos))
                {
                    return false;
                }

                seen.Add(key);
                direction |= letter == 'c';
                speed |= letter == 's';
                pos += 1 + runOn;
                continue;
            }

            FieldResult result = width > 0 && !seen.Contains(key)
                ? TryFieldValue(info, pos, width, letter, snow, ctx, out fieldValue, out fieldLength)
                : FieldResult.NotAField;
            if (result == FieldResult.Abort)
            {
                return false;
            }

            if (result == FieldResult.Field)
            {
                seen.Add(key);
                if (!Assign(ref weather, letter, snow, fieldValue, pos, ctx))
                {
                    return false;
                }

                direction |= letter == 'c';
                speed |= letter == 's' && !snow;
                pos += 1 + fieldLength;
                continue;
            }

            if (Text.IsUpper((byte)letter) || Text.IsLower((byte)letter) || letter == '#')
            {
                int end = pos + 1;
                while (end < info.Length && (Text.IsDigit(info[end]) || info[end] is (byte)'.' or (byte)'-'))
                {
                    end++;
                }

                bool looksLikeField = end - pos >= 3 && Text.IsDigit(info[end - 1]) && !IsKnown(letter);
                if (looksLikeField)
                {
                    additional.Add(new AprsWeatherField(letter, Text.Latin1(info[(pos + 1)..end])));
                    pos = end;
                    continue;
                }
            }

            break;
        }

        if (additional.Count > 0)
        {
            weather = weather with { AdditionalFields = additional };
        }

        return true;

        static bool IsKnown(char c) => c is 'c' or 's' or 'g' or 't' or 'r' or 'p' or 'P' or 'h' or 'b' or 'L' or 'l' or '#';
    }

    /// <summary>
    /// The characters after the letter that a fixed-width field's value runs on to (an optional
    /// <c>-</c> for temperature, then digits) when they are <paramref name="width"/> + 2 or more, else 0.
    /// </summary>
    private static int RunOnLength(ReadOnlySpan<byte> info, int pos, int width, char letter)
    {
        int i = pos + 1;
        if (letter == 't' && i < info.Length && info[i] == (byte)'-')
        {
            i++;
        }

        while (i < info.Length && Text.IsDigit(info[i]))
        {
            i++;
        }

        int length = i - (pos + 1);
        return length >= width + 2 ? length : 0;
    }

    /// <summary>
    /// Whether the run ends the information field, or goes on to a defined field not yet read, with
    /// a value (the field the loop would read next), which tells a run-on value from a comment.
    /// </summary>
    private static bool RunOnIsFollowedByAField(ReadOnlySpan<byte> info, int after, HashSet<char> seen, char runKey, bool windAsFields, bool positionless, bool direction, bool speed)
    {
        if (after >= info.Length)
        {
            return true;
        }

        char next = (char)info[after];
        bool snow = next == 's' && (!windAsFields || speed || !(positionless || direction));
        char key = next switch
        {
            'l' => 'L',
            's' when snow => 'S',
            _ => next,
        };
        int width = next switch
        {
            'c' when windAsFields => 3,
            's' or 'g' or 't' or 'r' or 'p' or 'P' or 'L' or 'l' or '#' => 3,
            'h' => 2,
            'b' => 5,
            _ => 0,
        };

        return width > 0 && key != runKey && !seen.Contains(key)
            && TryFieldValue(info, after, width, next, snow, new DecodeContext(AprsParseOptions.Lenient), out _, out _) == FieldResult.Field;
    }

    private enum FieldResult
    {
        NotAField,
        Field,
        Abort,
    }

    /// <summary>
    /// A field value: exactly <paramref name="width"/> characters (APRS12c §12), or, tolerated, a
    /// run of 1 to width+1 digits or dots that ends at a non-digit (<c>t45</c>, <c>h070</c>, <c>b...</c>).
    /// The spec width is tried first, and a spec-width value that runs on into more digits falls
    /// back to it only if no shorter/longer reading fits.
    /// </summary>
    private static FieldResult TryFieldValue(ReadOnlySpan<byte> info, int pos, int width, char letter, bool snow, DecodeContext ctx, out decimal? value, out int consumed)
    {
        value = null;
        consumed = width;
        int avail = info.Length - pos - 1;
        bool exactOk = avail >= width && TryValue(info.Slice(pos + 1, width), letter, snow, out value);
        bool followedByDigit = avail > width && Text.IsDigit(info[pos + 1 + width]);

        // Snowfall keeps its width of three characters: a digit after s1.5 could as well be a
        // stray character as a fourth figure, so it is left as text (vectors interpretations.md,
        // "Which weather field a letter is").
        if (exactOk && (!followedByDigit || snow))
        {
            return FieldResult.Field;
        }

        // Variable width: a run of digits (with a leading '-' for temperature) or of dots. For
        // snowfall only a run of dots may be shorter, and a value that holds a digit (.5) is a
        // number, not a run of dots.
        int run = 0;
        bool dots = avail > 0 && info[pos + 1] == (byte)'.';
        if (snow && dots)
        {
            int after = pos + 1;
            while (after < info.Length && info[after] == (byte)'.')
            {
                after++;
            }

            dots = after >= info.Length || !Text.IsDigit(info[after]);
        }

        bool minus = letter == 't' && avail > 0 && info[pos + 1] == (byte)'-';
        int i = pos + 1 + (minus ? 1 : 0);
        while (i < info.Length && (dots ? info[i] == (byte)'.' : Text.IsDigit(info[i])))
        {
            run++;
            i++;
        }

        int length = run + (minus ? 1 : 0);
        if (run >= 1 && length != width && length <= width + 1 && !(snow && !dots))
        {
            if (!ctx.Tolerate(
                    ctx.Options.AllowNonStandardWeatherFieldWidth,
                    AprsDiagnosticCode.NonStandardWeatherFieldWidth,
                    $"weather field '{letter}' has {length} characters instead of {width} (APRS12c ch. 12, UAP 5.31)",
                    pos))
            {
                return FieldResult.Abort;
            }

            consumed = length;
            value = dots ? null : (minus ? -1 : 1) * Text.ParseDigits(info.Slice(pos + 1 + (minus ? 1 : 0), run));
            return FieldResult.Field;
        }

        return exactOk ? FieldResult.Field : FieldResult.NotAField;
    }

    private static bool TryValue(ReadOnlySpan<byte> v, char letter, bool snow, out decimal? value)
    {
        value = null;
        if (v.SequenceEqual("..........."u8[..v.Length]) || v.SequenceEqual("           "u8[..v.Length]))
        {
            return true;
        }

        if (Text.AllDigits(v))
        {
            value = Text.ParseDigits(v);
            return true;
        }

        if (letter == 't' && v[0] == (byte)'-' && Text.AllDigits(v[1..]))
        {
            value = -Text.ParseDigits(v[1..]);
            return true;
        }

        if (snow && v.Count((byte)'.') == 1 && decimal.TryParse(Text.Latin1(v), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal d))
        {
            value = d;
            return true;
        }

        return false;
    }

    private static bool Assign(ref AprsWeather w, char letter, bool snow, decimal? v, int pos, DecodeContext ctx)
    {
        int? i = v is { } d ? (int)d : null;
        switch (letter)
        {
            case 'c':
                if (i > 360)
                {
                    if (!ctx.Tolerate(ctx.Options.AllowOutOfRangeValue, AprsDiagnosticCode.OutOfRangeValue, "wind direction is over 360 degrees; ignored", pos))
                    {
                        return false;
                    }

                    i = null;
                }

                w = w with { WindDirectionDegrees = i };
                break;
            case 's' when !snow:
                w = w with { WindSpeedMph = i };
                break;
            case 's':
                w = w with { SnowfallLast24HoursInches = (double?)v };
                break;
            case 'g':
                w = w with { WindGustMph = i };
                break;
            case 't':
                w = w with { TemperatureFahrenheit = i };
                break;
            case 'r':
                w = w with { RainLastHourInches = i / 100.0 };
                break;
            case 'p':
                w = w with { RainLast24HoursInches = i / 100.0 };
                break;
            case 'P':
                w = w with { RainSinceMidnightInches = i / 100.0 };
                break;
            case 'h':
                if (i > 100)
                {
                    if (!ctx.Tolerate(ctx.Options.AllowOutOfRangeValue, AprsDiagnosticCode.OutOfRangeValue, "humidity is over 100%; ignored", pos))
                    {
                        return false;
                    }

                    i = null;
                }

                w = w with { HumidityPercent = i == 0 ? 100 : i };
                break;
            case 'b':
                w = w with { PressureMillibars = i / 10.0 };
                break;
            case 'L':
                w = w with { LuminosityWattsPerSquareMetre = i };
                break;
            case 'l':
                w = w with { LuminosityWattsPerSquareMetre = i + 1000 };
                break;
            case '#':
                w = w with { RainRawCounter = i };
                break;
        }

        return true;
    }

    public static void WriteWind(InfoWriter w, AprsWeather weather)
    {
        weather.Validate();
        PositionCodec.WriteCourseSpeed(w, weather.WindDirectionDegrees, weather.WindSpeedMph);
    }

    public static void WriteFields(InfoWriter w, AprsWeather weather, bool positionless)
    {
        // A position report's wind is written before the fields (the extension, or the cs bytes).
        weather.Validate(windInFields: positionless);
        int start = w.Length;
        if (positionless)
        {
            Field(w, 'c', Whole(weather.WindDirectionDegrees), 3, always: true);
            Field(w, 's', weather.WindSpeedMph is { } s ? (int)Steps.Whole(s) : null, 3, always: true);
        }

        Field(w, 'g', Whole(weather.WindGustMph), 3, always: true);
        w.Char('t');
        if (Whole(weather.TemperatureFahrenheit) is { } t)
        {
            _ = t < 0 ? w.Char('-').Digits(-t, 2) : w.Digits(t, 3);
        }
        else
        {
            w.Ascii("...");
        }

        Field(w, 'r', Hundredths(weather.RainLastHourInches), 3);
        Field(w, 'p', Hundredths(weather.RainLast24HoursInches), 3);
        Field(w, 'P', Hundredths(weather.RainSinceMidnightInches), 3);
        Field(w, 'h', Whole(weather.HumidityPercent) is { } h ? h % 100 : null, 2);
        Field(w, 'b', weather.PressureMillibars is { } b ? (int)Steps.Count(b, 10) : null, 5);
        if (Whole(weather.LuminosityWattsPerSquareMetre) is { } lum)
        {
            _ = lum < 1000 ? w.Char('L').Digits(lum, 3) : w.Char('l').Digits(lum - 1000, 3);
        }

        if (weather.SnowfallLast24HoursInches is { } snow)
        {
            w.Char('s').Ascii(Snowfall(snow) ?? throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "snowfall is written in three characters, so it is at most 999 inches"));
        }

        Field(w, '#', weather.RainRawCounter, 3);
        foreach (AprsWeatherField extra in weather.AdditionalFields)
        {
            w.Char(extra.Letter).Ascii(extra.Value);
        }

        if (weather.SoftwareType is { } sw)
        {
            int fieldsEnd = w.Length;
            w.Char(sw).Ascii(weather.UnitType ?? "");

            // Written after the fields, the software type and unit must read back as themselves:
            // the field run has to end where they start, and what follows be a letter then a
            // 2-4 character unit (h89b1 would read back as humidity and pressure).
            byte[] written = w.Written[start..].ToArray();
            int pos = 0;
            var probe = new DecodeContext(AprsParseOptions.Lenient);
            var back = new AprsWeather();
            if (!TryReadRun(written, ref pos, probe, ref back, positionless, positionless, out _, out _, out _)
                || pos != fieldsEnd - start || !CommentCodec.IsSoftwareAndUnit(written.AsSpan(pos)))
            {
                w.Defer(AprsRefusalCode.TextReadsAsField, $"the software type and unit '{sw}{weather.UnitType}' would read back as weather fields or comment text (APRS12c ch. 12)");
            }
        }
    }

    /// <summary>A value rounded once to the whole unit its field holds (already checked in range by <c>Validate</c>).</summary>
    private static int? Whole(double? value) => value is { } v ? (int)Steps.Whole(v) : null;

    private static int? Hundredths(double? inches) => inches is { } i ? (int)Steps.Count(i, 100) : null;

    /// <summary>
    /// Snowfall's three characters (vectors README, Encoding, and ruling F3): rounded to hundredths
    /// if that leaves it under 1 (<c>.50</c>), else to tenths if that leaves it under 10 (<c>2.5</c>),
    /// else to whole inches; a result that is a whole number is written as one, in three digits
    /// (<c>002</c>, and 0.995 is <c>001</c>). Null when it rounds past 999.
    /// </summary>
    internal static string? Snowfall(double inches)
    {
        double hundredths = Steps.Nearest(inches * 100);
        if (double.IsNaN(hundredths) || hundredths < 0)
        {
            return null;
        }

        if (hundredths < 100)
        {
            return hundredths == 0 ? "000" : "." + ((int)hundredths).ToString("00", CultureInfo.InvariantCulture);
        }

        double tenths = Steps.Nearest(inches * 10);
        if (tenths < 100)
        {
            return tenths % 10 == 0
                ? ((int)(tenths / 10)).ToString("000", CultureInfo.InvariantCulture)
                : ((int)(tenths / 10)).ToString(CultureInfo.InvariantCulture) + "." + ((int)(tenths % 10)).ToString(CultureInfo.InvariantCulture);
        }

        double whole = Steps.Nearest(inches);
        return whole <= 999 ? ((int)whole).ToString("000", CultureInfo.InvariantCulture) : null;
    }

    private static void Field(InfoWriter w, char letter, int? value, int width, bool always = false)
    {
        if (value is { } v)
        {
            w.Char(letter).Digits(v, width);
        }
        else if (always)
        {
            w.Char(letter).Ascii(new string('.', width));
        }
    }
}
