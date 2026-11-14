namespace Packet.Aprs.Internal;

/// <summary>
/// Rounding a value to the steps its field holds (vectors ruling F2, interpretations.md "Values
/// between steps are rounded"): to the nearest step, halves away from zero. A half is judged on the
/// value in binary floating point, where one within <see cref="HalfTolerance"/> of a step's half,
/// measured in steps, is the half: decimal text such as 58.585 rarely has an exact binary value, and
/// minutes derived from degrees almost never do, so the half the text means wins without decimal
/// arithmetic. A value derived from another is computed from the value given and rounded once.
/// </summary>
internal static class Steps
{
    /// <summary>How near a step's half, in steps, a value counts as the half.</summary>
    public const double HalfTolerance = 1e-6;

    /// <summary>The nearest whole number of steps to <paramref name="steps"/>, halves away from zero.</summary>
    public static double Nearest(double steps)
    {
        if (double.IsNaN(steps) || double.IsInfinity(steps))
        {
            return steps;
        }

        double floor = Math.Floor(steps);
        double fraction = steps - floor;
        if (Math.Abs(fraction - 0.5) <= HalfTolerance)
        {
            return steps >= 0 ? floor + 1 : floor;
        }

        return fraction < 0.5 ? floor : floor + 1;
    }

    /// <summary><see cref="Nearest(double)"/> of <paramref name="value"/> x <paramref name="stepsPerUnit"/>, as a whole number of steps.</summary>
    public static long Count(double value, double stepsPerUnit = 1) => Whole(Nearest(value * stepsPerUnit), value);

    /// <summary>
    /// <paramref name="value"/> x <paramref name="stepsPerUnit"/> cut to a whole number of steps
    /// (towards zero for a value that is not negative), where a value within
    /// <see cref="HalfTolerance"/> below a step is that step: the step the decimal text means.
    /// </summary>
    public static long Cut(double value, double stepsPerUnit) => Whole(Math.Floor((value * stepsPerUnit) + HalfTolerance), value);

    /// <summary>
    /// A count of steps as a whole number. A value that is not a number, an infinity included,
    /// is <c>not-a-number</c>, and one too large for any field <c>value-out-of-range</c>, wherever
    /// it is met (vectors README, Refusal reasons), never an overflow.
    /// </summary>
    private static long Whole(double steps, double value)
    {
        NotANumber.Refuse(value, "a value");
        return steps is >= -9.2e18 and <= 9.2e18
            ? (long)steps
            : throw new AprsEncodeException(AprsRefusalCode.ValueOutOfRange, "a value too large for any field");
    }

    /// <summary><paramref name="value"/> rounded to whole units.</summary>
    public static double Whole(double value) => Nearest(value);

    /// <summary>A decimal rounded to whole units, halves away from zero (a decimal is exact, so a half is a half).</summary>
    public static decimal Whole(decimal value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
