namespace Lander.Core.Physics;

/// <summary>
/// Lo que el piloto (jugador o IA) pide en un paso de simulación. Nadie mueve el lander directamente:
/// todo pasa por aquí.
/// </summary>
/// <param name="Throttle">Motor principal, de 0 a 1.</param>
/// <param name="Rotate">Propulsores de giro, de -1 (antihorario) a 1 (horario).</param>
/// <param name="Lateral">Propulsores laterales (modo asistido), de -1 (izquierda) a 1 (derecha).</param>
/// <param name="Stabilize">Estabilizador: endereza la nave y frena el giro.</param>
/// <param name="Boost">Impulso corto de emergencia.</param>
/// <param name="RetractLegs">Recoger las patas (por defecto están desplegadas).</param>
/// <param name="LandOn">
/// Si no es <see cref="AnyPlatform"/>, solo se aterriza en las plataformas de ese dueño (y en el suelo):
/// el piloto automático pasa por delante de las demás ventanas camino de su destino.
/// <see cref="GroundOnly"/> no aterriza en ninguna ventana.
/// </param>
/// <param name="DropThrough">
/// Posado en una ventana o en un borde de contenido: dejarse caer a través de él (el suelo no se atraviesa).
/// </param>
public readonly record struct ThrustInput(
    double Throttle = 0,
    double Rotate = 0,
    double Lateral = 0,
    bool Stabilize = false,
    bool Boost = false,
    bool RetractLegs = false,
    long LandOn = 0,
    bool DropThrough = false)
{
    public const long AnyPlatform = 0;
    public const long GroundOnly = long.MinValue;

    public static ThrustInput None => default;

    public bool IsIdle => Throttle <= 0 && Rotate == 0 && Lateral == 0 && !Boost;
}
