using Packet.Aprs.Internal;

namespace Packet.Aprs;

/// <summary>
/// The common end of every fluent builder started from an <see cref="AprsStation"/>:
/// <see cref="ToData"/> for the data record alone, <see cref="Build"/> for the whole packet.
/// </summary>
public abstract class AprsBuilder
{
    private protected AprsBuilder(AprsStation station)
    {
        ArgumentNullException.ThrowIfNull(station);
        Station = station;
    }

    /// <summary>The sending station, whose header the packet gets.</summary>
    public AprsStation Station { get; }

    /// <summary>The data record built so far, without a header. Nothing is checked until it is encoded.
    /// Builders change themselves and return themselves, so a builder that is kept and built from
    /// again still has everything set on it.</summary>
    public abstract AprsData ToData();

    /// <summary>
    /// The packet: the station's header and this data, encoded. Throws <see cref="AprsEncodeException"/>
    /// for anything APRS12c does not allow, and <see cref="InvalidOperationException"/> when
    /// something the packet needs, such as a symbol, was never given. A Mic-E report gets its
    /// computed destination, whatever the station's.
    /// </summary>
    public AprsPacket Build()
    {
        AprsData data = ToData();
        return AprsPacket.Create(Station.Source, data, data is AprsMicEReport ? null : Station.Destination, Station.Path);
    }
}

/// <summary>Sends a data record built by hand, from <see cref="AprsStation.Data(AprsData)"/>.</summary>
public sealed class AprsDataBuilder : AprsBuilder
{
    private readonly AprsData data;

    internal AprsDataBuilder(AprsStation station, AprsData data)
        : base(station)
    {
        ArgumentNullException.ThrowIfNull(data);
        this.data = data;
    }

    /// <inheritdoc/>
    public override AprsData ToData() => data;
}

