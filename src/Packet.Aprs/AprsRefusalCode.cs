namespace Packet.Aprs;

/// <summary>
/// Why the library refused to write what it was given, carried by <see cref="AprsEncodeException.Code"/>.
/// The names are the shared refusal reason codes of the APRS vectors (kebab-case there). Values are never
/// renumbered and new codes are added at the end, so a caller must allow one it does not know.
/// </summary>
public enum AprsRefusalCode
{
    /// <summary>A header address (source, destination, path entry, or a third-party one) is not 1-9 letters, digits or `-` in TNC2 text.</summary>
    InvalidAddress = 1,

    /// <summary>An AX.25 frame cannot carry the header: a call over 6 characters or in lower case, an SSID over 15 or written with a leading zero, more than 8 path entries.</summary>
    Ax25CannotCarry = 2,

    /// <summary>A TNC2 line cannot carry the packet: its information field is not UTF-8 text.</summary>
    Tnc2CannotCarry = 3,

    /// <summary>A third-party packet's inner header cannot be written as received.</summary>
    InvalidThirdPartyHeader = 4,

    /// <summary>A message, bulletin, NWS bulletin, telemetry metadata or directed query addressee is empty, too long, holds a space or a colon, or is not the form its report needs.</summary>
    InvalidAddressee = 5,

    /// <summary>An object or item name is the wrong length or holds a character a name cannot.</summary>
    InvalidName = 6,

    /// <summary>A message ID is empty, over 5 characters or not letters and digits, or an ack or reject carries the wrong kind of ID.</summary>
    InvalidMessageId = 7,

    /// <summary>Text starts or ends with a space that would be read back as padding.</summary>
    PaddingSpace = 8,

    /// <summary>Free text or a list is longer than its field allows.</summary>
    TooLong = 9,

    /// <summary>Text in a fixed-form field is not that form (a locator, a bit sense, telemetry bits, signpost text, a capability token).</summary>
    Malformed = 10,

    /// <summary>Free text holds a character its field cannot carry: a line break, a left brace in message-like text, or a character outside printable ASCII.</summary>
    InvalidCharacter = 11,

    /// <summary>Free text would read back as a structured element (an altitude, DAO, telemetry, a frequency, PHG, a timestamp, a locator, a Mic-E device suffix, a weather field).</summary>
    TextReadsAsField = 12,

    /// <summary>A value is NaN or infinite.</summary>
    NotANumber = 13,

    /// <summary>A number is outside what its field holds, after rounding.</summary>
    ValueOutOfRange = 14,

    /// <summary>A value inside the range that no form reads back as.</summary>
    ValueHasNoForm = 15,

    /// <summary>A builder input was given in two units.</summary>
    ValueGivenTwice = 16,

    /// <summary>A timestamp has a field out of range, or is a format the report does not take.</summary>
    InvalidTimestamp = 17,

    /// <summary>The data lacks something its report or another field needs.</summary>
    MissingField = 18,

    /// <summary>The symbol table or code is not a valid symbol.</summary>
    InvalidSymbol = 19,

    /// <summary>The symbol disagrees with the data.</summary>
    SymbolMismatch = 20,

    /// <summary>Two things want the one data extension or the compressed cs bytes.</summary>
    ExtensionConflict = 21,

    /// <summary>The report has no place for a field, or it contradicts another.</summary>
    FieldNotAllowed = 22,

    /// <summary>An enumerated value outside its set.</summary>
    InvalidCode = 23,

    /// <summary>An NMEA sentence is malformed, its checksum does not match, or a field given beside it disagrees with it.</summary>
    InvalidNmea = 24,

    /// <summary>The call has nothing it can write: undecoded data, or Mic-E through a call that cannot return the destination.</summary>
    NotEncodable = 25,

    /// <summary>Fallback: the library's own read-back check found the bytes decode to different data and no rule names why. Numbered last as the fallback; every refusal that can be named is.</summary>
    DoesNotReadBack = 26,
}
