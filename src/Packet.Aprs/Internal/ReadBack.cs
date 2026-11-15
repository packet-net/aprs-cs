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

    /// <summary>
    /// Whether the rules attach the info <paramref name="code"/> to the form the data itself asks for
    /// (README, Read-back, Diagnostics): <c>obsolete-format</c> on a positionless weather report, raw
    /// weather, raw NMEA, a Maidenhead beacon and Mic-E Rev 0 telemetry; <c>mic-e-missing-device-type</c>
    /// on Mic-E status text; <c>invalid-query</c> and <c>invalid-telemetry-metadata</c> on a message
    /// the rules read as a plain message. An info on any other form is a change.
    /// </summary>
    private static bool InfoAllowed(AprsData given, AprsDiagnosticCode code) => code switch
    {
        AprsDiagnosticCode.ObsoleteFormat => given is AprsWeatherReport or AprsRawWeatherReport or AprsNmeaReport or AprsMaidenheadBeacon
            || given is AprsMicEReport { LegacyTelemetry: not null },
        AprsDiagnosticCode.MicEMissingDeviceType => given is AprsMicEReport,
        AprsDiagnosticCode.InvalidQuery or AprsDiagnosticCode.InvalidTelemetryMetadata => given is AprsTextMessage,
        _ => false,
    };

    /// <summary>
    /// A seam for the tests, like <see cref="InfoEncoder.AfterWriting"/>: changes the diagnostics the
    /// read-back decode gave, so a test can show that a warning, an error or an info no decode of
    /// ours gives (an info outside the listed four) is refused. Null, and thread-local, outside such a test.
    /// </summary>
    [ThreadStatic]
    internal static Func<IReadOnlyList<AprsDiagnostic>, IReadOnlyList<AprsDiagnostic>>? AfterDecoding;

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
    public static Mismatch? Check(AprsData given, byte[] info, bool? altitudeAsFeet = null, AprsAddress? written = null)
    {
        // The field is read back under the destination actually written (a Mic-E report under the one
        // it computes), and an information field written alone under N0CALL>APZ001: the header changes
        // a reading only through a Mic-E destination.
        // A Mic-E report written under a destination other than the one its data computes is read back
        // under that one, and any difference in the data (latitude digits, message bits, N/S, longitude
        // offset, W/E, the destination SSID) is a report that is not the one held: not-encodable.
        AprsAddress computed = given is AprsMicEReport micReport ? MicECodec.EncodeDestination(micReport) : default;
        bool otherMicEDestination = given is AprsMicEReport && written is { } other && other.Value != computed.Value;
        AprsAddress destination = given is AprsMicEReport ? (otherMicEDestination ? written!.Value : computed) : written ?? DefaultDestination;
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

            return bad.Count == 0 ? null : new Mismatch(back.Data is AprsMicEReport ? AprsRefusalCode.NotEncodable : AprsRefusalCode.DoesNotReadBack, bad);
        }

        // Diagnostics: no warning, no error, and of infos only those the rules attach to the form the data asks for.
        foreach (AprsDiagnostic d in AfterDecoding?.Invoke(back.Diagnostics) ?? back.Diagnostics)
        {
            if (d.Severity != AprsDiagnosticSeverity.Info || !InfoAllowed(given, d.Code))
            {
                bad.Add($"{Neutral.Kebab(d.Severity.ToString())}:{Neutral.Kebab(d.Code.ToString())}");
            }
        }

        // An inner packet's warnings and errors are data: they must be exactly those the data gives
        // (a multiset), at every depth, and its infos, given or decoded, are not compared. Data decoded
        // from bytes is written as received and reads back with its own; one built by an encoder has
        // none. A warning or error the data gives that its bytes do not read back with has no form.
        bool lost = false;
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
                List<string> missing = Minus(carried, found);
                lost |= missing.Count > 0;
                bad.Add("inner diagnostics: " + string.Join(", ", missing.Count > 0 ? missing.Select(m => "missing " + m) : Minus(found, carried)));
            }
        }

        Dictionary<string, object> expected = Neutral.Given(given, altitudeAsFeet);

        // The one place every number of the data is seen, whatever its type or depth: a number that is
        // not a number never compares equal to a finite one, nor as absent, and is refused as what it is
        // (each field's own check says so first; this holds for a field a check forgot).
        if (expected.Values.Any(v => v is double d && !double.IsFinite(d)))
        {
            return new Mismatch(AprsRefusalCode.NotANumber, [.. expected.Where(kv => kv.Value is double d && !double.IsFinite(d)).Select(kv => kv.Key)]);
        }

        Dictionary<string, object> read = Neutral.Decoded(back.Data);
        Allowances(given, expected, read);
        Compare(expected, read, bad);
        if (bad.Count > 0 && (otherMicEDestination || (back.Data is AprsMicEReport && given is not AprsMicEReport)))
        {
            // Under this destination the field reads as a Mic-E position: nothing the call can write is the data.
            return new Mismatch(AprsRefusalCode.NotEncodable, bad);
        }

        return bad.Count == 0 ? null : new Mismatch(lost ? AprsRefusalCode.ValueHasNoForm : Code(given, back.Data, bad), bad);
    }

    /// <summary>The multiset <paramref name="a"/> less <paramref name="b"/>.</summary>
    private static List<string> Minus(List<string> a, List<string> b)
    {
        List<string> rest = [.. b];
        var left = new List<string>();
        foreach (string item in a)
        {
            if (!rest.Remove(item))
            {
                left.Add(item);
            }
        }

        return left;
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

        if (a is long la)
        {
            // An integer or a code is compared exactly.
            return b is long lb && la == lb;
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
    /// The faults of a header as given, whatever form is written and before any fault in the data
    /// (vectors README, Refusal reasons): an address that is not one is <c>invalid-address</c>, then
    /// a path with an entry not marked used before one that is is <c>invalid-path</c>. A header
    /// written on its own is read back as a user's decode of that form reads it, the outer header
    /// rules, whatever the packet's origin: an inner packet whose source is <c>a b</c> is not
    /// carried. An empty destination is a fault of a header given to a builder, but a held packet
    /// may carry one it was decoded with (<paramref name="allowEmptyDestination"/>).
    /// </summary>
    public static void RequireHeader(AprsAddress source, AprsAddress destination, IReadOnlyList<AprsPathEntry> path, bool allowEmptyDestination)
    {
        RequireAddress(source);
        if (!(allowEmptyDestination && string.IsNullOrEmpty(destination.Value)))
        {
            RequireAddress(destination);
        }

        foreach (AprsPathEntry entry in path)
        {
            RequireAddress(entry.Address);
        }

        RequirePathOrder(path);
    }

    private static void RequireAddress(AprsAddress address)
    {
        string a = address.Value ?? "";
        if (!Tnc2Codec.IsAprsIsAddress(a))
        {
            throw new AprsEncodeException(AprsRefusalCode.InvalidAddress, $"'{a}' is not an address: 1-9 letters, digits or - (vectors README, Addresses)");
        }
    }

    /// <summary>
    /// A path entry before a used one is used too, so a path with one not marked used reads back
    /// changed in a line and in a frame alike, and no decoder gives one: a fault of the header
    /// given, <c>invalid-path</c>, not of a form.
    /// </summary>
    public static void RequirePathOrder(IReadOnlyList<AprsPathEntry> path)
    {
        int lastUsed = -1;
        for (int i = 0; i < path.Count; i++)
        {
            if (path[i].Used)
            {
                lastUsed = i;
            }
        }

        for (int i = 0; i < lastUsed; i++)
        {
            if (!path[i].Used)
            {
                throw new AprsEncodeException(AprsRefusalCode.InvalidPath, $"the path {AprsPathEntry.FormatList(path)} has an entry not marked used before one that is: it would read back used, in a line or a frame");
            }
        }
    }

    /// <summary>
    /// A TNC2 line ends at a CR or LF, as APRS-IS reads it, so a line cannot carry an information
    /// field with one inside it. A line break at the very end is taken off by a decoder as
    /// <c>trailing-line-break</c>, ends the line where it ends anyway, and is carried.
    /// </summary>
    public static void CheckLineBreaks(ReadOnlySpan<byte> information)
    {
        int end = information.Length;
        while (end > 0 && information[end - 1] is (byte)'\r' or (byte)'\n')
        {
            end--;
        }

        if (information[..end].IndexOfAny((byte)'\r', (byte)'\n') >= 0)
        {
            throw new AprsEncodeException(AprsRefusalCode.Tnc2CannotCarry, "a TNC2 line ends at a CR or LF, so it cannot carry an information field with one inside it");
        }
    }

    /// <summary>
    /// Reads back the header of a TNC2 line: the source, destination and path, entry by entry with
    /// its used mark, as given, by the outer header rules.
    /// </summary>
    public static void CheckTnc2Header(AprsAddress source, AprsAddress destination, IReadOnlyList<AprsPathEntry> path, byte[] line)
    {
        int colon = Array.IndexOf(line, (byte)':');
        var ctx = new DecodeContext(AprsParseOptions.Lenient);
        if (colon < 0 || !Tnc2Codec.TryParseHeader(line.AsSpan(0, colon), ctx, out Tnc2Codec.Header header)
            || !SameHeader(source, destination, path, header, ctx))
        {
            throw new AprsEncodeException(AprsRefusalCode.Tnc2CannotCarry, HeaderRefusal("a TNC2 line", path));
        }
    }

    /// <summary>The same for an AX.25 frame's addresses, a digipeater being used when its H bit is set or one after it is.</summary>
    public static void CheckAx25Header(AprsAddress source, AprsAddress destination, IReadOnlyList<AprsPathEntry> path, byte[] frame)
    {
        var ctx = new DecodeContext(AprsParseOptions.Lenient);
        if (!Ax25Codec.TryDecode(frame, ctx, out Tnc2Codec.Header header, out _)
            || !SameHeader(source, destination, path, header, ctx))
        {
            throw new AprsEncodeException(AprsRefusalCode.Ax25CannotCarry, HeaderRefusal("an AX.25 frame", path));
        }
    }

    private static string HeaderRefusal(string form, IReadOnlyList<AprsPathEntry> path) =>
        $"{form} cannot carry this header (path {AprsPathEntry.FormatList(path)}): it would read back as a different header";

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
