using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Kobii.Core.Animation;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;

namespace Kobii.Assistant.Rendering;

/// <summary>
/// Lo que va con Kobii además de su cuerpo: lo que lleva en la mano o puesto y las partículas (corazones, notas, confeti…).
/// Todo con la API de dibujo de WPF y en píxeles de pantalla (los bocadillos los pone el host).
/// </summary>
internal static class ScenePainter
{
    private static readonly uint[] BallColors = [0xEF5350, 0xFFCA28, 0x42A5F5, 0xFFFFFF];
    private static readonly Geometry HeartShape = Frozen("M0,0.9 C-1.3,0 -1,-1.2 0,-0.5 C1,-1.2 1.3,0 0,0.9 Z");
    private static readonly Geometry StarShape = Frozen("M0,-1 C0.1,-0.1 0.1,-0.1 1,0 C0.1,0.1 0.1,0.1 0,1 C-0.1,0.1 -0.1,0.1 -1,0 C-0.1,-0.1 -0.1,-0.1 0,-1 Z");
    private static readonly Geometry WaveShape = Frozen("M0,1 C0.6,0.6 -0.6,0.2 0,-0.2 C0.6,-0.5 -0.4,-0.8 0.1,-1");
    private static readonly Geometry VeinShape = Frozen("M0.18,-0.95 C0.22,-0.42 0.42,-0.22 0.95,-0.18");
    private static readonly Geometry ShardShape = Frozen("M-1,-0.4 L0.2,-1 L1,0.3 L-0.3,0.8 Z");
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Dictionary<string, Geometry> Labels = [];

    // Objetos -------------------------------------------------------------------------------------------

