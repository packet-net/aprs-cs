using System.Reflection;

namespace Packet.Aprs.Tests.Encoding;

/// <summary>
/// An <see cref="AprsPacket"/> cannot hold a decoded form that disagrees with its bytes: it has no
/// public constructor and nothing on it can be set, so data changed with <c>with</c>, or a header
/// changed, becomes a packet only by being written again from data (and read back). The writers
/// of a held packet write the bytes it holds, which are the bytes its data and header came from.
/// </summary>
public class ImmutablePacketTests
{
    [Fact]
    public void A_packet_has_no_public_constructor_and_no_setter()
    {
        typeof(AprsPacket).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty();
        foreach (PropertyInfo property in typeof(AprsPacket).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            property.SetMethod?.IsPublic.Should().NotBe(true, property.Name);
        }

        typeof(AprsPacket).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Should().NotContain(m => m.Name == "<Clone>$", "a class has no with-expression");
    }

    [Fact]
    public void Changed_data_is_written_from_the_changed_data_at_depth_0_1_and_2()
    {
        AprsPacket decoded = AprsPacket.Decode("N0CALL>APZ001,TCPIP*:!5132.07N/00007.65W-hello");
        var position = (AprsPositionReport)decoded.Data;
        AprsPositionReport moved = position with { Position = position.Position with { Latitude = 40.5 } };

        // Depth 0: the held packet still writes its own bytes; the changed data makes a new packet.
        System.Text.Encoding.ASCII.GetString(decoded.ToTnc2()).Should().Contain("5132.07N");
        AprsPacket again = AprsPacket.Create(decoded.Source, moved, null, decoded.Path);
        AprsPacket.Decode(again.ToTnc2()).Data.Should().BeOfType<AprsPositionReport>().Which.Position.Latitude.Should().BeApproximately(40.5, 1e-3);

        // Depth 1 and 2: a decoded third-party packet whose inner packet is replaced with one made
        // from the changed data (`with` on the held record) is a new data value; writing it
        // encodes the change, and the bytes read back as the change at both depths.
        AprsPathEntry[] tcpip = [new AprsPathEntry(AprsAddress.Parse("TCPIP"), true)];
        AprsData deep = AprsThirdPartyTraffic.Create(AprsAddress.Parse("G4ABC"), AprsThirdPartyTraffic.Create(AprsAddress.Parse("G4ABD"), position, null, tcpip), null, tcpip);
        var heldOuter = (AprsThirdPartyTraffic)AprsPacket.Decode(AprsPacket.Create(AprsAddress.Parse("N0CALL"), deep).ToTnc2()).Data;
        var heldMiddle = (AprsThirdPartyTraffic)heldOuter.Packet.Data;
        AprsPacket changedInnermost = AprsPacket.Create(AprsAddress.Parse("G4ABD"), moved, null, tcpip);
        AprsThirdPartyTraffic changedMiddle = heldMiddle with { Packet = changedInnermost };
        AprsPacket middlePacket = AprsPacket.Create(heldOuter.Packet.Source, changedMiddle, null, heldOuter.Packet.Path);
        AprsThirdPartyTraffic changedOuter = heldOuter with { Packet = middlePacket };
        AprsPacket outer = AprsPacket.Create(AprsAddress.Parse("N0CALL"), changedOuter);
        var depth1 = (AprsThirdPartyTraffic)AprsPacket.Decode(outer.ToTnc2()).Data;
        var depth2 = (AprsThirdPartyTraffic)depth1.Packet.Data;
        depth2.Packet.Data.Should().BeOfType<AprsPositionReport>().Which.Position.Latitude.Should().BeApproximately(40.5, 1e-3);
    }

    [Fact]
    public void A_header_changed_by_copy_is_a_different_packet_written_from_data()
    {
        AprsPacket decoded = AprsPacket.Decode("N0CALL>APZ001,TCPIP*:>status");
        AprsPacket other = AprsPacket.Create(AprsAddress.Parse("M0XYZ"), decoded.Data, AprsAddress.Parse("APZ002"), decoded.Path);
        System.Text.Encoding.ASCII.GetString(other.ToTnc2()).Should().Be("M0XYZ>APZ002,TCPIP*:>status");
        System.Text.Encoding.ASCII.GetString(decoded.ToTnc2()).Should().Be("N0CALL>APZ001,TCPIP*:>status");
    }
}
