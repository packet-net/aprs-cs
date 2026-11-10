namespace Packet.Aprs;

/// <summary>
/// Thrown when the library will not write what it was given: a value APRS12c cannot carry, a
/// combination of fields that would not read back as it was written, or a text address that is not
/// one. Every such refusal, from <see cref="AprsData.ToInformationField"/>,
/// <see cref="AprsPacket.Create(AprsAddress, AprsData, AprsAddress?, IEnumerable{AprsPathEntry}?)"/>, <see cref="AprsPacket.ToAx25Frame"/>,
/// <see cref="AprsBuilder.Build"/> and the builder methods that check at once, is this type, so a
/// caller can tell a refusal from a programming error: a null argument is an
/// <see cref="ArgumentNullException"/>, and a builder that was never given something its packet
/// needs, such as a symbol, throws <see cref="InvalidOperationException"/>.
/// </summary>
/// <remarks>
/// The <see cref="Exception.Message"/> says why, in words that may change between versions; it is not
/// part of the stable API. It derives from <see cref="ArgumentException"/> for callers that already
/// catch that.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1032:Implement standard exception constructors",
    Justification = "Only the library throws it, and a reason code is to be added to its constructors later.")]
public sealed class AprsEncodeException : ArgumentException
{
    internal AprsEncodeException(string message)
        : base(message)
    {
    }

    internal AprsEncodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
