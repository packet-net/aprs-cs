using System.Globalization;
using System.Text;

namespace Packet.Aprs.Internal;

/// <summary>
/// A number given as data rather than as the text sent (vectors ruling F11): the shortest decimal
/// that reads back as the same number, with no exponent and no <c>+</c>, a <c>0</c> before a point
/// (<c>0.53</c>) and no trailing <c>.0</c> (<c>190.0</c> is <c>190</c>; <c>1e61</c> is a 1 and
/// 61 zeros).
/// </summary>
internal static class PlainNumber
{
    public static string Format(decimal value)
    {
        // Dividing by 1 with trailing zeros removes the scale: 190.0m becomes 190m.
        decimal normal = value / 1.0000000000000000000000000000m;
        string s = normal.ToString(CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }

    public static string Format(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new AprsEncodeException(AprsRefusalCode.NotANumber, "a number is finite");
        }

        if (value == 0)
        {
            return "0";
        }

        // "R" is the shortest round-trip form, which may carry an exponent (1E+61, 1E-07).
        string r = value.ToString("R", CultureInfo.InvariantCulture);
        int e = r.IndexOfAny(['E', 'e']);
        if (e < 0)
        {
            return r;
        }

        bool negative = r[0] == '-';
        string mantissa = r[(negative ? 1 : 0)..e];
        int exponent = int.Parse(r.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        int dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        string digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
        int point = (dot < 0 ? mantissa.Length : dot) + exponent; // digits before the point
        var sb = new StringBuilder();
        if (negative)
        {
            sb.Append('-');
        }

        if (point <= 0)
        {
            sb.Append("0.").Append('0', -point).Append(digits);
        }
        else if (point >= digits.Length)
        {
            sb.Append(digits).Append('0', point - digits.Length);
        }
        else
        {
            sb.Append(digits, 0, point).Append('.').Append(digits, point, digits.Length - point);
        }

        return sb.ToString();
    }
}
