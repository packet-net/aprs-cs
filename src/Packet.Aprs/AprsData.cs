using Packet.Aprs.Internal;

namespace Packet.Aprs;

/// <summary>
/// The decoded content of an APRS information field. The concrete type is chosen by the data type
/// identifier (APRS12c §5); pattern-match on it:
/// <code>
/// switch (packet.Data)
/// {
///     case AprsPositionReport p: ...
///     case AprsMicEReport m: ...
///     case AprsTextMessage msg: ...
/// }
/// </code>
/// </summary>
public abstract record AprsData
{
    private protected AprsData()
    {
    }

    /// <summary>The data type identifier this data is encoded with: the first byte of the
    /// information field.</summary>
    public abstract char DataTypeIdentifier { get; }

    /// <summary>
    /// Encodes this data as an information field. Throws <see cref="AprsEncodeException"/> if any
    /// value cannot be represented as APRS12c requires, and for a Mic-E report, whose position is
    /// split between the information field and the destination address: make its packet with
    /// <see cref="AprsPacket.Create(AprsAddress, AprsData, AprsAddress?, IEnumerable{AprsPathEntry}?)"/>.
    /// An information field is at most 256 bytes (APRS12c ch. 3): more is refused as
    /// <see cref="AprsRefusalCode.InformationFieldTooLong"/>, though a decoder sets no limit.
    /// </summary>
    public byte[] ToInformationField() => this is AprsMicEReport
        ? throw new AprsEncodeException(AprsRefusalCode.NotEncodable, "a Mic-E report carries part of its position in the destination address, so its information field alone is not a position; make a packet with AprsPacket.Create")
        : EncodeInformation();

    /// <summary>
    /// The information field, whatever the data type (for a Mic-E report, without its destination).
    /// Ends in the read-back. <paramref name="micEBytesFirst"/> is a builder telling the encoder to
    /// write a Mic-E altitude in the Mic-E bytes first; it is an argument of the call, never held
    /// on the data. <paramref name="destination"/> is the destination the call writes the field under,
    /// which the read-back decodes it under (null for a field written alone).
    /// </summary>
    internal byte[] EncodeInformation(bool micEBytesFirst = false, AprsAddress? destination = null) => InfoEncoder.Encode(this, micEBytesFirst, destination);

    internal abstract void Encode(InfoWriter writer);
}

/// <summary>Why an information field could not be decoded as APRS data.</summary>
public enum AprsUnrecognizedReason
{
    /// <summary>The information field is empty.</summary>
    Empty,

    /// <summary>The first byte is not an APRS data type identifier: an ordinary AX.25 beacon or ID
    /// packet, which APRS clients may show as status text (APRS12c §20 All Other Packets).</summary>
    NotAprs,

    /// <summary>A data type identifier the spec reserves but never defined (<c>&amp;</c> map feature,
    /// <c>+</c> shelter data, <c>.</c> space weather).</summary>
    ReservedDataType,

    /// <summary>A known data type that failed to decode; the packet's diagnostics say why.</summary>
    Malformed,
}

/// <summary>
/// An information field that could not be decoded. The raw bytes are kept and re-encode unchanged.
/// </summary>
public sealed record AprsUnrecognizedData : AprsData
{
    /// <summary>Why it was not decoded.</summary>
    public required AprsUnrecognizedReason Reason { get; init; }

    /// <summary>The information field exactly as received.</summary>
    public required IReadOnlyList<byte> Raw { get; init => field = EquatableList<byte>.Of(value); }

    /// <summary>The information field as text (UTF-8, or Latin-1 where not valid UTF-8), for display.</summary>
    public string Text => Internal.Text.ForDisplay(Raw.ToArray());

    /// <inheritdoc/>
    public override char DataTypeIdentifier => Raw.Count > 0 ? (char)Raw[0] : '\0';

    internal override void Encode(InfoWriter writer) => writer.Bytes(Raw.ToArray());
}
