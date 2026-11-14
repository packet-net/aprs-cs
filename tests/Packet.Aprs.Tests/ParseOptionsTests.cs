using System.Reflection;

namespace Packet.Aprs.Tests;

/// <summary>
/// The two presets. What each flag tolerates, and that turning it off alone rejects the defect, is
/// checked case by case in the language-neutral vectors (<c>spec/aprs/cases</c>, run by
/// <c>Vectors/AprsVectorTests</c>).
/// </summary>
public class ParseOptionsTests
{
    [Fact]
    public void Strict_turns_every_flag_off_and_lenient_turns_every_flag_on()
    {
        foreach (PropertyInfo p in typeof(AprsParseOptions).GetProperties().Where(p => p.PropertyType == typeof(bool)))
        {
            ((bool)p.GetValue(AprsParseOptions.Strict)!).Should().BeFalse(p.Name);
            ((bool)p.GetValue(AprsParseOptions.Lenient)!).Should().BeTrue(p.Name);
        }
    }

    [Fact]
    public void A_path_entry_of_only_a_used_marker_is_an_invalid_address_not_an_empty_entry()
    {
        // Outer header: not an address, whatever the options.
        AprsPacket.TryDecode("N0CALL>APZ001,WIDE1-1*,*,WIDE2-1*:>x", out _, AprsParseOptions.Lenient).Should().BeFalse();

        // Inside a third-party packet: the inner header is unusable, so the data is unrecognized.
        AprsPacket.TryDecode("N0CALL>APZ001:}N000CALL>APZ,WIDE1-1*,*,WIDE2-1*:>x", out AprsPacket? packet, AprsParseOptions.Lenient).Should().BeTrue();
        packet!.Data.Should().BeOfType<AprsUnrecognizedData>();
        packet.Diagnostics.Should().Contain(d => d.Code == AprsDiagnosticCode.InvalidThirdParty);
        packet.Diagnostics.Should().NotContain(d => d.Code == AprsDiagnosticCode.EmptyPathEntry || d.Code == AprsDiagnosticCode.MultipleUsedMarkers);
    }
}
