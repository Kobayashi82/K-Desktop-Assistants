using Kobii.Core.Geometry;

namespace Kobii.Core.Animation;

/// <summary>Gestos: la capa que va encima de la postura (manos, cara, cabeza…).</summary>
public enum Gesture
{
    None,
    Wave,
    WaveBoth,
    Clap,

    /// <summary>Rascarse la cabeza.</summary>
    Scratch,

    Yawn,
    Point,
    CoverEyes,

    /// <summary>Cucú-tras: se tapa los ojos y se destapa.</summary>
    PeekABoo,

    RubEyes,
    ArmsUp,
    HoldBelly,

    /// <summary>La mano en la barbilla.</summary>
    Think,

    /// <summary>Encogerse de hombros.</summary>
    Shrug,

    /// <summary>Abrazarse (frío, pena o mimos).</summary>
    Hug,

    Nod,
    ShakeHead,
    Sniff,

    /// <summary>Menear el culete.</summary>
    Wiggle,

    /// <summary>Reverencia.</summary>
    Bow,

    /// <summary>Tamborilear sobre la barriga.</summary>
    Drum,

    /// <summary>Dirigir una música imaginaria (canturrear).</summary>
    Conduct,

    /// <summary>Sujetar algo con la mano.</summary>
    Hold,

    /// <summary>Sujetar algo en alto (un globo, una hoja como paraguas).</summary>
    HoldUp,

    /// <summary>Sujetar algo delante con las dos manos (un libro, la pelota).</summary>
    HoldBoth,

    /// <summary>Beber de una taza a sorbitos.</summary>
    Sip,

    /// <summary>Soplar (pompas, un beso).</summary>
    Blow,

    Paint,

    /// <summary>Dar golpecitos con el pie (impaciente).</summary>
    TapFoot,

    /// <summary>Llamarte con la mano: «ven».</summary>
    Beckon,

    LookAround,

    /// <summary>Agitar los brazos (un susto).</summary>
    Flail,

    /// <summary>Botar la pelota.</summary>
    Dribble,

    /// <summary>Oler algo que tiene en la mano.</summary>
    Smell,

    /// <summary>Sujetar el micrófono cerca de la boca.</summary>
    Sing,

    /// <summary>Las manos a los riñones (a los mayores les duele la espalda).</summary>
    Back,

    /// <summary>Le hacen cosquillas: se retuerce de la risa tapándose la barriga.</summary>
    Tickled,

    /// <summary>Ofrece la mano para que se la estreches.</summary>
    Offer,

    /// <summary>Estrecharte la mano (arriba y abajo).</summary>
    ShakeHands,

    /// <summary>Chocar los cinco.</summary>
    HighFive,

    /// <summary>Una patada (a la pelota).</summary>
    Kick,

    /// <summary>Los brazos por delante, abiertos, para atrapar algo.</summary>
    Catch,

    /// <summary>Limpiarse la boca con el dorso de la mano.</summary>
    Wipe,

    /// <summary>Palmaditas en la barriga, satisfecho.</summary>
    PatBelly,

    /// <summary>La mano en la frente como visera, buscando algo a lo lejos.</summary>
    Visor,

    /// <summary>Llevar algo abrazado contra el pecho (un peluche, la pelota).</summary>
    Carry,

    /// <summary>Empujar algo con las dos manos.</summary>
    Push,

    /// <summary>Estornudar: coge aire echándose atrás y, ¡chís!, se dobla hacia delante.</summary>
    Sneeze,

    /// <summary>Estornudar tapándose la boca con la mano (lo que hacen las que ya saben).</summary>
    CoverSneeze,

    /// <summary>Lanzar lo que lleva en la mano: echa el brazo atrás y lo suelta por arriba (en <see cref="Gestures.ThrowReleaseAt"/>).</summary>
    Throw,

    /// <summary>Teclear deprisa con las dos manos delante.</summary>
    Type,

