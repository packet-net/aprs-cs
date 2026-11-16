using System.Text.Json.Nodes;
using Packet.Aprs.Tests.Vectors;

namespace Packet.Aprs.Tests.Encoding;

/// <summary>
/// The differences a six-way comparison found, each with the referee's reproducer: the order of
/// refusals over a whole third-party record, nesting past four levels, a Mic-E comment no form can
/// write, and a number that is not a number in a field a report has no place for.
/// </summary>
public class TriageTests
{
    private static readonly AprsAddress Me = AprsAddress.Parse("N0CALL");

    private static AprsRefusalCode? Encode(string neutral)
    {
        try
        {
            AprsData data = NeutralReader.Data(JsonNode.Parse(neutral)!.AsObject());
            AprsPacket.Create(Me, data, data is AprsMicEReport ? null : AprsAddress.Parse("APZ001"));
            return null;
        }
        catch (AprsEncodeException ex)
        {
            return ex.Code;
        }
    }

    [Fact]
    public void An_inner_header_fault_comes_before_a_data_fault_one_level_further_down()
    {
        // The first inner header has no destination; the data two levels down has a control character.
        Encode("""{"type":"third-party","packet":{"source":"AB","data":{"type":"third-party","packet":{"source":"A","destination":"B","data":{"type":"telemetry","sequence":"005","analog":[199,0,255,73,123],"bits":"01101001","comment":" hi\u0001"}}}}}""")
            .Should().Be(AprsRefusalCode.InvalidThirdPartyHeader);

        // The first inner path has an unmarked entry before a used one; the data below holds a bad addressee-free text.
        Encode("""{"type":"third-party","packet":{"source":"A","destination":"B","data":{"type":"third-party","packet":{"source":"VK2XYZ","destination":"S32U6T","data":{"type":"telemetry-names","addressee":"PP5CFL-2","names":["","146.52 MHzLo","","@LoRa iGate",":","{5}","xxxxxxxxxxxx","","Radio Club"," R25m$QTH"]}}},"path":["TCPIP","G4ABC*"]}}""")
            .Should().Be(AprsRefusalCode.InvalidThirdPartyHeader);
    }

    [Fact]
    public void Data_nested_past_four_levels_is_refused_for_its_header_without_looking_below()
    {
        Encode("""{"type":"third-party","packet":{"source":"N1CALL","destination":"APZ001","path":["WIDE2-1","qAR","N2CALL"],"data":{"type":"third-party","packet":{"source":"A","destination":"B","data":{"type":"third-party","packet":{"source":"A","destination":"B","data":{"type":"third-party","packet":{"source":"A","destination":"B","data":{"type":"third-party","packet":{"source":"A","destination":"B","data":{"type":"unrecognized","reason":"malformed"}}}}}},"path":["WIDE1-1*","WIDE2-1"]}}}}}""")
            .Should().Be(AprsRefusalCode.InvalidThirdPartyHeader);
    }

    [Fact]
    public void A_mic_e_comment_no_form_can_write_is_text_reads_as_field()
    {
        // Straight on, > reads as a type code; after a slash, /> and 5} read as a Mic-E altitude; after a space, the space is a type code.
        Encode("""{"type":"mic-e","mic_e_message":"en-route","latitude":51.5,"longitude":-0.5,"symbol":"/>","speed_knots":10,"comment":">5}"}""")
            .Should().Be(AprsRefusalCode.TextReadsAsField);
    }

    [Fact]
    public void A_number_that_is_not_a_number_is_not_a_number_in_a_field_the_report_has_no_place_for()
    {
        var weather = new AprsWeather { WindDirectionDegrees = 220, WindSpeedMph = 4, WindGustMph = 5, TemperatureFahrenheit = 77, HumidityPercent = 50 };
        AprsPositionReport report = new() { Position = new AprsPosition(-62.69733333333333, 109.89566666666667), Symbol = new AprsSymbol('/', '_'), Weather = weather };
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            FluentActions.Invoking(() => AprsPacket.Create(Me, report with { AltitudeFeet = bad, Comment = "\n" })).Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.NotANumber);
            FluentActions.Invoking(() => AprsPacket.Create(Me, report with { Frequency = new AprsVoiceFrequency { FrequencyMHz = bad } })).Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.NotANumber);
            FluentActions.Invoking(() => AprsPacket.Create(Me, report with { Telemetry = new AprsCommentTelemetry { Sequence = 8281, Analog = [1] }, RangeMiles = bad })).Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.NotANumber);
        }
    }
}
