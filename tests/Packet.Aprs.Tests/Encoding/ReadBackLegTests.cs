using Packet.Aprs.Internal;

namespace Packet.Aprs.Tests.Encoding;

/// <summary>
/// One test for each leg of the read-back's comparison (vectors README, Rules, "Read-back"), each
/// shown to fail when its leg is removed: a vectors case can never pin these, because a rule check
/// on the data always comes before the read-back (README, Refusal reasons), so the bytes a writer
/// makes are made wrong here on purpose, between writing and the read-back
/// (<see cref="InfoEncoder.AfterWriting"/>), or the diagnostics the decode gave are changed
/// (<see cref="ReadBack.AfterDecoding"/>).
/// </summary>
public class ReadBackLegTests
{
    private static readonly AprsAddress Me = AprsAddress.Parse("N0CALL");
    private static readonly AprsPosition Here = new(51.5, -0.5);

    private static AprsPositionReport Position() => new() { Position = Here, Symbol = AprsSymbol.Car };

    /// <summary>The refusal of writing <paramref name="data"/> with its bytes changed by <paramref name="tamper"/>, or null when it was written.</summary>
    private static AprsRefusalCode? WithBytes(AprsData data, Func<byte[], byte[]> tamper)
    {
        InfoEncoder.AfterWriting = tamper;
        try
        {
            AprsPacket.Create(Me, data);
            return null;
        }
        catch (AprsEncodeException ex)
        {
            return ex.Code;
        }
        finally
        {
            InfoEncoder.AfterWriting = null;
        }
    }

    /// <summary>The refusal of writing <paramref name="data"/> when its read-back decode also gives <paramref name="extra"/>, or null when it was written.</summary>
    private static AprsRefusalCode? WithDiagnostic(AprsData data, AprsDiagnosticSeverity severity, AprsDiagnosticCode code)
    {
        ReadBack.AfterDecoding = diagnostics => [.. diagnostics, new AprsDiagnostic(code, severity, "added by a test")];
        try
        {
            AprsPacket.Create(Me, data);
            return null;
        }
        catch (AprsEncodeException ex)
        {
            return ex.Code;
        }
        finally
        {
            ReadBack.AfterDecoding = null;
        }
    }

    // ------------------------------------------------------------------ diagnostics

    [Fact]
    public void A_read_back_that_sees_only_a_warning_refuses()
    {
        // A lower-case hemisphere reads back as the same position, with one warning and nothing else different.
        byte[] lower = [.. Position().ToInformationField()];
        lower[Array.IndexOf(lower, (byte)'N')] = (byte)'n';
        AprsPacket decoded = AprsPacket.Decode(Me, AprsAddress.Parse("APZ001"), [], lower);
        decoded.Diagnostics.Select(d => (d.Severity, d.Code)).Should().Equal((AprsDiagnosticSeverity.Warning, AprsDiagnosticCode.LowercaseHemisphere));
        AprsPacket.Decode(Me, AprsAddress.Parse("APZ001"), [], Position().ToInformationField()).Data.Should().Be(decoded.Data, "the data is the same: only the warning differs");

        WithBytes(Position(), _ => lower).Should().Be(AprsRefusalCode.DoesNotReadBack);
    }

    [Fact]
    public void A_read_back_that_sees_only_an_error_refuses()
    {
        WithDiagnostic(Position(), AprsDiagnosticSeverity.Error, AprsDiagnosticCode.InvalidHeader).Should().Be(AprsRefusalCode.DoesNotReadBack);
    }