    /// <summary>Lo que lleva en la mano o puesto, por delante del cuerpo y detrás de las manos que lo agarran.</summary>
    public static void DrawProps(DrawingContext dc, in PropPose pose)
    {
        var u = pose.Unit;
        var hand = P(pose.Hand);
        var other = P(pose.OtherHand);
        var side = pose.Side;
        var rotation = pose.Rotation;
        var t = pose.Time;
        var line = Math.Max(1, 0.035 * u);

        switch (pose.Prop)
        {
            case Prop.Ball:
                DrawBall(dc, pose, hand, other, u, t);
                break;

            case Prop.Book:
                DrawBook(dc, Mid(hand, other) + new Vector(0, 0.06 * u), u, rotation, t);
                break;

            case Prop.Pillow:
            {
                // Abrazada contra la barriga (sin taparle la cara).
                var center = Mid(hand, other) + Rotate(new Vector(0, 0.30), rotation) * u;
                dc.PushTransform(new RotateTransform(Deg(rotation), center.X, center.Y));
                dc.DrawRoundedRectangle(Fill(0xEDE7F6), Stroke(0x9C86C8, 0.05 * u), new Rect(center.X - 0.36 * u, center.Y - 0.20 * u, 0.72 * u, 0.40 * u), 0.16 * u, 0.16 * u);
                dc.Pop();
                break;
            }

            case Prop.Flower:
            {
                var top = hand + Rotate(new Vector(side * 0.05, -0.48), rotation) * u;
                Stick(dc, hand, top, 0x7CB342, 0.06 * u);
                Petals(dc, top, 0.13 * u, 0xF48FB1, t * 0.5, line);
                break;
            }

            case Prop.Teacup:
            {
                // Taza humeante.
                var cup = hand + new Vector(side * 0.10 * u, -0.08 * u);
                dc.DrawEllipse(null, Stroke(0x90A4AE, 0.06 * u), cup + new Vector(side * 0.19 * u, 0), 0.07 * u, 0.06 * u);
                var body = new Rect(cup.X - 0.16 * u, cup.Y - 0.12 * u, 0.32 * u, 0.24 * u);
                dc.DrawRoundedRectangle(Fill(0xFFFFFF), Stroke(0x90A4AE, 0.03 * u), body, 0.06 * u, 0.06 * u);
                dc.DrawRoundedRectangle(Fill(0xA1887F), null, new Rect(cup.X - 0.12 * u, cup.Y - 0.10 * u, 0.24 * u, 0.06 * u), 0.03 * u, 0.03 * u);
                for (var i = 0; i < 2; i++)
                {
                    var rise = (t * 0.6 + i * 0.5) % 1;
                    var steam = cup + new Vector((i - 0.5) * 0.12 * u + Math.Sin(t * 3 + i) * 0.03 * u, -(0.18 + 0.35 * rise) * u);
                    dc.DrawEllipse(Fill(0xFFFFFF, 0.55 * (1 - rise)), null, steam, 0.04 * u, 0.06 * u);
                }

                break;
            }

            case Prop.Balloon:
            {
                // Globo atado con un hilo, que se mece.
                var sway = Math.Sin(t * 1.3) * 0.18 * u;
                var balloon = hand + new Vector(side * 0.1 * u + sway, -1.25 * u);
                var thread = new StreamGeometry();
                using (var g = thread.Open())
                {
                    g.BeginFigure(hand, false, false);
                    g.BezierTo(hand + new Vector(sway * 0.2, -0.4 * u), balloon + new Vector(-sway * 0.5, 0.6 * u), balloon + new Vector(0, 0.40 * u), true, true);
                }

                thread.Freeze();
                dc.DrawGeometry(null, Stroke(0x9E9E9E, Math.Max(1, 0.025 * u)), thread);
                dc.DrawEllipse(Fill(0xEF5350, 0.95), Stroke(0xB71C1C, line), balloon, 0.32 * u, 0.40 * u);
                dc.DrawEllipse(Fill(0xC62828), null, balloon + new Vector(0, 0.41 * u), 0.05 * u, 0.04 * u);
                dc.DrawEllipse(Fill(0xFFFFFF, 0.55), null, balloon - new Vector(0.12 * u, 0.16 * u), 0.07 * u, 0.11 * u);
                break;
            }

            case Prop.Leaf:
            {
                // Una hoja grande como paraguas.
                var top = hand + Rotate(new Vector(0, -0.55), rotation) * u;
                Stick(dc, hand, top, 0x558B2F, 0.06 * u);
                dc.PushTransform(new RotateTransform(Deg(rotation + side * 0.15), top.X, top.Y));
                dc.DrawEllipse(Fill(0x7CB342), Stroke(0x3E6B1F, line), top + new Vector(0, -0.05 * u), 0.62 * u, 0.26 * u);
                dc.DrawLine(Stroke(0x558B2F, 0.04 * u), top + new Vector(-0.55 * u, -0.05 * u), top + new Vector(0.55 * u, -0.05 * u));
                dc.Pop();
                break;
            }

            case Prop.Wand:
            {
                // Pompero: un palito con un aro.
                var ring = hand + Rotate(new Vector(side * 0.30, -0.18), rotation) * u;
                Stick(dc, hand, ring, 0x26A69A, 0.05 * u);
                var loop = ring + Rotate(new Vector(side * 0.08, -0.05), rotation) * u;
                dc.DrawEllipse(Fill(0xBFE6FF, 0.35), Stroke(0x26A69A, 0.045 * u), loop, 0.11 * u, 0.11 * u);
                break;
            }

            case Prop.Broom:
                DrawBroom(dc, hand, other, pose.GroundY, u, side, pose.OtherGrip);
                break;

            case Prop.Microphone:
            {
                var head = hand + Rotate(new Vector(-side * 0.04, -0.24), rotation) * u;
                Stick(dc, hand + Rotate(new Vector(side * 0.04, 0.14), rotation) * u, head, 0x37474F, 0.09 * u);
                dc.DrawEllipse(Fill(0xB0BEC5), Stroke(0x546E7A, 0.03 * u), head, 0.11 * u, 0.11 * u);
                break;
            }
        }

        switch (pose.Wear)
        {
            case Wear.Glasses:
                DrawGlasses(dc, P(pose.EyeL), P(pose.EyeR), u);
                break;
            case Wear.GlassesUp:
            {
                var forehead = P(pose.Forehead);
                var half = (P(pose.EyeR) - P(pose.EyeL)) / 2;
                DrawGlasses(dc, forehead - half, forehead + half, u * 0.9);
                break;
            }
        }
    }

