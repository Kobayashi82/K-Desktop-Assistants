using Lander.Core.Geometry;

namespace Lander.Core.Game;

public enum ControlMode
{
    Auto,
    Manual,
}

public enum ModeChangeReason
{
    User,
    Timeout,
    Fullscreen,
    Paused,
}

public enum MissionFrequency
{
    Off,
    Low,
    Normal,
    High,
}

/// <summary>Teclas del juego pulsadas en este instante.</summary>
/// <param name="Up">↑: motor principal.</param>
/// <param name="Down">↓: endereza la nave con los propulsores de giro.</param>
/// <param name="Left">←: gira a la izquierda (propulsores de giro).</param>
/// <param name="Right">→: gira a la derecha.</param>
/// <param name="Boost">Espacio: impulso.</param>
/// <param name="SlideLeft">A: propulsores de traslación hacia la izquierda.</param>
/// <param name="SlideRight">D: propulsores de traslación hacia la derecha.</param>
/// <param name="Legs">G: saca o recoge las patas.</param>
public readonly record struct ManualControls(
    bool Up,
    bool Down,
    bool Left,
    bool Right,
    bool Boost,
    bool SlideLeft = false,
    bool SlideRight = false,
    bool Legs = false)
{
    public bool Any => Up || Down || Left || Right || Boost || SlideLeft || SlideRight || Legs;
}

/// <summary>Lo que el lander percibe del sistema en cada paso.</summary>
public sealed class Sensors
{
    public Vec2? Cursor { get; set; }

    /// <summary>Velocidad suavizada del cursor (px/s).</summary>
    public Vec2 CursorVelocity { get; set; }

    /// <summary>Segundos que lleva el cursor sin moverse.</summary>
    public double CursorStillSeconds { get; set; }

    /// <summary>Segundos desde la última entrada del usuario (teclado o ratón).</summary>
    public double UserIdleSeconds { get; set; }

    public bool BatteryLow { get; set; }

    public bool PowerSaver { get; set; }

    /// <summary>No molestar: el lander se queda quieto y en silencio.</summary>
    public bool QuietMode { get; set; }

    public bool DarkMode { get; set; }

    /// <summary>Monitores con una aplicación a pantalla completa.</summary>
    public IReadOnlySet<int> FullscreenMonitors { get; set; } = new HashSet<int>();

    /// <summary>Ventana en primer plano (0 si no es una de las que forman terreno).</summary>
    public long ActiveWindow { get; set; }

    /// <summary>Hora local (para las horas en punto). Null en los tests y simulaciones.</summary>
    public DateTime? LocalTime { get; set; }
}

/// <summary>Por qué necesita el lander ver el contenido de las ventanas en este momento.</summary>
public enum ContentNeed
{
    /// <summary>No hace falta: no se captura nada.</summary>
    None,

    /// <summary>Va a elegir destino enseguida: conviene tener los bordes vistos (y confirmados) para entonces.</summary>
    ChoosingSoon,

    /// <summary>Vuela hacia un borde de contenido: hay que seguir comprobando que sigue ahí.</summary>
    Destination,

    /// <summary>Posado sobre un borde de contenido: hay que seguirlo (scroll, cambios de página).</summary>
    Landed,

    /// <summary>Pilotando a mano: los bordes cercanos son plataformas del juego.</summary>
    Manual,
}

/// <summary>Qué contenido necesita ver el lander y de qué ventana (0 si no es de una concreta).</summary>
/// <param name="Need">Para qué.</param>
/// <param name="Window">Ventana que hay que seguir (posado o volando hacia su contenido).</param>
/// <param name="Owner">Plataforma de contenido concreta (posado o destino), o 0.</param>
/// <param name="Piloting">Pilotando a mano: además, las ventanas de alrededor.</param>
public readonly record struct ContentInterest(ContentNeed Need, long Window = 0, long Owner = 0, bool Piloting = false);

/// <summary>Sucesos puntuales del sistema a los que el lander reacciona.</summary>
public abstract record SystemEvent;

public sealed record WindowOpened(long Handle) : SystemEvent;

/// <summary>El usuario ha pasado a otra ventana (primer plano).</summary>
public sealed record WindowActivated(long Handle) : SystemEvent;

public sealed record NotificationArrived : SystemEvent;

public sealed record RecycleBinEmptied(Vec2 Location) : SystemEvent;

/// <summary>Ráfaga de viento (por ejemplo, scroll rápido cerca del lander).</summary>
public sealed record WindGust(Vec2 Acceleration, double Duration) : SystemEvent;

public sealed record GameOptions
{
    public double LanderSizeDip { get; init; } = 48;

    /// <summary>0 = sigue al cursor, 1 = huye de él.</summary>
    public double Shyness { get; init; } = 0.5;

    /// <summary>Multiplicador de la frecuencia de vuelos automáticos (0,5 tranquilo – 2 activo).</summary>
    public double Activity { get; init; } = 1;

    public MissionFrequency MissionFrequency { get; init; } = MissionFrequency.Normal;

    public double ManualTimeoutSeconds { get; init; } = 30;

    /// <summary>Visita el reloj de la barra de tareas a las horas en punto.</summary>
    public bool VisitClock { get; init; } = true;

    /// <summary>
    /// Torpeza del piloto automático, de 0 (nunca se equivoca) a 1: a veces calcula mal la frenada, baja
    /// demasiado deprisa o llega torcido, y puede acabar en un aterrizaje duro o en un accidente.
    /// </summary>
    public double PilotErrors { get; init; }

    /// <summary>
    /// Ganas de acrobacias del piloto automático, de 0 (vuela siempre tranquilo: sin volteretas, caídas libres
    /// ni saltos balísticos) a 1 (las hace a menudo). 0,5 es lo normal.
    /// </summary>
    public double Acrobatics { get; init; } = 0.5;
}
