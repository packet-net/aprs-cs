using System.Diagnostics;

namespace Packet.Aprs.Tests;

/// <summary>
/// Round 5, final pass: a later Mic-E altitude never uses the bytes of a whole /A= altitude, and
/// a decoder fed hostile text stays linear. Beyond the shared vector cases.
/// </summary>
public class FinalPassTests
{
    private const string MicEHead = "N0CALL>S32UVT:`(_fn\"Oj/`";

    private static AprsMicEReport Mic(string status, AprsParseOptions? options = null) =>
        AprsPacket.Decode(MicEHead + status, options).Data.Should().BeOfType<AprsMicEReport>().Subject;

    [Theory]
    [InlineData("x/A=000100}", 100, "x}")]
    [InlineData("x/A=000100}y", 100, "x}y")]
    [InlineData("/A=000100}", 100, "}")]
    [InlineData("x/A=-00016}", -16, "x}")]
    [InlineData("x/A=000100x}", 100, "xx}")]
    public void A_later_mic_e_altitude_never_uses_the_bytes_of_a_whole_altitude(string status, double feet, string comment)
    {
        foreach (AprsParseOptions? options in new AprsParseOptions?[] { null, AprsParseOptions.Strict })
        {
            AprsPacket p = AprsPacket.Decode(MicEHead + status, options);
            var r = p.Data.Should().BeOfType<AprsMicEReport>().Subject;
            r.AltitudeFeet.Should().Be(feet);
            r.Comment.Should().Be(comment);
            p.Diagnostics.Should().NotContain(d => d.Code == AprsDiagnosticCode.MicEAltitudeNotFirst);
        }
    }

    [Fact]
    public void An_altitude_written_with_a_comment_that_ends_in_a_brace_reads_back_as_it_was()
    {
        var r = Mic("x/A=000100}");
        AprsPacket created = AprsPacket.Create(AprsAddress.Parse("N0CALL"), r);
        System.Text.Encoding.Latin1.GetString(created.Information.Span).Should().EndWith("/A=000100x}");
        AprsPacket.Decode(created.Source, created.Destination, [], created.Information.Span).Data.Should().Be(r);
    }

    [Fact]
    public void A_clear_later_altitude_is_still_taken_beside_an_altitude()
    {
        AprsPacket p = AprsPacket.Decode(MicEHead + "x/A=000100}y\"4e}z");
        var r = p.Data.Should().BeOfType<AprsMicEReport>().Subject;
        r.AltitudeFeet.Should().Be(100);
        r.Comment.Should().Be("x}yz");
        p.Diagnostics.Should().ContainSingle(d => d.Code == AprsDiagnosticCode.MicEAltitudeNotFirst);
    }

    [Fact]
    public void An_altitude_of_five_digits_protects_nothing()
    {
        AprsPacket p = AprsPacket.Decode(MicEHead + "x/A=00100}");
        var r = p.Data.Should().BeOfType<AprsMicEReport>().Subject;
        r.Comment.Should().Be("x/A=00");
        p.Diagnostics.Should().ContainSingle(d => d.Code == AprsDiagnosticCode.MicEAltitudeNotFirst);
    }

    [Fact]
    public void A_run_of_overlapping_altitudes_is_scanned_once()
    {
        string status = "x" + string.Concat(Enumerable.Repeat("/A=000100}", 20000));
        var r = Mic(status);
        r.AltitudeFeet.Should().Be(100);
    }

    private static readonly string[] Heads =
    [
        "N0CALL>APZ001:!4903.50N/07201.75W-",
        "N0CALL>APZ001:!4903.50N\\07201.75Wm",
        "N0CALL>APZ001:;SIGN     *111111z4903.50N\\07201.75Wm",
        "N0CALL>APZ001:!4903.50N/07201.75W_",
        MicEHead + "x",
    ];

    private static readonly string[] Elements = ["{", "PHG1", "|!!|", "/A=000001", "!W12!", "\"4e}", "/A=000100}", "RNG0", "DFS2"];

    private static long Best(string head, string element, int bytes)
    {
        string packet = head + string.Concat(Enumerable.Repeat(element, bytes / element.Length));
        long best = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            long t = Stopwatch.GetTimestamp();
            AprsPacket.Decode(packet);
            AprsPacket.Decode(packet, AprsParseOptions.Strict);
            best = Math.Min(best, Stopwatch.GetTimestamp() - t);
        }

        return best;
    }

    [Fact]
    public void Decoding_a_long_run_of_one_element_is_linear()
    {
        // Four times the bytes must cost well under sixteen times the time (a quadratic decoder
        // costs sixteen, a linear one four): a ratio at two sizes, never a time limit.
        foreach (string head in Heads)
        {
            AprsPacket.Decode(head + new string('x', 5000) + "{PHG1|!!|!W12!/A=000001");
            foreach (string element in Elements)
            {
                long small = Best(head, element, 200_000);
                long large = Best(head, element, 800_000);
                ((double)large / Math.Max(small, Stopwatch.Frequency / 2000)).Should().BeLessThan(9, $"{head[15..]} + {element}");
            }
        }
    }
}
