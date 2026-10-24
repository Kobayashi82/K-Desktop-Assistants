using Kumo.Core.Cloud;
using Kumo.Core.Geometry;

namespace Kumo.Assistant.Rendering;

/// <summary>
/// Lo que hace falta para dibujar un fotograma de Kumo, sacado de la nube: los contornos del campo (el del cuerpo y los anillos
/// de su borde blando), las bolas para el sombreado, la cara y lo que se ve. Todo en unidades de Kumo relativas a
/// <see cref="Origin"/> (píxeles).
/// </summary>
internal sealed class KumoFrame
{
    /// <summary>Lado de la celda del campo (unidades): unos 2,4 píxeles a escala 1.</summary>
    public const double Cell = 0.055;

    /// <summary>Distancia mínima entre dos puntos de un contorno (unidades).</summary>
    public const double Spacing = 0.07;

    /// <summary>Distancia mínima entre puntos del contorno del cuerpo en pantalla (píxeles): con menos no se ve diferencia.</summary>
    public const double CorePixels = 5;

    /// <summary>Lo mismo en los anillos del borde blando (borrosos: aguantan más).</summary>
    public const double RingPixels = 7;

    /// <summary>
    /// Dónde va cada anillo del borde entre lo que ya no se ve (0) y el cuerpo (1), y lo opaco que es: juntos dan un borde que
    /// se difumina como el del boceto (smoothstep), sin bruma.
    /// </summary>
    public static readonly (double Level, double Opacity)[] RingLevels = [(0.12, 0.152), (0.36, 0.393), (0.62, 0.786)];

    private readonly CloudField _field = new();
    private readonly FieldBlob[] _blobs = new FieldBlob[KumoCloud.BallCount + 1 + 24];

    public Vec2 Origin { get; private set; }

    public double Unit { get; private set; } = 44;

    /// <summary>El cuerpo (lo opaco): uno o varios bucles (las nubecitas que ya se han separado).</summary>
    public List<Vec2[]> Core { get; } = [];

    /// <summary>
    /// El cuerpo encogido <see cref="RimWidth"/> hacia dentro: entre él y el cuerpo va el borde iluminado (una banda rellena;
    /// antes era un trazo recortado al cuerpo, que en el host cuesta una capa y ensanchar el trazo en cada fotograma). Los trozos
    /// demasiado pequeños no tienen: son borde enteros.
    /// </summary>
    public List<Vec2[]> Inset { get; } = [];

    /// <summary>Lo ancho que se ve el borde iluminado (unidades).</summary>
    public const double RimWidth = 0.06;

    /// <summary>Los anillos del borde blando, de fuera adentro.</summary>
    public List<Vec2[]>[] Rings { get; } = [[], [], []];

    public Ball[] Balls { get; private set; } = [];

    public Vec2 Center { get; private set; }

    public double Extent { get; private set; } = 1;

    /// <summary>Lo que ocupa todo lo que se dibuja (unidades): el campo y la sombra.</summary>
    public (double Left, double Top, double Right, double Bottom) Bounds { get; private set; }

    public double Smooth { get; private set; }

    public double Tint { get; private set; }

    public double Opacity { get; private set; } = 1;

    public double Tilt { get; private set; }

    public Vec2 Face { get; private set; }

    public double FaceScale { get; private set; } = 1;

    public Vec2 Look { get; private set; }

    public CloudEyes Eyes { get; private set; }

    public CloudMouth Mouth { get; private set; }

    public double Lid { get; private set; }

    /// <summary>Lo abierta que tiene la boca al hablar (0…1).</summary>
    public double Mouthful { get; private set; }

    public bool ShowFace { get; private set; } = true;

    public bool Dark { get; set; }

