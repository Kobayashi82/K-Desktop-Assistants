using Kobii.Core.Geometry;

namespace Kobii.Core.Animation;

/// <summary>Qué partes del esqueleto toca un gesto.</summary>
[Flags]
public enum RigMask
{
    None = 0,
    LeftHand = 1,
    RightHand = 2,
    Hands = LeftHand | RightHand,
    Feet = 4,

    /// <summary>Posición de la cara (asentir, negar, olfatear).</summary>
    Face = 8,

    Body = 16,
    Head = 32,

    /// <summary>La boca: masticar, morder, inflar los mofletes, sacar la lengua.</summary>
    Mouth = 64,

    All = Hands | Feet | Face | Body | Head | Mouth,
}

/// <summary>
/// El esqueleto de la criatura en un instante, como desplazamientos respecto a su postura de reposo:
/// todo a cero es estar de pie y quieto. Lo calcula el <see cref="Animator"/> y lo dibuja el shader.
/// </summary>
/// <remarks>
/// Unidades de la criatura: mide 2 de alto, el origen está en los pies y la y crece hacia abajo. Las
/// posiciones de reposo de manos, hombros y pies (ver <see cref="RestHand"/>, <see cref="Shoulder"/> y
/// <see cref="RestFoot"/>) son las mismas que usa Creature.hlsl: si cambian allí, hay que cambiarlas aquí.
/// </remarks>
public struct Rig
{
    /// <summary>Centro de giro del cuerpo.</summary>
    public static readonly Vec2 Pivot = new(0, -0.95);

    /// <summary>Largo natural del brazo (de hombro a mano). Si la mano está más cerca, el codo se dobla.</summary>
    public const double ArmLength = 0.70;

    /// <summary>Desplazamiento del cuerpo entero (pies incluidos).</summary>
    public double OffsetX { get; set; }

    public double OffsetY { get; set; }

    /// <summary>Giro del cuerpo alrededor de <see cref="Pivot"/> (radianes; positivo, como las agujas del reloj).</summary>
    public double Spin { get; set; }

    /// <summary>Estiramiento vertical (positivo) o aplastamiento (negativo).</summary>
    public double Stretch { get; set; }

    /// <summary>Encorvado hacia delante (0–1): la postura de los mayores.</summary>
    public double Hunch { get; set; }

    /// <summary>Aplastado contra el suelo (0–1), como un mochi blando: el cuerpo se ensancha y la cara baja con él (tumbado).</summary>
    public double Melt { get; set; }

    /// <summary>Desplazamiento de cada mano desde su sitio (L: la de la izquierda de la pantalla).</summary>
    public double HandLX { get; set; }

    public double HandLY { get; set; }

    public double HandRX { get; set; }

    public double HandRY { get; set; }

    /// <summary>La mano va por delante del cuerpo (1) o a su lado, fundida con él (0). Entre medias, se funde.</summary>
    public double FrontL { get; set; }

    public double FrontR { get; set; }

    /// <summary>Enseña los bracitos aunque sea una cría (que de normal no los saca).</summary>
    public double ShowArms { get; set; }

    /// <summary>Giro del codo respecto a su sitio natural (hacia fuera y abajo), en radianes.</summary>
    public double ElbowL { get; set; }

    public double ElbowR { get; set; }

    /// <summary>Mano cerrada (1, agarrando algo) o abierta (0).</summary>
    public double GripL { get; set; }

    public double GripR { get; set; }

    public double FootLX { get; set; }

    public double FootLY { get; set; }

    public double FootRX { get; set; }

    public double FootRY { get; set; }

    /// <summary>Giro de cada pie (punta arriba o abajo al dar el paso).</summary>
    public double FootLTurn { get; set; }

    public double FootRTurn { get; set; }

    /// <summary>Los pies se dibujan por delante del cuerpo (sentado).</summary>
    public double FeetFront { get; set; }

    /// <summary>Giro de cada oreja (radianes).</summary>
    public double EarL { get; set; }

    public double EarR { get; set; }

    /// <summary>Curvatura extra del brote (positivo: se dobla hacia la derecha; caído si está triste).</summary>
    public double Sprout { get; set; }

    /// <summary>Inclinación de la cabeza (radianes).</summary>
    public double HeadTilt { get; set; }

