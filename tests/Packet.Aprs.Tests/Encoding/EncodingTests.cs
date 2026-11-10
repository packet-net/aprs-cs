namespace Packet.Aprs.Tests.Encoding;

/// <summary>
/// Building whole packets through the C# API. What the encoder writes for given data, and what it
/// refuses, is in the language-neutral vectors (<c>spec/aprs/cases/encode.json</c>).
/// </summary>
public class EncodingTests : AprsSpec
{
    private static readonly AprsPosition Somewhere = new(49 + (3.50 / 60), -(72 + (1.75 / 60)));

    [Fact]
    public void Creating_a_packet_needs_mic_e_to_go_through_create_mic_e()
    {
        var mic = new AprsMicEReport { Position = Somewhere, Symbol = AprsSymbol.Parse("/>") };
        FluentActions.Invoking(() => AprsPacket.Create("N0CALL", "APZ001", mic)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_compression_type_with_no_cs_data_to_carry_it_is_refused()
    {
        // The unknown wind extension leaves the cs bytes blank, and blank cs bytes carry no T byte,
        // so writing them would lose the compression type (vectors interpretations.md, "Re-encoding
        // into compressed bytes rounds": a value the cs bytes cannot hold is refused).
        GivenInformationField("!/4_@>Q?Y`_.&H.../...g...t066");
        WhenDecoded();
        WhenEncodingIsAttempted(Packet.Data);
        ThenEncodingFailed();
    }

    [Fact]
    public void A_compressed_wind_direction_without_a_speed_is_refused()
    {
        // The cs bytes carry direction and speed together and cannot say either is unknown, so
        // writing this wind would turn the unknown speed into 0 (the mirror of the vectors case
        // weather/compressed-wind-speed-without-a-direction).
        GivenInformationField("!/5L!!<*e7_ sT156/...g000t066");
        WhenDecoded();
        WhenEncodingIsAttempted(Packet.Data);
        ThenEncodingFailed();
    }

    [Fact]
    public void A_comment_after_an_nmea_sentence_needs_its_checksum()
    {
        var withChecksum = new AprsNmeaReport { Sentence = "GPGLL,2554.459,N,08020.187,W,154027.281,A*22", Comment = "/Home" };
        var without = withChecksum with { Sentence = "GPGLL,2554.459,N,08020.187,W,154027.281,A" };

        WhenEncodingIsAttempted(without);
        ThenEncodingFailed();

        WhenEncoded(withChecksum);
        ThenEncodedIs("$GPGLL,2554.459,N,08020.187,W,154027.281,A*22/Home");
        ThenEncodedDecodesBackTo(withChecksum with { Position = new AprsPosition(25.90765, -80.33645), FixValid = true, Time = new TimeOnly(15, 40, 27, 281), TimeText = "154027.281" });
    }

    [Fact]
    public void A_text_message_that_would_read_back_as_another_kind_is_refused()
    {
        // Addressed BLN1 it is a bulletin, and ?WX is a directed query (vectors README, Messages and queries).
        WhenEncodingIsAttempted(new AprsTextMessage { Addressee = "BLN1", Text = "Net tonight" });
        ThenEncodingFailed();
        WhenEncodingIsAttempted(new AprsTextMessage { Addressee = "N0CALL", Text = "?WX" });
        ThenEncodingFailed();
    }

    [Fact]
    public void A_weather_station_symbol_without_weather_is_refused()
    {
        // A decoder reads the _ symbol as a weather report, so this would read back as incomplete weather.
        WhenEncodingIsAttempted(new AprsPositionReport { Position = Somewhere, Symbol = AprsSymbol.WeatherStation, Comment = "Hello" });
        ThenEncodingFailed();
    }

    [Fact]
    public void A_course_and_speed_after_the_area_symbol_is_refused()
    {
        // After \l, 088/036 reads as an area object (APRS12c ch. 11).
        WhenEncodingIsAttempted(new AprsPositionReport { Position = Somewhere, Symbol = AprsSymbol.Parse("\\l"), CourseDegrees = 88, SpeedKnots = 36 });
        ThenEncodingFailed();
    }

    [Fact]
    public void An_ambiguous_position_at_the_pole_is_refused()
    {
        // The centre of the blanked box lies past 90 degrees, which a decoder rejects.
        WhenEncodingIsAttempted(new AprsPositionReport { Position = new AprsPosition(90, 0, 2), Symbol = AprsSymbol.Parse("/>") });
        ThenEncodingFailed();
    }

    [Fact]
    public void A_mic_e_course_of_0_is_refused()
    {
        // A decoder reads a Mic-E course of 0 as no course; north is 360.
        var report = new AprsMicEReport { Position = Somewhere, Symbol = AprsSymbol.Parse("/>"), CourseDegrees = 0, SpeedKnots = 10 };
        FluentActions.Invoking(() => AprsPacket.CreateMicE(AprsAddress.Parse("N0CALL"), report)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_mic_e_longitude_of_180_degrees_is_refused()
    {
        // Mic-E writes 100-109 degrees as 180-189 would be written, so 180 itself has no form (APRS12c ch. 10).
        var report = new AprsMicEReport { Position = new AprsPosition(49.05, 180), Symbol = AprsSymbol.Parse("/>") };
        FluentActions.Invoking(() => AprsPacket.CreateMicE(AprsAddress.Parse("N0CALL"), report)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_mic_e_device_suffix_its_type_code_does_not_have_is_refused()
    {
        // |3 is not a suffix the device database lists after the > type code, so it would read
        // back as comment text.
        var report = new AprsMicEReport { Position = Somewhere, Symbol = AprsSymbol.Parse("/>"), TypeCode = '>', DeviceSuffix = "|3", Comment = "Net" };
        FluentActions.Invoking(() => AprsPacket.CreateMicE(AprsAddress.Parse("N0CALL"), report)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Mic_telemetry_with_an_empty_first_channel_is_refused()
    {
        // T#MIC,,119 reads back otherwise, so no form holds it (vectors ruling F12).
        WhenEncodingIsAttempted(new AprsTelemetryReport { Sequence = "MIC", Analog = [null, 119m, 62.66m, 175m, 999m], Digital = 0b1001_0110 });
        ThenEncodingFailed();
    }

    [Fact]
    public void User_defined_data_ending_with_a_line_break_is_refused()
    {
        WhenEncodingIsAttempted(new AprsUserDefinedData { UserId = 'Q', PacketType = 'x', Data = "data\r"u8.ToArray() });
        ThenEncodingFailed();
    }

    [Theory]
    [InlineData("12.5", "s013")]
    [InlineData("0.995", "s001")]
    [InlineData("9.96", "s010")]
    [InlineData("0.125", "s.13")]
    [InlineData("2.96", "s003")]
    [InlineData("0", "s000")]
    public void Snowfall_is_rounded_to_its_three_characters(string inches, string written)
    {
        // Hundredths under 1, tenths under 10, else whole; a whole result in three digits (ruling F3).
        var weather = new AprsWeather { WindDirectionDegrees = 220, WindSpeedMph = 4, WindGustMph = 5, TemperatureFahrenheit = 77, SnowfallLast24HoursInches = decimal.Parse(inches, System.Globalization.CultureInfo.InvariantCulture) };
        WhenEncoded(new AprsPositionReport { Position = Somewhere, Symbol = AprsSymbol.WeatherStation, Weather = weather });
        Show(Encoded).Should().EndWith(written);
    }

    [Fact]
    public void Snowfall_that_rounds_past_999_is_refused()
    {
        var weather = new AprsWeather { WindDirectionDegrees = 220, WindSpeedMph = 4, WindGustMph = 5, TemperatureFahrenheit = 77, SnowfallLast24HoursInches = 999.5m };
        WhenEncodingIsAttempted(new AprsPositionReport { Position = Somewhere, Symbol = AprsSymbol.WeatherStation, Weather = weather });
        ThenEncodingFailed();
    }

    [Fact]
    public void A_footprint_keeps_its_numbers_as_sent_but_compares_by_value()
    {
        GivenInformationField("?APRS? 34.360,-.1715,0200");
        WhenDecoded();
        var query = ThenDataIs<AprsGeneralQuery>();
        query.Footprint.Should().Be(new AprsQueryFootprint(34.36m, -0.1715m, 200));

        WhenEncoded(query);
        ThenEncodedIs("?APRS? 34.360,-.1715,0200");

        WhenEncoded(query with { Footprint = new AprsQueryFootprint(34.36m, -0.1715m, 200) });
        ThenEncodedIs("?APRS? 34.36,-0.1715,0200");
    }

    // ------------------------------------------------------------ values between steps (vectors rulings F2, F3, F11)

    [Theory]
    [InlineData(0.5, "!4903.50N/07201.75W>090/001")]
    [InlineData(29.75, "!4903.50N/07201.75W>090/030")]
    [InlineData(-0.3, "!4903.50N/07201.75W>090/000")]
    public void A_speed_between_knots_is_rounded_halves_away_from_zero(double knots, string written)
    {
        WhenEncoded(new AprsPositionReport { Position = Somewhere, Symbol = AprsSymbol.Car, CourseDegrees = 90, SpeedKnots = knots });
        ThenEncodedIs(written);
    }

    [Fact]
    public void A_compressed_speed_whose_nearest_step_is_0_is_written_and_one_below_is_refused()
    {
        // -0.03 knots is nearest s = 0; -0.3 is nearest s = -5, outside the byte (ruling G3).
        var report = new AprsPositionReport { Position = new AprsPosition(49.5, -72.75), Symbol = AprsSymbol.Car, IsCompressed = true, CompressionType = AprsCompressionType.Default, CourseDegrees = 88, SpeedKnots = -0.03 };
        WhenEncoded(report);
        Show(Encoded).Should().StartWith("!/5L!!<*e8>7!");
        WhenEncodingIsAttempted(report with { SpeedKnots = -0.3 });
        ThenEncodingFailed();
    }

    [Fact]
    public void Compressed_cs_values_without_a_compression_type_are_refused()
    {
        // The T byte that goes with them reads back as a type the data does not have.
        WhenEncodingIsAttempted(new AprsPositionReport { Position = new AprsPosition(49.5, -72.75), Symbol = AprsSymbol.Car, IsCompressed = true, CourseDegrees = 88, SpeedKnots = 36 });
        ThenEncodingFailed();
    }

    [Theory]
    [InlineData(10.5, "/A=000011")]
    [InlineData(-0.5, "/A=-00001")]
    [InlineData(-0.4, "/A=000000")]
    public void An_altitude_is_rounded_to_whole_feet(double feet, string written)
    {
        WhenEncoded(new AprsPositionReport { Position = Somewhere, Symbol = AprsSymbol.Car, AltitudeFeet = feet });
        Show(Encoded).Should().EndWith(written);
    }

    [Fact]
    public void A_mic_e_range_and_speed_are_rounded()
    {
        var report = new AprsMicEReport { Position = new AprsPosition(33.427333, -112.129), Symbol = new AprsSymbol('/', 'j'), SpeedKnots = 0.5, RadioRangeMiles = 4362.7, TypeCode = '`' };
        string info = Show(AprsPacket.CreateMicE(AprsAddress.Parse("N0CALL"), report).Information.ToArray());
        info.Should().EndWith("RNG4363");
        ((AprsMicEReport)AprsPacket.Decode(AprsAddress.Parse("N0CALL"), global::Packet.Aprs.Internal.MicECodec.EncodeDestination(report), [], AprsPacket.CreateMicE(AprsAddress.Parse("N0CALL"), report).Information.ToArray()).Data)
            .SpeedKnots.Should().Be(1);
    }

    [Fact]
    public void Numbers_given_as_data_are_plain_decimals()
    {
        WhenEncoded(new AprsTelemetryCoefficients { Addressee = "N0CALL", Coefficients = [0.53, 190.0, 1e61, 1e-7, -94.0] });
        ThenEncodedIs(":N0CALL   :EQNS.0.53,190,1" + new string('0', 61) + ",0.0000001,-94");
        WhenEncoded(new AprsGeneralQuery { QueryType = "APRS", Footprint = new AprsQueryFootprint(0m, 34.0m, 200) });
        ThenEncodedIs("?APRS? 0, 34,0200");
    }

    [Fact]
    public void A_dao_on_an_ambiguous_position_is_refused_whatever_its_precision()
    {
        WhenEncodingIsAttempted(new AprsPositionReport { Position = Somewhere with { Ambiguity = 1 }, Symbol = AprsSymbol.Car, Dao = new AprsDao('W', AprsDaoPrecision.None) });
        ThenEncodingFailed();
    }

    // ------------------------------------------------------------ exact bytes (vectors rulings E1-E12)

    [Theory]
    [InlineData("T#005,2,073,.53,190.0,5,00000000")]
    [InlineData(":N0CALL   :EQNS.0,.53,0,0,0510,0,0,1,0,0,1,0,0,1,0")]
    [InlineData("?APRS? 34.0,-117.15,0200")]
    [InlineData("?APRS? 10.02,117.15,0200")]
    public void Free_form_numbers_are_written_back_as_sent(string info)
    {
        // E1: telemetry values, EQNS coefficients and footprint coordinates keep their text.
        GivenInformationField(info);
        WhenDecoded();
        ThenReEncodesExactly();
    }

    [Fact]
    public void A_dao_on_a_compressed_position_carries_the_digits_of_the_position()
    {
        // E2: the digits a decoder ignores agree with the position reported.
        GivenInformationField("!L5<&{T\"Pt#  GLoRa APRS Digi Godowa|(X#d*_!s%R!");
        WhenDecoded();
        WhenEncoded(Packet.Data);
        ThenEncodedIs("!L5<&{T\"Pt# sTLoRa APRS Digi Godowa|(X#d*_!sCp!");
    }

    [Fact]
    public void The_frequency_comes_first_then_braces_then_altitude_then_text()
    {
        // E3: the frequency is in the first 10 bytes of the comment (APRS12c ch. 18).
        var report = new AprsPositionReport
        {
            Position = Somewhere, Symbol = AprsSymbol.Parse("/j"), CourseDegrees = 6, SpeedKnots = 58,
            Frequency = new AprsVoiceFrequency { FrequencyMHz = 146.520m }, AltitudeFeet = 889, Comment = "Dayton Bound",
        };
        WhenEncoded(report);
        ThenEncodedIs("!4903.50N/07201.75Wj006/058/146.520MHz/A=000889 Dayton Bound");
        ThenEncodedDecodesBackTo(report);

        var signpost = new AprsPositionReport { Position = Somewhere, Symbol = AprsSymbol.Parse("\\m"), SignpostText = "55", AltitudeFeet = 100, Comment = "Slow" };
        WhenEncoded(signpost);
        ThenEncodedIs("!4903.50N\\07201.75Wm{55}/A=000100Slow");
        ThenEncodedDecodesBackTo(signpost);
    }

    [Theory]
    [InlineData("!4903.50N/07201.75W#PHG7260/146.520MHz Hello")]
    [InlineData("!4903.50N/07201.75W#PHG72604/146.520MHz Hello")]
    [InlineData("!4903.50N/07201.75W>088/036/146.520MHz T100 Hello")]
    public void A_frequency_after_a_data_extension_follows_one_slash(string info)
    {
        // E4: a '/' after a seven-byte extension; a PHGR already ends in one.
        GivenInformationField(info);
        WhenDecoded();
        ThenReEncodesExactly();
    }

    [Fact]
    public void A_mic_e_frequency_after_a_phg_follows_a_slash()
    {
        // E4, in Mic-E status text.
        var report = new AprsMicEReport
        {
            Position = Somewhere, Symbol = AprsSymbol.Parse("/>"), TypeCode = '`', Phg = new AprsPhg(7, 5, 3, 0),
            Frequency = new AprsVoiceFrequency { FrequencyMHz = 145.425m }, Comment = "Club",
        };
        AprsPacket created = AprsPacket.CreateMicE(AprsAddress.Parse("N0CALL"), report);
        Show(created.Information.Span).Should().EndWith("`PHG7530/145.425MHz Club");
        var back = (AprsMicEReport)AprsPacket.Decode(created.Source, created.Destination, [], created.Information.Span).Data;
        (back.Phg, back.Frequency, back.Comment).Should().Be((report.Phg, report.Frequency, report.Comment));
    }

    [Fact]
    public void A_mic_e_speed_of_190_to_199_knots_is_written_with_a_slash()
    {
        // E5: SP + 80 would be DEL; the table's other character for that band is '/'.
        var report = new AprsMicEReport { Position = Somewhere, Symbol = AprsSymbol.Parse("/>"), CourseDegrees = 90, SpeedKnots = 195 };
        AprsPacket created = AprsPacket.CreateMicE(AprsAddress.Parse("N0CALL"), report);
        created.Information.Span[4].Should().Be((byte)'/');
        AprsPacket.Decode(created.Source, created.Destination, [], created.Information.Span).Data.Should().Be(report);
    }

    [Theory]
    [InlineData("N1JCM-9>TRQP7T:`c'wl|+>/<0x1d>ABCD/<0x1d>ABCDEhello")]
    [InlineData("N0CALL>S32UVT:`(_fn\"Oj/]wO91SX/G Hello")]
    [InlineData("KB3AWQ-9>T1QW8Q:`i[Yl*<0x1c>P\\`\"6,}444.900MHz <0x00>173 +500 4.7V S06")]
    public void A_mic_e_comment_gets_a_delimiter_only_where_it_needs_one(string tnc2)
    {
        // E6: after Rev 0 telemetry a 0x1D is text. E7: no '/' after the type code where the
        // comment reads back as itself, and a space after a frequency before a 0x00.
        GivenPacket(tnc2);
        WhenDecoded();
        AprsPacket created = AprsPacket.CreateMicE(Packet.Source, (AprsMicEReport)Packet.Data);
        Show(created.Information.Span).Should().Be(Show(Packet.Information.Span));
        created.Destination.Should().Be(Packet.Destination);
    }

    [Fact]
    public void Wind_text_after_a_compressed_position_needs_no_delimiter()
    {
        // E7: without the weather symbol, .../... after a compressed position is comment text.
        GivenInformationField("!L4a^FO<i>b  G.../...g...t055h.._10194LoRa_APRS I-Gate");
        WhenDecoded();
        WhenEncoded(Packet.Data);
        ThenEncodedIs("!L4a^FO<i>b sT.../...g...t055h.._10194LoRa_APRS I-Gate");
    }

    [Theory]
    [InlineData("0.5", "s.50")]
    [InlineData("0.32", "s.32")]
    [InlineData("2.5", "s2.5")]
    [InlineData("2", "s002")]
    public void Snowfall_is_written_in_its_one_form(string inches, string written)
    {
        // E8: under 1 inch is '.' and two digits.
        var weather = new AprsWeather { WindDirectionDegrees = 220, WindSpeedMph = 4, WindGustMph = 5, TemperatureFahrenheit = 77, SnowfallLast24HoursInches = decimal.Parse(inches, System.Globalization.CultureInfo.InvariantCulture) };
        var report = new AprsPositionReport { Position = Somewhere, Symbol = AprsSymbol.WeatherStation, Weather = weather };
        WhenEncoded(report);
        ThenEncodedIs("!4903.50N/07201.75W_220/004g005t077" + written);
        ThenEncodedDecodesBackTo(report);
    }

    [Theory]
    [InlineData(358, '!')]
    [InlineData(2, '"')]
    [InlineData(6, '#')]
    public void A_compressed_course_rounds_to_the_nearest_step_halves_away_from_zero(int course, char c)
    {
        // E9: rounding is not optional; 358 rounds to 360, written as north (c = 0).
        WhenEncoded(new AprsPositionReport { Position = Somewhere, Symbol = AprsSymbol.Parse("/>"), IsCompressed = true, CompressionType = AprsCompressionType.Default, CourseDegrees = course, SpeedKnots = 10 });
        Encoded[11].Should().Be((byte)c);
    }

    [Fact]
    public void A_compressed_wind_direction_of_358_degrees_is_written_as_north()
    {
        // E9 for wind: c = 0, which a compressed wind reads back as 0.
        var weather = new AprsWeather { WindDirectionDegrees = 358, WindSpeedMph = 10, TemperatureFahrenheit = 50 };
        WhenEncoded(new AprsPositionReport { Position = Somewhere, Symbol = AprsSymbol.WeatherStation, IsCompressed = true, CompressionType = AprsCompressionType.Default, Weather = weather });
        Encoded[11].Should().Be((byte)'!');
    }

    [Fact]
    public void A_compressed_speed_byte_of_90_is_written()
    {
        // E10: APRS12c ch. 9 limits only the course byte to '!'-'z'.
        GivenInformationField("@260520z/=M?$8WCS_[{!");
        WhenDecoded();
        WhenEncoded(Packet.Data);
        ThenEncodedIs("@260520z/=M?$8WCS_[{!g...t...");
    }

    [Fact]
    public void An_information_field_has_no_size_limit()
    {
        // E11: refusing means the rule declines the data, never that a buffer is full.
        string comment = new('x', 1000);
        WhenEncoded(new AprsPositionReport { Position = Somewhere, Symbol = AprsSymbol.Parse("/>"), Comment = comment });
        ThenEncodedIs("!4903.50N/07201.75W>" + comment);
    }

    [Fact]
    public void A_mic_e_comment_that_would_end_an_altitude_is_written_after_a_space()
    {
        // E12: written straight after the /A= altitude, the comment's "4} would read back as the
        // Mic-E altitude 0"4}. A separating space is a clean form, so it is written, not refused.
        GivenPacket("AK6ID-9>SS5UQQ:`.3 lSU>/`\"4}_01/A=000010 70cm%");
        WhenDecoded();
        var report = ThenDataIs<AprsMicEReport>();
        AprsPacket created = AprsPacket.CreateMicE(Packet.Source, report);
        Show(created.Information.Span).Should().Be("`.3 lSU>/`/A=000010 \"4}_01 70cm%");
        AprsPacket again = AprsPacket.Decode(created.Source, created.Destination, [], created.Information.Span);
        again.Diagnostics.Should().BeEmpty();
        again.Data.Should().Be(report);
    }

    [Theory]
    [InlineData(1, 50.6, 55)]
    [InlineData(2, 50.6, 50)]
    [InlineData(2, 49.5, 50)]
    public void An_ambiguous_mic_e_longitude_writes_the_centre_in_the_digits_a_decoder_ignores(int ambiguity, double hundredths, int written)
    {
        // Vectors README, Mic-E: with ambiguity the longitude digits the latitude's level covers
        // hold the centre the decoder reports, not the position's own digits.
        double longitude = -(70 + ((34 + (hundredths / 100)) / 60));
        var report = new AprsMicEReport { Position = new AprsPosition(49.0583, longitude, ambiguity), Symbol = AprsSymbol.Parse("/>"), CourseDegrees = 90, SpeedKnots = 10 };
        AprsPacket created = AprsPacket.CreateMicE(AprsAddress.Parse("N0CALL"), report);
        (created.Information.Span[3] - 28).Should().Be(written);
    }

    [Fact]
    public void A_mic_e_report_with_two_data_extensions_is_refused()
    {
        // A decoder lifts only one of PHG, RNG and DFS from the status text.
        var report = new AprsMicEReport { Position = Somewhere, Symbol = AprsSymbol.Parse("/>"), Phg = new AprsPhg(1, 5, 6, 0), RadioRangeMiles = 1 };
        FluentActions.Invoking(() => AprsPacket.CreateMicE(AprsAddress.Parse("N0CALL"), report)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_nmea_report_whose_values_disagree_with_its_sentence_is_refused()
    {
        // The sentence is what is sent; a position it does not hold would read back differently.
        var report = new AprsNmeaReport { Sentence = "GPGLL,2554.459,N,08020.187,W,154027.281,A", Position = new AprsPosition(25.907775, -80.33645) };
        WhenEncodingIsAttempted(report);
        ThenEncodingFailed();
    }

    [Fact]
    public void A_dao_on_a_compressed_position_agrees_with_the_compressed_position_not_the_raw_one()
    {
        // E2: the digits are those of the position the compressed bytes report.
        var raw = new AprsPosition(29.52529966450177, 29.52529966450177);
        var report = new AprsPositionReport { Position = raw, Symbol = AprsSymbol.Parse("/>"), IsCompressed = true, Dao = new AprsDao('W', AprsDaoPrecision.Base91) };
        WhenEncoded(report);
        GivenBytes(Bytes("N0CALL>APZ001:" + Show(Encoded)));
        WhenDecoded();
        var back = ThenDataIs<AprsPositionReport>();
        WhenEncoded(back);
        ThenEncodedIs(Show(report.ToInformationField()));
    }

    [Fact]
    public void Create_builds_the_whole_packet()
    {
        var status = new AprsStatusReport { Text = "Net Control" };
        AprsPacket packet = AprsPacket.Create("M0LTE-9", "APZ001", status, "WIDE1-1,WIDE2-1");
        packet.ToString().Should().Be("M0LTE-9>APZ001,WIDE1-1,WIDE2-1:>Net Control");
        packet.Diagnostics.Should().BeEmpty();
        AprsPacket.DecodeAx25(packet.ToAx25Frame()).Should().Be(packet);
    }
}