/// <summary>
/// What every report that puts something on the map shares: position, symbol, course and speed,
/// altitude, comment and the rest (APRS12c §5-§9). <typeparamref name="TSelf"/> is the concrete
/// builder, so each call returns it.
/// </summary>
/// <typeparam name="TSelf">The concrete builder.</typeparam>
public abstract class AprsPositionedBuilder<TSelf> : AprsBuilder
    where TSelf : AprsPositionedBuilder<TSelf>
{
    private AprsPosition? position;
    private AprsSymbol? symbol;
    private int ambiguity;
    private bool compressed;
    private double? course;
    private double? speedKnots;
    private double? altitudeFeet;
    private double? altitudeMetres;
    private AprsPhg? phg;
    private double? rangeMiles;
    private AprsVoiceFrequency? frequency;
    private AprsDao? dao;
    private AprsCommentTelemetry? telemetry;
    private string comment = "";

    private protected AprsPositionedBuilder(AprsStation station, AprsPosition? position)
        : base(station) => this.position = position;

    private TSelf Self => (TSelf)this;

    /// <summary>Where it is, in decimal degrees: north and east positive.</summary>
    public TSelf At(double latitude, double longitude) => At(new AprsPosition(latitude, longitude, ambiguity));

    /// <summary>Where it is.</summary>
    public TSelf At(AprsPosition position)
    {
        this.position = position;
        ambiguity = position.Ambiguity;
        return Self;
    }

    /// <summary>How to draw it, e.g. <see cref="AprsSymbol.Car"/>.</summary>
    public TSelf Symbol(AprsSymbol symbol)
    {
        this.symbol = symbol;
        return Self;
    }

    /// <summary>Course over ground in degrees clockwise from north, 1-360 (360 is north); 0 is the
    /// spec's unknown course (APRS12c ch. 7, 10), which a compressed report cannot send. A fraction is
    /// rounded once, to the whole degree (or the 4-degree step of a compressed report).</summary>
    public TSelf CourseDegrees(double degrees)
    {
        course = degrees;
        return Self;
    }

    /// <summary>Speed over ground in knots.</summary>
    public TSelf SpeedKnots(double knots)
    {
        speedKnots = knots;
        return Self;
    }

    /// <summary>Speed over ground in kilometres per hour, sent in knots.</summary>
    public TSelf SpeedKmh(double kilometresPerHour) => SpeedKnots(kilometresPerHour / 1.852);

    /// <summary>Altitude above mean sea level in feet (<c>/A=</c>).</summary>
    public TSelf AltitudeFeet(double feet)
    {
        altitudeFeet = feet;
        altitudeMetres = null;
        return Self;
    }

    /// <summary>Altitude above mean sea level in metres, sent in feet (<c>/A=</c>, rounded once to
    /// the foot), or in a Mic-E report in its altitude bytes, rounded to the metre (APRS12c ch. 10).</summary>
    public TSelf AltitudeMetres(double metres)
    {
        altitudeFeet = metres * Units.FeetPerMetre;
        altitudeMetres = metres;
        return Self;
    }

    /// <summary>The altitude given in metres, or null when none was or it was given in feet.</summary>
    private protected double? AltitudeInMetres => altitudeMetres;

    /// <summary>Free text after the structured parts. May contain UTF-8.</summary>
    public TSelf Comment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        comment = text;
        return Self;
    }

    /// <summary>Power, antenna height, gain and directivity codes (<c>PHGphgd</c>, APRS12c §7).</summary>
    public TSelf Phg(AprsPhg phg)
    {
        this.phg = phg;
        return Self;
    }

    /// <summary>Omnidirectional radio range in miles (<c>RNGrrrr</c>, APRS12c §7).</summary>
    public TSelf RangeMiles(double miles)
    {
        rangeMiles = miles;
        return Self;
    }

    /// <summary>
    /// A voice frequency at the start of the comment, as radios with a tune button read it
    /// (APRS12c §18): e.g. <c>Frequency(145.725, AprsToneType.Tone, 118, -600)</c> sends
    /// <c>145.725MHz T118 -060</c>. <paramref name="mhz"/> is rounded once to the field's step (1 kHz).
    /// <paramref name="toneValue"/> is the tone as sent, whole Hz for a CTCSS tone or the DCS code,
    /// and goes with a <paramref name="tone"/> of <see cref="AprsToneType.Tone"/>,
    /// <see cref="AprsToneType.Ctcss"/> or <see cref="AprsToneType.Dcs"/>.
    /// </summary>
    public TSelf Frequency(double mhz, AprsToneType? tone = null, int? toneValue = null, int? offsetKHz = null) => Frequency(new AprsVoiceFrequency
    {
        FrequencyMHz = mhz,
        ToneType = tone,
        ToneValue = toneValue,
        OffsetKHz = offsetKHz,
    });

    /// <summary>A voice frequency at the start of the comment, with every option (APRS12c §18).</summary>
    public TSelf Frequency(AprsVoiceFrequency frequency)
    {
        ArgumentNullException.ThrowIfNull(frequency);
        this.frequency = frequency;
        return Self;
    }

    /// <summary>
    /// Sends the position in compressed (base-91) form: shorter and more precise, but with no room
    /// for PHG or other data extensions (APRS12c §9).
    /// </summary>
    public TSelf Compressed(bool compressed = true)
    {
        this.compressed = compressed;
        return Self;
    }

    /// <summary>Blanks the last 1-4 digits of the position to hide it: to about 0.1, 1, 10 or 60
    /// nautical miles (APRS12c §6 Position Ambiguity). Not with <see cref="Compressed"/>.</summary>
    public TSelf Ambiguity(int digits)
    {
        ambiguity = digits;
        if (position is { } p)
        {
            position = p with { Ambiguity = digits };
        }

        return Self;
    }

    /// <summary>Adds a <c>!DAO!</c> extension carrying extra position precision, by default the WGS84
    /// base-91 form (about 0.2 m).</summary>
    public TSelf Dao(AprsDao? dao = null)
    {
        this.dao = dao ?? AprsDao.Wgs84Base91;
        return Self;
    }

    /// <summary>Base-91 telemetry in the comment (<c>|ss1122..|</c>, APRS12c §13): a sequence
    /// number and 1-5 analog values, each 0-8280.</summary>
    public TSelf Telemetry(int sequence, params int[] analog) =>
        Telemetry(new AprsCommentTelemetry { Sequence = sequence, Analog = analog });

    /// <summary>Base-91 telemetry in the comment with the 8 digital bits (bit 0 is B1), which
    /// can only follow all 5 analog values.</summary>
    public TSelf Telemetry(int sequence, int[] analog, byte digital) =>
        Telemetry(new AprsCommentTelemetry { Sequence = sequence, Analog = analog, Digital = digital });

    /// <summary>Base-91 telemetry in the comment (APRS12c §13).</summary>
    public TSelf Telemetry(AprsCommentTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        this.telemetry = telemetry;
        return Self;
    }

    /// <inheritdoc/>
    public abstract override AprsPositionedData ToData();

    private protected AprsPosition RequirePosition(string what) =>
        position ?? throw new InvalidOperationException($"{what} needs a position: call At(latitude, longitude)");

    private protected AprsSymbol RequireSymbol(string what) =>
        symbol ?? throw new InvalidOperationException($"{what} needs a symbol: call Symbol(...), e.g. Symbol(AprsSymbol.Car)");

    /// <summary>Copies the shared fields onto <paramref name="data"/>, which carries only its own.</summary>
    private protected T Apply<T>(T data)
        where T : AprsPositionedData => (T)(data with
        {
            IsCompressed = compressed,
            CourseDegrees = course,
            SpeedKnots = speedKnots,
            AltitudeFeet = altitudeFeet,
            Phg = phg,
            RangeMiles = rangeMiles,
            Frequency = frequency,
            Dao = dao,
            Telemetry = telemetry,
            Comment = comment,
        });
}

