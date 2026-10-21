using System.Windows;
using System.Windows.Media;

namespace Lander.Assistant.Rendering.Skins;

/// <summary>Cápsula moderna: blanca, cónica, con escudo térmico y ventanilla panorámica.</summary>
internal sealed class CapsuleSkin : LanderSkin
{
    private static readonly Geometry Body = Gfx.RoundedPolygon(3, -13, 8, 13, 8, 8.5, -15, 0, -19.5, -8.5, -15);
    private static readonly Geometry HeatShield = Gfx.RoundedPolygon(1.2, -13.5, 7.5, 13.5, 7.5, 12, 11.5, -12, 11.5);
    private static readonly Geometry Visor = Gfx.Frozen(new EllipseGeometry(new Point(0, -6.5), 5.6, 5.6));

    private static readonly Brush BodyFill = Gfx.Gradient(new Point(0.15, 0), new Point(0.85, 1),
        (0, Gfx.Rgb(0xFFFFFF)), (0.6, Gfx.Rgb(0xEEF2F6)), (1, Gfx.Rgb(0xCBD3DD)));
    private static readonly Brush ShieldFill = Gfx.Vertical((0, Gfx.Rgb(0x3A3F49)), (1, Gfx.Rgb(0x15181D)));
    private static readonly Brush Glass = Gfx.Radial((0, Gfx.Rgb(0x1E3A5F)), (1, Gfx.Rgb(0x070D18)));
    private static readonly Brush Accent = Gfx.Brush(0x2F6FEB);
    private static readonly Pen Outline = Gfx.Pen(0x334155, 0.8, 160);

    public override string Id => "capsule";

    public override Point Nozzle => new(0, 11.5);

    public override Point LeftJetPoint => new(-11.5, -9);

    public override Point RightJetPoint => new(11.5, -9);

    public override Point LowerLeftJetPoint => new(-13.6, 4.5);

    public override Point LowerRightJetPoint => new(13.6, 4.5);

    public override Color[] DebrisColors { get; } = [Gfx.Rgb(0xF1F5F9), Gfx.Rgb(0x2F6FEB), Gfx.Rgb(0x1F2329)];

    protected override void DrawExhaust(DrawingContext dc, in LanderPose pose)
    {
        // Tres toberas pequeñas en lugar de una grande.
        LanderParts.Flame(dc, new Point(-5, 11.5), pose with { MainFlame = pose.MainFlame * 0.8 }, 0.55);
        LanderParts.Flame(dc, new Point(5, 11.5), pose with { MainFlame = pose.MainFlame * 0.8 }, 0.55);
        LanderParts.Flame(dc, Nozzle, pose, 0.7);
    }

    protected override void DrawLegs(DrawingContext dc, in LanderPose pose) =>
        LanderParts.Legs(dc, pose.LegsDeploy, Gfx.Rgb(0xCBD3DD), Gfx.Rgb(0x8C96A5), Gfx.Rgb(0x3A3F49), hipX: 9.5, hipY: 8, compress: pose.Compress);

    protected override void DrawBody(DrawingContext dc, in LanderPose pose)
    {
        dc.DrawGeometry(BodyFill, Outline, Body);
        dc.PushClip(Body);
        dc.DrawRectangle(Accent, null, new Rect(-15, 1, 30, 2.4));
        dc.Pop();
        dc.DrawGeometry(ShieldFill, Outline, HeatShield);

        foreach (var side in new[] { -1, 1 })
        {
            dc.DrawRoundedRectangle(Gfx.Brush(0x2B3038), null, Gfx.Centered(9.6 * side, -9, 2, 3.4), 0.6, 0.6);
        }

        LanderParts.Scorch(dc, pose.Damage, new Point(-6, 4), new Point(7, -12));

        dc.DrawEllipse(Gfx.Brush(0xDDE3EA), Outline, new Point(0, -6.5), 6.8, 6.8);
        EyeRenderer.Draw(dc, Visor, pose, Glass, Gfx.Rgb(0x8FD3FF), Gfx.Rgb(0xEEF2F6), Gfx.Pen(0x0F172A, 0.7, 220));
        LanderParts.SweatDrop(dc, new Point(9, -15), pose);

        if (pose.CarryingPackage)
        {
            LanderParts.Package(dc, new Point(0, -22.5));
        }
    }

    protected internal override Point SignalPoint => new(0, -20.5);

    protected override (Point Pivot, double Factor) SignalSway => (default, 0);

    protected override void DrawLights(DrawingContext dc, in LanderPose pose) =>
        LanderParts.NavigationLights(dc, new Point(-13.5, 5.5), new Point(13.5, 5.5), new Point(0, -20.5), pose.Time);
}
