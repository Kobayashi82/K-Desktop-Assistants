using System.Windows;
using System.Windows.Media;
using Lander.Core.Progression;

namespace Lander.Assistant.Rendering.Skins;

/// <summary>
/// Aspecto del lander. Se dibuja en unidades base: 48 de alto, origen en el centro de masas, y hacia
/// abajo, las patas apoyan en y = 24.
/// </summary>
internal abstract class LanderSkin
{
    public abstract string Id { get; }

    /// <summary>Boca del motor principal (de ahí sale el humo).</summary>
    public virtual Point Nozzle => new(0, 13);

    public virtual Point LeftJetPoint => new(-15.5, -12.8);

    public virtual Point RightJetPoint => new(15.5, -12.8);

    /// <summary>Propulsores de abajo: con los de arriba, en diagonal, hacen girar la nave.</summary>
    public virtual Point LowerLeftJetPoint => new(-16.8, 4.5);

    public virtual Point LowerRightJetPoint => new(16.8, 4.5);

    /// <summary>Colores de los trozos al estallar.</summary>
    public virtual Color[] DebrisColors { get; } = [Gfx.Rgb(0xE9AE45), Gfx.Rgb(0xE8ECF1), Gfx.Rgb(0x6B7280)];

    public void Draw(DrawingContext dc, in LanderPose pose)
    {
        // Con la suspensión hundida (o estirada) el cuerpo se desplaza y las patas se doblan; los pies se
        // quedan donde están.
        var compress = pose.Compress;
        var shifted = Math.Abs(compress) > 0.01;
        if (shifted)
        {
            dc.PushTransform(new TranslateTransform(0, compress));
        }

        DrawExhaust(dc, pose);
        if (shifted)
        {
            dc.Pop();
        }

        DrawLegs(dc, pose);
        if (shifted)
        {
            dc.PushTransform(new TranslateTransform(0, compress));
        }

        DrawBody(dc, pose);
        if (pose.Signal > 0.01)
        {
            DrawSignal(dc, pose);
        }

        LanderParts.Jet(dc, LeftJetPoint, -1, pose.LeftJet);
        LanderParts.Jet(dc, RightJetPoint, 1, pose.RightJet);
        LanderParts.Jet(dc, LowerLeftJetPoint, -1, pose.LowerLeftJet);
        LanderParts.Jet(dc, LowerRightJetPoint, 1, pose.LowerRightJet);
        if (pose.Night)
        {
            DrawLights(dc, pose);
        }

        if (shifted)
        {
            dc.Pop();
        }
    }

    protected virtual void DrawExhaust(DrawingContext dc, in LanderPose pose) => LanderParts.Flame(dc, Nozzle, pose);

    protected virtual void DrawLegs(DrawingContext dc, in LanderPose pose) =>
        LanderParts.Legs(dc, pose.LegsDeploy, Gfx.Rgb(0xA3ACB9), Gfx.Rgb(0x7B8494), Gfx.Rgb(0x5B6472), compress: pose.Compress);

    protected abstract void DrawBody(DrawingContext dc, in LanderPose pose);

    protected virtual void DrawLights(DrawingContext dc, in LanderPose pose) =>
        LanderParts.NavigationLights(dc, new Point(-16.5, 1.5), new Point(16.5, 1.5), new Point(8.8, -28.6), pose.Time);

    /// <summary>Dónde se enciende la luz de señal: la punta de la antena.</summary>
    protected internal virtual Point SignalPoint => new(8.8, -27.4);

    /// <summary>Sobre qué punto se mece la antena y cuánto (1 = lo que dice la inercia; 0, no se mece).</summary>
    protected virtual (Point Pivot, double Factor) SignalSway => (new Point(5.5, -21.6), 1);

    /// <summary>La luz de la antena (escuchando, pensando, hablando), meciéndose con ella.</summary>
    protected virtual void DrawSignal(DrawingContext dc, in LanderPose pose)
    {
        var (pivot, factor) = SignalSway;
        var sways = factor != 0 && pose.AntennaSway != 0;
        if (sways)
        {
            dc.PushTransform(new RotateTransform(pose.AntennaSway * factor * 180 / Math.PI, pivot.X, pivot.Y));
        }

        LanderParts.Light(dc, SignalPoint, pose.SignalColor, pose.Signal);
        if (sways)
        {
            dc.Pop();
        }
    }
}

public static class SkinCatalog
{
    internal static LanderSkin Get(string? id) => id switch
    {
        "apollo" => Apollo,
        "capsule" => Capsule,
        "saucer" => Saucer,
        "pixel" => Pixel,
        _ => Classic,
    };

    internal static LanderSkin Classic { get; } = new ClassicSkin();

    internal static LanderSkin Apollo { get; } = new ApolloSkin();

    internal static LanderSkin Capsule { get; } = new CapsuleSkin();

    internal static LanderSkin Saucer { get; } = new SaucerSkin();

    internal static LanderSkin Pixel { get; } = new PixelSkin();

    public static IEnumerable<string> Ids => Unlocks.OfKind(UnlockKind.Skin).Select(u => u.Id);
}
