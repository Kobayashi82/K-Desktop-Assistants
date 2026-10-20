using System.Windows;
using System.Windows.Media;
using Lander.Core.Game;
using Expression = Lander.Core.Game.Expression;

namespace Lander.Assistant.Rendering.Skins;

/// <summary>Piezas comunes a varios aspectos, en unidades base (48 de alto, origen en el centro).</summary>
internal static class LanderParts
{
    [ThreadStatic]
    private static Dictionary<(uint, int), RadialGradientBrush>? _glows;

    private static readonly Color FlameCore = Gfx.Rgb(0xFFF7D6, 245);
    private static readonly Color FlameMid = Gfx.Rgb(0xFFC24D);
    private static readonly Color FlameOuter = Gfx.Rgb(0xFF7A2E, 210);
    private static readonly Color FlameTip = Gfx.Rgb(0xFF4D2E, 0);

    /// <summary>Halo radial del color dado (cacheado por opacidad cuantizada).</summary>
    public static RadialGradientBrush Glow(Color color, double alpha)
    {
        var level = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 20);
        var key = ((uint)(color.R << 16 | color.G << 8 | color.B), level);
        _glows ??= [];
        if (_glows.Count > 2000)
        {
            _glows.Clear();
        }

        if (!_glows.TryGetValue(key, out var brush))
        {
            var a = level / 20.0;
            brush = Gfx.Radial((0, color.WithAlpha(a)), (0.45, color.WithAlpha(a * 0.45)), (1, color.WithAlpha(0)));
            _glows[key] = brush;
        }

