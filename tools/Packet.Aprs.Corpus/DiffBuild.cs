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
            }

            return o;
        }

        byte[] tnc2 = packet.ToTnc2();
        o["result"] = "built";
        o["tnc2"] = Convert.ToHexStringLower(tnc2);
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
}
