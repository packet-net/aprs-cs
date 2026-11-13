using Packet.Aprs.Internal;

namespace Packet.Aprs;

/// <summary>The raw weather station formats (APRS12c §12 Raw Weather Reports).</summary>
public enum AprsRawWeatherFormat
{
    /// <summary><c>#</c>: Peet Bros Ultimeter-II.</summary>
    PeetBrosHash,

    /// <summary><c>*</c>: Peet Bros Ultimeter-II.</summary>
    PeetBrosStar,

    /// <summary><c>$ULTW</c>: Ultimeter 2000 packet mode.</summary>
    UltimeterPacket,

    /// <summary><c>!!</c>: Ultimeter 2000 data logging mode.</summary>
    UltimeterLogging,
}

/// <summary>
/// Raw data from a weather station, sent without reformatting (APRS12c §12). Not recommended:
/// the sending software should convert it to a complete weather report. The vendor-specific
/// data is kept as text.
/// </summary>
public sealed record AprsRawWeatherReport : AprsData
{
    /// <summary>Which station format it is.</summary>
    public required AprsRawWeatherFormat Format { get; init; }

    /// <summary>Everything after the format prefix, as sent.</summary>
    public required string Data { get; init; }

    /// <inheritdoc/>
    public override char DataTypeIdentifier => Format switch
    {
        AprsRawWeatherFormat.PeetBrosHash => '#',
        AprsRawWeatherFormat.PeetBrosStar => '*',
        AprsRawWeatherFormat.UltimeterPacket => '$',
        _ => '!',
    };

    internal override void Encode(InfoWriter writer)
    {
        writer.Ascii(Format switch
        {
            AprsRawWeatherFormat.PeetBrosHash => "#",
            AprsRawWeatherFormat.PeetBrosStar => "*",
            AprsRawWeatherFormat.UltimeterPacket => "$ULTW",
            _ => "!!",
        });
        writer.Ascii(Data);
    }
}

/// <summary>
/// A raw NMEA 0183 sentence from a GPS, sent as-is by early trackers (APRS12c §6 NMEA Data, §8).
/// Obsolete (UAP §5.20). GGA, RMC, GLL, VTG and WPL are interpreted; the symbol, if any, comes from
/// the destination address (<see cref="AprsSymbolTable.FromDestination"/>) or source SSID.
/// </summary>
public sealed record AprsNmeaReport : AprsData
{
    /// <summary>
    /// The sentence without the leading <c>$</c>, up to and including any <c>*hh</c> checksum:
    /// an address field (<c>GPRMC</c>, or <c>P</c> and a manufacturer's code), then comma-separated
    /// fields in printable ASCII.
    /// </summary>
    public required string Sentence { get; init; }

    /// <summary>
    /// Text after the sentence's checksum, as sent (TinyTrack and FreeTrak add a comment there, as
    /// any APRS packet may carry one, APRS12c ch. 5); empty when there is none. A comment needs a
    /// checksum before it, which is where the sentence ends.
    /// </summary>
    public string Comment { get; init; } = "";

    /// <summary>
    /// The address field: the talker and sentence formatter, e.g. <c>GPRMC</c>, or <c>P</c> and a
    /// manufacturer's code for a proprietary sentence, e.g. <c>PMGNWPL</c>.
    /// </summary>
    public string SentenceType => Sentence.IndexOfAny([',', '*']) is var end and >= 0 ? Sentence[..end] : Sentence;

    /// <summary>
    /// True when the sentence ends with a <c>*hh</c> checksum. A decoded sentence's checksum always
    /// matches: one that doesn't is corrupt and is not decoded, and the encoder refuses to send it.
    /// </summary>
    public bool HasChecksum => Internal.NmeaCodec.TryReadChecksum(Sentence, out _, out _, out _);

    /// <summary>The position, for GGA, RMC, GLL and WPL.</summary>
    public AprsPosition? Position { get; init; }

