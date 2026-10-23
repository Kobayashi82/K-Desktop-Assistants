using System.Runtime.InteropServices;
using Kobii.Core.Animation;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;

namespace Kobii.Assistant.Rendering;

/// <summary>Un objeto que hay que dibujar junto a Kobii (lo que lleva en la mano o puesto).</summary>
/// <param name="Prop">Objeto en la mano.</param>
/// <param name="Wear">Lo que lleva puesto.</param>
/// <param name="Gesture">Gesto con el que lo usa (botar la pelota, abrazarla…).</param>
/// <param name="Hand">Mano que lo sujeta, en el mundo.</param>
/// <param name="OtherHand">La otra mano.</param>
/// <param name="EyeL">Ojo izquierdo en el mundo (para las gafas).</param>
/// <param name="EyeR">Ojo derecho.</param>
/// <param name="Forehead">Frente (gafas subidas).</param>
/// <param name="Rotation">Giro del cuerpo (radianes).</param>
/// <param name="Side">Lado de la mano que lo sujeta (−1 o 1).</param>
/// <param name="Time">Segundos desde que empezó la animación (para mecer el globo, botar la pelota…).</param>
/// <param name="Unit">Píxeles por unidad de la criatura.</param>
/// <param name="GroundY">Altura del suelo bajo sus pies (en el mundo).</param>
/// <param name="OtherGrip">Lo cerrada que está la otra mano (si suelta un objeto de dos manos, lo lleva solo con una).</param>
internal readonly record struct PropPose(Prop Prop, Wear Wear, Gesture Gesture, Vec2 Hand, Vec2 OtherHand, Vec2 EyeL, Vec2 EyeR, Vec2 Forehead, double Rotation, double Side, double Time, double Unit, double GroundY, double OtherGrip = 1);

/// <summary>Lo que tiene en las manos además del objeto de la animación: algo de comer o un juguete.</summary>
/// <param name="Food">Lo que come o bebe (null si es un juguete o no hay nada).</param>
/// <param name="Toy">El juguete (si no es comida).</param>
/// <param name="Center">Su centro, en el mundo.</param>
/// <param name="Rotation">Giro (el del cuerpo y, al beber, la inclinación del vaso).</param>
/// <summary>La cama en la que está (para dibujar su borde por delante de él).</summary>
internal readonly record struct BedPose(Vec2 Feet, double Radius, double Squash);

internal readonly record struct HeldThing(FoodKind? Food, int Bites, double BiteSide, double Remaining, ToyKind? Toy, Vec2 Center, double Rotation, double Spin, double Scale)
{
    public bool Any => Food is not null || Toy is not null;
}

/// <summary>Lo que se ve de Kobii en un fotograma, aparte del esqueleto (cara, rebote, sombra…).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct KobiiLook
{
    /// <summary>Píxeles por unidad de la criatura (la mitad de su altura).</summary>
    public double Unit;

    /// <summary>Tiempo de las animaciones (segundos).</summary>
    public double Time;

    public double LookX, LookY;

    /// <summary>Ojos cerrados (0–1).</summary>
    public double Blink;

    /// <summary>Aplastado (menos de 1) o estirado, alrededor de los pies; incluye la respiración.</summary>
    public double Squash;

    /// <summary>Inclinación (radianes).</summary>
    public double Lean;

    /// <summary>Boca al hablar (0–1).</summary>
    public double Mouth;

    /// <summary>Cara (<see cref="Kobii.Core.Creature.Expression"/>).</summary>
    public double Expression;

    public double Blush;

    /// <summary>Hacia dónde gira la cara (−1..1).</summary>
    public double Facing;

    public double Opacity;

    /// <summary>Brillo de la hoja del brote (escuchando, pensando).</summary>
    public double Antenna;

    /// <summary>Distancia de los pies al suelo en unidades (99: no hay suelo debajo).</summary>
    public double ShadowDrop;

    public double ShadowAlpha;

    /// <summary>La mano de cada lado agarra algo y va por encima de ello.</summary>
    public double HandOverL, HandOverR;

    /// <summary>Pijama y gorro de dormir puestos (0–1; el gorro baja desde arriba al ponérselo).</summary>
    public double Pajamas, Nightcap;

    /// <summary>Pelo brillante tras cepillarle (0–1).</summary>
    public double Shine;
}

