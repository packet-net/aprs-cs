# Changelog

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