    /// <summary>Whether the receiver reported a valid fix (RMC/GLL status A, GGA quality above 0), if stated.</summary>
    public bool? FixValid { get; init; }

    /// <summary>Course over ground in degrees true (RMC, VTG).</summary>
    public double? CourseDegrees { get; init; }

    /// <summary>Speed over ground in knots (RMC, VTG).</summary>
    public double? SpeedKnots { get; init; }

    /// <summary>Altitude above mean sea level in metres (GGA).</summary>
    public double? AltitudeMetres { get; init; }

    /// <summary>The UTC time of the fix (GGA, RMC, GLL), to the 100 ns <see cref="TimeOnly"/> holds; <see cref="TimeText"/> has every digit sent.</summary>
    public TimeOnly? Time { get; init; }

    /// <summary>
    /// The time field exactly as sent, <c>hhmmss</c> and any fraction of a second (e.g.
    /// <c>154027.1234567890</c>), when it is a valid time; null otherwise.
    /// </summary>
    public string? TimeText { get; init; }

    /// <summary>The waypoint name (WPL).</summary>
    public string? WaypointName { get; init; }

    /// <inheritdoc/>
    public override char DataTypeIdentifier => '$';

    internal override void Encode(InfoWriter writer)
    {
        byte[] sentence = System.Text.Encoding.Latin1.GetBytes(Sentence);
        if (Sentence.Any(c => c > 0xFF) || Internal.NmeaCodec.SentenceLength(sentence) != sentence.Length)
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidNmea, "an NMEA sentence is an address field (5 upper-case letters or digits, or P and 3 or more) and comma-separated fields in printable ASCII, ending at any *hh checksum");
        }

        if (Internal.NmeaCodec.TryReadChecksum(Sentence, out _, out byte expected, out byte sum) && sum != expected)
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidNmea, $"the NMEA checksum is {expected:X2} but the sentence sums to {sum:X2}");
        }

        Internal.Text.RequireNoLineBreaks(Comment);
        if (Comment.Length > 0 && !HasChecksum)
        {
            throw new AprsEncodeException(AprsRefusalCode.TextReadsAsField, "a comment after an NMEA sentence needs the sentence's *hh checksum before it, or it would read back as part of the sentence");
        }

        writer.Char('$').Ascii(Sentence).Utf8(Comment);
        VerifyReadsBack();
    }

    /// <summary>
    /// The sentence is what is sent; the values read from it (position, fix, course, speed,
    /// altitude, time, waypoint) are not written. Any of them given must be what the sentence
    /// reads back as, or the report would read back as different data (vectors README, Encoding).
    /// The time is compared as a time: <see cref="TimeText"/> is its text, which the sentence holds.
    /// </summary>
    private void VerifyReadsBack()
    {
        byte[] info = [(byte)'$', .. System.Text.Encoding.Latin1.GetBytes(Sentence)];
        var back = AprsPacket.Decode(AprsAddress.Parse("N0CALL"), AprsAddress.Parse("APZ001"), [], info).Data as AprsNmeaReport;
        string? differs =
            back is null ? "the sentence"
            : Position is { } p && (back.Position is not { } q || p.Ambiguity != q.Ambiguity || !Near(p.Latitude, q.Latitude) || !Near(p.Longitude, q.Longitude)) ? nameof(Position)
            : FixValid is not null && FixValid != back.FixValid ? nameof(FixValid)
            : CourseDegrees is { } c && (back.CourseDegrees is not { } c2 || !Near(c, c2)) ? nameof(CourseDegrees)
            : SpeedKnots is { } s && (back.SpeedKnots is not { } s2 || !Near(s, s2)) ? nameof(SpeedKnots)
            : AltitudeMetres is { } a && (back.AltitudeMetres is not { } a2 || !Near(a, a2)) ? nameof(AltitudeMetres)
            : Time is not null && Time != back.Time ? nameof(Time)
            : WaypointName is not null && WaypointName != back.WaypointName ? nameof(WaypointName)
            : null;
        if (differs is not null)
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidNmea, $"{differs} is not what the sentence reads back as; the sentence is what is sent");
        }

        // The same value, but for the last bits a different computation of it may differ in.
        static bool Near(double x, double y) => Math.Abs(x - y) <= 1e-9 * Math.Max(1, Math.Abs(x));
    }
}

