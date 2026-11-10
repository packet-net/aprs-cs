using System.Text;
using Packet.Aprs.Internal;

namespace Packet.Aprs;

/// <summary>
/// One APRS packet: the addresses, the raw information field, and its decoded <see cref="Data"/>.
/// </summary>
/// <remarks>
/// <para>
/// Decode from TNC2 / APRS-IS text with <see cref="Decode(string, AprsParseOptions?)"/>, from an
/// AX.25 UI frame with <see cref="DecodeAx25"/>, or from header fields you already have (for
/// example a Packet.NET <c>Ax25Frame</c>) with
/// <see cref="Decode(AprsAddress, AprsAddress, IEnumerable{AprsPathEntry}, ReadOnlySpan{byte}, AprsParseOptions?)"/>.
/// Problems in the information field never throw: <see cref="Data"/> is always set and
/// <see cref="Diagnostics"/> says what was wrong.
/// </para>
/// <para>
/// Build a packet to send with <see cref="Create(AprsAddress, AprsData, AprsAddress?, IEnumerable{AprsPathEntry}?)"/>,
/// then write it with <see cref="ToTnc2"/> or <see cref="ToAx25Frame"/>.
/// </para>
/// </remarks>
public sealed class AprsPacket : IEquatable<AprsPacket>
{
    private readonly byte[] information;

    private AprsPacket(
        AprsAddress source,
        AprsAddress destination,
        IReadOnlyList<AprsPathEntry> path,
        byte[] information,
        AprsData data,
        IReadOnlyList<AprsDiagnostic> diagnostics,
        bool readQConstruct = true)
    {
        Source = source;
        Destination = destination;
        Path = path;
        this.information = information;
        Data = data;
        Diagnostics = diagnostics;
        QConstruct = readQConstruct ? AprsQConstruct.Find(path) : null;
    }

    /// <summary>The station that originated the packet.</summary>
    public AprsAddress Source { get; }

    /// <summary>The destination address: usually the sending software's identifier (<c>APxxxx</c>),
    /// or part of the position for Mic-E (APRS12c §4). May be empty only when decoding tolerated it.</summary>
    public AprsAddress Destination { get; }

    /// <summary>The digipeater path, and for APRS-IS packets the q-construct and server after it.</summary>
    public IReadOnlyList<AprsPathEntry> Path { get; }

    /// <summary>The information field exactly as received (or as encoded).</summary>
    public ReadOnlyMemory<byte> Information => information;

    /// <summary>The decoded information field. Always set; <see cref="AprsUnrecognizedData"/> if it
    /// could not be decoded.</summary>
    public AprsData Data { get; }

    /// <summary>What the decoder noticed, most importantly any tolerated deviations (warnings) and
    /// the reason decoding failed (errors). Empty for a packet built with <c>Create</c>.</summary>
    public IReadOnlyList<AprsDiagnostic> Diagnostics { get; }

    /// <summary>True if any diagnostic is an error, meaning <see cref="Data"/> is <see cref="AprsUnrecognizedData"/>.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == AprsDiagnosticSeverity.Error);

    /// <summary>True if the packet decoded but only because tolerances allowed deviations from the spec.</summary>
    public bool HasWarnings => Diagnostics.Any(d => d.Severity == AprsDiagnosticSeverity.Warning);

    /// <summary>The APRS-IS q-construct in the path, if any. It is read only in the outer header:
    /// the packet inside a third-party packet has none, and its path is kept as sent.</summary>
    public AprsQConstruct? QConstruct { get; }

    // ------------------------------------------------------------------ decoding

