using System.Windows;
using System.Windows.Media;

namespace Lander.Assistant.Rendering.Skins;

/// <summary>El módulo de siempre: limpio, brillante y con estilo Fluent.</summary>
internal sealed class ClassicSkin : LanderSkin
{
    private static readonly Geometry DescentStage = Gfx.RoundedPolygon(2.2, -15, -5, 15, -5, 16.5, 1.5, 15, 8, -15, 8, -16.5, 1.5);
    private static readonly Geometry Bell = Gfx.RoundedPolygon(0.8, -4, 8, 4, 8, 6.3, 13, -6.3, 13);
    private static readonly Geometry Cabin = Gfx.RoundedPolygon(2.6, -12, -5, -13.5, -12, -9.5, -19.5, -4, -22, 4, -22, 9.5, -19.5, 13.5, -12, 12, -5);
    private static readonly Geometry Visor = Gfx.Frozen(new RectangleGeometry(new Rect(-5.9, -17.4, 11.8, 8.4), 3.3, 3.3));

    private static readonly Brush DescentFill = Gfx.Vertical((0, Gfx.Rgb(0xF8D57E)), (0.45, Gfx.Rgb(0xE9AE45)), (1, Gfx.Rgb(0xC98326)));
    private static readonly Brush CabinFill = Gfx.Gradient(new Point(0.2, 0), new Point(0.75, 1),
        (0, Gfx.Rgb(0xFFFFFF)), (0.55, Gfx.Rgb(0xE9EDF2)), (1, Gfx.Rgb(0xC6CED9)));
    private static readonly Brush BellFill = Gfx.Vertical((0, Gfx.Rgb(0x6B7280)), (1, Gfx.Rgb(0x2F3540)));
    private static readonly Brush Glass = Gfx.Radial((0, Gfx.Rgb(0x3A4E78)), (1, Gfx.Rgb(0x0E1526)));
    private static readonly Brush RcsFill = Gfx.Vertical((0, Gfx.Rgb(0xB8C0CB)), (1, Gfx.Rgb(0x8C95A3)));

    private static readonly Pen Outline = Gfx.Pen(0x3B4252, 0.8, 150);
    private static readonly Pen GoldOutline = Gfx.Pen(0x8A5A12, 0.8, 170);
    private static readonly Pen Highlight = Gfx.Pen(0xFFFFFF, 0.7, 95);
    private static readonly Pen Band = Gfx.Pen(0x000000, 0.8, 28);
    private static readonly Pen PanelLine = Gfx.Pen(0x94A3B8, 0.6, 140);
    private static readonly Pen AntennaPen = Gfx.Pen(0x6B7280, 0.9);

    public override string Id => "classic";

    protected override void DrawBody(DrawingContext dc, in LanderPose pose)
    {
        dc.DrawGeometry(BellFill, Outline, Bell);
        dc.DrawGeometry(DescentFill, GoldOutline, DescentStage);
        dc.DrawLine(Highlight, new Point(-12, -2.5), new Point(-6.5, 5.5));
        dc.DrawLine(Highlight, new Point(4, -2.5), new Point(8.5, 4.5));
        dc.DrawLine(Band, new Point(-15.5, 2), new Point(15.5, 2));
        LanderParts.Scorch(dc, pose.Damage, new Point(-8, 3.5), new Point(9, -1));

        dc.DrawRoundedRectangle(RcsFill, Outline, new Rect(-15.4, -15, 2.4, 4.4), 0.7, 0.7);
        dc.DrawRoundedRectangle(RcsFill, Outline, new Rect(13, -15, 2.4, 4.4), 0.7, 0.7);

        dc.DrawGeometry(CabinFill, Outline, Cabin);
        dc.DrawLine(PanelLine, new Point(-12.3, -8), new Point(12.3, -8));
        LanderParts.Scorch(dc, pose.Damage - 0.15, new Point(7.5, -10));

        dc.PushTransform(new RotateTransform(pose.AntennaSway * 180 / Math.PI, 5.5, -21.6));
        dc.DrawLine(AntennaPen, new Point(5.5, -21.6), new Point(8.5, -27));
        dc.DrawEllipse(Gfx.Brush(0xD9DEE5), Gfx.Pen(0x6B7280, 0.5), new Point(8.8, -27.4), 1.8, 1.05);
        dc.Pop();

        EyeRenderer.Draw(dc, Visor, pose, Glass, Gfx.Rgb(0x7DE3FF), Gfx.Rgb(0xE6EAF0), Gfx.Pen(0x1F2937, 0.7, 200));
        LanderParts.SweatDrop(dc, new Point(11.5, -20), pose);

        if (pose.CarryingPackage)
        {
            LanderParts.Package(dc, new Point(-3, -25.3));
        }
    }
}
