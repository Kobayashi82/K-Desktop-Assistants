using System.Numerics;
using Kobii.Core.Animation;
using Kobii.Core.Geometry;
using Vortice;
using Vortice.Direct2D1;

namespace Kobii.Assistant.Rendering;

/// <summary>Los objetos que lleva en la mano o puestos (pelota, libro, bastón, gafas…).</summary>
internal sealed partial class ScenePainter
{
    private static readonly uint[] BallColors = [0xEF5350, 0xFFCA28, 0x42A5F5, 0xFFFFFF];

    private void DrawProps(ID2D1DeviceContext dc, Vec2 origin, in PropPose pose)
    {
        var u = (float)pose.Unit;
        var hand = ToVector(pose.Hand - origin);
        var other = ToVector(pose.OtherHand - origin);
        var side = (float)pose.Side;
        var rotation = (float)pose.Rotation;
        var t = pose.Time;
        dc.Transform = Matrix3x2.Identity;

        switch (pose.Prop)
        {
            case Prop.Ball:
                DrawBall(dc, pose, hand, other, origin, u, t);
                break;
            case Prop.Book:
                DrawBook(dc, (hand + other) / 2 + new Vector2(0, 0.06f * u), u, rotation, t);
                break;
            case Prop.Pillow:
            {
                // Abrazada contra la barriga (sin taparle la cara).
                var center = (hand + other) / 2 + Rotate(new Vector2(0, 0.30f), rotation) * u;
                dc.Transform = Matrix3x2.CreateRotation(rotation, center);
                var r = new RawRectF(center.X - 0.36f * u, center.Y - 0.20f * u, center.X + 0.36f * u, center.Y + 0.20f * u);
                Fill(dc, new RoundedRectangle { Rect = r, RadiusX = 0.16f * u, RadiusY = 0.16f * u }, 0xEDE7F6, 1);
                _brush.Color = ToColor(0xB39DDB, 1);
                dc.DrawRoundedRectangle(new RoundedRectangle { Rect = r, RadiusX = 0.16f * u, RadiusY = 0.16f * u }, _brush, 0.05f * u);
                dc.Transform = Matrix3x2.Identity;
                break;
            }

            case Prop.Cane:
            {
                // Bastón de madera con el puño curvo hacia delante, apoyado en el suelo.
                var ground = new Vector2(hand.X + side * 0.08f * u, (float)(pose.GroundY - origin.Y));
                Stick(dc, hand + new Vector2(0, 0.05f * u), ground, 0x8D6E63, 0.10f * u);
                var hook = hand + new Vector2(side * 0.16f * u, -0.02f * u);
                Stick(dc, hand + new Vector2(-side * 0.04f * u, 0.02f * u), hook - new Vector2(0, 0.06f * u), 0x8D6E63, 0.10f * u);
                Stick(dc, hook - new Vector2(0, 0.06f * u), hook + new Vector2(side * 0.02f * u, 0.05f * u), 0x8D6E63, 0.10f * u);
                Fill(dc, new Ellipse(ground, 0.06f * u, 0.03f * u), 0x5D4037, 1);
                break;
            }

            case Prop.Flower:
            {
                var top = hand + Rotate(new Vector2(side * 0.05f, -0.48f), rotation) * u;
                Stick(dc, hand, top, 0x7CB342, 0.06f * u);
                Petals(dc, top, 0.13f * u, 0xF48FB1, (float)t * 0.5f);
                break;
            }

            case Prop.Brush:
            {
                var tip = hand + Rotate(new Vector2(side * 0.42f, -0.36f), rotation) * u;
                Stick(dc, hand - Rotate(new Vector2(side * 0.10f, -0.08f), rotation) * u, tip, 0xA1887F, 0.07f * u);
                Fill(dc, new Ellipse(tip, 0.07f * u, 0.07f * u), BallColors[(int)(t / 2) % 3], 1);
                break;
            }

            case Prop.Teacup:
            {
                // Taza humeante.
                var cup = hand + new Vector2(side * 0.10f * u, -0.08f * u);
                Fill(dc, new RoundedRectangle { Rect = new RawRectF(cup.X - 0.16f * u, cup.Y - 0.12f * u, cup.X + 0.16f * u, cup.Y + 0.12f * u), RadiusX = 0.06f * u, RadiusY = 0.06f * u }, 0xFFFFFF, 1);
                Stroke(dc, new Ellipse(cup + new Vector2(side * 0.19f * u, 0), 0.07f * u, 0.06f * u), 0xFFFFFF, 0.04f * u);
                Fill(dc, new RoundedRectangle { Rect = new RawRectF(cup.X - 0.12f * u, cup.Y - 0.10f * u, cup.X + 0.12f * u, cup.Y - 0.04f * u), RadiusX = 0.03f * u, RadiusY = 0.03f * u }, 0xA1887F, 1);
                _brush.Color = ToColor(0x90A4AE, 1);
                dc.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cup.X - 0.16f * u, cup.Y - 0.12f * u, cup.X + 0.16f * u, cup.Y + 0.12f * u), RadiusX = 0.06f * u, RadiusY = 0.06f * u }, _brush, 0.025f * u);
                for (var i = 0; i < 2; i++)
                {
                    var rise = (float)((t * 0.6 + i * 0.5) % 1);
                    var steam = cup + new Vector2((i - 0.5f) * 0.12f * u + (float)Math.Sin(t * 3 + i) * 0.03f * u, -(0.18f + 0.35f * rise) * u);
                    Fill(dc, new Ellipse(steam, 0.04f * u, 0.06f * u), 0xFFFFFF, 0.55f * (1 - rise));
                }

