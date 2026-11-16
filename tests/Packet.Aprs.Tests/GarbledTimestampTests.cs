namespace Packet.Aprs.Tests;

/// <summary>
/// Round 5: a timestamp that is not valid, whose seven bytes end in z, / or h and are followed by
/// an uncompressed position, is garbled, even when the bytes after the DTI would read as a
/// compressed position. Beyond the shared vector cases.
/// </summary>
public class GarbledTimestampTests
{
    private static AprsPacket Lenient(string info) => AprsPacket.Decode("N0CALL>APZ001:" + info);

    private static void Strict(string info)
    {
        AprsPacket p = AprsPacket.Decode("N0CALL>APZ001:" + info, AprsParseOptions.Strict);
        p.Data.Should().BeOfType<AprsUnrecognizedData>().Which.Reason.Should().Be(AprsUnrecognizedReason.Malformed);
        p.Diagnostics.Should().Contain(d => d.Code == AprsDiagnosticCode.MalformedTimestamp);
    }

    private static bool Warned(AprsPacket p) =>
        p.Diagnostics.Any(d => d.Code == AprsDiagnosticCode.MalformedTimestamp && d.Severity == AprsDiagnosticSeverity.Warning);

    [Theory]
    [InlineData("@R30549z2831.07N/08142.92W_015/001g006", 'z')]
    [InlineData("/R30549z2831.07N/08142.92W_015/001g006", 'z')]
    [InlineData("@R30549/2831.07N/08142.92W_015/001g006", '/')]
    [InlineData("/R30549h2831.07N/08142.92W_015/001g006", 'h')]
    public void The_position_after_the_seven_bytes_wins_over_a_compressed_reading(string info, char suffix)
    {
        info[7].Should().Be(suffix);
        AprsPacket p = Lenient(info);
        var report = p.Data.Should().BeOfType<AprsPositionReport>().Subject;
        report.Position.Latitude.Should().BeApproximately(28.5178333, 1e-6);
        report.Position.Longitude.Should().BeApproximately(-81.7153333, 1e-6);
        report.Symbol.Code.Should().Be('_');
        report.MessagingCapable.Should().Be(info[0] == '@');
        Warned(p).Should().BeTrue();
    }

    [Fact]
    public void Strictly_the_report_is_rejected_by_the_malformed_timestamp()
    {
        Strict("@R30549z2831.07N/08142.92W_015/001g006");
    }

    [Fact]
    public void A_lower_case_hemisphere_reads_leniently_and_is_rejected_strictly()
    {
        AprsPacket p = Lenient("@R30549z2831.07n/08142.92w_015/001g006");
        var report = p.Data.Should().BeOfType<AprsPositionReport>().Subject;
        report.Position.Latitude.Should().BeApproximately(28.5178333, 1e-6);
        Warned(p).Should().BeTrue();

        Strict("@R30549z2831.07n/08142.92w_015/001g006");
    }

    [Fact]
    public void Position_ambiguity_after_the_seven_bytes_is_read()
    {
        AprsPacket p = Lenient("@R30549z28  .  N/081  .  W_");
        var report = p.Data.Should().BeOfType<AprsPositionReport>().Subject;
        report.Position.Ambiguity.Should().BeGreaterThan(0);
        Warned(p).Should().BeTrue();

        Strict("@R30549z28  .  N/081  .  W_");
    }

    [Fact]
    public void A_seventh_byte_that_is_not_a_time_indicator_keeps_the_reading_after_the_dti()
    {
        // Same bytes as the live packet but 'q' in seventh place: the compressed reading stands.
        AprsPacket p = Lenient("@R30549q2831.07N/08142.92W_015/001g006");
        var report = p.Data.Should().BeOfType<AprsPositionReport>().Subject;
        report.Position.Latitude.Should().NotBeApproximately(28.5178333, 0.01);
        report.Symbol.Table.Should().Be('R');
    }

    [Fact]
    public void A_compressed_position_after_the_seven_bytes_does_not_win()
    {
        // 'R30549z' then a compressed position: the reading straight after the DTI stands.
        AprsPacket p = Lenient("@R30549z/5L!!<*e7>{?");
        var report = p.Data.Should().BeOfType<AprsPositionReport>().Subject;
        report.Symbol.Table.Should().Be('R');
    }

    [Fact]
    public void A_field_too_short_for_a_position_after_the_seven_bytes_is_not_misread()
    {
        AprsPacket p = Lenient("@R30549z");
        p.Data.Should().NotBeOfType<AprsPositionReport>();
    }

    [Fact]
    public void The_garbled_report_re_encodes_without_a_timestamp()
    {
        AprsPacket p = Lenient("@R30549z2831.07N/08142.92W_015/001g006");
        p.Data.Should().BeOfType<AprsPositionReport>();
        AprsPacket.Create(AprsAddress.Parse("N0CALL"), p.Data, AprsAddress.Parse("APZ001"))
            .Information.ToArray().Should().StartWith("="u8.ToArray());
    }
}
