using System.Text.Json.Nodes;
using Packet.Aprs.Tests.Vectors;

namespace Packet.Aprs.Corpus;

/// <summary>
/// <c>diff build</c>: builds each recipe (aprs-vectors README, "Build records") with the fluent
/// builder, as a program would, through the same code the vectors test runner uses
/// (<see cref="RecipeBuilder"/>), and writes the TNC2 line built (<c>built</c>), the builder's or
/// encoder's refusal (<c>refused</c>), or <c>unsupported</c> naming the recipe key the builder
/// has no way to say.
/// </summary>
internal static class DiffBuild
{
    public static JsonObject Record(long n, JsonObject recipe)
    {
        var o = new JsonObject { ["n"] = n };
        RecipeBuilder.Outcome outcome = RecipeBuilder.Run(recipe);
        if (outcome.Unsupported is { } unsupported)
        {
            o["result"] = "unsupported";
            o["reason"] = unsupported;
            return o;
        }

        if (outcome.Packet is not { } packet)
        {
            o["result"] = "refused";
            o["reason"] = outcome.Reason;
            if (outcome.Refusal is { } refusal)
            {
                o["refusal"] = refusal;

                // No packet, so no frame either: the refusal is the frame's too.
                o["ax25"] = "refused";
                o["ax25_refusal"] = refusal;
            }

            if (outcome.ViaTool)
            {
                o["via"] = "tool";
            }

            return o;
        }

        // The same packet written both ways: each form reads back its own header (Read-back).
        (string ax25, string? ax25Refusal) = Frame(packet);
        byte[] tnc2;
        try
        {
            tnc2 = packet.ToTnc2();
        }
        catch (AprsEncodeException ex)
        {
            o["result"] = "refused";
            o["reason"] = ex.Message;
            o["refusal"] = NeutralJson.Kebab(ex.Code.ToString());
            o["ax25"] = ax25;
            if (ax25Refusal is not null)
            {
                o["ax25_refusal"] = ax25Refusal;
            }

            return o;
        }

        o["result"] = "built";
        o["tnc2"] = Convert.ToHexStringLower(tnc2);
        o["ax25"] = ax25;
        if (ax25Refusal is not null)
        {
            o["ax25_refusal"] = ax25Refusal;
        }

        try
        {
            o["again"] = DiffCommand.Decoded(AprsPacket.Decode(tnc2, AprsParseOptions.Lenient), withHeader: true);
        }
        catch (AprsFormatException ex)
        {
            o["again"] = new JsonObject { ["header_error"] = NeutralJson.Diagnostics(ex.Diagnostics) };
        }

        return o;
    }

    private static (string Frame, string? Refusal) Frame(AprsPacket packet)
    {
        try
        {
            return (Convert.ToHexStringLower(packet.ToAx25Frame()), null);
        }
        catch (AprsEncodeException ex)
        {
            return ("refused", NeutralJson.Kebab(ex.Code.ToString()));
        }
    }
}