    /// <summary>
    /// Agachado y agarrado con las dos manos al borde en el que está (la barra de título de una ventana que arrastra):
    /// dos tirones echándose atrás (en <see cref="Gestures.HaulTugs"/>) y luego, mientras la ventana se desliza, inclinado
    /// hacia donde va, bien agarrado y temblando del esfuerzo.
    /// </summary>
    Haul,
}

/// <summary>Cómo es un gesto.</summary>
/// <param name="Name">Nombre para las herramientas de desarrollo.</param>
/// <param name="UsesHands">Necesita las manos (no se combina con posturas en las que no están libres).</param>
/// <param name="Props">Objetos con los que tiene sentido.</param>
public sealed record GestureInfo(Gesture Gesture, string Name, bool UsesHands, IReadOnlyList<Prop> Props);

public static class Gestures
{
    private static readonly Dictionary<Gesture, GestureInfo> Infos = new GestureInfo[]
    {
        new(Gesture.None, "Nada", false, []),
        new(Gesture.Wave, "Saludar", true, []),
        new(Gesture.WaveBoth, "Saludar con las dos manos", true, []),
        new(Gesture.Clap, "Aplaudir", true, []),
        new(Gesture.Scratch, "Rascarse la cabeza", true, []),
        new(Gesture.Yawn, "Bostezar", true, []),
        new(Gesture.Point, "Señalar", true, []),
        new(Gesture.CoverEyes, "Taparse los ojos", true, []),
        new(Gesture.PeekABoo, "Cucú-tras", true, []),
        new(Gesture.RubEyes, "Frotarse los ojos", true, []),
        new(Gesture.ArmsUp, "Brazos arriba", true, [Prop.Flower]),
        new(Gesture.HoldBelly, "Agarrarse la barriga", true, []),
        new(Gesture.Think, "Pensar", true, []),
        new(Gesture.Shrug, "Encogerse de hombros", true, []),
        new(Gesture.Hug, "Abrazarse", true, [Prop.Ball, Prop.Pillow]),
        new(Gesture.Nod, "Asentir", false, []),
        new(Gesture.ShakeHead, "Negar", false, []),
        new(Gesture.Sniff, "Olfatear", false, []),
        new(Gesture.Wiggle, "Menearse", false, []),
        new(Gesture.Bow, "Reverencia", true, []),
        new(Gesture.Drum, "Tamborilear", true, []),
        new(Gesture.Conduct, "Dirigir la música", true, [Prop.Brush, Prop.Flower]),
        new(Gesture.Hold, "Sujetar", true, [Prop.Cane, Prop.Flower, Prop.Brush, Prop.Teacup, Prop.Leaf, Prop.Balloon, Prop.Microphone, Prop.Wand]),
        new(Gesture.HoldUp, "Sujetar en alto", true, [Prop.Balloon, Prop.Leaf, Prop.Flower, Prop.Ball]),
        new(Gesture.HoldBoth, "Sujetar con las dos manos", true, [Prop.Book, Prop.Ball, Prop.Pillow, Prop.Flower]),
        new(Gesture.Sip, "Beber a sorbitos", true, [Prop.Teacup]),
        new(Gesture.Blow, "Soplar", true, [Prop.Wand]),
        new(Gesture.Paint, "Pintar", true, [Prop.Brush]),
        new(Gesture.TapFoot, "Dar golpecitos con el pie", false, []),
        new(Gesture.Beckon, "Llamar con la mano", true, []),
        new(Gesture.LookAround, "Mirar alrededor", false, []),
        new(Gesture.Flail, "Agitar los brazos", true, []),
        new(Gesture.Dribble, "Botar la pelota", true, [Prop.Ball]),
        new(Gesture.Smell, "Oler", true, [Prop.Flower, Prop.Teacup]),
        new(Gesture.Sing, "Cantar", true, [Prop.Microphone]),
        new(Gesture.Back, "Dolor de espalda", true, []),
        new(Gesture.Tickled, "Cosquillas", true, []),
        new(Gesture.Offer, "Ofrecer la mano", true, []),
        new(Gesture.ShakeHands, "Estrechar la mano", true, []),
        new(Gesture.HighFive, "Chocar los cinco", true, []),
        new(Gesture.Kick, "Dar una patada", false, []),
        new(Gesture.Catch, "Atrapar", true, []),
        new(Gesture.Wipe, "Limpiarse la boca", true, []),
        new(Gesture.PatBelly, "Palmaditas en la barriga", true, []),
        new(Gesture.Visor, "Mirar a lo lejos", true, []),
        new(Gesture.Carry, "Llevar en brazos", true, [Prop.Ball, Prop.Pillow, Prop.Plush]),
        new(Gesture.Push, "Empujar", true, []),
        new(Gesture.Sneeze, "Estornudar", false, []),
        new(Gesture.CoverSneeze, "Estornudar tapándose", true, []),
        new(Gesture.Throw, "Lanzar", true, [Prop.Ball]),
        new(Gesture.Type, "Teclear", true, []),
        new(Gesture.Haul, "Arrastrar agarrado", true, []),
    }.ToDictionary(i => i.Gesture);

