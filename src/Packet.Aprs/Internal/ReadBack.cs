namespace Packet.Aprs.Internal;

/// <summary>
/// The read-back (vectors README, Rules, "Read-back"): decode what an encoder wrote and compare it
/// with the data it was given, field for field, by the one comparison of this class. Every byte an
/// encoder returns has passed it, header included (<see cref="InfoEncoder"/> is the only caller
/// for an information field; <see cref="AprsPacket.ToTnc2"/> and <see cref="AprsPacket.ToAx25Frame"/>
/// for a header).
/// </summary>
internal static class ReadBack
{
    private static readonly AprsAddress Sender = AprsAddress.Parse("N0CALL");
    private static readonly AprsAddress DefaultDestination = AprsAddress.Parse(AprsStation.DefaultDestination);

    /// <summary>The infos the rules attach to the form the data itself asks for; any other info is a change.</summary>
    private static readonly AprsDiagnosticCode[] AllowedInfos =
    [
        AprsDiagnosticCode.ObsoleteFormat,
        AprsDiagnosticCode.MicEMissingDeviceType,
        AprsDiagnosticCode.InvalidQuery,
        AprsDiagnosticCode.InvalidTelemetryMetadata,
    ];

    /// <summary>The NMEA fields a sentence determines: given or left out, they read back as the sentence has them.</summary>
    private static readonly string[] NmeaDetermined = ["latitude", "longitude", "fix", "course_degrees", "speed_knots", "altitude_m", "time", "waypoint"];

    /// <summary>What differed: each field or diagnostic that did not read back as given, and the refusal it earns.</summary>
    internal sealed class Mismatch(AprsRefusalCode code, IReadOnlyList<string> fields)
    {
        public AprsRefusalCode Code { get; } = code;

        public IReadOnlyList<string> Fields { get; } = fields;

        public AprsEncodeException ToException() =>
            new(Code, $"the bytes written would read back as different data ({string.Join(", ", Fields.Take(6))}{(Fields.Count > 6 ? ", ..." : "")}); no form the rules allow reads back as given");
    }

    /// <summary>
    /// Decodes <paramref name="info"/> leniently under <c>N0CALL&gt;APZ001</c> (for Mic-E, the
    /// destination the encoder computes) and compares it with <paramref name="given"/>. Null when it
    /// matches. <paramref name="altitudeAsFeet"/> says which form a Mic-E altitude was written in.
    /// </summary>
    public static Mismatch? Check(AprsData given, byte[] info, bool? altitudeAsFeet = null)
    {
        AprsAddress destination = given is AprsMicEReport mic ? MicECodec.EncodeDestination(mic) : DefaultDestination;
        AprsPacket back = AprsPacket.Decode(Sender, destination, [], info);
        var bad = new List<string>();

        if (given is AprsUnrecognizedData unrecognized)
        {
            // A field kept as received reads back as the same unrecognized bytes, for the same reason.
            // Its own diagnostics (not-aprs, an error) are the reading of those bytes, not a change.
            if (back.Data is not AprsUnrecognizedData again || again.Reason != unrecognized.Reason || !again.Raw.SequenceEqual(unrecognized.Raw))
            {
                bad.Add("raw");
            }

            return bad.Count == 0 ? null : new Mismatch(AprsRefusalCode.DoesNotReadBack, bad);
        }

        // Diagnostics: none but the listed infos; an inner packet is held to the same, and its infos are not compared.
        foreach (AprsDiagnostic d in back.Diagnostics)
        {
            if (d.Severity != AprsDiagnosticSeverity.Info || !AllowedInfos.Contains(d.Code))
            {
                bad.Add($"{Neutral.Kebab(d.Severity.ToString())}:{Neutral.Kebab(d.Code.ToString())}");
            }
        }

        // An inner packet is held to the same, against the diagnostics it carries as data: one
        // decoded with a warning or an error is written as received and reads back with it, one
        // built by an encoder has none. Its infos are not compared, given or decoded.
        AprsData wanted = given;
        for (AprsData data = back.Data; data is AprsThirdPartyTraffic tp; data = tp.Packet.Data)
        {
            var carried = wanted is AprsThirdPartyTraffic gt ? NonInfo(gt.Packet.Diagnostics) : [];
            wanted = wanted is AprsThirdPartyTraffic w ? w.Packet.Data : wanted;
            var found = NonInfo(tp.Packet.Diagnostics);
            carried.Sort(StringComparer.Ordinal);
            found.Sort(StringComparer.Ordinal);
            if (!carried.SequenceEqual(found))
            {
                bad.Add("inner diagnostics: " + string.Join(", ", found.Except(carried)));
            }
        }

        Dictionary<string, object> expected = Neutral.Given(given, altitudeAsFeet);
        Dictionary<string, object> read = Neutral.Decoded(back.Data);
        Allowances(given, expected, read);
        Compare(expected, read, bad);
        return bad.Count == 0 ? null : new Mismatch(Code(given, back.Data, bad), bad);
    }

