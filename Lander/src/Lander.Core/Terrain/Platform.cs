using Lander.Core.Geometry;

namespace Lander.Core.Terrain;

public enum PlatformKind
{
    /// <summary>Suelo: barra de tareas o borde inferior del monitor. Es la base del lander.</summary>
    Ground,

    /// <summary>Borde superior visible de una ventana.</summary>
    Window,

    /// <summary>
    /// Borde horizontal dentro del contenido de una ventana (un separador, una cabecera, una tabla), visto en
    /// una captura de la pantalla. Se mueve con la ventana y con el scroll.
    /// </summary>
    Content,
}

/// <summary>
/// Segmento horizontal sobre el que el lander puede posarse. Solo colisiona desde arriba.
/// </summary>
/// <param name="OwnerId">
/// Dueño del segmento: HWND de la ventana, identificador negativo para el suelo o, para un borde de contenido,
/// una combinación de la ventana y el borde (<see cref="ContentOwnerId"/>).
/// </param>
/// <param name="Kind">Tipo de superficie.</param>
/// <param name="Left">Extremo izquierdo en píxeles físicos.</param>
/// <param name="Right">Extremo derecho en píxeles físicos.</param>
/// <param name="Y">Altura de la superficie.</param>
/// <param name="MonitorIndex">Monitor al que pertenece.</param>
/// <param name="OwnerOrigin">
/// Origen del dueño (esquina superior izquierda de la ventana). Sirve para seguir el movimiento de la
/// ventana aunque el segmento visible cambie por oclusiones. En un borde de contenido es el borde izquierdo
/// de la ventana a la altura del borde, así que también sigue al scroll.
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
    private const long ContentFlag = 1L << 62;

    public double Width => Right - Left;

    public double CenterX => (Left + Right) / 2;

    public bool IsBase => Kind == PlatformKind.Ground;

    /// <summary>Ventana a la que pertenece: la propia para las ventanas y la que lo contiene para el contenido.</summary>
    public long WindowHandle => Kind switch
    {
        PlatformKind.Window => OwnerId,
        PlatformKind.Content => ContentWindow(OwnerId),
        _ => 0,
    };

    public bool ContainsX(double x, double margin = 0) => x >= Left + margin && x <= Right - margin;

    /// <summary>Identificador del suelo número <paramref name="segment"/> del monitor <paramref name="monitorIndex"/>.</summary>
    public static long GroundOwnerId(int monitorIndex, int segment) => -(1 + monitorIndex * 16L + segment);

    /// <summary>
    /// Identificador del borde de contenido <paramref name="edgeId"/> de una ventana. Los HWND solo usan los
    /// 32 bits bajos (también en 64 bits), así que el borde va en los altos y nunca coincide con una ventana.
    /// </summary>
    public static long ContentOwnerId(long windowHandle, int edgeId) =>
        ContentFlag | ((long)(edgeId & 0x3FFF_FFFF) << 32) | (windowHandle & 0xFFFF_FFFFL);

    public static bool IsContentOwner(long ownerId) => ownerId > 0 && (ownerId & ContentFlag) != 0;

    /// <summary>Ventana de un identificador de <see cref="ContentOwnerId"/>.</summary>
    public static long ContentWindow(long ownerId) => ownerId & 0xFFFF_FFFFL;

    /// <summary>Borde de un identificador de <see cref="ContentOwnerId"/>.</summary>
    public static int ContentEdgeId(long ownerId) => (int)((ownerId >> 32) & 0x3FFF_FFFF);
}
