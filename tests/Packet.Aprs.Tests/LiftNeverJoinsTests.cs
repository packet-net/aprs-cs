namespace Packet.Aprs.Tests;

/// <summary>
/// Round 5: an element is recognised only where its bytes stand whole as sent; lifting one never
/// joins the text either side of it, bar a /A= before a voice frequency or between its fields.
/// Beyond the shared vector cases: other element pairs, in both orders, in each kind of comment.
/// </summary>
public class LiftNeverJoinsTests
{
    private const string Pos = "N0CALL>APZ001:=4903.50N/07201.75W-";
    private const string Tele = "|!:&0hl!Y[/|";

    private static T Decode<T>(string packet, AprsParseOptions? options = null)
        where T : AprsData =>
        AprsPacket.Decode(packet, options).Data.Should().BeOfType<T>().Subject;

    [Fact]
    public void A_range_split_by_an_altitude_is_text()
    {
        var r = Decode<AprsPositionReport>(Pos + "x RNG0/A=000100045 y");
        r.RangeMiles.Should().BeNull();
        r.AltitudeFeet.Should().Be(100);
        r.Comment.Should().Be("x RNG0045 y");
    }

    [Fact]
    public void A_range_split_by_a_dao_is_text_but_one_before_it_is_read()
    {
        Decode<AprsPositionReport>(Pos + "x RNG00!W12!45").RangeMiles.Should().BeNull();

        var whole = Decode<AprsPositionReport>(Pos + "x RNG0045!W12!");
        whole.RangeMiles.Should().Be(45);
        whole.Dao.Should().NotBeNull();
    }

    [Fact]
    public void A_df_strength_split_by_a_dao_is_text()
    {
        var r = Decode<AprsPositionReport>(Pos + "x DFS23!W12!60");
        r.DfSignalStrength.Should().BeNull();
        r.Dao.Should().NotBeNull();
        r.Comment.Should().Be("x DFS2360");
    }

    [Fact]
    public void A_signpost_split_by_an_altitude_is_text_in_an_object()
    {
        AprsPacket p = AprsPacket.Decode("N0CALL>APZ001:;SIGN     *111111z4903.50N\\07201.75Wm{5/A=0001005}");
        var r = p.Data.Should().BeOfType<AprsObjectReport>().Subject;
        r.SignpostText.Should().BeNull();
        r.AltitudeFeet.Should().Be(100);
        r.Comment.Should().Be("{55}");
    }

    [Fact]
    public void A_signpost_split_by_telemetry_is_text_in_an_item()
    {
        AprsPacket p = AprsPacket.Decode("N0CALL>APZ001:)SIGN!4903.50N\\07201.75Wm{5" + Tele + "5}");
        var r = p.Data.Should().BeOfType<AprsItemReport>().Subject;
        r.SignpostText.Should().BeNull();
        r.Telemetry.Should().NotBeNull();
        r.Comment.Should().Be("{55}");
    }

    [Fact]
    public void A_weather_software_type_split_by_telemetry_is_text()
    {
        const string weather = "N0CALL>APZ001:=4903.50N/07201.75W_220/004g005t077";
        var split = Decode<AprsPositionReport>(weather + "wR" + Tele + "SW");
        split.Weather!.SoftwareType.Should().BeNull();
        split.Telemetry.Should().NotBeNull();
        split.Comment.Should().Be("wRSW");

        var whole = Decode<AprsPositionReport>(weather + "wRSW" + Tele);
        whole.Weather!.SoftwareType.Should().Be('w');
        whole.Comment.Should().BeEmpty();
    }

    [Fact]
    public void A_frequency_split_by_a_dao_is_text()
    {
        var r = Decode<AprsPositionReport>(Pos + "146.5!W12!20MHz");
        r.Frequency.Should().BeNull();
        r.Comment.Should().Be("146.520MHz");
    }

