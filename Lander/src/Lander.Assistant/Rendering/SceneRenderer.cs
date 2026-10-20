using System.Windows;
using System.Windows.Media;
using Lander.Assistant.Rendering.Skins;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Missions;
using Lander.Core.Racing;

namespace Lander.Assistant.Rendering;

/// <summary>Lo que hay que pintar en la superficie del lander en un fotograma.</summary>
internal sealed class LanderScene
{
    public Vec2 Position { get; set; }

    public double Angle { get; set; }

    public double Scale { get; set; } = 1;

    public bool Visible { get; set; } = true;

    public double Opacity { get; set; } = 1;

    public LanderPose Pose { get; set; }

    public LanderSkin Skin { get; set; } = SkinCatalog.Classic;

    public Bubble? Bubble { get; set; }

    public double BubbleAge { get; set; }

    public ImageSource? BubbleIcon { get; set; }

    /// <summary>Una tecla junto al bocadillo («CTRL+ALT+L» para aceptar una misión), o null.</summary>
    public string? BubbleHint { get; set; }

    public bool ShowHud { get; set; }

    public double Fuel { get; set; }

    public double Damage { get; set; }

    public Mission? Mission { get; set; }

    public ImageSource? MissionIcon { get; set; }

    public Race? Race { get; set; }

    public IReadOnlyList<ScorePopup> Popups { get; set; } = [];

    public bool Dark { get; set; }

    public double Time { get; set; }

    /// <summary>Borde superior del monitor donde está el lander.</summary>
    public double TopLimit { get; set; } = double.MinValue;

    /// <summary>
    /// Desplazamiento puramente visual en unidades base (saltito de alegría, vibración del motor). No
    /// cambia la física: el lander sigue donde está.
    /// </summary>
    public Vec2 Offset { get; set; }

    /// <summary>Inclinación visual añadida (saludo, meneo), en radianes.</summary>
    public double Tilt { get; set; }

    /// <summary>Superficie bajo el lander donde se proyecta su sombra, si está cerca.</summary>
    public double? ShadowY { get; set; }

    /// <summary>Intensidad de la sombra (0–1), mayor cuanto más cerca de la superficie.</summary>
    public double ShadowStrength { get; set; }

    /// <summary>Patas pedidas en modo manual (indicador del HUD).</summary>
    public bool LegsDown { get; set; } = true;

    /// <summary>Resplandor del motor sobre la superficie de debajo (0–1).</summary>
    public double GroundGlow { get; set; }

    /// <summary>Faro de aterrizaje (de noche, al acercarse a donde va a posarse), de 0 a 1.</summary>
    public double LandingLight { get; set; }

    /// <summary>Superficie que ilumina el faro.</summary>
    public double LightSurfaceY { get; set; }
}

internal static class SceneRenderer
{
    /// <summary>
    /// Dibuja la escena en píxeles de pantalla (como los pide el host); <paramref name="window"/> es el rectángulo del
    /// fotograma, para desvanecer las partículas que se acercan a su borde.
    /// </summary>
    public static void Draw(DrawingContext dc, LanderScene scene, ParticleSystem particles, Rect window)
    {
        if (scene.Visible && scene.Opacity > 0.01)
        {
            DrawGroundEffects(dc, scene);
        }

        particles.Draw(dc, window, front: false);

        if (scene.Visible && scene.Opacity > 0.01)
        {
            var offset = scene.Offset * scene.Scale;
            var cos = Math.Cos(scene.Angle);
            var sin = Math.Sin(scene.Angle);
            var position = scene.Position + new Vec2(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos);
            DrawLander(dc, scene.Skin, position, scene.Angle + scene.Tilt, scene.Scale, scene.Pose, scene.Opacity);
        }

        particles.Draw(dc, window, front: true);

        var center = new Point(scene.Position.X, scene.Position.Y);
        if (scene.Visible && scene.ShowHud)
        {
            HudRenderer.DrawGauges(dc, center, scene.Scale, scene.Fuel, scene.Damage, scene.Time, scene.LegsDown);
            if (scene.Race is { } race)
            {
                HudRenderer.DrawRace(dc, center, scene.Scale, race, scene.Dark, scene.Time);
            }
            else if (scene.Mission is { Status: MissionStatus.Active } mission)
            {
                HudRenderer.DrawMission(dc, center, scene.Scale, mission, scene.MissionIcon, scene.Dark, scene.Time);
            }
        }

        if (scene.Visible && scene.Bubble is { } bubble)
        {
            var top = new Point(center.X, center.Y - 24 * scene.Scale);
            BubbleRenderer.Draw(dc, bubble, scene.BubbleAge, top, scene.Scale, scene.Dark, scene.BubbleIcon, scene.Time, scene.TopLimit, scene.BubbleHint);
        }

        foreach (var popup in scene.Popups)
        {
            HudRenderer.DrawPopup(dc, popup, scene.Scale);
        }
    }

