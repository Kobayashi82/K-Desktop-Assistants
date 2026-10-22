using Kobii.Core.Geometry;

namespace Kobii.Core.Animation;

/// <summary>Posturas del cuerpo entero. Son la capa de abajo: encima van los gestos, la cara y los objetos.</summary>
public enum Pose
{
    Stand,
    Walk,
    Sit,

    /// <summary>Sentado en el borde de una ventana, con los pies colgando.</summary>
    SitEdge,

    /// <summary>Tumbado sobre la barriga, de cara, con la barbilla en las manos (descansar y la siesta).</summary>
    Lie,

    /// <summary>Acurrucado, hecho una bolita.</summary>
    Curl,

    /// <summary>Agachado.</summary>
    Crouch,

    /// <summary>De puntillas.</summary>
    Tiptoe,

    /// <summary>Estirarse con los brazos arriba (una vez, a lo largo de la duración del clip).</summary>
    Stretch,

    /// <summary>Rodar por el suelo (el juego lo desplaza a la vez).</summary>
    Roll,

    /// <summary>Voltereta en el aire (el juego le hace saltar).</summary>
    Flip,

    Dance,

    /// <summary>Balancearse a un lado y a otro (canturrear, esperar contento).</summary>
    Sway,

    /// <summary>Botar en el sitio.</summary>
    Hop,

    /// <summary>Asomarse por el borde.</summary>
    Peek,

    /// <summary>Colgando cuando se le coge con el ratón.</summary>
    Dangle,

    /// <summary>Cayendo, agitando los brazos.</summary>
    Tumble,

    /// <summary>De pie y encorvado (los mayores).</summary>
    Hunched,

    /// <summary>Sentado y dando cabezadas.</summary>
    Doze,

    /// <summary>Corriendo (perseguir la pelota, jugar a pillar): zancadas largas y brazos que bracean.</summary>
    Run,

    /// <summary>En el aire en un salto preparado (trepar): brazos atrás al subir y adelante al bajar.</summary>
    Jump,
}

/// <summary>Cómo es una postura (para combinarla con gestos y elegir cuándo usarla).</summary>
/// <param name="Name">Nombre para las herramientas de desarrollo.</param>
/// <param name="Upright">Está erguido: admite encorvarse y los gestos con las manos.</param>
/// <param name="Lively">Se mueve todo el rato (hay que dibujar a la frecuencia alta).</param>
public sealed record PoseInfo(Pose Pose, string Name, bool Upright, bool Lively);

/// <summary>Contexto para evaluar una postura o un gesto.</summary>
/// <param name="Side">Hacia dónde mira (−1 izquierda, 1 derecha): la mano que se usa primero.</param>
/// <param name="Form">Forma (1 cría … 5 mayor).</param>
/// <param name="Duration">Duración del clip (para las posturas de una sola vez); 0 si no tiene.</param>
/// <param name="WalkPhase">Fase del paso al caminar.</param>
/// <param name="Walk">Intensidad del paso (0–1).</param>
/// <param name="Facing">Hacia dónde gira la cara (−1..1): las manos que van a la cara la siguen.</param>
public readonly record struct PoseContext(double Side, double Form, double Duration = 0, double WalkPhase = 0, double Walk = 0, double Facing = 0)
{
    /// <summary>Desplazamiento horizontal de la cara según hacia dónde mira (el mismo que en el shader).</summary>
    public double FaceShift => Facing * 0.16;
}

public static class Poses
{
    private static readonly Dictionary<Pose, PoseInfo> Infos = new PoseInfo[]
    {
        new(Pose.Stand, "De pie", Upright: true, Lively: false),
        new(Pose.Walk, "Caminando", Upright: true, Lively: true),
        new(Pose.Sit, "Sentado", Upright: true, Lively: false),
        new(Pose.SitEdge, "Sentado en el borde", Upright: true, Lively: false),
        new(Pose.Lie, "Tumbado", Upright: false, Lively: false),
        new(Pose.Curl, "Acurrucado", Upright: false, Lively: false),
        new(Pose.Crouch, "Agachado", Upright: true, Lively: false),
        new(Pose.Tiptoe, "De puntillas", Upright: true, Lively: false),
        new(Pose.Stretch, "Estirándose", Upright: false, Lively: true),
        new(Pose.Roll, "Rodando", Upright: false, Lively: true),
        new(Pose.Flip, "Voltereta", Upright: false, Lively: true),
        new(Pose.Dance, "Bailando", Upright: true, Lively: true),
        new(Pose.Sway, "Balanceándose", Upright: true, Lively: true),
        new(Pose.Hop, "Botando", Upright: true, Lively: true),
        new(Pose.Peek, "Asomándose", Upright: false, Lively: false),
        new(Pose.Dangle, "Colgando", Upright: false, Lively: true),
        new(Pose.Tumble, "Cayendo", Upright: false, Lively: true),
        new(Pose.Hunched, "Encorvado", Upright: true, Lively: false),
        new(Pose.Doze, "Dando cabezadas", Upright: true, Lively: false),
        new(Pose.Run, "Corriendo", Upright: true, Lively: true),
        new(Pose.Jump, "Saltando", Upright: false, Lively: true),
    }.ToDictionary(i => i.Pose);

