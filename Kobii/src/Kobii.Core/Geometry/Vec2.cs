namespace Kobii.Core.Geometry;

/// <summary>
/// Vector 2D en coordenadas de pantalla (x hacia la derecha, y hacia abajo) con precisión doble.
/// </summary>
public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 Zero => default;

    public double Length => Math.Sqrt(X * X + Y * Y);

    public double LengthSquared => X * X + Y * Y;

    public Vec2 Normalized()
    {
        var length = Length;
        return length > 1e-9 ? new Vec2(X / length, Y / length) : Zero;
    }

    public Vec2 ClampLength(double max)
    {
        var length = Length;
        return length > max && length > 1e-9 ? this * (max / length) : this;
    }

    public Vec2 WithX(double x) => new(x, Y);

    public Vec2 WithY(double y) => new(X, y);

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>
    /// Vector unitario que sale por la parte superior de un cuerpo girado <paramref name="angle"/>
    /// radianes en sentido horario (0 = vertical).
    /// </summary>
    public static Vec2 UpFromAngle(double angle) => new(Math.Sin(angle), -Math.Cos(angle));

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Vec2 operator -(Vec2 v) => new(-v.X, -v.Y);

    public static Vec2 operator *(Vec2 v, double s) => new(v.X * s, v.Y * s);

    public static Vec2 operator *(double s, Vec2 v) => new(v.X * s, v.Y * s);

    public static Vec2 operator /(Vec2 v, double s) => new(v.X / s, v.Y / s);

    public override string ToString() => $"({X:0.##}, {Y:0.##})";
}
