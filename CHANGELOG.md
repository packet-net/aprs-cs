# Changelog

## Unreleased

The aprs-vectors round 4 rulings (version 1.2; vectors pinned at `fe7d451`, aprs-vectors#29). The licence is now MIT.

Breaking changes for callers

- An encoder or builder refuses an information field over 256 bytes (`TooLong`), whatever it writes, third-party packets counted on the outermost field. The size is the last check, on the bytes actually written: a record any other rule refuses (a missing field, a bad symbol, a fault in a third-party packet's inner field, the read-back) gets that rule's code, and only an otherwise sound record over 256 bytes is `TooLong` for its size. A field over its own limit (message text over 67 characters, more than five analog values) stays `TooLong` at its place in the order. A decoder sets no limit, so a decoded packet over 256 bytes decodes as before and re-encoding it is refused.
- Text with a control character (a byte below 0x20 or 0x7F, a TAB or a CR or LF inside the text included) is refused as `InvalidCharacter`, not only a CR or LF; a Mic-E status text starting with 0x1D is refused too, not written after a `/`. A decoder gives the new warning `ControlCharacterInText` for such text, so a strict decoder (`AllowControlCharacterInText` off) now rejects those packets.
- A run-on weather value (a fixed-width field whose digits run on by two or more) followed by the end of the information field or by a defined field it goes on to read is dropped with `OutOfRangeValue`, and the fields carry on after it, so `t2147483647r000p000` has a rain of 0 and no temperature.
- An object or a positionless weather report built with no timestamp is refused (`MissingField`) instead of being stamped from the clock. A builder never reads the clock: pass `AprsTimestamp.FromDateTime(DateTime.UtcNow)` (a positionless weather report: with `AprsTimestampFormat.MonthDayHoursMinutesUtc`) for the time now. `AprsWeatherReport.Timestamp` is now `AprsTimestamp?`, null meaning none given.
- A Mic-E altitude goes in the Mic-E altitude bytes, rounded once to the nearest metre from the value given (1000 feet is 305 m), instead of a `/A=` in the status text for a builder given feet. A builder's altitude outside -10000 to 743570 m is `ValueOutOfRange`.
- Neutral Mic-E data (not decoded from a `/A=`): an altitude of -50000 feet is written as `/A=-50000` instead of refused, and one that is neither a whole number of feet nor of metres (100.5 feet) goes in the Mic-E bytes as 31 m instead of `/A=000101`. A `/A=` is written only for a whole number of feet within its six digits that the Mic-E bytes do not hold exactly; an altitude read from a `/A=` is still written back as one.
- A position, object, item or Mic-E report built with no symbol is refused as `InvalidSymbol` (a builder used to throw `InvalidOperationException`); a builder still throws it for an object or item with no position.

Other changes

- New diagnostic codes `ControlCharacterInText` (a warning, tolerated by the new `AllowControlCharacterInText` flag, on in `Lenient`, off in `Strict`) and `ImplementationLimit`, which Packet.Aprs has no use for (it has no fixed capacity) but which is in the shared code catalogue.
- A field the report has no place for gets none of its checks: a NaN or infinite altitude on a weather report that has no place for it (anything but a GGA altitude in the cs bytes of a compressed one) is `FieldNotAllowed`, or whatever an earlier fault in the record is named, not `NotANumber`. A GGA altitude of NaN or an infinity in the cs bytes is `NotANumber`, before a longitude out of range.
- A status symbol given with no locator is `MissingField` (was `FieldNotAllowed`), in a builder and in neutral data alike. Neutral telemetry data with no sequence is `MissingField` (was `Malformed`); a sequence that is there but not letters or digits is still `Malformed`.
- A Mic-E altitude the bytes cannot carry in place (the first byte would read as a type code, with no type code given) and no `/A=` holds exactly is `ValueHasNoForm` (was `TextReadsAsField` for some values).
- The builder defaults are written into the vectors' rules and are as before: no path, destination `APZ001`, messaging off, a Mic-E report with no message off duty, a weather report with no symbol `/_`, a telemetry report with no bits `00000000`.
- The vectors test runner runs the new `build` case input (`cases/builder.json`) through the same recipe builder as `aprs-corpus diff build` (`tests/Packet.Aprs.Tests/Vectors/RecipeBuilder.cs`, linked into the tool), comparing the TNC2 line built or the refusal code. `diff encode` and `diff build` records carry the input's own `n`, so a rerun of a subset lines up with the set.
- `aprs-corpus diff build` passes only the keys a recipe gives. A telemetry sequence left out is refused as `missing-field` (the builder takes the sequence as an argument, so it is built with a placeholder only to name an earlier fault in the shared order); a message, bulletin or status with no `text` still gets empty text, as absent text is empty text.

Review fixes

- Fixed: `AprsTimestamp.Resolve(DateTime)` used a Local reference's wall-clock fields as if they were UTC, so on a machine ahead of UTC it could pick the wrong day. A Local reference is now converted to UTC first; Unspecified is still taken as UTC (documented). New overload `Resolve(DateTimeOffset)`, which never depends on the machine's time zone.
- Fixed the package README's worked example 2, which did not compile (`mobile.Message` is `mobile.MicEMessage`).
- Tests: every `csharp` block in the README is now checked against the tested copy in `ReadmeExamples`, so the two cannot drift apart unnoticed.

## 1.1.0 (2026-11-14)

The aprs-vectors round 3 rulings (vectors pinned at `10d4e25`, aprs-vectors#24).

Behaviour changes for 1.x callers

- Some data 1.0.x wrote is now refused: a locator in lower case (status, Mic-E, Maidenhead beacon), telemetry with too few or too many values, more than 15 `EQNS.` coefficients, third-party packets nested more than four deep, and a third-party path entry before a used one that is not marked used.
- An AX.25 frame whose address holds punctuation or a control character is now an error (`InvalidAddress`) in both modes, not a tolerated `InvalidAx25AddressCharacters`.
- Where data has several faults, the refusal code may differ from 1.0.x: it is now the first in the shared order.
- An infinity is `NotANumber` wherever a NaN is (it was `ValueOutOfRange` at some checks, weather values included), an NMEA field given as NaN or an infinity is `NotANumber` whatever the sentence, and a value too large for any field is `ValueOutOfRange` where an overflow could escape before. A compressed position out of range no longer hides a fault in its cs values.
- `AprsPacket.Create` and the builders now refuse a source, destination or path entry that is not 1-9 letters, digits or `-` (`InvalidAddress`), before the data is looked at. 1.0.x wrote such a header, which a decoder then rejected.

Refusal reasons

- Once a field is refused, the later fields are still checked even where the first fault leaves them meaningless (a longitude of 1000 has no Mic-E digits), so an earlier code later in the record, such as `NotANumber`, is still named.
- A record with several faults is refused with the first code in the vectors' Refusal reasons order, whichever field each fault is in. Before, the first field checked won, so an object name ending in a space was named ahead of a latitude past 90, a CR in the comment or a bad symbol. Each field is now checked on its own and the earliest code is reported; a field the report has no place for is still `FieldNotAllowed` without its own checks (telemetry on a weather report is now one of those), while a field whose place exists but is wanted twice keeps its checks (a PHG digit of 10 beside a course is `ValueOutOfRange`, as is storm data with a value too wide for its digits beside a weather report's wind or a DF bearing; a compressed course, speed, range or GGA altitude is range-checked before any conflict over the cs bytes; so is a course beside a weather report's wind, and a Mic-E RNG beside a PHG).
- More analog values than base-91 telemetry or a telemetry report holds is `TooLong`, fewer is `Malformed`; more than 15 `EQNS.` coefficients is `TooLong`.
- A locator in lower case, in a status, a Mic-E report or a Maidenhead beacon, is `Malformed`, since it reads back in upper case. The status builder's `Locator` sends it in upper case.
- A `!DAO!` digit datum with added precision is `InvalidCode` (was `FieldNotAllowed`).
- An NMEA comment given with a sentence that has no checksum is `InvalidNmea` (was `TextReadsAsField`).
- A compressed weather report with a course or speed is `ExtensionConflict` even when it gives no wind.
- A general or directed query with an empty query type is `InvalidCode`: absent text is empty text.
- A reply-ack is checked as a message ID before a missing message ID is reported.
- The telemetry builder checks its analog values and bits when the report is built, so their faults are named in the shared order with the others.
- A builder setter replaces its quantity whatever unit it is given in, so on a kept builder the last call wins, as in 1.0.x. `ValueGivenTwice` is only for one input that carries a quantity in two units at once: `aprs-corpus diff build` builds such a recipe once with each unit and names the first code in the shared order among both builds' refusals and `ValueGivenTwice` (vectors README, Builder rules).

Third-party packets

- New `AprsThirdPartyTraffic.Create(source, data, destination = null, path = null)` checks the inner header before the inner data, so a header that cannot be written is `InvalidThirdPartyHeader` whatever is wrong with the data. Its destination follows `AprsPacket.Create`: null is the default, and a Mic-E report computes its own.
- Third-party data nested more than four deep is refused as `InvalidThirdPartyHeader`, since a decoder reads only four levels. Depth is counted without recursion, so a chain of any length is refused cleanly.
- A third-party path entry before a used one that is not marked used is refused as `InvalidThirdPartyHeader`: it reads back marked.

Decoding

- An AX.25 address is upper-case letters and digits padded with spaces. A lower-case letter is still tolerated (`InvalidAx25AddressCharacters`); any other character (punctuation, a control character, a space before the last character) is now `InvalidAddress`, an error in both modes, so no TNC2 line is written from such a frame. A source of spaces only is `InvalidAddress`.
- A `!DAO!` on an ambiguous position gives `DaoWithAmbiguity` whatever its precision, `!W  !` included.

Tools

- `aprs-corpus diff encode` holds an addressee, name, query type or symbol left out of the data as empty, so the encoder refuses it rather than the line being unsupported (absent text is empty text); a line nested too deep for its JSON reader is unsupported, not a crash; the generator's `NaN`, `Infinity` and `-Infinity` tokens are read; third-party data whose inner packet carries a tolerated diagnostic is built as the packet that reads with it, so it is refused (`invalid-third-party-header`); a symbol of one character no longer crashes it ("Index was outside the bounds of the array").
- The vectors runner reads third-party data inside its refusal check, and skips the neutral read-back of nested third-party packets with undecoded data at any depth.

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
