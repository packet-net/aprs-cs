namespace Packet.Aprs.Tests.Encoding;

/// <summary>
/// The fluent builder, <c>Aprs.From(...)</c>: each kind of packet it builds, written out as TNC2
/// and read back under strict parsing, plus the defaults and the refusals.
/// </summary>
public class BuilderTests
{
    private static readonly AprsStation Me = Aprs.From("M0LTE");

    [Fact]
    public void A_position_report()
    {
        AprsPacket packet = Aprs.From("M0LTE-9").Via("WIDE1-1", "WIDE2-1")
            .Position(51.4543, -0.9781)
            .Symbol(AprsSymbol.Car)
            .Course(88).Speed(36).Altitude(120)
            .Comment("Mobile")
            .Build();

        Sent(packet).Should().Be("M0LTE-9>APZ001,WIDE1-1,WIDE2-1:!5127.26N/00058.69W>088/036/A=000120Mobile");
    }

    [Fact]
    public void A_position_report_with_messaging_and_a_timestamp()
    {
        AprsPacket packet = Me.Position(51.4543, -0.9781).Symbol(AprsSymbol.House)
            .Messaging().Timestamp(new DateTime(2026, 9, 26, 14, 5, 0, DateTimeKind.Utc))
            .Build();

        Sent(packet).Should().Be("M0LTE>APZ001:@261405z5127.26N/00058.69W-");
    }

    [Fact]
    public void A_compressed_position_with_extra_precision_decodes_to_what_was_given()
    {
        AprsPacket packet = Me.Position(51.4543, -0.9781).Symbol(AprsSymbol.Car).Compressed().Build();

        var heard = (AprsPositionReport)AprsPacket.Decode(Sent(packet)).Data;
        heard.IsCompressed.Should().BeTrue();
        heard.Position.Latitude.Should().BeApproximately(51.4543, 0.00001);
        heard.Position.Longitude.Should().BeApproximately(-0.9781, 0.00001);
    }

    [Fact]
    public void Other_units_are_converted_to_the_ones_aprs_sends()
    {
        var report = Me.Position(51.4543, -0.9781).Symbol(AprsSymbol.Car).SpeedKmh(100).AltitudeMetres(100).ToData();

        report.SpeedKnots.Should().BeApproximately(54, 0.1);
        report.AltitudeFeet.Should().BeApproximately(328.08, 0.01);
    }

    [Fact]
    public void Ambiguity_blanks_digits_whenever_it_is_set()
    {
        Sent(Me.Position(51.4543, -0.9781).Ambiguity(2).Symbol(AprsSymbol.House).Build())
            .Should().Be("M0LTE>APZ001:!5127.  N/00058.  W-");
        Sent(Me.Object("HIDDEN").Ambiguity(1).At(51.4543, -0.9781).Symbol(AprsSymbol.House).Timestamp(AprsTimestamp.DayHoursMinutes(26, 14, 5)).Build())
            .Should().Be("M0LTE>APZ001:;HIDDEN   *261405z5127.2 N/00058.6 W-");
    }

    [Fact]
    public void A_message_with_an_id()
    {
        Sent(Me.Message("G3NRW", "Hi Ian").WithId("01").Build()).Should().Be("M0LTE>APZ001::G3NRW    :Hi Ian{01");
    }

    [Fact]
    public void A_message_in_reply_ack_form()
    {
        Sent(Me.Message("G3NRW", "Hi Ian").WithId("02").ReplyAck("17").Build()).Should().Be("M0LTE>APZ001::G3NRW    :Hi Ian{02}17");
        Sent(Me.Message("G3NRW", "Hi Ian").WithId("03").ReplyAck().Build()).Should().Be("M0LTE>APZ001::G3NRW    :Hi Ian{03}");
    }

    [Fact]
    public void Acks_and_rejects()
    {
        Sent(Me.Ack("G3NRW", "01").Build()).Should().Be("M0LTE>APZ001::G3NRW    :ack01");
        Sent(Me.Reject("G3NRW", "01").Build()).Should().Be("M0LTE>APZ001::G3NRW    :rej01");
        Sent(Me.Ack("G3NRW", "02").ReplyAck("17").Build()).Should().Be("M0LTE>APZ001::G3NRW    :ack02}17");
    }

