# Changelog

## 1.0.1 (2026-11-12)

Refusals carry a reason code.

- `AprsEncodeException` has a new read-only `Code` property of the new enum `AprsRefusalCode`: the shared reason codes of aprs-vectors (`codes.json` `refusals`), 26 of them, the last, `DoesNotReadBack`, being the fallback. Every refusal carries the code its rule maps to. Nothing is removed or renamed: the exception type, its message text and every 1.0 signature are as they were.
- `aprs-corpus diff` writes the code as `refusal` on a refused encode or build record, `reencode_refusal` on a decode record whose re-encode was refused, and `ax25_refusal` in the API view. The vectors runner knows the `refusal` and `reencode_refusal` case keys and checks them when a case names one.
- The submodule moves to the aprs-vectors `refusal-codes` branch.

## 1.0.0 (2026-11-10)

This is the first stable release. The public API is now frozen under semantic versioning. From 1.0 the major and minor version are shared with aprs-vectors: a ruling round is a minor version for all five implementations, this library's own fixes are patch versions, and an API break is a major version.

The 1.0 API cleanup. Every item below is a breaking change, with no deprecation shims: the names and shapes here are the ones 1.0 keeps. The wire output is unchanged: the encoder writes the same bytes and the decoder reads the same data, apart from the number formats noted under "Numbers kept as sent".

### Breaking changes

Refusals

- Every refusal from the encoder, the builders and the text address parsers is now an `AprsEncodeException` (an `ArgumentException`, so `catch (ArgumentException)` still works). It carries a reason in `Message` and no longer names an internal parameter (`ParamName` is null). A null argument is still an `ArgumentNullException`, and a builder missing something its packet needs (a position, a symbol) still throws `InvalidOperationException`. This includes `Telemetry(int)` out of 0-999, a telemetry builder given a bad value, and `AprsStation.From`, `To`, `Via` and the string `AprsPacket.Create` given text that is not an address (was `FormatException`).

Creating packets

- `AprsPacket.Create` is now `Create(AprsAddress source, AprsData data, AprsAddress? destination = null, IEnumerable<AprsPathEntry>? path = null)` and `Create(string source, AprsData data, string? destination = null, string path = "")`: the data comes before the destination, and a null destination is `AprsStation.DefaultDestination`. Migrate `Create(source, destination, data)` to `Create(source, data, destination)`.
- `AprsPacket.CreateMicE` is gone: `Create` computes a Mic-E report's destination, and a destination given with a Mic-E report is refused. Migrate `CreateMicE(source, report, path)` to `Create(source, report, null, path)`.
- `AprsData.ToInformationField()` now refuses a Mic-E report (it dropped the destination, which holds half the position); make a packet with `Create` and read `Information`.
- The static class `Aprs` is gone (it clashed with its own namespace). Use `AprsStation.From(...)` and `AprsStation.DefaultDestination`.
- `AprsBuilder.Build()` is no longer virtual.

Builder names: a setter has the name of the field it fills, and the one that converts changes only the unit

