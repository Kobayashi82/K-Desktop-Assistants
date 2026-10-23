using Kobii.Core.Geometry;

namespace Kobii.Core.Creature;

/// <summary>Lo que come y bebe (sin hambre ni sed: lo hace porque le apetece o porque se lo das).</summary>
public enum FoodKind
{
    Apple,
    Berries,
    Cookie,
    Juice,
    Milk,
    Water,
}

public static class Foods
{
    /// <summary>Se bebe (en vaso, con las dos manos y a tragos).</summary>
    public static bool IsDrink(this FoodKind kind) => kind is FoodKind.Juice or FoodKind.Milk or FoodKind.Water;

    /// <summary>Lo que más le gusta (le salen corazones).</summary>
    public static bool IsFavorite(this FoodKind kind) => kind is FoodKind.Cookie or FoodKind.Berries or FoodKind.Juice;

    /// <summary>Nombre estable (para los sucesos y el registro).</summary>
    public static string Id(this FoodKind kind) => kind.ToString().ToLowerInvariant();
}

/// <summary>Algo de comer o de beber en el escritorio o en sus manos (el de Kobii AI).</summary>
public sealed class FoodItem(FoodKind kind, CreatureBody body)
{
    /// <summary>Lo que mide (en DIP) de arriba abajo.</summary>
    public const double SizeDip = 18;

    public FoodKind Kind { get; } = kind;

    public CreatureBody Body { get; } = body;

    /// <summary>Segundos desde que se posó.</summary>
    public double RestingSeconds { get; set; }

    public bool Eaten { get; set; }

    /// <summary>Lo que queda de él mientras se lo come (1 entero, 0 acabado).</summary>
    public double Remaining { get; set; } = 1;

    /// <summary>Lo tiene en la mano (se dibuja entre sus dedos).</summary>
    public bool Held { get; set; }

    /// <summary>La mano que lo sujeta (−1 o 1).</summary>
    public double HeldSide { get; set; } = 1;

    /// <summary>Lo sujeta con las dos manos.</summary>
    public bool TwoHands { get; set; }

    /// <summary>Dónde está su centro mientras lo tiene en la mano (en el espacio de la criatura).</summary>
    public Vec2 HeldLocal { get; set; }

    /// <summary>Lo mismo en el paso anterior (para dibujar entre dos pasos).</summary>
    public Vec2 PreviousHeldLocal { get; set; }

    /// <summary>Inclinación al beber (radianes).</summary>
    public double Tilt { get; set; }

    /// <summary>Mordiscos que le ha dado (se ven en la comida).</summary>
    public int Bites { get; set; }

    /// <summary>Lado por el que muerde (hacia su boca).</summary>
    public double BiteSide { get; set; } = -1;

    /// <summary>Lo ha sacado él de detrás de la espalda (no se lo has dado tú).</summary>
    public bool Own { get; init; }
}