    [Theory]
    [InlineData(AprsDiagnosticCode.TrailingLineBreak)]
    [InlineData(AprsDiagnosticCode.DaoWithAmbiguity)]
    [InlineData(AprsDiagnosticCode.ControlCharacterInText)]
    public void An_info_outside_the_listed_four_refuses(AprsDiagnosticCode code)
    {
        WithDiagnostic(Position(), AprsDiagnosticSeverity.Info, code).Should().Be(AprsRefusalCode.DoesNotReadBack);
        WithDiagnostic(new AprsMaidenheadBeacon { Locator = "IO91SX" }, AprsDiagnosticSeverity.Info, code).Should().Be(AprsRefusalCode.DoesNotReadBack, "not even on a form that is allowed another info");
    }

    [Fact]
    public void An_info_is_accepted_on_the_forms_the_rules_attach_it_to()
    {
        var mic = new AprsMicEReport { Position = Here, Symbol = AprsSymbol.Car, CourseDegrees = 10, SpeedKnots = 5 };
        var legacy = mic with { LegacyTelemetry = [1, 2, 3, 4, 5] };
        var weather = new AprsWeatherReport { Timestamp = AprsTimestamp.MonthDayHoursMinutes(10, 9, 23, 45), Weather = new AprsWeather { TemperatureFahrenheit = 60 } };
        var message = new AprsTextMessage { Addressee = "G3NRW", Text = "hi" };

        WithDiagnostic(weather, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.ObsoleteFormat).Should().BeNull();
        WithDiagnostic(new AprsRawWeatherReport { Format = AprsRawWeatherFormat.PeetBrosHash, Data = "abc" }, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.ObsoleteFormat).Should().BeNull();
        WithDiagnostic(new AprsNmeaReport { Sentence = "GPGLL,4916.45,N,12311.12,W,225444,A" }, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.ObsoleteFormat).Should().BeNull();
        WithDiagnostic(new AprsMaidenheadBeacon { Locator = "IO91SX" }, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.ObsoleteFormat).Should().BeNull();
        WithDiagnostic(legacy, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.ObsoleteFormat).Should().BeNull();
        WithDiagnostic(mic, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.MicEMissingDeviceType).Should().BeNull();
        WithDiagnostic(message, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.InvalidQuery).Should().BeNull();
        WithDiagnostic(message, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.InvalidTelemetryMetadata).Should().BeNull();
    }

    [Fact]
    public void An_info_listed_for_one_form_is_not_accepted_on_another()
    {
        var mic = new AprsMicEReport { Position = Here, Symbol = AprsSymbol.Car, CourseDegrees = 10, SpeedKnots = 5 };
        var status = new AprsStatusReport { Text = "hi" };
        var message = new AprsTextMessage { Addressee = "G3NRW", Text = "hi" };

        // obsolete-format belongs to positionless weather, raw weather, raw NMEA, a Maidenhead beacon and Mic-E Rev 0 telemetry.
        WithDiagnostic(status, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.ObsoleteFormat).Should().Be(AprsRefusalCode.DoesNotReadBack);
        WithDiagnostic(Position(), AprsDiagnosticSeverity.Info, AprsDiagnosticCode.ObsoleteFormat).Should().Be(AprsRefusalCode.DoesNotReadBack);
        WithDiagnostic(message, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.ObsoleteFormat).Should().Be(AprsRefusalCode.DoesNotReadBack);
        WithDiagnostic(mic, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.ObsoleteFormat).Should().Be(AprsRefusalCode.DoesNotReadBack, "a Mic-E report with no Rev 0 telemetry has no obsolete format");

        // mic-e-missing-device-type belongs to Mic-E status text.
        WithDiagnostic(status, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.MicEMissingDeviceType).Should().Be(AprsRefusalCode.DoesNotReadBack);
        WithDiagnostic(message, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.MicEMissingDeviceType).Should().Be(AprsRefusalCode.DoesNotReadBack);

        // invalid-query and invalid-telemetry-metadata belong to a message read as a plain message.
        WithDiagnostic(status, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.InvalidQuery).Should().Be(AprsRefusalCode.DoesNotReadBack);
        WithDiagnostic(mic, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.InvalidTelemetryMetadata).Should().Be(AprsRefusalCode.DoesNotReadBack);
        WithDiagnostic(new AprsBulletin { Addressee = "BLN1", Text = "hi" }, AprsDiagnosticSeverity.Info, AprsDiagnosticCode.InvalidQuery).Should().Be(AprsRefusalCode.DoesNotReadBack);
    }

