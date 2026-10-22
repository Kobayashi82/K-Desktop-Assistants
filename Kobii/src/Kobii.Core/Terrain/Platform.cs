using Kobii.Core.Geometry;

namespace Kobii.Core.Terrain;

public enum PlatformKind
{
    /// <summary>Suelo: barra de tareas o borde inferior del monitor. Es la base dla criatura.</summary>
    Ground,

    /// <summary>Borde superior visible de una ventana.</summary>
    Window,
}

/// <summary>
/// Segmento horizontal sobre el que la criatura puede posarse. Solo colisiona desde arriba.
/// </summary>
/// <param name="OwnerId">Dueño del segmento: HWND de la ventana o identificador negativo para el suelo.</param>
/// <param name="Kind">Tipo de superficie.</param>
/// <param name="Left">Extremo izquierdo en píxeles físicos.</param>
/// <param name="Right">Extremo derecho en píxeles físicos.</param>
/// <param name="Y">Altura de la superficie.</param>
/// <param name="MonitorIndex">Monitor al que pertenece.</param>
/// <param name="OwnerOrigin">
/// Origen del dueño (esquina superior izquierda de la ventana). Sirve para seguir el movimiento de la
/// ventana aunque el segmento visible cambie por oclusiones.
/// </param>
/// <param name="AppId">Aplicación dueña, si es una ventana.</param>
public sealed record Platform(
    long OwnerId,
    PlatformKind Kind,
    double Left,
    double Right,
    double Y,
    int MonitorIndex,
    Vec2 OwnerOrigin,
    string? AppId = null)
{
    public double Width => Right - Left;

    public double CenterX => (Left + Right) / 2;

    public bool IsBase => Kind == PlatformKind.Ground;

    public bool ContainsX(double x, double margin = 0) => x >= Left + margin && x <= Right - margin;

    /// <summary>Identificador del suelo número <paramref name="segment"/> del monitor <paramref name="monitorIndex"/>.</summary>
    public static long GroundOwnerId(int monitorIndex, int segment) => -(1 + monitorIndex * 16L + segment);
}
