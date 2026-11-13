using System.Text.Json.Nodes;
using Packet.Aprs.Tests.Vectors;

namespace Packet.Aprs.Tests.Encoding;

/// <summary>A refusal carries the shared reason code for the rule it broke (1.0.1).</summary>
public class RefusalCodeTests
{
    private static readonly AprsStation Me = AprsStation.From("M0LTE");

    private static AprsPositionReport Report() =>
        new() { Position = new AprsPosition(51, 0), Symbol = AprsSymbol.Car };

    public static TheoryData<string, AprsRefusalCode> Refusals => new()
    {
        { "invalid-address", AprsRefusalCode.InvalidAddress },
        { "ax25-address", AprsRefusalCode.Ax25CannotCarry },
        { "ax25-path", AprsRefusalCode.Ax25CannotCarry },
        { "addressee", AprsRefusalCode.InvalidAddressee },
        { "message-id", AprsRefusalCode.InvalidMessageId },
        { "object-name", AprsRefusalCode.InvalidName },
        { "object-padding", AprsRefusalCode.PaddingSpace },
        { "message-too-long", AprsRefusalCode.TooLong },
        { "locator", AprsRefusalCode.Malformed },
        { "brace", AprsRefusalCode.InvalidCharacter },
        { "comment-reads-as-field", AprsRefusalCode.TextReadsAsField },
        { "latitude", AprsRefusalCode.ValueOutOfRange },
        { "mic-e-course-0", AprsRefusalCode.ValueHasNoForm },
        { "timestamp", AprsRefusalCode.InvalidTimestamp },
        { "object-no-timestamp", AprsRefusalCode.MissingField },
        { "symbol", AprsRefusalCode.InvalidSymbol },
        { "symbol-mismatch", AprsRefusalCode.SymbolMismatch },
        { "extension-conflict", AprsRefusalCode.ExtensionConflict },
        { "weather-comment", AprsRefusalCode.FieldNotAllowed },
        { "query-type", AprsRefusalCode.InvalidCode },
        { "nmea-checksum", AprsRefusalCode.InvalidNmea },
        { "mic-e-no-destination", AprsRefusalCode.NotEncodable },
        { "nan", AprsRefusalCode.NotANumber },
        { "humidity-zero", AprsRefusalCode.ValueHasNoForm },
        { "capability-token", AprsRefusalCode.Malformed },
        { "single-field-before-relation", AprsRefusalCode.InvalidCharacter },
        { "relation-order", AprsRefusalCode.FieldNotAllowed },
        { "ack-id-order", AprsRefusalCode.InvalidMessageId },
        { "telemetry-bits-order", AprsRefusalCode.InvalidCode },
        { "empty-sequence", AprsRefusalCode.Malformed },
        { "mic-e-short-telemetry", AprsRefusalCode.Malformed },
        { "third-party-unrecognized", AprsRefusalCode.NotEncodable },
        { "mic-e-unknown-before-suffix", AprsRefusalCode.InvalidCode },
        { "mic-e-pole-before-dao", AprsRefusalCode.ValueHasNoForm },
        { "message-bln-station", AprsRefusalCode.TextReadsAsField },
        { "mic-e-value-before-unknown", AprsRefusalCode.ValueHasNoForm },
        { "mic-e-altitude-no-form", AprsRefusalCode.ValueHasNoForm },
        { "area-symbol-course", AprsRefusalCode.SymbolMismatch },
        { "order-across-fields", AprsRefusalCode.InvalidCharacter },
        { "order-across-fields-2", AprsRefusalCode.ValueOutOfRange },
        { "third-party-header-first", AprsRefusalCode.InvalidThirdPartyHeader },
        { "third-party-unmarked-entry", AprsRefusalCode.InvalidThirdPartyHeader },
        { "given-twice", AprsRefusalCode.ValueGivenTwice },
        { "given-twice-after-a-field", AprsRefusalCode.ValueOutOfRange },
        { "builder-bits-after-text", AprsRefusalCode.InvalidCharacter },
    };

