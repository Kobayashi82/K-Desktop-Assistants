namespace Lander.Core.Progression;

/// <summary>Un logro: se consigue al llevar una estadística del piloto hasta un objetivo.</summary>
public sealed record Achievement(string Id, string Name, string Description, Func<PilotProgress, double> Value, double Target)
{
    public bool IsEarned(PilotProgress progress) => Value(progress) >= Target;

    /// <summary>Fracción (0–1) recorrida hacia el objetivo.</summary>
    public double ProgressOf(PilotProgress progress) => Math.Clamp(Value(progress) / Target, 0, 1);
}

/// <summary>Logros sencillos a partir de las estadísticas: no guardan nada propio.</summary>
public static class Achievements
{
    public static IReadOnlyList<Achievement> All { get; } =
    [
        new("first-perfect", "Primer contacto", "Haz un aterrizaje perfecto pilotando tú.", p => p.PerfectLandings, 1),
        new("steady-hand", "Pulso firme", "Encadena 5 aterrizajes perfectos seguidos.", p => p.BestStreak, 5),
        new("unstoppable", "Imparable", "Encadena 12 aterrizajes perfectos seguidos.", p => p.BestStreak, 12),
        new("veteran", "Veterano", "Suma 100 aterrizajes perfectos.", p => p.PerfectLandings, 100),
        new("crystals", "Cazador de cristales", "Recoge 30 cristales.", p => p.CrystalsCollected, 30),
        new("courier", "Mensajero", "Completa 10 misiones.", p => p.MissionsCompleted, 10),
        new("first-race", "Bandera a cuadros", "Termina una carrera.", p => p.RacesCompleted, 1),
        new("gold", "Medalla de oro", "Termina una carrera por debajo del tiempo de referencia.", p => p.GoldMedals, 1),
        new("circuit-king", "Rey del circuito", "Consigue 10 medallas de oro.", p => p.GoldMedals, 10),
        new("flight-hour", "Una hora en el aire", "Vuela una hora en total, en automático o pilotando.", p => p.FlightSeconds, 3600),
        new("companion", "Compañero fiel", "Deja que aterrice solo 500 veces.", p => p.AutoLandings, 500),
        new("scrap", "Chatarrero", "Estrella el lander 25 veces. Les pasa a los mejores.", p => p.Crashes, 25),
        new("ace", "As del pilotaje", "Llega al nivel 10.", p => p.Level, 10),
        new("legend", "Leyenda", "Llega al nivel máximo.", p => p.Level, Levels.MaxLevel),
    ];

    public static IEnumerable<Achievement> EarnedBy(PilotProgress progress) => All.Where(a => a.IsEarned(progress));
}