    /// <summary>
    /// Sombra suave sobre la superficie de debajo (más nítida cuanto más cerca) y, con el motor encendido
    /// cerca del suelo, el resplandor cálido de la llama sobre ella.
    /// </summary>
    private static void DrawGroundEffects(DrawingContext dc, LanderScene scene)
    {
        DrawLandingLight(dc, scene);
        if (scene.ShadowY is not { } surface)
        {
            return;
        }

        var scale = scene.Scale;
        var x = scene.Position.X;
        var strength = Math.Clamp(scene.ShadowStrength, 0, 1);
        if (strength > 0.02)
        {
            var spread = 1.6 - 0.6 * strength;
            var shadow = Gfx.Rgb(0x000000).WithAlpha(1);
            dc.DrawEllipse(Skins.LanderParts.Glow(shadow, (scene.Dark ? 0.32 : 0.24) * strength), null,
                new Point(x, surface + 0.5 * scale), 21 * scale * spread, 3.4 * scale * spread);
        }

        var glow = Math.Clamp(scene.GroundGlow, 0, 1);
        if (glow > 0.02)
        {
            dc.DrawEllipse(Skins.LanderParts.Glow(Gfx.Rgb(0xFFA94D), 0.55 * glow), null,
                new Point(x, surface), 30 * scale * (0.6 + 0.4 * glow), 5 * scale * (0.6 + 0.4 * glow));
        }
    }

    private static readonly Geometry LightCone = Gfx.Polygon(-0.22, 0, 0.22, 0, 1, 1, -1, 1);

    private static readonly Brush LightConeFill = Gfx.Gradient(new Point(0, 0), new Point(0, 1),
        (0, Gfx.Rgb(0xFFF4D6, 120)), (0.55, Gfx.Rgb(0xFFF1C9, 46)), (1, Gfx.Rgb(0xFFF1C9, 10)));

    /// <summary>
    /// De noche, al acercarse a donde se va a posar, un haz cálido desde la panza del lander hasta la
    /// superficie y la mancha de luz que deja en ella. El haz sigue la inclinación de la nave.
    /// </summary>
    private static void DrawLandingLight(DrawingContext dc, LanderScene scene)
    {
        var light = Math.Clamp(scene.LandingLight, 0, 1);
        if (light < 0.02)
        {
            return;
        }

        var scale = scene.Scale;
        var down = new Vec2(-Math.Sin(scene.Angle), Math.Cos(scene.Angle));
        if (down.Y < 0.4)
        {
            return;
        }

        var belly = scene.Position + down * (10 * scale);
        var length = (scene.LightSurfaceY - belly.Y) / down.Y;
        if (length < 4 * scale)
        {
            return;
        }

        var spread = 7 * scale + length * 0.42;
        var cone = Matrix.Identity;
        cone.Scale(spread, length);
        cone.Rotate(scene.Angle * 180 / Math.PI);
        cone.Translate(belly.X, belly.Y);
        dc.PushOpacity(light);
        dc.PushTransform(new MatrixTransform(cone));
        dc.DrawGeometry(LightConeFill, null, LightCone);
        dc.Pop();

        var spot = belly + down * length;
        dc.DrawEllipse(Skins.LanderParts.Glow(Gfx.Rgb(0xFFF1C9), 0.75), null, new Point(spot.X, scene.LightSurfaceY), spread * 1.15, 4.5 * scale);
        dc.Pop();
    }

    /// <summary>Dibuja el lander con su posición, giro y escala.</summary>
    public static void DrawLander(DrawingContext dc, LanderSkin skin, Vec2 position, double angle, double scale, in LanderPose pose, double opacity = 1)
    {
        var matrix = Matrix.Identity;
        matrix.Scale(scale, scale);
        matrix.Rotate(angle * 180 / Math.PI);
        matrix.Translate(position.X, position.Y);
        dc.PushTransform(new MatrixTransform(matrix));
        var faded = opacity < 0.99;
        if (faded)
        {
            dc.PushOpacity(opacity);
        }

        skin.Draw(dc, pose);

        if (faded)
        {
            dc.Pop();
        }

        dc.Pop();
    }
}
