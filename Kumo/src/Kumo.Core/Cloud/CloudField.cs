using Kumo.Core.Geometry;

namespace Kumo.Core.Cloud;

/// <summary>
/// Una bola del campo (en unidades de Kumo). Con <see cref="HalfWidth"/> y <see cref="HalfHeight"/> a cero es una esfera; si no,
/// un rectángulo con las esquinas redondeadas de radio <see cref="Radius"/> (el bocadillo, lo que abraza una ventana).
/// </summary>
public readonly record struct FieldBlob(double X, double Y, double Radius, double HalfWidth = 0, double HalfHeight = 0)
{
    /// <summary>La distancia del punto al «hueso» de la bola (su centro, o el rectángulo sin las esquinas).</summary>
    public double CoreDistanceSquared(double x, double y)
    {
        var dx = Math.Max(Math.Abs(x - X) - HalfWidth, 0);
        var dy = Math.Max(Math.Abs(y - Y) - HalfHeight, 0);
        return dx * dx + dy * dy;
    }
}

/// <summary>
/// El campo de bolas de la nube y sus contornos. Cada bola suma <c>(1 − d²/R²)³</c> hasta su alcance <c>R = r × alcance</c>,
/// escalado para valer 1 justo en su radio: el borde de una bola sola es su círculo y, juntas, se funden con curvas suaves
/// (las nubecitas que se separan forman un puente que se estrecha y se rompe). El campo se evalúa en una rejilla y los contornos
/// salen con marching squares (vale para cualquier forma, también en varios trozos).
/// </summary>
/// <remarks>Sin memoria nueva en cada fotograma: la rejilla y las listas se reutilizan.</remarks>
public sealed class CloudField
{
    private float[] _values = [];
    private int[] _first = [];
    private int[] _second = [];
    private bool[] _visited = [];
    private readonly List<Vec2> _points = [];

    /// <summary>Esquina de arriba a la izquierda de la rejilla (unidades).</summary>
    public Vec2 Origin { get; private set; }

    /// <summary>Lado de una celda (unidades).</summary>
    public double Cell { get; private set; }

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    /// <summary>Lo que llegan las bolas más allá de su radio (1,34 = el de la nube; más, más blanda).</summary>
    public double Reach { get; private set; } = 1.34;

    /// <summary>Evalúa el campo de estas bolas en una rejilla de celdas de <paramref name="cell"/> unidades.</summary>
    public void Build(ReadOnlySpan<FieldBlob> blobs, double reach, double cell)
    {
        Reach = Math.Max(1.05, reach);
        Cell = Math.Max(0.005, cell);
        var invT = 1 / Math.Pow(1 - 1 / (Reach * Reach), 3);

        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        var any = false;
        foreach (var b in blobs)
        {
            if (b.Radius <= 1e-4)
            {
                continue;
            }

            any = true;
            var r = b.Radius * Reach;
            x0 = Math.Min(x0, b.X - b.HalfWidth - r);
            y0 = Math.Min(y0, b.Y - b.HalfHeight - r);
            x1 = Math.Max(x1, b.X + b.HalfWidth + r);
            y1 = Math.Max(y1, b.Y + b.HalfHeight + r);
        }

        if (!any)
        {
            Columns = Rows = 0;
            return;
        }

        // Una celda de margen alrededor: el campo vale 0 en el borde de la rejilla y todos los contornos se cierran.
        Origin = new Vec2(x0 - Cell, y0 - Cell);
        Columns = (int)Math.Ceiling((x1 - x0) / Cell) + 3;
        Rows = (int)Math.Ceiling((y1 - y0) / Cell) + 3;
        var count = Columns * Rows;
        if (_values.Length < count)
        {
            _values = new float[count + count / 2];
        }

        Array.Clear(_values, 0, count);
        foreach (var b in blobs)
        {
            if (b.Radius <= 1e-4)
            {
                continue;
            }

            var reachR = b.Radius * Reach;
            var reach2 = reachR * reachR;
            var inv = 1 / reach2;
            var i0 = Math.Max(0, (int)Math.Floor((b.X - b.HalfWidth - reachR - Origin.X) / Cell));
            var i1 = Math.Min(Columns - 1, (int)Math.Ceiling((b.X + b.HalfWidth + reachR - Origin.X) / Cell));
            var j0 = Math.Max(0, (int)Math.Floor((b.Y - b.HalfHeight - reachR - Origin.Y) / Cell));
            var j1 = Math.Min(Rows - 1, (int)Math.Ceiling((b.Y + b.HalfHeight + reachR - Origin.Y) / Cell));
            for (var j = j0; j <= j1; j++)
            {
                var y = Origin.Y + j * Cell;
                var dy = Math.Max(Math.Abs(y - b.Y) - b.HalfHeight, 0);
                var dy2 = dy * dy;
                if (dy2 >= reach2)
                {
                    continue;
                }

                var row = j * Columns;
                for (var i = i0; i <= i1; i++)
                {
                    var x = Origin.X + i * Cell;
                    var dx = Math.Max(Math.Abs(x - b.X) - b.HalfWidth, 0);
                    var d2 = dx * dx + dy2;
                    if (d2 >= reach2)
                    {
                        continue;
                    }

                    var u = 1 - d2 * inv;
                    _values[row + i] += (float)(u * u * u * invT);
                }
            }
        }
    }

