using Packet.Aprs.Internal;

namespace Packet.Aprs.Tests.Encoding;

/// <summary>
/// The writers' rules the vectors cannot pin one by one (README, Read-back, Refusal reasons): what a
/// line or a frame may carry, the order of refusals, a header written on its own, display
/// conversions, empties in a builder, and the regressions the review of this library found.
/// </summary>
public class WriterRuleTests
{
    private static readonly AprsAddress Me = AprsAddress.Parse("N0CALL");

    private static AprsPacket Held(string info, string destination = "APZ001", params AprsPathEntry[] path) =>
        AprsPacket.Decode(Me, destination.Length == 0 ? AprsAddress.CreateUnchecked("") : AprsAddress.Parse(destination), path, System.Text.Encoding.Latin1.GetBytes(info));

    private static AprsRefusalCode Refused(Func<object> write) =>
        FluentActions.Invoking(write).Should().Throw<AprsEncodeException>().Which.Code;

    // ------------------------------------------------------------------ the order of refusals

    [Fact]
    public void The_refusal_order_is_the_vectors_order()
    {
        AprsRefusalCode[] order =
        [
            AprsRefusalCode.NotEncodable, AprsRefusalCode.InvalidAddress, AprsRefusalCode.InvalidPath, AprsRefusalCode.InvalidThirdPartyHeader,
            AprsRefusalCode.InvalidAddressee, AprsRefusalCode.InvalidName, AprsRefusalCode.InvalidTimestamp, AprsRefusalCode.NotANumber,
            AprsRefusalCode.ValueOutOfRange, AprsRefusalCode.ValueHasNoForm, AprsRefusalCode.InvalidCode, AprsRefusalCode.InvalidSymbol,
            AprsRefusalCode.InvalidCharacter, AprsRefusalCode.TooLong, AprsRefusalCode.PaddingSpace, AprsRefusalCode.Malformed,
            AprsRefusalCode.InvalidMessageId, AprsRefusalCode.InvalidNmea, AprsRefusalCode.ExtensionConflict, AprsRefusalCode.FieldNotAllowed,
            AprsRefusalCode.SymbolMismatch, AprsRefusalCode.MissingField, AprsRefusalCode.ValueGivenTwice, AprsRefusalCode.TextReadsAsField,
            AprsRefusalCode.DoesNotReadBack, AprsRefusalCode.InformationFieldTooLong, AprsRefusalCode.Ax25CannotCarry, AprsRefusalCode.Tnc2CannotCarry,
        ];
        order.Select(InfoWriter.Rank).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        order.Should().BeEquivalentTo(Enum.GetValues<AprsRefusalCode>(), "every code has its place in the order");
    }

    // ------------------------------------------------------------------ what a line and a frame carry

