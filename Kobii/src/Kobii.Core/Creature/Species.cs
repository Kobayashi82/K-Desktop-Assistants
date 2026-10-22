namespace Kobii.Core.Creature;

/// <summary>Colores de Kobii (RGB 0xRRGGBB): los mismos cinco coloridos que en Kobii AI.</summary>
/// <param name="Id">Identificador estable (el valor del ajuste «color»).</param>
public sealed record Palette(string Id, uint Body, uint Belly, uint Accent, uint Shell, uint Spots);

/// <summary>
/// Lo fijo de Kobii como asistente: siempre con la forma de joven (la 3 del shader), su tamaño y su paso, y los
/// coloridos que se pueden elegir en sus ajustes.
/// </summary>
public static class Species
{
    /// <summary>Forma del shader y del esqueleto: joven (1 cría … 4 adulto, 5 mayor).</summary>
    public const int Form = 3;

    /// <summary>Altura a tamaño normal, en DIP (la del joven en Kobii AI).</summary>
    public const double HeightDip = 68;

    /// <summary>Velocidad al andar, en DIP/s.</summary>
    public const double WalkSpeedDip = 80;

    /// <summary>Familia de formas en el shader (0 = Kobii).</summary>
    public const int ShapeId = 0;

    public static IReadOnlyList<Palette> Palettes { get; } =
    [
        new("mint", 0x7FD8BE, 0xF4F1DE, 0xFF8A80, 0xFFF4E0, 0x7FD8BE),
        new("sky", 0x8EC9F5, 0xF2F6FB, 0xFFB26B, 0xF4F8FF, 0x8EC9F5),
        new("peach", 0xFFB997, 0xFFF1E6, 0xE85D75, 0xFFF6EE, 0xFF9E7A),
        new("lilac", 0xC3A6F2, 0xF6F0FF, 0x7ED6C7, 0xFAF5FF, 0xB08CEB),
        new("lemon", 0xF5DC6C, 0xFFFBE6, 0x6FB7E8, 0xFFFBEA, 0xF0CF4A),
    ];

    /// <summary>El colorido con ese identificador, o el verde.</summary>
    public static Palette Palette(string? id) => Palettes.FirstOrDefault(p => p.Id == id) ?? Palettes[0];
}
