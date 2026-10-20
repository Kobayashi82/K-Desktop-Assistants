using System.Windows;
using System.Windows.Media;
using Lander.Core.Game;
using Expression = Lander.Core.Game.Expression;

namespace Lander.Assistant.Rendering.Skins;

/// <summary>
/// La ventanilla de la cabina hace de ojo: mira hacia donde va, parpadea y cambia de expresión.
/// </summary>
internal static class EyeRenderer
{
    public static void Draw(DrawingContext dc, Geometry visor, in LanderPose pose, Brush glass, Color pupil, Color lid, Pen? outline)
    {
        var bounds = visor.Bounds;
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        var size = Math.Min(bounds.Width, bounds.Height);
        var look = new Vector(pose.Look.X * bounds.Width * 0.22, pose.Look.Y * bounds.Height * 0.2);
        var pupilCenter = center + look;
        var radius = size * 0.27;
        var closed = Math.Clamp(pose.Blink, 0, 1);
        var bottomLid = 0.0;

        dc.DrawGeometry(glass, null, visor);
        dc.PushClip(visor);

        switch (pose.Expression)
        {
            case Expression.Happy:
                DrawArc(dc, pupilCenter, size * 0.32, pupil, up: true);
                closed = 0;
                break;

            case Expression.Asleep:
                closed = 1;
                break;

            case Expression.Tired:
                closed = Math.Max(closed, 0.5);
                DrawPupil(dc, pupilCenter + new Vector(0, size * 0.12), radius, pupil);
                break;

            case Expression.Scared:
                dc.DrawEllipse(Gfx.Brush(Colors.White.WithAlpha(0.9)), null, pupilCenter, radius * 1.35, radius * 1.35);
                dc.DrawEllipse(Gfx.Brush(Gfx.Lerp(pupil, Colors.Black, 0.55)), null, pupilCenter, radius * 0.55, radius * 0.55);
                break;

            case Expression.Curious:
                DrawPupil(dc, pupilCenter, radius * 1.3, pupil);
                break;

            case Expression.Focused:
                closed = Math.Max(closed, 0.3);
                bottomLid = 0.25;
                DrawPupil(dc, pupilCenter, radius * 0.85, pupil);
                break;

            default:
                DrawPupil(dc, pupilCenter, radius, pupil);
                break;
        }

        // Bostezo: los párpados casi se juntan y luego se vuelven a abrir despacio.
        if (pose.Yawn > 0.01)
        {
            closed = Math.Max(closed, Math.Clamp(pose.Yawn, 0, 1) * 0.85);
            bottomLid = Math.Max(bottomLid, Math.Clamp(pose.Yawn, 0, 1) * 0.3);
        }

        var lidBrush = Gfx.Brush(lid);
        if (closed > 0)
        {
            var height = (bounds.Height + 2) * closed;
            dc.DrawRectangle(lidBrush, null, new Rect(bounds.X - 1, bounds.Y - 1, bounds.Width + 2, height));
            if (closed >= 0.98)
            {
                // Ojo cerrado: una curva suave hacia abajo.
                DrawArc(dc, center + new Vector(0, size * 0.05), size * 0.3, Gfx.Lerp(lid, Colors.Black, 0.45), up: false);
            }
        }

        if (bottomLid > 0)
        {
            var height = bounds.Height * bottomLid;
            dc.DrawRectangle(lidBrush, null, new Rect(bounds.X - 1, bounds.Bottom - height, bounds.Width + 2, height + 1));
        }

        dc.Pop();

        if (outline is not null)
        {
            dc.DrawGeometry(null, outline, visor);
        }

        // Reflejo del cristal.
        dc.DrawEllipse(Gfx.Brush(Colors.White.WithAlpha(0.32)), null,
            new Point(bounds.X + bounds.Width * 0.3, bounds.Y + bounds.Height * 0.28), bounds.Width * 0.13, bounds.Height * 0.09);
    }

    private static void DrawPupil(DrawingContext dc, Point center, double radius, Color color)
    {
        dc.DrawEllipse(LanderParts.Glow(color, 0.7), null, center, radius * 2.1, radius * 2.1);
        dc.DrawEllipse(Gfx.Brush(color), null, center, radius, radius);
        dc.DrawEllipse(Gfx.Brush(Colors.White.WithAlpha(0.9)), null, center + new Vector(-radius * 0.35, -radius * 0.35), radius * 0.3, radius * 0.3);
    }

    private static void DrawArc(DrawingContext dc, Point center, double halfWidth, Color color, bool up)
    {
        var height = halfWidth * 0.8 * (up ? -1 : 1);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(center.X - halfWidth, center.Y - height / 2), false, false);
            context.QuadraticBezierTo(new Point(center.X, center.Y + height), new Point(center.X + halfWidth, center.Y - height / 2), true, true);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, Gfx.Pen(color, Math.Max(0.9, halfWidth * 0.42)), geometry);
    }
}
