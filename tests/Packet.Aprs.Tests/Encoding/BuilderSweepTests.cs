using System.Reflection;

namespace Packet.Aprs.Tests.Encoding;

/// <summary>
/// Hostile numbers in every numeric argument of every fluent builder method and station factory:
/// each call either refuses with <see cref="AprsEncodeException"/> or writes a packet (which the
/// read-back has already shown reads back as given), and a number that is not a number is never
/// written, nor a finite one refused as one. Methods are found by reflection, so a setter added
/// later is swept too.
/// </summary>
public class BuilderSweepTests
{
    private static readonly double[] Values =
    [
        double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.MinValue, double.Epsilon, 1e-320, -0.0, 0, -1, 0.5, 1e15, 4294967296, 1e308,
    ];

    private static AprsStation Me => AprsStation.From("N0CALL");

    private static IEnumerable<(string Name, Func<AprsBuilder> Make)> Bases() =>
    [
        ("position", () => Me.Position(51.5, -0.5).Symbol(AprsSymbol.Car)),
        ("object", () => Me.Object("OBJ").At(51.5, -0.5).Symbol(AprsSymbol.Car).Timestamp(AprsTimestamp.DayHoursMinutes(1, 2, 3))),
        ("item", () => Me.Item("ITEM").At(51.5, -0.5).Symbol(AprsSymbol.Car)),
        ("mic-e", () => Me.MicE(51.5, -0.5).Symbol(AprsSymbol.Car)),
        ("weather", () => Me.Weather().At(51.5, -0.5).WindDirectionDegrees(10).WindSpeedMph(5)),
        ("telemetry", () => Me.Telemetry(1).Analog(1, 2, 3, 4, 5)),
        ("coefficients", () => Me.TelemetryCoefficients(0, 1, 0)),
    ];

    private static bool Numeric(Type t) => (Nullable.GetUnderlyingType(t) ?? t) is var u && (u == typeof(double) || u == typeof(int))
        || t == typeof(double[]) || t == typeof(double?[]) || t == typeof(int[]);

    private static object One(Type t) => t == typeof(double[]) ? new double[] { 1 }
        : t == typeof(double?[]) ? new double?[] { 1 }
        : t == typeof(int[]) ? new[] { 1 }
        : (Nullable.GetUnderlyingType(t) ?? t) == typeof(int) ? 1 : (object)1.0;

    private static object With(Type t, double value) => t == typeof(double[]) ? new[] { value }
        : t == typeof(double?[]) ? new double?[] { value }
        : t == typeof(int[]) ? new[] { (int)Math.Clamp(value, int.MinValue, int.MaxValue) }
        : (Nullable.GetUnderlyingType(t) ?? t) == typeof(int) ? (object)(int)Math.Clamp(value, int.MinValue, int.MaxValue) : value;