    private static void DrawBall(DrawingContext dc, in PropPose pose, Point hand, Point other, double u, double t)
    {
        var r = 0.26 * u;
        Point center;
        switch (pose.Gesture)
        {
            case Gesture.Dribble:
            {
                // Bota entre la mano y el suelo, al ritmo de la mano.
                var ground = pose.GroundY - r;
                var top = hand.Y + r;
                var phase = Math.Abs(Math.Cos(t * Math.PI / 0.7));
                center = new Point(hand.X, top + (ground - top) * (1 - phase));
                break;
            }

            case Gesture.HoldUp:
                center = hand - new Vector(0, r * 0.9);
                break;
            default:
                center = Mid(hand, other) + new Vector(0, 0.05 * u);
                break;
        }

        // Pelota de playa: gajos de colores que giran, contorno y un brillo.
        var ball = new EllipseGeometry(center, r, r);
        ball.Freeze();
        dc.DrawGeometry(Fill(0xFFFFFF), null, ball);
        dc.PushClip(ball);
        for (var i = 0; i < 3; i++)
        {
            dc.PushTransform(new RotateTransform(Deg(t * 2 + i * Math.PI / 3), center.X, center.Y));
            dc.DrawEllipse(Fill(BallColors[i]), null, center, r * 1.05, r * 0.30);
            dc.Pop();
        }

        dc.Pop();
        dc.DrawGeometry(null, Stroke(0x8A98A3, Math.Max(1, 0.035 * u)), ball);
        dc.DrawEllipse(Fill(0xFFFFFF, 0.6), null, center - new Vector(r * 0.35, r * 0.4), r * 0.22, r * 0.15);
    }

    private static void DrawBook(DrawingContext dc, Point center, double u, double rotation, double t)
    {
        dc.PushTransform(new RotateTransform(Deg(rotation), center.X, center.Y));
        var w = 0.50 * u;
        var h = 0.32 * u;
        dc.DrawRoundedRectangle(Fill(0x5C6BC0), Stroke(0x3949AB, 0.03 * u), new Rect(center.X - w - 0.03 * u, center.Y - h - 0.02 * u, 2 * w + 0.06 * u, 2 * h + 0.06 * u), 0.05 * u, 0.05 * u);
        dc.DrawRoundedRectangle(Fill(0xFFFDF6), null, new Rect(center.X - w, center.Y - h, w - 0.01 * u, 2 * h), 0.03 * u, 0.03 * u);
        dc.DrawRoundedRectangle(Fill(0xFFFDF6), null, new Rect(center.X + 0.01 * u, center.Y - h, w - 0.01 * u, 2 * h), 0.03 * u, 0.03 * u);
        var text = Stroke(0xB0BEC5, Math.Max(1, 0.025 * u));
        for (var i = 0; i < 4; i++)
        {
            var y = center.Y - h * 0.6 + i * h * 0.38;
            dc.DrawLine(text, new Point(center.X - w * 0.85, y), new Point(center.X - w * 0.15, y));
            dc.DrawLine(text, new Point(center.X + w * 0.15, y), new Point(center.X + w * (i == 3 ? 0.5 : 0.85), y));
        }

        // Pasa una página de vez en cuando.
        var turn = t % 6 / 0.6;
        if (turn < 1)
        {
            var x = center.X + w * (1 - 2 * turn);
            dc.DrawRoundedRectangle(Fill(0xF3EFE6, 0.95), null, new Rect(Math.Min(x, center.X), center.Y - h, Math.Abs(x - center.X), 2 * h), 0.03 * u, 0.03 * u);
        }

        dc.Pop();
    }

