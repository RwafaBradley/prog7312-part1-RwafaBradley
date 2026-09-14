using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartX.Api;

public static class SmartXJson
{
    // one shared json setup, two of them drift apart and the client quietly starts reading the wrong shape
    public static readonly JsonSerializerOptions Options = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null));
        return options;
    }

    public static void ApplyTo(JsonSerializerOptions target)
    {
        target.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        target.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        target.Converters.Add(new JsonStringEnumConverter(namingPolicy: null));
    }
}
