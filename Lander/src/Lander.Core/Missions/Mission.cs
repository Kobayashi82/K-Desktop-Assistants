using Lander.Core.Game;
using Lander.Core.Geometry;

namespace Lander.Core.Missions;

public enum MissionKind
{
    /// <summary>Aterrizar sobre la ventana de una aplicación concreta.</summary>
    LandOnApp,

    /// <summary>Recoger los cristales que flotan sobre el escritorio.</summary>
    CollectCrystals,

    /// <summary>Ir de la base a una ventana lejana y volver sin repostar.</summary>
    RoundTrip,

    /// <summary>Llevar un paquete a otro monitor.</summary>
    Delivery,
}

public enum MissionStatus
{
    Offered,
    Active,
    Completed,
    Expired,
}

public sealed class Mission
{
    public required MissionKind Kind { get; init; }

    public MissionStatus Status { get; internal set; } = MissionStatus.Offered;

    public required int Reward { get; init; }

    /// <summary>Segundos restantes de la oferta o de la misión activa.</summary>
    public double TimeLeft { get; internal set; }

    /// <summary>Duración total de la misión una vez aceptada.</summary>
    public required double Duration { get; init; }

    /// <summary>Aplicación objetivo (<see cref="MissionKind.LandOnApp"/>).</summary>
    public string? AppId { get; init; }

    /// <summary>Una ventana de esa aplicación, para mostrar su icono.</summary>
    public long AppHandle { get; init; }

    /// <summary>Cristales pendientes (<see cref="MissionKind.CollectCrystals"/>).</summary>
    public List<Vec2> Crystals { get; } = [];

    public int TotalCrystals { get; internal set; }

    /// <summary>Monitor de destino (<see cref="MissionKind.Delivery"/>).</summary>
    public int TargetMonitor { get; internal set; } = -1;

    /// <summary>Etapa del viaje de ida y vuelta: 0 = salir de la base, 1 = posarse lejos, 2 = volver.</summary>
    public int Stage { get; internal set; }

    internal double OriginX { get; set; }

    public BubbleIcon Icon => Kind switch
    {
        MissionKind.LandOnApp => BubbleIcon.App,
        MissionKind.CollectCrystals => BubbleIcon.Crystal,
        MissionKind.RoundTrip => BubbleIcon.RoundTrip,
        _ => BubbleIcon.Package,
    };

    /// <summary>Progreso de 0 a 1, para el indicador.</summary>
    public double Progress => Kind switch
    {
        MissionKind.CollectCrystals when TotalCrystals > 0 => 1 - Crystals.Count / (double)TotalCrystals,
        MissionKind.RoundTrip => Stage / 3.0,
        _ => 0,
    };
}
