namespace Packet.Aprs;

/// <summary>
/// A sending station and its header, from <see cref="From(string)"/>: the source, destination
/// and path every packet built from it shares, and the starting point for each kind of packet.
/// Immutable: <see cref="To(string)"/> and <see cref="Via(string[])"/> return a new station. The
/// builders it starts are not: each call changes the builder and returns it.
/// </summary>
/// <remarks>
/// <code>
/// AprsPacket packet = AprsStation.From("M0LTE-9").Via("WIDE1-1", "WIDE2-1")
///     .Position(51.4543, -0.9781)
///     .Symbol(AprsSymbol.Car)
///     .CourseDegrees(88).SpeedKnots(36).AltitudeFeet(120)
///     .Comment("Mobile")
///     .Build();
///
/// AprsStation.From("M0LTE").Message("G3NRW", "Hi Ian").MessageId("01").Build();
/// AprsStation.From("M0LTE").Object("MYRPTR").At(51.45, -0.98).Symbol(AprsSymbol.Repeater)
///     .Frequency(145.725, AprsToneType.Tone, toneValue: 118, offsetKHz: -600).Build();
/// AprsStation.From("M0LTE").Weather().At(51.45, -0.98).WindDirectionDegrees(220).WindSpeedMph(4)
///     .WindGustMph(5).TemperatureFahrenheit(77).Build();
/// </code>
/// <para>
/// A station can be kept and reused: every packet built from it shares its source, destination
/// and path. <c>Build()</c> encodes the packet, so anything APRS12c does not allow is refused there
/// with an <see cref="AprsEncodeException"/>, exactly as <see cref="AprsPacket.Create(AprsAddress, AprsData, AprsAddress?, IEnumerable{AprsPathEntry}?)"/>
/// would. <c>ToData()</c> gives the data record without a header, for anything the builder does
/// not cover (records support <c>with</c>); <see cref="Data(AprsData)"/> sends it.
/// </para>
/// </remarks>
public sealed class AprsStation
{
    private AprsStation(AprsAddress source, AprsAddress destination, IReadOnlyList<AprsPathEntry> path)
    {
        Source = source;
        Destination = destination;
        Path = path;
    }

    /// <summary>
    /// The destination address used unless <see cref="To(string)"/> sets one:
    /// <c>APZ001</c>, in the range the APRS device identification database keeps for experimental
    /// software. An application should use its own allocated destination (its "tocall").
    /// </summary>
    public const string DefaultDestination = "APZ001";

    /// <summary>The source address.</summary>
    public AprsAddress Source { get; }

    /// <summary>The destination address; <see cref="DefaultDestination"/> unless set with <see cref="To(string)"/>.
    /// A Mic-E report computes its own and ignores this.</summary>
    public AprsAddress Destination { get; }

    /// <summary>The digipeater path; empty unless set with <see cref="Via(string[])"/>.</summary>
    public IReadOnlyList<AprsPathEntry> Path { get; }

    /// <summary>The station sending the packets, e.g. <c>M0LTE-9</c>. Throws <see cref="AprsEncodeException"/> for text that is not an address.</summary>
    public static AprsStation From(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return From(ParseAddress(source));
    }

    /// <summary>The station sending the packets.</summary>
    public static AprsStation From(AprsAddress source) => new(source, AprsAddress.Parse(DefaultDestination), []);

    /// <summary>This station with another destination address, usually the application's allocated tocall.</summary>
    public AprsStation To(string destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return new(Source, ParseAddress(destination), Path);
    }

    /// <summary>This station with a digipeater path, e.g. <c>Via("WIDE1-1", "WIDE2-1")</c>, replacing any set before.</summary>
    public AprsStation Via(params string[] path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new(Source, Destination, path.Select(p => new AprsPathEntry(ParseAddress(p))).ToList());
    }