/// <summary>Todo lo necesario para dibujar a Kobii en un fotograma.</summary>
internal sealed class KobiiFrame
{
    /// <summary>Pies en el mundo (interpolados), en píxeles físicos.</summary>
    public Vec2 Feet { get; set; }

    public double HeightPx { get; set; }

    public KobiiLook Look;

    /// <summary>Esqueleto interpolado de este fotograma.</summary>
    public Rig Rig { get; set; }

    public bool Dark { get; set; }

    public Palette Palette { get; set; } = Species.Palettes[0];

    /// <summary>Lo que lleva en la mano o puesto (nada si no lleva nada).</summary>
    public PropPose Props { get; set; }

    /// <summary>Lo que tiene en las manos (comida o un juguete).</summary>
    public HeldThing Held { get; private set; }

    /// <summary>La cama en la que está, si está en una.</summary>
    public BedPose? BedFront { get; private set; }

    /// <summary>Hay algo más que la criatura que dibujar (lo que lleva en la mano o puesto).</summary>
    public bool HasExtras => Props.Prop != Prop.None || Props.Wear != Wear.None || Held.Any || BedFront is not null;

    /// <summary>Línea detrás de la que está escondido (en el mundo), si lo está.</summary>
    public ClipLine? Clip { get; private set; }

    /// <summary>Rellena los datos a partir del estado del juego.</summary>
    /// <param name="game">Kobii.</param>
    /// <param name="feet">Posición interpolada de los pies.</param>
    /// <param name="time">Tiempo de animación en segundos.</param>
    /// <param name="blink">Parpadeo (0–1).</param>
    /// <param name="groundBelow">Altura del suelo bajo él (para la sombra), o null.</param>
    /// <param name="alpha">Punto entre el paso anterior (0) y el actual (1) en el que se dibuja.</param>
    /// <param name="opacity">Opacidad (al entrar y al salir).</param>
    public void Update(KobiiGame game, Vec2 feet, double time, double blink, double? groundBelow, double alpha, double opacity)
    {
        var visual = game.Visual;
        var body = game.Body;
        Feet = feet;
        HeightPx = body.Height;

        var unit = body.Height / 2;
        var sleeping = visual.Expression == Expression.Sleeping;
        var breathing = 1 + (sleeping ? 0.035 * Math.Sin(time * 1.6) : 0.012 * Math.Sin(time * 2.4));
        var animator = game.Animator;
        var rig = Rig.Interpolate(animator.PreviousRig, animator.Rig, alpha);
        var pose = alpha >= 1 ? visual.Pose : VisualPose.Lerp(visual.PreviousPose, visual.Pose, alpha);
        Rig = rig;

        ref var c = ref Look;
        c.Unit = unit;
        c.Time = time;
        c.LookX = pose.Look.X;
        c.LookY = pose.Look.Y;
        c.Blink = Math.Max(sleeping ? 1 : blink, visual.EyesClosed);
        c.Squash = pose.Squash * breathing * (1 + rig.Stretch);
        c.Lean = pose.Lean;
        c.Mouth = visual.Mouth;
        c.Expression = (double)visual.Expression;
        c.Blush = 0.25 + (game.Mood == Mood.Happy ? 0.2 : 0);
        c.Facing = pose.Facing;
        c.Opacity = Math.Clamp(opacity, 0, 1);
        c.Antenna = visual.Antenna;
        c.Pajamas = visual.Pajamas;
        c.Nightcap = visual.Nightcap;
        c.Shine = visual.Shine;
        c.ShadowDrop = groundBelow is { } ground ? Math.Max(0, (ground - feet.Y) / unit) : 99;
        c.ShadowAlpha = (Dark ? 0.34 : 0.22) * Math.Clamp(opacity, 0, 1);

        // Escondido: lo que queda al otro lado de la línea no se ve (y no hay sombra).
        Clip = visual.Clip;
        if (Clip is not null)
        {
            c.ShadowAlpha = 0;
        }

        Props = PoseProps(animator.Prop, animator.Wear, animator.Clip?.Gesture ?? Gesture.None, animator.PropSide, animator.Time);
        Held = HeldNow(game, alpha);
        BedFront = game.Bed is { } bed ? new BedPose(bed.Body.IsGrounded ? bed.Body.Feet : Vec2.Lerp(bed.PreviousFeet, bed.Body.Feet, alpha), bed.Radius, bed.Squash) : null;
        UpdateHandsOver();
    }