/// <summary>
/// A Maidenhead locator beacon, data type <c>[</c>: <c>[IO91SX] comment</c> (APRS12c §8). Obsolete.
/// </summary>
public sealed record AprsMaidenheadBeacon : AprsData
{
    /// <summary>The 4 or 6 character locator.</summary>
    public required string Locator { get; init; }

    /// <summary>Text after the closing bracket.</summary>
    public string Comment { get; init; } = "";

    /// <inheritdoc/>
    public override char DataTypeIdentifier => '[';

    internal override void Encode(InfoWriter writer)
    {
        if (!Maidenhead.IsLocator(System.Text.Encoding.ASCII.GetBytes(Locator)))
        {
            throw new AprsEncodeException(AprsRefusalCode.Malformed, $"'{Locator}' is not a 4 or 6 character Maidenhead locator");
        }

        Internal.Text.RequireNoLineBreaks(Comment);
        writer.Char('[').Ascii(Locator.ToUpperInvariant()).Char(']').Utf8(Comment);
    }
}

/// <summary>The target area of a general query: floating-point degrees and a radius in miles (APRS12c §15).</summary>
/// <remarks>
/// A decoded footprint is written back with its coordinates as sent (<c>-.1715</c>, <c>34.0</c>,
/// with or without the leading space before a positive value); one built in code gets the spec's
/// form, a leading space in place of a plus sign. How a coordinate was written is not part of
/// equality.
/// </remarks>
/// <param name="Latitude">Degrees, positive north.</param>
/// <param name="Longitude">Degrees, positive east.</param>
/// <param name="RadiusMiles">Radius, 0-9999 miles: whole when decoded; rounded once when encoded.</param>
public readonly record struct AprsQueryFootprint(double Latitude, double Longitude, double RadiusMiles)
{
    /// <summary>The latitude's text as received, when decoded (<c>-.1715</c>, <c> 34.0</c>); null for one built in code.</summary>
    public string? LatitudeText { get; init; }

    /// <summary>The longitude's text as received, when decoded; null for one built in code.</summary>
    public string? LongitudeText { get; init; }

    /// <summary>The same place and radius, however the coordinates were written.</summary>
    /// <param name="other">The footprint to compare with.</param>
    public bool Equals(AprsQueryFootprint other) =>
        Latitude == other.Latitude && Longitude == other.Longitude && RadiusMiles == other.RadiusMiles;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Latitude, Longitude, RadiusMiles);
}

/// <summary>
/// A general query to all stations, data type <c>?</c>: <c>?APRS?</c>, <c>?IGATE?</c> or
/// <c>?WX?</c>, optionally limited to a footprint (APRS12c §15). One-shot and never acknowledged.
/// IGates must not forward general queries from RF to APRS-IS (UAP §2.8.1).
/// </summary>
public sealed record AprsGeneralQuery : AprsData
{
    /// <summary>The query type, e.g. <c>APRS</c>, <c>IGATE</c>, <c>WX</c>.</summary>
    public required string QueryType { get; init; }

    /// <summary>The area the query is for, or null for everyone.</summary>
    public AprsQueryFootprint? Footprint { get; init; }

    /// <inheritdoc/>
    public override char DataTypeIdentifier => '?';

    internal override void Encode(InfoWriter writer) => QueryCodec.WriteGeneral(writer, this);
}

/// <summary>One station capability: a token, optionally with <c>=value</c> (APRS12c §15).</summary>
/// <param name="Token">e.g. <c>IGATE</c>, <c>MSG_CNT</c>.</param>
/// <param name="Value">The value after <c>=</c>, or null for a bare token.</param>
public readonly record struct AprsCapability(string Token, string? Value);