        return brush;
    }

    /// <summary>Llama del motor principal con parpadeo.</summary>
    public static void Flame(DrawingContext dc, Point nozzle, in LanderPose pose, double width = 1)
    {
        var flame = Math.Clamp(pose.MainFlame, 0, 1);
        var boost = Math.Clamp(pose.BoostFlash / 0.35, 0, 1);
        if (flame < 0.02 && boost <= 0)
        {
            return;
        }

        var t = pose.Time;
        var flicker = 1 + 0.12 * Math.Sin(t * 47) + 0.07 * Math.Sin(t * 29 + 1.3);
        var length = (5 + 21 * flame) * flicker + boost * 14;
        var w = (3.4 + 3.2 * Math.Max(flame, boost)) * width;

        dc.DrawEllipse(Glow(Gfx.Rgb(0xFFA94D), 0.3 + 0.35 * Math.Max(flame, boost)), null,
            new Point(nozzle.X, nozzle.Y + length * 0.38), w * 1.9, length * 0.65);

        var outer = Teardrop(nozzle, w, length);
        dc.DrawGeometry(Gfx.Vertical((0, FlameCore), (0.28, FlameMid), (0.65, FlameOuter), (1, FlameTip)), null, outer);

        var inner = Teardrop(nozzle, w * 0.45, length * 0.55);
        dc.DrawGeometry(Gfx.Vertical((0, Colors.White), (0.6, Gfx.Rgb(0xFFF1B8, 200)), (1, Gfx.Rgb(0xFFE08A, 0))), null, inner);
    }

    public static StreamGeometry Teardrop(Point origin, double width, double length)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(origin.X - width / 2, origin.Y), true, true);
            context.BezierTo(
                new Point(origin.X - width * 0.62, origin.Y + length * 0.42),
                new Point(origin.X - width * 0.12, origin.Y + length * 0.82),
                new Point(origin.X, origin.Y + length), true, true);
            context.BezierTo(
                new Point(origin.X + width * 0.12, origin.Y + length * 0.82),
                new Point(origin.X + width * 0.62, origin.Y + length * 0.42),
                new Point(origin.X + width / 2, origin.Y), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Dos patas con tirante y pie; se recogen hacia el cuerpo. Con <paramref name="compress"/> el cuerpo
    /// (las caderas) baja ese tanto y los amortiguadores se acortan mientras los pies siguen en el suelo.
    /// </summary>
    public static void Legs(DrawingContext dc, double deploy, Color strut, Color brace, Color pad, double hipX = 10, double hipY = 2, double compress = 0)
    {
        deploy = Math.Clamp(deploy, 0, 1);
        var strutPen = Gfx.Pen(strut, 1.8);
        var bracePen = Gfx.Pen(brace, 1.2);
        var padBrush = Gfx.Brush(pad);
        var shockPen = Gfx.Pen(Gfx.Lerp(strut, Colors.White, 0.35), 1.1);

        // Con las patas recogidas no hay suspensión que valga: todo baja junto.
        var squash = compress * deploy;
        for (var side = -1; side <= 1; side += 2)
        {
            var hip = new Point(hipX * side, hipY + squash);
            var retracted = new Point((hipX + 3.5) * side, hipY + 8 + compress);
            var deployed = new Point(18.5 * side, 22);
            var foot = retracted + (deployed - retracted) * deploy;
            var knee = hip + (foot - hip) * 0.55 + new Vector(side * Math.Max(0, squash) * 0.35, 0);
            dc.DrawLine(bracePen, new Point((hipX + 3) * side, hipY - 3.5 + squash), knee);
            dc.DrawLine(strutPen, hip, knee);
            dc.DrawLine(strutPen, knee, foot);

            // El amortiguador: un tramo más claro que se ve trabajar al posarse.
            var shock = knee + (foot - knee) * 0.35;
            dc.DrawLine(shockPen, knee, shock);
            dc.DrawEllipse(padBrush, null, new Point(foot.X, foot.Y + 0.9), 3.4, 1.15);
        }
    }

    /// <summary>Bocanada de un propulsor lateral. <paramref name="direction"/>: -1 hacia la izquierda, 1 hacia la derecha.</summary>
    public static void Jet(DrawingContext dc, Point origin, double direction, double strength)
    {
        if (strength < 0.03)
        {
            return;
        }

        var center = new Point(origin.X + direction * (1.6 + 2.6 * strength), origin.Y);
        dc.DrawEllipse(Glow(Colors.White, 0.85 * strength), null, center, 1.8 + 3 * strength, 1.3 + 1.6 * strength);
        dc.DrawEllipse(Gfx.Brush(Colors.White.WithAlpha(0.8 * strength)), null, center, 0.9 + 1.2 * strength, 0.7 + 0.6 * strength);
    }

    /// <summary>Luz de navegación con halo.</summary>
    public static void Light(DrawingContext dc, Point position, Color color, double intensity)
    {
        if (intensity <= 0.01)
        {
            return;
        }

        dc.DrawEllipse(Glow(color, 0.7 * intensity), null, position, 5, 5);
        dc.DrawEllipse(Gfx.Brush(Gfx.Lerp(color, Colors.White, 0.35 * intensity)), null, position, 1.15, 1.15);
    }

    /// <summary>Luces de posición: roja a babor, verde a estribor y un destello blanco.</summary>
    public static void NavigationLights(DrawingContext dc, Point left, Point right, Point strobe, double time)
    {
        var pulse = 0.75 + 0.25 * Math.Sin(time * 3);
        Light(dc, left, Gfx.Rgb(0xFF4D4D), pulse);
        Light(dc, right, Gfx.Rgb(0x3DFF7A), pulse);
        var phase = time % 2.2;
        Light(dc, strobe, Colors.White, phase < 0.3 ? 1 - phase / 0.3 : 0);
    }

    /// <summary>Manchas de hollín según el daño.</summary>
    public static void Scorch(DrawingContext dc, double damage, params Point[] spots)
    {
        if (damage < 0.25)
        {
            return;
        }

        var alpha = Math.Min(0.55, (damage - 0.25) / 0.75 * 0.6);
        var brush = Gfx.Brush(Gfx.Rgb(0x1F1A17).WithAlpha(alpha));
        foreach (var spot in spots)
        {
            dc.DrawEllipse(brush, null, spot, 2.8, 1.7);
        }
    }

    /// <summary>Paquete de la misión de entrega, sobre el techo.</summary>
    public static void Package(DrawingContext dc, Point center)
    {
        var rect = Gfx.Centered(center.X, center.Y, 9, 6.5);
        dc.DrawRoundedRectangle(Gfx.Vertical((0, Gfx.Rgb(0xD8A46A)), (1, Gfx.Rgb(0xB57B42))), Gfx.Pen(0x6B4423, 0.7, 200), rect, 1, 1);
        dc.DrawRectangle(Gfx.Brush(0xF3DDB0), null, new Rect(center.X - 0.9, rect.Top, 1.8, rect.Height));
    }

    /// <summary>Expresión «asustado»: gota de sudor junto a la cabina.</summary>
    public static void SweatDrop(DrawingContext dc, Point at, in LanderPose pose)
    {
        if (pose.Expression != Expression.Scared)
        {
            return;
        }

        var drop = new StreamGeometry();
        using (var context = drop.Open())
        {
            context.BeginFigure(new Point(at.X, at.Y - 2.2), true, true);
            context.BezierTo(new Point(at.X + 1.6, at.Y - 0.2), new Point(at.X + 1.4, at.Y + 1.4), new Point(at.X, at.Y + 1.4), true, true);
            context.BezierTo(new Point(at.X - 1.4, at.Y + 1.4), new Point(at.X - 1.6, at.Y - 0.2), new Point(at.X, at.Y - 2.2), true, true);
        }

        drop.Freeze();
        dc.DrawGeometry(Gfx.Brush(0x9BD7FF, 230), Gfx.Pen(0x4A90C8, 0.5, 200), drop);
    }
}