    private static AprsEncodeException Refusal(string what)
    {
        Action act = what switch
        {
            "invalid-address" => () => AprsStation.From("not a callsign!"),
            "ax25-address" => () => AprsPacket.Create("M0LTE", new AprsStatusReport { Text = "x" }, "APZ001", "wide1-1").ToAx25Frame(),
            "ax25-path" => () => AprsPacket.Create("M0LTE", new AprsStatusReport { Text = "x" }, "APZ001", "A,B,C,D,E,F,G,H,I").ToAx25Frame(),
            "addressee" => () => new AprsTextMessage { Addressee = "TOOLONGADDRESSEE", Text = "hi" }.ToInformationField(),
            "message-id" => () => new AprsTextMessage { Addressee = "G0ABC", Text = "hi", MessageId = "TOOLONG" }.ToInformationField(),
            "object-name" => () => new AprsObjectReport { Name = "", Timestamp = AprsTimestamp.DayHoursMinutes(1, 2, 3), Position = new AprsPosition(51, 0), Symbol = AprsSymbol.Car }.ToInformationField(),
            "object-padding" => () => new AprsObjectReport { Name = "ab ", Timestamp = AprsTimestamp.DayHoursMinutes(1, 2, 3), Position = new AprsPosition(51, 0), Symbol = AprsSymbol.Car }.ToInformationField(),
            "message-too-long" => () => new AprsTextMessage { Addressee = "G0ABC", Text = new string('x', 68) }.ToInformationField(),
            "locator" => () => new AprsStatusReport { Text = "x", Locator = "ZZ99", Symbol = AprsSymbol.Car }.ToInformationField(),
            "brace" => () => new AprsTextMessage { Addressee = "G0ABC", Text = "a{b" }.ToInformationField(),
            "comment-reads-as-field" => () => (Report() with { Comment = "/A=000100" }).ToInformationField(),
            "latitude" => () => (Report() with { Position = new AprsPosition(95, 0) }).ToInformationField(),
            "mic-e-course-0" => () => AprsPacket.Create("M0LTE", new AprsMicEReport { Position = new AprsPosition(51, 0), Symbol = AprsSymbol.Car, CourseDegrees = 0, SpeedKnots = 1 }),
            "timestamp" => () => new AprsStatusReport { Text = "x", Timestamp = AprsTimestamp.MonthDayHoursMinutes(1, 2, 3, 4) }.ToInformationField(),
            "object-no-timestamp" => () => new AprsObjectReport { Name = "abc", Position = new AprsPosition(51, 0), Symbol = AprsSymbol.Car }.ToInformationField(),
            "symbol" => () => (Report() with { Symbol = new AprsSymbol('x', '!') }).ToInformationField(),
            "symbol-mismatch" => () => (Report() with { Weather = new AprsWeather { TemperatureFahrenheit = 50 } }).ToInformationField(),
            "extension-conflict" => () => (Report() with { CourseDegrees = 10, SpeedKnots = 5, Phg = new AprsPhg(1, 2, 3, 4) }).ToInformationField(),
            "weather-comment" => () => (Report() with { Symbol = AprsSymbol.WeatherStation, Weather = new AprsWeather { TemperatureFahrenheit = 50 }, Comment = "hello" }).ToInformationField(),
            "query-type" => () => new AprsGeneralQuery { QueryType = "lower" }.ToInformationField(),
            "nmea-checksum" => () => new AprsNmeaReport { Sentence = "$GPGGA,1*00" }.ToInformationField(),
            "mic-e-no-destination" => () => new AprsMicEReport { Position = new AprsPosition(51, 0), Symbol = AprsSymbol.Car }.ToInformationField(),
            "nan" => () => (Report() with { Position = new AprsPosition(double.NaN, 0) }).ToInformationField(),
            "humidity-zero" => () => (Report() with { Symbol = AprsSymbol.WeatherStation, Weather = new AprsWeather { HumidityPercent = 0.4 } }).ToInformationField(),
            "capability-token" => () => new AprsStationCapabilities { Capabilities = [new AprsCapability("A B", null)] }.ToInformationField(),
            "single-field-before-relation" => () => (Report() with { Symbol = AprsSymbol.WeatherStation, Comment = "a\nb" }).ToInformationField(),
            "relation-order" => () => (Report() with { Symbol = AprsSymbol.WeatherStation, Position = new AprsPosition(51, 0, 1), Dao = new AprsDao('W', AprsDaoPrecision.Base91) }).ToInformationField(),
            "ack-id-order" => () => new AprsMessageAck { Addressee = "G0ABC", MessageId = "1", AcknowledgedId = "TOOLONGID" }.ToInformationField(),
            "telemetry-bits-order" => () => (Report() with { Telemetry = new AprsCommentTelemetry { Sequence = 1, Analog = [1, 2], Digital = 1 }, Dao = new AprsDao('?', AprsDaoPrecision.Base91) }).ToInformationField(),
            "empty-sequence" => () => new AprsTelemetryReport { Sequence = "", Analog = [1, 2, 3, 4, 5], Bits = "00000000" }.ToInformationField(),
            "mic-e-short-telemetry" => () => AprsPacket.Create("M0LTE", new AprsMicEReport { Position = new AprsPosition(51, 0), Symbol = AprsSymbol.Car, LegacyTelemetry = [1, 2, 3] }),
            "third-party-unrecognized" => () => NeutralReader.Data(JsonNode.Parse("""{"type":"third-party","packet":{"source":"N0CALL","destination":"APRS","path":[],"data":{"type":"unrecognized","reason":"not-aprs"}}}""")!.AsObject()),
            "mic-e-unknown-before-suffix" => () => AprsPacket.Create("M0LTE", new AprsMicEReport { Position = new AprsPosition(51, 0), Symbol = AprsSymbol.Car, MicEMessage = (AprsMicEMessage)99, DeviceSuffix = "_1" }),
            "mic-e-pole-before-dao" => () => AprsPacket.Create("M0LTE", new AprsMicEReport { Position = new AprsPosition(90, 0, 1), Symbol = AprsSymbol.Car, Dao = new AprsDao('W', AprsDaoPrecision.Base91), Comment = "a\nb" }),
            "message-bln-station" => () => new AprsTextMessage { Addressee = "BLNe", Text = "?WX" }.ToInformationField(),
            "mic-e-value-before-unknown" => () => AprsPacket.Create("M0LTE", new AprsMicEReport { Position = new AprsPosition(51, -180), Symbol = AprsSymbol.Car, MicEMessage = (AprsMicEMessage)99 }),
            "mic-e-altitude-no-form" => () => AprsPacket.Create("M0LTE", new AprsMicEReport { Position = new AprsPosition(47.20366666666666, -122.50216666666667), Symbol = new AprsSymbol('W', 'v'), AltitudeFeet = 1597896.9816272964, CourseDegrees = 26, SpeedKnots = 0 }),
            "area-symbol-course" => () => (Report() with { Symbol = new AprsSymbol('\\', 'l'), CourseDegrees = 1, SpeedKnots = 1 }).ToInformationField(),
            "order-across-fields" => () => new AprsObjectReport { Name = "OR4F ", Timestamp = AprsTimestamp.DayHoursMinutes(1, 2, 3), Position = new AprsPosition(51, 0), Symbol = AprsSymbol.Car, Comment = "a\rb" }.ToInformationField(),
            "order-across-fields-2" => () => (Report() with { CourseDegrees = 10, SpeedKnots = 5, Phg = new AprsPhg(10, 1, 1, 1) }).ToInformationField(),
            "third-party-header-first" => () => AprsThirdPartyTraffic.Create(AprsAddress.Parse("N0CALL"), Report() with { Position = new AprsPosition(95, 0) }, AprsAddress.Parse("APZ001"), [new AprsPathEntry(AprsAddress.Parse("TCPIP")), new AprsPathEntry(AprsAddress.Parse("G4ABC"), true)]),
            "third-party-unmarked-entry" => () => AprsThirdPartyTraffic.Create(AprsAddress.Parse("N0CALL"), Report(), AprsAddress.Parse("APZ001"), [new AprsPathEntry(AprsAddress.Parse("TCPIP")), new AprsPathEntry(AprsAddress.Parse("G4ABC"), true)]),
            "given-twice" => () => Me.Position(51, 0).Symbol(AprsSymbol.Car).AltitudeFeet(100).AltitudeMetres(30.48).Build(),
            "given-twice-after-a-field" => () => Me.Position(51, 0).Symbol(AprsSymbol.Car).SpeedKnots(10).SpeedKmh(5000).Build(),
            "builder-bits-after-text" => () => Me.Telemetry(1).Analog(1, 2, 3, 4, 5).Bits("0000000x").Comment("a\rb").Build(),
            _ => throw new ArgumentException(what),
        };
        return FluentActions.Invoking(act).Should().Throw<AprsEncodeException>().Which;
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void A_refusal_carries_the_code_of_its_rule(string what, AprsRefusalCode expected) =>
        Refusal(what).Code.Should().Be(expected);

    [Fact]
    public void Every_code_is_in_the_vectors_list_under_its_kebab_case_name()
    {
        string[] listed = [.. JsonNode.Parse(File.ReadAllText(Path.Combine(VectorCases.Directory, "codes.json")))!["refusals"]!.AsArray().Select(n => (string)n!["id"]!)];
        string[] ours = [.. Enum.GetValues<AprsRefusalCode>().Select(c => NeutralJson.Kebab(c.ToString()))];

        ours.Should().Equal(listed);
        ((int)AprsRefusalCode.DoesNotReadBack).Should().Be(listed.Length);
    }
}