                break;
            }

            case Prop.Balloon:
            {
                // Globo atado con un hilo, que se mece.
                var sway = (float)Math.Sin(t * 1.3) * 0.18f * u;
                var balloon = hand + new Vector2(side * 0.1f * u + sway, -1.25f * u);
                using (var path = _gpu.D2DFactory.CreatePathGeometry())
                {
                    using (var sink = path.Open())
                    {
                        sink.BeginFigure(hand, FigureBegin.Hollow);
                        sink.AddBezier(new BezierSegment { Point1 = hand + new Vector2(sway * 0.2f, -0.4f * u), Point2 = balloon + new Vector2(-sway * 0.5f, 0.6f * u), Point3 = balloon + new Vector2(0, 0.40f * u) });
                        sink.EndFigure(FigureEnd.Open);
                        sink.Close();
                    }

                    _brush.Color = ToColor(0x9E9E9E, 1);
                    dc.DrawGeometry(path, _brush, Math.Max(1, 0.025f * u));
                }

                Fill(dc, new Ellipse(balloon, 0.32f * u, 0.40f * u), 0xEF5350, 0.95f);
                Fill(dc, new Ellipse(balloon + new Vector2(0, 0.40f * u), 0.05f * u, 0.04f * u), 0xC62828, 1);
                Fill(dc, new Ellipse(balloon - new Vector2(0.12f * u, 0.16f * u), 0.07f * u, 0.11f * u), 0xFFFFFF, 0.55f);
                break;
            }

            case Prop.Leaf:
            {
                // Una hoja grande como paraguas.
                var top = hand + Rotate(new Vector2(0, -0.55f), rotation) * u;
                Stick(dc, hand, top, 0x558B2F, 0.06f * u);
                dc.Transform = Matrix3x2.CreateRotation(rotation + side * 0.15f, top);
                Fill(dc, new Ellipse(top + new Vector2(0, -0.05f * u), 0.62f * u, 0.26f * u), 0x7CB342, 1);
                Line(dc, top + new Vector2(-0.55f * u, -0.05f * u), top + new Vector2(0.55f * u, -0.05f * u), 0x558B2F, 0.04f * u);
                dc.Transform = Matrix3x2.Identity;
                break;
            }

            case Prop.Wand:
            {
                // Pompero: un palito con un aro.
                var ring = hand + Rotate(new Vector2(side * 0.30f, -0.18f), rotation) * u;
                Stick(dc, hand, ring, 0x26A69A, 0.05f * u);
                Stroke(dc, new Ellipse(ring + Rotate(new Vector2(side * 0.08f, -0.05f), rotation) * u, 0.11f * u, 0.11f * u), 0x26A69A, 0.045f * u);
                Fill(dc, new Ellipse(ring + Rotate(new Vector2(side * 0.08f, -0.05f), rotation) * u, 0.09f * u, 0.09f * u), 0xBFE6FF, 0.35f);
                break;
            }

            case Prop.Broom:
                DrawBroom(dc, hand, other, (float)(pose.GroundY - origin.Y), u, side, (float)pose.OtherGrip);
                break;

            case Prop.Microphone:
            {
                var head = hand + Rotate(new Vector2(-side * 0.04f, -0.24f), rotation) * u;
                Stick(dc, hand + Rotate(new Vector2(side * 0.04f, 0.14f), rotation) * u, head, 0x37474F, 0.09f * u);
                Fill(dc, new Ellipse(head, 0.11f * u, 0.11f * u), 0xB0BEC5, 1);
                Stroke(dc, new Ellipse(head, 0.11f * u, 0.11f * u), 0x546E7A, 0.03f * u);
                break;
            }
        }

        switch (pose.Wear)
        {
            case Wear.Glasses:
                DrawGlasses(dc, ToVector(pose.EyeL - origin), ToVector(pose.EyeR - origin), u);
                break;
            case Wear.GlassesUp:
            {
                // Subidas en la frente (y él buscándolas por todas partes).
                var forehead = ToVector(pose.Forehead - origin);
                var half = (ToVector(pose.EyeR - origin) - ToVector(pose.EyeL - origin)) / 2;
                DrawGlasses(dc, forehead - half, forehead + half, u * 0.9f);
                break;
            }
        }

        dc.Transform = Matrix3x2.Identity;
    }

    private void DrawBall(ID2D1DeviceContext dc, in PropPose pose, Vector2 hand, Vector2 other, Vec2 origin, float u, double t)
    {
        var r = 0.26f * u;
        Vector2 center;
        switch (pose.Gesture)
        {
            case Gesture.Dribble:
            {
                // Bota entre la mano y el suelo, al ritmo de la mano.
                var ground = (float)(pose.GroundY - origin.Y) - r;
                var top = hand.Y + r;
                var phase = Math.Abs(Math.Cos(t * Math.PI / 0.7));
                center = new Vector2(hand.X, top + (ground - top) * (1 - (float)phase));
                break;
            }

            case Gesture.HoldUp:
                center = hand - new Vector2(0, r * 0.9f);
                break;
            default:
                center = (hand + other) / 2 + new Vector2(0, 0.05f * u);
                break;
        }

        // Pelota de playa: gajos de colores y un brillo.
        var spin = (float)(t * 2);
        for (var i = 0; i < 4; i++)
        {
            dc.Transform = Matrix3x2.CreateRotation(spin + i * MathF.PI / 4, center);
            Fill(dc, new Ellipse(center, r, r * 0.42f), BallColors[i], 1);
        }

        dc.Transform = Matrix3x2.Identity;
        Stroke(dc, new Ellipse(center, r, r), 0xB0BEC5, Math.Max(1, 0.03f * u));
        Fill(dc, new Ellipse(center - new Vector2(r * 0.35f, r * 0.4f), r * 0.22f, r * 0.15f), 0xFFFFFF, 0.6f);
    }

    private void DrawBook(ID2D1DeviceContext dc, Vector2 center, float u, float rotation, double t)
    {
        dc.Transform = Matrix3x2.CreateRotation(rotation, center);
        var w = 0.50f * u;
        var h = 0.32f * u;
        Fill(dc, new RoundedRectangle { Rect = new RawRectF(center.X - w - 0.03f * u, center.Y - h - 0.02f * u, center.X + w + 0.03f * u, center.Y + h + 0.04f * u), RadiusX = 0.05f * u, RadiusY = 0.05f * u }, 0x5C6BC0, 1);
        Fill(dc, new RoundedRectangle { Rect = new RawRectF(center.X - w, center.Y - h, center.X - 0.01f * u, center.Y + h), RadiusX = 0.03f * u, RadiusY = 0.03f * u }, 0xFFFDF6, 1);
        Fill(dc, new RoundedRectangle { Rect = new RawRectF(center.X + 0.01f * u, center.Y - h, center.X + w, center.Y + h), RadiusX = 0.03f * u, RadiusY = 0.03f * u }, 0xFFFDF6, 1);
        for (var i = 0; i < 4; i++)
        {
            var y = center.Y - h * 0.6f + i * h * 0.38f;
            Line(dc, new Vector2(center.X - w * 0.85f, y), new Vector2(center.X - w * 0.15f, y), 0xB0BEC5, Math.Max(1, 0.025f * u));
            Line(dc, new Vector2(center.X + w * 0.15f, y), new Vector2(center.X + w * (i == 3 ? 0.5f : 0.85f), y), 0xB0BEC5, Math.Max(1, 0.025f * u));
        }

        // Pasa una página de vez en cuando.
        var turn = (float)((t % 6) / 0.6);
        if (turn < 1)
        {
            var x = center.X + w * (1 - 2 * turn);
            Fill(dc, new RoundedRectangle { Rect = new RawRectF(Math.Min(x, center.X), center.Y - h, Math.Max(x, center.X), center.Y + h), RadiusX = 0.03f * u, RadiusY = 0.03f * u }, 0xF3EFE6, 0.95f);
        }

        dc.Transform = Matrix3x2.Identity;
    }

    /// <summary>Una escobita: el palo pasa por las dos manos (la de abajo, <paramref name="lower"/>) y las cerdas barren el suelo.</summary>
    private void DrawBroom(ID2D1DeviceContext dc, Vector2 lower, Vector2 upper, float ground, float u, float side, float otherGrip)
    {
        // El palo va hacia abajo (aunque las manos estén a la misma altura) y no se alarga más de la cuenta. Si
        // la otra mano lo suelta, queda casi de pie en la mano que lo sujeta.
        var dir = lower - upper;
        dir = Vector2.Normalize(new Vector2(dir.X, MathF.Max(dir.Y, MathF.Abs(dir.X) * 0.6f + 0.01f)));
        var held = otherGrip * otherGrip * (3 - 2 * otherGrip);
        dir = Vector2.Normalize(Vector2.Lerp(Vector2.Normalize(new Vector2(side * 0.18f, 1)), dir, Math.Clamp(held, 0, 1)));
        upper = Vector2.Lerp(lower - dir * 0.55f * u, upper, Math.Clamp(held, 0, 1));
        var bristles = 0.36f * u;
        var neck = lower + dir * Math.Clamp((ground - lower.Y - bristles * dir.Y) / dir.Y, 0.12f * u, 0.9f * u);
        Stick(dc, upper - dir * 0.24f * u, neck, 0xA1887F, 0.075f * u);

        // Cerdas de paja en abanico, con su atadura roja.
        var normal = new Vector2(-dir.Y, dir.X);
        var end = neck + dir * bristles;
        using (var fan = _gpu.D2DFactory.CreatePathGeometry())
        {
            using (var sink = fan.Open())
            {
                sink.BeginFigure(neck + normal * 0.09f * u, FigureBegin.Filled);
                sink.AddLine(end + normal * 0.22f * u);
                sink.AddQuadraticBezier(new QuadraticBezierSegment { Point1 = end + dir * 0.04f * u, Point2 = end - normal * 0.22f * u });
                sink.AddLine(neck - normal * 0.09f * u);
                sink.EndFigure(FigureEnd.Closed);
                sink.Close();
            }

            FillGeometry(dc, fan, 0xE8C170, 1);
            DrawGeometry(dc, fan, 0xB8893C, Math.Max(1, 0.03f * u), 1);
        }

        for (var i = -1; i <= 1; i++)
        {
            Line(dc, neck + normal * (0.05f * i * u) + dir * 0.06f * u, end + normal * (0.13f * i * u) - dir * 0.03f * u, 0xC9A04E, Math.Max(1, 0.02f * u));
        }

        Stick(dc, neck - normal * 0.10f * u, neck + normal * 0.10f * u, 0xD84343, 0.075f * u);
    }

    private void DrawGlasses(ID2D1DeviceContext dc, Vector2 left, Vector2 right, float u)
    {
        var r = 0.17f * u;
        var frame = 0x5D4037u;
        Fill(dc, new Ellipse(left, r, r * 0.92f), 0xE3F2FD, 0.25f);
        Fill(dc, new Ellipse(right, r, r * 0.92f), 0xE3F2FD, 0.25f);
        Stroke(dc, new Ellipse(left, r, r * 0.92f), frame, Math.Max(1, 0.045f * u));
        Stroke(dc, new Ellipse(right, r, r * 0.92f), frame, Math.Max(1, 0.045f * u));
        var dir = Vector2.Normalize(right - left + new Vector2(1e-3f, 0));
        Line(dc, left + dir * r, right - dir * r, frame, Math.Max(1, 0.04f * u));
        Fill(dc, new Ellipse(left - new Vector2(r * 0.35f, r * 0.35f), r * 0.18f, r * 0.12f), 0xFFFFFF, 0.7f);
        Fill(dc, new Ellipse(right - new Vector2(r * 0.35f, r * 0.35f), r * 0.18f, r * 0.12f), 0xFFFFFF, 0.7f);
    }

    /// <summary>Un palo con las puntas redondeadas.</summary>
    private void Stick(ID2D1DeviceContext dc, Vector2 from, Vector2 to, uint color, float width)
    {
        _brush.Color = ToColor(color, 1);
        dc.DrawLine(from, to, _brush, Math.Max(1, width), _round);
    }

    private void Petals(ID2D1DeviceContext dc, Vector2 center, float size, uint color, float spin)
    {
        for (var k = 0; k < 5; k++)
        {
            var a = spin + k * MathF.Tau / 5;
            Fill(dc, new Ellipse(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * size, size * 0.8f, size * 0.8f), color, 1);
        }

        Fill(dc, new Ellipse(center, size * 0.6f, size * 0.6f), 0xFFD54F, 1);
    }

    private static Vector2 Rotate(Vector2 v, float angle)
    {
        var c = MathF.Cos(angle);
        var s = MathF.Sin(angle);
        return new Vector2(c * v.X - s * v.Y, s * v.X + c * v.Y);
    }
}
