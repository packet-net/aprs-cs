using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Packet.Aprs.Tests.Vectors;

namespace Packet.Aprs.Corpus;

/// <summary>
/// Differential comparison against other implementations of the conformance vectors (aprs-vectors
/// README, "Comparing implementations"; its tools/compare.py compares any number of dumps).
/// <c>lines</c> extracts the capture once into a language-neutral file (one hex-encoded TNC2 line
/// per line, gzip), so every implementation decodes exactly the same bytes. <c>dump</c> decodes
/// each of those lines; <c>encode</c> encodes data in the neutral form (tools/generate.py);
/// <c>build</c> builds packets from builder recipes (tools/generate.py --recipes). Each writes one
/// JSON object per input line, gzip, in the input's order.
/// </summary>
internal static class DiffCommand
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private const string Usage = """
        diff lines <corpus-dir> <lines.hex.gz>
        diff dump <lines.hex.gz> <out.jsonl.gz> [--limit n]
        diff encode <data.jsonl.gz> <out.jsonl.gz> [--limit n]
        diff build <recipes.jsonl.gz> <out.jsonl.gz> [--limit n]
        """;

    public static int Run(string[] args)
    {
        long limit = long.MaxValue;
        int at = Array.IndexOf(args, "--limit");
        if (at >= 0)
        {
            if (at + 1 >= args.Length || !long.TryParse(args[at + 1], NumberStyles.None, CultureInfo.InvariantCulture, out limit))
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }

            args = [.. args[..at], .. args[(at + 2)..]];
        }

        switch (args.FirstOrDefault())
        {
            case "lines" when args.Length == 3:
                return Lines(args[1], args[2]);
            case "dump" when args.Length == 3:
                return Each(args[1], args[2], limit, "packets", (n, line) => DecodeRecord(n, Convert.FromHexString(line)));
            case "encode" when args.Length == 3:
                return Each(args[1], args[2], limit, "data", (n, line) => Parsed(line) is { } o ? EchoN(EncodeRecord(n, o), o) : TooDeep(n));
            case "build" when args.Length == 3:
                return Each(args[1], args[2], limit, "recipes", (n, line) => Parsed(line) is { } o ? EchoN(DiffBuild.Record(n, o), o) : TooDeep(n));
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    /// <summary>A record carries the input's own <c>n</c> when it has one, so a rerun of a subset of a set still lines up with the set.</summary>
    private static JsonObject EchoN(JsonObject record, JsonObject input)
    {
        if (input["n"] is JsonValue n)
        {
            record["n"] = n.DeepClone();
        }

        return record;
    }

    /// <summary>Every packet line from the completed hourly files (a .partial file is still being written).</summary>
    private static int Lines(string corpusDir, string output)
    {
        IEnumerable<string> files = Directory.EnumerateFiles(corpusDir, "aprsis-*.txt.gz", SearchOption.AllDirectories).Order(StringComparer.Ordinal);
        using var gz = new GZipStream(File.Create(output), CompressionLevel.Fastest);
        using var w = new StreamWriter(gz, new UTF8Encoding(false)) { NewLine = "\n" };
        long n = 0;
        int fileCount = 0;
        foreach (string file in files)
        {
            fileCount++;
            foreach (CorpusReader.Record r in CorpusReader.ReadFile(file))
            {
                w.WriteLine(Convert.ToHexString(r.Line));
                n++;
            }
        }

        Console.WriteLine($"{output}: {n:N0} lines from {fileCount} files");
        return 0;
    }

    /// <summary>One output record per input line (numbered from 0), up to <paramref name="limit"/> lines.</summary>
    private static int Each(string input, string output, long limit, string what, Func<long, string, JsonObject> record)
    {
        using var inGz = new GZipStream(File.OpenRead(input), CompressionMode.Decompress);
        using var reader = new StreamReader(inGz, new UTF8Encoding(false));
        using var outGz = new GZipStream(File.Create(output), CompressionLevel.Fastest);
        using var w = new StreamWriter(outGz, new UTF8Encoding(false)) { NewLine = "\n" };
        long n = 0;
        while (n < limit && reader.ReadLine() is { } line)
        {
            w.WriteLine(record(n, line).ToJsonString(Compact));
            n++;
        }

        Console.WriteLine($"{output}: {n:N0} {what}");
        return 0;
    }

    // ------------------------------------------------------------------ decode

    /// <summary>A line of JSON, or null when it nests deeper than the reader takes (a third-party chain thousands deep).</summary>
    private static JsonObject? Parsed(string line)
    {
        try
        {
            bool nonFinite = false;
            string text = NonFiniteToStrings(line, ref nonFinite);
            JsonObject o = JsonNode.Parse(text, documentOptions: new System.Text.Json.JsonDocumentOptions { MaxDepth = 256 })!.AsObject();
            if (nonFinite)
            {
                RestoreNonFinite(o);
            }

            return o;
        }
        catch (System.Text.Json.JsonException ex) when (ex.Message.Contains("depth", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    private const string NonFiniteMark = "\u0001\u0002nonfinite:";

    /// <summary>
    /// The generator's <c>--nonfinite</c> lines hold the non-standard tokens <c>NaN</c>,
    /// <c>Infinity</c> and <c>-Infinity</c>, which System.Text.Json does not read: outside
    /// strings they become marked strings, turned back into numbers by <see cref="RestoreNonFinite"/>.
    /// </summary>
    private static string NonFiniteToStrings(string line, ref bool found)
    {
        if (!line.Contains("NaN", StringComparison.Ordinal) && !line.Contains("Infinity", StringComparison.Ordinal))
        {
            return line;
        }

        var sb = new System.Text.StringBuilder(line.Length + 16);
        bool inString = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inString)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < line.Length)
                {
                    sb.Append(line[++i]);
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            string? token = c == 'N' && string.CompareOrdinal(line, i, "NaN", 0, 3) == 0 ? "NaN"
                : c == 'I' && string.CompareOrdinal(line, i, "Infinity", 0, 8) == 0 ? "Infinity"
                : c == '-' && string.CompareOrdinal(line, i, "-Infinity", 0, 9) == 0 ? "-Infinity"
                : null;
            if (token is not null)
            {
                sb.Append("\"\\u0001\\u0002nonfinite:").Append(token).Append('"');
                i += token.Length - 1;
                found = true;
                continue;
            }

            inString = c == '"';
            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Turns the marked strings back into NaN and the infinities, without recursion.</summary>
    private static void RestoreNonFinite(JsonNode root)
    {
        var pending = new Stack<JsonNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            JsonNode node = pending.Pop();
            IEnumerable<(JsonNode? Child, Action<JsonNode> Set)> children = node switch
            {
                JsonObject o => [.. o.Select(kv => ((JsonNode?)kv.Value, (Action<JsonNode>)(v => o[kv.Key] = v)))],
                JsonArray a => [.. a.Select((v, i) => (v, (Action<JsonNode>)(x => a[i] = x)))],
                _ => [],
            };
            foreach ((JsonNode? child, Action<JsonNode> set) in children)
            {
                if (child is JsonValue v && v.TryGetValue(out string? s) && s.StartsWith(NonFiniteMark, StringComparison.Ordinal) && s[NonFiniteMark.Length..] is "NaN" or "Infinity" or "-Infinity")
                {
                    set(JsonValue.Create(s[NonFiniteMark.Length..] switch
                    {
                        "NaN" => double.NaN,
                        "Infinity" => double.PositiveInfinity,
                        _ => double.NegativeInfinity,
                    }));
                }
                else if (child is not null)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static JsonObject TooDeep(long n) => new()
    {
        ["n"] = n,
        ["result"] = "unsupported",
        ["reason"] = "the line nests deeper than the JSON reader takes (256 levels)",
    };

    private static JsonObject DecodeRecord(long n, byte[] line)
    {
        var o = new JsonObject
        {
            ["n"] = n,
            ["lenient"] = Result(line, AprsParseOptions.Lenient, out AprsPacket? lenient),
            ["strict"] = Result(line, AprsParseOptions.Strict, out _),
            ["reencode"] = Reencode(lenient, out byte[]? written, out AprsAddress? writtenDestination, out string? reencodeRefusal),
        };
        if (reencodeRefusal is not null)
        {
            o["reencode_refusal"] = reencodeRefusal;
        }

        if (written is not null)
        {
            o["written"] = Convert.ToHexStringLower(written);
        }

        if (writtenDestination is not null)
        {
            o["written_destination"] = writtenDestination.ToString();
        }

        if (lenient is not null)
        {
            o["api"] = ApiView.Of(lenient);
        }

        return o;
    }

    /// <summary>The header, data and diagnostics, or the header error's diagnostics.</summary>
    private static JsonObject Result(byte[] line, AprsParseOptions options, out AprsPacket? packet)
    {
        try
        {
            packet = AprsPacket.Decode(line, options);
        }
        catch (AprsFormatException ex)
        {
            packet = null;
            return new JsonObject { ["header_error"] = NeutralJson.Diagnostics(ex.Diagnostics) };
        }

        return Decoded(packet, withHeader: true);
    }

    /// <summary>A decoded packet in the neutral form: <c>header</c> (when asked for), <c>data</c> and <c>diagnostics</c>.</summary>
    internal static JsonObject Decoded(AprsPacket packet, bool withHeader)
    {
        var o = new JsonObject();
        if (withHeader)
        {
            o["header"] = NeutralJson.Header(packet);
        }

        o["data"] = NeutralJson.Data(packet.Data);
        o["diagnostics"] = NeutralJson.Diagnostics(packet.Diagnostics);
        return o;
    }

    /// <summary>
    /// Adds the refusal reason code, as the vectors' kebab-case id, when the exception is the library's
    /// own refusal; an <see cref="InvalidOperationException"/> (a builder missing input) has none.
    /// </summary>
    internal static void AddRefusal(JsonObject o, string key, Exception ex)
    {
        if (ex is AprsEncodeException refusal)
        {
            o[key] = NeutralJson.Kebab(refusal.Code.ToString());
        }
    }

    /// <summary>
    /// What encoding the lenient data again gives, as the vectors' reencode check sees it:
    /// <c>identical</c>, <c>equivalent</c> (decodes, leniently, to the same data with no warnings or
    /// errors), <c>refused</c>, <c>fails</c> (it wrote something that does not), or <c>none</c> (nothing
    /// was decoded). <paramref name="written"/> is the information field it wrote, and
    /// <paramref name="writtenDestination"/> the Mic-E destination it computed.
    /// </summary>
    private static string Reencode(AprsPacket? packet, out byte[]? written, out AprsAddress? writtenDestination, out string? refusal)
    {
        refusal = null;
        written = null;
        writtenDestination = null;
        if (packet is null || packet.Data is AprsUnrecognizedData)
        {
            return "none";
        }

        byte[] info;
        AprsAddress destination = packet.Destination;
        try
        {
            if (packet.Data is AprsMicEReport mic)
            {
                AprsPacket created = AprsPacket.Create(packet.Source, mic, null, packet.Path);
                info = created.Information.ToArray();
                destination = created.Destination;
                writtenDestination = destination;
            }
            else
            {
                info = packet.Data.ToInformationField();
            }
        }
        catch (AprsEncodeException ex)
        {
            refusal = NeutralJson.Kebab(ex.Code.ToString());
            return "refused";
        }

        written = info;
        byte[] original = packet.Information.ToArray();
        int end = original.Length;
        while (end > 0 && original[end - 1] is (byte)'\r' or (byte)'\n')
        {
            end--;
        }

        if (info.AsSpan().SequenceEqual(original.AsSpan(0, end)) && destination == packet.Destination)
        {
            return "identical";
        }

        AprsPacket again = AprsPacket.Decode(packet.Source, destination, packet.Path, info);
        bool same = JsonMatch.Differences(NeutralJson.Data(packet.Data), NeutralJson.Data(again.Data)).Count == 0;
        return same && !again.HasWarnings && !again.HasErrors ? "equivalent" : "fails";
    }

    // ------------------------------------------------------------------ encode

    private static readonly AprsAddress EncodeSource = AprsAddress.Parse("N0CALL");
    private static readonly AprsAddress EncodeDestination = AprsAddress.Parse("APZ001");

    /// <summary>
    /// <c>data</c> in the neutral form, read into Packet.Aprs data as the vectors' encode cases are,
    /// then encoded: <c>written</c> with the information field, the destination (computed for Mic-E)
    /// and what the field decodes to, leniently, under <c>N0CALL&gt;</c> and that destination;
    /// <c>refused</c> when the encoder declines; <c>unsupported</c> when the data cannot be held.
    /// </summary>
    private static JsonObject EncodeRecord(long n, JsonObject input)
    {
        var o = new JsonObject { ["n"] = n };
        JsonObject neutral = input["data"]!.AsObject();

        // A third-party packet's inner packet is built, and so encoded, as it is read: its data is
        // read first on its own, so that the encoder refusing it is told apart from data that
        // cannot be held.
        // An empty inner information field has no data to read on its own.
        bool thirdParty = neutral["packet"]?["data"] is JsonObject inner && (string?)neutral["type"] == "third-party";
        AprsData data;
        try
        {
            if (thirdParty)
            {
                NeutralReader.CheckNesting(neutral["packet"]!["data"]);
            }

            if (thirdParty && (string?)neutral["packet"]!["data"]!["type"] != "unrecognized")
            {
                _ = NeutralReader.Data(neutral["packet"]!["data"]!.AsObject());
            }

            data = NeutralReader.Data(neutral);
        }
        catch (Exception ex) when (thirdParty && ex is ArgumentException and not ArgumentNullException)
        {
            o["result"] = "refused";
            o["reason"] = $"the inner packet: {ex.Message}";
            if (ex is AprsEncodeException)
            {
                AddRefusal(o, "refusal", ex);
            }
            else
            {
                // The neutral reader's own refusals: an inner packet with nothing to encode (it was
                // not decoded), or one with no destination (a header an inner packet cannot have).
                o["refusal"] = (string?)neutral["packet"]!["data"]!["type"] == "unrecognized" ? "not-encodable" : "invalid-third-party-header";
            }

            return o;
        }
#pragma warning disable CA1031 // any failure to read the data is reported for that line alone
        catch (Exception ex)
#pragma warning restore CA1031
        {
            o["result"] = "unsupported";
            o["reason"] = $"the data cannot be held as Packet.Aprs data: {ex.Message}";
            return o;
        }

        byte[] info;
        AprsAddress destination = EncodeDestination;
        try
        {
            if (data is AprsMicEReport mic)
            {
                AprsPacket created = AprsPacket.Create(EncodeSource, mic);
                info = created.Information.ToArray();
                destination = created.Destination;
            }
            else
            {
                info = data.ToInformationField();
            }
        }
        catch (Exception ex) when (ex is AprsEncodeException or InvalidOperationException)
        {
            o["result"] = "refused";
            o["reason"] = ex.Message;
            AddRefusal(o, "refusal", ex);
            return o;
        }

        o["result"] = "written";
        o["info"] = Convert.ToHexStringLower(info);
        o["destination"] = destination.Value;
        AprsPacket again = AprsPacket.Decode(EncodeSource, destination, [], info, AprsParseOptions.Lenient);
        o["again"] = Decoded(again, withHeader: false);
        return o;
    }
}