    private static IEnumerable<(string Call, Func<AprsBuilder> Make, Func<AprsBuilder, AprsBuilder> Apply, bool NotFinite)> Calls()
    {
        foreach ((string name, Func<AprsBuilder> make) in Bases())
        {
            foreach (MethodInfo m in make().GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Concat(make().GetType().BaseType!.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
            {
                ParameterInfo[] ps = m.GetParameters();
                if (!typeof(AprsBuilder).IsAssignableFrom(m.ReturnType) || ps.Length == 0 || !ps.All(p => Numeric(p.ParameterType)) || m.IsGenericMethod)
                {
                    continue;
                }

                for (int at = 0; at < ps.Length; at++)
                {
                    foreach (double value in Values)
                    {
                        int index = at;
                        MethodInfo method = m;
                        object[] Args() => [.. ps.Select((p, i) => i == index ? With(p.ParameterType, value) : One(p.ParameterType))];
                        yield return ($"{name}.{m.Name}({string.Join(", ", ps.Select(p => p.Name))}) arg {ps[at].Name} = {value:R}", make, b => (AprsBuilder)method.Invoke(b, Args())!, !double.IsFinite(value) && (Nullable.GetUnderlyingType(ps[index].ParameterType) ?? ps[index].ParameterType) != typeof(int) && ps[index].ParameterType != typeof(int[]));
                    }
                }
            }
        }
    }

    [Fact]
    public void The_sweep_reaches_every_numeric_builder_method()
    {
        string[] calls = [.. Calls().Select(c => c.Call).Where(c => c.EndsWith("= 0", StringComparison.Ordinal))];
        calls.Should().Contain(c => c.StartsWith("position.CourseDegrees", StringComparison.Ordinal));
        calls.Should().Contain(c => c.StartsWith("position.AltitudeMetres", StringComparison.Ordinal));
        calls.Should().Contain(c => c.StartsWith("mic-e.SpeedKmh", StringComparison.Ordinal));
        calls.Should().Contain(c => c.StartsWith("weather.PressureMillibars", StringComparison.Ordinal));
        calls.Should().Contain(c => c.StartsWith("weather.TemperatureCelsius", StringComparison.Ordinal));
        calls.Should().Contain(c => c.StartsWith("position.Telemetry", StringComparison.Ordinal));
        calls.Should().Contain(c => c.StartsWith("telemetry.Analog", StringComparison.Ordinal));
        calls.Length.Should().BeGreaterThan(25);
    }

    [Fact]
    public void A_hostile_number_in_any_builder_argument_is_refused_or_read_back_and_never_crashes()
    {
        int swept = 0;
        foreach ((string call, Func<AprsBuilder> make, Func<AprsBuilder, AprsBuilder> apply, bool notFinite) in Calls())
        {
            swept++;
            AprsPacket? built = null;
            try
            {
                built = apply(make()).Build();
            }
            catch (AprsEncodeException ex)
            {
                if (notFinite)
                {
                    ex.Code.Should().Be(AprsRefusalCode.NotANumber, call);
                }
                else
                {
                    ex.Code.Should().NotBe(AprsRefusalCode.NotANumber, call);
                }
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is AprsEncodeException inner)
            {
                if (notFinite)
                {
                    inner.Code.Should().Be(AprsRefusalCode.NotANumber, call);
                }
                else
                {
                    inner.Code.Should().NotBe(AprsRefusalCode.NotANumber, call);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{call}: {ex}", ex);
            }

            if (built is null)
            {
                continue;
            }

            notFinite.Should().BeFalse($"{call} wrote bytes from a number that is not a number");
            byte[] line = built.ToTnc2();
            line.Should().NotBeEmpty(call);
            AprsPacket again = AprsPacket.Decode(line);
            again.HasErrors.Should().BeFalse(call);
            again.Information.ToArray().Should().Equal(built.Information.ToArray(), call);
            again.Data.GetType().Should().Be(built.Data.GetType(), call);

            // The same data inside third-party packets, one and two deep.
            AprsData data = built.Data;
            for (int depth = 1; depth <= 2; depth++)
            {
                // Data a builder wrote is writable inside a third-party packet at depth 1 and 2;
                // a refusal here is a failure, not something to skip.
                AprsThirdPartyTraffic wrapped = AprsThirdPartyTraffic.Create(AprsAddress.Parse("G4ABC"), data, data is AprsMicEReport ? built.Destination : null, [new AprsPathEntry(AprsAddress.Parse("TCPIP"), true)]);
                AprsPacket outer = AprsPacket.Create(AprsAddress.Parse("N0CALL"), wrapped);
                AprsPacket outerAgain = AprsPacket.Decode(outer.ToTnc2());
                outerAgain.HasErrors.Should().BeFalse($"{call} at depth {depth}");
                outerAgain.Information.ToArray().Should().Equal(outer.Information.ToArray(), $"{call} at depth {depth}");
                data = wrapped;
            }
        }

        swept.Should().BeGreaterThan(300);
    }
}
