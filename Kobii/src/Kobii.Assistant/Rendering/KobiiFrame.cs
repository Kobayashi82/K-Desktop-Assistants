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

/// <summary>Todo lo necesario para dibujar a Kobii en un fotograma (el <c>PetFrame</c> de Kobii AI sin comida ni juguetes).</summary>
internal sealed class KobiiFrame
{
    /// <summary>Pies en el mundo (interpolados), en píxeles físicos.</summary>
    public Vec2 Feet { get; set; }

    public double HeightPx { get; set; }

    /// <summary>Escala DIP → píxeles (tamaño elegido × DPI del monitor).</summary>
    public double Scale { get; set; } = 1;

    public CreatureConstants Constants;

    /// <summary>Esqueleto interpolado de este fotograma.</summary>
    public Rig Rig { get; set; }

    public bool Dark { get; set; }

    public Palette Palette { get; set; } = Species.Palettes[0];

    /// <summary>Lo que lleva en la mano o puesto (nada si no lleva nada).</summary>
    public PropPose Props { get; set; }

    /// <summary>Alguna mano agarra algo: hay que repetirla por encima de los objetos.</summary>
    public bool HandsOver => Constants.HandOverL > 0.5f || Constants.HandOverR > 0.5f;

    /// <summary>Hay algo más que la criatura que dibujar con Direct2D.</summary>
    public bool HasExtras => Props.Prop != Prop.None || Props.Wear != Wear.None;

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
        Scale = body.Scale;
        HeightPx = body.Height;

        var unit = (float)(body.Height / 2);
        var sleeping = visual.Expression == Expression.Sleeping;
        var breathing = 1 + (sleeping ? 0.035 * Math.Sin(time * 1.6) : 0.012 * Math.Sin(time * 2.4));
        var animator = game.Animator;
        var rig = Rig.Interpolate(animator.PreviousRig, animator.Rig, alpha);
        var pose = alpha >= 1 ? visual.Pose : VisualPose.Lerp(visual.PreviousPose, visual.Pose, alpha);

        ref var c = ref Constants;
        c.Unit = unit;
        c.Time = (float)time;
        c.Form = Species.Form;
        c.Hatch = -1;
        c.LookX = (float)pose.Look.X;
        c.LookY = (float)pose.Look.Y;
        c.Blink = sleeping ? 1 : (float)blink;
        c.Squash = (float)(pose.Squash * breathing * (1 + rig.Stretch));
        c.Lean = (float)pose.Lean;
        c.Mouth = (float)visual.Mouth;
        c.Expression = (float)visual.Expression;
        c.Blush = 0.25f + (game.Mood == Mood.Happy ? 0.2f : 0);
        c.Glow = 0;
        c.Crack = 0;
        c.Wobble = 0;
        c.Facing = (float)pose.Facing;
        c.WalkPhase = (float)visual.WalkPhase;
        c.Walk = (float)visual.Walk;
        c.Opacity = (float)Math.Clamp(opacity, 0, 1);
        c.Antenna = (float)visual.Antenna;
        c.Flash = 0;
        c.ShadowDrop = groundBelow is { } ground ? (float)Math.Max(0, (ground - feet.Y) / unit) : 99;
        c.ShadowAlpha = (Dark ? 0.34f : 0.22f) * (float)Math.Clamp(opacity, 0, 1);
        c.Shape = Species.ShapeId;
        c.Pale = 0;
        c.Dirt = 0;
        c.Ghost = 0;
        c.Wet = (float)visual.Wet;
        c.SetRig(rig);
        Rig = rig;
        c.Elder = 0;
        c.SetPalette(Palette);
        c.PassMode = 0;
        c.Shine = (float)visual.Shine;
        c.Pajamas = (float)visual.Pajamas;
        c.Nightcap = (float)visual.Nightcap;
        c.Blink = Math.Max(c.Blink, (float)visual.EyesClosed);

        // Escondido: la línea de recorte se pasa al lienzo cuando se conoce su origen (ver SetClip).
        Clip = visual.Clip;
        c.ClipSide = 0;
        if (Clip is not null)
        {
            c.ShadowAlpha = 0;
        }

        Props = PoseProps(animator.Prop, animator.Wear, animator.Clip?.Gesture ?? Gesture.None, animator.PropSide, animator.Time);
        UpdateHandsOver();
    }

    /// <summary>Pasa el lienzo, que empieza en <paramref name="origin"/> (en el mundo) y mide lo indicado, a las constantes.</summary>
    public void SetCanvas(Vec2 origin, int width, int height)
    {
        ref var c = ref Constants;
        c.ResolutionX = width;
        c.ResolutionY = height;
        c.OriginX = (float)(Feet.X - origin.X);
        c.OriginY = (float)(Feet.Y - origin.Y);
        if (Clip is not { } clip)
        {
            c.ClipSide = 0;
            return;
        }

        c.ClipX = (float)(clip.Position - (clip.Horizontal ? origin.Y : origin.X));
        c.ClipSide = clip.Side * (clip.Horizontal ? 2 : 1);
    }

    /// <summary>Calcula dónde van los objetos con las constantes actuales.</summary>
    public PropPose PoseProps(Prop prop, Wear wear, Gesture gesture, double side, double time)
    {
        var c = Constants;
        var rig = c.GetRig();
        var feet = Feet;
        Vec2 World(Vec2 local) => CreatureSpace.ToWorld(local, rig, feet, c.Unit, c.Squash, c.Lean);

        // Los ojos y la frente están en el espacio de la cara: se mueven con la cabeza (igual que en el shader).
        var head = new Vec2(0, -1.0);
        var hunch = new Vec2(Math.Clamp(c.Facing * 2.0, -1, 1) * 0.12, 0.09) * c.Hunch;
        Vec2 Face(double x, double y)
        {
            var h = new Vec2(x + c.FaceX, y + c.FaceY) - head;
            var cos = Math.Cos(c.HeadTilt);
            var sin = Math.Sin(c.HeadTilt);
            return World(new Vec2(cos * h.X - sin * h.Y, sin * h.X + cos * h.Y) + head + hunch);
        }

        var grow = Rig.Grow(c.Form);
        var shift = c.Facing * 0.16 + c.LookX * 0.05;
        var eyeY = -1.02 - 0.04 * grow + c.LookY * 0.04;
        return new PropPose(
            prop,
            wear,
            gesture,
            World(rig.Hand(side, c.Form)),
            World(rig.Hand(-side, c.Form)),
            Face(-0.31 + shift, eyeY),
            Face(0.31 + shift, eyeY),
            Face(shift * 0.8, eyeY - 0.36),
            c.Lean + c.Spin + c.HeadTilt * 0.5,
            side,
            time,
            c.Unit,
            feet.Y,
            rig.Grip(-side));
    }

    /// <summary>Las manos que agarran algo se repiten por encima de él.</summary>
    private void UpdateHandsOver()
    {
        ref var c = ref Constants;
        var left = false;
        var right = false;
        if (Props.Prop != Prop.None)
        {
            var both = Props.Prop.TwoHanded() || Props.Gesture is Gesture.HoldBoth or Gesture.Carry or Gesture.Hug;
            left = Props.Side < 0 || both;
            right = Props.Side > 0 || both;
        }

        c.HandOverL = left && c.GripL > 0.5f ? 1 : 0;
        c.HandOverR = right && c.GripR > 0.5f ? 1 : 0;
    }
}