/// <summary>A position report, from <see cref="AprsStation.Position(double, double)"/> (APRS12c §8, §9).</summary>
public sealed class AprsPositionBuilder : AprsPositionedBuilder<AprsPositionBuilder>
{
    private bool messaging;
    private AprsTimestamp? timestamp;

    internal AprsPositionBuilder(AprsStation station, AprsPosition position)
        : base(station, position)
    {
    }

    /// <summary>Says the station can receive APRS messages (<c>=</c> or <c>@</c> rather than <c>!</c> or <c>/</c>).</summary>
    public AprsPositionBuilder Messaging(bool capable = true)
    {
        messaging = capable;
        return this;
    }

    /// <summary>When the position was valid, for a report that is not current (APRS12c §6).</summary>
    public AprsPositionBuilder Timestamp(AprsTimestamp timestamp)
    {
        this.timestamp = timestamp;
        return this;
    }

    /// <summary>When the position was valid, sent as day, hours and minutes UTC (<c>DDHHMMz</c>).</summary>
    public AprsPositionBuilder Timestamp(DateTime utc) => Timestamp(AprsTimestamp.FromDateTime(utc));

    /// <inheritdoc/>
    public override AprsPositionReport ToData() => Apply(new AprsPositionReport
    {
        Position = RequirePosition("a position report"),
        Symbol = RequireSymbol("a position report"),
        MessagingCapable = messaging,
        Timestamp = timestamp,
    });
}

/// <summary>An object, from <see cref="AprsStation.Object(string)"/> (APRS12c §11).</summary>
public sealed class AprsObjectBuilder : AprsPositionedBuilder<AprsObjectBuilder>
{
    private readonly string name;
    private bool killed;
    private AprsTimestamp? timestamp;

    internal AprsObjectBuilder(AprsStation station, string name)
        : base(station, null)
    {
        ArgumentNullException.ThrowIfNull(name);
        this.name = name;
    }

    /// <summary>When the object report was made. Without this, the time of <c>ToData()</c> or <c>Build()</c>, to the minute.</summary>
    public AprsObjectBuilder Timestamp(AprsTimestamp timestamp)
    {
        this.timestamp = timestamp;
        return this;
    }

    /// <summary>When the object report was made, sent as day, hours and minutes UTC (<c>DDHHMMz</c>).</summary>
    public AprsObjectBuilder Timestamp(DateTime utc) => Timestamp(AprsTimestamp.FromDateTime(utc));