    [Fact]
    public void An_altitude_between_two_fields_does_not_cut_them_off_but_a_dao_does()
    {
        var through = Decode<AprsPositionReport>(Pos + "146.520MHz T100/A=000100 -060 x");
        through.Frequency!.ToneValue.Should().Be(100);
        through.Frequency.OffsetKHz.Should().Be(-600);
        through.AltitudeFeet.Should().Be(100);
        through.Comment.Should().Be("x");

        var cut = Decode<AprsPositionReport>(Pos + "146.520MHz T100!W12! -060 x");
        cut.Frequency!.ToneValue.Should().Be(100);
        cut.Frequency.OffsetKHz.Should().BeNull();
        cut.Comment.Should().Be("-060 x");
    }

    [Fact]
    public void A_mic_e_frequency_loses_its_tone_to_a_dao_between_them()
    {
        var r = Decode<AprsMicEReport>("N0CALL>S32UVT:`(_fn\"Oj/`146.520MHz!W12! T100 x");
        r.Frequency.Should().NotBeNull();
        r.Frequency!.ToneValue.Should().BeNull();
        r.Dao.Should().NotBeNull();
        r.Comment.Should().Be("T100 x");
    }

    [Fact]
    public void A_mic_e_altitude_split_by_telemetry_is_text()
    {
        var r = Decode<AprsMicEReport>("N0CALL>S32UVT:`(_fn\"Oj/`x\"4" + Tele + "e}");
        r.AltitudeFeet.Should().BeNull();
        r.Telemetry.Should().NotBeNull();
        r.Comment.Should().Be("x\"4e}");
    }

    [Fact]
    public void A_mic_e_phg_split_by_a_dao_is_text()
    {
        var r = Decode<AprsMicEReport>("N0CALL>S32UVT:`(_fn\"Oj/`PH!W12!G3330 x");
        r.Phg.Should().BeNull();
        r.Dao.Should().NotBeNull();
        r.Comment.Should().Be("PHG3330 x");
    }

    [Fact]
    public void Strict_reads_text_left_across_a_dao_and_never_rejects_for_a_later_altitude()
    {
        AprsPacket p = AprsPacket.Decode("N0CALL>S32UVT:`(_fn\"Oj/`x\"4!W12!e}", AprsParseOptions.Strict);
        p.Data.Should().BeOfType<AprsMicEReport>().Which.Comment.Should().Be("x\"4e}");
        p.Diagnostics.Should().NotContain(d => d.Code == AprsDiagnosticCode.MicEAltitudeNotFirst);
    }

    [Theory]
    [InlineData("PHG3330 x")]
    [InlineData("a /A=000123")]
    [InlineData("{55}")]
    public void Text_left_that_would_read_as_an_element_has_no_clean_form(string comment)
    {
        AprsPositionReport back = Decode<AprsPositionReport>(Pos + "PH!W12!G3330 x");
        back.Comment.Should().Be("PHG3330 x");

        var report = new AprsPositionReport { Position = back.Position, Symbol = new AprsSymbol('\\', 'm'), Comment = comment };
        FluentActions.Invoking(() => report.ToInformationField())
            .Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.TextReadsAsField);
    }

    [Fact]
    public void A_comment_after_a_frequency_and_an_altitude_that_reads_as_a_field_is_written_straight_after()
    {
        var report = new AprsPositionReport
        {
            Position = new AprsPosition(49.0583, -72.0292),
            Symbol = AprsSymbol.Car,
            Frequency = new AprsVoiceFrequency { FrequencyMHz = 146.52 },
            AltitudeFeet = 100,
            Comment = "T100 x",
        };
        string written = System.Text.Encoding.Latin1.GetString(report.ToInformationField());
        written.Should().EndWith("146.520MHz/A=000100T100 x");

        var back = Decode<AprsPositionReport>("N0CALL>APZ001:" + written);
        back.Frequency!.ToneType.Should().BeNull();
        back.Comment.Should().Be("T100 x");
    }
}
