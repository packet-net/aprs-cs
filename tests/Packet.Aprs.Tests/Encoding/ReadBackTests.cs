using Packet.Aprs.Internal;

namespace Packet.Aprs.Tests.Encoding;

/// <summary>
/// The final read-back (vectors README, Rules, "Read-back"): every public call that writes bytes
/// from data decodes what it wrote and compares it with the data given, whatever the report type,
/// and never returns bytes that failed. Each test here fails if the read-back is removed or skipped
/// for the type it names: the data is crafted so that the bytes a writer makes naively read back as
/// something else, or a writer is made to write a wrong byte.
/// </summary>
public class ReadBackTests
{
    private static readonly AprsAddress Me = AprsAddress.Parse("N0CALL");
    private static readonly AprsPosition Here = new(51.5, -0.5);

    private static AprsTimestamp Dhm => AprsTimestamp.DayHoursMinutes(1, 2, 3);

    private static byte[] Info(AprsData data) => AprsPacket.Create(Me, data).Information.ToArray();

    private static string Written(AprsData data) => System.Text.Encoding.Latin1.GetString(Info(data));

    private static AprsRefusalCode Refused(AprsData data) =>
        FluentActions.Invoking(() => AprsPacket.Create(Me, data)).Should().Throw<AprsEncodeException>().Which.Code;

    // ------------------------------------------------------------------ crafted data, by report type

    [Fact]
    public void A_position_comment_that_reads_as_a_gale_radius_after_storm_data_takes_a_slash()
    {
        var storm = new AprsStorm { Type = AprsStormType.Hurricane, SustainedWindKnots = 150, GustKnots = 200, CentralPressureMillibars = 980, HurricaneWindRadiusNauticalMiles = 90, TropicalStormWindRadiusNauticalMiles = 30 };
        Written(new AprsPositionReport { Position = Here, Symbol = new AprsSymbol('/', '@'), Storm = storm, Comment = "%123/4" })
            .Should().Be("!5130.00N/00030.00W@.../.../HC/150^200/0980>090&030/%123/4");
    }

    [Fact]
    public void An_object_comment_that_reads_as_a_dao_is_refused()
    {
        Refused(new AprsObjectReport { Name = "OBJ", Timestamp = Dhm, Position = Here, Symbol = new AprsSymbol('/', 'r'), Comment = "x !W12!" })
            .Should().Be(AprsRefusalCode.TextReadsAsField);
    }

    [Fact]
    public void An_item_comment_that_reads_as_an_altitude_is_refused()
    {
        Refused(new AprsItemReport { Name = "ITEM", Position = Here, Symbol = new AprsSymbol('/', 'r'), Comment = "hi /A=000123" })
            .Should().Be(AprsRefusalCode.TextReadsAsField);
    }

    [Fact]
    public void A_mic_e_comment_that_would_read_as_a_phg_after_the_altitude_is_written_after_a_slash()
    {
        AprsPacket packet = AprsPacket.Decode("N0CALL>S32UVT:`(_fn\"Oj3/\"4e}PHG1|146.520MHz!!|");
        AprsPacket again = AprsPacket.Create(packet.Source, packet.Data);
        System.Text.Encoding.Latin1.GetString(again.Information.ToArray()).Should().Be("`(_fn\"Oj3\"4e}/PHG1|146.520MHz!!|");
    }

    [Fact]
    public void A_mic_e_comment_that_reads_as_a_frequency_is_refused()
    {
        Refused(new AprsMicEReport { Position = Here, Symbol = new AprsSymbol('/', '>'), CourseDegrees = 10, SpeedKnots = 5, AltitudeFeet = 1000, Comment = "PHG1|146.520MHz!!|" })
            .Should().Be(AprsRefusalCode.TextReadsAsField);
    }

    [Fact]
    public void A_message_text_that_reads_as_an_ack_or_a_query_is_refused()
    {
        Refused(new AprsTextMessage { Addressee = "G3NRW", Text = "ack12" }).Should().Be(AprsRefusalCode.TextReadsAsField);
        Refused(new AprsTextMessage { Addressee = "G3NRW", Text = "?APRSP" }).Should().Be(AprsRefusalCode.TextReadsAsField);
    }

    [Fact]
    public void A_bulletin_text_that_reads_as_an_ack_is_refused()
    {
        Refused(AprsBulletin.Create('1', "ack12")).Should().Be(AprsRefusalCode.TextReadsAsField);
    }

    [Fact]
    public void A_text_message_to_a_bulletin_addressee_is_an_invalid_addressee()
    {
        Refused(new AprsTextMessage { Addressee = "BLN1", Text = "hi" }).Should().Be(AprsRefusalCode.InvalidAddressee);
    }