    public static IReadOnlyCollection<PoseInfo> All => Infos.Values;

    public static PoseInfo Info(Pose pose) => Infos[pose];

    /// <summary>Esqueleto de una postura <paramref name="t"/> segundos después de empezar.</summary>
    public static Rig Evaluate(Pose pose, double t, in PoseContext c)
    {
        var s = c.Side < 0 ? -1.0 : 1.0;
        var r = default(Rig);
        switch (pose)
        {
            case Pose.Walk:
                Stride(ref r, c.WalkPhase, c.Walk, s, run: false);
                break;

            case Pose.Run:
                Stride(ref r, c.WalkPhase, Math.Max(0.6, c.Walk), s, run: true);
                break;

            case Pose.Jump:
            {
                // Al subir, brazos atrás y pies recogidos; al bajar (Walk hacia 1), todo adelante para aterrizar.
                var reach = Math.Clamp(0.5 + 0.5 * c.Walk, 0, 1);
                (r.HandLX, r.HandLY) = (-0.10 - 0.10 * reach, -0.30 - 0.55 * (1 - reach));
                (r.HandRX, r.HandRY) = (0.10 + 0.10 * reach, -0.30 - 0.55 * (1 - reach));
                (r.FootLY, r.FootRY) = (-0.10 + 0.12 * reach, -0.10 + 0.12 * reach);
                (r.FootLX, r.FootRX) = (0.05, -0.05);
                r.Stretch = 0.10 - 0.16 * reach;
                r.Spin = s * 0.12 * (1 - reach);
                r.ShowArms = 1;
                break;
            }

            case Pose.Sit:
                Sit(ref r);
                break;

            case Pose.SitEdge:
                Sit(ref r);
                r.FootLY = 0.27 + 0.05 * Math.Sin(t * 3);
                r.FootRY = 0.27 + 0.05 * Math.Sin(t * 3 + 2.1);
                r.FootLX = -0.07;
                r.FootRX = 0.07;
                break;

            case Pose.Doze:
            {
                Sit(ref r);
                // Cabecea despacio y, de vez en cuando, se le cae la cabeza y la levanta de golpe.
                var nod = Math.Pow(Math.Max(0, Math.Sin(t * 0.9)), 3);
                r.FaceY = 0.07 * nod;
                r.HeadTilt = s * 0.10 * nod;
                r.Sprout = s * 0.25 * nod;
                break;
            }

            case Pose.Lie:
            {
                // Tumbado sobre la barriga, de cara: medio derretido contra el suelo, con la barbilla apoyada en las manos,
                // la cabeza un poco ladeada y respirando despacio (el cuerpo sube y baja).
                var breath = Math.Sin(t * 1.6);
                r.Melt = 0.40;
                r.Stretch = 0.015 * breath;
                r.SetHand(-1, new Vec2(-0.19, -0.42 + 0.01 * breath), c.Form, front: true);
                r.SetHand(1, new Vec2(0.19, -0.42 + 0.01 * breath), c.Form, front: true);
                r.HeadTilt = s * 0.10;
                r.Sprout = s * 0.30;
                (r.EarL, r.EarR) = (0.20, 0.20);
                break;
            }

            case Pose.Curl:
                r.Melt = 0.18;
                r.Stretch = -0.10;
                (r.HandLX, r.HandLY, r.FrontL) = (0.55, 0.08, 1);
                (r.HandRX, r.HandRY, r.FrontR) = (-0.55, 0.10, 1);
                (r.FootLX, r.FootRX, r.FootLY, r.FootRY) = (0.10, -0.10, -0.04, -0.04);
                r.Sprout = s * 0.25;
                r.HeadTilt = s * 0.12;
                break;

            case Pose.Crouch:
                r.Stretch = -0.16;
                (r.HandLX, r.HandLY, r.HandRX, r.HandRY) = (0.07, 0.12, -0.07, 0.12);
                (r.FootLX, r.FootRX) = (-0.06, 0.06);
                break;

            case Pose.Tiptoe:
                r.OffsetY = -0.10;
                (r.FootLY, r.FootRY) = (0.10, 0.10);
                r.Stretch = 0.05;
                (r.HandLY, r.HandRY) = (-0.20, -0.20);
                r.Spin = 0.04 * Math.Sin(t * 3);
                r.ShowArms = 1;
                break;

            case Pose.Stretch:
            {
                var b = Bump(t, c.Duration > 0 ? c.Duration : 2.4);
                r.Stretch = 0.15 * b;
                r.OffsetY = -0.08 * b;
                (r.FootLY, r.FootRY) = (0.08 * b, 0.08 * b);
                (r.HandLX, r.HandLY, r.HandRX, r.HandRY) = (0.10 * b, -1.25 * b, -0.10 * b, -1.25 * b);
                r.ShowArms = b > 0.05 ? 1 : 0;
                break;
            }

            case Pose.Roll:
                r.Spin = s * Math.Tau * t / 1.1;
                r.Stretch = -0.06;
                TuckArms(ref r);
                (r.FootLX, r.FootRX, r.FootLY, r.FootRY) = (0.12, -0.12, -0.10, -0.10);
                break;

            case Pose.Flip:
            {
                var u = Math.Clamp(t / 0.6, 0, 1);
                r.Spin = s * Math.Tau * Smooth(u);
                TuckArms(ref r);
                (r.FootLY, r.FootRY) = (-0.08 * Bump(u, 1), -0.08 * Bump(u, 1));
                break;
            }

            case Pose.Dance:
            {
                var beat = Math.Sin(t * Math.Tau);
                var bounce = Math.Sin(t * Math.Tau * 2);
                r.OffsetX = 0.06 * beat;
                r.Spin = 0.10 * beat;
                r.Stretch = 0.05 * Math.Abs(bounce);
                (r.HandLX, r.HandLY) = (-0.10, 0.10 - 0.9 * Math.Max(0, beat));
                (r.HandRX, r.HandRY) = (0.10, 0.10 - 0.9 * Math.Max(0, -beat));
                r.FootLY = -0.10 * Math.Max(0, bounce);
                r.FootRY = -0.10 * Math.Max(0, -bounce);
                r.HeadTilt = 0.15 * beat;
                r.Sprout = -0.2 * beat;
                r.ShowArms = 1;
                break;
            }

            case Pose.Sway:
            {
                var b = Math.Sin(t * 1.7);
                r.Spin = 0.07 * b;
                r.OffsetX = 0.025 * b;
                r.HeadTilt = 0.08 * b;
                r.Sprout = -0.12 * b;
                break;
            }

            case Pose.Hop:
            {
                var h = Math.Abs(Math.Sin(t * 5));
                r.Stretch = 0.10 * (h - 0.4);
                (r.HandLY, r.HandRY) = (-0.30 * h, -0.30 * h);
                r.ShowArms = 1;
                break;
            }

            case Pose.Peek:
                // Se inclina hacia el borde sin mover los pies.
                r.OffsetX = s * 0.14;
                r.Spin = s * 0.30;
                r.Stretch = -0.08;
                (r.FootLX, r.FootRX) = (-s * 0.14, -s * 0.14);
                r.FaceY = 0.04;
                (r.HandLX, r.HandLY, r.HandRX, r.HandRY) = (s * 0.10, 0.12, s * 0.10, 0.12);
                break;

            case Pose.Dangle:
                r.Stretch = 0.10;
                r.FootLY = 0.12 + 0.03 * Math.Sin(t * 5);
                r.FootRY = 0.12 + 0.03 * Math.Sin(t * 5 + 1);
                (r.HandLX, r.HandLY) = (-0.05, -0.35 + 0.05 * Math.Sin(t * 7));
                (r.HandRX, r.HandRY) = (0.05, -0.35 + 0.05 * Math.Sin(t * 7 + 2));
                r.ShowArms = 1;
                break;

            case Pose.Tumble:
                (r.HandLX, r.HandLY) = (-0.10, -0.80 + 0.12 * Math.Sin(t * 22));
                (r.HandRX, r.HandRY) = (0.10, -0.80 + 0.12 * Math.Sin(t * 22 + 1.5));
                (r.FootLX, r.FootRX) = (-0.08, 0.08);
                r.FootLY = 0.05 * Math.Sin(t * 18);
                r.FootRY = 0.05 * Math.Sin(t * 18 + 2);
                r.ShowArms = 1;
                break;

            case Pose.Hunched:
                r.Hunch = 1;
                r.OffsetY = 0.02;
                (r.HandLX, r.HandLY, r.HandRX, r.HandRY) = (0.06, 0.06, -0.06, 0.06);
                break;
        }

        return r;
    }

