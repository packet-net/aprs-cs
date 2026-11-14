namespace Packet.Aprs;

/// <summary>
/// Thrown when the library will not write what it was given: a value APRS12c cannot carry, a
/// combination of fields that would not read back as it was written, or a text address that is not
/// one. Every such refusal, from <see cref="AprsData.ToInformationField"/>,
/// <see cref="AprsPacket.Create(AprsAddress, AprsData, AprsAddress?, IEnumerable{AprsPathEntry}?)"/>, <see cref="AprsPacket.ToAx25Frame"/>,
/// <see cref="AprsBuilder.Build"/> and the builder methods that check at once, is this type, so a
/// caller can tell a refusal from a programming error: a null argument is an
/// <see cref="ArgumentNullException"/>, and a builder that was never given a position its packet
/// needs throws <see cref="InvalidOperationException"/>. Something else it was never given, such as a
/// symbol, is a refusal like any other.
/// </summary>
/// <remarks>
/// The <see cref="Exception.Message"/> says why, in words that may change between versions; it is not
/// part of the stable API; <see cref="Code"/> is. It derives from <see cref="ArgumentException"/> for callers that already
/// catch that.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1032:Implement standard exception constructors",
    Justification = "Only the library throws it, so it has no public constructors.")]
public sealed class AprsEncodeException : ArgumentException
{
    internal AprsEncodeException(string message)
        : this(AprsRefusalCode.DoesNotReadBack, message)
    {
    }

    internal AprsEncodeException(string message, Exception innerException)
        : this(AprsRefusalCode.DoesNotReadBack, message, innerException)
    {
    }

    internal AprsEncodeException(AprsRefusalCode code, string message)
        : base(message)
    {
        Code = code;
    }

    internal AprsEncodeException(AprsRefusalCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>
    /// The rule the refusal is under, for a caller that acts on the reason rather than reading the
    /// message. The codes are shared with the other APRS libraries and the vectors. A code this
    /// version does not know can come from a later 1.x release, so a <c>switch</c> needs a default.
    /// </summary>
    public AprsRefusalCode Code { get; }
}