    [Fact]
    public void A_weather_extra_field_that_reads_as_humidity_is_refused_in_a_positionless_report()
    {
        var weather = new AprsWeather { PressureMillibars = 1013.2, AdditionalFields = [new AprsWeatherField('h', "50")] };
        Refused(new AprsWeatherReport { Timestamp = AprsTimestamp.MonthDayHoursMinutes(10, 9, 23, 45), Weather = weather })
            .Should().Be(AprsRefusalCode.TextReadsAsField);
    }

    [Fact]
    public void A_weather_extra_field_that_reads_as_a_rain_counter_is_refused_in_a_position_report()
    {
        var weather = new AprsWeather { PressureMillibars = 1013.2, AdditionalFields = [new AprsWeatherField('#', "123")] };
        Refused(new AprsPositionReport { Position = Here, Symbol = new AprsSymbol('/', '_'), Weather = weather })
            .Should().Be(AprsRefusalCode.TextReadsAsField);
    }

    [Fact]
    public void An_nmea_field_that_disagrees_with_its_sentence_is_refused()
    {
        Refused(new AprsNmeaReport { Sentence = "GPGLL,4916.45,N,12311.12,W,225444,A", Position = new AprsPosition(10, 10) })
            .Should().Be(AprsRefusalCode.InvalidNmea);
    }

    [Fact]
    public void An_nmea_field_that_agrees_with_its_sentence_is_written_as_the_sentence()
    {
        Written(new AprsNmeaReport { Sentence = "GPGLL,4916.45,N,12311.12,W,225444,A", Position = new AprsPosition(49.274166666666666, -123.18533333333333) })
            .Should().Be("$GPGLL,4916.45,N,12311.12,W,225444,A");
    }

    // ------------------------------------------------------------------ a writer that writes a wrong byte

    /// <summary>One value of every report type that writes bytes from data.</summary>
    public static TheoryData<string> Types
    {
        get
        {
            var types = new TheoryData<string>();
            foreach (string name in Samples.Keys)
            {
                types.Add(name);
            }

            return types;
        }
    }

    private static readonly AprsStorm Storm = new() { Type = AprsStormType.Hurricane, SustainedWindKnots = 150, GustKnots = 200, CentralPressureMillibars = 980, HurricaneWindRadiusNauticalMiles = 90, TropicalStormWindRadiusNauticalMiles = 30 };

    private static readonly Dictionary<string, Func<AprsData>> Samples = new()
    {
        ["position"] = () => new AprsPositionReport { Position = Here, Symbol = AprsSymbol.Car, CourseDegrees = 88, SpeedKnots = 36, Comment = "Mobile" },
        ["position-compressed"] = () => new AprsPositionReport { Position = Here, Symbol = AprsSymbol.Car, IsCompressed = true, CourseDegrees = 88, SpeedKnots = 36, Comment = "Mobile" },
        ["position-storm"] = () => new AprsPositionReport { Position = Here, Symbol = new AprsSymbol('/', '@'), Storm = Storm },
        ["position-weather"] = () => new AprsPositionReport { Position = Here, Symbol = new AprsSymbol('/', '_'), Weather = new AprsWeather { WindDirectionDegrees = 200, WindSpeedMph = 5, TemperatureFahrenheit = 60 } },
        ["mic-e"] = () => new AprsMicEReport { Position = Here, Symbol = AprsSymbol.Car, CourseDegrees = 88, SpeedKnots = 36, Comment = "Mobile" },
        ["object"] = () => new AprsObjectReport { Name = "OBJ", Timestamp = Dhm, Position = Here, Symbol = new AprsSymbol('/', 'r'), Comment = "rpt" },
        ["item"] = () => new AprsItemReport { Name = "ITEM", Position = Here, Symbol = new AprsSymbol('/', 'r'), Comment = "rpt" },
        ["message"] = () => new AprsTextMessage { Addressee = "G3NRW", Text = "hello", MessageId = "12" },
        ["ack"] = () => new AprsMessageAck { Addressee = "G3NRW", AcknowledgedId = "12" },
        ["reject"] = () => new AprsMessageReject { Addressee = "G3NRW", RejectedId = "12" },
        ["bulletin"] = () => AprsBulletin.Create('1', "all stations"),
        ["nws-bulletin"] = () => new AprsNwsBulletin { Addressee = "NWS-ABCDE", Text = "warning" },
        ["telemetry-names"] = () => new AprsTelemetryParameterNames { Addressee = "N0CALL", Names = ["Volts", "Amps"] },
        ["telemetry-units"] = () => new AprsTelemetryUnits { Addressee = "N0CALL", Units = ["V", "A"] },
        ["telemetry-coefficients"] = () => new AprsTelemetryCoefficients { Addressee = "N0CALL", Coefficients = [0, 1.5, 0] },
        ["telemetry-bits"] = () => new AprsTelemetryBitSense { Addressee = "N0CALL", Bits = "11111111", Project = "Test" },
        ["directed-query"] = () => new AprsDirectedQuery { Addressee = "N0CALL", QueryType = "APRSP" },
        ["status"] = () => new AprsStatusReport { Text = "On the air" },
        ["telemetry"] = () => new AprsTelemetryReport { Sequence = "001", Analog = [1, 2, 3, 4, 5], Bits = "00000000", Comment = "test" },
        ["weather"] = () => new AprsWeatherReport { Timestamp = AprsTimestamp.MonthDayHoursMinutes(10, 9, 23, 45), Weather = new AprsWeather { WindDirectionDegrees = 200, WindSpeedMph = 5, TemperatureFahrenheit = 60 } },
        ["raw-weather"] = () => new AprsRawWeatherReport { Format = AprsRawWeatherFormat.PeetBrosHash, Data = "abc" },
        ["nmea"] = () => new AprsNmeaReport { Sentence = "GPGLL,4916.45,N,12311.12,W,225444,A" },
        ["maidenhead"] = () => new AprsMaidenheadBeacon { Locator = "IO91SX", Comment = "hi" },
        ["query"] = () => new AprsGeneralQuery { QueryType = "APRS" },
        ["capabilities"] = () => new AprsStationCapabilities { Capabilities = [new AprsCapability("IGATE", "a=b")] },
        ["third-party"] = () => AprsThirdPartyTraffic.Create(AprsAddress.Parse("G4ABC"), new AprsStatusReport { Text = "hi" }, null, [new AprsPathEntry(AprsAddress.Parse("TCPIP"), true)]),
        ["user-defined"] = () => new AprsUserDefinedData { UserId = 'Q', PacketType = 'x', Data = new byte[] { 1, 2, 3 } },
        ["test"] = () => new AprsTestData { Data = "x" },
        ["agrelo"] = () => new AprsAgreloDfReport { BearingDegrees = 90, Quality = 5 },
    };