    /// <summary>
    /// Un paso: cada pie se levanta y avanza en arco (con la punta abajo al despegar y arriba al apoyar) y,
    /// apoyado, se queda atrás mientras el cuerpo avanza. El cuerpo se contonea hacia el pie que apoya, sube
    /// y baja a cada paso, y los brazos van al contrario que los pies.
    /// </summary>
    private static void Stride(ref Rig r, double phase, double w, double dir, bool run)
    {
        var lift = run ? 0.22 : 0.15;
        var length = run ? 0.16 : 0.10;
        for (var i = 0; i < 2; i++)
        {
            var p = phase + i * Math.PI;
            var up = Math.Max(0, Math.Sin(p));
            var y = -lift * up * w;
            var x = -Math.Cos(p) * length * w * dir;
            var turn = dir * 0.32 * w * -Math.Sin(p - Math.PI / 4) * Math.Sqrt(up);
            if (i == 0)
            {
                (r.FootLX, r.FootLY, r.FootLTurn) = (x, y, turn);
            }
            else
            {
                (r.FootRX, r.FootRY, r.FootRTurn) = (x, y, turn);
            }
        }

        var bounce = Math.Abs(Math.Sin(phase));
        r.OffsetY = -(run ? 0.06 : 0.035) * bounce * w;
        r.OffsetX = 0.018 * Math.Sin(phase) * w;
        r.Spin = (run ? 0.03 : 0.055) * Math.Sin(phase) * w + (run ? dir * 0.10 * w : 0);
        r.Stretch = (run ? 0.05 : 0.025) * bounce * w - 0.02 * w;
        r.HeadTilt = -0.035 * Math.Sin(phase) * w;

        // Brazos: cada uno va al contrario que el pie de su lado.
        var arm = run ? 0.16 : 0.07;
        r.HandLX = Math.Cos(phase) * arm * w * dir;
        r.HandRX = -Math.Cos(phase) * arm * w * dir;
        r.HandLY = -(run ? 0.16 : 0.05) * Math.Max(0, Math.Cos(phase)) * w;
        r.HandRY = -(run ? 0.16 : 0.05) * Math.Max(0, -Math.Cos(phase)) * w;
        if (run)
        {
            r.ShowArms = 1;
            r.HandLY -= 0.10 * w;
            r.HandRY -= 0.10 * w;
        }
    }

