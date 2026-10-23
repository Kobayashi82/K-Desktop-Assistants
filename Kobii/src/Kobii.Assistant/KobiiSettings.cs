using System.Text.Json;
using KDesktop.Assistant.Sdk;
using Kobii.Core.Creature;

namespace Kobii.Assistant;

/// <summary>
/// Sus ajustes: los de Package\settings.json (lo que manda el host, con los valores por defecto ya puestos) y lo que K-Desktop
/// pone igual para todos los asistentes (SDK 1.9, pestaña «General» de su ficha): tamaño, lo movido que es y cogerlo con el ratón.
/// </summary>
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

    public static KobiiSettings Read(JsonElement json, CommonSettings common)
    {
        var defaults = new KobiiSettings();
        var own = json.ValueKind == JsonValueKind.Object;
        return new KobiiSettings
        {
            Color = own && Text(json, "color") is { } color && Species.Palettes.Any(p => p.Id == color) ? color : defaults.Color,
            Size = Math.Clamp(common.Size, 0.5, 2),
            // Al 0 % lo menos movido que puede ser.
            Liveliness = Math.Clamp(common.Liveliness, 0.2, 2),
            FollowCursor = (own ? Flag(json, "followCursor") : null) ?? defaults.FollowCursor,
            AllowDragging = common.Dragging,
        };
    }

    private static string? Text(JsonElement json, string key) =>
        json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? Flag(JsonElement json, string key) =>
        json.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
