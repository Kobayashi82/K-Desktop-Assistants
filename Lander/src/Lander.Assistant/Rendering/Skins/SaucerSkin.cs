using System.Windows;
using System.Windows.Media;
using Lander.Core.Game;
using Expression = Lander.Core.Game.Expression;

namespace Lander.Assistant.Rendering.Skins;

/// <summary>Platillo volante con su pequeño piloto dentro de la cúpula. En vez de llama, un rayo tractor.</summary>
internal sealed class SaucerSkin : LanderSkin
{
    private static readonly Geometry Dome = CreateDome();
    private static readonly Geometry AlienEye = Gfx.Frozen(new EllipseGeometry(new Point(0, -6), 3.3, 3));

    private static readonly Brush DiscFill = Gfx.Vertical((0, Gfx.Rgb(0xF1F5F9)), (0.5, Gfx.Rgb(0xC3CCD8)), (1, Gfx.Rgb(0x5E6B7D)));
    private static readonly Brush Underside = Gfx.Vertical((0, Gfx.Rgb(0x4B5565)), (1, Gfx.Rgb(0x262B33)));
    private static readonly Brush DomeGlass = Gfx.Radial((0, Gfx.Rgb(0xC9F7FF, 90)), (1, Gfx.Rgb(0x67E8F9, 70)));
    private static readonly Brush AlienSkin = Gfx.Vertical((0, Gfx.Rgb(0xA7F3B5)), (1, Gfx.Rgb(0x5FCB78)));
    private static readonly Brush Sclera = Gfx.Brush(0xFFFFFF);
    private static readonly Pen Outline = Gfx.Pen(0x334155, 0.8, 170);
    private static readonly Color[] RimColors = [Gfx.Rgb(0xFFD84D), Gfx.Rgb(0x5CF2FF), Gfx.Rgb(0xFF6BD6)];

    public override string Id => "saucer";

    public override Point Nozzle => new(0, 9);

    public override Point LeftJetPoint => new(-20.5, 4);

    public override Point RightJetPoint => new(20.5, 4);

    public override Point LowerLeftJetPoint => new(-17.5, 7.5);

    public override Point LowerRightJetPoint => new(17.5, 7.5);

    public override Color[] DebrisColors { get; } = [Gfx.Rgb(0xC3CCD8), Gfx.Rgb(0x67E8F9), Gfx.Rgb(0x7DDC8F)];

    protected override void DrawExhaust(DrawingContext dc, in LanderPose pose)
    {
        var strength = Math.Max(pose.MainFlame, pose.BoostFlash / 0.35);
        if (strength < 0.02)
        {
            return;
        }

        var length = 8 + 20 * strength;
        var spread = 4 + 6 * strength;
        var beam = Gfx.Polygon(-4.5, 8.5, 4.5, 8.5, spread + 4, 8.5 + length, -spread - 4, 8.5 + length);
        var shimmer = 0.75 + 0.25 * Math.Sin(pose.Time * 18);
        dc.DrawGeometry(Gfx.Vertical((0, Gfx.Rgb(0xB8FBFF).WithAlpha(0.75 * shimmer)), (1, Gfx.Rgb(0x5CF2FF).WithAlpha(0))), null, beam);
    }

    protected override void DrawLegs(DrawingContext dc, in LanderPose pose) =>
        LanderParts.Legs(dc, pose.LegsDeploy, Gfx.Rgb(0xA8B3C2), Gfx.Rgb(0x7C8899), Gfx.Rgb(0x475569), hipX: 9, hipY: 7, compress: pose.Compress);

    protected override void DrawBody(DrawingContext dc, in LanderPose pose)
    {
        dc.DrawEllipse(Underside, Outline, new Point(0, 7.5), 12.5, 3);

        // Piloto: cabeza verde con un ojo grande y antenas.
        dc.DrawLine(Gfx.Pen(0x4FAF67, 0.8), new Point(-2.5, -10), new Point(-4.5, -14));
        dc.DrawLine(Gfx.Pen(0x4FAF67, 0.8), new Point(2.5, -10), new Point(4.5, -14));
        dc.DrawEllipse(Gfx.Brush(0xA7F3B5), null, new Point(-4.6, -14.3), 1.1, 1.1);
        dc.DrawEllipse(Gfx.Brush(0xA7F3B5), null, new Point(4.6, -14.3), 1.1, 1.1);
        dc.DrawEllipse(AlienSkin, Gfx.Pen(0x3E9356, 0.6), new Point(0, -5), 6.3, 6.8);
        EyeRenderer.Draw(dc, AlienEye, pose, Sclera, Gfx.Rgb(0x1E293B), Gfx.Rgb(0x8EE6A1), Gfx.Pen(0x2E7D47, 0.5));

        dc.DrawGeometry(DomeGlass, Gfx.Pen(0x9BE7F2, 0.7, 200), Dome);
        dc.DrawEllipse(Gfx.Brush(Colors.White.WithAlpha(0.45)), null, new Point(-4.5, -9), 1.8, 3.2);

        dc.DrawEllipse(DiscFill, Outline, new Point(0, 4), 20, 5.6);
        dc.DrawEllipse(null, Gfx.Pen(0xFFFFFF, 0.6, 110), new Point(0, 3.3), 16, 3.4);
        LanderParts.Scorch(dc, pose.Damage, new Point(-10, 5), new Point(9, 4));

        // Luces del borde que se van encendiendo en secuencia.
        var night = pose.Night ? 1 : 0.6;
        for (var i = 0; i < 7; i++)
        {
            var x = -15 + i * 5;
            var y = 5.5 - Math.Abs(x) * 0.05;
            var phase = (pose.Time * 3 + i * 0.45) % 3;
            var color = RimColors[(int)phase % RimColors.Length];
            dc.DrawEllipse(LanderParts.Glow(color, 0.5 * night), null, new Point(x, y), 2.6, 2.6);
            dc.DrawEllipse(Gfx.Brush(color), null, new Point(x, y), 0.9, 0.9);
        }

        LanderParts.SweatDrop(dc, new Point(8.5, -12), pose with { Expression = pose.Expression == Expression.Scared ? Expression.Scared : Expression.Neutral });

        if (pose.CarryingPackage)
        {
            LanderParts.Package(dc, new Point(13, -1));
        }
    }

    protected override void DrawLights(DrawingContext dc, in LanderPose pose)
    {
        // Las luces del borde ya hacen de luces de navegación.
    }

    private static StreamGeometry CreateDome()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(-10.5, 2.5), true, true);
            context.BezierTo(new Point(-10.5, -9), new Point(-6, -15.5), new Point(0, -15.5), true, true);
            context.BezierTo(new Point(6, -15.5), new Point(10.5, -9), new Point(10.5, 2.5), true, true);
        }

        geometry.Freeze();
        return geometry;
    }
}
