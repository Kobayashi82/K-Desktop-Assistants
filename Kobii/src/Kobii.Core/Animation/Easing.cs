using Kobii.Core.Geometry;

namespace Kobii.Core.Animation;

/// <summary>Curvas de suavizado: nada se mueve a velocidad constante, como en los dibujos animados.</summary>
public enum Ease
{
    Linear,

    /// <summary>Arranca despacio y acelera.</summary>
    In,

    /// <summary>Sale rápido y frena al llegar.</summary>
    Out,

    /// <summary>Arranca y frena despacio (lo normal al mover una mano).</summary>
    InOut,

    /// <summary>Se pasa un poco y vuelve (un gesto con energía).</summary>
    Back,

    /// <summary>Casi todo el recorrido al principio (un mordisco, un golpe).</summary>
    Snap,
}

public static class Easing
{
    /// <summary>Lleva <paramref name="t"/> (0–1) por la curva indicada.</summary>
    public static double Apply(Ease ease, double t)
    {
        t = Math.Clamp(t, 0, 1);
        switch (ease)
        {
            case Ease.In:
                return t * t * t;
            case Ease.Out:
            {
                var u = 1 - t;
                return 1 - u * u * u;
            }

            case Ease.InOut:
                return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            case Ease.Back:
            {
                const double c1 = 1.70158;
                const double c3 = c1 + 1;
                var u = t - 1;
                return 1 + c3 * u * u * u + c1 * u * u;
            }

            case Ease.Snap:
            {
                var u = 1 - t;
                return 1 - u * u * u * u;
            }

            default:
                return t;
        }
    }

    /// <summary>Avance de un tramo que empieza en <paramref name="start"/> y dura <paramref name="duration"/> segundos.</summary>
    public static double Segment(double t, double start, double duration, Ease ease = Ease.InOut) =>
        duration <= 0 ? (t >= start ? 1 : 0) : Apply(ease, (t - start) / duration);

    /// <summary>
    /// Camino curvo de <paramref name="from"/> a <paramref name="to"/>: las manos no van en línea recta, sino
    /// en arco. <paramref name="bulge"/> es lo que se separa el arco de la recta (en el sentido indicado).
    /// </summary>
    public static Vec2 Arc(Vec2 from, Vec2 to, Vec2 bulge, double t)
    {
        var control = (from + to) * 0.5 + bulge * 2;
        var u = 1 - t;
        return from * (u * u) + control * (2 * u * t) + to * (t * t);
    }

    /// <summary>Sube de 0 a 1 en <paramref name="rise"/> segundos y vuelve a 0 en <paramref name="fall"/>.</summary>
    public static double Pulse(double t, double rise, double fall)
    {
        if (t <= 0)
        {
            return 0;
        }

        return t < rise ? Apply(Ease.Out, t / rise) : 1 - Apply(Ease.InOut, (t - rise) / Math.Max(1e-6, fall));
    }
}