    /// <summary>Decodes a TNC2 / APRS-IS line. Throws <see cref="AprsFormatException"/> if the header
    /// cannot be parsed; information-field problems are reported in <see cref="Diagnostics"/>.</summary>
    public static AprsPacket Decode(string tnc2, AprsParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tnc2);
        return Decode(Encoding.UTF8.GetBytes(tnc2), options);
    }

    /// <summary>Decodes a TNC2 / APRS-IS line given as raw bytes (preferred: exact, including non-UTF-8 bytes).</summary>
    public static AprsPacket Decode(ReadOnlySpan<byte> tnc2, AprsParseOptions? options = null) =>
        TryDecode(tnc2, out AprsPacket? packet, out IReadOnlyList<AprsDiagnostic> diagnostics, options)
            ? packet
            : throw new AprsFormatException(diagnostics);

    /// <summary>Tries to decode a TNC2 / APRS-IS line; false only if the header cannot be parsed.</summary>
    public static bool TryDecode(string tnc2, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AprsPacket? packet, AprsParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tnc2);
        return TryDecode(Encoding.UTF8.GetBytes(tnc2), out packet, out _, options);
    }

    /// <summary>Tries to decode a TNC2 / APRS-IS line given as raw bytes; false only if the header cannot be parsed.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> tnc2, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AprsPacket? packet, AprsParseOptions? options = null) =>
        TryDecode(tnc2, out packet, out _, options);

    private static bool TryDecode(
        ReadOnlySpan<byte> tnc2,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AprsPacket? packet,
        out IReadOnlyList<AprsDiagnostic> diagnostics,
        AprsParseOptions? options)
    {
        var ctx = new DecodeContext(options ?? AprsParseOptions.Lenient);
        packet = null;
        diagnostics = ctx.Diagnostics;
        if (!Tnc2Codec.TrySplit(tnc2, ctx, out Tnc2Codec.Header header, out int infoStart))
        {
            return false;
        }

        packet = Build(header, tnc2[infoStart..], ctx);
        return true;
    }

    /// <summary>
    /// Decodes an information field whose header you already have, for example from a Packet.NET
    /// <c>Ax25Frame</c>: pass its source, destination, digipeaters (with their H bits) and info bytes.
    /// </summary>
    public static AprsPacket Decode(
        AprsAddress source,
        AprsAddress destination,
        IEnumerable<AprsPathEntry> path,
        ReadOnlySpan<byte> information,
        AprsParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var ctx = new DecodeContext(options ?? AprsParseOptions.Lenient);
        return Build(new Tnc2Codec.Header(source, destination, path.ToList()), information, ctx);
    }

    /// <summary>
    /// Decodes an AX.25 UI frame without flags or FCS (what a KISS TNC delivers after the KISS
    /// command byte). Throws <see cref="AprsFormatException"/> if it is not an APRS UI frame.
    /// </summary>
    public static AprsPacket DecodeAx25(ReadOnlySpan<byte> frame, AprsParseOptions? options = null) =>
        TryDecodeAx25(frame, out AprsPacket? packet, out IReadOnlyList<AprsDiagnostic> diagnostics, options)
            ? packet
            : throw new AprsFormatException(diagnostics);

    /// <summary>Tries to decode an AX.25 UI frame; false if it is not an APRS UI frame.</summary>
    public static bool TryDecodeAx25(ReadOnlySpan<byte> frame, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AprsPacket? packet, AprsParseOptions? options = null) =>
        TryDecodeAx25(frame, out packet, out _, options);

    private static bool TryDecodeAx25(
        ReadOnlySpan<byte> frame,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AprsPacket? packet,
        out IReadOnlyList<AprsDiagnostic> diagnostics,
        AprsParseOptions? options)
    {
        var ctx = new DecodeContext(options ?? AprsParseOptions.Lenient);
        packet = null;
        diagnostics = ctx.Diagnostics;
        if (!Ax25Codec.TryDecode(frame, ctx, out Tnc2Codec.Header header, out int infoStart))
        {
            return false;
        }

        packet = Build(header, frame[infoStart..], ctx);
        return true;
    }

    internal static AprsPacket Build(Tnc2Codec.Header header, ReadOnlySpan<byte> information, DecodeContext ctx)
    {
        AprsData data = InfoDecoder.Decode(information, header.Destination, ctx);
        return new AprsPacket(header.Source, header.Destination, header.Path, information.ToArray(), data, ctx.Diagnostics, readQConstruct: ctx.Depth == 0);
    }

    // ------------------------------------------------------------------ building

    /// <summary>
    /// Builds a packet from data, encoding its information field. <paramref name="destination"/> is
    /// normally your software's registered identifier (<c>APxxxx</c>, or <c>APZxxx</c> while
    /// experimenting; APRS12c §4); when null it is <see cref="AprsStation.DefaultDestination"/>. A
    /// Mic-E report puts part of its position in the destination, so its destination is computed
    /// from it, and giving one is refused. Throws <see cref="AprsEncodeException"/> for anything the
    /// library will not write.
    /// </summary>
    public static AprsPacket Create(AprsAddress source, AprsData data, AprsAddress? destination = null, IEnumerable<AprsPathEntry>? path = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        RequireAddress(source);
        if (data is AprsMicEReport micE)
        {
            if (destination is not null)
            {
                throw new AprsEncodeException("a Mic-E report computes its destination address, so none can be given");
            }

            return new AprsPacket(source, MicECodec.EncodeDestination(micE), path?.ToList() ?? [], micE.EncodeInformation(), micE, []);
        }

        AprsAddress to = destination ?? AprsAddress.Parse(AprsStation.DefaultDestination);
        RequireAddress(to);
        return new AprsPacket(source, to, path?.ToList() ?? [], data.EncodeInformation(), data, []);
    }

    /// <summary>
    /// Builds a packet from text addresses and a TNC2 path such as <c>WIDE1-1,WIDE2-1</c>. A text
    /// that is not an address is an <see cref="AprsEncodeException"/>.
    /// </summary>
    public static AprsPacket Create(string source, AprsData data, string? destination = null, string path = "")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(path);
        return Create(
            AprsStation.ParseAddress(source),
            data,
            destination is null ? null : AprsStation.ParseAddress(destination),
            AprsStation.ParsePath(path));
    }

    private static void RequireAddress(AprsAddress address)
    {
        if (string.IsNullOrEmpty(address.Value))
        {
            throw new AprsEncodeException("address is empty");
        }
    }

    // ------------------------------------------------------------------ writing

    /// <summary>The packet in TNC2 form as bytes (the information field may hold non-ASCII bytes).</summary>
    public byte[] ToTnc2() => Tnc2Codec.Format(Source, Destination, Path, information);

    /// <summary>
    /// The packet as an AX.25 UI frame without flags or FCS, ready for a KISS TNC. Every address
    /// must be a valid AX.25 address (<see cref="AprsAddress.IsAx25"/>) and the path at most 8 entries.
    /// </summary>
    public byte[] ToAx25Frame() => Ax25Codec.Encode(Source, Destination, Path, information);

    /// <summary>The packet in TNC2 form. The information field is shown as UTF-8 (Latin-1 where invalid).</summary>
    public override string ToString() => Text.ForDisplay(ToTnc2());

    /// <summary>
    /// Two packets are equal when they have the same source, destination, path (including used
    /// flags) and information bytes: the same packet on the air. <see cref="Data"/> and
    /// <see cref="Diagnostics"/> follow from those and are not compared.
    /// </summary>
    public bool Equals(AprsPacket? other) =>
        other is not null && Source == other.Source && Destination == other.Destination
        && Path.SequenceEqual(other.Path) && information.AsSpan().SequenceEqual(other.information);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as AprsPacket);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Source);
        hash.Add(Destination);
        hash.AddBytes(information);
        return hash.ToHashCode();
    }

    /// <summary>Value equality; see <see cref="Equals(AprsPacket?)"/>.</summary>
    public static bool operator ==(AprsPacket? left, AprsPacket? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(AprsPacket? left, AprsPacket? right) => !(left == right);
}
