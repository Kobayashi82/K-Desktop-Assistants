using System.Text.Json;
using Kobii.Core.Creature;

namespace Kobii.Assistant;

/// <summary>Sus ajustes (las claves de Package\settings.json), leídos de lo que manda el host con los valores por defecto ya puestos.</summary>
internal sealed record KobiiSettings
{
    /// <summary>Su colorido (id de <see cref="Species.Palettes"/>).</summary>
    public string Color { get; init; } = "mint";

    /// <summary>Tamaño (1 = el normal).</summary>
    public double Size { get; init; } = 1;

    /// <summary>Lo movido que es (1 normal).</summary>
    public double Liveliness { get; init; } = 1;

    public bool FollowCursor { get; init; } = true;

    public bool AllowDragging { get; init; } = true;

    public GameOptions ToGameOptions() => new() { Size = Size, Liveliness = Liveliness, FollowCursor = FollowCursor };

    public static KobiiSettings Read(JsonElement json)
    {
        var defaults = new KobiiSettings();
        if (json.ValueKind != JsonValueKind.Object)
        {
            return defaults;
        }

        return new KobiiSettings
        {
            Color = Text(json, "color") is { } color && Species.Palettes.Any(p => p.Id == color) ? color : defaults.Color,
            Size = Math.Clamp(Number(json, "size") ?? defaults.Size, 0.6, 2),
            Liveliness = Math.Clamp(Number(json, "liveliness") ?? defaults.Liveliness, 0.2, 2),
            FollowCursor = Flag(json, "followCursor") ?? defaults.FollowCursor,
            AllowDragging = Flag(json, "allowDragging") ?? defaults.AllowDragging,
        };
    }

    private static string? Text(JsonElement json, string key) =>
        json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement json, string key) =>
        json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static bool? Flag(JsonElement json, string key) =>
        json.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
