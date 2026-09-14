using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace SmartX.Core.Telemetry;

// adding two floats and adding two ints are different machine instructions underneath
// so the right one is worked out once per type and kept, like cutting a key once instead of picking the lock every time
public static class TelemetryOperator<T> where T : struct
{
    public static readonly Func<T, T, T> Add;

    public static readonly Func<T, T, T> Subtract;

    public static readonly Func<T, double> ToMagnitude;

    public static readonly Func<double, T> FromMagnitude;

    public static bool IsNumeric { get; }

    static TelemetryOperator()
    {
        // bool has no plus, so two valves combine like light switches, either one open means the pair is open
        if (typeof(T) == typeof(bool))
        {
            IsNumeric = false;

            Add = static (a, b) => Reinterpret(AsBool(a) || AsBool(b));
            Subtract = static (a, b) => Reinterpret(AsBool(a) && !AsBool(b));
            ToMagnitude = static v => AsBool(v) ? 1d : 0d;
            FromMagnitude = static d => Reinterpret(d >= 0.5d);
            return;
        }

        IsNumeric = true;

        var left = Expression.Parameter(typeof(T), "left");
        var right = Expression.Parameter(typeof(T), "right");

        // these compiled shortcuts are what the packet operators will actually call
        Add = Expression.Lambda<Func<T, T, T>>(Expression.AddChecked(left, right), left, right).Compile();
        Subtract = Expression.Lambda<Func<T, T, T>>(Expression.SubtractChecked(left, right), left, right).Compile();

        var single = Expression.Parameter(typeof(T), "value");
        ToMagnitude = Expression.Lambda<Func<T, double>>(
            Expression.Convert(single, typeof(double)), single).Compile();

        var magnitude = Expression.Parameter(typeof(double), "magnitude");
        FromMagnitude = Expression.Lambda<Func<double, T>>(
            Expression.Convert(magnitude, typeof(T)), magnitude).Compile();
    }

    private static bool AsBool(T value) => Unsafe.As<T, bool>(ref value);

    private static T Reinterpret(bool value) => Unsafe.As<bool, T>(ref value);
}
