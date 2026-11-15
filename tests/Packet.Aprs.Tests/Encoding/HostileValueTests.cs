using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using Packet.Aprs.Tests.Vectors;

namespace Packet.Aprs.Tests.Encoding;

/// <summary>
/// Hostile numbers in every numeric field of every report type, at the top and inside third-party
/// packets: a non-finite number is <c>not-a-number</c> wherever it is, never read as absent, and no
/// value of any kind makes a writer throw anything but <see cref="AprsEncodeException"/> (the
/// review's crash in the read-back of a short NMEA time was this class of fault). The fields are
/// found by reflection, so a field added later is covered.
/// </summary>
public class HostileValueTests
{
    private static readonly AprsAddress Me = AprsAddress.Parse("N0CALL");

    /// <summary>Records every numeric field between them: each is given, and the report writes.</summary>
    private static readonly string[] Samples =
    [
        """{"type":"position","latitude":51.5,"longitude":-0.5,"symbol":"/>","course_degrees":88,"speed_knots":36,"altitude_feet":123,"frequency":{"mhz":145.5,"tone":"tone","tone_value":118,"offset_khz":-600},"dao":{"datum":"W","precision":"base91"},"telemetry":{"sequence":5,"analog":[1,2,3,4,5],"digital":4},"comment":"hello"}""",
        """{"type":"position","latitude":51.5,"longitude":-0.5,"ambiguity":2,"symbol":"/>","phg":{"power":4,"height":1,"gain":3,"directivity":2,"beacons_per_hour":3},"comment":"x"}""",
        """{"type":"position","latitude":51.5,"longitude":-0.5,"symbol":"/>","range_miles":50}""",
        """{"type":"position","latitude":51.5,"longitude":-0.5,"symbol":"/\\","dfs":{"strength":1,"height":3,"gain":6,"directivity":0}}""",
        """{"type":"position","latitude":51.5,"longitude":-0.5,"symbol":"\\l","area":{"shape":"line-down-right","color":"black","lat_offset":3,"lon_offset":4,"corridor_width_miles":5}}""",
        """{"type":"position","latitude":51.5,"longitude":-0.5,"symbol":"/\\","df_bearing":{"bearing_degrees":90,"number":1,"range":2,"quality":3}}""",
        """{"type":"position","latitude":51.5,"longitude":-0.5,"symbol":"/@","storm":{"type":"hurricane","sustained_wind_knots":150,"gust_knots":200,"central_pressure_mbar":980,"hurricane_radius_nm":90,"tropical_storm_radius_nm":30,"whole_gale_radius_nm":10}}""",
        """{"type":"position","latitude":51.5,"longitude":-0.5,"symbol":"/>","compressed":true,"compression":{"fix":"current","source":"gga","origin":"software"},"altitude_feet":1000}""",
        """{"type":"position","latitude":51.5,"longitude":-0.5,"symbol":"/>","compressed":true,"course_degrees":88,"speed_knots":36}""",
        """{"type":"position","latitude":51.5,"longitude":-0.5,"symbol":"/>","compressed":true,"range_miles":50}""",
        """{"type":"position","latitude":51.5,"longitude":-0.5,"symbol":"/_","compressed":true,"weather":{"wind_direction_degrees":200,"wind_speed_mph":5}}""",
        """{"type":"position","latitude":51.5,"longitude":-0.5,"symbol":"/_","weather":{"wind_direction_degrees":200,"wind_speed_mph":5,"wind_gust_mph":9,"temperature_f":60,"rain_1h_in":0.1,"rain_24h_in":0.2,"rain_midnight_in":0.3,"humidity_percent":50,"pressure_mbar":1013.2,"luminosity_w_m2":300,"snow_24h_in":0.5,"software":"x","unit":"Dvs"}}""",
        """{"type":"weather","timestamp":"10092345","weather":{"wind_direction_degrees":200,"wind_speed_mph":5,"wind_gust_mph":9,"temperature_f":60,"rain_raw":12,"humidity_percent":50,"pressure_mbar":1013.2}}""",
        """{"type":"mic-e","mic_e_message":"off-duty","latitude":51.5,"longitude":-0.5,"symbol":"/>","course_degrees":88,"speed_knots":36,"altitude_feet":1250,"legacy_telemetry":[1,2,3,4,5],"destination_ssid":1}""",
        """{"type":"mic-e","mic_e_message":"custom3","latitude":-33.5,"longitude":151.2,"ambiguity":1,"symbol":"/>","range_miles":20,"comment":"x"}""",
        """{"type":"object","name":"OBJ","timestamp":"092345z","latitude":51.5,"longitude":-0.5,"symbol":"/r","altitude_feet":123,"comment":"rpt"}""",
        """{"type":"item","name":"ITEM","latitude":51.5,"longitude":-0.5,"symbol":"/r","telemetry":{"sequence":5,"analog":[1,2]}}""",
        """{"type":"telemetry","sequence":"001","analog":[1.5,null,3,4,5],"bits":"00000000","comment":"test"}""",
        """{"type":"telemetry-coefficients","addressee":"N0CALL","coefficients":[0,1.5,0]}""",
        """{"type":"nmea","sentence":"GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,","latitude":48.1173,"longitude":11.516666666666667,"fix":"valid","altitude_m":545.4,"time":"12:35:19"}""",
        """{"type":"nmea","sentence":"GPRMC,225446,A,4916.45,N,12311.12,W,000.5,054.7,191194,020.3,E*68","latitude":49.27416666666666,"longitude":-123.18533333333333,"course_degrees":54.7,"speed_knots":0.5}""",
        """{"type":"query","query_type":"APRS","footprint":{"latitude":34.0,"longitude":-117.15,"radius_miles":200}}""",
        """{"type":"agrelo-df","bearing_degrees":90,"quality":5}""",
    ];