    [Fact]
    public void A_line_break_inside_the_information_field_is_not_carried_by_a_line_but_by_a_frame()
    {
        AprsPacket packet = Held(">a\rb");
        Refused(packet.ToTnc2).Should().Be(AprsRefusalCode.Tnc2CannotCarry);
        packet.ToAx25Frame().Should().EndWith(">a\rb"u8.ToArray());
        Held(">a\nb").Invoking(p => p.ToTnc2()).Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.Tnc2CannotCarry);
    }

    [Fact]
    public void A_line_break_at_the_very_end_is_carried()
    {
        System.Text.Encoding.Latin1.GetString(Held(">hi\r\n").ToTnc2()).Should().Be("N0CALL>APZ001:>hi\r\n");
        System.Text.Encoding.Latin1.GetString(Held(">hi\n").ToTnc2()).Should().Be("N0CALL>APZ001:>hi\n");
    }

    [Fact]
    public void User_defined_data_that_would_end_the_line_is_written_as_a_field_but_not_as_a_line()
    {
        var data = new AprsUserDefinedData { UserId = 'Q', PacketType = 'x', Data = "a\nX>Y:>evil"u8.ToArray() };
        AprsPacket packet = AprsPacket.Create(Me, data);
        packet.Information.ToArray().Should().Equal([.. "{Qxa\nX>Y:>evil"u8.ToArray()]);
        Refused(packet.ToTnc2).Should().Be(AprsRefusalCode.Tnc2CannotCarry);
        packet.ToAx25Frame().Should().NotBeEmpty();
        data.ToInformationField().Should().NotBeEmpty("an information field written alone is not judged for it");
    }

    [Fact]
    public void An_empty_destination_is_written_in_a_line_and_refused_for_a_frame()
    {
        AprsPacket packet = Held(">x", destination: "");
        System.Text.Encoding.Latin1.GetString(packet.ToTnc2()).Should().Be("N0CALL>:>x");
        Refused(packet.ToAx25Frame).Should().Be(AprsRefusalCode.Ax25CannotCarry);
    }

    [Fact]
    public void A_call_a_frame_cannot_carry_is_refused_for_a_frame_and_carried_by_a_line()
    {
        AprsPacket lower = AprsPacket.Decode(AprsAddress.Parse("n0call"), AprsAddress.Parse("APZ001"), [], ">x"u8);
        Refused(lower.ToAx25Frame).Should().Be(AprsRefusalCode.Ax25CannotCarry);
        System.Text.Encoding.Latin1.GetString(lower.ToTnc2()).Should().Be("n0call>APZ001:>x");
        AprsPacket nine = AprsPacket.Decode(AprsAddress.Parse("N0CALLABC"), AprsAddress.Parse("APZ001"), [], ">x"u8);
        Refused(nine.ToAx25Frame).Should().Be(AprsRefusalCode.Ax25CannotCarry);
    }

    // ------------------------------------------------------------------ a header written on its own

    [Fact]
    public void An_inner_packet_written_on_its_own_is_held_to_the_outer_address_rule()
    {
        AprsPacket outer = AprsPacket.Decode("N0CALL>APRS:}a b>APRS,A*,B:>hi");
        AprsPacket inner = ((AprsThirdPartyTraffic)outer.Data).Packet;
        inner.Source.Value.Should().Be("a b");
        Refused(inner.ToTnc2).Should().Be(AprsRefusalCode.InvalidAddress);
        Refused(inner.ToAx25Frame).Should().Be(AprsRefusalCode.InvalidAddress);
        inner.ToString().Should().StartWith("a b>APRS,A*,B:", "showing a packet never refuses");
    }

    [Fact]
    public void An_inner_packet_whose_path_holds_a_q_construct_is_written_as_a_line_but_not_in_a_frame()
    {
        AprsPacket inner = ((AprsThirdPartyTraffic)AprsPacket.Decode("N0CALL>APRS:}N0CALL>APRS,qAR,X:>hi").Data).Packet;
        System.Text.Encoding.Latin1.GetString(inner.ToTnc2()).Should().Be("N0CALL>APRS,qAR,X:>hi");
        Refused(inner.ToAx25Frame).Should().Be(AprsRefusalCode.Ax25CannotCarry);
    }

    [Fact]
    public void An_inner_packet_with_an_empty_information_field_is_written_as_received()
    {
        AprsPacket inner = ((AprsThirdPartyTraffic)AprsPacket.Decode("N0CALL>APRS:}N0CALL>APRS:").Data).Packet;
        inner.Data.Should().BeOfType<AprsUnrecognizedData>();
        System.Text.Encoding.Latin1.GetString(inner.ToTnc2()).Should().Be("N0CALL>APRS:");
        inner.ToAx25Frame().Should().NotBeEmpty();
    }

    // ------------------------------------------------------------------ a display conversion is not a writer

    [Fact]
    public void A_display_conversion_never_refuses_and_shows_the_packet_as_held()
    {
        AprsPathEntry[] backwards = [new(AprsAddress.Parse("WIDE1-1")), new(AprsAddress.Parse("N0CALL-1"), true)];
        AprsPacket odd = AprsPacket.Decode(AprsAddress.CreateUnchecked("a b"), AprsAddress.CreateUnchecked(""), backwards, "{Qx\n\rbadÿ"u8);
        FluentActions.Invoking(odd.ToString).Should().NotThrow();
        odd.ToString().Should().StartWith("a b>,WIDE1-1,N0CALL-1*:");
        FluentActions.Invoking(() => AprsPathEntry.FormatList(backwards)).Should().NotThrow();
        AprsPathEntry.FormatList(backwards).Should().Be("WIDE1-1,N0CALL-1*");
        FluentActions.Invoking(() => new AprsPathEntry(default, true).ToString()).Should().NotThrow();
        FluentActions.Invoking(() => AprsPacket.Decode(AprsAddress.CreateUnchecked("x"), AprsAddress.CreateUnchecked(""), [new AprsPathEntry(AprsAddress.CreateUnchecked(""))], default).ToString()).Should().NotThrow();
    }

    // ------------------------------------------------------------------ builders

    [Fact]
    public void A_builder_refuses_an_empty_message_id_destination_and_ack_id_and_writes_no_text_for_an_empty_comment()
    {
        Refused(() => AprsStation.From("N0CALL").Message("G3NRW", "hi").MessageId("").Build()).Should().Be(AprsRefusalCode.InvalidMessageId);
        Refused(() => AprsStation.From("N0CALL").Ack("G3NRW", "").Build()).Should().Be(AprsRefusalCode.InvalidMessageId);
        Refused(() => AprsStation.From("N0CALL").Reject("G3NRW", "").Build()).Should().Be(AprsRefusalCode.InvalidMessageId);
        Refused(() => AprsStation.From("N0CALL").To("").Status("hi").Build()).Should().Be(AprsRefusalCode.InvalidAddress);
        System.Text.Encoding.Latin1.GetString(AprsStation.From("N0CALL").Position(51.5, -0.5).Symbol(AprsSymbol.Car).Comment("").Build().Information.Span)
            .Should().Be(System.Text.Encoding.Latin1.GetString(AprsStation.From("N0CALL").Position(51.5, -0.5).Symbol(AprsSymbol.Car).Build().Information.Span));
    }

    [Fact]
    public void An_ack_or_reject_with_no_id_in_data_is_an_invalid_message_id()
    {
        Refused(() => AprsPacket.Create(Me, new AprsMessageAck { Addressee = "G3NRW", AcknowledgedId = "" })).Should().Be(AprsRefusalCode.InvalidMessageId);
        Refused(() => AprsPacket.Create(Me, new AprsMessageReject { Addressee = "G3NRW", RejectedId = "" })).Should().Be(AprsRefusalCode.InvalidMessageId);
    }

    // ------------------------------------------------------------------ a Mic-E altitude carries no mark

    [Fact]
    public void No_data_a_caller_can_see_or_copy_carries_a_hidden_mark()
    {
        // Every property of every report type is public: the builder's choice of form is never held on the data.
        var hidden = typeof(AprsData).Assembly.GetTypes()
            .Where(t => typeof(AprsData).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly).Select(p => $"{t.Name}.{p.Name}"))
            .Where(n => !n.EndsWith(".EqualityContract", StringComparison.Ordinal))
            .ToList();
        hidden.Should().BeEmpty();
    }

    [Fact]
    public void A_built_mic_e_packet_holds_the_altitude_as_given_and_its_copy_is_written_by_the_value()
    {
        AprsMicEBuilder builder = AprsStation.From("N0CALL").MicE(51.5, -0.5).Symbol(AprsSymbol.Car).AltitudeFeet(1000);
        AprsPacket built = builder.Build();
        ((AprsMicEReport)built.Data).AltitudeFeet.Should().Be(1000, "as given, not rounded to the metre");
        System.Text.Encoding.Latin1.GetString(built.Information.Span).Should().Contain("}", "a builder writes the Mic-E altitude bytes first");
        System.Text.Encoding.Latin1.GetString(built.Information.Span).Should().NotContain("/A=");

        // The same data given to the encoder is neutral data: its value decides, and 1000 feet is no whole number of metres.
        AprsPacket copy = AprsPacket.Create(Me, built.Data);
        System.Text.Encoding.Latin1.GetString(copy.Information.Span).Should().Contain("/A=001000");

        ((AprsMicEReport)AprsStation.From("N0CALL").MicE(51.5, -0.5).Symbol(AprsSymbol.Car).AltitudeMetres(305.4).Build().Data).AltitudeFeet
            .Should().BeApproximately(305.4 * 3.280839895013123, 1e-9, "as given, in feet");
    }

    // ------------------------------------------------------------------ read back under the header written

    private static AprsUnrecognizedData MicEShapedBytes() => new() { Reason = AprsUnrecognizedReason.Malformed, Raw = "`(_fn\"Oj/"u8.ToArray() };

    [Fact]
    public void Bytes_that_read_as_a_mic_e_position_under_the_destination_written_are_not_encodable()
    {
        // Under APZ001 these bytes are undecoded data; under S32U6T they are a Mic-E position (33.43, -12.13).
        AprsUnrecognizedData bytes = MicEShapedBytes();
        System.Text.Encoding.Latin1.GetString(AprsPacket.Create(Me, bytes, AprsAddress.Parse("APZ001")).ToTnc2()).Should().Be("N0CALL>APZ001:`(_fn\"Oj/");
        Refused(() => AprsPacket.Create(Me, bytes, AprsAddress.Parse("S32U6T"))).Should().Be(AprsRefusalCode.NotEncodable);
        Refused(() => AprsStation.From("N0CALL").To("S32U6T").Data(bytes).Build()).Should().Be(AprsRefusalCode.NotEncodable);
        Refused(() => AprsThirdPartyTraffic.Create(Me, bytes, AprsAddress.Parse("S32U6T"))).Should().Be(AprsRefusalCode.NotEncodable);
        FluentActions.Invoking(() => AprsThirdPartyTraffic.Create(Me, bytes, AprsAddress.Parse("APZ001"))).Should().NotThrow();
        bytes.ToInformationField().Should().NotBeEmpty("an information field written alone is judged under N0CALL>APZ001");
    }

    [Fact]
    public void A_mic_e_report_is_written_under_the_destination_it_computes_and_no_other()
    {
        var mic = new AprsMicEReport { Position = new AprsPosition(33.42733333333334, -12.129), Symbol = new AprsSymbol('/', 'j'), MicEMessage = AprsMicEMessage.Returning, CourseDegrees = 251, SpeedKnots = 20 };
        AprsAddress computed = AprsPacket.Create(Me, mic).Destination;
        computed.Value.Should().Be("S32U6T");
        AprsThirdPartyTraffic.Create(Me, mic, computed).Packet.Destination.Should().Be(computed, "written under the destination it computes");
        Refused(() => AprsThirdPartyTraffic.Create(Me, mic, AprsAddress.Parse("APRS"))).Should().Be(AprsRefusalCode.NotEncodable);
        Refused(() => AprsPacket.Create(Me, mic, AprsAddress.Parse("APZ001"))).Should().Be(AprsRefusalCode.NotEncodable);
    }

    [Theory]
    [InlineData("S32U7T")]
    [InlineData("S32U6T-3")]
    [InlineData("332U6T")]
    [InlineData("S3256T")]
    [InlineData("S32UVT")]
    [InlineData("S32U64")]
    [InlineData("APZ001")]
    public void A_held_mic_e_report_read_back_under_another_destination_is_not_encodable(string other)
    {
        AprsPacket held = AprsPacket.Decode("N0CALL>S32U6T:`(_fn\"Oj/");
        held.Data.Should().BeOfType<AprsMicEReport>();
        ReadBack.Check(held.Data, held.Information.ToArray(), null, held.Destination).Should().BeNull("under its own destination it reads back as held");
        ReadBack.Check(held.Data, held.Information.ToArray(), null, AprsAddress.Parse(other))!.Code.Should().Be(AprsRefusalCode.NotEncodable);
        Refused(() => AprsPacket.Create(Me, held.Data, AprsAddress.Parse(other))).Should().Be(AprsRefusalCode.NotEncodable);
        Refused(() => AprsThirdPartyTraffic.Create(Me, held.Data, AprsAddress.Parse(other))).Should().Be(AprsRefusalCode.NotEncodable);
    }

    [Fact]
    public void A_builder_made_mic_e_packet_with_a_latitude_between_steps_is_written_under_its_computed_destination()
    {
        AprsPacket built = AprsStation.From("N0CALL").MicE(51.50123456, -0.50123456).Symbol(AprsSymbol.Car).Build();
        built.Destination.Value.Should().NotBe("APZ001");
        AprsPacket.Decode(built.ToTnc2()).Data.Should().BeOfType<AprsMicEReport>();
        ((AprsMicEReport)built.Data).Position.Latitude.Should().Be(51.50123456, "the data holds the value as given");
    }

    [Fact]
    public void A_destination_ssid_of_zero_is_none()
    {
        var mic = new AprsMicEReport { Position = new AprsPosition(33.42733333333334, -12.129), Symbol = new AprsSymbol('/', 'j'), MicEMessage = AprsMicEMessage.Returning, CourseDegrees = 251, SpeedKnots = 20 };
        AprsPacket.Create(Me, mic with { DestinationSsid = 0 }).Destination.Value.Should().Be("S32U6T");
        AprsPacket.Create(Me, mic with { DestinationSsid = 3 }).Destination.Value.Should().Be("S32U6T-3");
    }

    // ------------------------------------------------------------------ decoder diagnostics

    private static AprsDiagnosticCode[] Codes(AprsPacket p) => [.. p.Diagnostics.Where(d => d.Severity != AprsDiagnosticSeverity.Info).Select(d => d.Code)];

    [Fact]
    public void A_defect_of_one_field_or_entry_is_listed_once_for_each_and_a_judgement_of_a_whole_once()
    {
        Codes(AprsPacket.Decode("N0CALL>APZ001,,,WIDE1-1:>x")).Should().Equal(AprsDiagnosticCode.EmptyPathEntry, AprsDiagnosticCode.EmptyPathEntry);
        Codes(((AprsThirdPartyTraffic)AprsPacket.Decode("N0CALL>APZ001:}A>B,,,C:>x").Data).Packet).Should().Equal(AprsDiagnosticCode.EmptyPathEntry, AprsDiagnosticCode.EmptyPathEntry);
        Codes(AprsPacket.Decode("N0CALL>APZ001,A*,B*,C*:>x")).Should().Equal(AprsDiagnosticCode.MultipleUsedMarkers);
        Codes(AprsPacket.Decode("N0CALL>APZ001:!5130.00n/00030.00w>")).Should().Equal(AprsDiagnosticCode.LowercaseHemisphere, AprsDiagnosticCode.LowercaseHemisphere);
        Codes(AprsPacket.Decode("N0CALL>APZ001:>a\u0001b\u0002c")).Should().Equal(AprsDiagnosticCode.ControlCharacterInText);
    }

    [Fact]
    public void A_strict_decoder_lists_nothing_found_after_the_defect_it_rejects_by()
    {
        // A missing space after a Mic-E locator is met where the locator is read, before the DAO and its ambiguity check.
        AprsPacket packet = AprsPacket.Decode("KJ5IMV-2>RYTWLZ,WIDE1-1,WIDE2-1,qAR,KJ5IMV:'{P\u001c\u001c\u001c\u001cj/]IO91/G!w3U!}Pi/\"4G}Graywolf/181.14.13", AprsParseOptions.Strict);
        packet.Diagnostics.Select(d => $"{d.Severity}:{d.Code}").Should().Equal("Error:MissingSpaceAfterLocator");
    }

    // ------------------------------------------------------------------ regressions from the review

    [Theory]
    [InlineData("A,B")]
    [InlineData("PARM.A,B")]
    [InlineData("N0CALL   ")]
    [InlineData("A:B")]
    public void A_third_party_inner_destination_that_would_not_read_back_as_given_is_an_invalid_header(string destination)
    {
        // "A,B" would read back as a destination A and a path entry B.
        Refused(() => AprsThirdPartyTraffic.Create(AprsAddress.Parse("G4ABC"), new AprsStatusReport { Text = "x" }, AprsAddress.CreateUnchecked(destination)))
            .Should().Be(AprsRefusalCode.InvalidThirdPartyHeader);
    }

    [Fact]
    public void A_voice_frequency_offset_of_the_smallest_integer_is_refused_not_a_crash()
    {
        var data = new AprsPositionReport { Position = new AprsPosition(51.5, -0.5), Symbol = AprsSymbol.Car, Frequency = new AprsVoiceFrequency { FrequencyMHz = 145.5, OffsetKHz = int.MinValue } };
        Refused(() => AprsPacket.Create(Me, data)).Should().Be(AprsRefusalCode.ValueOutOfRange);
        Refused(() => AprsPacket.Create(Me, data with { Frequency = data.Frequency with { OffsetKHz = int.MaxValue } })).Should().Be(AprsRefusalCode.ValueOutOfRange);
    }

    [Theory]
    [InlineData("")]
    public void An_nmea_time_text_that_is_empty_is_absent(string time)
    {
        var report = new AprsNmeaReport { Sentence = "GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,", TimeText = time };
        System.Text.Encoding.Latin1.GetString(report.ToInformationField()).Should().Be("$GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("12345")]
    [InlineData("1235")]
    [InlineData("1235190")]
    [InlineData("abcdef")]
    [InlineData("123520")]
    public void An_nmea_time_text_that_does_not_match_the_sentence_is_refused_not_a_crash(string time)
    {
        var report = new AprsNmeaReport { Sentence = "GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,", TimeText = time };
        FluentActions.Invoking(report.ToInformationField).Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.InvalidNmea);
    }
}
