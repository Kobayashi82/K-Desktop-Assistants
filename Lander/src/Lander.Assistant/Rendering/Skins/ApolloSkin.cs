using System.Windows;
using System.Windows.Media;

namespace Lander.Assistant.Rendering.Skins;

/// <summary>Módulo lunar de 1969: lámina dorada arrugada, facetas oscuras y antena parabólica.</summary>
internal sealed class ApolloSkin : LanderSkin
{
    private static readonly Geometry DescentStage = Gfx.Polygon(-16, -4, 16, -4, 17.5, 1.5, 16, 8.5, -16, 8.5, -17.5, 1.5);
    private static readonly Geometry Bell = Gfx.Polygon(-4.5, 8.5, 4.5, 8.5, 7, 14, -7, 14);
    private static readonly Geometry Cabin = Gfx.Polygon(-12, -4, -14.5, -9, -11.5, -15.5, -7, -20.5, 4.5, -21.5, 9.5, -17, 13.5, -11.5, 12, -4);
    private static readonly Geometry DarkFacet = Gfx.Polygon(4.5, -21.5, 9.5, -17, 13.5, -11.5, 12, -4, 5, -4, 3, -12);
    private static readonly Geometry Visor = Gfx.Polygon(-8, -17.5, 1, -17.5, -3.5, -10.5);

    private static readonly Brush Foil = Gfx.Gradient(new Point(0, 0), new Point(1, 1),
        (0, Gfx.Rgb(0xFFE08A)), (0.3, Gfx.Rgb(0xE8A93A)), (0.55, Gfx.Rgb(0xFFD36B)), (0.8, Gfx.Rgb(0xC9852A)), (1, Gfx.Rgb(0xE9B04F)));
    private static readonly Brush CabinFill = Gfx.Vertical((0, Gfx.Rgb(0xD4D8DD)), (1, Gfx.Rgb(0x8D939A)));
    private static readonly Brush FacetFill = Gfx.Vertical((0, Gfx.Rgb(0x4A5058)), (1, Gfx.Rgb(0x2A2E34)));
    private static readonly Brush BlackPanel = Gfx.Brush(0x24262B);
    private static readonly Brush BellFill = Gfx.Vertical((0, Gfx.Rgb(0x8C8F94)), (1, Gfx.Rgb(0x3E4146)));
    private static readonly Brush Glass = Gfx.Radial((0, Gfx.Rgb(0x2D3446)), (1, Gfx.Rgb(0x090B10)));

    private static readonly Pen Outline = Gfx.Pen(0x1E2126, 0.8, 190);
    private static readonly Pen Crinkle = Gfx.Pen(0xFFF3C4, 0.5, 150);
    private static readonly Pen CrinkleDark = Gfx.Pen(0x8A5A12, 0.5, 140);

    public override string Id => "apollo";

    public override Point Nozzle => new(0, 14);

    public override Color[] DebrisColors { get; } = [Gfx.Rgb(0xE8A93A), Gfx.Rgb(0xB9BEC4), Gfx.Rgb(0x2A2E34)];

    protected override void DrawLegs(DrawingContext dc, in LanderPose pose) =>
        LanderParts.Legs(dc, pose.LegsDeploy, Gfx.Rgb(0xE0B04A), Gfx.Rgb(0xA87A2A), Gfx.Rgb(0xC9CDD2), hipX: 11, hipY: 3, compress: pose.Compress);

    protected override void DrawBody(DrawingContext dc, in LanderPose pose)
    {
        dc.DrawGeometry(BellFill, Outline, Bell);
        dc.DrawGeometry(Foil, Outline, DescentStage);
        dc.DrawRectangle(BlackPanel, null, new Rect(-16, -4, 4.5, 12.5));
        dc.DrawRectangle(BlackPanel, null, new Rect(11.5, -4, 4.5, 12.5));
        dc.DrawGeometry(null, Outline, DescentStage);

        // Arrugas de la lámina.
        dc.DrawLine(Crinkle, new Point(-10, -2), new Point(-7, 3));
        dc.DrawLine(Crinkle, new Point(-7, 3), new Point(-9, 7));
        dc.DrawLine(CrinkleDark, new Point(-3, -3), new Point(0, 2));
        dc.DrawLine(Crinkle, new Point(2, 0), new Point(6, 6));
        dc.DrawLine(CrinkleDark, new Point(6, -3), new Point(9, 1));
        LanderParts.Scorch(dc, pose.Damage, new Point(-4, 4), new Point(7, 2));

        // Cuádruples de control de actitud.
        foreach (var side in new[] { -1, 1 })
        {
            var x = 15 * side;
            dc.DrawRectangle(Gfx.Brush(0x9EA3A9), Outline, Gfx.Centered(x, -11, 2.2, 2.2));
            dc.DrawLine(Gfx.Pen(0x5B6066, 0.8), new Point(x - 2.2, -11), new Point(x + 2.2, -11));
            dc.DrawLine(Gfx.Pen(0x5B6066, 0.8), new Point(x, -13.2), new Point(x, -8.8));
        }

        dc.DrawGeometry(CabinFill, Outline, Cabin);
        dc.DrawGeometry(FacetFill, null, DarkFacet);
        dc.DrawGeometry(null, Outline, Cabin);

        // Escotilla de acoplamiento y antena parabólica.
        dc.DrawRectangle(Gfx.Brush(0x3A3F46), Outline, new Rect(-3, -23.5, 5, 2.4));
        dc.PushTransform(new RotateTransform(pose.AntennaSway * 120 / Math.PI, 9, -17.5));
        dc.DrawLine(Gfx.Pen(0x6B7280, 0.8), new Point(9, -17.5), new Point(12.5, -23));
        dc.DrawEllipse(Gfx.Brush(0xE5E7EB), Gfx.Pen(0x4B5563, 0.6), new Point(12.8, -24.2), 3, 1.4);
        dc.Pop();

        EyeRenderer.Draw(dc, Visor, pose, Glass, Gfx.Rgb(0xFFC857), Gfx.Rgb(0xB9BEC4), Gfx.Pen(0x111318, 0.8));
        LanderParts.SweatDrop(dc, new Point(-12, -18), pose);

        if (pose.CarryingPackage)
        {
            LanderParts.Package(dc, new Point(-5, -25));
        }
    }

    protected override void DrawLights(DrawingContext dc, in LanderPose pose) =>
        LanderParts.NavigationLights(dc, new Point(-17.5, 1.5), new Point(17.5, 1.5), new Point(12.8, -26), pose.Time);
}