    /// <summary>Desplazamiento de la cara (asentir, negar, olfatear).</summary>
    public double FaceX { get; set; }

    public double FaceY { get; set; }

    /// <summary>Boca abierta de par en par (0–1): a punto de dar un mordisco.</summary>
    public double MouthOpen { get; set; }

    /// <summary>Mandíbula al masticar (0 cerrada, 1 abierta): sube y baja varias veces por segundo.</summary>
    public double Chew { get; set; }

    /// <summary>Mofletes inflados (0–1), con la boca llena.</summary>
    public double Cheeks { get; set; }

    /// <summary>Lengua fuera relamiéndose (0–1); el signo dice hacia qué lado.</summary>
    public double Tongue { get; set; }

    /// <summary>Mano en reposo para <paramref name="side"/> (−1 izquierda, 1 derecha) y la forma indicada.</summary>
    public static Vec2 RestHand(double side, double form) => new(side * (0.96 - 0.04 * Grow(form)), -0.54);

    /// <summary>Hombro (donde nace el brazo). Al girarse, los hombros se desplazan con la cara (vista de tres cuartos).</summary>
    public static Vec2 Shoulder(double side, double form, double facing = 0) => new(side * (0.70 - 0.04 * Grow(form)) + facing * 0.14, -0.92);

    public static Vec2 RestFoot(double side) => new(side * 0.40, -0.10);

    /// <summary>De 0 (cría) a 1 (adulto): cuánto ha crecido la forma.</summary>
    public static double Grow(double form) => Math.Clamp((form - 1) / 3, 0, 1);

    /// <summary>Posición de una mano en el espacio de la criatura (sin el giro ni el desplazamiento del cuerpo).</summary>
    public readonly Vec2 Hand(double side, double form) =>
        RestHand(side, form) + (side < 0 ? new Vec2(HandLX, HandLY) : new Vec2(HandRX, HandRY));

    public readonly Vec2 Foot(double side) =>
        RestFoot(side) + (side < 0 ? new Vec2(FootLX, FootLY) : new Vec2(FootRX, FootRY));

    public readonly double Front(double side) => side < 0 ? FrontL : FrontR;

    public readonly double Grip(double side) => side < 0 ? GripL : GripR;

    /// <summary>
    /// Codo de un brazo (el mismo cálculo que el shader): si la mano está más cerca que el largo del brazo,
    /// el codo sale hacia fuera y abajo, girado lo que diga <see cref="ElbowL"/> o <see cref="ElbowR"/>.
    /// </summary>
    public readonly Vec2 Elbow(double side, double form, double facing = 0)
    {
        var shoulder = Shoulder(side, form, facing);
        var hand = Hand(side, form);
        var reach = hand - shoulder;
        var d = reach.Length;
        var middle = (shoulder + hand) * 0.5;
        if (d < 1e-6)
        {
            return middle;
        }

        var h = Math.Sqrt(Math.Max(0, ArmLength * ArmLength / 4 - d * d / 4)) * BendAmount(side, form);
        var normal = new Vec2(-reach.Y, reach.X) / d;
        var outward = Rotate(new Vec2(side, 0.7), side * (side < 0 ? ElbowL : ElbowR));
        if (Vec2.Dot(normal, outward) < 0)
        {
            normal = -normal;
        }

        return middle + normal * h;
    }

    /// <summary>
    /// Cuánto se dobla el codo: en reposo el brazo cuelga casi recto (si no, parecerían alitas) y se dobla del
    /// todo en cuanto la mano se aparta un poco de su sitio.
    /// </summary>
    public readonly double BendAmount(double side, double form)
    {
        var away = (Hand(side, form) - RestHand(side, form)).Length;
        var u = Math.Clamp(away / 0.3, 0, 1);
        return 0.2 + 0.8 * u * u * (3 - 2 * u);
    }

    /// <summary>Lleva un punto del espacio del cuerpo al de la criatura (aplica el giro y el desplazamiento).</summary>
    public readonly Vec2 Place(Vec2 local)
    {
        var p = local - Pivot;
        var c = Math.Cos(Spin);
        var s = Math.Sin(Spin);
        return new Vec2(c * p.X - s * p.Y, s * p.X + c * p.Y) + Pivot + new Vec2(OffsetX, OffsetY);
    }

