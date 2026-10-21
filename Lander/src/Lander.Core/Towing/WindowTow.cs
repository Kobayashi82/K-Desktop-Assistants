using Lander.Core.Geometry;

namespace Lander.Core.Towing;

/// <summary>Un rectángulo de pantalla con decimales (la ventana mientras se remolca, antes de redondear al píxel).</summary>
public readonly record struct TowRect(double Left, double Top, double Width, double Height)
{
    public Vec2 TopLeft => new(Left, Top);

    public Vec2 Size => new(Width, Height);
}

/// <summary>
/// Una ventana remolcada por el gancho: avanza hacia su destino al ritmo al que avanza la nave hacia el suyo (si la nave
/// lleva la mitad del camino, la ventana debería llevar la mitad), pero con «masa»: la sigue con un muelle amortiguado,
/// así que arranca con algo de retraso, frena y da un pequeño rebote al llegar. Cuanto más grande, más pesada (más
/// lenta en responder). <see cref="Finish"/> la deja exactamente en su sitio.
/// </summary>
public sealed class WindowTow
{
    /// <summary>Paso fijo del muelle (s).</summary>
    private const double TimeStep = 1.0 / 240;

    /// <summary>Amortiguación del muelle: por debajo de 1 rebota un poco al llegar.</summary>
    private const double DampingRatio = 0.62;

    /// <summary>Una ventana de este área (800 × 600) responde con <see cref="BaseFrequency"/>.</summary>
    private const double ReferenceArea = 800 * 600;

    private const double BaseFrequency = 12;

    private readonly TowRect _start;
    private readonly TowRect _destination;
    private readonly Vec2 _landerStart;
    private readonly Vec2 _landerEnd;
    private Vec2 _position;
    private Vec2 _velocity;
    private double _carry;
    private readonly double _arrival;

    /// <param name="start">Dónde está la ventana al engancharla.</param>
    /// <param name="destination">Dónde tiene que quedar.</param>
    /// <param name="landerStart">Dónde está la nave al empezar a tirar.</param>
    /// <param name="landerEnd">Dónde acabará la nave cuando la ventana esté en su sitio.</param>
    /// <param name="titleOffset">A cuántos píxeles del borde de arriba va el gancho (la barra de título).</param>
    /// <param name="arrival">A esta distancia de su punto la nave ya ha llegado (nunca se queda clavada al píxel).</param>
    public WindowTow(TowRect start, TowRect destination, Vec2 landerStart, Vec2 landerEnd, double titleOffset, double arrival = 0)
    {
        _arrival = Math.Max(0, arrival);
        _start = start;
        _destination = destination;
        _landerStart = landerStart;
        _landerEnd = landerEnd;
        _position = start.TopLeft;
        TitleOffset = titleOffset;
        Current = start;

        // Más área, más masa: la frecuencia baja con la raíz cuarta (una ventana cuatro veces mayor, la mitad de viva no).
        var area = Math.Max(1, start.Width * start.Height);
        Frequency = Math.Clamp(BaseFrequency * Math.Pow(ReferenceArea / area, 0.25), 6, 18);
    }

    /// <summary>Lo viva que responde (rad/s): más pequeña, más viva.</summary>
    public double Frequency { get; }

    public double TitleOffset { get; }

    /// <summary>Lo que lleva del camino (0…1), según la nave: nunca vuelve atrás.</summary>
    public double Progress { get; private set; }

    /// <summary>Dónde está ahora (con decimales).</summary>
    public TowRect Current { get; private set; }

    public TowRect Destination => _destination;

    /// <summary>Dónde va clavado el gancho ahora: en la barra de título, al 30 % de su ancho.</summary>
    public Vec2 Hook => HookOf(Current);

    /// <summary>El punto del gancho en una ventana cualquiera.</summary>
    public Vec2 HookOf(TowRect rect) => new(rect.Left + Math.Min(rect.Width * 0.3, 260), rect.Top + TitleOffset);

    /// <summary>La nave ha llegado y la ventana está casi en su sitio y casi quieta.</summary>
    public bool Arrived => Progress >= 0.999 && Vec2.Distance(_position, _destination.TopLeft) < 1.5 && _velocity.Length < 25;

    /// <summary>Lo más lejos que se ha pasado de su destino, en la dirección del viaje (px).</summary>
    public double Overshoot { get; private set; }

    /// <summary>Avanza <paramref name="seconds"/> con la nave en <paramref name="lander"/>.</summary>
    public void Step(double seconds, Vec2 lander)
    {
        var total = Vec2.Distance(_landerStart, _landerEnd) - _arrival;
        var progress = total < 1 ? 1 : 1 - Math.Max(0, Vec2.Distance(lander, _landerEnd) - _arrival) / total;
        Progress = Math.Max(Progress, Math.Clamp(progress, 0, 1));

        var target = Vec2.Lerp(_start.TopLeft, _destination.TopLeft, Progress);
        var omega = Frequency;
        _carry = Math.Min(_carry + Math.Max(0, seconds), 0.25);
        while (_carry >= TimeStep)
        {
            _carry -= TimeStep;
            var acceleration = (target - _position) * (omega * omega) - _velocity * (2 * DampingRatio * omega);
            _velocity += acceleration * TimeStep;
            _position += _velocity * TimeStep;
        }

        var travel = _destination.TopLeft - _start.TopLeft;
        if (travel.Length > 1)
        {
            var past = Vec2.Dot(_position - _destination.TopLeft, travel / travel.Length);
            Overshoot = Math.Max(Overshoot, past);
        }

        // El tamaño cambia según avanza (colocar a la izquierda la hace más alta, por ejemplo), sin rebote.
        var s = Progress * Progress * (3 - 2 * Progress);
        var size = Vec2.Lerp(_start.Size, _destination.Size, s);
        Current = new TowRect(_position.X, _position.Y, size.X, size.Y);
    }

    /// <summary>La deja exactamente en su destino (al acabar, o si se acaba el tiempo).</summary>
    public void Finish()
    {
        Progress = 1;
        _position = _destination.TopLeft;
        _velocity = Vec2.Zero;
        Current = _destination;
    }
}
