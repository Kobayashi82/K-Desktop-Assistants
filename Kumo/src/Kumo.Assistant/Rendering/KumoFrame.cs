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
        Core.Clear();
        _field.Contours(e1, Spacing, Core);
        for (var i = 0; i < Rings.Length; i++)
        {
            Rings[i].Clear();
            _field.Contours(e0 + RingLevels[i].Level * (e1 - e0), Spacing, Rings[i]);
        }

        // El campo y la sombra de debajo (la de KumoPainter.Draw).
        var l = Math.Min(_field.Origin.X, Center.X + 0.1 - Extent * 1.15);
        var t = _field.Origin.Y;
        var r = Math.Max(_field.Origin.X + _field.Columns * Cell, Center.X + 0.1 + Extent * 1.15);
        var b = Math.Max(_field.Origin.Y + _field.Rows * Cell, Center.Y + 0.38 + Extent * 0.8);
        Bounds = (l, t, r, b);
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
