namespace Lander.Core.Terrain;

/// <summary>
/// Borde de contenido confirmado dentro de una ventana, en coordenadas locales de la ventana (origen en su
/// esquina superior izquierda), para que siga a la ventana si se mueve.
/// </summary>
/// <param name="WindowHandle">Ventana que lo contiene.</param>
/// <param name="EdgeId">Identificador del borde, estable mientras siga a la vista.</param>
/// <param name="Left">Primera columna local.</param>
/// <param name="Right">Columna local siguiente a la última.</param>
/// <param name="Y">Fila local de la superficie.</param>
public sealed record ContentSurface(long WindowHandle, int EdgeId, int Left, int Right, int Y);