    public static Rig Lerp(in Rig a, in Rig b, double t) => new()
    {
        OffsetX = L(a.OffsetX, b.OffsetX, t),
        OffsetY = L(a.OffsetY, b.OffsetY, t),
        Spin = L(a.Spin, b.Spin, t),
        Stretch = L(a.Stretch, b.Stretch, t),
        Hunch = L(a.Hunch, b.Hunch, t),
        Melt = L(a.Melt, b.Melt, t),
        HandLX = L(a.HandLX, b.HandLX, t),
        HandLY = L(a.HandLY, b.HandLY, t),
        HandRX = L(a.HandRX, b.HandRX, t),
        HandRY = L(a.HandRY, b.HandRY, t),
        FrontL = L(a.FrontL, b.FrontL, t),
        FrontR = L(a.FrontR, b.FrontR, t),
        ShowArms = L(a.ShowArms, b.ShowArms, t),
        ElbowL = L(a.ElbowL, b.ElbowL, t),
        ElbowR = L(a.ElbowR, b.ElbowR, t),
        GripL = L(a.GripL, b.GripL, t),
        GripR = L(a.GripR, b.GripR, t),
        FootLX = L(a.FootLX, b.FootLX, t),
        FootLY = L(a.FootLY, b.FootLY, t),
        FootRX = L(a.FootRX, b.FootRX, t),
        FootRY = L(a.FootRY, b.FootRY, t),
        FootLTurn = L(a.FootLTurn, b.FootLTurn, t),
        FootRTurn = L(a.FootRTurn, b.FootRTurn, t),
        FeetFront = L(a.FeetFront, b.FeetFront, t),
        EarL = L(a.EarL, b.EarL, t),
        EarR = L(a.EarR, b.EarR, t),
        Sprout = L(a.Sprout, b.Sprout, t),
        HeadTilt = L(a.HeadTilt, b.HeadTilt, t),
        FaceX = L(a.FaceX, b.FaceX, t),
        FaceY = L(a.FaceY, b.FaceY, t),
        MouthOpen = L(a.MouthOpen, b.MouthOpen, t),
        Chew = L(a.Chew, b.Chew, t),
        Cheeks = L(a.Cheeks, b.Cheeks, t),
        Tongue = L(a.Tongue, b.Tongue, t),
    };

    /// <summary>
    /// Interpola entre dos fotogramas de la simulación para dibujar entre medias. Los giros se toman por el
    /// camino corto (el cuerpo puede dar vueltas enteras al rodar).
    /// </summary>
    public static Rig Interpolate(in Rig previous, in Rig current, double t)
    {
        var from = previous;
        from.Spin = current.Spin + MathUtil.WrapAngle(previous.Spin - current.Spin);
        return Lerp(from, current, t);
    }

    /// <summary>Mezcla encima las partes de <paramref name="overlay"/> que indica la máscara, con peso <paramref name="w"/>.</summary>
    public static Rig Mix(in Rig under, in Rig overlay, RigMask mask, double w)
    {
        if (w <= 0 || mask == RigMask.None)
        {
            return under;
        }

        var mixed = Lerp(under, overlay, Math.Min(1, w));
        var r = under;
        if (mask.HasFlag(RigMask.LeftHand))
        {
            (r.HandLX, r.HandLY, r.FrontL, r.ElbowL, r.GripL) = (mixed.HandLX, mixed.HandLY, mixed.FrontL, mixed.ElbowL, mixed.GripL);
        }

        if (mask.HasFlag(RigMask.RightHand))
        {
            (r.HandRX, r.HandRY, r.FrontR, r.ElbowR, r.GripR) = (mixed.HandRX, mixed.HandRY, mixed.FrontR, mixed.ElbowR, mixed.GripR);
        }

        if ((mask & RigMask.Hands) != 0)
        {
            r.ShowArms = Math.Max(under.ShowArms, mixed.ShowArms);
        }

        if (mask.HasFlag(RigMask.Feet))
        {
            (r.FootLX, r.FootLY, r.FootRX, r.FootRY, r.FeetFront) = (mixed.FootLX, mixed.FootLY, mixed.FootRX, mixed.FootRY, mixed.FeetFront);
            (r.FootLTurn, r.FootRTurn) = (mixed.FootLTurn, mixed.FootRTurn);
        }

        if (mask.HasFlag(RigMask.Face))
        {
            (r.FaceX, r.FaceY) = (mixed.FaceX, mixed.FaceY);
        }

        if (mask.HasFlag(RigMask.Head))
        {
            (r.HeadTilt, r.EarL, r.EarR, r.Sprout) = (mixed.HeadTilt, mixed.EarL, mixed.EarR, mixed.Sprout);
        }

        if (mask.HasFlag(RigMask.Body))
        {
            (r.OffsetX, r.OffsetY, r.Spin, r.Stretch, r.Melt) = (mixed.OffsetX, mixed.OffsetY, mixed.Spin, mixed.Stretch, mixed.Melt);
        }

        if (mask.HasFlag(RigMask.Mouth))
        {
            (r.MouthOpen, r.Chew, r.Cheeks, r.Tongue) = (mixed.MouthOpen, mixed.Chew, mixed.Cheeks, mixed.Tongue);
        }

        return r;
    }

