using Lander.Core.Geometry;

namespace Lander.Core.Terrain;

/// <summary>
/// Ventana de nivel superior visible, tal como la ve el gestor de ventanas.
/// </summary>
/// <param name="Handle">Identificador estable de la ventana (HWND).</param>
/// <param name="Bounds">Límites reales (DWM, sin sombras ni bordes invisibles) en píxeles físicos.</param>
/// <param name="IsMaximized">Si la ventana está maximizada.</param>
/// <param name="AppId">Identificador de la aplicación (nombre del proceso en minúsculas), si se conoce.</param>
public sealed record WindowInfo(long Handle, PixelRect Bounds, bool IsMaximized = false, string? AppId = null);