    /// <summary>Momento del «¡chís!» en los gestos de estornudar (antes coge aire).</summary>
    public const double SneezeAt = 0.55;

    /// <summary>Lo que dura un estornudo, de coger aire a recomponerse.</summary>
    public const double SneezeSeconds = 1.3;

    /// <summary>Momento en que suelta lo que lanza (antes echa el brazo atrás).</summary>
    public const double ThrowReleaseAt = 0.3;

    /// <summary>Lo que dura el gesto de lanzar, con el brazo acompañando hasta abajo.</summary>
    public const double ThrowSeconds = 0.62;

    /// <summary>Momentos de los dos tirones al empezar a arrastrar (antes de que la ventana se deslice del todo).</summary>
    public static readonly double[] HaulTugs = [0.22, 0.52];

    /// <summary>Lo que duran los tirones: después la ventana ya se desliza.</summary>
    public const double HaulTugSeconds = 0.7;

    /// <summary>Gestos en los que la mano agarra el objeto que lleva (y se cierra sobre él).</summary>
    public static bool Holds(Gesture gesture) => gesture is Gesture.Hold or Gesture.HoldUp or Gesture.HoldBoth or Gesture.Sip
        or Gesture.Blow or Gesture.Paint or Gesture.Smell or Gesture.Sing or Gesture.Conduct or Gesture.Hug or Gesture.Carry;

    public static IReadOnlyCollection<GestureInfo> All => Infos.Values;

    public static GestureInfo Info(Gesture gesture) => Infos[gesture];

    /// <summary>Qué partes mueve (la mano «principal» es la del lado hacia el que mira).</summary>
    public static RigMask Mask(Gesture gesture, double side)
    {
        var lead = side < 0 ? RigMask.LeftHand : RigMask.RightHand;
        return gesture switch
        {
            Gesture.None => RigMask.None,
            Gesture.Wave or Gesture.Scratch or Gesture.Think or Gesture.Conduct or Gesture.Paint => lead | RigMask.Head,
            Gesture.Point or Gesture.Hold or Gesture.HoldUp or Gesture.Sip or Gesture.Blow or Gesture.Beckon or Gesture.Dribble
                or Gesture.Offer or Gesture.ShakeHands or Gesture.HighFive or Gesture.Wipe => lead,
            Gesture.Visor => lead | RigMask.Face | RigMask.Head,
            Gesture.Tickled => RigMask.Hands | RigMask.Body | RigMask.Head,
            Gesture.Kick => RigMask.Feet | RigMask.Body,
            Gesture.Smell or Gesture.Sing => lead | RigMask.Face,
            Gesture.Nod or Gesture.Sniff => RigMask.Face,
            Gesture.ShakeHead or Gesture.LookAround => RigMask.Face | RigMask.Head,
            Gesture.Wiggle => RigMask.Body,
            Gesture.TapFoot => RigMask.Feet,
            Gesture.Bow => RigMask.Hands | RigMask.Face | RigMask.Body,
            Gesture.Shrug => RigMask.Hands | RigMask.Head,
            Gesture.Sneeze => RigMask.Face | RigMask.Body | RigMask.Mouth,
            Gesture.CoverSneeze => lead | RigMask.Face | RigMask.Body | RigMask.Mouth,
            Gesture.Throw => lead | RigMask.Body | RigMask.Head,
            Gesture.Type => RigMask.Hands | RigMask.Face,
            Gesture.Haul => RigMask.Hands | RigMask.Body | RigMask.Head | RigMask.Face,
            _ => RigMask.Hands,
        };
    }

