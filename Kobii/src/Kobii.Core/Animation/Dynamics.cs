using Kobii.Core.Geometry;

namespace Kobii.Core.Animation;

/// <summary>
/// Sistema de segundo orden (un muelle con amortiguación) que persigue un valor: le da inercia a lo que
/// mueve la animación. Con <c>z</c> &lt; 1 se pasa un poco y vuelve (el remate de un gesto); con
/// <c>r</c> &gt; 0 reacciona antes de moverse (la anticipación de los dibujos animados).
/// </summary>
/// <remarks>Fórmula de «Giving Personality to Procedural Animations» (t3ssel8r), estable con cualquier paso.</remarks>
public struct SecondOrder
{
    private readonly double _k1;
    private readonly double _k2;
    private readonly double _k3;
    private double _previous;
    private double _velocity;
    private bool _started;

    /// <param name="f">Frecuencia natural (Hz): lo rápido que responde.</param>
    /// <param name="z">Amortiguación: 0 vibra sin parar, 1 llega sin pasarse.</param>
    /// <param name="r">Respuesta inicial: 0 arranca despacio, 1 al momento, &gt;1 se adelanta.</param>
    public SecondOrder(double f, double z, double r)
    {
        _k1 = z / (Math.PI * f);
        _k2 = 1 / (2 * Math.PI * f * (2 * Math.PI * f));
        _k3 = r * z / (2 * Math.PI * f);
        _previous = 0;
        _velocity = 0;
        Value = 0;
        _started = false;
    }

    public double Value { get; private set; }

    public double Step(double dt, double target)
    {
        if (!_started || dt <= 0)
        {
            Reset(target);
            return Value;
        }

        var targetVelocity = (target - _previous) / dt;
        _previous = target;
        var k2 = Math.Max(_k2, Math.Max(dt * dt / 2 + dt * _k1 / 2, dt * _k1));
        Value += dt * _velocity;
        _velocity += dt * (target + _k3 * targetVelocity - Value - _k1 * _velocity) / k2;
        return Value;
    }

    /// <summary>Salta al valor sin inercia.</summary>
    public void Reset(double value)
    {
        _previous = value;
        Value = value;
        _velocity = 0;
        _started = true;
    }

    /// <summary>Desplaza el valor y el objetivo a la vez, sin tocar la velocidad (dar la vuelta a un ángulo).</summary>
    public void Shift(double delta)
    {
        _previous += delta;
        Value += delta;
    }
}

/// <summary>Lo mismo que <see cref="SecondOrder"/> para un punto.</summary>
public struct SecondOrder2(double f, double z, double r)
{
    private SecondOrder _x = new(f, z, r);
    private SecondOrder _y = new(f, z, r);

    public readonly Vec2 Value => new(_x.Value, _y.Value);

    public Vec2 Step(double dt, Vec2 target) => new(_x.Step(dt, target.X), _y.Step(dt, target.Y));

    public void Reset(Vec2 value)
    {
        _x.Reset(value.X);
        _y.Reset(value.Y);
    }
}

/// <summary>
/// Un muelle que se mueve por las sacudidas del cuerpo (el brote y las orejas al andar, al parar o al caer).
/// Vuelve solo a cero.
/// </summary>
public struct Jiggle(double stiffness, double damping)
{
    private double _velocity;

    public double Value { get; private set; }

    public double Step(double dt, double force)
    {
        _velocity += (force - stiffness * Value - damping * _velocity) * dt;
        Value += _velocity * dt;
        Value = Math.Clamp(Value, -1.2, 1.2);
        return Value;
    }

    public void Reset()
    {
        Value = 0;
        _velocity = 0;
    }
}
