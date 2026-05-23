using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomRP.Modern.Services;

internal static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // known-apps.json uses PascalCase enum names (e.g. "SinceStart").
        Converters = { new JsonStringEnumConverter() },
    };

    public static JsonSerializerOptions PresetOptions { get; } = new(Options)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
