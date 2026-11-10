namespace Packet.Aprs.Tests.Encoding;

/// <summary>
/// The 1.0 API contract: what a refusal is, how a builder reads, and what data carries so that
/// writing it again gives the same bytes.
/// </summary>
public class ApiContractTests
{
    private static readonly AprsStation Me = AprsStation.From("M0LTE");

    private static string Sent(AprsPacket packet) => System.Text.Encoding.UTF8.GetString(packet.ToTnc2());

    [Fact]
    public void Every_refusal_is_an_AprsEncodeException()
    {
        var badHumidity = new AprsWeather { HumidityPercent = 0.4 };
        var position = new AprsPositionReport { Position = new AprsPosition(51, 0), Symbol = AprsSymbol.Car, Weather = badHumidity };

        FluentActions.Invoking(() => position.ToInformationField()).Should().Throw<AprsEncodeException>();
        FluentActions.Invoking(() => Me.Position(51, 0).Symbol(AprsSymbol.Car).CourseDegrees(400).Build()).Should().Throw<AprsEncodeException>();
        FluentActions.Invoking(() => Me.Telemetry(1000)).Should().Throw<AprsEncodeException>();
        FluentActions.Invoking(() => Me.Telemetry(1).Analog(1, 2, 3, 4, 5, 6)).Should().Throw<AprsEncodeException>();
        FluentActions.Invoking(() => Me.Telemetry(1).Bits("1010")).Should().Throw<AprsEncodeException>();
        FluentActions.Invoking(() => AprsStation.From("not a callsign!")).Should().Throw<AprsEncodeException>();
        FluentActions.Invoking(() => Me.To("")).Should().Throw<AprsEncodeException>();
        FluentActions.Invoking(() => Me.Via("WIDE1-1", "no good")).Should().Throw<AprsEncodeException>();
        FluentActions.Invoking(() => AprsPacket.Create("M0LTE", new AprsStatusReport { Text = "x" }, "bad address")).Should().Throw<AprsEncodeException>();
        FluentActions.Invoking(() => AprsPacket.Create("M0LTE", new AprsStatusReport { Text = "x" }, "APZ001", "WIDE1-1,no good")).Should().Throw<AprsEncodeException>();
    }

    [Fact]
    public void A_refusal_names_no_internal_parameter()
    {
        AprsEncodeException refusal = FluentActions.Invoking(() => new AprsStatusReport { Text = "a\nb" }.ToInformationField())
            .Should().Throw<AprsEncodeException>().Which;

        refusal.Message.Should().NotContain("Parameter");
        refusal.ParamName.Should().BeNull();
    }

