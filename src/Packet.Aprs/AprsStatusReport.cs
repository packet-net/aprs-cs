using Packet.Aprs.Internal;

namespace Packet.Aprs;

/// <summary>
/// A status report (APRS12c §16): data type identifier <c>&gt;</c>, an optional DHM UTC timestamp,
/// then text. Two special forms are decoded: a Maidenhead locator with a symbol at the start
/// (<c>&gt;IO91SX/- text</c>, no timestamp allowed), and a meteor-scatter beam heading and power
/// at the end (<c>^B7</c>).
/// </summary>
public sealed record AprsStatusReport : AprsData
{
    /// <summary>When the status was set; DHM UTC only. Null if not sent.</summary>
    public AprsTimestamp? Timestamp { get; init; }

    /// <summary>The status text, without any locator, symbol or beam/power suffix. May contain UTF-8.</summary>
    public string Text { get; init; } = "";

    /// <summary>A 4- or 6-character Maidenhead locator at the start, or null.</summary>
    public string? Locator { get; init; }

    /// <summary>The symbol that follows the locator; present exactly when <see cref="Locator"/> is.</summary>
    public AprsSymbol? Symbol { get; init; }

    /// <summary>The beam heading/power code pair from a trailing <c>^HP</c>, or null.</summary>
    public AprsBeamHeading? BeamHeading { get; init; }

    /// <inheritdoc/>
    public override char DataTypeIdentifier => '>';

    internal override void Encode(InfoWriter writer) => StatusCodec.Write(writer, this);
}

/// <summary>
/// Meteor-scatter beam heading and effective radiated power, sent as <c>^HP</c> at the end of a
/// status report (APRS12c §16 Status Report with Beam Heading and ERP).
/// </summary>
/// <param name="HeadingCode">H: <c>0</c>-<c>9</c> for 0-90 degrees, <c>A</c>-<c>Z</c> for 100-350 degrees (tens of degrees).</param>
/// <param name="PowerCode">P: <c>1</c>-<c>9</c> then <c>:</c> onwards (ASCII order) up to <c>K</c>; ERP is 10 x n^2 watts.</param>
public readonly record struct AprsBeamHeading(char HeadingCode, char PowerCode)
{
    /// <summary>The beam heading in degrees.</summary>
    public int HeadingDegrees => HeadingCode <= '9' ? (HeadingCode - '0') * 10 : (HeadingCode - 'A' + 10) * 10;

    /// <summary>The effective radiated power in watts.</summary>
    public int ErpWatts => 10 * (PowerCode - '0') * (PowerCode - '0');

    internal bool IsValid => HeadingCode is (>= '0' and <= '9') or (>= 'A' and <= 'Z') && PowerCode is >= '1' and <= 'K';
}

/// <summary>
/// A telemetry report, data type <c>T</c> (APRS12c §13): <c>T#sss,aaa,aaa,aaa,aaa,aaa,bbbbbbbb</c>
/// then a comment. Channel meanings come from <see cref="AprsTelemetryParameterNames"/> and friends.
/// </summary>
/// <remarks>
/// APRS 1.2 widened values from 000-255 to 000-999, and notes that variable-width and decimal
/// values (<c>45.7</c>, <c>-7.3</c>) are common and should be accepted; they are decoded in all
/// modes. A decoded report keeps each value's text as sent in <see cref="AnalogText"/> (<c>073</c>,
/// <c>2</c>, <c>.53</c>) and writes it back; one built in code writes a whole number from 0 to
/// 999 as three digits, the spec's form.
/// </remarks>
public sealed record AprsTelemetryReport : AprsData
{
    /// <summary>The sequence number as sent: usually 3 digits, or <c>MIC</c>.</summary>
    public required string Sequence { get; init; }

    /// <summary>Up to 5 analog values; a null entry was sent empty.</summary>
    public required IReadOnlyList<double?> Analog { get; init => field = EquatableList<double?>.Of(value); }

    /// <summary>
    /// How each of <see cref="Analog"/> was written when decoded (<c>073</c>, <c>190.0</c>,
    /// <c>.53</c>; empty for one sent empty), which the encoder writes back for any entry that
    /// still means the same number; empty for a report built in code. Not part of equality.
    /// </summary>
    public IReadOnlyList<string> AnalogText { get; init => field = EquatableList<string>.Of(value); } = EquatableList<string>.Empty;

    /// <summary>The 8 digital channels as sent, B1 first: eight 0 or 1 characters, or null if not sent.</summary>
    public string? Bits { get; init; }

    /// <summary>Free text after the digital channels.</summary>
    public string Comment { get; init; } = "";

    /// <inheritdoc/>
    public override char DataTypeIdentifier => 'T';

    /// <summary>The same report, however the values were written.</summary>
    /// <param name="other">The report to compare with.</param>
    public bool Equals(AprsTelemetryReport? other) =>
        other is not null && Sequence == other.Sequence && Analog.SequenceEqual(other.Analog)
        && Bits == other.Bits && Comment == other.Comment;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Sequence, Analog.Count, Bits, Comment);

    internal override void Encode(InfoWriter writer) => TelemetryCodec.Write(writer, this);
}

/// <summary>
/// A positionless weather report, data type <c>_</c> (APRS12c §12): an MDHM timestamp then weather
/// fields starting <c>c s g t</c>. Not recommended: senders should use a position report with the
/// weather symbol. The station's position has to come from its separate position reports.
/// </summary>
public sealed record AprsWeatherReport : AprsData
{
    /// <summary>The MDHM timestamp. Null (a report built with none) is refused as <see cref="AprsRefusalCode.MissingField"/>
    /// when it is encoded; a decoded report always has one.</summary>
    public required AprsTimestamp? Timestamp { get; init; }

    /// <summary>The observations.</summary>
    public required AprsWeather Weather { get; init; }

    /// <summary>Text after the weather data (tolerated when decoding; not encoded).</summary>
    public string Comment { get; init; } = "";

    /// <inheritdoc/>
    public override char DataTypeIdentifier => '_';

    internal override void Encode(InfoWriter writer)
    {
        if (Timestamp is not { } timestamp)
        {
            writer.Defer(AprsRefusalCode.MissingField, "a positionless weather report must have a timestamp (APRS12c ch. 12)");
            writer.Char('_');
        }
        else
        {
            if (timestamp.Format != AprsTimestampFormat.MonthDayHoursMinutesUtc)
            {
                throw new AprsEncodeException(AprsRefusalCode.InvalidTimestamp, "a positionless weather report uses an MDHM timestamp (APRS12c ch. 12)");
            }

            writer.Char('_');
            TimestampCodec.Write(writer, timestamp, allowMonthDay: true);
        }

        if (Comment.Length > 0)
        {
            writer.Defer(AprsRefusalCode.FieldNotAllowed, "a weather report has no comment field (APRS12c ch. 12)");
        }

        WeatherCodec.WriteFields(writer, Weather, positionless: true);
    }
}
