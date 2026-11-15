namespace Packet.Aprs.Tests;

/// <summary>
/// Round 5: the locator's space is judged on the text once every lifted element is out, a later
/// Mic-E altitude and the base-91 telemetry it leaves between two bars included. Beyond the shared
/// vector cases.
/// </summary>
public class MicELocatorSpaceTests
{
    private const string Head = "N0CALL>S32UVT:`(_fn\"Oj/`IO91/G";

    private static bool MissingSpace(AprsPacket p) => p.Diagnostics.Any(d => d.Code == AprsDiagnosticCode.MissingSpaceAfterLocator);

    [Theory]
    [InlineData("|!:&0hl!Y[/>\"4e}='p| DG2MM", "|!:&0hl!Y[/>='p| DG2MM")]
    [InlineData("|!:&0hl!Y[/>\"4e}='p| Hello world", "|!:&0hl!Y[/>='p| Hello world")]
    [InlineData("|!:&0hl!Y[/>\"4e}='p| 73", "|!:&0hl!Y[/>='p| 73")]
    public void Telemetry_split_by_a_lifted_altitude_is_text_so_the_locators_space_is_missing(string text, string comment)
    {
        AprsPacket p = AprsPacket.Decode(Head + text);
        MissingSpace(p).Should().BeTrue();
        var report = p.Data.Should().BeOfType<AprsMicEReport>().Subject;
        report.Locator.Should().Be("IO91");
        report.Telemetry.Should().BeNull();
        report.AltitudeFeet.Should().NotBeNull();
        report.Comment.Should().Be(comment);
    }

    [Fact]
    public void Strictly_the_altitude_is_text_and_the_missing_space_rejects()
    {
        AprsPacket p = AprsPacket.Decode(Head + "|!:&0hl!Y[/>\"4e}='p| DG2MM", AprsParseOptions.Strict);
        p.Data.Should().BeOfType<AprsUnrecognizedData>();
        p.Diagnostics.Should().Contain(d => d.Code == AprsDiagnosticCode.MissingSpaceAfterLocator && d.Severity == AprsDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("\"4e}DG2MM")]
    [InlineData("|!:&0hl!Y[/>\"4e}='p|DG2MM")]
    public void Without_a_space_once_everything_is_lifted_the_warning_stays(string text)
    {
        MissingSpace(AprsPacket.Decode(Head + text)).Should().BeTrue();
    }

    [Theory]
    [InlineData("!W1\"4e}2! hi", "!W12! hi")]
    [InlineData("!w9\"4e}1!", "!w91!")]
    public void A_dao_only_joined_across_a_lifted_altitude_is_text_and_the_space_is_missing(string text, string comment)
    {
        AprsPacket p = AprsPacket.Decode(Head + text);
        MissingSpace(p).Should().BeTrue();
        var report = p.Data.Should().BeOfType<AprsMicEReport>().Subject;
        report.AltitudeFeet.Should().NotBeNull();
        report.Comment.Should().Be(comment);
        AprsPacket.Decode(Head + text, AprsParseOptions.Strict).Diagnostics.Should().Contain(d => d.Code == AprsDiagnosticCode.MissingSpaceAfterLocator && d.Severity == AprsDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("\"4e}  x", "x")]
    [InlineData("|!:&0hl!Y[/>\"4e}='p|  x", "|!:&0hl!Y[/>='p|  x")]
    [InlineData("  x", "x")]
    public void Two_spaces_after_the_locator_leave_the_comment_without_either(string text, string comment)
    {
        AprsPacket p = AprsPacket.Decode(Head + text);
        p.Data.Should().BeOfType<AprsMicEReport>().Subject.Comment.Should().Be(comment);
    }
}
