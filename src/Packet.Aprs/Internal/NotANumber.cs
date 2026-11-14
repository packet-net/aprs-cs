namespace Packet.Aprs.Internal;

/// <summary>The not-a-number refusal, checked ahead of a value's range so that NaN is never reported as out of range.</summary>
internal static class NotANumber
{
    /// <summary>
    /// Refuses a NaN or an infinity: "NaN or an infinity is not-a-number at every check that also
    /// catches it" (vectors README, Refusal reasons).
    /// </summary>
    public static void Refuse(double? value, string what)
    {
        if (value is { } v && !double.IsFinite(v))
        {
            throw new AprsEncodeException(AprsRefusalCode.NotANumber, $"{what} is not a number");
        }
    }
}
