using Packet.Aprs.Internal;

namespace Packet.Aprs;

/// <summary>A weather field this library does not interpret, kept so it is not lost:
/// a letter followed by its value text (APRS12c §12; UAP §2.7.1 says receivers should skip
/// unrecognised fields rather than fail).</summary>
/// <param name="Letter">The field letter, e.g. <c>X</c>.</param>
/// <param name="Value">The characters that followed it, as sent.</param>
public readonly record struct AprsWeatherField(char Letter, string Value);

/// <summary>
/// Weather observations (APRS12c §12 Weather Data), in the units APRS uses on air. Every value is
/// nullable: null means the field was absent or sent as dots / spaces (unknown).
/// </summary>
public sealed record AprsWeather
{
    /// <summary>Wind direction in degrees clockwise from north (<c>c</c>, or the DIR part of the extension). Whole when decoded; rounded once when encoded.</summary>
    public double? WindDirectionDegrees { get; init; }

    /// <summary>Sustained one-minute wind speed in mph (<c>s</c>, or the SPD part of the extension;
    /// see <c>interpretations.md</c> in <c>packet-net/aprs-vectors</c> on units). Fractional when it came from a compressed position.</summary>
    public double? WindSpeedMph { get; init; }

    /// <summary>Peak wind speed in the last 5 minutes, mph (<c>g</c>).</summary>
    public double? WindGustMph { get; init; }

    /// <summary>Temperature in degrees Fahrenheit (<c>t</c>); may be negative.</summary>
    public double? TemperatureFahrenheit { get; init; }

    /// <summary>Rainfall in the last hour, inches (<c>r</c>, sent in hundredths).</summary>
    public double? RainLastHourInches { get; init; }

    /// <summary>Rainfall in the last 24 hours, inches (<c>p</c>, sent in hundredths).</summary>
    public double? RainLast24HoursInches { get; init; }

    /// <summary>Rainfall since local midnight, inches (<c>P</c>, sent in hundredths).</summary>
    public double? RainSinceMidnightInches { get; init; }

    /// <summary>Relative humidity 1-100 percent (<c>h</c>; <c>h00</c> on air means 100). Whole when decoded; rounded once when encoded.</summary>
    public double? HumidityPercent { get; init; }

    /// <summary>Barometric pressure in millibars / hPa (<c>b</c>, sent in tenths).</summary>
    public double? PressureMillibars { get; init; }

    /// <summary>Luminosity in W/m^2 (<c>L</c> for 0-999, <c>l</c> for 1000-1999). Whole when decoded; rounded once when encoded.</summary>
    public double? LuminosityWattsPerSquareMetre { get; init; }

    /// <summary>Snowfall in the last 24 hours, inches (<c>s</c> after the mandatory fields; may have a decimal point).</summary>
    public double? SnowfallLast24HoursInches { get; init; }

    /// <summary>Raw rain gauge counter (<c>#</c>).</summary>
    public int? RainRawCounter { get; init; }

    /// <summary>Other letter-plus-value fields, in the order received.</summary>
    public IReadOnlyList<AprsWeatherField> AdditionalFields { get; init => field = EquatableList<AprsWeatherField>.Of(value); } = [];

    /// <summary>The one-character APRS software type after the data, e.g. <c>w</c> WinAPRS (APRS12c §12).</summary>
    public char? SoftwareType { get; init; }

    /// <summary>The 2-4 character weather unit type after the software type, e.g. <c>RSW</c>, <c>Dvs</c>, <c>U2k</c>.</summary>
    public string? UnitType { get; init; }

    /// <summary>
    /// Checks each value fits the field it is written in. <paramref name="windInFields"/> is false
    /// when the wind goes elsewhere: a compressed position's cs bytes hold wind faster than a
    /// three-digit field can (up to 1.08^90 - 1 knots, APRS12c ch. 9), and are checked as they are written.
    /// </summary>
    internal void Validate(bool windInFields = true)
    {
        // Each value is checked as its field holds it, rounded to the field's steps (vectors ruling
        // F2): a humidity of 99.7 is 100, written h00, and one of 0.4 is 0, which has no form (F6).
        if ((windInFields && (IsNaN(WindDirectionDegrees) || IsNaN(WindSpeedMph))) || IsNaN(WindGustMph) || IsNaN(TemperatureFahrenheit)
            || IsNaN(HumidityPercent) || IsNaN(PressureMillibars) || IsNaN(LuminosityWattsPerSquareMetre) || IsNaN(RainLastHourInches)
            || IsNaN(RainLast24HoursInches) || IsNaN(RainSinceMidnightInches) || IsNaN(SnowfallLast24HoursInches))
        {
            throw new AprsEncodeException(AprsRefusalCode.NotANumber, "a weather value is not a number");
        }

        if ((windInFields && (Outside(WindDirectionDegrees, 1, 0, 360) || Outside(WindSpeedMph, 1, 0, 999))) || Outside(WindGustMph, 1, 0, 999)
            || Outside(TemperatureFahrenheit, 1, -99, 999) || Outside(HumidityPercent, 1, 0, 100)
            || Outside(PressureMillibars, 10, 0, 99999) || Outside(LuminosityWattsPerSquareMetre, 1, 0, 1999)
            || Outside(RainLastHourInches, 100, 0, 999) || Outside(RainLast24HoursInches, 100, 0, 999) || Outside(RainSinceMidnightInches, 100, 0, 999)
            || (SnowfallLast24HoursInches is { } snow && WeatherCodec.Snowfall(snow) is null) || RainRawCounter is < 0 or > 999)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "a weather value is outside what its fixed-width field can carry");
        }

        if (Outside(HumidityPercent, 1, 1, 100))
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueHasNoForm, "a humidity that rounds to 0 has no form: 1-100, and 100 is written 00");
        }

        if ((SoftwareType is null) != (UnitType is null) && UnitType is not null)
        {
            throw new AprsEncodeException(AprsRefusalCode.MissingField, "a weather unit type needs a software type before it");
        }

        if (UnitType is { Length: < 2 or > 4 })
        {
            throw new AprsEncodeException(AprsRefusalCode.TooLong, "weather unit type must be 2-4 characters");
        }
    }

    /// <summary>True when a value, rounded to <paramref name="stepsPerUnit"/> steps, is outside <paramref name="min"/>-<paramref name="max"/> steps.</summary>
    private static bool Outside(double? value, double stepsPerUnit, long min, long max) =>
        value is { } v && !double.IsNaN(v) && Steps.Nearest(v * stepsPerUnit) is var steps && (steps < min || steps > max);

    private static bool IsNaN(double? value) => value is { } v && double.IsNaN(v);
}
