namespace Lander.Core.Progression;

/// <summary>Progreso persistente del piloto y estadísticas de vuelo.</summary>
public sealed class PilotProgress
{
    public long TotalPoints { get; set; }

    public int CurrentStreak { get; set; }

    public int BestStreak { get; set; }

    public int PerfectLandings { get; set; }

    public int HardLandings { get; set; }

    public int AutoLandings { get; set; }

    public int Crashes { get; set; }

    public int MissionsCompleted { get; set; }

    public int CrystalsCollected { get; set; }

    public int RacesCompleted { get; set; }

    public int GoldMedals { get; set; }

    public double FlightSeconds { get; set; }

    public double ManualFlightSeconds { get; set; }

    public int Level => Levels.LevelFor(TotalPoints);

    /// <summary>Copia independiente (para guardarla desde otro hilo).</summary>
    public PilotProgress Clone() => (PilotProgress)MemberwiseClone();

    public void CopyFrom(PilotProgress other)
    {
        TotalPoints = other.TotalPoints;
        CurrentStreak = other.CurrentStreak;
        BestStreak = other.BestStreak;
        PerfectLandings = other.PerfectLandings;
        HardLandings = other.HardLandings;
        AutoLandings = other.AutoLandings;
        Crashes = other.Crashes;
        MissionsCompleted = other.MissionsCompleted;
        CrystalsCollected = other.CrystalsCollected;
        RacesCompleted = other.RacesCompleted;
        GoldMedals = other.GoldMedals;
        FlightSeconds = other.FlightSeconds;
        ManualFlightSeconds = other.ManualFlightSeconds;
    }
}
