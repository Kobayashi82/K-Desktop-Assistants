using System.Windows;
using System.Windows.Media;
using Kobii.Core.Animation;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;

namespace Kobii.Assistant.Rendering;

/// <summary>Lo que cambia la silueta del cuerpo (si no cambia, se reutiliza la geometría del fotograma anterior).</summary>
internal readonly record struct ShapeKey(double Hunch, double Facing, double HeadTilt, double EarL, double EarR, double Cheeks, double Melt, double FaceX)
{
    public static ShapeKey Of(in Rig rig, double facing) => new(
        Round(rig.Hunch), Round(facing), Round(rig.HeadTilt), Round(rig.EarL), Round(rig.EarR), Round(rig.Cheeks), Round(rig.Melt), Round(rig.FaceX));

    private static double Round(double v) => Math.Round(v * 200) / 200;
}

/// <summary>
/// La forma del cuerpo de Kobii, con las mismas fórmulas que el shader de Kobii AI (una bola tipo «mochi» con la base más
/// ancha, las orejas y los mofletes fundidos con suavidad): se evalúa la distancia a la forma y se recorre su borde para
/// sacar una silueta vectorial. Así el dibujo conserva la forma exacta sin calcular nada por píxel.
/// </summary>
/// <remarks>Unidades de la criatura: mide 2 de alto, el origen está en los pies y la y crece hacia abajo.</remarks>
internal static class KobiiShape
{
    /// <summary>Centro de la cabeza (el de los giros de la cara, las orejas y el brote).</summary>
    public static readonly Vec2 HeadCenter = new(0, -1.0);

    /// <summary>Cuánto ha crecido (la forma de joven).</summary>
    public static readonly double Grow = Rig.Grow(Species.Form);

    public const int Rays = 128;

    /// <summary>Lo que se desplaza la cabeza al encorvarse.</summary>
    public static Vec2 HunchShift(in Rig rig, double facing) => new Vec2(Math.Clamp(facing * 2.0, -1, 1) * 0.12, 0.09) * rig.Hunch;

    /// <summary>Lo que baja la cabeza al derretirse (la cara se escurre con el cuerpo).</summary>
    public static double MeltDrop(double melt) => 0.62 * melt;

    /// <summary>Un punto del cuerpo en el espacio de la cabeza (deshace el encorvado, el derretido y la inclinación).</summary>
    public static Vec2 HeadSpace(Vec2 q, in Rig rig, double facing)
    {
        var p = q - HunchShift(rig, facing) - new Vec2(0, MeltDrop(rig.Melt)) - HeadCenter;
        return Rotate(p, -rig.HeadTilt) + HeadCenter;
    }

    /// <summary>Distancia (negativa dentro) al cuerpo sin los brazos ni los pies.</summary>
    public static double Body(Vec2 q, in Rig rig, double facing)
    {
        var g = Grow;
        var melt = rig.Melt;
        var drop = MeltDrop(melt);

        // Aplastado, la bola de arriba se hunde y encoge y la base se ensancha.
        var top = Circle(q - new Vec2(0, -0.98 - 0.05 * g + drop) - HunchShift(rig, facing), 0.80 - 0.04 * g - 0.16 * melt);
        var baseRadius = new Vec2(0.92 - 0.06 * g + 0.42 * melt, 0.52 + 0.04 * g - 0.24 * melt);
        var bottom = Ellipse(q - new Vec2(0, -baseRadius.Y + 0.003), baseRadius);
        var d = SMin(top, bottom, 0.35 + 0.15 * melt);

        var h = HeadSpace(q, rig, facing);
        if (rig.Cheeks > 0.01)
        {
            var shift = facing * 0.16 * 0.7 + rig.FaceX * 0.7;
            var puff = SMin(
                Circle(h - new Vec2(-0.60 + shift, -0.80 - 0.03 * g), 0.27 * rig.Cheeks),
                Circle(h - new Vec2(0.60 + shift, -0.80 - 0.03 * g), 0.27 * rig.Cheeks), 0.05);
            d = SMin(d, puff, 0.12);
        }

        // Orejitas, cada una con su giro. Derretido, la cabeza encoge y las orejas se doblan hacia abajo pegadas a ella.
        var droop = 1.25 * melt;
        var root = new Vec2(0.48 + 0.06 * melt, -1.72 + 0.20 * melt);
        var ear = new Vec2(0.15, 0.24) * (1 - 0.25 * melt);
        var earR = h - root;
        var earL = new Vec2(-h.X, h.Y) - root;
        d = SMin(d, Ellipse(Rotate(earR, 0.45 + rig.EarR + droop), ear), 0.12);
        d = SMin(d, Ellipse(Rotate(earL, 0.45 + rig.EarL + droop), ear), 0.12);
        return d;
    }

