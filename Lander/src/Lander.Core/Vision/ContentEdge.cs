namespace Lander.Core.Vision;

/// <summary>
/// Borde horizontal encontrado en una imagen: el lado superior de algo (un separador, una cabecera, el
/// marco de una tabla) con espacio libre encima.
/// </summary>
/// <param name="Y">Primera fila del elemento, es decir, la superficie donde apoyarían las patas.</param>
/// <param name="Left">Primera columna del tramo.</param>
/// <param name="Right">Columna siguiente a la última (exclusiva).</param>
/// <param name="Contrast">Salto medio de brillo entre la fila de encima y la del borde (0-255).</param>
/// <param name="Rising">Si el elemento es más claro que el fondo de encima.</param>
public readonly record struct ContentEdge(int Y, int Left, int Right, int Contrast, bool Rising)
{
    public int Length => Right - Left;
}
