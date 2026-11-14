namespace Packet.Aprs.Tests;

/// <summary>
/// <c>AprsTimestamp.Resolve</c> works in UTC whatever the machine's time zone: a Local reference is
/// converted first, and Unspecified is taken as UTC.
/// </summary>
public class TimestampResolveTests
{
    private static readonly DateTime HeardUtc = new(2026, 9, 10, 15, 1, 0, DateTimeKind.Utc);

    [Fact]
    public void A_local_reference_is_converted_to_utc_first()
    {
        // Whatever zone the machine is in, the local form of the same instant resolves the same.
        DateTime local = HeardUtc.ToLocalTime();
        local.Kind.Should().Be(DateTimeKind.Local);

        AprsTimestamp.HoursMinutesSeconds(15, 0, 0).Resolve(local)
            .Should().Be(new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc));
        AprsTimestamp.DayHoursMinutes(10, 15, 0).Resolve(local)
            .Should().Be(new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc));
        AprsTimestamp.MonthDayHoursMinutes(9, 10, 15, 0).Resolve(local)
            .Should().Be(new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("Pacific/Kiritimati")]
    [InlineData("Pacific/Pago_Pago")]
    [InlineData("UTC")]
    public void A_reference_with_an_offset_ignores_the_machine_zone(string zoneId)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return;
        }

        var reference = new DateTimeOffset(TimeZoneInfo.ConvertTimeFromUtc(HeardUtc, zone), zone.GetUtcOffset(HeardUtc));
        reference.UtcDateTime.Should().Be(HeardUtc);

        AprsTimestamp.HoursMinutesSeconds(15, 0, 0).Resolve(reference)
            .Should().Be(new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc));
        AprsTimestamp.DayHoursMinutes(10, 15, 0).Resolve(reference)
            .Should().Be(new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc));
        AprsTimestamp.MonthDayHoursMinutes(9, 10, 15, 0).Resolve(reference)
            .Should().Be(new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void An_unspecified_reference_is_taken_as_utc()
    {
        var unspecified = new DateTime(2026, 9, 10, 15, 1, 0, DateTimeKind.Unspecified);

        AprsTimestamp.HoursMinutesSeconds(15, 0, 0).Resolve(unspecified)
            .Should().Be(new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void A_local_form_timestamp_still_resolves_to_an_unspecified_kind()
    {
        AprsTimestamp.DayHoursMinutes(10, 15, 0, utc: false).Resolve(HeardUtc)!.Value.Kind
            .Should().Be(DateTimeKind.Unspecified);
    }
}