    /// <summary>
    /// La silueta del cuerpo como geometría congelada: se lanzan rayos desde el centro y en cada uno se busca el borde más
    /// exterior; los puntos se unen con curvas suaves (Catmull-Rom).
    /// </summary>
    /// <param name="radii">Lo que mide cada rayo (de <see cref="Rays"/> elementos), para la vez siguiente; null si no hace falta.</param>
    /// <param name="near">Los radios traen lo de la vez anterior: se busca cerca (la forma cambia poco de un fotograma a otro).</param>
    public static Geometry Silhouette(in Rig rig, double facing, double[]? radii = null, bool near = false)
    {
        var center = new Vec2(0, -0.95 + 0.40 * rig.Melt);
        var points = new Point[Rays];
        var guess = near && radii is { Length: Rays };
        for (var i = 0; i < Rays; i++)
        {
            var angle = Math.Tau * i / Rays;
            var dir = new Vec2(Math.Cos(angle), Math.Sin(angle));
            var r = guess ? Near(center, dir, radii![i], rig, facing) : Edge(center, dir, rig, facing);
            if (radii is { Length: Rays })
            {
                radii[i] = r;
            }

            var p = center + dir * r;
            points[i] = new Point(p.X, p.Y);
        }

        return Smooth(points);
    }

    /// <summary>
    /// El borde cerca de <paramref name="previous"/>: se avanza a pasos cortos hacia donde cambia el signo y se afina con
    /// bisección. Si se aleja mucho (la forma cambió de golpe), se busca desde fuera.
    /// </summary>
    private static double Near(Vec2 center, Vec2 dir, double previous, in Rig rig, double facing)
    {
        const double step = 0.03;
        var inside = Body(center + dir * previous, rig, facing) <= 0;
        double a = previous, b = previous;
        for (var k = 0; k < 8; k++)
        {
            b = inside ? a + step : a - step;
            if (b <= 0)
            {
                return Edge(center, dir, rig, facing);
            }

            if (Body(center + dir * b, rig, facing) <= 0 != inside)
            {
                // El borde está entre a y b: bisección (dentro, fuera).
                var (lo, hi) = inside ? (a, b) : (b, a);
                for (var j = 0; j < 9; j++)
                {
                    var mid = (lo + hi) / 2;
                    if (Body(center + dir * mid, rig, facing) <= 0)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                // Un trozo de la forma más afuera (una oreja que se acerca al rayo) solo lo ve la búsqueda desde fuera.
                return inside || Body(center + dir * (hi + 0.25), rig, facing) > 0 ? (lo + hi) / 2 : Edge(center, dir, rig, facing);
            }

            a = b;
        }

        return Edge(center, dir, rig, facing);
    }

    /// <summary>Distancia desde <paramref name="center"/> al borde más exterior en la dirección <paramref name="dir"/>.</summary>
    private static double Edge(Vec2 center, Vec2 dir, in Rig rig, double facing)
    {
        // De fuera hacia dentro: el primer punto dentro y, entre él y el anterior, bisección.
        const double far = 2.4;
        const double step = 0.04;
        var outside = far;
        for (var r = far; r > 0; r -= step)
        {
            if (Body(center + dir * r, rig, facing) <= 0)
            {
                var inside = r;
                for (var k = 0; k < 14; k++)
                {
                    var mid = (inside + outside) / 2;
                    if (Body(center + dir * mid, rig, facing) <= 0)
                    {
                        inside = mid;
                    }
                    else
                    {
                        outside = mid;
                    }
                }

                return (inside + outside) / 2;
            }

            outside = r;
        }

        return 0;
    }

    /// <summary>Curva cerrada y suave que pasa por los puntos (Catmull-Rom convertida a Bézier cúbicas).</summary>
    public static Geometry Smooth(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var n = points.Count;
            g.BeginFigure(points[0], true, true);
            for (var i = 0; i < n; i++)
            {
                var p0 = points[(i - 1 + n) % n];
                var p1 = points[i];
                var p2 = points[(i + 1) % n];
                var p3 = points[(i + 2) % n];
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                g.BezierTo(c1, c2, p2, true, true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    public static Vec2 Rotate(Vec2 v, double angle)
    {
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        return new Vec2(c * v.X - s * v.Y, s * v.X + c * v.Y);
    }

    public static double Circle(Vec2 p, double r) => p.Length - r;

    public static double Ellipse(Vec2 p, Vec2 r)
    {
        var k0 = new Vec2(p.X / r.X, p.Y / r.Y).Length;
        var k1 = new Vec2(p.X / (r.X * r.X), p.Y / (r.Y * r.Y)).Length;
        return k0 * (k0 - 1.0) / Math.Max(k1, 1e-5);
    }

    public static double SMin(double a, double b, double k)
    {
        var h = Math.Clamp(0.5 + 0.5 * (b - a) / k, 0, 1);
        return b + (a - b) * h - k * h * (1.0 - h);
    }
}