    /// <summary>Una escobita: el palo pasa por las dos manos (la de abajo, <paramref name="lower"/>) y las cerdas barren el suelo.</summary>
    private static void DrawBroom(DrawingContext dc, Point lower, Point upper, double ground, double u, double side, double otherGrip)
    {
        // El palo va hacia abajo (aunque las manos estén a la misma altura) y no se alarga más de la cuenta. Si la otra
        // mano lo suelta, queda casi de pie en la mano que lo sujeta.
        var dir = lower - upper;
        dir = Normalize(new Vector(dir.X, Math.Max(dir.Y, Math.Abs(dir.X) * 0.6 + 0.01)));
        var held = Math.Clamp(otherGrip * otherGrip * (3 - 2 * otherGrip), 0, 1);
        dir = Normalize(Normalize(new Vector(side * 0.18, 1)) * (1 - held) + dir * held);
        upper = lower - dir * 0.55 * u + ((upper - lower) + dir * 0.55 * u) * held;
        var bristles = 0.36 * u;
        var neck = lower + dir * Math.Clamp((ground - lower.Y - bristles * dir.Y) / dir.Y, 0.12 * u, 0.9 * u);
        Stick(dc, upper - dir * 0.24 * u, neck, 0xA1887F, 0.075 * u);

        // Cerdas de paja en abanico, con su atadura roja.
        var normal = new Vector(-dir.Y, dir.X);
        var end = neck + dir * bristles;
        var fan = new StreamGeometry();
        using (var g = fan.Open())
        {
            g.BeginFigure(neck + normal * 0.09 * u, true, true);
            g.LineTo(end + normal * 0.22 * u, true, true);
            g.QuadraticBezierTo(end + dir * 0.04 * u, end - normal * 0.22 * u, true, true);
            g.LineTo(neck - normal * 0.09 * u, true, true);
        }

        fan.Freeze();
        dc.DrawGeometry(Fill(0xE8C170), Stroke(0xB8893C, Math.Max(1, 0.03 * u)), fan);
        for (var i = -1; i <= 1; i++)
        {
            dc.DrawLine(Stroke(0xC9A04E, Math.Max(1, 0.02 * u)), neck + normal * (0.05 * i * u) + dir * 0.06 * u, end + normal * (0.13 * i * u) - dir * 0.03 * u);
        }

        Stick(dc, neck - normal * 0.10 * u, neck + normal * 0.10 * u, 0xD84343, 0.075 * u);
    }

    private static void DrawGlasses(DrawingContext dc, Point left, Point right, double u)
    {
        var r = 0.17 * u;
        var frame = Stroke(0x5D4037, Math.Max(1, 0.045 * u));
        dc.DrawEllipse(Fill(0xE3F2FD, 0.25), frame, left, r, r * 0.92);
        dc.DrawEllipse(Fill(0xE3F2FD, 0.25), frame, right, r, r * 0.92);
        var dir = Normalize(right - left + new Vector(1e-3, 0));
        dc.DrawLine(Stroke(0x5D4037, Math.Max(1, 0.04 * u)), left + dir * r, right - dir * r);
        dc.DrawEllipse(Fill(0xFFFFFF, 0.7), null, left - new Vector(r * 0.35, r * 0.35), r * 0.18, r * 0.12);
        dc.DrawEllipse(Fill(0xFFFFFF, 0.7), null, right - new Vector(r * 0.35, r * 0.35), r * 0.18, r * 0.12);
    }

    /// <summary>Un palo con las puntas redondeadas.</summary>
    private static void Stick(DrawingContext dc, Point from, Point to, uint color, double width) =>
        dc.DrawLine(Stroke(color, Math.Max(1, width)), from, to);