    /// <summary>Calcula dónde van los objetos con los datos actuales.</summary>
    public PropPose PoseProps(Prop prop, Wear wear, Gesture gesture, double side, double time)
    {
        var c = Look;
        var rig = Rig;
        var feet = Feet;
        Vec2 World(Vec2 local) => CreatureSpace.ToWorld(local, rig, feet, c.Unit, c.Squash, c.Lean);

        // Los ojos y la frente están en el espacio de la cara: se mueven con la cabeza (igual que al dibujarla).
        var head = KobiiShape.HeadCenter;
        var hunch = KobiiShape.HunchShift(rig, c.Facing) + new Vec2(0, KobiiShape.MeltDrop(rig.Melt));
        Vec2 Face(double x, double y)
        {
            var h = new Vec2(x + rig.FaceX, y + rig.FaceY) - head;
            return World(KobiiShape.Rotate(h, rig.HeadTilt) + head + hunch);
        }

        var shift = c.Facing * 0.16 + c.LookX * 0.05;
        var eyeY = -1.02 - 0.04 * KobiiShape.Grow + c.LookY * 0.04;
        return new PropPose(
            prop,
            wear,
            gesture,
            World(rig.Hand(side, Species.Form)),
            World(rig.Hand(-side, Species.Form)),
            Face(-0.31 + shift, eyeY),
            Face(0.31 + shift, eyeY),
            Face(shift * 0.8, eyeY - 0.36),
            c.Lean + rig.Spin + rig.HeadTilt * 0.5,
            side,
            time,
            c.Unit,
            feet.Y,
            rig.Grip(-side));
    }

    /// <summary>Un punto del cuerpo (en unidades de la criatura) en el mundo, con la postura de este fotograma.</summary>
    public Vec2 ToWorld(Vec2 local) => CreatureSpace.ToWorld(local, Rig, Feet, Look.Unit, Look.Squash, Look.Lean);

    /// <summary>La comida o el juguete que tiene en las manos ahora (entre el paso anterior y este).</summary>
    private HeldThing HeldNow(KobiiGame game, double alpha)
    {
        var turn = Look.Lean + Rig.Spin;
        foreach (var food in game.FoodItems)
        {
            if (food.Held)
            {
                var local = Vec2.Lerp(food.PreviousHeldLocal, food.HeldLocal, alpha);
                return new HeldThing(food.Kind, food.Bites, food.BiteSide, food.Remaining, null, ToWorld(local), turn + food.Tilt, 0, game.Body.Scale);
            }
        }

        foreach (var toy in game.Playground.Toys)
        {
            if (toy.Held)
            {
                var local = Vec2.Lerp(toy.PreviousHeldLocal, toy.HeldLocal, alpha);
                return new HeldThing(null, 0, 0, 1, toy.Kind, ToWorld(local), turn + toy.HeldTilt, toy.Spin, toy.Body.Scale);
            }
        }

        return default;
    }

    /// <summary>Huella de todo lo que se ve (para no repetir un fotograma idéntico).</summary>
    public int Signature()
    {
        var hash = new HashCode();
        var look = Look;
        var rig = Rig;
        hash.AddBytes(MemoryMarshal.AsBytes(new ReadOnlySpan<KobiiLook>(in look)));
        hash.AddBytes(MemoryMarshal.AsBytes(new ReadOnlySpan<Rig>(in rig)));
        hash.Add(Feet);
        hash.Add(Props);
        hash.Add(Held);
        hash.Add(BedFront);
        hash.Add(Palette);
        hash.Add(Clip);
        return hash.ToHashCode();
    }

    /// <summary>Las manos que agarran algo se repiten por encima de él.</summary>
    private void UpdateHandsOver()
    {
        ref var c = ref Look;
        var left = false;
        var right = false;
        if (Props.Prop != Prop.None)
        {
            var both = Props.Prop.TwoHanded() || Props.Gesture is Gesture.HoldBoth or Gesture.Carry or Gesture.Hug;
            left = Props.Side < 0 || both;
            right = Props.Side > 0 || both;
        }

        if (Held.Any && Held.Toy != ToyKind.Plush)
        {
            // Lo que lleva en las manos va por detrás de las que lo agarran (el osito abrazado no: le taparían la cara).
            left = right = true;
        }

        c.HandOverL = left && Rig.GripL > 0.5 ? 1 : 0;
        c.HandOverR = right && Rig.GripR > 0.5 ? 1 : 0;
    }
}
