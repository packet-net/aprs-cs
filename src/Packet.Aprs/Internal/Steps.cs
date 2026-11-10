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
    public static long Count(double value, double stepsPerUnit = 1) => checked((long)Nearest(value * stepsPerUnit));

    /// <summary>
    /// <paramref name="value"/> x <paramref name="stepsPerUnit"/> cut to a whole number of steps
    /// (towards zero for a value that is not negative), where a value within
    /// <see cref="HalfTolerance"/> below a step is that step: the step the decimal text means.
    /// </summary>
    public static long Cut(double value, double stepsPerUnit) => checked((long)Math.Floor((value * stepsPerUnit) + HalfTolerance));

    /// <summary><paramref name="value"/> rounded to whole units.</summary>
    public static double Whole(double value) => Nearest(value);

    /// <summary>A decimal rounded to whole units, halves away from zero (a decimal is exact, so a half is a half).</summary>
    public static decimal Whole(decimal value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
