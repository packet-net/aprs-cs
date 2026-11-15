using System.Diagnostics;

namespace Packet.Aprs.Tests;

/// <summary>
/// Round 5, final pass: a later Mic-E altitude never uses the bytes of a whole /A= altitude, and
/// a decoder fed hostile text stays linear. Beyond the shared vector cases.
/// </summary>
/// <summary>Holds the timing ratio test: a collection that runs alone, so other tests do not slow one size and not the other.</summary>
[CollectionDefinition("Timing", DisableParallelization = true)]
public class TimingDefinition;

[Collection("Timing")]
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

    [Theory]
    [InlineData(">/A=000123/A=-00016}^")]
    [InlineData("]/A=000123/A=000100}habc}ello")]
    [InlineData("'146.52 MHz t077 -060/A=-00016/A=000100}  ")]
    public void A_second_altitude_as_text_after_an_altitude_is_written_after_a_slash(string status)
    {
        AprsPacket decoded = AprsPacket.Decode("N0CALL>S32UVT:`(_fn\"Oj/" + status);
        var r = decoded.Data.Should().BeOfType<AprsMicEReport>().Subject;
        r.Comment.Should().StartWith("A=");
        AprsPacket created = AprsPacket.Create(AprsAddress.Parse("N0CALL"), r);
        string info = System.Text.Encoding.Latin1.GetString(created.Information.Span);
        info.Should().Contain("/A=");
        AprsPacket.Decode(created.Source, created.Destination, [], created.Information.Span).Data.Should().Be(r);
    }

    [Theory]
    [InlineData("\"4e}/PHG1|146.520MHz!!|", "PHG1")]
    [InlineData("\"4e}/PHG5|146.520MHz!!|", "PHG5")]
    public void Text_that_reads_as_a_phg_after_the_altitude_is_written_after_a_slash(string status, string comment)
    {
        var r = MicNoType(status);
        r.Comment.Should().Be(comment);
        r.Phg.Should().BeNull();
        AprsPacket created = AprsPacket.Create(AprsAddress.Parse("N0CALL"), r);
        System.Text.Encoding.Latin1.GetString(created.Information.Span).Should().EndWith(status);
        AprsPacket.Decode(created.Source, created.Destination, [], created.Information.Span).Data.Should().Be(r);
    }

    [Theory]
    [InlineData("/PHG1|146.520MHz!!|")]
    [InlineData("/FN31ab/G PHG1|146.520MHz!!|")]
    [InlineData("/\"4e}PHG1|x")]
    public void Text_that_could_read_as_a_phg_reads_back_in_whatever_form_is_written(string status)
    {
        var r = MicNoType(status);
        AprsPacket created = AprsPacket.Create(AprsAddress.Parse("N0CALL"), r);
        AprsPacket.Decode(created.Source, created.Destination, [], created.Information.Span).Data.Should().Be(r);
    }

    [Theory]
    [InlineData("RNG0100", true)]
    [InlineData("DFS2360", true)]
    [InlineData("RNG0100", false)]
    [InlineData("DFS2360", false)]
    public void Text_that_reads_as_an_extension_with_no_form_that_holds_it_is_refused_never_written(string comment, bool afterAltitude)
    {
        var r = MicNoType(afterAltitude ? "/\"4e}" : "/") with { Comment = comment };
        Action act = () => AprsPacket.Create(AprsAddress.Parse("N0CALL"), r);
        act.Should().Throw<AprsEncodeException>();
    }

    private static AprsMicEReport MicNoType(string status) =>
        AprsPacket.Decode("N0CALL>S32UVT:`(_fn\"Oj3" + status).Data.Should().BeOfType<AprsMicEReport>().Subject;

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

    private static readonly string[] Elements = ["{", "PHG1", "|!!|", "/A=000001", "!W12!", "\"4e}", "/A=000100}", "RNG0", "DFS2", "|", "||", "|ab|", "|!!!!", "|!!!!|a b"];

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
        // costs sixteen, a linear one four): a ratio at two sizes, never a time limit. A run on a
        // busy machine can be slowed by a pause in the larger size only, so the cost at each size is
        // the least of every run so far, and more runs are made until the ratio is under the limit
        // or four rounds have been made: a quadratic decoder is over it every round, and noise is not.
        foreach (string head in Heads)
        {
            AprsPacket.Decode(head + new string('x', 5000) + "{PHG1|!!|!W12!/A=000001");
            foreach (string element in Elements)
            {
                long small = long.MaxValue;
                long large = long.MaxValue;
                double ratio = double.MaxValue;
                for (int round = 0; round < 4 && ratio >= 9; round++)
                {
                    GC.Collect();
                    small = Math.Min(small, Best(head, element, 200_000));
                    large = Math.Min(large, Best(head, element, 800_000));
                    ratio = (double)large / Math.Max(small, Stopwatch.Frequency / 2000);
                }

                ratio.Should().BeLessThan(9, $"{head[15..]} + {element}");
            }
        }
    }
}
