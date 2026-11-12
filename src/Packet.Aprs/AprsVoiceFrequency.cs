namespace Packet.Aprs;

/// <summary>How a <see cref="AprsVoiceFrequency"/> specifies access tone or squelch (APRS12c §18).</summary>
public enum AprsToneType
{
    /// <summary><c>Tnnn</c>: CTCSS tone required to access the repeater (it does not send one back).</summary>
    Tone,

    /// <summary><c>Cnnn</c>: CTCSS in both directions; the repeater also transmits the tone.</summary>
    Ctcss,

    /// <summary><c>Dnnn</c>: DCS code.</summary>
    Dcs,

    /// <summary><c>1750</c>: 1750 Hz tone burst.</summary>
    ToneBurst,

    /// <summary><c>Toff</c>: no tone, no DCS.</summary>
    Off,
}

/// <summary>
/// A voice frequency advertised in a position, object or Mic-E comment, in the fixed formats of
/// APRS12c §18 (the APRS Frequency Specification): <c>FFF.FFFMHz</c> at the start of the comment,
/// then optional tone, offset and range fields each introduced by a space.
/// </summary>
/// <remarks>
/// Radios with a TUNE / QSY button parse exactly this layout, which is why it has to be exact:
/// <c>146.520</c> without <c>MHz</c>, or the fields run together, is not recognised (UAP §3.3, §5.14).
/// </remarks>
public sealed record AprsVoiceFrequency
{
    /// <summary>The frequency in MHz: whole kilohertz (or 10 kilohertz) when decoded; rounded once to
    /// the field's step when encoded.</summary>
    public required double FrequencyMHz { get; init; }

    /// <summary>True when written to 10 kHz with a space before MHz (<c>146.52 MHz</c>) rather than
    /// to 1 kHz (<c>146.520MHz</c>).</summary>
    public bool TenKilohertzResolution { get; init; }

    /// <summary>The tone or squelch type, or null if none was given.</summary>
    public AprsToneType? ToneType { get; init; }

    /// <summary>The tone frequency in whole Hz (tenths are dropped on air, <c>T088</c> = 88.5 Hz) or
    /// the DCS code; null for <see cref="AprsToneType.Off"/> and <see cref="AprsToneType.ToneBurst"/>.</summary>
    public int? ToneValue { get; init; }

    /// <summary>True when the tone field was lower case, which signals narrow-band FM.</summary>
    public bool Narrow { get; init; }

    /// <summary>The transmit offset in kHz, e.g. -600, or null for the region's standard offset.
    /// Carried on air in tens of kHz (<c>-060</c>).</summary>
    public int? OffsetKHz { get; init; }

    /// <summary>The nominal range, or null.</summary>
    public int? Range { get; init; }

    /// <summary>True if <see cref="Range"/> is in kilometres (<c>R25k</c>), false for miles (<c>R25m</c>).</summary>
    public bool RangeInKilometres { get; init; }

    /// <summary>The frequency as the encoder writes it: <see cref="FrequencyMHz"/> rounded once to the field's step, a half going up (15 significant digits, so 145.7255 is a half).</summary>
    internal decimal RoundedMHz()
    {
        Internal.NotANumber.Refuse(FrequencyMHz, "frequency", orInfinity: true);
        return Math.Abs(FrequencyMHz) < 1e9
            ? decimal.Round((decimal)FrequencyMHz, TenKilohertzResolution ? 2 : 3, MidpointRounding.AwayFromZero)
            : throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "frequency must be 0 to below 24300 MHz");
    }

    /// <summary>This frequency with <see cref="FrequencyMHz"/> rounded as the encoder writes it, which is what decoding the written form gives back.</summary>
    internal AprsVoiceFrequency Rounded() => this with { FrequencyMHz = (double)RoundedMHz() };

    internal void Validate()
    {
        Internal.NotANumber.Refuse(FrequencyMHz, "frequency", orInfinity: true);
        if (FrequencyMHz is < 0 or >= 24300)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "frequency must be 0 to below 24300 MHz");
        }

        bool toneNeedsValue = ToneType is AprsToneType.Tone or AprsToneType.Ctcss or AprsToneType.Dcs;
        if (toneNeedsValue != ToneValue.HasValue)
        {
            throw new AprsEncodeException(AprsRefusalCode.MissingField, "tone value 0-999 is required for Tone, Ctcss and Dcs and not allowed otherwise");
        }

        if (ToneValue is < 0 or > 999)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "tone value 0-999 is required for Tone, Ctcss and Dcs and not allowed otherwise");
        }

        if (OffsetKHz is { } o && Math.Abs(o) <= 9990 && o % 10 != 0)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueHasNoForm, "offset must be a multiple of 10 kHz up to 9.99 MHz");
        }

        if (OffsetKHz is { } o2 && Math.Abs(o2) > 9990)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "offset must be a multiple of 10 kHz up to 9.99 MHz");
        }

        if (Range is < 0 or > 99)
        {
            throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "range must be 0-99");
        }
    }
}
