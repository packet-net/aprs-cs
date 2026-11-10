namespace Packet.Aprs;

/// <summary>A message to another station, from <see cref="AprsStation.Message(string, string)"/> (APRS12c §14).</summary>
public sealed class AprsMessageBuilder : AprsBuilder
{
    private readonly string addressee;
    private readonly string text;
    private string? id;
    private string? replyAck;

    internal AprsMessageBuilder(AprsStation station, string addressee, string text)
        : base(station)
    {
        ArgumentNullException.ThrowIfNull(addressee);
        ArgumentNullException.ThrowIfNull(text);
        this.addressee = addressee;
        this.text = text;
    }

    /// <summary>A message ID (1-5 letters or digits), which asks the other station to acknowledge it.</summary>
    public AprsMessageBuilder MessageId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        this.id = id;
        return this;
    }

    /// <summary>
    /// Uses the reply-ack format (APRS12c §14 New Message Number Format), which needs
    /// <see cref="MessageId"/>: acknowledges the other station's message <paramref name="theirId"/> in
    /// the same packet, or with none just says this station understands reply-acks.
    /// </summary>
    public AprsMessageBuilder ReplyAck(string theirId = "")
    {
        ArgumentNullException.ThrowIfNull(theirId);
        replyAck = theirId;
        return this;
    }

    /// <inheritdoc/>
    public override AprsTextMessage ToData() => new() { Addressee = addressee, Text = text, MessageId = id, ReplyAck = replyAck };
}

/// <summary>An acknowledgement or rejection, from <see cref="AprsStation.Ack(string, string)"/> or
/// <see cref="AprsStation.Reject(string, string)"/> (APRS12c §14).</summary>
public sealed class AprsAckBuilder : AprsBuilder
{
    private readonly string addressee;
    private readonly string messageId;
    private readonly bool reject;
    private string? replyAck;

    internal AprsAckBuilder(AprsStation station, string addressee, string messageId, bool reject)
        : base(station)
    {
        ArgumentNullException.ThrowIfNull(addressee);
        ArgumentNullException.ThrowIfNull(messageId);
        this.addressee = addressee;
        this.messageId = messageId;
        this.reject = reject;
    }

    /// <summary>For a message that came in reply-ack format (<c>{MM}AA</c>), echoes back its <c>AA</c> part.</summary>
    public AprsAckBuilder ReplyAck(string theirAck)
    {
        ArgumentNullException.ThrowIfNull(theirAck);
        replyAck = theirAck;
        return this;
    }

    /// <inheritdoc/>
    public override AprsMessage ToData() => reject
        ? new AprsMessageReject { Addressee = addressee, RejectedId = messageId, ReplyAck = replyAck }
        : new AprsMessageAck { Addressee = addressee, AcknowledgedId = messageId, ReplyAck = replyAck };
}

/// <summary>A status report, from <see cref="AprsStation.Status(string)"/> (APRS12c §16).</summary>
public sealed class AprsStatusBuilder : AprsBuilder
{
    private readonly string text;
    private AprsTimestamp? timestamp;
    private string? locator;
    private AprsSymbol? symbol;
    private AprsBeamHeading? beam;

    internal AprsStatusBuilder(AprsStation station, string text)
        : base(station)
    {
        ArgumentNullException.ThrowIfNull(text);
        this.text = text;
    }

    /// <summary>When the status was set, sent as day, hours and minutes UTC. Not with <see cref="Locator(string)"/>.</summary>
    public AprsStatusBuilder Timestamp(AprsTimestamp timestamp)
    {
        this.timestamp = timestamp;
        return this;
    }

    /// <summary>When the status was set, sent as day, hours and minutes UTC (<c>DDHHMMz</c>).</summary>
    public AprsStatusBuilder Timestamp(DateTime utc) => Timestamp(AprsTimestamp.FromDateTime(utc));

    /// <summary>A 4- or 6-character Maidenhead locator at the start, e.g. <c>IO91SX</c>, which needs
    /// <see cref="Symbol(AprsSymbol)"/> too.</summary>
    public AprsStatusBuilder Locator(string locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        this.locator = locator;
        return this;
    }

