namespace SmartX.Core.Domain;

// a node declares what kind of thing it is once, at sign up, and never changes its mind
// that one choice is what decides which typed pipe gets opened for it further along
public enum SensorCategory
{
    Environmental = 0,

    PowerConsumption = 1,

    Actuator = 2
}

// the raw type sitting behind each category, a float for a probe, an int for a meter, a bool for a valve
public enum TelemetryValueKind
{
    Float = 0,
    Integer = 1,
    Boolean = 2
}

public static class SensorCategoryExtensions
{
    public static TelemetryValueKind ValueKind(this SensorCategory category) => category switch
    {
        SensorCategory.Environmental => TelemetryValueKind.Float,
        SensorCategory.PowerConsumption => TelemetryValueKind.Integer,
        SensorCategory.Actuator => TelemetryValueKind.Boolean,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown sensor category.")
    };

    public static string DefaultUnit(this SensorCategory category) => category switch
    {
        SensorCategory.Environmental => "%RH",
        SensorCategory.PowerConsumption => "W",
        SensorCategory.Actuator => "state",
        _ => string.Empty
    };
}