/// <summary>
/// Station capabilities, data type <c>&lt;</c>: comma-separated tokens such as
/// <c>&lt;IGATE,MSG_CNT=43,LOC_CNT=14</c>, sent in reply to a <c>?IGATE?</c> query (APRS12c §15).
/// </summary>
public sealed record AprsStationCapabilities : AprsData
{
    /// <summary>The capabilities, in order.</summary>
    public required IReadOnlyList<AprsCapability> Capabilities { get; init => field = EquatableList<AprsCapability>.Of(value); }

    /// <inheritdoc/>
    public override char DataTypeIdentifier => '<';

    internal override void Encode(InfoWriter writer)
    {
        if (Capabilities.Count == 0)
        {
            throw new AprsEncodeException(AprsRefusalCode.MissingField, "a capabilities report lists at least one capability (APRS12c ch. 15)");
        }

        // The first fault in the shared order: a value's characters, then a value's padding, then a
        // token (fixed-form text), whichever capability holds it.
        if (Capabilities.Any(c => c.Value is { } v && v.Any(ch => ch is < ' ' or '\x7F' or ',')))
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidCharacter, "a capability value is text without ',' or control characters");
        }

        if (Capabilities.Any(c => c.Value is { } v && (v.StartsWith(' ') || v.EndsWith(' '))))
        {
            throw new AprsEncodeException(AprsRefusalCode.PaddingSpace, "a capability value does not start or end with a space");
        }

        if (Capabilities.Any(c => c.Token.Length == 0 || c.Token.Any(ch => ch is <= ' ' or '\x7F' or ',' or '=')))
        {
            throw new AprsEncodeException(AprsRefusalCode.Malformed, "a capability token is not empty and has no spaces, ',', '=' or control characters");
        }

        writer.Char('<');
        for (int i = 0; i < Capabilities.Count; i++)
        {
            AprsCapability c = Capabilities[i];
            if (i > 0)
            {
                writer.Char(',');
            }

            writer.Utf8(c.Token);
            if (c.Value is { } value)
            {
                writer.Char('=').Utf8(value);
            }
        }
    }
}

/// <summary>
/// Third-party traffic, data type <c>}</c>: a whole packet from another network (usually APRS-IS,
/// relayed to RF by an IGate) carried inside this one (APRS12c §17). Digipeaters look only at the
/// outer path; applications decode <see cref="Packet"/>. An RF-to-IS IGate must not forward it if
/// the inner path contains <c>TCPIP</c>, which prevents loops.
/// </summary>
public sealed record AprsThirdPartyTraffic : AprsData
{
    /// <summary>The encapsulated packet. Its source need not be a valid AX.25 address.</summary>
    public required AprsPacket Packet { get; init; }

    /// <summary>True if the inner path contains <c>TCPIP</c>, meaning it came from the internet.</summary>
    public bool CameFromInternet => Packet.Path.Any(p => p.Address.Value == "TCPIP");

    /// <inheritdoc/>
    public override char DataTypeIdentifier => '}';

    private const string HeaderRefusal = "the inner packet's header is not one a strict decoder reads (source 1-9 printable ASCII characters other than > and :, destination and path 1-9 letters, digits or -, every path entry before a used one marked used), or a defect in it was tolerated when it was decoded (APRS12c ch. 17)";