    private static void Petals(DrawingContext dc, Point center, double size, uint color, double spin, double line)
    {
        var outline = Stroke(0xD06A8E, line * 0.7);
        for (var k = 0; k < 5; k++)
        {
            var a = spin + k * Math.Tau / 5;
            dc.DrawEllipse(null, outline, center + new Vector(Math.Cos(a), Math.Sin(a)) * size, size * 0.8, size * 0.8);
        }

        for (var k = 0; k < 5; k++)
        {
            var a = spin + k * Math.Tau / 5;
            dc.DrawEllipse(Fill(color), null, center + new Vector(Math.Cos(a), Math.Sin(a)) * size, size * 0.8, size * 0.8);
        }

        dc.DrawEllipse(Fill(0xFFD54F), null, center, size * 0.6, size * 0.6);
    }

    // Partículas ----------------------------------------------------------------------------------------

    /// <summary>Las partículas, en píxeles de pantalla.</summary>
    public static void DrawParticles(DrawingContext dc, IReadOnlyList<Particle> particles)
    {
        foreach (var particle in particles)
        {
            DrawParticle(dc, particle);
        }
    }

    private static void DrawParticle(DrawingContext dc, Particle p)
    {
        var at = P(p.Position);
        var alpha = p.Alpha;
        var size = p.Size;
        switch (p.Kind)
        {
            case EmoteKind.Heart:
                Shape(dc, HeartShape, at, size * (0.8 + 0.2 * Math.Min(1, p.Age * 6)), 0, p.Color, alpha, outline: true);
                break;
            case EmoteKind.Sparkle:
                Shape(dc, StarShape, at, size * (0.6 + 0.4 * Math.Sin(p.Age * 18)), p.Rotation, p.Color, alpha);
                break;
            case EmoteKind.Shell:
                Shape(dc, ShardShape, at, size, p.Rotation, p.Color, alpha);
                break;
            case EmoteKind.Sleep:
                Label(dc, "z", at, size * (0.7 + 0.6 * p.T), p.Color, alpha);
                break;
            case EmoteKind.Note:
                Label(dc, "♪", at, size, p.Color, alpha);
                break;
            case EmoteKind.Question:
                Label(dc, "?", at, size * Math.Min(1, 0.6 + p.Age * 4), p.Color, alpha);
                break;
            case EmoteKind.Exclamation:
                Label(dc, "!", at, size * Math.Min(1, 0.6 + p.Age * 4), p.Color, alpha);
                break;
            case EmoteKind.Dust:
                dc.DrawEllipse(Fill(p.Color, alpha * 0.5), null, at, size * (1 + p.T), size * (0.7 + p.T * 0.6));
                break;
            case EmoteKind.Warmth:
                dc.DrawEllipse(Fill(p.Color, alpha * 0.2), null, at, size * 1.8, size * 1.8);
                dc.DrawEllipse(Fill(p.Color, alpha * 0.6), null, at, size, size);
                break;
            case EmoteKind.Bubble:
                dc.DrawEllipse(Fill(0xFFFFFF, alpha * 0.3), Stroke(p.Color, Math.Max(1, size * 0.15), alpha * 0.9), at, size, size);
                dc.DrawEllipse(Fill(0xFFFFFF, alpha * 0.9), null, at - new Vector(size * 0.35, size * 0.35), size * 0.25, size * 0.25);
                break;
            case EmoteKind.Drop:
                dc.DrawEllipse(Fill(p.Color, alpha * 0.85), null, at, size * 0.7, size);
                dc.DrawEllipse(Fill(0xFFFFFF, alpha * 0.7), null, at - new Vector(size * 0.2, size * 0.3), size * 0.2, size * 0.3);
                break;
            case EmoteKind.Stink:
            {
                var pen = Stroke(p.Color, 0.22, alpha * 0.8);
                dc.PushTransform(new MatrixTransform(size, 0, 0, size, at.X, at.Y));
                dc.DrawGeometry(null, pen, WaveShape);
                dc.Pop();
                dc.PushTransform(new MatrixTransform(size * 0.8, 0, 0, size * 0.8, at.X + size * 0.9, at.Y + size * 0.4));
                dc.DrawGeometry(null, pen, WaveShape);
                dc.Pop();
                break;
            }

            case EmoteKind.Fly:
            {
                var flap = 0.6 + 0.4 * Math.Sin(p.Age * 70);
                dc.DrawEllipse(Fill(0xFFFFFF, alpha * 0.75), null, at + new Vector(-size * 0.7, -size * 0.9), size * 0.8, size * 0.55 * flap);
                dc.DrawEllipse(Fill(0xFFFFFF, alpha * 0.75), null, at + new Vector(size * 0.7, -size * 0.9), size * 0.8, size * 0.55 * flap);
                dc.DrawEllipse(Fill(p.Color, alpha), null, at, size, size * 0.85);
                break;
            }

            case EmoteKind.Flower:
                dc.DrawLine(Stroke(0x7CB342, size * 0.3), at, at + new Vector(0, size * 1.6));
                for (var k = 0; k < 5; k++)
                {
                    var a = p.Rotation + k * Math.Tau / 5;
                    dc.DrawEllipse(Fill(p.Color, alpha), null, at + new Vector(Math.Cos(a), Math.Sin(a)) * size * 0.55, size * 0.42, size * 0.42);
                }

                dc.DrawEllipse(Fill(0xFFD54F, alpha), null, at, size * 0.32, size * 0.32);
                break;
            case EmoteKind.Paint:
                dc.DrawEllipse(Fill(p.Color, alpha), null, at, size, size * 1.15);
                break;
            case EmoteKind.Leaf:
                dc.PushTransform(new RotateTransform(Deg(p.Rotation), at.X, at.Y));
                dc.DrawEllipse(Fill(p.Color, alpha), null, at, size, size * 0.5);
                dc.DrawLine(Stroke(0x8D4A21, Math.Max(1, size * 0.15), alpha * 0.8), at - new Vector(size * 1.3, 0), at + new Vector(size * 0.8, 0));
                dc.Pop();
                break;
            case EmoteKind.Fluff:
                dc.DrawEllipse(Fill(p.Color, alpha * 0.9), null, at, size, size * 0.8);
                dc.DrawEllipse(Fill(p.Color, alpha * 0.8), null, at + new Vector(size * 0.6, -size * 0.3), size * 0.6, size * 0.5);
                dc.DrawEllipse(Fill(p.Color, alpha * 0.8), null, at - new Vector(size * 0.5, size * 0.2), size * 0.5, size * 0.45);
                break;

            case EmoteKind.Burst:
            {
                // Estrella de ocho puntas que crece de golpe, con el centro blanco.
                var grow = 0.55 + 0.6 * Easing.Apply(Ease.Out, Math.Min(1, p.Age / 0.12));
                var fade = Math.Clamp(1 - (p.Age - 0.12) / 0.26, 0, 1);
                Shape(dc, StarShape, at, size * grow, p.Rotation, p.Color, fade);
                Shape(dc, StarShape, at, size * grow * 0.8, p.Rotation + Math.PI / 4, 0xFFB300, fade);
                dc.DrawEllipse(Fill(0xFFFFFF, fade), null, at, size * grow * 0.32, size * grow * 0.32);
                break;
            }

            case EmoteKind.Confetti:
            {
                // Un papelito que gira en el aire: se estrecha al ponerse de canto y por detrás se ve más apagado.
                var flip = Math.Cos(p.Age * (7 + Math.Abs(p.Spin)) + p.Anchor.Y);
                var width = size * (0.25 + 0.75 * Math.Abs(flip));
                dc.PushTransform(new RotateTransform(Deg(p.Rotation), at.X, at.Y));
                dc.DrawRectangle(Fill(p.Color, alpha * (flip >= 0 ? 1 : 0.72)), null, new Rect(at.X - width / 2, at.Y - size * 0.32, width, size * 0.64));
                dc.Pop();
                break;
            }

            case EmoteKind.Anger:
            {
                // 💢: aparece de golpe y late (cada vez menos), con un borde blanco para que se lea sobre cualquier fondo.
                var pop = Easing.Apply(Ease.Out, Math.Min(1, p.Age / 0.1)) * (1 + 0.16 * Math.Sin(p.Age * 15) * Math.Exp(-p.Age * 1.5));
                for (var pass = 0; pass < 2; pass++)
                {
                    var pen = Stroke(pass == 0 ? 0xFFFFFFu : p.Color, pass == 0 ? 0.5 : 0.27, alpha * (pass == 0 ? 0.85 : 1));
                    for (var k = 0; k < 4; k++)
                    {
                        var m = Matrix.Identity;
                        m.Rotate(Deg(p.Rotation + k * Math.PI / 2));
                        m.Scale(size * pop, size * pop);
                        m.Translate(at.X, at.Y);
                        dc.PushTransform(new MatrixTransform(m));
                        dc.DrawGeometry(null, pen, VeinShape);
                        dc.Pop();
                    }
                }

                break;
            }

            default:
                dc.DrawEllipse(Fill(p.Color, alpha), null, at, size, size);
                break;
        }
    }

