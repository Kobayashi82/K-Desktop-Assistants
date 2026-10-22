using Kobii.Core.Geometry;

namespace Kobii.Core.Creature;

/// <summary>
/// Detecta que el usuario frota a Kobii: el cursor pasa por encima yendo y viniendo (de lado a lado o de
/// arriba abajo, como con un cepillo). Cada cambio de sentido tras un recorrido mínimo cuenta como una pasada.
/// </summary>
public sealed class RubDetector
{
    private Vec2? _last;
    private Axis _x;
    private Axis _y;
    private double _sinceStroke;

    /// <summary>Pasadas seguidas recientes (decae si deja de frotar).</summary>
    public double Intensity { get; private set; }

    /// <summary>
    /// Añade la posición del cursor. Devuelve cuántas pasadas nuevas ha habido (0 o 1).
    /// </summary>
    /// <param name="cursor">Posición del cursor, o null si no está encima.</param>
    /// <param name="minTravel">Recorrido mínimo en píxeles para que cuente una pasada.</param>
    /// <param name="dt">Tiempo desde la última llamada.</param>
    public int Update(Vec2? cursor, double minTravel, double dt)
    {
        _sinceStroke += dt;
        Intensity = Math.Max(0, Intensity - dt * 1.5);
        if (cursor is not { } point)
        {
            Reset();
            return 0;
        }

        if (_last is not { } last)
        {
            _last = point;
            return 0;
        }

        _last = point;
        var reversed = _x.Move(point.X - last.X, minTravel) | _y.Move(point.Y - last.Y, minTravel);
        if (!reversed)
        {
            return 0;
        }

        // Una pasada cuenta si llega poco después de la anterior (si no, es empezar a frotar).
        var counted = _sinceStroke < 1.2;
        _sinceStroke = 0;
        if (!counted)
        {
            return 0;
        }

        Intensity = Math.Min(4, Intensity + 1);
        return 1;
    }

    public void Reset()
    {
        _last = null;
        _x = default;
        _y = default;
    }

    /// <summary>El vaivén en un eje: cuánto ha recorrido en el sentido actual.</summary>
    private struct Axis
    {
        private int _direction;
        private double _travel;

        /// <summary>Avanza; devuelve true si cambia de sentido tras haber recorrido lo mínimo.</summary>
        public bool Move(double delta, double minTravel)
        {
            if (Math.Abs(delta) < 0.5)
            {
                return false;
            }

            var direction = Math.Sign(delta);
            if (direction == _direction || _direction == 0)
            {
                _direction = direction;
                _travel += Math.Abs(delta);
                return false;
            }

            var counted = _travel >= minTravel;
            _direction = direction;
            _travel = Math.Abs(delta);
            return counted;
        }
    }
}