    [Fact]
    public void The_samples_cover_every_report_type()
    {
        Type[] covered = [.. Samples.Values.Select(make => make().GetType()).Distinct()];
        Type[] all = [.. typeof(AprsData).Assembly.GetTypes().Where(t => t.IsSealed && !t.IsAbstract && typeof(AprsData).IsAssignableFrom(t) && t != typeof(AprsUnrecognizedData))];
        all.Except(covered).Should().BeEmpty("a report type with no sample is a type no test holds to the read-back");
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Every_type_is_written_when_it_reads_back(string type)
    {
        AprsData data = Samples[type]();
        FluentActions.Invoking(() => AprsPacket.Create(Me, data)).Should().NotThrow();
    }

    /// <summary>A writer that wrote a wrong byte: whatever the type, the public call refuses and returns nothing.</summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void A_wrong_byte_from_any_writer_is_refused_never_returned(string type)
    {
        AprsData data = Samples[type]();
        InfoEncoder.AfterWriting = info => [.. info, (byte)'~'];
        try
        {
            AprsPacket? packet = null;
            AprsEncodeException ex = FluentActions.Invoking(() => packet = AprsPacket.Create(Me, data)).Should().Throw<AprsEncodeException>().Which;
            packet.Should().BeNull();
            ex.Code.Should().NotBe(AprsRefusalCode.InformationFieldTooLong);
            if (data is not AprsMicEReport)
            {
                FluentActions.Invoking(data.ToInformationField).Should().Throw<AprsEncodeException>();
            }
        }
        finally
        {
            InfoEncoder.AfterWriting = null;
        }
    }

    // ------------------------------------------------------------------ the choke point

    /// <summary>
    /// The structure that makes the read-back unconditional: bytes leave an <see cref="InfoWriter"/>
    /// in one place (<c>InfoEncoder</c>, after the read-back), the only callers of
    /// <c>EncodeInformation</c> are the two public entry points that write an information field, and
    /// a packet is made from bytes only by those or by decoding.
    /// </summary>
    [Fact]
    public void Bytes_leave_a_writer_only_through_the_final_read_back()
    {
        string source = TestPaths.InRepo("src", "Packet.Aprs");
        var files = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => Path.GetRelativePath(source, f), File.ReadAllText);

        files.Where(f => f.Value.Contains("TakeBytes(", StringComparison.Ordinal)).Select(f => f.Key)
            .Should().BeEquivalentTo(["Internal/InfoWriter.cs", "Internal/InfoEncoder.cs"], "only the encoder takes the bytes a writer made");
        files.Where(f => f.Value.Contains("EncodeInformation()", StringComparison.Ordinal)).Select(f => f.Key)
            .Should().BeEquivalentTo(["AprsData.cs", "AprsPacket.cs"], "an information field is written by ToInformationField and AprsPacket.Create only");
        files.Where(f => f.Value.Contains("ReadBack.Check(", StringComparison.Ordinal)).Select(f => f.Key)
            .Should().BeEquivalentTo(["Internal/InfoEncoder.cs"], "the one read-back of an information field is called in one place, with no condition on it beyond the data being decoded rather than given");
        files["Internal/InfoEncoder.cs"].Should().NotContain("CommentCheck", "no heuristic decides whether the read-back runs");
    }
}