    // ------------------------------------------------------------------ fields

    [Fact]
    public void A_decode_with_a_field_the_data_does_not_give_refuses()
    {
        // The bytes read back with a PHG the data never gave; every field the data does give is the same.
        WithBytes(Position(), info => [.. info, .. "PHG5132"u8.ToArray()]).Should().Be(AprsRefusalCode.DoesNotReadBack);
    }

    [Fact]
    public void A_field_the_data_gives_that_the_decode_lacks_refuses()
    {
        var withPhg = Position() with { Phg = new AprsPhg(5, 1, 3, 2) };
        WithBytes(withPhg, info => info[..^7]).Should().Be(AprsRefusalCode.DoesNotReadBack);
    }

    [Fact]
    public void An_integer_that_reads_back_differently_refuses_even_when_it_is_close()
    {
        // 6 is not 5: an integer or a code is compared exactly, never within a tolerance.
        var withPhg = Position() with { Phg = new AprsPhg(5, 1, 3, 2) };
        WithBytes(withPhg, info => [.. info[..^7], .. "PHG6132"u8.ToArray()]).Should().Be(AprsRefusalCode.DoesNotReadBack);
    }

    // ------------------------------------------------------------------ a third-party packet's inner packet

    private static AprsThirdPartyTraffic Wrapping(AprsPacket inner) => new() { Packet = inner };