    /// <summary>Sentado: un poco más bajo y aplastado, con los pies por delante.</summary>
    private static void Sit(ref Rig r)
    {
        r.OffsetY = 0.03;
        r.Stretch = -0.06;
        (r.FootLX, r.FootRX, r.FootLY, r.FootRY) = (-0.10, 0.10, 0.06, 0.06);
        r.FeetFront = 1;
        (r.HandLX, r.HandLY, r.HandRX, r.HandRY) = (0.10, 0.12, -0.10, 0.12);
    }

    private static void TuckArms(ref Rig r)
    {
        (r.HandLX, r.HandLY, r.FrontL) = (0.40, -0.10, 1);
        (r.HandRX, r.HandRY, r.FrontR) = (-0.40, -0.10, 1);
    }

    /// <summary>De 0 a 1 y vuelta a 0 a lo largo de <paramref name="duration"/>, con una meseta en medio.</summary>
    public static double Bump(double t, double duration)
    {
        if (duration <= 0)
        {
            return 0;
        }

        var u = Math.Clamp(t / duration, 0, 1);
        return Smooth(Math.Clamp(u / 0.3, 0, 1)) * Smooth(Math.Clamp((1 - u) / 0.3, 0, 1));
    }

    public static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    /// <summary>Punto de la cara (ojos, boca…) en el espacio de la criatura, para que las manos lleguen a él.</summary>
    public static Vec2 FacePoint(in PoseContext c, double x, double y) => new(x + c.FaceShift, y);
}