    private static List<string> NonInfo(IEnumerable<AprsDiagnostic> diagnostics) =>
        [.. diagnostics.Where(d => d.Severity != AprsDiagnosticSeverity.Info).Select(d => $"{Neutral.Kebab(d.Severity.ToString())}:{Neutral.Kebab(d.Code.ToString())}")];

    /// <summary>The values a form must carry where the data leaves them out (README, Read-back, allowance 2).</summary>
    private static void Allowances(AprsData given, Dictionary<string, object> expected, Dictionary<string, object> read)
    {
        if (given is AprsMicEReport { SpeedKnots: null } && read.TryGetValue("speed_knots", out object? speed) && speed is double and 0)
        {
            read.Remove("speed_knots");
        }

        if (given is AprsPositionedData { IsCompressed: true, CompressionType: null } && read.ContainsKey("compression.fix"))
        {
            // Exactly the default type, not any.
            expected["compression.fix"] = Neutral.Kebab(AprsCompressionType.Default.Fix.ToString());
            expected["compression.source"] = Neutral.Kebab(AprsCompressionType.Default.Source.ToString());
            expected["compression.origin"] = Neutral.Kebab(AprsCompressionType.Default.Origin.ToString());
        }

        if (given is AprsNmeaReport)
        {
            foreach (string key in NmeaDetermined)
            {
                if (!expected.ContainsKey(key))
                {
                    read.Remove(key);
                }
            }
        }
    }

    private static void Compare(Dictionary<string, object> expected, Dictionary<string, object> read, List<string> bad)
    {
        foreach ((string key, object value) in expected)
        {
            if (!read.TryGetValue(key, out object? other) || !Same(value, other))
            {
                bad.Add(key);
            }
        }

        foreach (string key in read.Keys)
        {
            if (!expected.ContainsKey(key))
            {
                bad.Add(key);
            }
        }
    }

    /// <summary>Strings, booleans and integers exactly; any other number within 1e-9, relative to the larger magnitude (absolute below 1).</summary>
    private static bool Same(object a, object b)
    {
        if (a is string sa)
        {
            return b is string sb && sa == sb;
        }

        if (a is bool ba)
        {
            return b is bool bb && ba == bb;
        }

        if (a is long la && b is long lb)
        {
            return la == lb;
        }

        if (a is Neutral.Null)
        {
            return b is Neutral.Null;
        }

        if (!TryNumber(a, out double x) || !TryNumber(b, out double y))
        {
            return false;
        }

        return x == y || Math.Abs(x - y) <= 1e-9 * Math.Max(1, Math.Max(Math.Abs(x), Math.Abs(y)));
    }

