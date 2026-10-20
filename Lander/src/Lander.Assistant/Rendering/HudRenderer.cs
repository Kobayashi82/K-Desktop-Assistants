using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Lander.Core.Game;
using Lander.Core.Missions;
using Lander.Core.Racing;

namespace Lander.Assistant.Rendering;

/// <summary>
/// Indicadores del modo manual: combustible y casco junto al lander, la misión en curso y los puntos.
/// </summary>
internal static class HudRenderer
{
    private static readonly Typeface Bold = new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    /// <param name="center">Centro del lander en el mundo (sin rotación).</param>
    public static void DrawGauges(DrawingContext dc, Point center, double scale, double fuel, double damage, double time, bool legsDown = true)
    {
        // Patas (G): verdes desplegadas; recogidas, ámbar y parpadeando cerca del suelo no es buena idea.
        var legsColor = legsDown ? Gfx.Rgb(0x2FBF71) : Gfx.Rgb(0xF5A524);
        var legsPen = Gfx.Pen(legsColor, 1.1 * scale);
        var legsAt = new Point(center.X + 27.5 * scale, center.Y + 21 * scale);
        var spread = (legsDown ? 3.2 : 1.4) * scale;
        dc.DrawLine(legsPen, new Point(legsAt.X - 1 * scale, legsAt.Y - 3 * scale), new Point(legsAt.X - spread, legsAt.Y));
        dc.DrawLine(legsPen, new Point(legsAt.X + 1 * scale, legsAt.Y - 3 * scale), new Point(legsAt.X + spread, legsAt.Y));

        var height = 34 * scale;
        var top = center.Y - height / 2;

        // Combustible
        var fuelRect = new Rect(center.X + 25 * scale, top, 5 * scale, height);
        var fuelColor = fuel > 0.5 ? Gfx.Rgb(0x2FBF71) : fuel > 0.2 ? Gfx.Rgb(0xF5A524) : Gfx.Rgb(0xEF4444);
        if (fuel < 0.15 && time % 0.6 < 0.3)
        {
            fuelColor = fuelColor.WithAlpha(0.45);
        }

        DrawBar(dc, fuelRect, fuel, fuelColor, scale);

        // Casco (solo si hay daños)
        if (damage > 0.05)
        {
            var hullRect = new Rect(center.X + 31.5 * scale, top + height * 0.25, 3 * scale, height * 0.75);
            DrawBar(dc, hullRect, 1 - damage, damage > 0.6 ? Gfx.Rgb(0xEF4444) : Gfx.Rgb(0x60A5FA), scale);
        }
    }

    /// <summary>Insignia de la misión activa: icono, tiempo restante en un anillo y progreso.</summary>
    public static void DrawMission(DrawingContext dc, Point center, double scale, Mission mission, ImageSource? appIcon, bool dark, double time)
    {
        var radius = 10 * scale;
        var at = new Point(center.X - 34 * scale, center.Y - 4 * scale);
        var fill = dark ? Gfx.Rgb(0x2B2B2B, 235) : Gfx.Rgb(0xFFFFFF, 240);
        dc.DrawEllipse(Gfx.Brush(Colors.Black.WithAlpha(0.18)), null, new Point(at.X, at.Y + 1.2 * scale), radius + scale, radius + scale);
        dc.DrawEllipse(Gfx.Brush(fill), null, at, radius, radius);

        var remaining = Math.Clamp(mission.TimeLeft / Math.Max(1, mission.Duration), 0, 1);
        var ringColor = remaining > 0.25 ? Icons.Accent(dark) : Gfx.Rgb(0xEF4444);
        DrawArc(dc, at, radius - 1.2 * scale, remaining, Gfx.Pen(ringColor, 1.8 * scale));

        dc.PushTransform(new MatrixTransform(0.78 * scale, 0, 0, 0.78 * scale, at.X, at.Y));
        Icons.Draw(dc, mission.Icon, 0, appIcon, time, dark);
        dc.Pop();

        if (mission.Kind == MissionKind.CollectCrystals && mission.TotalCrystals > 0)
        {
            var collected = mission.TotalCrystals - mission.Crystals.Count;
            for (var i = 0; i < mission.TotalCrystals; i++)
            {
                var dot = new Point(at.X + (i - (mission.TotalCrystals - 1) / 2.0) * 4.5 * scale, at.Y + radius + 4 * scale);
                dc.DrawEllipse(Gfx.Brush(i < collected ? Gfx.Rgb(0x34D3E0) : Gfx.Rgb(0x94A3B8, 160)), null, dot, 1.5 * scale, 1.5 * scale);
            }
        }
        else if (mission.Kind == MissionKind.RoundTrip)
        {
            for (var i = 0; i < 3; i++)
            {
                var dot = new Point(at.X + (i - 1) * 4.5 * scale, at.Y + radius + 4 * scale);
                dc.DrawEllipse(Gfx.Brush(i < mission.Stage ? Icons.Accent(dark) : Gfx.Rgb(0x94A3B8, 160)), null, dot, 1.5 * scale, 1.5 * scale);
            }
        }
    }

