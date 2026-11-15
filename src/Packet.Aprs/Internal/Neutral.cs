using System.Globalization;

namespace Packet.Aprs.Internal;

/// <summary>
/// The vectors' data form (README, "The data form") as a flat map from a field's path to its value,
/// for the read-back's one comparison. A field that is null, an empty string or an empty list is
/// left out, a boolean is there only when true, and a list item is a value of its own (an empty
/// telemetry name, a capability's empty value and a <c>null</c> channel compare as themselves).
/// The data a caller gave is made with <c>given</c> set: a number its form holds in steps is then
/// the value rounded once, to the step of the form actually written (README, Read-back, allowance
/// 1), and a value the form must carry where the data leaves it out is added (allowance 2); the
/// data decoded from the bytes is made as it is.
/// </summary>
internal sealed class Neutral
{
    /// <summary>A list item that is <c>null</c> (an analog channel sent empty).</summary>
    internal sealed class Null
    {
        public static readonly Null Value = new();
    }

    private readonly bool given;
    private readonly Dictionary<string, object> map;
    private int depth;

    private Neutral(bool given, Dictionary<string, object>? map = null)
    {
        this.given = given;
        this.map = map ?? new Dictionary<string, object>(StringComparer.Ordinal);
    }

    /// <summary>The fields of data as given, with the rounding its form writes.</summary>
    public static Dictionary<string, object> Given(AprsData data) => Build(data, given: true);

    /// <summary>The fields of data as decoded.</summary>
    public static Dictionary<string, object> Decoded(AprsData data) => Build(data, given: false);

    private static Dictionary<string, object> Build(AprsData data, bool given)
    {
        var n = new Neutral(given);
        n.Data("", data);
        return n.map;
    }

    // ------------------------------------------------------------------ putting fields