    /// <summary>The symbol after the locator, e.g. <c>-</c> on the primary table for a house; only with <see cref="Locator(string)"/>.</summary>
    public AprsStatusBuilder Symbol(AprsSymbol symbol)
    {
        this.symbol = symbol;
        return this;
    }

    /// <summary>A meteor-scatter beam heading and power at the end (<c>^HP</c>).</summary>
    public AprsStatusBuilder Beam(AprsBeamHeading beam)
    {
        this.beam = beam;
        return this;
    }

    /// <inheritdoc/>
    public override AprsStatusReport ToData() => new()
    {
        Text = text,
        Timestamp = timestamp,
        Locator = locator,
        Symbol = symbol,
        BeamHeading = beam,
    };
}

/// <summary>
/// A telemetry report, from <see cref="AprsStation.Telemetry(int)"/> (APRS12c §13): five analog
/// values and eight digital bits. The channel names, units and scaling go in separate messages:
/// <see cref="AprsStation.TelemetryNames(string[])"/> and the methods after it.
/// </summary>
public sealed class AprsTelemetryBuilder : AprsBuilder
{
    private readonly string sequence;
    private double?[] analog = [null, null, null, null, null];
    private string bits = "00000000";
    private string comment = "";

    internal AprsTelemetryBuilder(AprsStation station, int sequence)
        : base(station)
    {
        // 0-999 in the spec's three digits (APRS12c ch. 13 sss); a larger one is refused, not
        // wrapped (vectors rulings B6, B8).
        if (sequence is < 0 or > 999)
        {
            throw new AprsEncodeException("a telemetry sequence number is 0-999 (APRS12c ch. 13)");
        }

        this.sequence = sequence.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Up to 5 analog values, A1 first; a null one, and any not given, are sent empty.</summary>
    public AprsTelemetryBuilder Analog(params double?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length > 5)
        {
            throw new AprsEncodeException("a telemetry report carries at most 5 analog values (APRS12c ch. 13)");
        }

        analog = [.. values, .. Enumerable.Repeat<double?>(null, 5 - values.Length)];
        return this;
    }

    /// <summary>The 8 digital channels as sent, B1 first, e.g. <c>"10000000"</c> for B1 on.</summary>
    public AprsTelemetryBuilder Bits(string bits)
    {
        ArgumentNullException.ThrowIfNull(bits);
        if (bits.Length != 8 || bits.Any(c => c is not ('0' or '1')))
        {
            throw new AprsEncodeException("the digital channels are 8 characters, each 0 or 1, B1 first");
        }

        this.bits = bits;
        return this;
    }

    /// <summary>Free text after the values.</summary>
    public AprsTelemetryBuilder Comment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        comment = text;
        return this;
    }

    /// <inheritdoc/>
    public override AprsTelemetryReport ToData() => new() { Sequence = sequence, Analog = analog, Bits = bits, Comment = comment };
}

/// <summary>
/// The names, units, scaling or bit sense of a station's telemetry channels, from
/// <see cref="AprsStation.TelemetryNames(string[])"/> and its siblings (APRS12c §13). It is a
/// message to the telemetry station itself, which is the sending station unless
/// <see cref="Addressee(string)"/> says otherwise.
/// </summary>
public sealed class AprsTelemetryMetadataBuilder : AprsBuilder
{
    private readonly Func<string, AprsMessage> make;
    private string? addressee;

    internal AprsTelemetryMetadataBuilder(AprsStation station, Func<string, AprsMessage> make)
        : base(station) => this.make = make;

    /// <summary>The station the telemetry belongs to, when not the sender.</summary>
    public AprsTelemetryMetadataBuilder Addressee(string addressee)
    {
        ArgumentNullException.ThrowIfNull(addressee);
        this.addressee = addressee;
        return this;
    }

    /// <inheritdoc/>
    public override AprsMessage ToData() => make(addressee ?? Station.Source.Value);
}
