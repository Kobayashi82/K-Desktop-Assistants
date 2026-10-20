using Lander.Core.Geometry;

namespace Lander.Core.Physics;

public enum LandingQuality
{
    Perfect,
    Hard,
    Crash,
}

public sealed record LandingResult(
    LandingQuality Quality,
    double VerticalSpeed,
    double HorizontalSpeed,
    double Angle,
    bool LegsDeployed,
    bool BothFeetOnPlatform);

public static class LandingEvaluator
{
    /// <summary>
    /// Clasifica un contacto con una plataforma. Las velocidades son relativas a la plataforma y en px/s;
    /// <paramref name="scale"/> es la escala actual del lander.
    /// </summary>
    public static LandingResult Evaluate(
        double horizontalSpeed,
        double verticalSpeed,
        double angle,
        bool legsDeployed,
        bool bothFeetOnPlatform,
        PhysicsConfig config,
        double scale)
    {
        var tilt = Math.Abs(MathUtil.WrapAngle(angle));
        var vx = Math.Abs(horizontalSpeed);
        var vy = Math.Max(0, verticalSpeed);
        var hard = config.Hard;
        var perfect = config.Perfect;

        var crash = vy > hard.VerticalSpeed * scale
            || vx > hard.HorizontalSpeed * scale
            || tilt > hard.Angle;

        // Sin patas, la panza aguanta mucho menos.
        if (!legsDeployed && vy > perfect.VerticalSpeed * scale * 1.6)
        {
            crash = true;
        }

        LandingQuality quality;
        if (crash)
        {
            quality = LandingQuality.Crash;
        }
        else if (legsDeployed && bothFeetOnPlatform
            && vy <= perfect.VerticalSpeed * scale
            && vx <= perfect.HorizontalSpeed * scale
            && tilt <= perfect.Angle)
        {
            quality = LandingQuality.Perfect;
        }
        else
        {
            quality = LandingQuality.Hard;
        }

        return new LandingResult(quality, vy, vx, tilt, legsDeployed, bothFeetOnPlatform);
    }
}