    internal static AprsAddress ParseAddress(string text)
    {
        try
        {
            return AprsAddress.Parse(text);
        }
        catch (FormatException e)
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidAddress, e.Message, e);
        }
    }

    internal static List<AprsPathEntry> ParsePath(string text)
    {
        try
        {
            return [.. AprsPathEntry.ParseList(text)];
        }
        catch (FormatException e)
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidAddress, e.Message, e);
        }
    }

    /// <summary>A position report for this station (APRS12c §8, §9).</summary>
    public AprsPositionBuilder Position(double latitude, double longitude) => new(this, new AprsPosition(latitude, longitude));

    /// <summary>A position report for this station (APRS12c §8, §9).</summary>
    public AprsPositionBuilder Position(AprsPosition position) => new(this, position);

    /// <summary>An object: something other than this station placed on the map under its own name,
    /// which needs <see cref="AprsPositionedBuilder{TSelf}.At(double, double)"/> (APRS12c §11).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming", "CA1720:Identifier contains type name",
        Justification = "Object is the APRS term (APRS12c ch. 11) and sits beside Position, Item and Message.")]
    public AprsObjectBuilder Object(string name) => new(this, name);

    /// <summary>An item: like an object but with no timestamp, for things that do not move, which
    /// needs <see cref="AprsPositionedBuilder{TSelf}.At(double, double)"/> (APRS12c §11).</summary>
    public AprsItemBuilder Item(string name) => new(this, name);

    /// <summary>A Mic-E position report, which carries part of its position in the destination
    /// address, so ignores <see cref="Destination"/> (APRS12c §10).</summary>
    public AprsMicEBuilder MicE(double latitude, double longitude) => new(this, new AprsPosition(latitude, longitude));

    /// <summary>
    /// A weather report. With <see cref="AprsWeatherBuilder.At(double, double)"/> it is a position
    /// report with the weather station symbol, as APRS12c recommends; without, a positionless
    /// weather report (APRS12c §12).
    /// </summary>
    public AprsWeatherBuilder Weather() => new(this);

    /// <summary>A message to another station (APRS12c §14).</summary>
    public AprsMessageBuilder Message(string addressee, string text) => new(this, addressee, text);

    /// <summary>An acknowledgement of message <paramref name="messageId"/> from <paramref name="addressee"/> (APRS12c §14).</summary>
    public AprsAckBuilder Ack(string addressee, string messageId) => new(this, addressee, messageId, reject: false);

    /// <summary>A rejection of message <paramref name="messageId"/> from <paramref name="addressee"/> (APRS12c §14).</summary>
    public AprsAckBuilder Reject(string addressee, string messageId) => new(this, addressee, messageId, reject: true);

    /// <summary>A general bulletin (<paramref name="id"/> <c>0</c>-<c>9</c>) or an announcement (<c>A</c>-<c>Z</c>) (APRS12c §14).</summary>
    public AprsDataBuilder Bulletin(char id, string text) => Data(AprsBulletin.Create(id, text));

    /// <summary>A group bulletin, e.g. <c>BLN4WX</c> for id <c>4</c> and group <c>WX</c> (APRS12c §14).</summary>
    public AprsDataBuilder GroupBulletin(char id, string group, string text) => Data(AprsBulletin.CreateGroup(id, group, text));

    /// <summary>A status report (APRS12c §16).</summary>
    public AprsStatusBuilder Status(string text = "") => new(this, text);

    /// <summary>A telemetry report with sequence number <paramref name="sequence"/>, 0-999, sent as
    /// three digits (APRS12c §13); a larger one is refused, not wrapped.</summary>
    public AprsTelemetryBuilder Telemetry(int sequence) => new(this, sequence);

    /// <summary>The names of this station's telemetry channels, A1-A5 then B1-B8 (<c>PARM.</c>, APRS12c §13).
    /// Addressed to this station unless <see cref="AprsTelemetryMetadataBuilder.Addressee(string)"/> says otherwise.</summary>
    public AprsTelemetryMetadataBuilder TelemetryNames(params string[] names) =>
        new(this, source => new AprsTelemetryParameterNames { Addressee = source, Names = names });

    /// <summary>The units of this station's analog telemetry channels then the labels of its digital
    /// ones (<c>UNIT.</c>, APRS12c §13). Addressed to this station unless
    /// <see cref="AprsTelemetryMetadataBuilder.Addressee(string)"/> says otherwise.</summary>
    public AprsTelemetryMetadataBuilder TelemetryUnits(params string[] units) =>
        new(this, source => new AprsTelemetryUnits { Addressee = source, Units = units });

    /// <summary>The scaling for this station's analog telemetry channels: a, b and c for each in
    /// turn, giving a x v^2 + b x v + c (<c>EQNS.</c>, APRS12c §13). Addressed to this station unless
    /// <see cref="AprsTelemetryMetadataBuilder.Addressee(string)"/> says otherwise.</summary>
    public AprsTelemetryMetadataBuilder TelemetryCoefficients(params double[] coefficients) =>
        new(this, source => new AprsTelemetryCoefficients { Addressee = source, Coefficients = coefficients });

    /// <summary>Which state of each digital channel matches its label, as eight 0 and 1 characters
    /// with B1 first, and the project title (<c>BITS.</c>, APRS12c §13). Addressed to this station
    /// unless <see cref="AprsTelemetryMetadataBuilder.Addressee(string)"/> says otherwise.</summary>
    public AprsTelemetryMetadataBuilder TelemetryBits(string bits, string project = "") =>
        new(this, source => new AprsTelemetryBitSense { Addressee = source, Bits = bits, Project = project });

    /// <summary>Any data built by hand, sent from this station.</summary>
    public AprsDataBuilder Data(AprsData data) => new(this, data);
}