    /// <summary>El valor del campo en un punto (interpolado en la rejilla).</summary>
    public double ValueAt(Vec2 p)
    {
        if (Columns < 2 || Rows < 2)
        {
            return 0;
        }

        var gx = (p.X - Origin.X) / Cell;
        var gy = (p.Y - Origin.Y) / Cell;
        if (gx < 0 || gy < 0 || gx >= Columns - 1 || gy >= Rows - 1)
        {
            return 0;
        }

        var i = (int)gx;
        var j = (int)gy;
        var fx = gx - i;
        var fy = gy - j;
        var a = _values[j * Columns + i];
        var b = _values[j * Columns + i + 1];
        var c = _values[(j + 1) * Columns + i];
        var d = _values[(j + 1) * Columns + i + 1];
        return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
    }

    /// <summary>
    /// Los contornos del campo al nivel <paramref name="level"/> (1 = el radio de las bolas; menos, más afuera): bucles cerrados,
    /// cada uno con sus puntos separados al menos <paramref name="spacing"/> unidades. Se añaden a <paramref name="loops"/>.
    /// </summary>
    public void Contours(double level, double spacing, List<Vec2[]> loops)
    {
        if (Columns < 2 || Rows < 2)
        {
            return;
        }

        var keys = Columns * Rows * 2;
        if (_first.Length < keys)
        {
            _first = new int[keys + keys / 2];
            _second = new int[_first.Length];
            _visited = new bool[_first.Length];
        }

        Array.Fill(_first, -1, 0, keys);
        Array.Fill(_second, -1, 0, keys);
        Array.Clear(_visited, 0, keys);
        var lv = (float)level;
        for (var j = 0; j < Rows - 1; j++)
        {
            for (var i = 0; i < Columns - 1; i++)
            {
                var v0 = _values[j * Columns + i];
                var v1 = _values[j * Columns + i + 1];
                var v2 = _values[(j + 1) * Columns + i + 1];
                var v3 = _values[(j + 1) * Columns + i];
                var index = (v0 >= lv ? 1 : 0) | (v1 >= lv ? 2 : 0) | (v2 >= lv ? 4 : 0) | (v3 >= lv ? 8 : 0);
                if (index is 0 or 15)
                {
                    continue;
                }

                var top = Horizontal(i, j);
                var bottom = Horizontal(i, j + 1);
                var left = Vertical(i, j);
                var right = Vertical(i + 1, j);
                switch (index)
                {
                    case 1 or 14: Link(left, top); break;
                    case 2 or 13: Link(top, right); break;
                    case 4 or 11: Link(right, bottom); break;
                    case 8 or 7: Link(bottom, left); break;
                    case 3 or 12: Link(left, right); break;
                    case 6 or 9: Link(top, bottom); break;
                    case 5:
                        // Esquinas 0 y 2 dentro: si el centro también, están unidas y se rodean las otras dos.
                        if ((v0 + v1 + v2 + v3) / 4 >= lv)
                        {
                            Link(top, right);
                            Link(bottom, left);
                        }
                        else
                        {
                            Link(left, top);
                            Link(right, bottom);
                        }

                        break;
                    case 10:
                        if ((v0 + v1 + v2 + v3) / 4 >= lv)
                        {
                            Link(left, top);
                            Link(right, bottom);
                        }
                        else
                        {
                            Link(top, right);
                            Link(bottom, left);
                        }

                        break;
                }
            }
        }

        var spacing2 = spacing * spacing;
        for (var start = 0; start < keys; start++)
        {
            if (_first[start] < 0 || _visited[start])
            {
                continue;
            }

            _points.Clear();
            var previous = -1;
            var current = start;
            while (current >= 0 && !_visited[current])
            {
                _visited[current] = true;
                var p = PointOf(current, lv);
                if (_points.Count == 0 || (p - _points[^1]).LengthSquared >= spacing2)
                {
                    _points.Add(p);
                }

                var next = _first[current] != previous ? _first[current] : _second[current];
                previous = current;
                current = next;
            }

            // El último punto, si quedó pegado al primero, sobra.
            if (_points.Count > 3 && (_points[^1] - _points[0]).LengthSquared < spacing2 * 0.25)
            {
                _points.RemoveAt(_points.Count - 1);
            }

            if (_points.Count >= 4)
            {
                loops.Add([.. _points]);
            }
        }
    }

    private int Horizontal(int i, int j) => (j * Columns + i) * 2;

    private int Vertical(int i, int j) => (j * Columns + i) * 2 + 1;

    private void Link(int a, int b)
    {
        Attach(a, b);
        Attach(b, a);
    }

    private void Attach(int key, int other)
    {
        if (_first[key] < 0)
        {
            _first[key] = other;
        }
        else if (_second[key] < 0 && _first[key] != other)
        {
            _second[key] = other;
        }
    }

    /// <summary>Dónde corta el contorno esa arista de la rejilla (interpolando entre sus dos puntos).</summary>
    private Vec2 PointOf(int key, float level)
    {
        var cell = key >> 1;
        var i = cell % Columns;
        var j = cell / Columns;
        var a = _values[cell];
        float b;
        double ox = 0, oy = 0;
        if ((key & 1) == 0)
        {
            b = _values[cell + 1];
            ox = 1;
        }
        else
        {
            b = _values[cell + Columns];
            oy = 1;
        }

        var t = Math.Abs(b - a) < 1e-9 ? 0.5 : Math.Clamp((level - a) / (b - a), 0, 1);
        return new Vec2(Origin.X + (i + ox * t) * Cell, Origin.Y + (j + oy * t) * Cell);
    }

    /// <summary>El área con signo de un bucle (positiva si va en el sentido de las agujas del reloj, con la y hacia abajo).</summary>
    public static double Area(IReadOnlyList<Vec2> loop)
    {
        var sum = 0.0;
        for (var i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum / 2;
    }
}