    [Fact]
    public void Bulletins()
    {
        Sent(Me.Bulletin('1', "Net tonight 8pm").Build()).Should().Be("M0LTE>APZ001::BLN1     :Net tonight 8pm");
        Sent(Me.Bulletin('A', "Rally Sunday").Build()).Should().Be("M0LTE>APZ001::BLNA     :Rally Sunday");
        Sent(Me.GroupBulletin('4', "WX", "Gales later").Build()).Should().Be("M0LTE>APZ001::BLN4WX   :Gales later");
    }

    [Fact]
    public void An_object_announcing_a_repeater()
    {
        AprsPacket packet = Me.Object("MYRPTR").At(51.45, -0.98).Symbol(AprsSymbol.Repeater)
            .Timestamp(AprsTimestamp.DayHoursMinutes(25, 18, 30))
            .Frequency(145.725, tone: 118.8, offsetKHz: -600)
            .Build();

        Sent(packet).Should().Be("M0LTE>APZ001:;MYRPTR   *251830z5127.00N/00058.80Wr145.725MHz T118 -060");
    }

    [Fact]
    public void An_object_is_stamped_now_unless_told_otherwise()
    {
        DateTime before = DateTime.UtcNow;
        var sent = (AprsObjectReport)AprsPacket.Decode(Sent(Me.Object("MYRPTR").At(51.45, -0.98).Symbol(AprsSymbol.Repeater).Build())).Data;

        sent.Timestamp!.Value.Format.Should().Be(AprsTimestampFormat.DayHoursMinutesUtc);
        sent.Timestamp.Value.Resolve(before).Should().BeCloseTo(before, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void A_permanent_object_and_a_killed_one()
    {
        AprsObjectBuilder obj = Me.Object("MYRPTR").At(51.45, -0.98).Symbol(AprsSymbol.Repeater).Permanent();

        Sent(obj.Build()).Should().Be("M0LTE>APZ001:;MYRPTR   *111111z5127.00N/00058.80Wr");
        Sent(obj.Kill().Build()).Should().Be("M0LTE>APZ001:;MYRPTR   _111111z5127.00N/00058.80Wr");
    }

    [Fact]
    public void An_item_and_a_killed_one()
    {
        AprsItemBuilder item = Me.Item("AID #2").At(49 + (3.50 / 60), -(72 + (1.75 / 60))).Symbol(AprsSymbol.AidStation);

        Sent(item.Build()).Should().Be("M0LTE>APZ001:)AID #2!4903.50N/07201.75WA");
        Sent(item.Kill().Build()).Should().Be("M0LTE>APZ001:)AID #2_4903.50N/07201.75WA");
    }

    [Fact]
    public void A_mic_e_report_computes_its_destination_and_is_not_an_emergency_by_default()
    {
        AprsPacket packet = Aprs.From("M0LTE-9").Via("WIDE1-1").MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).Course(215).Speed(9).Messaging().Build();

        var heard = (AprsMicEReport)AprsPacket.Decode(Sent(packet)).Data;
        packet.Destination.Value.Should().NotBe(Aprs.DefaultDestination);
        heard.Message.Should().Be(AprsMicEMessage.OffDuty);
        heard.MessagingCapable.Should().BeTrue();
        heard.Position.Latitude.Should().BeApproximately(42.179, 0.01 / 60);
        heard.Position.Longitude.Should().BeApproximately(-71.1985, 0.01 / 60);
        heard.CourseDegrees.Should().Be(215);
        heard.SpeedKnots.Should().Be(9);

        var enRoute = (AprsMicEReport)AprsPacket.Decode(Sent(Me.MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).Message(AprsMicEMessage.EnRoute).Build())).Data;
        enRoute.Message.Should().Be(AprsMicEMessage.EnRoute);
    }

