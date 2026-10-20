using Lander.Core.Geometry;

namespace Lander.Core.Terrain;

/// <summary>Un monitor con sus límites y su área de trabajo (sin la barra de tareas) en píxeles físicos.</summary>
public sealed record MonitorInfo(int Index, PixelRect Bounds, PixelRect WorkArea, double DpiScale, bool IsPrimary);
