namespace Lander.Core.Geometry;

/// <summary>
/// Rectángulo en píxeles físicos de pantalla. <see cref="Right"/> y <see cref="Bottom"/> son exclusivos,
/// igual que en Win32.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public Vec2 Center => new((Left + Right) / 2.0, (Top + Bottom) / 2.0);

    public static PixelRect FromSize(int x, int y, int width, int height) => new(x, y, x + width, y + height);

    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public bool Contains(Vec2 point) => Contains(point.X, point.Y);

    public PixelRect Intersect(PixelRect other)
    {
        var result = new PixelRect(
            Math.Max(Left, other.Left),
            Math.Max(Top, other.Top),
            Math.Min(Right, other.Right),
            Math.Min(Bottom, other.Bottom));
        return result.IsEmpty ? default : result;
    }

    public bool Intersects(PixelRect other) => !Intersect(other).IsEmpty;

    /// <summary>Punto del rectángulo más cercano a <paramref name="point"/>.</summary>
    public Vec2 ClosestPoint(Vec2 point) => new(
        Math.Clamp(point.X, Left, Math.Max(Left, Right - 1)),
        Math.Clamp(point.Y, Top, Math.Max(Top, Bottom - 1)));

    public double DistanceSquaredTo(Vec2 point) => (ClosestPoint(point) - point).LengthSquared;

    public override string ToString() => $"[{Left},{Top} → {Right},{Bottom}]";
}
