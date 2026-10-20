using Lander.Core.Physics;

namespace Lander.Core.Progression;

public enum UnlockKind
{
    Skin,
    Trail,
    Upgrade,
}

public sealed record Unlock(int Level, UnlockKind Kind, string Id, string Name, string Description);

/// <summary>Aspectos, estelas y mejoras que se desbloquean al subir de nivel.</summary>
public static class Unlocks
{
    public const string DefaultSkin = "classic";
    public const string DefaultTrail = "white";

    public const string TankUpgrade = "tank";
    public const string EngineUpgrade = "engine";
    public const string LegsUpgrade = "legs";

    public static IReadOnlyList<Unlock> All { get; } =
    [
        new(1, UnlockKind.Skin, DefaultSkin, "Clásico", "El módulo de siempre, limpio y brillante."),
        new(1, UnlockKind.Trail, DefaultTrail, "Humo blanco", "La estela de serie."),
        new(2, UnlockKind.Trail, "blue", "Estela azul", "Humo con un toque de hielo."),
        new(3, UnlockKind.Upgrade, TankUpgrade, "Depósito ampliado", "Un 30 % más de combustible."),
        new(4, UnlockKind.Skin, "apollo", "Apolo retro", "Lámina dorada y antenas de 1969."),
        new(5, UnlockKind.Upgrade, EngineUpgrade, "Motor eficiente", "Gasta un 20 % menos de combustible."),
        new(6, UnlockKind.Trail, "rainbow", "Estela arcoíris", "Porque sí."),
        new(7, UnlockKind.Skin, "capsule", "Cápsula moderna", "Blanca, lisa y con ventanilla panorámica."),
        new(8, UnlockKind.Upgrade, LegsUpgrade, "Patas amortiguadas", "Aguantan aterrizajes un 30 % más bruscos."),
        new(9, UnlockKind.Trail, "plasma", "Estela de plasma", "Violeta eléctrico."),
        new(10, UnlockKind.Skin, "saucer", "Platillo volante", "Visitante de otro escritorio."),
        new(12, UnlockKind.Skin, "pixel", "Pixel art", "Directo de una recreativa de los 80."),
        new(14, UnlockKind.Trail, "gold", "Estela dorada", "Para pilotos de élite."),
    ];

    public static Unlock? Find(string id) => All.FirstOrDefault(u => u.Id == id);

    public static bool IsUnlocked(string id, int level) => Find(id) is { } unlock && unlock.Level <= level;

    /// <summary>Lo que se desbloquea al pasar de <paramref name="fromLevel"/> a <paramref name="toLevel"/>.</summary>
    public static IReadOnlyList<Unlock> Between(int fromLevel, int toLevel) =>
        All.Where(u => u.Level > fromLevel && u.Level <= toLevel).ToList();

    public static IEnumerable<Unlock> OfKind(UnlockKind kind) => All.Where(u => u.Kind == kind);

    /// <summary>Aplica a la física las mejoras desbloqueadas por el nivel.</summary>
    public static PhysicsConfig ApplyUpgrades(PhysicsConfig config, int level)
    {
        if (IsUnlocked(TankUpgrade, level))
        {
            config = config with { FuelCapacity = config.FuelCapacity * 1.3 };
        }

        if (IsUnlocked(EngineUpgrade, level))
        {
            config = config with
            {
                MainBurnRate = config.MainBurnRate * 0.8,
                LateralBurnRate = config.LateralBurnRate * 0.8,
                RotationBurnRate = config.RotationBurnRate * 0.8,
                BoostFuelCost = config.BoostFuelCost * 0.8,
            };
        }

        if (IsUnlocked(LegsUpgrade, level))
        {
            config = config with { Perfect = config.Perfect.Scaled(1.3), Hard = config.Hard.Scaled(1.3) };
        }

        return config;
    }
}
