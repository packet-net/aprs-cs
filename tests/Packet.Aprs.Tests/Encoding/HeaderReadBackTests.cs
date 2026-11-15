namespace Packet.Aprs.Tests.Encoding;

/// <summary>
/// The header a packet writes is read back too (vectors README, Rules, "Read-back"): a line writes
/// <c>*</c> on the last used entry only, a frame sets the H bit of every used entry, and a path with
/// an entry not marked used before one that is cannot be carried, since it reads back with that
/// entry used. A function that writes out a packet the program already holds writes its
/// information field as received, but its header is read back all the same.
/// </summary>
public class HeaderReadBackTests
{
    private static AprsPacket Built(params string[] via) => AprsStation.From("N0CALL").Via(via).Status("hi").Build();

    [Fact]
    public void A_line_writes_a_star_on_the_last_used_entry_only()
    {
        System.Text.Encoding.ASCII.GetString(Built("WIDE1-1*", "N0CALL-1*", "WIDE2-1").ToTnc2())
            .Should().Be("N0CALL>APZ001,WIDE1-1,N0CALL-1*,WIDE2-1:>hi");
    }

    [Fact]
    public void A_frame_sets_the_h_bit_of_every_used_entry()
    {
        byte[] frame = Built("WIDE1-1*", "N0CALL-1*", "WIDE2-1").ToAx25Frame();
        bool[] hBits = [.. Enumerable.Range(2, 3).Select(i => (frame[(7 * i) + 6] & 0x80) != 0)];
        hBits.Should().Equal(true, true, false);
    }

    private static AprsPacket Held(params AprsPathEntry[] path) =>
        AprsPacket.Decode(AprsAddress.Parse("N0CALL"), AprsAddress.Parse("APZ001"), path, ">hi"u8);

    [Fact]
    public void A_path_with_an_unused_entry_before_a_used_one_is_refused_when_the_header_is_given()
    {
        // A fault of the header given, whatever form is written: invalid-path, ahead of any fault in the data.
        FluentActions.Invoking(() => Built("WIDE1-1", "N0CALL-1*")).Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.InvalidPath);
        FluentActions.Invoking(() => AprsPacket.Create("N0CALL", new AprsStatusReport { Text = "hi" }, path: "WIDE1-1,N0CALL-1*"))
            .Should().NotThrow("a text path marks the entries before a * used");
        FluentActions.Invoking(() => AprsPacket.Create(AprsAddress.Parse("N0CALL"), new AprsStatusReport { Text = "hi" }, null, [new AprsPathEntry(AprsAddress.Parse("WIDE1-1")), new AprsPathEntry(AprsAddress.Parse("N0CALL-1"), true)]))
            .Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.InvalidPath);
        FluentActions.Invoking(() => AprsStation.From("N0CALL").Via("WIDE1-1", "N0CALL-1*").Status("a\u0001b").Build())
            .Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.InvalidPath, "the path is judged before the data");
        FluentActions.Invoking(() => AprsStation.From("N0CALL").To("WIDE 1").Via("WIDE1-1", "N0CALL-1*").Status("hi").Build())
            .Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.InvalidAddress, "an address that is not one comes before the path");
    }

    [Fact]
    public void A_held_packet_with_an_unused_entry_before_a_used_one_cannot_be_written_in_either_form()
    {
        AprsPacket packet = Held(new AprsPathEntry(AprsAddress.Parse("WIDE1-1")), new AprsPathEntry(AprsAddress.Parse("N0CALL-1"), true));
        FluentActions.Invoking(packet.ToTnc2).Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.InvalidPath);
        FluentActions.Invoking(packet.ToAx25Frame).Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.InvalidPath);
        packet.ToString().Should().Contain("WIDE1-1,N0CALL-1*", "showing a packet never throws");
    }

    [Fact]
    public void A_path_with_no_used_entry_and_a_path_starting_used_are_carried()
    {
        System.Text.Encoding.ASCII.GetString(Built("WIDE1-1", "WIDE2-1").ToTnc2()).Should().Be("N0CALL>APZ001,WIDE1-1,WIDE2-1:>hi");
        System.Text.Encoding.ASCII.GetString(Built("N0CALL-1*", "WIDE2-1").ToTnc2()).Should().Be("N0CALL>APZ001,N0CALL-1*,WIDE2-1:>hi");
    }

    [Fact]
    public void A_star_is_never_part_of_an_address()
    {
        FluentActions.Invoking(() => AprsStation.From("N0CALL").Via("*")).Should().Throw<AprsEncodeException>().Which.Code.Should().Be(AprsRefusalCode.InvalidAddress);
    }

    [Fact]
    public void A_decoded_packet_with_two_stars_is_written_with_one()
    {
        AprsPacket packet = AprsPacket.Decode("N0CALL>APZ001,WIDE1-1*,N0CALL-1*:>hi");
        packet.HasWarnings.Should().BeTrue();
        System.Text.Encoding.ASCII.GetString(packet.ToTnc2()).Should().Be("N0CALL>APZ001,WIDE1-1,N0CALL-1*:>hi");
    }

    [Fact]
    public void A_decoded_frame_whose_first_digipeater_is_not_marked_before_a_used_one_reads_both_used()
    {
        // H clear, then set: the Addresses rule reads the first as used, as a line's entries before a * are.
        byte[] frame = Convert.FromHexString("82A0B4606062E09C608682989860AE92888A6240629C6086829898E303F03E6869");
        AprsPacket packet = AprsPacket.DecodeAx25(frame);
        packet.Path.Select(e => e.ToString()).Should().Equal("WIDE1-1*", "N0CALL-1*");
        System.Text.Encoding.ASCII.GetString(packet.ToTnc2()).Should().Be("N0CALL>APZ001,WIDE1-1,N0CALL-1*:>hi");
        packet.ToAx25Frame().Should().NotEqual(frame, "the frame written sets the H bit of every used entry");
    }
}