    /// <summary>Un símbolo de 2×2 unidades centrado en el origen, girado y escalado.</summary>
    private static void Shape(DrawingContext dc, Geometry shape, Point at, double size, double rotation, uint color, double alpha, bool outline = false)
    {
        var m = Matrix.Identity;
        m.Rotate(Deg(rotation));
        m.Scale(size, size);
        m.Translate(at.X, at.Y);
        dc.PushTransform(new MatrixTransform(m));
        dc.DrawGeometry(Fill(color, alpha), outline ? Stroke(0xFFFFFF, 0.22, alpha * 0.85) : null, shape);
        dc.Pop();
    }

    /// <summary>Un signo (z, ♪, ?, !) con un borde claro para que se lea sobre cualquier fondo.</summary>
    private static void Label(DrawingContext dc, string text, Point center, double size, uint color, double alpha)
    {
        if (!Labels.TryGetValue(text, out var glyph))
        {
            // Se hace una vez a 100 px y se escala: así no hay que medir texto en cada fotograma.
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, 100, Brushes.Black, 1);
            glyph = formatted.BuildGeometry(new Point(-formatted.Width / 2, -formatted.Height / 2));
            glyph.Freeze();
            Labels[text] = glyph;
        }

        var scale = size / 100;
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, center.X, center.Y));
        dc.DrawGeometry(null, Stroke(0xFFFFFF, 14, alpha * 0.8), glyph);
        dc.DrawGeometry(Fill(color, alpha), null, glyph);
        dc.Pop();
    }

    // Utilidades ----------------------------------------------------------------------------------------

    private static SolidColorBrush Fill(uint rgb, double alpha = 1) =>
        KobiiPainter.Brush(Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    private static Pen Stroke(uint rgb, double width, double alpha = 1) =>
        KobiiPainter.Pen(Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb), width);

    private static Geometry Frozen(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static Point P(Vec2 v) => new(v.X, v.Y);

    private static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static double Deg(double radians) => radians * 180 / Math.PI;

    private static Vector Rotate(Vector v, double angle)
    {
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        return new Vector(c * v.X - s * v.Y, s * v.X + c * v.Y);
    }

    private static Vector Normalize(Vector v)
    {
        var length = v.Length;
        return length > 1e-9 ? v / length : new Vector(0, 1);
    }
}
