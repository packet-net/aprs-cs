namespace Packet.Aprs.Internal;

/// <summary>The <c>!DAO!</c> precision and datum extension (APRS12c §5).</summary>
internal static class DaoCodec
{
    /// <summary>
    /// Recognises a 5-byte <c>!DAO!</c> at <paramref name="s"/> and reports the extra latitude and
    /// longitude in minutes. Forms: <c>!Wdd!</c> (upper-case datum, a decimal digit each: thousandths of
    /// a minute), <c>!wBB!</c> (lower-case datum, a base-91 character each: v/91 hundredths of a minute),
    /// <c>!D  !</c> (datum only).
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> s, out AprsDao dao, out double extraLatMinutes, out double extraLonMinutes)
    {
        dao = default;
        extraLatMinutes = 0;
        extraLonMinutes = 0;
        if (s.Length != 5 || s[0] != (byte)'!' || s[4] != (byte)'!')
        {
            return false;
        }

        byte d = s[1];
        byte a = s[2];
        byte o = s[3];
        if (a == (byte)' ' && o == (byte)' ' && Text.IsAlphanumeric(d))
        {
            dao = new AprsDao(char.ToUpperInvariant((char)d), AprsDaoPrecision.None);
            return true;
        }

        if (Text.IsUpper(d) && Text.IsDigit(a) && Text.IsDigit(o))
        {
            dao = new AprsDao((char)d, AprsDaoPrecision.Thousandths);
            extraLatMinutes = (a - '0') * 0.001;
            extraLonMinutes = (o - '0') * 0.001;
            return true;
        }

        if (Text.IsLower(d) && Base91.IsDigit(a) && Base91.IsDigit(o))
        {
            dao = new AprsDao((char)(d - 32), AprsDaoPrecision.Base91);
            extraLatMinutes = (a - 33) / 91.0 * 0.01;
            extraLonMinutes = (o - 33) / 91.0 * 0.01;
            return true;
        }

        return false;
    }

    /// <summary>Adds the DAO's extra minutes to a position, away from zero in each hemisphere.</summary>
    public static AprsPosition Apply(AprsPosition p, double extraLatMinutes, double extraLonMinutes) =>
        p with
        {
            Latitude = p.Latitude + (Math.CopySign(1, p.Latitude) * extraLatMinutes / 60),
            Longitude = p.Longitude + (Math.CopySign(1, p.Longitude) * extraLonMinutes / 60),
        };

    /// <summary>
    /// Rounds an absolute coordinate (degrees) to the finest unit the report can carry and returns
    /// the whole hundredths of a minute for the DDMM.hh field; <paramref name="extra"/> is the DAO
    /// digit (0-9) or base-91 value (0-90) for the remainder.
    /// </summary>
    public static long SplitForDao(double absDegrees, AprsDao? dao, out int extra)
    {
        switch (dao?.Precision)
        {
            case AprsDaoPrecision.Thousandths:
            {
                long thousandths = Steps.Count(absDegrees, 60 * 1000);
                extra = (int)(thousandths % 10);
                return thousandths / 10;
            }

            case AprsDaoPrecision.Base91:
            {
                long units = Steps.Count(absDegrees, 60 * 100 * 91);
                extra = (int)(units % 91);
                return units / 91;
            }

            default:
                extra = 0;
                return Steps.Count(absDegrees, 60 * 100);
        }
    }

    public static void Write(InfoWriter w, AprsPosition position, AprsDao dao)
    {
        dao.Validate();
        if (position.Ambiguity != 0)
        {
            // Even !W  ! reads back with dao-with-ambiguity: a DAO adds precision to an ambiguous
            // position, which contradicts it, so no clean form exists (vectors ruling F4).
            throw new AprsEncodeException(AprsRefusalCode.FieldNotAllowed, "a DAO contradicts position ambiguity, whatever its precision");
        }

        SplitForDao(Math.Abs(position.Latitude), dao, out int lat);
        SplitForDao(Math.Abs(position.Longitude), dao, out int lon);
        w.Char('!');
        switch (dao.Precision)
        {
            case AprsDaoPrecision.Thousandths:
                w.Char(dao.Datum).Digits(lat, 1).Digits(lon, 1);
                break;
            case AprsDaoPrecision.Base91:
                w.Char(char.ToLowerInvariant(dao.Datum)).Byte((byte)(lat + 33)).Byte((byte)(lon + 33));
                break;
            default:
                w.Char(dao.Datum).Ascii("  ");
                break;
        }

        w.Char('!');
    }
}