    /// <summary>Lleva la mano de un lado a una posición absoluta del espacio de la criatura.</summary>
    public void SetHand(double side, Vec2 target, double form, bool front, double grip = 0)
    {
        var delta = target - RestHand(side, form);
        if (side < 0)
        {
            (HandLX, HandLY, FrontL, GripL) = (delta.X, delta.Y, front ? 1 : 0, grip);
        }
        else
        {
            (HandRX, HandRY, FrontR, GripR) = (delta.X, delta.Y, front ? 1 : 0, grip);
        }

        ShowArms = 1;
    }

    /// <summary>Cierra (o abre) la mano de un lado sin moverla.</summary>
    public void SetGrip(double side, double grip)
    {
        if (side < 0)
        {
            GripL = grip;
        }
        else
        {
            GripR = grip;
        }
    }

    private static double L(double a, double b, double t) => a + (b - a) * t;

    private static Vec2 Rotate(Vec2 v, double angle)
    {
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        return new Vec2(c * v.X - s * v.Y, s * v.X + c * v.Y);
    }
}

/// <summary>Cambios de espacio de la criatura, los mismos que hace el shader.</summary>
public static class CreatureSpace
{
    /// <summary>
    /// Lleva un punto del espacio del cuerpo al mundo (píxeles): giro y desplazamiento del esqueleto,
    /// aplastamiento alrededor de los pies e inclinación.
    /// </summary>
    /// <param name="local">Punto en unidades de la criatura (mide 2 de alto; y hacia abajo).</param>
    /// <param name="rig">Esqueleto.</param>
    /// <param name="feet">Pies en el mundo.</param>
    /// <param name="unit">Píxeles por unidad (la mitad de la altura).</param>
    /// <param name="squash">Aplastamiento total (1 normal), el mismo que recibe el shader.</param>
    /// <param name="lean">Inclinación en radianes.</param>
    public static Vec2 ToWorld(Vec2 local, in Rig rig, Vec2 feet, double unit, double squash, double lean)
    {
        var p = rig.Place(local);
        var sx = 1 / Math.Sqrt(Math.Max(squash, 0.3));
        p = new Vec2(p.X * sx, p.Y * squash);
        var c = Math.Cos(lean);
        var s = Math.Sin(lean);
        return feet + new Vec2(c * p.X - s * p.Y, s * p.X + c * p.Y) * unit;
    }

    /// <summary>Lo contrario de <see cref="ToWorld"/>: un punto del mundo en el espacio del cuerpo.</summary>
    public static Vec2 ToLocal(Vec2 world, in Rig rig, Vec2 feet, double unit, double squash, double lean)
    {
        var p = (world - feet) / Math.Max(1e-6, unit);
        var c = Math.Cos(-lean);
        var s = Math.Sin(-lean);
        p = new Vec2(c * p.X - s * p.Y, s * p.X + c * p.Y);
        var sx = 1 / Math.Sqrt(Math.Max(squash, 0.3));
        p = new Vec2(p.X / sx, p.Y / Math.Max(1e-6, squash));
        p -= new Vec2(rig.OffsetX, rig.OffsetY);
        var cs = Math.Cos(-rig.Spin);
        var sn = Math.Sin(-rig.Spin);
        var q = p - Rig.Pivot;
        return new Vec2(cs * q.X - sn * q.Y, sn * q.X + cs * q.Y) + Rig.Pivot;
    }
}