    /// <summary>Toma el estado de la nube y saca los contornos.</summary>
    /// <param name="fade">Lo que se ve además (entrar, salir), 0…1.</param>
    public void Update(KumoCloud cloud, double fade)
    {
        Unit = cloud.Unit;
        Origin = cloud.RenderPosition;
        Balls = cloud.Balls;
        Smooth = cloud.Smooth;
        Tint = cloud.Tint;
        Tilt = cloud.Tilt;
        Opacity = Math.Clamp(cloud.Alpha * fade, 0, 1);
        var core = cloud.Balls[0];
        Face = new Vec2(core.X + cloud.FaceOffset.X, core.Y + cloud.FaceOffset.Y);
        FaceScale = cloud.FaceScale;
        Look = cloud.Look;
        Eyes = cloud.Eyes;
        Mouth = cloud.Mouth;
        Lid = cloud.Lid;
        Mouthful = cloud.VoiceLevel > 0.02 ? Math.Clamp(cloud.VoiceLevel * 1.4, 0, 1) : 0.35 + 0.35 * Math.Abs(Math.Sin(cloud.Time * 13));
        ShowFace = cloud.Alpha > 0.15;
        (Center, Extent) = cloud.Centroid();

        var n = cloud.Blobs(_blobs);
        _field.Build(_blobs.AsSpan(0, n), cloud.FieldReach, Cell);
        var soft = cloud.Soft;
        var e0 = Math.Max(0.04, 1 - soft);
        var e1 = 1 + soft * 1.1 + 0.02;
        // Los puntos, por píxeles y no por unidades: las curvas pasan suaves por ellos, y cada uno cuesta al dibujar.
        var coreSpacing = Math.Max(Spacing, CorePixels / Unit);
        var ringSpacing = Math.Max(Spacing, RingPixels / Unit);
        Core.Clear();
        _field.Contours(e1, coreSpacing, Core);
        Inset.Clear();
        foreach (var loop in Core)
        {
            if (Offset(loop, e1, RimWidth) is { } inner)
            {
                Inset.Add(inner);
            }
        }

        for (var i = 0; i < Rings.Length; i++)
        {
            Rings[i].Clear();
            _field.Contours(e0 + RingLevels[i].Level * (e1 - e0), ringSpacing, Rings[i]);
        }

        // El campo y la sombra de debajo (la de KumoPainter.Draw).
        var l = Math.Min(_field.Origin.X, Center.X + 0.1 - Extent * 1.15);
        var t = _field.Origin.Y;
        var r = Math.Max(_field.Origin.X + _field.Columns * Cell, Center.X + 0.1 + Extent * 1.15);
        var b = Math.Max(_field.Origin.Y + _field.Rows * Cell, Center.Y + 0.38 + Extent * 0.8);
        Bounds = (l, t, r, b);
    }

    /// <summary>
    /// El bucle desplazado <paramref name="distance"/> hacia su interior (lo que queda por encima de <paramref name="level"/>),
    /// cada punto por la normal de su tramo; null si es tan pequeño que se daría la vuelta.
    /// </summary>
    private Vec2[]? Offset(Vec2[] loop, double level, double distance)
    {
        var n = loop.Length;
        if (n < 6 || Math.Abs(CloudField.Area(loop)) < distance * distance * 5)
        {
            return null;
        }

        static Vec2 Normal(Vec2[] points, int i)
        {
            var n = points.Length;
            var d = points[(i + 1) % n] - points[(i - 1 + n) % n];
            var length = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            return length < 1e-9 ? new Vec2(0, 0) : new Vec2(-d.Y / length, d.X / length);
        }

        // De qué lado queda el interior: el mismo en todo el bucle (sale de marching squares, siempre con el mismo sentido).
        var probe = Normal(loop, 0) * (distance * 0.5);
        var side = _field.ValueAt(loop[0] + probe) >= _field.ValueAt(loop[0] - probe) ? distance : -distance;
        // Nunca más allá de la mitad de lo que tiene de cuerpo delante: en un puente fino (una nubecita que se separa, la punta
        // del dedo) los dos lados se cruzarían y la banda saldría por fuera como triángulos.
        var step = distance / 4;
        var inner = new Vec2[n];
        for (var i = 0; i < n; i++)
        {
            var normal = Normal(loop, i) * Math.Sign(side);
            var depth = step;
            while (depth < distance * 2 && _field.ValueAt(loop[i] + normal * depth) >= level)
            {
                depth += step;
            }

            inner[i] = loop[i] + normal * Math.Min(distance, (depth - step) / 2);
        }

        return inner;
    }

    /// <summary>Lo que ocupa en pantalla (píxeles), redondeado hacia fuera.</summary>
    public (int Left, int Top, int Width, int Height) PixelBounds()
    {
        var left = (int)Math.Floor(Origin.X + Bounds.Left * Unit);
        var top = (int)Math.Floor(Origin.Y + Bounds.Top * Unit);
        var right = (int)Math.Ceiling(Origin.X + Bounds.Right * Unit);
        var bottom = (int)Math.Ceiling(Origin.Y + Bounds.Bottom * Unit);
        return (left, top, Math.Clamp(right - left, 1, 4096), Math.Clamp(bottom - top, 1, 4096));
    }
}