    /// <summary>
    /// Insignia de la carrera: bandera (o la medalla al terminar), balizas alcanzadas en el anillo y el
    /// cronómetro debajo.
    /// </summary>
    public static void DrawRace(DrawingContext dc, Point center, double scale, Race race, bool dark, double time)
    {
        var radius = 10 * scale;
        var at = new Point(center.X - 34 * scale, center.Y - 4 * scale);
        var fill = dark ? Gfx.Rgb(0x2B2B2B, 235) : Gfx.Rgb(0xFFFFFF, 240);
        var fade = race.IsActive ? 1 : Math.Clamp((RaceSystem.ResultDuration - race.ResultAge) / 0.5, 0, 1);
        if (fade <= 0.01)
        {
            return;
        }

        dc.PushOpacity(fade);
        dc.DrawEllipse(Gfx.Brush(Colors.Black.WithAlpha(0.18)), null, new Point(at.X, at.Y + 1.2 * scale), radius + scale, radius + scale);
        dc.DrawEllipse(Gfx.Brush(fill), null, at, radius, radius);

        var gold = Gfx.Rgb(0xFFD23F);
        var ringColor = race.Status switch
        {
            RaceStatus.Finished => gold,
            RaceStatus.Failed => Gfx.Rgb(0xEF4444),
            _ => Icons.Accent(dark),
        };
        DrawArc(dc, at, radius - 1.2 * scale, race.Status == RaceStatus.Finished ? 1 : race.Progress, Gfx.Pen(ringColor, 1.8 * scale));

        var icon = race.Medal switch
        {
            RaceMedal.Gold => BubbleIcon.MedalGold,
            RaceMedal.Silver => BubbleIcon.MedalSilver,
            RaceMedal.Bronze => BubbleIcon.MedalBronze,
            _ => BubbleIcon.Flag,
        };
        dc.PushTransform(new MatrixTransform(0.78 * scale, 0, 0, 0.78 * scale, at.X, at.Y));
        Icons.Draw(dc, icon, 0, null, time, dark);
        dc.Pop();

        // Cronómetro: parpadea mientras espera el despegue.
        var color = race.Status switch
        {
            RaceStatus.Finished => gold,
            RaceStatus.Failed => Gfx.Rgb(0xFF6B6B),
            _ => Colors.White,
        };
        var blink = race.Status == RaceStatus.Ready && time % 1 > 0.6 ? 0.35 : 1;
        var text = new FormattedText(Race.FormatTime(race.Elapsed), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Bold, 10.5 * scale, Gfx.Brush(color), 1);
        // Centrado bajo la insignia; si es largo, crece hacia la izquierda para no pisar el lander.
        var origin = new Point(Math.Min(at.X - text.Width / 2, at.X + radius - text.Width), at.Y + radius + 2 * scale);
        var geometry = text.BuildGeometry(origin);
        dc.PushOpacity(blink);
        dc.DrawGeometry(null, Gfx.Pen(Colors.Black.WithAlpha(0.6), 2.4 * scale), geometry);
        dc.DrawGeometry(Gfx.Brush(color), null, geometry);
        dc.Pop();
        dc.Pop();
    }

    /// <summary>Puntos flotantes que suben y se desvanecen.</summary>
    public static void DrawPopup(DrawingContext dc, ScorePopup popup, double scale)
    {
        var t = popup.Age / ScorePopup.Lifetime;
        var alpha = t < 0.7 ? 1 : 1 - (t - 0.7) / 0.3;
        var pop = 1 + 0.35 * Math.Max(0, 1 - popup.Age / 0.15);
        var color = popup.Style switch
        {
            PopupStyle.Perfect => Gfx.Rgb(0xFFD23F),
            PopupStyle.Bonus => Gfx.Rgb(0x5CE1E6),
            PopupStyle.Penalty => Gfx.Rgb(0xFF6B6B),
            _ => Colors.White,
        };

        var text = new FormattedText(popup.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Bold, 13 * scale * pop, Gfx.Brush(color), 1);
        var origin = new Point(popup.Position.X - text.Width / 2, popup.Position.Y - text.Height / 2 - popup.Age * 22 * scale);
        var geometry = text.BuildGeometry(origin);
        dc.PushOpacity(alpha);
        dc.DrawGeometry(null, Gfx.Pen(Colors.Black.WithAlpha(0.55), 2.6 * scale), geometry);
        dc.DrawGeometry(Gfx.Brush(color), null, geometry);
        dc.Pop();
    }

    private static void DrawBar(DrawingContext dc, Rect rect, double fraction, Color color, double scale)
    {
        var radius = rect.Width / 2;
        dc.DrawRoundedRectangle(Gfx.Brush(Colors.Black.WithAlpha(0.38)), Gfx.Pen(Colors.White.WithAlpha(0.6), 0.8 * scale), rect, radius, radius);
        var inner = rect;
        inner.Inflate(-scale, -scale);
        var filled = Math.Clamp(fraction, 0, 1) * inner.Height;
        if (filled > 0.5)
        {
            var fillRect = new Rect(inner.X, inner.Bottom - filled, inner.Width, filled);
            dc.DrawRoundedRectangle(Gfx.Brush(color), null, fillRect, inner.Width / 2, Math.Min(inner.Width / 2, filled / 2));
        }
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius, double fraction, Pen pen)
    {
        if (fraction <= 0.001)
        {
            return;
        }

        if (fraction >= 0.999)
        {
            dc.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var start = new Point(center.X, center.Y - radius);
        var angle = -Math.PI / 2 + fraction * Math.Tau;
        var end = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
