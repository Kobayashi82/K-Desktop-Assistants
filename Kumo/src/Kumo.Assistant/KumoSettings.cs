using System.Text.Json;
using KDesktop.Assistant.Sdk;

namespace Kumo.Assistant;

/// <summary>
/// Sus ajustes: los de Package\settings.json (lo que manda el host, con los valores por defecto ya puestos) y lo que K-Desktop
/// pone igual para todos los asistentes (SDK 1.9, pestaña «General» de su ficha): tamaño, lo movido que es y cogerlo con el ratón.
/// </summary>
internal sealed record KumoSettings
{
    /// <summary>Tamaño (1 = el normal).</summary>
    public double Size { get; init; } = 1;

    /// <summary>Lo movido que es (1 normal): cada cuánto viaja, cambia de forma o hace algo por su cuenta.</summary>
    public double Liveliness { get; init; } = 1;

    public bool AllowDragging { get; init; } = true;

    /// <summary>Te sigue con la mirada cuando el cursor pasa cerca.</summary>
    public bool FollowCursor { get; init; } = true;

    /// <summary>Se acerca a ver los avisos de Windows.</summary>
    public bool WatchNotifications { get; init; } = true;

    /// <summary>Baila con la música que suena.</summary>
    public bool Dance { get; init; } = true;

    public static KumoSettings Read(JsonElement json, CommonSettings common)
    {
        var defaults = new KumoSettings();
        var own = json.ValueKind == JsonValueKind.Object;
        return new KumoSettings
        {
            Size = Math.Clamp(common.Size, 0.5, 2),
            Liveliness = Math.Clamp(common.Liveliness, 0.2, 2),
            AllowDragging = common.Dragging,
            FollowCursor = (own ? Flag(json, "followCursor") : null) ?? defaults.FollowCursor,
            WatchNotifications = (own ? Flag(json, "watchNotifications") : null) ?? defaults.WatchNotifications,
            Dance = (own ? Flag(json, "dance") : null) ?? defaults.Dance,
        };
    }

    private static bool? Flag(JsonElement json, string key) =>
        json.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
