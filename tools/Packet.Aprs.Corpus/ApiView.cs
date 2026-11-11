using System.Text.Json.Nodes;
using Packet.Aprs.Tests.Vectors;

namespace Packet.Aprs.Corpus;

/// <summary>
/// The API view of a decoded packet (aprs-vectors README, "The API view"): what Packet.Aprs's own
/// API offers beyond the neutral form, read through the public API as a program would, not
/// through the code that writes the neutral form. Only the keys the API offers are written:
/// Packet.Aprs has no accessor saying a packet was carried inside a third-party packet, so there
/// is no <c>third_party</c>.
/// </summary>
internal static class ApiView
{
    public static JsonObject Of(AprsPacket packet)
    {
        var o = new JsonObject
        {
            ["source"] = packet.Source.Value,
            ["destination"] = packet.Destination.Value,
            ["path"] = new JsonArray([.. packet.Path.Select(e => (JsonNode)JsonValue.Create(e.ToString())!)]),
            ["q_construct"] = packet.QConstruct is { } q ? QConstruct(q) : null,
            ["has_errors"] = packet.HasErrors,
            ["has_warnings"] = packet.HasWarnings,
            ["tnc2"] = Convert.ToHexStringLower(packet.ToTnc2()),
            ["ax25"] = Ax25(packet, out string? ax25Refusal),
            ["device"] = Device(AprsDeviceIdentification.Identify(packet)),
        };
        if (ax25Refusal is not null)
        {
            o["ax25_refusal"] = ax25Refusal;
        }

        // The symbol in the data, where the data has one; its description is null when the
        // library has none for it.
        AprsSymbol? symbol = packet.Data switch
        {
            AprsPositionedData d => d.Symbol,
            AprsStatusReport s => s.Symbol,
            _ => null,
        };
        if (symbol is { } sym)
        {
            o["symbol"] = new JsonObject { ["description"] = sym.Description };
        }

        if (packet.Data is AprsPositionedData { Phg: { } phg })
        {
            o["phg"] = new JsonObject
            {
                ["watts"] = phg.PowerWatts,
                ["height_feet"] = phg.HeightFeet,
                ["gain_db"] = phg.GainDb,
                ["directivity_degrees"] = phg.DirectivityDegrees,
            };
        }

        if (packet.Data is AprsThirdPartyTraffic third)
        {
            o["inner"] = Of(third.Packet);
        }

        return o;
    }

    private static JsonObject QConstruct(AprsQConstruct q)
    {
        var o = new JsonObject { ["construct"] = q.Construct };
        if (q.Station is { } station)
        {
            o["station"] = station.Value;
        }

        return o;
    }

    /// <summary>The UI frame without flags or FCS, or <c>refused</c> when an address or the path cannot be carried.</summary>
    private static string Ax25(AprsPacket packet, out string? refusal)
    {
        refusal = null;
        try
        {
            return Convert.ToHexStringLower(packet.ToAx25Frame());
        }
        catch (AprsEncodeException ex)
        {
            refusal = NeutralJson.Kebab(ex.Code.ToString());
            return "refused";
        }
    }

    private static JsonObject? Device(AprsDevice? device)
    {
        if (device is null)
        {
            return null;
        }

        var o = new JsonObject();
        if (!string.IsNullOrEmpty(device.Vendor))
        {
            o["vendor"] = device.Vendor;
        }

        if (!string.IsNullOrEmpty(device.Model))
        {
            o["model"] = device.Model;
        }

        if (!string.IsNullOrEmpty(device.DeviceClass))
        {
            o["class"] = device.DeviceClass;
        }

        return o;
    }
}