    [Fact]
    public void A_weather_report_with_a_position()
    {
        AprsPacket packet = Me.Weather().At(51.45, -0.98).Wind(220, 4, gust: 5).Temperature(77).Humidity(54).Pressure(1013.2).Build();

        Sent(packet).Should().Be("M0LTE>APZ001:!5127.00N/00058.80W_220/004g005t077h54b10132");
    }

    [Fact]
    public void A_weather_report_without_a_position()
    {
        AprsPacket packet = Me.Weather().Timestamp(new DateTime(2026, 9, 25, 18, 30, 0, DateTimeKind.Utc))
            .Wind(220, 4, gust: 5).TemperatureCelsius(25).Rain(lastHour: 0.1, sinceMidnight: 0.25)
            .Build();

        Sent(packet).Should().Be("M0LTE>APZ001:_09251830c220s004g005t077r010P025");
    }

    [Fact]
    public void A_weather_report_needs_a_position_for_a_symbol_or_compression()
    {
        FluentActions.Invoking(() => Me.Weather().Temperature(77).Compressed().Build()).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => Me.Weather().Temperature(77).Symbol(AprsSymbol.WeatherStationWithDigipeater).Build()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_status_report()
    {
        Sent(Me.Status("Net Control").Build()).Should().Be("M0LTE>APZ001:>Net Control");
        Sent(Me.Status("Net Control").Timestamp(AprsTimestamp.DayHoursMinutes(26, 14, 5)).Build()).Should().Be("M0LTE>APZ001:>261405zNet Control");
        Sent(Me.Status("On the air").Locator("IO91SX", AprsSymbol.House).Build()).Should().Be("M0LTE>APZ001:>IO91SX/- On the air");
    }

    [Fact]
    public void Telemetry_and_what_it_means()
    {
        AprsStation wx = Aprs.From("M0LTE-11");

        Sent(wx.Telemetry(123).Analog(172, 123, 150, 50, 113).Digital("10100000").Build())
            .Should().Be("M0LTE-11>APZ001:T#123,172,123,150,050,113,10100000");
        Sent(wx.Telemetry(7).Analog(1.5m).Build()).Should().Be("M0LTE-11>APZ001:T#007,1.5,,,,,00000000");
        Sent(wx.Telemetry(8).Analog(null, 2.5m).Build()).Should().Be("M0LTE-11>APZ001:T#008,,2.5,,,,00000000");
        Sent(wx.TelemetryNames("Battery", "Temp").Build()).Should().Be("M0LTE-11>APZ001::M0LTE-11 :PARM.Battery,Temp");
        Sent(wx.TelemetryUnits("V", "degC").Build()).Should().Be("M0LTE-11>APZ001::M0LTE-11 :UNIT.V,degC");
        Sent(wx.TelemetryCoefficients(0, 0.075, 0, 0, 0.5, -40).Build()).Should().Be("M0LTE-11>APZ001::M0LTE-11 :EQNS.0,0.075,0,0,0.5,-40");
        Sent(wx.TelemetryBits(0xFF, "Garden station").Build()).Should().Be("M0LTE-11>APZ001::M0LTE-11 :BITS.11111111,Garden station");
    }

    [Fact]
    public void Comment_telemetry_rides_in_a_position_report()
    {
        AprsPacket packet = Me.Position(51.4543, -0.9781).Symbol(AprsSymbol.House).Comment("Home").Telemetry(1, 100, 200).Build();

        var heard = (AprsPositionReport)AprsPacket.Decode(Sent(packet)).Data;
        heard.Telemetry.Should().Be(new AprsCommentTelemetry { Sequence = 1, Analog = [100, 200] });
        heard.Comment.Should().Be("Home");
    }

    [Fact]
    public void The_station_is_immutable_and_sets_the_header()
    {
        AprsStation plain = Aprs.From("M0LTE");
        AprsStation routed = plain.To("APXYZ1").Via("WIDE2-1");

        plain.Destination.Value.Should().Be("APZ001");
        plain.Path.Should().BeEmpty();
        Sent(routed.Status("Hi").Build()).Should().Be("M0LTE>APXYZ1,WIDE2-1:>Hi");
    }

    [Fact]
    public void To_data_gives_the_record_for_anything_the_builder_does_not_cover()
    {
        AprsPositionReport report = Me.Position(51.4543, -0.9781).Symbol(AprsSymbol.ValueSign).ToData();

        Sent(Me.Data(report with { SignpostText = "30" }).Build()).Should().Be("M0LTE>APZ001:!5127.26N\\00058.69Wm{30}");
    }

    [Fact]
    public void Missing_pieces_are_named()
    {
        FluentActions.Invoking(() => Me.Position(51.4543, -0.9781).Build())
            .Should().Throw<InvalidOperationException>().WithMessage("*symbol*");
        FluentActions.Invoking(() => Me.Object("MYRPTR").Symbol(AprsSymbol.Repeater).Build())
            .Should().Throw<InvalidOperationException>().WithMessage("*position*");
    }

    [Fact]
    public void What_the_spec_forbids_is_refused_when_built()
    {
        FluentActions.Invoking(() => Me.Message("G3NRW", "Hi").WithId("TOOLONG").Build()).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Me.Object("NAMEISTOOLONG").At(51.45, -0.98).Symbol(AprsSymbol.Repeater).Build()).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Me.Position(51.4543, -0.9781).Symbol(AprsSymbol.Car).Compressed().Phg(new AprsPhg(5, 3, 6, 0)).Build()).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Me.Telemetry(1).Analog(1, 2, 3, 4, 5, 6)).Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------------------ builder rules (vectors rulings B1-B8)

    [Fact]
    public void A_course_of_0_is_unknown()
    {
        // 000 uncompressed, no course in Mic-E, and refused compressed, which has no unknown course (B1).
        Sent(Me.Position(49.0583333, -72.0291667).Symbol(AprsSymbol.Car).Course(0).Speed(36).Build())
            .Should().Be("M0LTE>APZ001:!4903.50N/07201.75W>000/036");
        ((AprsMicEReport)AprsPacket.Decode(Sent(Me.MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).Course(0).Speed(9).Build())).Data)
            .CourseDegrees.Should().BeNull();
        FluentActions.Invoking(() => Me.Position(49.5, -72.75).Symbol(AprsSymbol.Car).Compressed().Course(0).Speed(36).Build()).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_fractional_value_is_rounded_once_to_the_step_its_field_holds()
    {
        // A GPS heading is fractional. Uncompressed it is whole degrees; compressed it is 4-degree
        // steps, and 301.7 is the step nearest 301.7 (300), not nearest 302 (F2).
        Sent(Me.Position(49.0583333, -72.0291667).Symbol(AprsSymbol.Car).Course(301.7).Speed(36).Build())
            .Should().Be("M0LTE>APZ001:!4903.50N/07201.75W>302/036");
        ((AprsPositionReport)AprsPacket.Decode(Sent(Me.Position(49.5, -72.75).Symbol(AprsSymbol.Car).Compressed().Course(301.7).Speed(36).Build())).Data)
            .CourseDegrees.Should().Be(300);
        Sent(Me.Position(49.0583333, -72.0291667).Symbol(AprsSymbol.Car).Course(0.3).Speed(36).Build())
            .Should().Contain(">000/036");

        // Weather: a half rounds away from zero; a compressed wind direction is a 4-degree step.
        Sent(Me.Weather().At(49.0583333, -72.0291667).Wind(220.4, 4).Gust(5.5).Temperature(76.5).Humidity(54.5).Luminosity(1000.5).Build())
            .Should().Be("M0LTE>APZ001:!4903.50N/07201.75W_220/004g006t077h55l001");
        ((AprsPositionReport)AprsPacket.Decode(Sent(Me.Weather().At(49.0583333, -72.0291667).Compressed().Wind(41.6, 4).Build())).Data)
            .Weather!.WindDirectionDegrees.Should().Be(40);
        FluentActions.Invoking(() => Me.Weather().At(49.0583333, -72.0291667).Wind(220, 4).Humidity(0.4).Build()).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Other_units_are_converted_and_rounded_once()
    {
        // 100 km/h is 53.996 knots, written 054; 100.5 feet rounds away from zero to 101 (B2, F2).
        Sent(Me.Position(49.0583333, -72.0291667).Symbol(AprsSymbol.Car).Course(90).SpeedKmh(100).Altitude(100.5).Build())
            .Should().Be("M0LTE>APZ001:!4903.50N/07201.75W>090/054/A=000101");
        Sent(Me.Position(49.0583333, -72.0291667).Symbol(AprsSymbol.Car).AltitudeMetres(-3.81).Build())
            .Should().Be("M0LTE>APZ001:!4903.50N/07201.75W>/A=-00013");
        Sent(Me.Weather().At(49.0583333, -72.0291667).Wind(220, 4).Gust(5).TemperatureCelsius(25.25).RainMillimetres(lastHour: 1.27).Build())
            .Should().Be("M0LTE>APZ001:!4903.50N/07201.75W_220/004g005t077r005");
    }

    [Fact]
    public void A_mic_e_altitude_in_metres_goes_in_the_altitude_bytes_and_one_in_feet_in_a_slash_a()
    {
        var metres = (AprsMicEReport)AprsPacket.Decode(Sent(Me.MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).AltitudeMetres(100.4).Build())).Data;
        metres.AltitudeFeet.Should().BeApproximately(100 / 0.3048, 1e-9);
        metres.Comment.Should().BeEmpty();

        string feet = Sent(Me.MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).Altitude(0).Build());
        feet.Should().EndWith("/A=000000");
    }

    [Fact]
    public void Ambiguity_cuts_the_position_into_the_box_that_holds_it()
    {
        // 49 deg 03.596 min with one level is the box 03.5x, not 03.6x (B3); 132.89 degrees is
        // 53.40 minutes, its box 53.4x, though 132.89 x 6000 is just under 797340 in binary.
        Sent(Me.Position(49 + (3.596 / 60), 132.89).Ambiguity(1).Symbol(AprsSymbol.House).Build())
            .Should().Be("M0LTE>APZ001:!4903.5 N/13253.4 E-");
        Sent(Me.Position(89.99999, -179.99999).Ambiguity(4).Symbol(AprsSymbol.House).Build())
            .Should().Be("M0LTE>APZ001:!89  .  N/179  .  W-");
    }

    [Fact]
    public void A_mic_e_type_code_says_whether_the_station_can_message()
    {
        // ` it can, ' it cannot (B7).
        ((AprsMicEReport)AprsPacket.Decode(Sent(Me.MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).Messaging().Build())).Data).TypeCode.Should().Be('`');
        ((AprsMicEReport)AprsPacket.Decode(Sent(Me.MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).Build())).Data).TypeCode.Should().Be('\'');
    }

    [Fact]
    public void A_timestamp_is_cut_to_its_resolution()
    {
        // 09:23:59 is 0923, the minute a clock shows (B5).
        Sent(Me.Position(49.0583333, -72.0291667).Symbol(AprsSymbol.House).Timestamp(new DateTime(2026, 11, 10, 9, 23, 59, DateTimeKind.Utc)).Build())
            .Should().Be("M0LTE>APZ001:/100923z4903.50N/07201.75W-");
    }

    [Fact]
    public void A_telemetry_sequence_is_0_to_999_in_three_digits()
    {
        // B6, B8: refused past 999, never wrapped; values follow F11.
        Sent(Me.Telemetry(7).Analog(5m, 190.0m, 1.5m, 0m, 1000m).Digital("10101010").Build())
            .Should().Be("M0LTE>APZ001:T#007,005,190,1.5,000,1000,10101010");
        FluentActions.Invoking(() => Me.Telemetry(1000)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Out_of_range_is_refused_not_clamped()
    {
        // B8: an item name outside 3-9, a message ID that is empty or over 5 characters.
        FluentActions.Invoking(() => Me.Item("AB").At(51.45, -0.98).Symbol(AprsSymbol.Repeater).Build()).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Me.Item("TENLETTERS").At(51.45, -0.98).Symbol(AprsSymbol.Repeater).Build()).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Me.Message("G3NRW", "Hi").WithId("").Build()).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("/{}")]
    [InlineData("/x}AB")]
    public void A_mic_e_comment_after_a_slash_a_takes_a_space_where_a_slash_would_join_an_altitude(string comment)
    {
        // After /A=000000 a '/' delimiter would start //{} or 0/x} (ruling E12).
        string sent = Sent(Me.MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).Altitude(0).Comment(comment).Build());
        sent.Should().EndWith("/A=000000 " + comment);
        ((AprsMicEReport)AprsPacket.Decode(sent).Data).Comment.Should().Be(comment);
    }

    [Theory]
    [InlineData("/x}ABQTH")]
    [InlineData("/x}Test 123")]
    [InlineData("//}abc")]
    public void A_mic_e_comment_starting_with_a_slash_takes_a_space_where_a_slash_would_join_an_altitude(string comment)
    {
        // A '/' delimiter would make //x} an altitude, so the delimiter is a space (ruling E12).
        string sent = Sent(Me.MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).Course(1).Speed(199.6).Messaging().Comment(comment).Build());
        sent.Should().EndWith("` " + comment);
        ((AprsMicEReport)AprsPacket.Decode(sent).Data).Comment.Should().Be(comment);
    }

    [Fact]
    public void A_mic_e_comment_starting_with_a_slash_and_a_dao_takes_a_space_too()
    {
        string sent = Sent(Me.MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).Speed(143.32).Dao().Comment("/x}Digi").Build());
        var heard = (AprsMicEReport)AprsPacket.Decode(sent).Data;
        heard.Comment.Should().Be("/x}Digi");
        heard.Dao.Should().NotBeNull();
    }

    [Fact]
    public void A_compressed_course_and_speed_gets_the_default_compression_type()
    {
        var heard = (AprsPositionReport)AprsPacket.Decode(Sent(Me.Position(49.5, -72.75).Symbol(AprsSymbol.Car).Compressed().Course(88).Speed(36).Build())).Data;
        heard.CompressionType.Should().Be(AprsCompressionType.Default);
    }

    [Fact]
    public void A_slash_delimiter_is_kept_after_an_altitude_in_the_mic_e_bytes()
    {
        // With the altitude first in its bytes no later one is looked for, so //x} is text (E12).
        string sent = Sent(Me.MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).AltitudeMetres(100).Comment("/x}Hello").Build());
        sent.Should().EndWith("}//x}Hello");
    }

    [Fact]
    public void The_67_character_limit_counts_code_points()
    {
        // 67 code points, 68 UTF-16 units: the satellite emoji is one character.
        string text = "  ADRAD Bastel Owend al \U0001F4E1Donneschden um 17h Rue du Couvent n\u00b04,Digi";
        text.Length.Should().Be(68);
        Sent(Me.Bulletin('0', text).Build()).Should().EndWith(text);
        FluentActions.Invoking(() => Me.Bulletin('0', text + "x").Build()).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_device_lookup_is_case_sensitive()
    {
        // aprs-deviceid lists tocalls in upper case, and its wildcard n is a digit (A1).
        AprsDeviceIdentification.FromTocall("APNL02").Should().NotBeNull();
        AprsDeviceIdentification.FromTocall("APNl02").Should().BeNull();
        AprsDeviceIdentification.FromTocall("APDR16").Should().NotBeNull();
        AprsDeviceIdentification.FromTocall("APdR16").Should().BeNull();
    }

    /// <summary>The packet as TNC2 text, after checking it reads back under strict parsing with no
    /// warning or error (a positionless weather report still gets its "not recommended" note).</summary>
    private static string Sent(AprsPacket packet)
    {
        string line = packet.ToString();
        AprsPacket heard = AprsPacket.Decode(line, AprsParseOptions.Strict);
        heard.Diagnostics.Where(d => d.Severity != AprsDiagnosticSeverity.Info).Should().BeEmpty(line);
        heard.Data.Should().NotBeOfType<AprsUnrecognizedData>();
        return line;
    }
}
