namespace Packet.Aprs.Internal;

/// <summary>
/// The one place information-field bytes are made from data, and so the one choke point of the
/// read-back (vectors README, Rules, "Read-back"): the field is written, then decoded and compared
/// with the data given by <see cref="ReadBack"/>, whatever the report type, with no condition. When
/// it does not match, the next form the Encoding rule names for the same data is written and read
/// back; when none matches the data is refused. Bytes that failed are never returned, and the size
/// of the information field is judged last, on the bytes that passed. Every public call that writes
/// an information field ends here (<see cref="AprsData.ToInformationField"/>,
/// <see cref="AprsPacket.Create(AprsAddress, AprsData, AprsAddress?, IEnumerable{AprsPathEntry}?)"/>,
/// the builders, and a third-party packet's inner packet), and nothing else takes the bytes a
/// writer made (<see cref="InfoWriter.TakeBytes"/>): a test holds the source to that.
/// </summary>
internal static class InfoEncoder
{
    /// <summary>The most bytes an information field holds (APRS12c ch. 3).</summary>
    internal const int MaxInformationField = 256;

    /// <summary>
    /// A seam for the tests: changes the bytes between writing and the read-back, so a test can show
    /// that no report type can return bytes that were not read back (a writer that wrote a wrong
    /// byte is refused, whatever the type). Null, and thread-local, outside such a test.
    /// </summary>
    [ThreadStatic]
    internal static Func<byte[], byte[]>? AfterWriting;

    private static readonly CommentDelimiter[] Forms = [CommentDelimiter.Auto, CommentDelimiter.Slash, CommentDelimiter.Space];
    private static readonly CommentDelimiter[] OneForm = [CommentDelimiter.Auto];

    public static byte[] Encode(AprsData data)
    {
        // The forms that differ only in what stands before a comment: straight on as the rules
        // choose, then after a '/', then after a space.
        CommentDelimiter[] forms = data is AprsPositionedData { Comment.Length: > 0 } ? Forms : OneForm;
        AprsEncodeException? first = null;
        foreach (CommentDelimiter form in forms)
        {
            byte[] info;
            try
            {
                info = Write(data, form);
                if (AfterWriting is { } tamper)
                {
                    info = tamper(info);
                }
            }
            catch (AprsEncodeException ex) when (first is not null || ex.Code is AprsRefusalCode.TextReadsAsField or AprsRefusalCode.DoesNotReadBack)
            {
                // The first form's refusal is the answer if no form reads back.
                first ??= ex;
                continue;
            }

            // Undecoded data is a decoded field kept as received (nothing was given to compare with).
            if (data is not AprsUnrecognizedData && ReadBack.Check(data, info) is { } mismatch)
            {
                first ??= mismatch.ToException();
                continue;
            }

            if (info.Length > MaxInformationField)
            {
                // The last check, on the bytes actually written: a record any other rule refuses gets
                // that rule's code, and nothing is measured that was not written (vectors README,
                // Encoding and Refusal reasons).
                throw new AprsEncodeException(AprsRefusalCode.InformationFieldTooLong, $"the information field is {info.Length} bytes, over the 256 an APRS information field holds (APRS12c ch. 3)");
            }

            return info;
        }

        throw first!;
    }

    private static byte[] Write(AprsData data, CommentDelimiter form)
    {
        var writer = new InfoWriter { Delimiter = form };
        try
        {
            data.Encode(writer);
        }
        catch (AprsEncodeException ex)
        {
            writer.Interrupted = true;
            writer.Hold(ex);
        }
        catch (Exception) when (writer.HasDeferred)
        {
            writer.Interrupted = true;
            // A field written after one already refused may not make sense on its own; the
            // refusal held for the earlier field is the answer.
        }

        byte[] info = writer.TakeBytes();
        if (!writer.Interrupted && info.Length > 0 && info[^1] is (byte)'\r' or (byte)'\n')
        {
            // A field ending in CR or LF reads back with trailing-line-break, a warning, and
            // without the byte (Mic-E telemetry ending 13, say), so no clean form exists.
            writer.Defer(AprsRefusalCode.InvalidCharacter, "the information field would end with a line break, which a decoder strips with a warning");
        }

        writer.ThrowDeferred();
        return info;
    }
}
