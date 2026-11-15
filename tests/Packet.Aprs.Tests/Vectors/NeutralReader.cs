using System.Globalization;
using System.Text.Json.Nodes;

namespace Packet.Aprs.Tests.Vectors;

/// <summary>
/// Builds Packet.Aprs data from the neutral form: the inverse of <see cref="NeutralJson"/>, for
/// encode cases. A field it does not know is an error rather than ignored, so a case can never
/// pass because part of it was skipped; <c>AprsVectorTests</c> checks that writing and reading
/// back gives the same neutral form for every decoded case.
/// </summary>
internal static class NeutralReader
{
    /// <summary>The <see cref="Exception.Data"/> key marking a refusal the reader or tool made, not the library's public API.</summary>
    internal const string ToolMade = "via-tool";

    /// <summary>A refusal made by the reader or tool itself; the dump records it with <c>"via": "tool"</c>.</summary>
    internal static AprsEncodeException ToolRefusal(AprsRefusalCode code, string reason)
    {
        var ex = new AprsEncodeException(code, reason);
        ex.Data[ToolMade] = true;
        return ex;
    }

    public static AprsData Data(JsonObject d)
    {
        string type = Str(d, "type");
        return type switch
        {
            "position" => Positioned(d, new AprsPositionReport
            {
                Position = default,
                Symbol = default,
                Timestamp = Timestamp(d["timestamp"]),
                MessagingCapable = Flag(d, "messaging"),
            }, "timestamp", "messaging"),
            "mic-e" => Positioned(d, new AprsMicEReport
            {
                Position = default,
                Symbol = default,
                MicEMessage = Enum<AprsMicEMessage>(Str(d, "mic_e_message")),
                IsOldData = Flag(d, "old_data"),
                TypeCode = OptChar(d, "type_code"),
                DeviceSuffix = OptStr(d, "device_suffix") ?? "",
                Locator = OptStr(d, "locator"),
                LegacyTelemetry = d["legacy_telemetry"] is JsonArray lt ? [.. lt.Select(v => Code(v)!.Value)] : null,
                DestinationSsid = Code(d["destination_ssid"]) ?? 0,
            }, "mic_e_message", "old_data", "type_code", "device_suffix", "locator", "legacy_telemetry", "destination_ssid"),
            "object" => Positioned(d, new AprsObjectReport
            {
                Position = default,
                Symbol = default,
                Name = Absent(d, "name"),
                IsKilled = Flag(d, "killed"),
                Timestamp = Timestamp(d["timestamp"]),
            }, "name", "killed", "timestamp"),
            "item" => Positioned(d, new AprsItemReport
            {
                Position = default,
                Symbol = default,
                Name = Absent(d, "name"),
                IsKilled = Flag(d, "killed"),
            }, "name", "killed"),
            "message" => Only(d, new AprsTextMessage { Addressee = Absent(d, "addressee"), Text = OptStr(d, "text") ?? "", ReplyAck = RawStr(d, "reply_ack"), MessageId = OptStr(d, "message_id") }, "addressee", "text", "reply_ack", "message_id"),
            "ack" => Only(d, new AprsMessageAck { Addressee = Absent(d, "addressee"), AcknowledgedId = Absent(d, "acked_id"), ReplyAck = RawStr(d, "reply_ack"), MessageId = OptStr(d, "message_id") }, "addressee", "acked_id", "reply_ack", "message_id"),
            "reject" => Only(d, new AprsMessageReject { Addressee = Absent(d, "addressee"), RejectedId = Absent(d, "rejected_id"), ReplyAck = RawStr(d, "reply_ack"), MessageId = OptStr(d, "message_id") }, "addressee", "rejected_id", "reply_ack", "message_id"),
            "bulletin" => Only(d, new AprsBulletin { Addressee = Absent(d, "addressee"), Text = OptStr(d, "text") ?? "", MessageId = OptStr(d, "message_id") }, "addressee", "text", "message_id"),
            "nws-bulletin" => Only(d, new AprsNwsBulletin { Addressee = Absent(d, "addressee"), Text = OptStr(d, "text") ?? "", MessageId = OptStr(d, "message_id") }, "addressee", "text", "message_id"),
            "telemetry-names" => Only(d, new AprsTelemetryParameterNames { Addressee = Absent(d, "addressee"), Names = Strings(d["names"]), MessageId = OptStr(d, "message_id") }, "addressee", "names", "message_id"),
            "telemetry-units" => Only(d, new AprsTelemetryUnits { Addressee = Absent(d, "addressee"), Units = Strings(d["units"]), MessageId = OptStr(d, "message_id") }, "addressee", "units", "message_id"),
            "telemetry-coefficients" => Only(d, new AprsTelemetryCoefficients { Addressee = Absent(d, "addressee"), Coefficients = [.. (d["coefficients"] as JsonArray ?? []).Select(v => (double)v!)], MessageId = OptStr(d, "message_id") }, "addressee", "coefficients", "message_id"),
            "telemetry-bits" => Only(d, new AprsTelemetryBitSense { Addressee = Absent(d, "addressee"), Bits = Str(d, "bits"), Project = OptStr(d, "project") ?? "", MessageId = OptStr(d, "message_id") }, "addressee", "bits", "project", "message_id"),
            "directed-query" => Only(d, new AprsDirectedQuery { Addressee = Absent(d, "addressee"), QueryType = Absent(d, "query_type"), Target = OptStr(d, "target"), MessageId = OptStr(d, "message_id") }, "addressee", "query_type", "target", "message_id"),
            "status" => Only(d, new AprsStatusReport
            {
                Timestamp = Timestamp(d["timestamp"]),
                Locator = OptStr(d, "locator"),
                Symbol = OptStr(d, "symbol") is { } s ? Symbol(s) : null,
                BeamHeading = d["beam"] is JsonObject b ? new AprsBeamHeading(Str(b, "heading_code")[0], Str(b, "power_code")[0]) : null,
                Text = OptStr(d, "text") ?? "",
            }, "timestamp", "locator", "symbol", "beam", "text"),
            "telemetry" => Only(d, new AprsTelemetryReport
            {
                Sequence = Absent(d, "sequence"),
                Analog = [.. (d["analog"] as JsonArray ?? []).Select(v => v is null ? (double?)null : (double)v)],
                Bits = OptStr(d, "bits"),
                Comment = OptStr(d, "comment") ?? "",
            }, "sequence", "analog", "bits", "comment"),
            "weather" => Only(d, new AprsWeatherReport { Timestamp = Timestamp(d["timestamp"])!.Value, Weather = Weather(d["weather"]!.AsObject()), Comment = OptStr(d, "comment") ?? "" }, "timestamp", "weather", "comment"),
            "raw-weather" => Only(d, new AprsRawWeatherReport { Format = Enum<AprsRawWeatherFormat>(Str(d, "format")), Data = OptStr(d, "data") ?? "" }, "format", "data"),
            "nmea" => Only(d, new AprsNmeaReport
            {
                Sentence = Str(d, "sentence"),
                Position = NmeaPosition(d),
                FixValid = OptStr(d, "fix") is { } fix ? fix == "valid" : null,
                CourseDegrees = (double?)d["course_degrees"],
                SpeedKnots = (double?)d["speed_knots"],
                AltitudeMetres = (double?)d["altitude_m"],
                Time = OptStr(d, "time") is { } t ? TimeOnly.ParseExact(t.Length > 16 ? t[..16] : t, "HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) : null,
                TimeText = OptStr(d, "time") is { } text ? text.Replace(":", "", StringComparison.Ordinal) : null,
                WaypointName = OptStr(d, "waypoint"),
                Comment = OptStr(d, "comment") ?? "",
            }, "sentence", "has_checksum", "latitude", "longitude", "fix", "course_degrees", "speed_knots", "altitude_m", "time", "waypoint", "comment"),
            "maidenhead-beacon" => Only(d, new AprsMaidenheadBeacon { Locator = Str(d, "locator"), Comment = OptStr(d, "comment") ?? "" }, "locator", "comment"),
            "query" => Only(d, new AprsGeneralQuery
            {
                QueryType = OptStr(d, "query_type") ?? "",
                Footprint = d["footprint"] is JsonObject f ? new AprsQueryFootprint((double)f["latitude"]!, (double)f["longitude"]!, (double)f["radius_miles"]!) : null,
            }, "query_type", "footprint"),
            "capabilities" => Only(d, new AprsStationCapabilities
            {
                Capabilities = [.. (d["capabilities"] as JsonArray ?? []).Select(p => p!.AsArray()).Select(p => new AprsCapability((string)p[0]!, p.Count > 1 ? (string?)p[1] : null))],
            }, "capabilities"),
            "third-party" => Only(d, ThirdParty(d["packet"]!.AsObject()), "packet"),
            "user-defined" => Only(d, new AprsUserDefinedData
            {
                UserId = Str(d, "user_id")[0],
                PacketType = Str(d, "packet_type")[0],
                Data = System.Text.Encoding.Latin1.GetBytes(OptStr(d, "data") ?? ""),
            }, "user_id", "packet_type", "data"),
            "test" => Only(d, new AprsTestData { Data = OptStr(d, "data") ?? "" }, "data"),
            "agrelo-df" => Only(d, Agrelo(d), "bearing_degrees", "quality"),
            _ => throw new NotSupportedException($"no neutral reader for '{type}' data"),
        };
    }

    /// <summary>
    /// An Agrelo report. The bearing is degrees, rounded to the nearest one like a course (half away
    /// from zero); the library then range-checks it. A value that is not a number is
    /// <c>not-a-number</c> whatever else is wrong, which the library's whole numbers cannot hold, so
    /// it is noted for <see cref="StandIns"/> and read as 0.
    /// </summary>
    /// <summary>
    /// The position an NMEA record gives. The data holds a position whole, so a record that gives
    /// only one of latitude and longitude (beside a sentence, which says the other) takes the other
    /// from the sentence, or from 0 where the sentence has none: whatever it takes, a field given
    /// that the sentence does not read back as is refused as <c>invalid-nmea</c>, as the rule says.
    /// </summary>
    private static AprsPosition? NmeaPosition(JsonObject d)
    {
        if (d["latitude"] is null && d["longitude"] is null)
        {
            return null;
        }

        AprsPosition? sentence = null;
        if (d["sentence"] is { } text)
        {
            byte[] info = [(byte)'$', .. System.Text.Encoding.Latin1.GetBytes((string)text!)];
            sentence = (AprsPacket.Decode(AprsAddress.Parse("N0CALL"), AprsAddress.Parse("APZ001"), [], info).Data as AprsNmeaReport)?.Position;
        }

        return new AprsPosition((double?)d["latitude"] ?? sentence?.Latitude ?? 0, (double?)d["longitude"] ?? sentence?.Longitude ?? 0);
    }

    private static AprsAgreloDfReport Agrelo(JsonObject d)
    {
        double bearing = (double)d["bearing_degrees"]!;
        double quality = (double)d["quality"]!;
        bool noNumber = false;
        if (!double.IsFinite(bearing))
        {
            noNumber = true;
            bearing = 0;
        }

        if (!double.IsFinite(quality))
        {
            noNumber = true;
            quality = 0;
        }

        if (noNumber)
        {
            StandIns.Note(AprsRefusalCode.NotANumber, "an Agrelo bearing or quality is not a number");
        }

        if (quality != Math.Floor(quality))
        {
            // A quality is a code, never rounded: outside what the field holds.
            StandIns.Note(AprsRefusalCode.ValueOutOfRange, $"quality {quality.ToString(CultureInfo.InvariantCulture)} is not a whole number");
            quality = Math.Floor(quality);
        }

        return new AprsAgreloDfReport { BearingDegrees = Clamp(Math.Round(bearing, MidpointRounding.AwayFromZero)), Quality = Clamp(quality) };

        static int Clamp(double v) => (int)Math.Clamp(v, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// Refuses third-party data nested more than four deep, counted from the top without
    /// recursion, so it is refused for its header before anything inside it is built (vectors
    /// encode/third-party-nested-five-deep). <paramref name="data"/> is the data inside one
    /// third-party packet, or any data at all.
    /// </summary>
    public static void CheckNesting(JsonNode? data)
    {
        int depth = 1;
        JsonNode? d = data;
        while (d is JsonObject o && (string?)o["type"] == "third-party")
        {
            depth++;
            d = o["packet"]?["data"];
        }

        // Undecoded data at the end is not-encodable, which comes first in the order.
        bool undecoded = d is JsonObject last && (string?)last["type"] == "unrecognized" && (string?)last["reason"] != "empty";
        if (depth > 4 && !undecoded)
        {
            throw ToolRefusal(AprsRefusalCode.InvalidThirdPartyHeader, "third-party packets nest at most four deep");
        }
    }

    private static AprsThirdPartyTraffic ThirdParty(JsonObject p)
    {
        CheckNesting(p["data"]);

        // The inner header is held as given, whatever it holds: a destination or path entry that is no
        // address is for the encoder to refuse (invalid-third-party-header), not for this reader.
        IEnumerable<AprsPathEntry> path = Strings(p["path"]).Select(e => e.EndsWith('*') ? new AprsPathEntry(AprsAddress.CreateUnchecked(e[..^1]), true) : new AprsPathEntry(AprsAddress.CreateUnchecked(e)));

        // An inner source is 1-9 printable ASCII characters, which need not be an address (a space is allowed), so it is held as given.
        AprsAddress source = AprsAddress.CreateUnchecked(Str(p, "source"));
        AprsAddress destination = AprsAddress.CreateUnchecked(OptStr(p, "destination") ?? throw ToolRefusal(AprsRefusalCode.InvalidThirdPartyHeader, "the third-party packet has no destination, which an inner header needs"));
        JsonObject data = p["data"]!.AsObject();

        // An empty inner information field is data too: it is written back as it came, empty
        // (vectors README, Encoding). Other unrecognized data carries no bytes to write, so the
        // encoder declines it.
        if (Str(data, "type") == "unrecognized" && OptStr(data, "reason") != "empty")
        {
            throw ToolRefusal(AprsRefusalCode.NotEncodable, "an unrecognized inner packet has nothing to encode");
        }

        if (Str(data, "type") == "unrecognized" && OptStr(data, "reason") == "empty")
        {
            return new AprsThirdPartyTraffic { Packet = AprsPacket.Decode(source, destination, path, []) };
        }

        // A Mic-E report's destination is computed from it; the neutral form's is that one.
        AprsData inner = Data(data);
        AprsThirdPartyTraffic traffic = AprsThirdPartyTraffic.Create(source, inner, inner is AprsMicEReport ? null : destination, path);

        // Diagnostics on the inner packet are part of its data (a tolerated header defect such as
        // several used markers): the packet is the one that reads with them, its header as
        // given, which the encoder then refuses (vectors encode/third-party-inner-header-with-a-
        // tolerated-defect).
        if (Strings(p["diagnostics"]).Any(d => !d.StartsWith("info:", StringComparison.Ordinal)))
        {
            string header = $"{Str(p, "source")}>{traffic.Packet.Destination.Value}{string.Concat(Strings(p["path"]).Select(e => "," + e))}:";
            byte[] line = [.. System.Text.Encoding.UTF8.GetBytes(header), .. traffic.Packet.Information.ToArray()];
            traffic = new AprsThirdPartyTraffic { Packet = AprsPacket.Decode(line, AprsParseOptions.Lenient) };

            // A warning or error the data gives that its written bytes do not read back with has no
            // form (vectors encode/third-party-inner-warning-the-bytes-do-not-carry): the data
            // cannot be held as an inner packet, which only decoded bytes give diagnostics to.
            List<string> found = [.. traffic.Packet.Diagnostics.Where(d => d.Severity != AprsDiagnosticSeverity.Info).Select(d => NeutralJson.Diagnostic(d.Severity, d.Code))];
            foreach (string given in Strings(p["diagnostics"]).Where(d => !d.StartsWith("info:", StringComparison.Ordinal)))
            {
                if (!found.Remove(given))
                {
                    throw ToolRefusal(AprsRefusalCode.ValueHasNoForm, $"the inner packet is given the diagnostic {given}, which its written bytes do not read back with");
                }
            }
        }

        return traffic;
    }

    private static T Positioned<T>(JsonObject d, T data, params string[] ownFields)
        where T : AprsPositionedData
    {
        string[] known = ["latitude", "longitude", "ambiguity", "symbol", "compressed", "compression", "course_degrees", "speed_knots", "altitude_feet", "phg", "range_miles", "dfs", "area", "df_bearing", "storm", "dao", "telemetry", "frequency", "weather", "signpost", "comment"];
        Only(d, data, [.. ownFields, .. known]);
        return data with
        {
            Position = new AprsPosition((double)d["latitude"]!, (double)d["longitude"]!, Code(d["ambiguity"]) ?? 0),
            Symbol = Symbol(Absent(d, "symbol")),
            IsCompressed = Flag(d, "compressed"),
            CompressionType = d["compression"] is JsonObject ct
                ? new AprsCompressionType(Enum<AprsCompressionFix>(Str(ct, "fix")), Enum<AprsCompressionSource>(Str(ct, "source")), Enum<AprsCompressionOrigin>(Str(ct, "origin")))
                : null,
            CourseDegrees = (double?)d["course_degrees"],
            SpeedKnots = (double?)d["speed_knots"],
            AltitudeFeet = (double?)d["altitude_feet"],
            Phg = d["phg"] is JsonObject phg ? new AprsPhg(Code(phg["power"])!.Value, Code(phg["height"])!.Value, Code(phg["gain"])!.Value, Code(phg["directivity"])!.Value, Code(phg["beacons_per_hour"])) : null,
            RangeMiles = (double?)d["range_miles"],
            DfSignalStrength = d["dfs"] is JsonObject dfs ? new AprsDfSignalStrength(Code(dfs["strength"])!.Value, Code(dfs["height"])!.Value, Code(dfs["gain"])!.Value, Code(dfs["directivity"])!.Value) : null,
            AreaObject = d["area"] is JsonObject a
                ? new AprsAreaObject(Enum<AprsAreaShape>(Str(a, "shape")), Enum<AprsAreaColor>(Str(a, "color")), Code(a["lat_offset"])!.Value, Code(a["lon_offset"])!.Value, Code(a["corridor_width_miles"]))
                : null,
            DfBearing = d["df_bearing"] is JsonObject b ? new AprsDfBearing(Code(b["bearing_degrees"])!.Value, Code(b["number"])!.Value, Code(b["range"])!.Value, Code(b["quality"])!.Value) : null,
            Storm = d["storm"] is JsonObject s
                ? new AprsStorm
                {
                    Type = Enum<AprsStormType>(Str(s, "type")),
                    SustainedWindKnots = Code(s["sustained_wind_knots"]),
                    GustKnots = Code(s["gust_knots"]),
                    CentralPressureMillibars = Code(s["central_pressure_mbar"]),
                    HurricaneWindRadiusNauticalMiles = Code(s["hurricane_radius_nm"]),
                    TropicalStormWindRadiusNauticalMiles = Code(s["tropical_storm_radius_nm"]),
                    WholeGaleRadiusNauticalMiles = Code(s["whole_gale_radius_nm"]),
                }
                : null,
            Dao = d["dao"] is JsonObject dao ? new AprsDao(Str(dao, "datum")[0], Enum<AprsDaoPrecision>(Str(dao, "precision"))) : null,
            Telemetry = d["telemetry"] is JsonObject t
                ? new AprsCommentTelemetry { Sequence = Code(t["sequence"])!.Value, Analog = [.. t["analog"]!.AsArray().Select(v => Code(v)!.Value)], Digital = (byte?)Code(t["digital"]) }
                : null,
            Frequency = d["frequency"] is JsonObject f
                ? new AprsVoiceFrequency
                {
                    FrequencyMHz = (double)f["mhz"]!,
                    ToneType = OptStr(f, "tone") is { } tone ? Enum<AprsToneType>(tone) : null,
                    ToneValue = Code(f["tone_value"]),
                    OffsetKHz = Code(f["offset_khz"]),
                    Range = Code(f["range"]),
                    RangeInKilometres = Flag(f, "range_km"),
                    Narrow = Flag(f, "narrow"),
                    TenKilohertzResolution = Flag(f, "ten_khz_resolution"),
                }
                : null,
            Weather = d["weather"] is JsonObject w ? Weather(w) : null,
            SignpostText = OptStr(d, "signpost"),
            Comment = OptStr(d, "comment") ?? "",
        };
    }

    private static AprsWeather Weather(JsonObject w) => new()
    {
        WindDirectionDegrees = (double?)w["wind_direction_degrees"],
        WindSpeedMph = (double?)w["wind_speed_mph"],
        WindGustMph = (double?)w["wind_gust_mph"],
        TemperatureFahrenheit = (double?)w["temperature_f"],
        RainLastHourInches = (double?)w["rain_1h_in"],
        RainLast24HoursInches = (double?)w["rain_24h_in"],
        RainSinceMidnightInches = (double?)w["rain_midnight_in"],
        RainRawCounter = Code(w["rain_raw"]),
        HumidityPercent = (double?)w["humidity_percent"],
        PressureMillibars = (double?)w["pressure_mbar"],
        LuminosityWattsPerSquareMetre = (double?)w["luminosity_w_m2"],
        SnowfallLast24HoursInches = (double?)w["snow_24h_in"],
        SoftwareType = OptChar(w, "software"),
        UnitType = OptStr(w, "unit"),
        AdditionalFields = w["extra"] is JsonArray extra ? [.. extra.Select(e => new AprsWeatherField(Str(e!.AsObject(), "letter")[0], Str(e.AsObject(), "value")))] : [],
    };

    /// <summary>On-air timestamp text: DDHHMMz, DDHHMM/, HHMMSSh or MMDDHHMM. Out-of-range fields are kept, as the decoder keeps them.</summary>
    private static AprsTimestamp? Timestamp(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        string s = (string)node!;
        int F(int at) => int.Parse(s.AsSpan(at, 2), CultureInfo.InvariantCulture);
        return s.Length == 8
            ? AprsTimestamp.Unchecked(AprsTimestampFormat.MonthDayHoursMinutesUtc, F(0), F(2), F(4), F(6), 0)
            : s[6] switch
            {
                'z' => AprsTimestamp.Unchecked(AprsTimestampFormat.DayHoursMinutesUtc, 0, F(0), F(2), F(4), 0),
                '/' => AprsTimestamp.Unchecked(AprsTimestampFormat.DayHoursMinutesLocal, 0, F(0), F(2), F(4), 0),
                'h' => AprsTimestamp.Unchecked(AprsTimestampFormat.HoursMinutesSecondsUtc, 0, 0, F(0), F(2), F(4)),
                _ => throw new FormatException($"'{s}' is not an on-air timestamp"),
            };
    }

    /// <summary>
    /// A number for a field Packet.Aprs holds in whole units or as a code (a PHG or DFS digit, an
    /// area offset, a DF bearing field, a storm field, a comment telemetry value, a raw rain counter,
    /// an ambiguity): never rounded, since rounding is for the step fields the Encoding rule lists.
    /// The data cannot hold a non-integer there, so it is noted for <see cref="StandIns"/> as
    /// <c>value-out-of-range</c> and read as the whole number below it; a value past 32 bits is
    /// clamped, for the encoder to refuse.
    /// </summary>
    private static int? Code(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        double value = (double)node;
        if (value != Math.Floor(value))
        {
            StandIns.Note(AprsRefusalCode.ValueOutOfRange, $"{node.ToJsonString()} is not a whole number, and this field holds only whole units or a code");
            value = Math.Floor(value);
        }

        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// Two characters, table and code. Any other length is held as one the encoder refuses
    /// (invalid-symbol), with a NUL for each character missing and none of any extra.
    /// </summary>
    private static AprsSymbol Symbol(string s) =>
        s.Length == 2 ? new(s[0], s[1]) : new(s.Length > 0 ? s[0] : '\0', '\0');

    private static T Enum<T>(string kebab)
        where T : struct, Enum => System.Enum.Parse<T>(kebab.Replace("-", "", StringComparison.Ordinal), ignoreCase: true);

    private static bool Flag(JsonObject d, string name) => (bool?)d[name] ?? false;

    private static string Str(JsonObject d, string name) => (string?)d[name] ?? throw new FormatException($"'{name}' is required");

    private static string? OptStr(JsonObject d, string name) => (string?)d[name];

    /// <summary>
    /// A text field the data must have, held as empty when it is left out, so the encoder refuses
    /// it for what it is (an addressee of the wrong length, a missing query type, a sequence that
    /// is not the form, no symbol) rather than the data not being held at all.
    /// </summary>
    private static string Absent(JsonObject d, string name) => (string?)d[name] ?? "";

    /// <summary>A string where empty is meaningful (reply_ack).</summary>
    private static string? RawStr(JsonObject d, string name) => d.ContainsKey(name) ? (string?)d[name] : null;

    private static char? OptChar(JsonObject d, string name) => OptStr(d, name) is { Length: > 0 } s ? s[0] : null;

    private static string[] Strings(JsonNode? node) => node is JsonArray a ? [.. a.Select(v => (string)v!)] : [];

    private static T Only<T>(JsonObject d, T data, params string[] known)
    {
        string[] unknown = [.. d.Select(kv => kv.Key).Where(k => k != "type").Except(known)];
        return unknown.Length == 0 ? data : throw new NotSupportedException($"unknown field(s) {string.Join(", ", unknown)} for '{(string?)d["type"]}'");
    }
}