    [Fact]
    public void Missing_builder_input_and_null_arguments_are_not_refusals()
    {
        FluentActions.Invoking(() => Me.Position(51, 0).Build()).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => Me.Message(null!, "x")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => AprsPacket.Create("M0LTE", null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_mic_e_packet_is_created_like_any_other_and_computes_its_destination()
    {
        var report = new AprsMicEReport { Position = new AprsPosition(42.179, -71.1985), Symbol = AprsSymbol.Car, SpeedKnots = 9, CourseDegrees = 215, TypeCode = '`' };

        AprsPacket packet = AprsPacket.Create(AprsAddress.Parse("N0CALL"), report);

        packet.Destination.Value.Should().NotBe(AprsStation.DefaultDestination);
        AprsPacket.Decode(packet.ToTnc2()).Data.Should().BeOfType<AprsMicEReport>();
        AprsPacket.Create("N0CALL", report).Should().Be(packet);
        FluentActions.Invoking(() => AprsPacket.Create(AprsAddress.Parse("N0CALL"), report, AprsAddress.Parse("APZ001")))
            .Should().Throw<AprsEncodeException>();
    }

    [Fact]
    public void A_mic_e_information_field_alone_is_refused_because_the_destination_is_half_the_position()
    {
        var report = new AprsMicEReport { Position = new AprsPosition(42.179, -71.1985), Symbol = AprsSymbol.Car, TypeCode = '`' };

        FluentActions.Invoking(() => report.ToInformationField()).Should().Throw<AprsEncodeException>();
        FluentActions.Invoking(() => new AprsThirdPartyTraffic { Packet = AprsPacket.Create("N0CALL", report) }.ToInformationField()).Should().NotThrow();
    }

    [Fact]
    public void An_unset_destination_is_the_default_one()
    {
        AprsPacket.Create("M0LTE", new AprsStatusReport { Text = "x" }).Destination.Value.Should().Be(AprsStation.DefaultDestination);
    }

    [Fact]
    public void A_killed_flag_and_an_old_data_flag_default_to_false_and_a_builder_can_take_the_kill_back()
    {
        new AprsObjectReport { Name = "X", Position = default, Symbol = default }.IsKilled.Should().BeFalse();
        new AprsItemReport { Name = "XXX", Position = default, Symbol = default }.IsKilled.Should().BeFalse();
        new AprsMicEReport { Position = default, Symbol = default }.IsOldData.Should().BeFalse();

        AprsItemBuilder item = Me.Item("AID #2").At(51.5, -1.5).Symbol(AprsSymbol.AidStation);
        Sent(item.Killed().Build()).Should().Contain("_");
        Sent(item.Killed(false).Build()).Should().Contain(")AID #2!");
    }

    [Fact]
    public void Each_weather_setter_sets_one_value()
    {
        AprsPacket packet = Me.Weather().At(51.45, -0.98)
            .WindDirectionDegrees(220).WindSpeedMph(4).WindGustMph(5).TemperatureFahrenheit(77)
            .RainLastHourInches(0.1).RainLast24HoursInches(0.2).RainSinceMidnightMillimetres(2.54)
            .HumidityPercent(54).PressureMillibars(1013.2).LuminosityWattsPerSquareMetre(200).SnowfallLast24HoursInches(1.5)
            .Build();

        Sent(packet).Should().Be("M0LTE>APZ001:!5127.00N/00058.80W_220/004g005t077r010p020P010h54b10132L200s1.5");
    }

    [Fact]
    public void A_status_locator_and_its_symbol_are_two_setters()
    {
        Sent(Me.Status("On the air").Locator("IO91SX").Symbol(AprsSymbol.House).Build()).Should().Be("M0LTE>APZ001:>IO91SX/- On the air");
        FluentActions.Invoking(() => Me.Status("x").Locator("IO91SX").Build()).Should().Throw<AprsEncodeException>();
    }

    [Fact]
    public void Telemetry_metadata_is_addressed_to_the_sender_unless_told_otherwise()
    {
        AprsStation wx = AprsStation.From("M0LTE-11");

        Sent(wx.TelemetryNames("Battery").Build()).Should().Be("M0LTE-11>APZ001::M0LTE-11 :PARM.Battery");
        Sent(wx.TelemetryUnits("V").Addressee("N0CALL").Build()).Should().Be("M0LTE-11>APZ001::N0CALL   :UNIT.V");
        Sent(wx.TelemetryCoefficients(0, 0.5, 0).Addressee("N0CALL-1").Build()).Should().Be("M0LTE-11>APZ001::N0CALL-1 :EQNS.0,0.5,0");
        Sent(wx.TelemetryBits("10000000", "Proj").Addressee("N0CALL").Build()).Should().Be("M0LTE-11>APZ001::N0CALL   :BITS.10000000,Proj");
        wx.TelemetryBits("10000000").ToData().Should().BeOfType<AprsTelemetryBitSense>().Which.Bits.Should().Be("10000000");
    }

    [Fact]
    public void A_voice_frequency_is_rounded_once_to_its_step_and_never_refused_for_extra_decimals()
    {
        Sent(Me.Object("MYRPTR").At(51.45, -0.98).Symbol(AprsSymbol.Repeater).Timestamp(AprsTimestamp.DayHoursMinutes(25, 18, 30)).Frequency(145.7251).Build())
            .Should().EndWith("r145.725MHz");
        Sent(Me.Object("MYRPTR").At(51.45, -0.98).Symbol(AprsSymbol.Repeater).Timestamp(AprsTimestamp.DayHoursMinutes(25, 18, 30)).Frequency(145.7255).Build())
            .Should().EndWith("r145.726MHz");
        Sent(Me.Object("MYRPTR").At(51.45, -0.98).Symbol(AprsSymbol.Repeater).Timestamp(AprsTimestamp.DayHoursMinutes(25, 18, 30)).Frequency(145.725, AprsToneType.Ctcss, 88).Build())
            .Should().EndWith("r145.725MHz C088");
        Sent(Me.Object("MYRPTR").At(51.45, -0.98).Symbol(AprsSymbol.Repeater).Timestamp(AprsTimestamp.DayHoursMinutes(25, 18, 30)).Frequency(145.725, AprsToneType.Dcs, 23, 600).Build())
            .Should().EndWith("r145.725MHz D023 +060");
        FluentActions.Invoking(() => Me.Object("MYRPTR").At(51.45, -0.98).Symbol(AprsSymbol.Repeater).Frequency(145.725, AprsToneType.Tone).Build())
            .Should().Throw<AprsEncodeException>();
    }

    [Fact]
    public void A_footprint_radius_is_fractional_and_rounded_once_when_encoded()
    {
        var query = new AprsGeneralQuery { QueryType = "APRS", Footprint = new AprsQueryFootprint(34.36, -0.1715, 199.6) };

        System.Text.Encoding.UTF8.GetString(query.ToInformationField()).Should().Be("?APRS? 34.36,-0.1715,0200");
    }

    [Fact]
    public void Decoded_numbers_write_back_as_sent_and_a_changed_number_does_not()
    {
        var telemetry = (AprsTelemetryReport)AprsPacket.Decode("N0CALL>APZ001:T#073,073,190.0,.53,,5,01010101").Data;
        var coefficients = (AprsTelemetryCoefficients)AprsPacket.Decode("N0CALL>APZ001::N0CALL   :EQNS.10E60,.5,0").Data;
        var query = (AprsGeneralQuery)AprsPacket.Decode("N0CALL>APZ001:?APRS?-.1715, 34.0,0200").Data;

        telemetry.AnalogText.Should().Equal("073", "190.0", ".53", "", "5");
        telemetry.Bits.Should().Be("01010101");
        coefficients.CoefficientsText.Should().Equal("10E60", ".5", "0");
        query.Footprint!.Value.LatitudeText.Should().Be("-.1715");

        System.Text.Encoding.UTF8.GetString(telemetry.ToInformationField()).Should().Be("T#073,073,190.0,.53,,5,01010101");
        System.Text.Encoding.UTF8.GetString((telemetry with { Analog = [73, 191, 0.53, null, 5] }).ToInformationField()).Should().Be("T#073,073,191,.53,,5,01010101");
        telemetry.Should().Be(telemetry with { AnalogText = [] });
        coefficients.Should().Be(coefficients with { CoefficientsText = [] });
        System.Text.Encoding.UTF8.GetString((telemetry with { AnalogText = [] }).ToInformationField()).Should().Be("T#073,073,190,0.53,,005,01010101");
    }

    [Fact]
    public void A_mic_e_altitude_from_a_slash_a_is_written_back_there()
    {
        // 0 feet is a whole number of metres, which the Mic-E altitude bytes could hold; given in feet it goes in a /A=, and the decoder says so.
        AprsPacket builtInFeet = Me.MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).AltitudeFeet(0).Build();
        var fromComment = (AprsMicEReport)AprsPacket.Decode(builtInFeet.ToTnc2()).Data;
        var fromBytes = (AprsMicEReport)AprsPacket.Decode("N1JCM-9>TRQP7T,WA1PLE-4*:`c'wl|+>/`\"4-}_%").Data;
        AprsPacket builtInMetres = Me.MicE(42.179, -71.1985).Symbol(AprsSymbol.Car).AltitudeMetres(100).Build();

        ((AprsMicEReport)builtInFeet.Data).AltitudeInComment.Should().BeTrue();
        fromComment.AltitudeInComment.Should().BeTrue();
        fromBytes.AltitudeInComment.Should().BeFalse();
        fromBytes.AltitudeFeet.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(builtInFeet.Information.Span).Should().Contain("/A=");
        System.Text.Encoding.UTF8.GetString(builtInMetres.Information.Span).Should().NotContain("/A=");

        AprsPacket.Create(AprsAddress.Parse("M0LTE"), fromComment).Information.ToArray().Should().Equal(builtInFeet.Information.ToArray());
        AprsPacket.Create(AprsAddress.Parse("M0LTE"), fromComment with { AltitudeInComment = false }).Information.ToArray()
            .Should().NotEqual(builtInFeet.Information.ToArray());
    }

    [Fact]
    public void Every_parse_option_flag_is_allow_plus_the_code_it_tolerates()
    {
        string[] codes = [.. Enum.GetNames<AprsDiagnosticCode>()];

        foreach (System.Reflection.PropertyInfo flag in typeof(AprsParseOptions).GetProperties().Where(p => p.PropertyType == typeof(bool)))
        {
            flag.Name.Should().StartWith("Allow");
            codes.Should().Contain(flag.Name["Allow".Length..], "a flag is named for the code it tolerates");
        }
    }
}
