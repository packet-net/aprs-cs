# Changelog

## Unreleased

This release breaks the public API (a binary break, and a source break for code that assigns these values to an `int`), so it needs a minor version bump.

- Fractional values are accepted and rounded once, from the value given, to the step the field holds (halves away from zero, then range-checked), as the aprs-vectors Encoding rule says. A GPS heading of 301.7 in a compressed report is now the 4-degree step nearest 301.7, not the one nearest 302.
- `AprsPositionedData.CourseDegrees`, and `AprsWeather.WindDirectionDegrees`, `WindGustMph`, `TemperatureFahrenheit`, `HumidityPercent` and `LuminosityWattsPerSquareMetre` are now `double?` (were `int?`). Decoded values are the same whole numbers.
- `AprsPositionedBuilder.Course`, and `AprsWeatherBuilder.Wind`, `Gust`, `Temperature`, `Humidity` and `Luminosity` take `double` (were `int`). `TemperatureCelsius` no longer rounds to a whole degree Fahrenheit before the encoder rounds again.
- The Packet.Aprs.Corpus `diff encode` and `diff build` tools report a third-party packet with no inner destination, or an unrecognized non-empty inner packet, as `refused`, and no longer report `unsupported` for any recipe or neutral record.