- Positioned builders: `Course` is `CourseDegrees`, `Speed` is `SpeedKnots`, `Altitude` is `AltitudeFeet`, `Range` is `RangeMiles`. `SpeedKmh` and `AltitudeMetres` stay.
- `Frequency(double mhz, double? tone, int? offsetKHz)` is `Frequency(double mhz, AprsToneType? tone = null, int? toneValue = null, int? offsetKHz = null)`: the tone is a kind and its whole value as sent (a CTCSS tone, a DCS code), not Hz. The frequency is rounded once to its step (1 kHz, or 10 kHz) instead of being refused for extra decimals.
- Weather builder: `Wind(direction, speed, gust)` is three setters, `WindDirectionDegrees`, `WindSpeedMph` and `WindGustMph`; `Gust` is `WindGustMph`, `Temperature` is `TemperatureFahrenheit`, `Humidity` is `HumidityPercent`, `Pressure` is `PressureMillibars`, `Luminosity` is `LuminosityWattsPerSquareMetre`, `Snowfall(decimal)` is `SnowfallLast24HoursInches(double)`. `Rain(...)` and `RainMillimetres(...)` are one setter per value: `RainLastHourInches`, `RainLast24HoursInches`, `RainSinceMidnightInches` and the same with `Millimetres`. `TemperatureCelsius` stays.
- `AprsMessageBuilder.WithId` is `MessageId`.
- `AprsObjectBuilder.Kill()` and `AprsItemBuilder.Kill()` are `Killed(bool killed = true)`, so a builder that was killed can be brought back.
- `AprsMicEBuilder.Message` is `MicEMessage`.
- `AprsStatusBuilder.Locator(locator, symbol)` is two setters, `Locator(locator)` and `Symbol(symbol)`.
- `AprsTelemetryBuilder.Digital(byte)` and `Digital(string)` are `Bits(string)` (eight 0 or 1 characters, B1 first); `Analog(params decimal[])` and `Analog(params decimal?[])` are `Analog(params double?[])`.
- `AprsStation.TelemetryNames`, `TelemetryUnits`, `TelemetryCoefficients` and `TelemetryBits` return an `AprsTelemetryMetadataBuilder`, whose `Addressee(string)` sets the station the metadata is for (the sender by default). `TelemetryBits(byte bits, string projectTitle)` is `TelemetryBits(string bits, string project = "")`.
- `AprsBuilder` documents that builders change themselves and return themselves.

Data

- Telemetry values and coefficients are doubles, with their text as sent beside them: `AprsTelemetryReport.Analog` is `IReadOnlyList<double?>` (was `decimal?`) with `AnalogText`; `AprsTelemetryCoefficients` gains `CoefficientsText`; `AprsQueryFootprint.Latitude` and `Longitude` are `double` (were `decimal`) with `LatitudeText` and `LongitudeText`, and `RadiusMiles` is `double` (was `int`). `AprsTelemetryCoefficients.Scale` takes a `double`. The text is written back for any entry that still means the same number, and is not part of equality.
- Digital bits are the text as sent: `AprsTelemetryReport.Digital` (`byte?`) is `Bits` (`string?`, eight 0 or 1 characters, B1 first), and `AprsTelemetryBitSense.Bits` is a `string` (was `byte`). Migrate a byte `b` to the text with `string.Concat(Enumerable.Range(0, 8).Select(i => (b >> i & 1) == 1 ? '1' : '0'))`.
- `AprsVoiceFrequency.FrequencyMHz` is `double` (was `decimal`), rounded once when encoded. `AprsWeather.SnowfallLast24HoursInches` is `double?` (was `decimal?`).
- `AprsObjectReport.IsAlive` and `AprsItemReport.IsAlive` (default true) are `IsKilled` (default false). `AprsMicEReport.IsCurrent` (default true) is `IsOldData` (default false). `AprsMicEReport.Message` is `MicEMessage`.
- `AprsMicEReport.AltitudeInComment` is public (was an internal property): set by the decoder when the altitude came from a `/A=`, and honoured by the encoder, so writing a decoded report again gives the same bytes.
- `AprsPathEntry.HasBeenRepeated` is `Used`. `AprsTelemetryBitSense.ProjectTitle` is `Project`. `AprsPositionedData.RadioRangeMiles` is `RangeMiles`. `AprsMicEReport.MaidenheadLocator` and `AprsStatusReport.MaidenheadLocator` are `Locator`. `AprsUserDefinedData.DataText` is `Text`. `AprsPhg.GainDbi` is `GainDb`.
- `AprsGpsFix` is `AprsCompressionFix` and `AprsNmeaSource` is `AprsCompressionSource`.
- `AprsParseOptions` flags are all `Allow` plus the diagnostic code name: `StripTrailingLineBreaks` is `AllowTrailingLineBreak`, `RecognizeDataExtensionInComment` is `AllowDataExtensionInComment`, `AllowShortObjectName` is `AllowObjectNameNotPadded`, `AllowIncompleteTelemetry` is `AllowInvalidTelemetry`, `AllowNonStandardWeatherFieldWidths` is `AllowNonStandardWeatherFieldWidth`, `AllowWindFieldsInPositionWeather` is `AllowWindFieldsInsteadOfExtension`, `AllowMicEAltitudeAnywhere` is `AllowMicEAltitudeNotFirst` and `AllowOutOfRangeValues` is `AllowOutOfRangeValue`.
- `AprsAddress.MaxLength` is internal, and `AprsSymbolTable.Describe` is gone (use `AprsSymbol.Description`).

