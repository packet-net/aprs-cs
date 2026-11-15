namespace Packet.Aprs.Internal;

internal static class Units
{
    public const double MphPerKnot = 1.852 / 1.609344;
    public const double FeetPerMetre = 1 / 0.3048;

    /// <summary>
    /// A converted value: a finite value that overflows in the arithmetic stays a (huge) finite number,
    /// which the field's range refuses as out of range; only a value given as not-a-number is one.
    /// </summary>
    public static double Scaled(double given, double converted) =>
        double.IsFinite(given) && !double.IsFinite(converted) ? Math.CopySign(double.MaxValue, converted) : converted;

    public static double KnotsToMph(double knots) => Scaled(knots, knots * MphPerKnot);

    public static double MphToKnots(double mph) => mph / MphPerKnot;
}