    [Fact]
    public void An_inner_error_the_bytes_do_not_read_back_with_has_no_form()
    {
        // The review's reproducer: decoded strictly, a control character in a comment is an error and
        // the data is undecoded; its bytes, written inside a third-party packet, decode leniently as
        // a position with a warning instead.
        AprsPacket inner = AprsPacket.Decode("SRC>APRS:!5130.00N/00030.00W>x\u0001y", AprsParseOptions.Strict);
        inner.HasErrors.Should().BeTrue();
        FluentActions.Invoking(() => AprsPacket.Create(Me, Wrapping(inner))).Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.ValueHasNoForm);
        FluentActions.Invoking(Wrapping(inner).ToInformationField).Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.ValueHasNoForm);
    }

    [Fact]
    public void An_inner_error_the_bytes_do_not_read_back_with_has_no_form_at_every_depth()
    {
        // The packet is made inside another third-party packet: the same check applies at the second level.
        AprsPacket inner = AprsPacket.Decode("SRC>APRS:!5130.00N/00030.00W>x\u0001y", AprsParseOptions.Strict);
        FluentActions.Invoking(() => AprsThirdPartyTraffic.Create(AprsAddress.Parse("G4ABC"), Wrapping(inner)))
            .Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.ValueHasNoForm);
        AprsThirdPartyTraffic outer = Wrapping(AprsPacket.Create(AprsAddress.Parse("G4ABC"), Wrapping(AprsPacket.Create(AprsAddress.Parse("SRC"), new AprsStatusReport { Text = "x" }))));
        outer.Packet.Data.Should().BeOfType<AprsThirdPartyTraffic>();
    }

    [Fact]
    public void A_warning_an_inner_packet_carries_is_written_as_received_and_reads_back_with_it()
    {
        AprsPacket inner = AprsPacket.Decode("SRC>APRS:!5130.00n/00030.00W>x");
        inner.Diagnostics.Select(d => d.Code).Should().Equal(AprsDiagnosticCode.LowercaseHemisphere);
        AprsPacket outer = AprsPacket.Create(Me, Wrapping(inner));
        System.Text.Encoding.Latin1.GetString(outer.Information.Span).Should().Be("}SRC>APRS:!5130.00n/00030.00W>x");
    }

    [Fact]
    public void A_warning_the_inner_bytes_read_back_with_that_the_data_does_not_give_refuses()
    {
        // An inner packet built by an encoder has no diagnostic; bytes that read back with one differ in nothing else.
        AprsThirdPartyTraffic clean = AprsThirdPartyTraffic.Create(AprsAddress.Parse("G4ABC"), Position());
        WithBytes(clean, info =>
        {
            byte[] lower = [.. info];
            lower[Array.IndexOf(lower, (byte)'N')] = (byte)'n';
            return lower;
        }).Should().Be(AprsRefusalCode.DoesNotReadBack);
    }

    // ------------------------------------------------------------------ the header

    private static readonly AprsPathEntry[] Via = [new(AprsAddress.Parse("WIDE1-1"), true), new(AprsAddress.Parse("WIDE2-1"), false)];

    private static byte[] Line(string header) => System.Text.Encoding.ASCII.GetBytes(header + ":>x");

    private static AprsRefusalCode? Tnc2Check(string line, AprsAddress source, AprsAddress destination, params AprsPathEntry[] path)
    {
        try
        {
            ReadBack.CheckTnc2Header(source, destination, path, Line(line));
            return null;
        }
        catch (AprsEncodeException ex)
        {
            return ex.Code;
        }
    }

    [Fact]
    public void A_line_whose_header_reads_back_as_given_is_carried()
    {
        Tnc2Check("N0CALL>APZ001,WIDE1-1*,WIDE2-1", Me, AprsAddress.Parse("APZ001"), Via).Should().BeNull();
    }

    [Theory]
    [InlineData("N0CAL>APZ001,WIDE1-1*,WIDE2-1", "a different source")]
    [InlineData("N0CALL>APZ002,WIDE1-1*,WIDE2-1", "a different destination")]
    [InlineData("N0CALL>APZ001,WIDE1-1*", "a path entry short")]
    [InlineData("N0CALL>APZ001,WIDE1-1*,WIDE2-1,WIDE3-1", "a path entry over")]
    [InlineData("N0CALL>APZ001,WIDE1-2*,WIDE2-1", "a different path address")]
    [InlineData("N0CALL>APZ001,WIDE1-1,WIDE2-1", "an entry that is not used")]
    [InlineData("N0CALL>APZ001,WIDE1-1*,WIDE2-1*", "an entry that is used, two stars")]
    [InlineData("N0CALL>APZ001,WIDE1-1,WIDE2-1*", "an entry used that was not")]
    public void A_line_whose_header_reads_back_differently_is_refused(string line, string because)
    {
        Tnc2Check(line, Me, AprsAddress.Parse("APZ001"), Via).Should().Be(AprsRefusalCode.Tnc2CannotCarry, because);
    }

    [Fact]
    public void A_line_whose_header_reads_back_with_a_diagnostic_is_refused()
    {
        // The same source, destination and path, entry by entry, but two stars: the decode warns.
        AprsPathEntry[] bothUsed = [new(AprsAddress.Parse("WIDE1-1"), true), new(AprsAddress.Parse("WIDE2-1"), true)];
        AprsPacket.Decode("N0CALL>APZ001,WIDE1-1*,WIDE2-1*:>x").Path.Should().Equal(bothUsed);
        AprsPacket.Decode("N0CALL>APZ001,WIDE1-1*,WIDE2-1*:>x").Diagnostics.Select(d => d.Code).Should().Contain(AprsDiagnosticCode.MultipleUsedMarkers);
        Tnc2Check("N0CALL>APZ001,WIDE1-1*,WIDE2-1*", Me, AprsAddress.Parse("APZ001"), bothUsed).Should().Be(AprsRefusalCode.Tnc2CannotCarry);
        Tnc2Check("N0CALL>APZ001,WIDE1-1,WIDE2-1*", Me, AprsAddress.Parse("APZ001"), bothUsed).Should().BeNull();
    }

    [Fact]
    public void A_line_header_is_read_back_by_the_outer_rules_whatever_the_packets_origin()
    {
        // An inner third-party source of 'a b' is valid inside the inner header, and no outer address.
        Tnc2Check("a b>APZ001", AprsAddress.CreateUnchecked("a b"), AprsAddress.Parse("APZ001")).Should().Be(AprsRefusalCode.Tnc2CannotCarry);
    }

    private static AprsRefusalCode? Ax25Check(byte[] frame, AprsAddress source, AprsAddress destination, params AprsPathEntry[] path)
    {
        try
        {
            ReadBack.CheckAx25Header(source, destination, path, frame);
            return null;
        }
        catch (AprsEncodeException ex)
        {
            return ex.Code;
        }
    }

    [Fact]
    public void A_frame_whose_addresses_read_back_as_given_is_carried()
    {
        byte[] frame = Ax25Codec.Encode(Me, AprsAddress.Parse("APZ001"), Via, ">x"u8);
        Ax25Check(frame, Me, AprsAddress.Parse("APZ001"), Via).Should().BeNull();
    }

    [Fact]
    public void A_frame_whose_addresses_read_back_differently_is_refused()
    {
        byte[] frame = Ax25Codec.Encode(Me, AprsAddress.Parse("APZ001"), Via, ">x"u8);
        AprsAddress zz = AprsAddress.Parse("APZ002");
        AprsPathEntry[] used = [new(AprsAddress.Parse("WIDE1-1"), true), new(AprsAddress.Parse("WIDE2-1"), true)];
        AprsPathEntry[] other = [new(AprsAddress.Parse("WIDE1-2"), true), new(AprsAddress.Parse("WIDE2-1"), false)];
        Ax25Check(frame, AprsAddress.Parse("N0CAL"), AprsAddress.Parse("APZ001"), Via).Should().Be(AprsRefusalCode.Ax25CannotCarry, "a different source");
        Ax25Check(frame, Me, zz, Via).Should().Be(AprsRefusalCode.Ax25CannotCarry, "a different destination");
        Ax25Check(frame, Me, AprsAddress.Parse("APZ001"), Via[0]).Should().Be(AprsRefusalCode.Ax25CannotCarry, "a path entry short");
        Ax25Check(frame, Me, AprsAddress.Parse("APZ001"), used).Should().Be(AprsRefusalCode.Ax25CannotCarry, "an entry used that was not");
        Ax25Check(frame, Me, AprsAddress.Parse("APZ001"), other).Should().Be(AprsRefusalCode.Ax25CannotCarry, "a different path address");
        Ax25Check([.. frame[..^3]], Me, AprsAddress.Parse("APZ001"), Via).Should().Be(AprsRefusalCode.Ax25CannotCarry, "a frame with no control byte and PID");
    }

    [Fact]
    public void A_frame_whose_addresses_read_back_with_a_diagnostic_is_refused()
    {
        // The call is padded with NUL where a frame pads with spaces: the same addresses, a warning.
        byte[] frame = Ax25Codec.Encode(AprsAddress.Parse("N0CA"), AprsAddress.Parse("APZ001"), [], ">x"u8);
        frame[7 + 4] = 0;
        frame[7 + 5] = 0;
        var ctx = new DecodeContext(AprsParseOptions.Lenient);
        Ax25Codec.TryDecode(frame, ctx, out Tnc2Codec.Header header, out _).Should().BeTrue();
        header.Source.Value.Should().Be("N0CA");
        ctx.Diagnostics.Select(d => d.Code).Should().Contain(AprsDiagnosticCode.NulPaddedAddress);
        Ax25Check(frame, AprsAddress.Parse("N0CA"), AprsAddress.Parse("APZ001")).Should().Be(AprsRefusalCode.Ax25CannotCarry);
    }
}