    /// <summary>Marks the object permanent, one only this station may change, with the
    /// <c>111111z</c> timestamp (APRS12c §18 Object Name Permanence).</summary>
    public AprsObjectBuilder Permanent() => Timestamp(AprsTimestamp.DayHoursMinutes(11, 11, 11));

    /// <summary>Kills the object, removing it from maps (<c>_</c> rather than <c>*</c>); <c>Killed(false)</c> brings it back.</summary>
    public AprsObjectBuilder Killed(bool killed = true)
    {
        this.killed = killed;
        return this;
    }

    /// <inheritdoc/>
    public override AprsObjectReport ToData() => Apply(new AprsObjectReport
    {
        Name = name,
        IsKilled = killed,
        Timestamp = timestamp ?? AprsTimestamp.FromDateTime(DateTime.UtcNow),
        Position = RequirePosition("an object"),
        Symbol = RequireSymbol("an object"),
    });
}

/// <summary>An item, from <see cref="AprsStation.Item(string)"/> (APRS12c §11).</summary>
public sealed class AprsItemBuilder : AprsPositionedBuilder<AprsItemBuilder>
{
    private readonly string name;
    private bool killed;

    internal AprsItemBuilder(AprsStation station, string name)
        : base(station, null)
    {
        ArgumentNullException.ThrowIfNull(name);
        this.name = name;
    }

    /// <summary>Kills the item, removing it from maps (<c>_</c> rather than <c>!</c>); <c>Killed(false)</c> brings it back.</summary>
    public AprsItemBuilder Killed(bool killed = true)
    {
        this.killed = killed;
        return this;
    }

    /// <inheritdoc/>
    public override AprsItemReport ToData() => Apply(new AprsItemReport
    {
        Name = name,
        IsKilled = killed,
        Position = RequirePosition("an item"),
        Symbol = RequireSymbol("an item"),
    });
}

/// <summary>
/// A Mic-E position report, from <see cref="AprsStation.MicE(double, double)"/> (APRS12c §10). The
/// position comment defaults to <see cref="AprsMicEMessage.OffDuty"/>; <see cref="AprsMicEMessage.Emergency"/>
/// sets off alarms, so is only sent when asked for.
/// </summary>
public sealed class AprsMicEBuilder : AprsPositionedBuilder<AprsMicEBuilder>
{
    private AprsMicEMessage micEMessage = AprsMicEMessage.OffDuty;
    private bool messaging;

    internal AprsMicEBuilder(AprsStation station, AprsPosition position)
        : base(station, position)
    {
    }

    /// <summary>The position comment, e.g. <see cref="AprsMicEMessage.EnRoute"/>.</summary>
    public AprsMicEBuilder MicEMessage(AprsMicEMessage message)
    {
        micEMessage = message;
        return this;
    }

    /// <summary>Says the station can receive APRS messages: the <c>`</c> type code rather than <c>'</c> (APRS 1.2).</summary>
    public AprsMicEBuilder Messaging(bool capable = true)
    {
        messaging = capable;
        return this;
    }

    /// <inheritdoc/>
    public override AprsMicEReport ToData()
    {
        // The type code says whether the station can message: ` it can, ' it cannot (APRS 1.2
        // Mic-E type codes, vectors ruling B7).
        AprsMicEReport report = Apply(new AprsMicEReport
        {
            Position = RequirePosition("a Mic-E report"),
            Symbol = RequireSymbol("a Mic-E report"),
            MicEMessage = micEMessage,
            TypeCode = messaging ? '`' : '\'',
        });

        // A course of 0 is unknown, which Mic-E writes as 0, no course (APRS12c ch. 10, ruling B1).
        if (report.CourseDegrees is { } courseGiven && Steps.Whole(courseGiven) == 0)
        {
            report = report with { CourseDegrees = null };
        }

        // An altitude in metres goes in the Mic-E altitude bytes, whose unit is the metre, rounded
        // to the metre (ruling B2); one in feet goes in a /A=.
        return AltitudeInMetres is { } metres ? report with { AltitudeFeet = Steps.Whole(metres) * Units.FeetPerMetre }
            : report with { AltitudeInComment = report.AltitudeFeet is not null };
    }
}