### Numbers kept as sent

- A decoded telemetry value, coefficient or footprint coordinate now prints as the shortest double (`0` where it was `0.00` when held as a decimal); the text as sent is still written back unchanged.
- A whole-number field that the encoder rounds (course, wind direction, humidity, luminosity, footprint radius) is documented as whole when decoded and rounded once when encoded.

### Other

- New `AprsEncodeException`, `AprsTelemetryMetadataBuilder`; new tests for the API contract. The corpus tool's `diff build` now maps every recipe key with a single setter instead of falling back to hand-built records.

## 0.56.0 (2026-11-10)

This release breaks the public API (a binary break, and a source break for code that assigns these values to an `int`), so it needs a minor version bump.

### Exact bytes

- The encoder now writes exactly the bytes the shared test vectors give as `canonical_info`, and those bytes are binding. Comment fields come out in a fixed order (frequency, braces, `/A=`, text, telemetry, DAO), numbers are written as plain decimals as sent, and delimiters appear only where needed.
- One rounding rule for every field: nearest step, halves away from zero, rounded once, then range-checked. Out-of-range values are refused rather than clamped.
- Position reports: a course of 0 is written only where it reads back as 0, compressed course and speed are written together, direction-finding and storm data follow `.../...`, and a separating space is added where a `/` would otherwise join a Mic-E altitude.
- Builders: a course of 0 means unknown, km/h and metres are converted and rounded once, position ambiguity cuts the raw position, and the Mic-E type code comes from the messaging flag.
- Decoder fixes found by differential fuzzing against the other implementations: Mic-E telemetry and DAO are lifted before the locator, a later altitude is found past them, the strict-mode join rule is applied, and storm fields must be digits.
- New API: `AprsQueryFootprint` has value equality, and `AprsTelemetryBuilder.Analog` takes a `decimal?[]`.
- The `Packet.Aprs.Corpus` tool gains `diff dump`, `diff encode` and `diff build` modes and an API view, for comparing implementations. See `docs/aprs-validation.md`.

### Builder gaps

- Fractional values are accepted and rounded once, from the value given, to the step the field holds (halves away from zero, then range-checked), as the aprs-vectors Encoding rule says. A GPS heading of 301.7 in a compressed report is now the 4-degree step nearest 301.7, not the one nearest 302.
- `AprsPositionedData.CourseDegrees`, and `AprsWeather.WindDirectionDegrees`, `WindGustMph`, `TemperatureFahrenheit`, `HumidityPercent` and `LuminosityWattsPerSquareMetre` are now `double?` (were `int?`). Decoded values are the same whole numbers.
- `AprsPositionedBuilder.Course`, and `AprsWeatherBuilder.Wind`, `Gust`, `Temperature`, `Humidity` and `Luminosity` take `double` (were `int`). `TemperatureCelsius` no longer rounds to a whole degree Fahrenheit before the encoder rounds again.
- The Packet.Aprs.Corpus `diff encode` and `diff build` tools report a third-party packet with no inner destination, or an unrecognized non-empty inner packet, as `refused`, and no longer report `unsupported` for any recipe or neutral record.