    private static bool TryNumber(object value, out double number)
    {
        switch (value)
        {
            case double d:
                number = d;
                return true;
            case long l:
                number = l;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    /// <summary>
    /// The refusal for bytes that did not read back: the code of the rule whose fault the read-back
    /// caught where one covers it, <c>text-reads-as-field</c> when free text reads back changed (a
    /// weather report's software type, unit and extra fields are free text), else the fallback.
    /// </summary>
    private static AprsRefusalCode Code(AprsData given, AprsData back, List<string> bad)
    {
        if (given is AprsNmeaReport && bad.Exists(k => NmeaDetermined.Contains(k)))
        {
            return AprsRefusalCode.InvalidNmea;
        }

        if (given is AprsTextMessage message && back.GetType() != given.GetType() && MessageCodec.IsBulletinForm(message.Addressee))
        {
            // An addressee that makes a text message a bulletin is not the form a message needs.
            return AprsRefusalCode.InvalidAddressee;
        }

        return bad.Exists(IsFreeText) ? AprsRefusalCode.TextReadsAsField : AprsRefusalCode.DoesNotReadBack;
    }

    private static bool IsFreeText(string key)
    {
        int dot = key.LastIndexOf('.');
        string last = dot < 0 ? key : key[(dot + 1)..];
        int bracket = last.IndexOf('[', StringComparison.Ordinal);
        if (bracket >= 0)
        {
            last = last[..bracket];
        }

        return last is "comment" or "text" or "signpost" or "software" or "unit" or "letter" or "value" or "names" or "units" or "project" or "data";
    }

    // ------------------------------------------------------------------ the header

    /// <summary>
    /// Reads back the header of a TNC2 line: the source, destination and path, entry by entry with
    /// its used mark, as given. A path with an entry not marked used before one that is reads back
    /// with that entry used, so it cannot be carried.
    /// </summary>
    public static void CheckTnc2Header(AprsAddress source, AprsAddress destination, IReadOnlyList<AprsPathEntry> path, byte[] line, bool inner = false)
    {
        int colon = Array.IndexOf(line, (byte)':');
        var ctx = new DecodeContext(AprsParseOptions.Lenient);
        if (colon < 0 || !Tnc2Codec.TryParseHeader(line.AsSpan(0, colon), ctx, out Tnc2Codec.Header header, inner)
            || !SameHeader(source, destination, path, header, ctx))
        {
            throw new AprsEncodeException(AprsRefusalCode.Tnc2CannotCarry, HeaderRefusal("a TNC2 line", path));
        }
    }

    /// <summary>The same for an AX.25 frame's addresses, a digipeater being used when its H bit is set or one after it is.</summary>
    public static void CheckAx25Header(AprsAddress source, AprsAddress destination, IReadOnlyList<AprsPathEntry> path, byte[] frame)
    {
        var ctx = new DecodeContext(AprsParseOptions.Lenient);
        if (!Ax25Codec.TryDecode(frame, ctx, out Tnc2Codec.Header header, out _))
        {
            throw new AprsEncodeException(AprsRefusalCode.Ax25CannotCarry, HeaderRefusal("an AX.25 frame", path));
        }

        if (!SameHeader(source, destination, path, header, ctx))
        {
            throw new AprsEncodeException(AprsRefusalCode.Ax25CannotCarry, HeaderRefusal("an AX.25 frame", path));
        }
    }

    private static string HeaderRefusal(string form, IReadOnlyList<AprsPathEntry> path) =>
        $"{form} cannot carry the path {AprsPathEntry.FormatList(path)}: an entry not marked used comes before one that is, and reads back used";

    private static bool SameHeader(AprsAddress source, AprsAddress destination, IReadOnlyList<AprsPathEntry> path, Tnc2Codec.Header read, DecodeContext ctx)
    {
        // A packet decoded with an empty destination (tolerated, with a warning) writes one again.
        bool clean = ctx.Diagnostics.TrueForAll(d => d.Severity == AprsDiagnosticSeverity.Info
            || (d.Code == AprsDiagnosticCode.EmptyDestination && destination.Value.Length == 0));
        if (!clean || read.Source.Value != source.Value || read.Destination.Value != destination.Value || read.Path.Count != path.Count)
        {
            return false;
        }

        for (int i = 0; i < path.Count; i++)
        {
            if (read.Path[i].Address.Value != path[i].Address.Value || read.Path[i].Used != path[i].Used)
            {
                return false;
            }
        }

        return true;
    }
}