    private void S(string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            map[key] = value;
        }
    }

    /// <summary>A value that is a value even when empty (a list item, <c>reply_ack</c>).</summary>
    private void V(string key, string? value)
    {
        if (value is not null)
        {
            map[key] = value;
        }
    }

    private void F(string key, bool value)
    {
        if (value)
        {
            map[key] = true;
        }
    }

    private void I(string key, long? value)
    {
        if (value is { } v)
        {
            map[key] = v;
        }
    }

    private void D(string key, double? value)
    {
        if (value is { } v)
        {
            map[key] = v;
        }
    }

    /// <summary>A number its form holds in steps: as given, the value written; as decoded, the value read.</summary>
    private void Q(string key, double? value, Func<double, double> written)
    {
        if (value is { } v)
        {
            map[key] = given ? written(v) : v;
        }
    }

    private void Nil(string key) => map[key] = Null.Value;

    // ------------------------------------------------------------------ data

    private void Data(string p, AprsData data)
    {
        S(p + "type", TypeName(data));
        switch (data)
        {
            case AprsPositionReport r:
                S(p + "timestamp", r.Timestamp?.ToString());
                F(p + "messaging", r.MessagingCapable);
                Positioned(p, r);
                break;
            case AprsMicEReport m:
                S(p + "mic_e_message", Kebab(m.MicEMessage.ToString()));
                F(p + "old_data", m.IsOldData);
                S(p + "type_code", m.TypeCode?.ToString());
                S(p + "device_suffix", m.DeviceSuffix);
                S(p + "locator", m.Locator);
                if (m.LegacyTelemetry is { Count: > 0 } legacy)
                {
                    for (int i = 0; i < legacy.Count; i++)
                    {
                        I($"{p}legacy_telemetry[{i}]", legacy[i]);
                    }
                }

                I(p + "destination_ssid", m.DestinationSsid == 0 ? null : m.DestinationSsid);
                Positioned(p, m);
                break;
            case AprsObjectReport o:
                S(p + "name", o.Name);
                F(p + "killed", o.IsKilled);
                S(p + "timestamp", o.Timestamp?.ToString());
                Positioned(p, o);
                break;
            case AprsItemReport it:
                S(p + "name", it.Name);
                F(p + "killed", it.IsKilled);
                Positioned(p, it);
                break;
            case AprsMessage msg:
                Message(p, msg);
                break;
            case AprsStatusReport s:
                S(p + "timestamp", s.Timestamp?.ToString());
                S(p + "locator", s.Locator);
                S(p + "symbol", s.Symbol?.ToString());
                if (s.BeamHeading is { } b)
                {
                    S(p + "beam.heading_code", b.HeadingCode.ToString());
                    S(p + "beam.power_code", b.PowerCode.ToString());
                }

                S(p + "text", s.Text);
                break;
            case AprsTelemetryReport t:
                S(p + "sequence", t.Sequence);
                for (int i = 0; i < t.Analog.Count; i++)
                {
                    if (t.Analog[i] is { } v)
                    {
                        D($"{p}analog[{i}]", v);
                    }
                    else
                    {
                        Nil($"{p}analog[{i}]");
                    }
                }

                S(p + "bits", t.Bits);
                S(p + "comment", t.Comment);
                break;
            case AprsWeatherReport w:
                S(p + "timestamp", w.Timestamp.ToString());
                Weather(p + "weather.", w.Weather, windInCs: false);
                S(p + "comment", w.Comment);
                break;
            case AprsRawWeatherReport raw:
                S(p + "format", Kebab(raw.Format.ToString()));
                S(p + "data", raw.Data);
                break;
            case AprsNmeaReport n:
                S(p + "sentence", n.Sentence);
                F(p + "has_checksum", n.HasChecksum);
                if (n.Position is { } np)
                {
                    D(p + "latitude", np.Latitude);
                    D(p + "longitude", np.Longitude);
                }

                S(p + "fix", n.FixValid is { } fv ? (fv ? "valid" : "invalid") : null);
                D(p + "course_degrees", n.CourseDegrees);
                D(p + "speed_knots", n.SpeedKnots);
                D(p + "altitude_m", n.AltitudeMetres);
                S(p + "time", n.TimeText is { } time ? NmeaTime(time) : n.Time?.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture));
                S(p + "waypoint", n.WaypointName);
                S(p + "comment", n.Comment);
                break;
            case AprsMaidenheadBeacon mb:
                S(p + "locator", mb.Locator);
                S(p + "comment", mb.Comment);
                break;
            case AprsGeneralQuery q:
                S(p + "query_type", q.QueryType);
                if (q.Footprint is { } f)
                {
                    D(p + "footprint.latitude", f.Latitude);
                    D(p + "footprint.longitude", f.Longitude);
                    Q(p + "footprint.radius_miles", f.RadiusMiles, Steps.Whole);
                }

                break;
            case AprsStationCapabilities c:
                for (int i = 0; i < c.Capabilities.Count; i++)
                {
                    V($"{p}capabilities[{i}].token", c.Capabilities[i].Token);
                    V($"{p}capabilities[{i}].value", c.Capabilities[i].Value);
                }

                break;
            case AprsThirdPartyTraffic tp:
                ThirdParty(p, tp);
                break;
            case AprsUserDefinedData u:
                S(p + "user_id", u.UserId.ToString());
                S(p + "packet_type", u.PacketType.ToString());
                S(p + "data", System.Text.Encoding.Latin1.GetString([.. u.Data]));
                break;
            case AprsTestData td:
                S(p + "data", td.Data);
                break;
            case AprsAgreloDfReport ag:
                I(p + "bearing_degrees", ag.BearingDegrees);
                I(p + "quality", ag.Quality);
                break;
            case AprsUnrecognizedData un:
                S(p + "reason", Kebab(un.Reason.ToString()));
                S(p + "raw", System.Text.Encoding.Latin1.GetString([.. un.Raw]));
                break;
            default:
                throw new NotSupportedException($"no neutral form for {data.GetType().Name}");
        }
    }

    /// <summary>
    /// A third-party packet: its inner source, destination and path entry by entry, and its data
    /// at every depth. The inner information field is written as it stands (it was read back when
    /// it was written, or is a decoded packet's, as received), so what the outer packet must give
    /// back is that field's own reading under the inner header.
    /// </summary>
    private void ThirdParty(string p, AprsThirdPartyTraffic tp)
    {
        AprsPacket inner = tp.Packet;
        V(p + "packet.source", inner.Source.Value);
        V(p + "packet.destination", inner.Destination.Value);
        for (int i = 0; i < inner.Path.Count; i++)
        {
            V($"{p}packet.path[{i}]", inner.Path[i].Used ? inner.Path[i].Address.Value + "*" : inner.Path[i].Address.Value);
        }

        if (given)
        {
            // Read at the depth a decoder reads it at, so a level it would not read is not read here.
            var ctx = new DecodeContext(AprsParseOptions.Lenient) { Depth = depth + 1 };
            AprsPacket read = AprsPacket.Build(new Tnc2Codec.Header(inner.Source, inner.Destination, []), inner.Information.Span, ctx);
            new Neutral(given: false, map) { depth = depth + 1 }.Data(p + "packet.data.", read.Data);
        }
        else
        {
            Data(p + "packet.data.", inner.Data);
        }
    }

    // ------------------------------------------------------------------ messages

    private void Message(string p, AprsMessage msg)
    {
        S(p + "addressee", msg.Addressee);
        switch (msg)
        {
            case AprsTextMessage t:
                S(p + "text", t.Text);
                V(p + "reply_ack", t.ReplyAck);
                break;
            case AprsMessageAck a:
                S(p + "acked_id", a.AcknowledgedId);
                V(p + "reply_ack", a.ReplyAck);
                break;
            case AprsMessageReject r:
                S(p + "rejected_id", r.RejectedId);
                V(p + "reply_ack", r.ReplyAck);
                break;
            case AprsBulletin b:
                S(p + "text", b.Text);
                break;
            case AprsNwsBulletin n:
                S(p + "text", n.Text);
                break;
            case AprsTelemetryParameterNames pn:
                for (int i = 0; i < pn.Names.Count; i++)
                {
                    V($"{p}names[{i}]", pn.Names[i]);
                }

                break;
            case AprsTelemetryUnits un:
                for (int i = 0; i < un.Units.Count; i++)
                {
                    V($"{p}units[{i}]", un.Units[i]);
                }

                break;
            case AprsTelemetryCoefficients co:
                for (int i = 0; i < co.Coefficients.Count; i++)
                {
                    D($"{p}coefficients[{i}]", co.Coefficients[i]);
                }

                break;
            case AprsTelemetryBitSense bs:
                S(p + "bits", bs.Bits);
                S(p + "project", bs.Project);
                break;
            case AprsDirectedQuery dq:
                S(p + "query_type", dq.QueryType);
                S(p + "target", dq.Target);
                break;
        }

        S(p + "message_id", msg.MessageId);
    }

    // ------------------------------------------------------------------ positioned data

    private void Positioned(string p, AprsPositionedData d)
    {
        bool micE = d is AprsMicEReport;
        bool compressed = d.IsCompressed && !micE;
        (double latitude, double longitude) = given ? WrittenPosition(d, compressed) : (d.Position.Latitude, d.Position.Longitude);
        D(p + "latitude", latitude);
        D(p + "longitude", longitude);
        I(p + "ambiguity", d.Position.Ambiguity == 0 ? null : d.Position.Ambiguity);
        S(p + "symbol", d.Symbol.ToString());
        F(p + "compressed", d.IsCompressed);
        if (d.CompressionType is { } ct)
        {
            Compression(p, ct);
        }

        PositionedMotion(p, d, micE, compressed);
        if (d.Phg is { } phg)
        {
            I(p + "phg.power", phg.PowerCode);
            I(p + "phg.height", phg.HeightCode);
            I(p + "phg.gain", phg.GainCode);
            I(p + "phg.directivity", phg.DirectivityCode);
            I(p + "phg.beacons_per_hour", phg.BeaconsPerHour);
        }

        if (d.DfSignalStrength is { } dfs)
        {
            I(p + "dfs.strength", dfs.StrengthCode);
            I(p + "dfs.height", dfs.HeightCode);
            I(p + "dfs.gain", dfs.GainCode);
            I(p + "dfs.directivity", dfs.DirectivityCode);
        }

        if (d.AreaObject is { } area)
        {
            S(p + "area.shape", Kebab(area.Shape.ToString()));
            S(p + "area.color", Kebab(area.Color.ToString()));
            I(p + "area.lat_offset", area.LatitudeOffsetCode);
            I(p + "area.lon_offset", area.LongitudeOffsetCode);
            I(p + "area.corridor_width_miles", area.CorridorWidthMiles);
        }

        if (d.DfBearing is { } bearing)
        {
            I(p + "df_bearing.bearing_degrees", bearing.BearingDegrees);
            I(p + "df_bearing.number", bearing.Number);
            I(p + "df_bearing.range", bearing.Range);
            I(p + "df_bearing.quality", bearing.Quality);
        }

        if (d.Storm is { } st)
        {
            S(p + "storm.type", Kebab(st.Type.ToString()));
            I(p + "storm.sustained_wind_knots", st.SustainedWindKnots);
            I(p + "storm.gust_knots", st.GustKnots);
            I(p + "storm.central_pressure_mbar", st.CentralPressureMillibars);
            I(p + "storm.hurricane_radius_nm", st.HurricaneWindRadiusNauticalMiles);
            I(p + "storm.tropical_storm_radius_nm", st.TropicalStormWindRadiusNauticalMiles);
            I(p + "storm.whole_gale_radius_nm", st.WholeGaleRadiusNauticalMiles);
        }

        if (d.Dao is { } dao)
        {
            S(p + "dao.datum", dao.Datum.ToString());
            S(p + "dao.precision", Kebab(dao.Precision.ToString()));
        }

        if (d.Telemetry is { } t)
        {
            I(p + "telemetry.sequence", t.Sequence);
            for (int i = 0; i < t.Analog.Count; i++)
            {
                I($"{p}telemetry.analog[{i}]", t.Analog[i]);
            }

            I(p + "telemetry.digital", t.Digital is { } dg ? (int)dg : null);
        }

        if (d.Frequency is { } f)
        {
            Q(p + "frequency.mhz", f.FrequencyMHz, _ => (double)f.RoundedMHz());
            S(p + "frequency.tone", f.ToneType is { } tone ? Kebab(tone.ToString()) : null);
            I(p + "frequency.tone_value", f.ToneValue);
            I(p + "frequency.offset_khz", f.OffsetKHz);
            I(p + "frequency.range", f.Range);
            F(p + "frequency.range_km", f.RangeInKilometres);
            F(p + "frequency.narrow", f.Narrow);
            F(p + "frequency.ten_khz_resolution", f.TenKilohertzResolution);
        }

        if (d.Weather is { } w)
        {
            Weather(p + "weather.", w, windInCs: compressed && CsKind(d) == Cs.Wind);
        }

        S(p + "signpost", d.SignpostText);
        S(p + "comment", d.Comment);
    }

    private void Compression(string p, AprsCompressionType ct)
    {
        S(p + "compression.fix", Kebab(ct.Fix.ToString()));
        S(p + "compression.source", Kebab(ct.Source.ToString()));
        S(p + "compression.origin", Kebab(ct.Origin.ToString()));
    }

    /// <summary>What the cs bytes of a compressed position carry.</summary>
    internal enum Cs
    {
        None,
        Wind,
        Altitude,
        CourseSpeed,
        Range,
    }

    /// <summary>The cs bytes' contents, by the order <see cref="PositionCodec"/> writes them in.</summary>
    internal static Cs CsKind(AprsPositionedData d)
    {
        bool windInCs = d.Weather is { } weather && (weather.WindDirectionDegrees is not null || weather.WindSpeedMph is not null);
        if (windInCs)
        {
            return Cs.Wind;
        }

        if (d.CompressionType?.Source == AprsCompressionSource.Gga && d.AltitudeFeet is not null)
        {
            return Cs.Altitude;
        }

        if (d.CourseDegrees is not null || d.SpeedKnots is not null)
        {
            return Cs.CourseSpeed;
        }

        return d.RangeMiles is not null ? Cs.Range : Cs.None;
    }

    /// <summary>The latitude and longitude the form written holds for the position given.</summary>
    private static (double Latitude, double Longitude) WrittenPosition(AprsPositionedData d, bool compressed)
    {
        if (compressed)
        {
            AprsPosition reported = PositionCodec.CompressedReported(d.Position);
            return (reported.Latitude, reported.Longitude);
        }

        return (Axis(d.Position.Latitude, d.Dao, d.Position.Ambiguity), Axis(d.Position.Longitude, d.Dao, d.Position.Ambiguity));
    }

    /// <summary>
    /// One coordinate as an uncompressed position holds it: in hundredths of a minute, or finer
    /// with a <c>!DAO!</c>; an ambiguous one is cut to the box that holds it and read back as the
    /// box's centre.
    /// </summary>
    private static double Axis(double degrees, AprsDao? dao, int ambiguity)
    {
        double magnitude = Math.Abs(degrees);
        double value;
        if (ambiguity > 0)
        {
            long hundredths = Steps.Cut(magnitude, 6000);
            long box = ambiguity switch { 1 => 10, 2 => 100, 3 => 1000, _ => 6000 };
            value = ((hundredths / 6000) + ((((hundredths % 6000) / box * box) + (box / 2.0)) / 6000.0));
        }
        else
        {
            value = dao?.Precision switch
            {
                AprsDaoPrecision.Thousandths => Steps.Count(magnitude, 60 * 1000) / (60.0 * 1000),
                AprsDaoPrecision.Base91 => Steps.Count(magnitude, 60 * 100 * 91) / (60.0 * 100 * 91),
                _ => Steps.Count(magnitude, 60 * 100) / (60.0 * 100),
            };
        }

        return double.IsNegative(degrees) ? -value : value;
    }

    private void PositionedMotion(string p, AprsPositionedData d, bool micE, bool compressed)
    {
        Cs cs = compressed ? CsKind(d) : Cs.None;
        if (compressed && cs == Cs.CourseSpeed)
        {
            // The cs bytes hold a course in 4-degree steps (north is 360 as course) and a speed in 1.08^s - 1 knots.
            Q(p + "course_degrees", d.CourseDegrees, c => (Steps.Count(c / 4.0) % 90) is var s && s == 0 ? 360 : s * 4);
            Q(p + "speed_knots", d.SpeedKnots, v => Math.Pow(1.08, PositionCodec.SpeedCode(v)) - 1);
        }
        else
        {
            Q(p + "course_degrees", d.CourseDegrees, Steps.Whole);
            Q(p + "speed_knots", d.SpeedKnots, Steps.Whole);
        }

        Q(p + "altitude_feet", d.AltitudeFeet, feet => WrittenAltitude(d, micE, feet));
        if (compressed && cs == Cs.Range)
        {
            Q(p + "range_miles", d.RangeMiles, r => 2 * Math.Pow(1.08, PositionCodec.RangeStep(r)));
        }
        else
        {
            Q(p + "range_miles", d.RangeMiles, Steps.Whole);
        }
    }

    private static double WrittenAltitude(AprsPositionedData d, bool micE, double feet)
    {
        if (micE)
        {
            return MicECodec.AltitudeAsFeet((AprsMicEReport)d)
                ? Steps.Whole(feet)
                : Steps.Count(feet / Units.FeetPerMetre) * Units.FeetPerMetre;
        }

        if (d.IsCompressed && d.CompressionType?.Source == AprsCompressionSource.Gga
            && (d.Weather is not null || PositionCodec.AltitudeFitsInCs(d)))
        {
            // Only the cs bytes carry it: the nearest step, and 1 foot or less is 1 foot.
            return Math.Pow(1.002, PositionCodec.GgaStep(feet));
        }

        return Steps.Whole(feet);
    }

    // ------------------------------------------------------------------ weather

    private void Weather(string p, AprsWeather w, bool windInCs)
    {
        if (windInCs)
        {
            // A direction in 4-degree steps (north is 0) and a speed in 1.08^s - 1 knots, as mph.
            Q(p + "wind_direction_degrees", w.WindDirectionDegrees, v => (Steps.Count(v / 4.0) % 90) * 4);
            Q(p + "wind_speed_mph", w.WindSpeedMph, v => Units.KnotsToMph(Math.Pow(1.08, PositionCodec.SpeedCode(Units.MphToKnots(v))) - 1));
        }
        else
        {
            Q(p + "wind_direction_degrees", w.WindDirectionDegrees, Steps.Whole);
            Q(p + "wind_speed_mph", w.WindSpeedMph, Steps.Whole);
        }

        Q(p + "wind_gust_mph", w.WindGustMph, Steps.Whole);
        Q(p + "temperature_f", w.TemperatureFahrenheit, Steps.Whole);
        Q(p + "rain_1h_in", w.RainLastHourInches, v => Steps.Count(v, 100) / 100.0);
        Q(p + "rain_24h_in", w.RainLast24HoursInches, v => Steps.Count(v, 100) / 100.0);
        Q(p + "rain_midnight_in", w.RainSinceMidnightInches, v => Steps.Count(v, 100) / 100.0);
        I(p + "rain_raw", w.RainRawCounter);
        Q(p + "humidity_percent", w.HumidityPercent, Steps.Whole);
        Q(p + "pressure_mbar", w.PressureMillibars, v => Steps.Count(v, 10) / 10.0);
        Q(p + "luminosity_w_m2", w.LuminosityWattsPerSquareMetre, Steps.Whole);
        Q(p + "snow_24h_in", w.SnowfallLast24HoursInches, Snowfall);
        S(p + "software", w.SoftwareType?.ToString());
        S(p + "unit", w.UnitType);
        for (int i = 0; i < w.AdditionalFields.Count; i++)
        {
            S($"{p}extra[{i}].letter", w.AdditionalFields[i].Letter.ToString());
            S($"{p}extra[{i}].value", w.AdditionalFields[i].Value);
        }
    }

    /// <summary>Snowfall in its three characters: hundredths under 1, else tenths under 10, else whole inches.</summary>
    private static double Snowfall(double inches)
    {
        double hundredths = Steps.Nearest(inches * 100);
        if (hundredths < 100)
        {
            return hundredths / 100;
        }

        double tenths = Steps.Nearest(inches * 10);
        return tenths < 100 ? tenths / 10 : Steps.Nearest(inches);
    }

    // ------------------------------------------------------------------ names

    private static string NmeaTime(string field)
    {
        string fraction = field.Length > 7 ? field[7..].TrimEnd('0') : "";
        return $"{field[..2]}:{field[2..4]}:{field[4..6]}{(fraction.Length > 0 ? "." + fraction : "")}";
    }

    private static string TypeName(AprsData data) => data switch
    {
        AprsPositionReport => "position",
        AprsMicEReport => "mic-e",
        AprsObjectReport => "object",
        AprsItemReport => "item",
        AprsTextMessage => "message",
        AprsMessageAck => "ack",
        AprsMessageReject => "reject",
        AprsBulletin => "bulletin",
        AprsNwsBulletin => "nws-bulletin",
        AprsTelemetryParameterNames => "telemetry-names",
        AprsTelemetryUnits => "telemetry-units",
        AprsTelemetryCoefficients => "telemetry-coefficients",
        AprsTelemetryBitSense => "telemetry-bits",
        AprsDirectedQuery => "directed-query",
        AprsStatusReport => "status",
        AprsTelemetryReport => "telemetry",
        AprsWeatherReport => "weather",
        AprsRawWeatherReport => "raw-weather",
        AprsNmeaReport => "nmea",
        AprsMaidenheadBeacon => "maidenhead-beacon",
        AprsGeneralQuery => "query",
        AprsStationCapabilities => "capabilities",
        AprsThirdPartyTraffic => "third-party",
        AprsUserDefinedData => "user-defined",
        AprsTestData => "test",
        AprsAgreloDfReport => "agrelo-df",
        AprsUnrecognizedData => "unrecognized",
        _ => throw new NotSupportedException(data.GetType().Name),
    };

    /// <summary>PascalCase to kebab-case, splitting only before capitals: OffDuty to off-duty, Kpc3 to kpc3.</summary>
    internal static string Kebab(string pascal)
    {
        var sb = new System.Text.StringBuilder(pascal.Length + 4);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsAsciiLetterUpper(c) && i > 0)
            {
                sb.Append('-');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
