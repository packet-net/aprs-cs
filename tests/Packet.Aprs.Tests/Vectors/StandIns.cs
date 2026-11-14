using Packet.Aprs.Internal;

namespace Packet.Aprs.Tests.Vectors;

/// <summary>
/// Input the library's data cannot hold but whose value alone decides a refusal code (NaN or an
/// infinity where a whole number goes, a bulletin id of the wrong length). The reader notes the
/// code and carries on with a valid stand-in; the caller then applies the vectors README's
/// procedure for such a record: the result with the input left out is reported if it is refused
/// with a code earlier in the Refusal reasons order than the input's own code, and the input's own
/// code otherwise.
/// </summary>
internal static class StandIns
{
    [ThreadStatic]
    private static List<(AprsRefusalCode Code, string Reason)>? notes;

    /// <summary>Runs <paramref name="work"/>, collecting what the readers noted while it ran; <paramref name="own"/> is the earliest code noted, if any.</summary>
    public static T Collect<T>(Func<T> work, out (AprsRefusalCode Code, string Reason)? own)
    {
        List<(AprsRefusalCode Code, string Reason)>? outer = notes;
        notes = [];
        try
        {
            T result = work();
            own = notes.Count == 0 ? null : notes.OrderBy(n => InfoWriter.Rank(n.Code)).First();
            return result;
        }
        finally
        {
            notes = outer;
        }
    }

    /// <summary>The input's own refusal; the reader goes on with a stand-in when something is collecting, and refuses outright otherwise.</summary>
    public static void Note(AprsRefusalCode code, string reason)
    {
        if (notes is null)
        {
            throw new AprsEncodeException(code, reason);
        }

        notes.Add((code, reason));
    }

    /// <summary>True when the stand-in's own refusal comes before the input's own code, so it is what the record is refused for.</summary>
    public static bool Earlier(AprsRefusalCode standInRefusal, AprsRefusalCode own) => InfoWriter.Rank(standInRefusal) < InfoWriter.Rank(own);
}