    /// <summary>
    /// Builds third-party traffic from the inner packet's header and data. The header is checked
    /// first, so a header that cannot be written is refused as
    /// <see cref="AprsRefusalCode.InvalidThirdPartyHeader"/> whatever is wrong with the data
    /// (vectors README, Refusal reasons); then the inner packet is made as
    /// <see cref="AprsPacket.Create(AprsAddress, AprsData, AprsAddress?, IEnumerable{AprsPathEntry}?)"/> makes it.
    /// </summary>
    public static AprsThirdPartyTraffic Create(AprsAddress source, AprsData data, AprsAddress destination, IEnumerable<AprsPathEntry>? path = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        List<AprsPathEntry> entries = path?.ToList() ?? [];
        if (!ThirdPartyCodec.HeaderCanBeWritten(source, destination, entries))
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidThirdPartyHeader, HeaderRefusal);
        }

        return new AprsThirdPartyTraffic { Packet = AprsPacket.Create(source, data, data is AprsMicEReport ? null : destination, entries) };
    }

    internal override void Encode(InfoWriter writer)
    {
        byte[] inner = Packet.ToTnc2();
        if (!ThirdPartyCodec.CanWrite(Packet, inner))
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidThirdPartyHeader, HeaderRefusal);
        }

        writer.Char('}').Bytes(inner);
    }
}

/// <summary>
/// User-defined data, data type <c>{</c>: a one-character user ID, a one-character packet type,
/// then data in a format of the author's choosing (APRS12c §19). <c>{{</c> is for experiments.
/// </summary>
public sealed record AprsUserDefinedData : AprsData
{
    /// <summary>The user ID, e.g. <c>Q</c>; <c>{</c> means experimental. Any byte (U+0000-U+00FF): APRS12c ch. 19 only recommends printable ASCII.</summary>
    public required char UserId { get; init; }

    /// <summary>The user-defined packet type. Any byte (U+0000-U+00FF), as for <see cref="UserId"/>.</summary>
    public required char PacketType { get; init; }

    /// <summary>The rest of the information field, as sent.</summary>
    public required IReadOnlyList<byte> Data { get; init => field = EquatableList<byte>.Of(value); }

    /// <summary>The data as text (UTF-8, Latin-1 where invalid).</summary>
    public string Text => Internal.Text.ForDisplay(Data.ToArray());

    /// <inheritdoc/>
    public override char DataTypeIdentifier => '{';

    internal override void Encode(InfoWriter writer)
    {
        // "There is no restriction on the nature of user-defined data" (APRS12c ch. 19), header
        // included, so any byte is written back as it came.
        if (UserId > '\xFF' || PacketType > '\xFF')
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidCharacter, "user ID and packet type are single bytes (U+0000-U+00FF)");
        }

        // A trailing line break is a tolerated defect a decoder strips, not data.
        if ((Data.Count > 0 ? Data[^1] : (byte)PacketType) is (byte)'\r' or (byte)'\n')
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidCharacter, "user-defined data that ends with a carriage return or line feed would read back without it");
        }

        writer.Char('{').Char(UserId).Char(PacketType).Bytes(Data.ToArray());
    }
}

/// <summary>Invalid or test data, data type <c>,</c> (APRS12c §20), e.g. a Mic-E unit's invalid GPS fix.</summary>
public sealed record AprsTestData : AprsData
{
    /// <summary>Everything after the comma.</summary>
    public required string Data { get; init; }

    /// <inheritdoc/>
    public override char DataTypeIdentifier => ',';

    internal override void Encode(InfoWriter writer)
    {
        Internal.Text.RequireNoLineBreaks(Data);
        writer.Char(',').Utf8(Data);
    }
}

/// <summary>An Agrelo DFJr / MicroFinder bearing, data type <c>%</c>: <c>%bbb/q</c> (APRS12c Appendix 1).</summary>
public sealed record AprsAgreloDfReport : AprsData
{
    /// <summary>Bearing in degrees, 0-360.</summary>
    public required int BearingDegrees { get; init; }

    /// <summary>Signal quality, 0-9.</summary>
    public required int Quality { get; init; }

    /// <inheritdoc/>
    public override char DataTypeIdentifier => '%';

    internal override void Encode(InfoWriter writer)
    {
        if (BearingDegrees is < 0 or > 360 || Quality is < 0 or > 9)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "bearing 0-360, quality 0-9");
        }

        writer.Char('%').Digits(BearingDegrees, 3).Char('/').Digits(Quality, 1);
    }
}
