namespace Kobii.Core.Geometry;

public static class MathUtil
{
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static double InverseLerp(double a, double b, double value) =>
        Math.Abs(b - a) < 1e-12 ? 0 : Math.Clamp((value - a) / (b - a), 0, 1);

    public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    public static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

    /// <summary>Normaliza un ángulo al rango (-π, π].</summary>
    public static double WrapAngle(double angle)
    {
        angle %= Math.Tau;
        if (angle <= -Math.PI)
        {
            angle += Math.Tau;
        }
        else if (angle > Math.PI)
        {
            angle -= Math.Tau;
        }

        return angle;
    }

    /// <summary>Acerca <paramref name="current"/> a <paramref name="target"/> como máximo <paramref name="maxDelta"/>.</summary>
    public static double MoveTowards(double current, double target, double maxDelta)
    {
        if (Math.Abs(target - current) <= maxDelta)
        {
            return target;
        }

        return current + Math.Sign(target - current) * maxDelta;
    }

    /// <summary>
    /// Suavizado exponencial independiente de la tasa de refresco: tras 1/<paramref name="rate"/>
    /// segundos se ha recorrido ~63 % de la distancia.
    /// </summary>
    public static double Damp(double current, double target, double rate, double dt) =>
        target + (current - target) * Math.Exp(-rate * dt);

    public static Vec2 Damp(Vec2 current, Vec2 target, double rate, double dt) =>
        target + (current - target) * Math.Exp(-rate * dt);

    /// <summary>
    /// Velocidad máxima con la que se puede recorrer <paramref name="distance"/> y frenar a tiempo
    /// con una deceleración <paramref name="deceleration"/>.
    /// </summary>
    public static double BrakingSpeed(double distance, double deceleration) =>
        Math.Sqrt(2 * deceleration * Math.Abs(distance));
}