    /// <summary>Esqueleto del gesto <paramref name="t"/> segundos después de empezar (solo cuentan las partes de su máscara).</summary>
    public static Rig Evaluate(Gesture gesture, double t, in PoseContext c)
    {
        var s = c.Side < 0 ? -1.0 : 1.0;
        var f = c.Form;
        var r = default(Rig);
        switch (gesture)
        {
            case Gesture.Wave:
                r.SetHand(s, new Vec2(s * (0.98 + 0.10 * Math.Sin(t * 11)), -1.55), f, front: false);
                r.HeadTilt = -s * 0.06;
                break;

            case Gesture.WaveBoth:
                r.SetHand(-1, new Vec2(-(0.98 + 0.10 * Math.Sin(t * 11)), -1.55), f, front: false);
                r.SetHand(1, new Vec2(0.98 + 0.10 * Math.Sin(t * 11 + 1.2), -1.55), f, front: false);
                break;

            case Gesture.Clap:
            {
                var x = 0.20 + 0.20 * (0.5 + 0.5 * Math.Sin(t * 13));
                r.SetHand(-1, new Vec2(-x + c.FaceShift * 0.5, -0.66), f, front: true);
                r.SetHand(1, new Vec2(x + c.FaceShift * 0.5, -0.66), f, front: true);
                break;
            }

            case Gesture.Scratch:
                r.SetHand(s, new Vec2(s * 0.66, -1.58 + 0.04 * Math.Sin(t * 19)), f, front: false);
                r.HeadTilt = -s * 0.14;
                break;

            case Gesture.Yawn:
            {
                var b = Poses.Bump(t, c.Duration > 0 ? c.Duration : 2.2);
                r.SetHand(-1, new Vec2(-0.88, -0.75 - 0.45 * b), f, front: false, grip: 0.7);
                r.SetHand(1, new Vec2(0.88, -0.75 - 0.45 * b), f, front: false, grip: 0.7);
                break;
            }

            case Gesture.Point:
                r.SetHand(s, new Vec2(s * 1.38, -0.98), f, front: false);
                break;

            case Gesture.CoverEyes:
                r.SetHand(-1, Poses.FacePoint(c, -0.30, -1.00), f, front: true);
                r.SetHand(1, Poses.FacePoint(c, 0.30, -1.00), f, front: true);
                break;

            case Gesture.PeekABoo:
            {
                // Cada 2,2 s se destapa un momento: «¡cucú!».
                var open = Poses.Bump(t % 2.2 - 1.4, 0.7);
                r.SetHand(-1, Poses.FacePoint(c, -0.30 - 0.36 * open, -1.00 + 0.10 * open), f, front: true);
                r.SetHand(1, Poses.FacePoint(c, 0.30 + 0.36 * open, -1.00 + 0.10 * open), f, front: true);
                break;
            }

            case Gesture.RubEyes:
                r.SetHand(-1, Poses.FacePoint(c, -0.30 + 0.04 * Math.Cos(t * 10), -0.98 + 0.04 * Math.Sin(t * 10)), f, front: true, grip: 1);
                r.SetHand(1, Poses.FacePoint(c, 0.30 + 0.04 * Math.Cos(t * 10 + 2), -0.98 + 0.04 * Math.Sin(t * 10 + 2)), f, front: true, grip: 1);
                break;

            case Gesture.ArmsUp:
                r.SetHand(-1, new Vec2(-0.86, -1.75 + 0.05 * Math.Sin(t * 8)), f, front: false);
                r.SetHand(1, new Vec2(0.86, -1.75 + 0.05 * Math.Sin(t * 8 + 1)), f, front: false);
                break;

            case Gesture.HoldBelly:
                r.SetHand(-1, new Vec2(-0.30, -0.42), f, front: true);
                r.SetHand(1, new Vec2(0.30, -0.42), f, front: true);
                break;

            case Gesture.Think:
                r.SetHand(s, Poses.FacePoint(c, s * 0.10, -0.68), f, front: true);
                r.HeadTilt = s * 0.12;
                break;

            case Gesture.Shrug:
            {
                var up = 0.10 * Math.Max(0, Math.Sin(t * 4));
                r.SetHand(-1, new Vec2(-1.18, -0.80 - up), f, front: false);
                r.SetHand(1, new Vec2(1.18, -0.80 - up), f, front: false);
                r.HeadTilt = 0.10 * Math.Sin(t * 2);
                break;
            }

            case Gesture.Hug:
                r.SetHand(-1, new Vec2(0.05, -0.64), f, front: true);
                r.SetHand(1, new Vec2(-0.05, -0.70), f, front: true);
                break;

            case Gesture.Nod:
                r.FaceY = 0.06 * Math.Max(0, Math.Sin(t * 8));
                break;

            case Gesture.ShakeHead:
                r.FaceX = 0.07 * Math.Sin(t * 11);
                r.HeadTilt = 0.04 * Math.Sin(t * 11);
                break;

            case Gesture.Sniff:
                r.FaceY = -0.02 + 0.015 * Math.Sin(t * 30);
                r.FaceX = s * 0.04;
                break;

            case Gesture.Wiggle:
                r.Spin = 0.12 * Math.Sin(t * 13);
                r.OffsetX = 0.03 * Math.Sin(t * 13);
                break;

            case Gesture.Bow:
            {
                var b = Poses.Bump(t, c.Duration > 0 ? c.Duration : 2);
                r.FaceY = 0.12 * b;
                r.Stretch = -0.10 * b;
                r.SetHand(-1, new Vec2(-0.36, -0.48), f, front: true);
                r.SetHand(1, new Vec2(0.36, -0.48), f, front: true);
                break;
            }

            case Gesture.Drum:
                r.SetHand(-1, new Vec2(-0.32, -0.46 - 0.08 * Math.Max(0, Math.Sin(t * 14))), f, front: true, grip: 1);
                r.SetHand(1, new Vec2(0.32, -0.46 - 0.08 * Math.Max(0, -Math.Sin(t * 14))), f, front: true, grip: 1);
                break;

            case Gesture.Conduct:
                r.SetHand(s, new Vec2(s * (1.05 + 0.14 * Math.Cos(t * 5)), -1.28 + 0.14 * Math.Sin(t * 10)), f, front: false);
                r.HeadTilt = 0.10 * Math.Sin(t * 2.5);
                break;

            case Gesture.Hold:
                r.SetHand(s, new Vec2(s * 1.02, -0.70), f, front: false);
                break;

            case Gesture.HoldUp:
                r.SetHand(s, new Vec2(s * 0.92, -1.45), f, front: false);
                break;

            case Gesture.HoldBoth:
                r.SetHand(-1, new Vec2(-0.36 + c.FaceShift * 0.4, -0.64), f, front: true);
                r.SetHand(1, new Vec2(0.36 + c.FaceShift * 0.4, -0.64), f, front: true);
                break;

            case Gesture.Sip:
            {
                // Un sorbito cada 3,2 s.
                var lift = Poses.Bump(t % 3.2, 1.3);
                var low = new Vec2(s * 0.48, -0.56);
                var mouth = Poses.FacePoint(c, s * 0.16, -0.80);
                r.SetHand(s, Vec2.Lerp(low, mouth, lift), f, front: true);
                break;
            }

            case Gesture.Blow:
                r.SetHand(s, Poses.FacePoint(c, s * 0.30, -0.82), f, front: true);
                break;

            case Gesture.Paint:
                r.SetHand(s, new Vec2(s * (1.25 + 0.08 * Math.Sin(t * 6)), -0.92 + 0.10 * Math.Cos(t * 4.3)), f, front: false);
                r.HeadTilt = -s * 0.10;
                break;

            case Gesture.TapFoot:
                if (s < 0)
                {
                    r.FootLY = -0.08 * Math.Max(0, Math.Sin(t * 11));
                }
                else
                {
                    r.FootRY = -0.08 * Math.Max(0, Math.Sin(t * 11));
                }

                break;

            case Gesture.Beckon:
                r.SetHand(s, new Vec2(s * 0.95, -0.88 - 0.08 * Math.Max(0, Math.Sin(t * 7))), f, front: false);
                break;

            case Gesture.LookAround:
                r.FaceX = 0.06 * Math.Sin(t * 1.4);
                r.HeadTilt = 0.08 * Math.Sin(t * 1.4);
                break;

            case Gesture.Flail:
                r.SetHand(-1, new Vec2(-0.95, -1.40 + 0.15 * Math.Sin(t * 24)), f, front: false);
                r.SetHand(1, new Vec2(0.95, -1.40 + 0.15 * Math.Sin(t * 24 + 1.5)), f, front: false);
                break;

            case Gesture.Dribble:
                r.SetHand(s, new Vec2(s * 0.98, -0.42 - 0.12 * Math.Max(0, Math.Sin(t * Math.Tau / 0.7))), f, front: false);
                break;

            case Gesture.Smell:
                r.SetHand(s, Poses.FacePoint(c, s * 0.22, -0.92), f, front: true);
                r.FaceY = -0.02 + 0.012 * Math.Sin(t * 26);
                break;

            case Gesture.Sing:
                r.SetHand(s, Poses.FacePoint(c, s * 0.24, -0.70), f, front: true);
                r.FaceY = 0.02 * Math.Sin(t * 6);
                break;

            case Gesture.Tickled:
            {
                // Se retuerce de la risa: se tapa la barriga, se encoge y se mueve de un lado a otro.
                var wiggle = Math.Sin(t * 17);
                var curl = 0.6 + 0.4 * Math.Abs(Math.Sin(t * 5));
                r.SetHand(-1, new Vec2(-0.30 + 0.08 * wiggle + c.FaceShift * 0.3, -0.50 - 0.06 * curl), f, front: true, grip: 0.6);
                r.SetHand(1, new Vec2(0.30 + 0.08 * wiggle + c.FaceShift * 0.3, -0.56 - 0.06 * curl), f, front: true, grip: 0.6);
                r.Spin = 0.10 * wiggle;
                r.OffsetX = 0.03 * wiggle;
                r.Stretch = -0.08 * curl;
                r.HeadTilt = 0.12 * Math.Sin(t * 9);
                r.Sprout = 0.30 * Math.Sin(t * 9 + 1);
                break;
            }

            case Gesture.Offer:
                r.SetHand(s, new Vec2(s * 1.22, -0.80 + 0.02 * Math.Sin(t * 3)), f, front: false);
                r.HeadTilt = s * 0.05;
                break;

            case Gesture.ShakeHands:
                r.SetHand(s, new Vec2(s * 1.20, -0.80 + 0.08 * Math.Sin(t * 14)), f, front: false, grip: 0.8);
                break;

            case Gesture.HighFive:
            {
                // Sube la mano con energía, choca y se queda un momento arriba.
                var up = Easing.Apply(Ease.Back, t / 0.25);
                r.SetHand(s, Vec2.Lerp(RestOf(s, f), new Vec2(s * 1.06, -1.52), up), f, front: false);
                break;
            }

            case Gesture.Kick:
            {
                var kick = Easing.Pulse(t - 0.12, 0.12, 0.35);
                var windup = Easing.Pulse(t, 0.12, 0.08) * (t < 0.24 ? 1 : 0);
                if (s < 0)
                {
                    (r.FootLX, r.FootLY, r.FootLTurn) = (-0.30 * kick + 0.10 * windup, -0.16 * kick, 0.4 * kick);
                }
                else
                {
                    (r.FootRX, r.FootRY, r.FootRTurn) = (0.30 * kick - 0.10 * windup, -0.16 * kick, -0.4 * kick);
                }

                r.Spin = -s * (0.14 * kick - 0.05 * windup);
                r.OffsetY = -0.04 * kick;
                break;
            }

            case Gesture.Catch:
                r.SetHand(-1, new Vec2(s * 0.90 - 0.24, -0.86 + 0.03 * Math.Sin(t * 6)), f, front: s > 0);
                r.SetHand(1, new Vec2(s * 0.90 + 0.24, -0.86 + 0.03 * Math.Sin(t * 6 + 1)), f, front: s < 0);
                break;

            case Gesture.Wipe:
            {
                // El dorso de la mano pasa por la boca de un lado al otro.
                var sweep = Easing.Apply(Ease.InOut, 0.5 - 0.5 * Math.Cos(Math.Min(t, 1.2) * Math.Tau / 0.6));
                var from = Poses.FacePoint(c, s * 0.50, -0.80);
                var to = Poses.FacePoint(c, -s * 0.12, -0.78);
                r.SetHand(s, Vec2.Lerp(from, to, sweep), f, front: true, grip: 0.7);
                break;
            }

            case Gesture.PatBelly:
                r.SetHand(-1, new Vec2(-0.30, -0.44 - 0.06 * Math.Max(0, Math.Sin(t * 7))), f, front: true);
                r.SetHand(1, new Vec2(0.30, -0.44 - 0.06 * Math.Max(0, Math.Sin(t * 7 + 1.4))), f, front: true);
                break;

            case Gesture.Visor:
                r.SetHand(s, Poses.FacePoint(c, s * 0.10, -1.32), f, front: true);
                r.FaceX = 0.05 * Math.Sin(t * 1.3);
                r.HeadTilt = -s * 0.06 + 0.05 * Math.Sin(t * 1.3);
                break;

            case Gesture.Carry:
                r.SetHand(-1, new Vec2(-0.28 + c.FaceShift * 0.3, -0.68), f, front: true, grip: 0.8);
                r.SetHand(1, new Vec2(0.28 + c.FaceShift * 0.3, -0.72), f, front: true, grip: 0.8);
                break;

            case Gesture.Push:
            {
                var push = 0.5 + 0.5 * Math.Sin(t * 6);
                r.SetHand(-1, new Vec2(s * (0.95 + 0.15 * push) - 0.18, -0.62), f, front: s > 0);
                r.SetHand(1, new Vec2(s * (0.95 + 0.15 * push) + 0.18, -0.62), f, front: s < 0);
                break;
            }

            case Gesture.Back:
                r.SetHand(-1, new Vec2(-1.02, -0.40), f, front: false);
                r.SetHand(1, new Vec2(1.02, -0.40), f, front: false);
                break;

            case Gesture.Sneeze or Gesture.CoverSneeze:
            {
                // «A… a… ¡chís!»: coge aire echándose atrás con la boca abierta, se dobla de golpe hacia
                // delante y se recompone despacio.
                var inhale = Easing.Segment(t, 0, SneezeAt - 0.05);
                var burst = Easing.Segment(t, SneezeAt - 0.05, 0.1, Ease.Out);
                var settle = Easing.Segment(t, SneezeAt + 0.15, 0.55);
                var back = inhale * (1 - burst);
                var forward = burst * (1 - settle);
                r.Spin = s * (-0.06 * back + 0.08 * forward);
                r.Stretch = 0.05 * back - 0.04 * forward;
                r.FaceX = s * (-0.03 * back + 0.04 * forward);
                r.FaceY = -0.04 * back + 0.04 * forward;
                r.MouthOpen = 0.45 * back;
                if (gesture == Gesture.CoverSneeze)
                {
                    // La mano sube a la boca mientras coge aire y se queda ahí hasta que pasa.
                    var reach = Easing.Segment(t, 0.05, 0.35);
                    var mouth = Poses.FacePoint(c, s * 0.08, -0.72 + 0.03 * forward);
                    r.SetHand(s, Vec2.Lerp(Rig.RestHand(s, f), mouth, reach), f, front: true, grip: 0.4 * reach);
                }

                break;
            }

            case Gesture.Throw:
            {
                // Brazo atrás y arriba (el cuerpo se echa atrás), latigazo hacia delante al soltar y acompaña hasta abajo.
                var windup = Easing.Segment(t, 0, ThrowReleaseAt, Ease.Out);
                var swing = Easing.Segment(t, ThrowReleaseAt - 0.04, 0.12, Ease.Out);
                var settle = Easing.Segment(t, ThrowReleaseAt + 0.12, ThrowSeconds - ThrowReleaseAt - 0.12);
                var back = new Vec2(-s * 0.55, -1.62);
                var front = new Vec2(s * 1.30, -1.05);
                var hand = Vec2.Lerp(Vec2.Lerp(RestOf(s, f), back, windup), front, swing);
                r.SetHand(s, Vec2.Lerp(hand, RestOf(s, f), settle), f, front: false, grip: 1 - swing);
                var lean = -0.10 * windup * (1 - swing) + 0.12 * swing * (1 - settle);
                r.Spin = s * lean;
                r.HeadTilt = s * 0.5 * lean;
                break;
            }

            case Gesture.Type:
            {
                // Las manos delante, a la altura de un teclado imaginario, picando teclas a destiempo.
                var left = Math.Max(0, Math.Sin(t * 23));
                var right = Math.Max(0, Math.Sin(t * 23 + 2.1));
                r.SetHand(-1, new Vec2(-0.34 + 0.05 * Math.Sin(t * 5), -0.52 - 0.07 * left), f, front: true, grip: 0.3);
                r.SetHand(1, new Vec2(0.34 + 0.05 * Math.Sin(t * 5 + 1), -0.52 - 0.07 * right), f, front: true, grip: 0.3);
                r.FaceY = 0.04;
                break;
            }

            case Gesture.Haul:
            {
                // Se agacha y se agarra al borde delante de los pies; en cada tirón se echa atrás de golpe; luego, mientras
                // se desliza, se inclina hacia donde va con las manos bien cerradas y un temblor de esfuerzo.
                var tug = 0.0;
                foreach (var at in HaulTugs)
                {
                    tug = Math.Max(tug, Easing.Pulse(t - at + 0.06, 0.06, 0.2));
                }

                var ride = Easing.Segment(t, HaulTugSeconds - 0.1, 0.3);
                var shake = 0.012 * Math.Sin(t * 47) * ride;
                var grip = new Vec2(s * (1.02 - 0.10 * tug), -0.20 - 0.04 * tug);
                r.SetHand(-1, grip + new Vec2(-0.16 + shake, 0), f, front: s > 0, grip: 1);
                r.SetHand(1, grip + new Vec2(0.16 - shake, 0.02), f, front: s < 0, grip: 1);
                r.Stretch = -0.16 + 0.05 * tug;
                r.OffsetY = 0.06;
                r.Spin = s * (0.16 * ride - 0.20 * tug) + shake;
                r.HeadTilt = -s * (0.06 + 0.10 * tug);
                r.FaceX = s * 0.04;
                r.FaceY = 0.02;
                break;
            }
        }

        return r;
    }

    private static Vec2 RestOf(double side, double form) => Rig.RestHand(side, form);
}
