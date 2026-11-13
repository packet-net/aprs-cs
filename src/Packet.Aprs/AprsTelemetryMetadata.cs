using System.Globalization;
using Packet.Aprs.Internal;

namespace Packet.Aprs;

/// <summary>
/// Telemetry names (<c>PARM.</c>): a message to the telemetry station itself naming its 5 analog
/// and 8 digital channels (APRS12c §13 Parameter Name Message). The list may stop early.
/// </summary>
public sealed record AprsTelemetryParameterNames : AprsMessage
{
    /// <summary>Up to 13 names: A1-A5 then B1-B8.</summary>
    public required IReadOnlyList<string> Names { get; init => field = EquatableList<string>.Of(value); }

    private protected override void EncodeText(InfoWriter writer) => MessageCodec.WriteList(writer, "PARM.", Names, this);
}

/// <summary>
/// Telemetry units and labels (<c>UNIT.</c>): units for the 5 analog channels and labels for the
/// 8 digital ones (APRS12c §13 Unit/Label Message). The list may stop early.
/// </summary>
public sealed record AprsTelemetryUnits : AprsMessage
{
    /// <summary>Up to 13 entries: A1-A5 units then B1-B8 labels.</summary>
    public required IReadOnlyList<string> Units { get; init => field = EquatableList<string>.Of(value); }

    private protected override void EncodeText(InfoWriter writer) => MessageCodec.WriteList(writer, "UNIT.", Units, this);
}

/// <summary>
/// Telemetry scaling (<c>EQNS.</c>): three coefficients a, b, c per analog channel, giving
/// a x v^2 + b x v + c for raw value v (APRS12c §13 Equation Coefficients Message).
/// </summary>
public sealed record AprsTelemetryCoefficients : AprsMessage
{
    /// <summary>
    /// Up to 15 values in the order a1, b1, c1, a2, b2, c2 ... a5, b5, c5. The list may stop early.
    /// Each is a finite floating-point number (an exponent is allowed, <c>10E60</c>).
    /// </summary>
    public required IReadOnlyList<double> Coefficients { get; init => field = EquatableList<double>.Of(value); }

    /// <summary>
    /// How each of <see cref="Coefficients"/> was written when decoded (<c>.53</c>, <c>10E60</c>),
    /// which the encoder writes back for any entry that still means the same number; empty for
    /// coefficients built in code. Not part of equality.
    /// </summary>
    public IReadOnlyList<string> CoefficientsText { get; init => field = EquatableList<string>.Of(value); } = EquatableList<string>.Empty;

    /// <summary>Applies channel <paramref name="channel"/>'s (1-5) coefficients to a raw value, as a
    /// telemetry report carries it; a missing coefficient counts as a=0, b=1, c=0.</summary>
    public double Scale(int channel, double raw)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channel, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channel, 5);
        int i = (channel - 1) * 3;
        double a = i < Coefficients.Count ? Coefficients[i] : 0;
        double b = i + 1 < Coefficients.Count ? Coefficients[i + 1] : 1;
        double c = i + 2 < Coefficients.Count ? Coefficients[i + 2] : 0;
        return (a * raw * raw) + (b * raw) + c;
    }

    private protected override void EncodeText(InfoWriter writer)
    {
        if (Coefficients.Any(c => !double.IsFinite(c)))
        {
            throw new AprsEncodeException(AprsRefusalCode.NotANumber, "an EQNS coefficient is a finite number");
        }

        if (Coefficients.Count > 15)
        {
            // A list longer than its field (vectors README, Refusal reasons).
            throw new AprsEncodeException(AprsRefusalCode.TooLong, "EQNS carries 1 to 15 coefficients");
        }

        if (Coefficients.Count < 1)
        {
            throw new AprsEncodeException(AprsRefusalCode.Malformed, "EQNS carries 1 to 15 coefficients");
        }

        MessageCodec.WriteList(
            writer,
            "EQNS.",
            [.. Coefficients.Select((c, i) => i < CoefficientsText.Count && MessageCodec.CoefficientMeans(CoefficientsText[i], c) ? CoefficientsText[i] : PlainNumber.Format(c))],
            this);
    }

    /// <summary>The same coefficients, however they were written.</summary>
    /// <param name="other">The message to compare with.</param>
    public bool Equals(AprsTelemetryCoefficients? other) =>
        other is not null && base.Equals(other) && Coefficients.SequenceEqual(other.Coefficients);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Coefficients.Count);
}

/// <summary>
/// Telemetry bit sense and project name (<c>BITS.</c>): which state of each digital channel
/// matches its label, then a project title (APRS12c §13 Bit Sense/Project Name Message).
/// </summary>
public sealed record AprsTelemetryBitSense : AprsMessage
{
    /// <summary>The 8 bit-sense flags as sent, B1 first: eight 0 or 1 characters.</summary>
    public required string Bits { get; init; }

    /// <summary>The project title, up to 23 characters (APRS12c ch. 13), or empty. Decoding accepts a longer one.</summary>
    public string Project { get; init; } = "";

    private protected override void EncodeText(InfoWriter writer)
    {
        Internal.Text.RequireNoLineBreaks(Project);
        MessageCodec.RequireNoBrace(Project);
        if (Project.EnumerateRunes().Count() > 23)
        {
            throw new AprsEncodeException(AprsRefusalCode.TooLong, "the telemetry project title is limited to 23 characters (APRS12c ch. 13)");
        }

        if (Bits is not { Length: 8 } || Bits.Any(c => c is not ('0' or '1')))
        {
            throw new AprsEncodeException(AprsRefusalCode.Malformed, "the bit sense is 8 characters, each 0 or 1, B1 first");
        }

        writer.Ascii("BITS.").Ascii(Bits);
        if (Project.Length > 0)
        {
            writer.Char(',').Utf8(Project);
        }

        WriteMessageId(writer);
    }
}
