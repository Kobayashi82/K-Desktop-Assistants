using Lander.Core.Geometry;

namespace Lander.Core.Towing;

/// <summary>
/// El disparo del gancho: un proyectil balístico (con gravedad) que sale de la nave y llega al punto de la barra de título
/// justo en el tiempo pedido. La solución es cerrada, así que acierta a cualquier distancia y altura; si falla es porque la
/// ventana se ha movido mientras volaba (ver <see cref="Hits"/>).
/// </summary>
/// <param name="From">De dónde sale.</param>
/// <param name="To">Adónde va (el punto de la barra de título).</param>
/// <param name="Duration">Lo que tarda en llegar (s).</param>
/// <param name="Gravity">La gravedad del proyectil (px/s², positiva hacia abajo).</param>
public sealed record HookShot(Vec2 From, Vec2 To, double Duration, double Gravity)
{
    /// <summary>Lo que tarda como mucho y como poco, según la distancia.</summary>
    public const double MinDuration = 0.16;

    public const double MaxDuration = 0.5;

    /// <summary>Lo que se puede mover la ventana durante el vuelo sin que falle (px).</summary>
    public const double Tolerance = 14;

    /// <summary>Un disparo a <paramref name="to"/> a la velocidad que toque por la distancia.</summary>
    /// <param name="speed">Píxeles por segundo a los que vuela (a escala 1; se multiplica por <paramref name="scale"/>).</param>
    public static HookShot Aim(Vec2 from, Vec2 to, double scale, double speed = 1500, double gravity = 1200)
    {
        var duration = Math.Clamp(Vec2.Distance(from, to) / Math.Max(1, speed * scale), MinDuration, MaxDuration);
        return new HookShot(from, to, duration, gravity * scale);
    }

    /// <summary>La velocidad con la que sale: la que, con la gravedad, lo deja en <see cref="To"/> a los <see cref="Duration"/> s.</summary>
    public Vec2 Velocity => new((To.X - From.X) / Duration, (To.Y - From.Y - 0.5 * Gravity * Duration * Duration) / Duration);

    /// <summary>Dónde está a los <paramref name="t"/> segundos (pasado el final sigue la parábola).</summary>
    public Vec2 At(double t)
    {
        var v = Velocity;
        return new Vec2(From.X + v.X * t, From.Y + v.Y * t + 0.5 * Gravity * t * t);
    }

    /// <summary>Hacia dónde va a los <paramref name="t"/> segundos (para girar la punta del gancho).</summary>
    public Vec2 Heading(double t)
    {
        var v = Velocity;
        return new Vec2(v.X, v.Y + Gravity * t).Normalized();
    }

    /// <summary>
    /// Si se engancha: el punto al que apuntó sigue donde estaba (la ventana no se ha movido más que
    /// <see cref="Tolerance"/>). <paramref name="now"/> es dónde está ahora ese punto de la ventana (null: ya no está).
    /// </summary>
    public bool Hits(Vec2? now) => now is { } point && Vec2.Distance(point, To) <= Tolerance;
}