    public static TheoryData<int> SampleIndexes
    {
        get
        {
            var all = new TheoryData<int>();
            for (int i = 0; i < Samples.Length; i++)
            {
                all.Add(i);
            }

            return all;
        }
    }

    private static AprsData Sample(int index) => NeutralReader.Data(JsonNode.Parse(Samples[index])!.AsObject());

    // ------------------------------------------------------------------ finding and changing a field

    private static readonly double[] NotNumbers = [double.NaN, double.PositiveInfinity, double.NegativeInfinity];
    private static readonly double[] Hostile = [0, -0.0, -1, 0.5, 1e-320, double.Epsilon, 1e15, 4294967296, 1e308, -1e308, double.MaxValue, double.MinValue];
    private static readonly int[] HostileInts = [0, 1, 255, 256, -1, 100000, int.MaxValue, int.MinValue];

    private static bool IsRoot(Type t) => t.Namespace == "Packet.Aprs" && !t.IsEnum;

    /// <summary>The paths of every number in <paramref name="node"/>: a property name, then a child's, with <c>[i]</c> for a list item.</summary>
    private static IEnumerable<(string[] Path, Type Type)> Numbers(object? node, bool includeAbsent = true)
    {
        if (node is null)
        {
            yield break;
        }

        foreach (PropertyInfo p in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || p.GetMethod is null || p.SetMethod is null)
            {
                continue;
            }

            Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            object? value = p.GetValue(node);
            if (t == typeof(double) || t == typeof(int) || t == typeof(byte))
            {
                if (includeAbsent || value is not null)
                {
                    yield return ([p.Name], t);
                }
            }
            else if (value is IReadOnlyList<double?> or IReadOnlyList<double> or IReadOnlyList<int>)
            {
                int i = 0;
                foreach (object? item in (IEnumerable)value)
                {
                    _ = item;
                    yield return ([$"{p.Name}[{i++}]"], item is int ? typeof(int) : typeof(double));
                }
            }
            else if (value is not null && IsRoot(value.GetType()) && value is not AprsPacket)
            {
                foreach ((string[] path, Type type) in Numbers(value, includeAbsent))
                {
                    yield return ([p.Name, .. path], type);
                }
            }
        }
    }

    private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>A copy of <paramref name="node"/> with the number at <paramref name="path"/> set to <paramref name="value"/>.</summary>
    private static object With(object node, ReadOnlySpan<string> path, double value)
    {
        object copy = Clone.Invoke(node, null)!;
        string name = path[0];
        int bracket = name.IndexOf('[', StringComparison.Ordinal);
        PropertyInfo p = copy.GetType().GetProperty(bracket < 0 ? name : name[..bracket])!;
        if (bracket >= 0)
        {
            int index = int.Parse(name[(bracket + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
            Type item = p.PropertyType.GetGenericArguments()[0];
            var list = ((IEnumerable)p.GetValue(node)!).Cast<object?>().ToList();
            list[index] = Convert(value, item);
            Array array = Array.CreateInstance(item, list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                array.SetValue(list[i], i);
            }

            p.SetValue(copy, array);
        }
        else if (path.Length == 1)
        {
            p.SetValue(copy, Convert(value, p.PropertyType));
        }
        else
        {
            p.SetValue(copy, With(p.GetValue(node)!, path[1..], value));
        }

        return copy;
    }

    private static object Convert(double value, Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        double clamped = t == typeof(int) ? Math.Clamp(value, int.MinValue, int.MaxValue) : t == typeof(byte) ? Math.Clamp(value, byte.MinValue, byte.MaxValue) : value;
        return System.Convert.ChangeType(clamped, t, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static AprsData Wrap(AprsData data, int depth)
    {
        for (int i = 0; i < depth; i++)
        {
            data = AprsThirdPartyTraffic.Create(AprsAddress.Parse("G4ABC"), data, null, [new AprsPathEntry(AprsAddress.Parse("TCPIP"), true)]);
        }

        return data;
    }

    private static string Describe(string[] path) => string.Join('.', path);

    // ------------------------------------------------------------------ the tests

    [Fact]
    public void The_samples_write()
    {
        for (int i = 0; i < Samples.Length; i++)
        {
            AprsData data = Sample(i);
            FluentActions.Invoking(() => AprsPacket.Create(Me, data, data is AprsMicEReport ? null : AprsAddress.Parse("APZ001"))).Should().NotThrow($"sample {i} is {Samples[i]}");
            FluentActions.Invoking(() => Wrap(Wrap(data, 1), 1)).Should().NotThrow($"sample {i} inside third-party packets");
        }
    }

    [Fact]
    public void The_samples_between_them_hold_a_number_in_every_numeric_field_of_every_report_type()
    {
        // A field no sample gives is a field no hostile value reaches.
        HashSet<string> covered = [.. Enumerable.Range(0, Samples.Length).SelectMany(i => Numbers(Sample(i)).Select(n => Describe(n.Path)))];
        string[] expected =
        [
            "Position.Latitude", "Position.Longitude", "Position.Ambiguity", "CourseDegrees", "SpeedKnots", "AltitudeFeet", "RangeMiles",
            "Phg.PowerCode", "Phg.HeightCode", "Phg.GainCode", "Phg.DirectivityCode", "Phg.BeaconsPerHour",
            "DfSignalStrength.StrengthCode", "AreaObject.LatitudeOffsetCode", "AreaObject.CorridorWidthMiles",
            "DfBearing.BearingDegrees", "DfBearing.Quality", "Storm.SustainedWindKnots", "Storm.WholeGaleRadiusNauticalMiles",
            "Telemetry.Sequence", "Telemetry.Analog[2]", "Telemetry.Digital", "Frequency.FrequencyMHz", "Frequency.ToneValue", "Frequency.OffsetKHz",
            "Weather.WindDirectionDegrees", "Weather.RainRawCounter", "Weather.PressureMillibars", "Weather.SnowfallLast24HoursInches", "LegacyTelemetry[4]", "DestinationSsid",
            "Analog[0]", "Coefficients[2]", "AltitudeMetres", "Footprint.RadiusMiles", "Footprint.Latitude", "BearingDegrees", "Quality",
        ];
        expected.Except(covered).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(SampleIndexes))]
    public void A_number_that_is_not_a_number_is_not_a_number_wherever_it_is(int index)
    {
        AprsData sample = Sample(index);
        var paths = Numbers(sample, includeAbsent: false).Where(n => n.Type == typeof(double)).ToList();
        foreach ((string[] path, _) in paths)
        {
            foreach (double bad in NotNumbers)
            {
                var mutated = (AprsData)With(sample, path, bad);
                foreach (int depth in new[] { 0, 1, 3 })
                {
                    string where = $"{Describe(path)} = {bad} at depth {depth} in {Samples[index]}";
                    AprsEncodeException ex = FluentActions.Invoking(() => AprsPacket.Create(Me, Wrap(mutated, depth), mutated is AprsMicEReport && depth == 0 ? null : AprsAddress.Parse("APZ001")))
                        .Should().Throw<AprsEncodeException>(where).Which;
                    ex.Code.Should().Be(AprsRefusalCode.NotANumber, where);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SampleIndexes))]
    public void No_value_in_any_field_makes_a_writer_throw_anything_but_a_refusal(int index)
    {
        AprsData sample = Sample(index);
        foreach ((string[] path, Type type) in Numbers(sample))
        {
            foreach (double value in type != typeof(double) ? HostileInts.Select(i => (double)i) : Hostile)
            {
                var mutated = (AprsData)With(sample, path, value);
                foreach (int depth in new[] { 0, 2 })
                {
                    string where = $"{Describe(path)} = {value:R} at depth {depth} in {Samples[index]}";
                    try
                    {
                        AprsData data = Wrap(mutated, depth);
                        AprsPacket packet = AprsPacket.Create(Me, data, data is AprsMicEReport ? null : AprsAddress.Parse("APZ001"));
                        packet.ToTnc2();
                    }
                    catch (AprsEncodeException)
                    {
                        // A refusal is an answer.
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"{where}: {ex}", ex);
                    }
                }
            }
        }
    }
}
