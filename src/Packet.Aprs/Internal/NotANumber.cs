namespace Packet.Aprs.Internal;

/// <summary>The not-a-number refusal, checked ahead of a value's range so that NaN is never reported as out of range.</summary>
internal static class NotANumber
{
    /// <summary>Refuses a NaN, or an infinity when <paramref name="orInfinity"/>.</summary>
    public static void Refuse(double? value, string what, bool orInfinity = false)
    {
        if (value is { } v && (double.IsNaN(v) || (orInfinity && double.IsInfinity(v))))
        {
            throw new AprsEncodeException(AprsRefusalCode.NotANumber, $"{what} is not a number");
        }
    }
}
