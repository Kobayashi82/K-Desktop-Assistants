using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Racing;

public enum RaceStartResult
{
    Started,

    /// <summary>No hay suficientes ventanas donde posarse para trazar un circuito.</summary>
    NoCircuit,

    /// <summary>El lander no puede salir ahora (cogido con el ratón o recomponiéndose).</summary>
    NotReady,

    /// <summary>Las ventanas del último circuito ya no están.</summary>
    CircuitGone,
}

/// <summary>
/// Modo carrera: traza un circuito de ventanas, cronometra el recorrido y guarda el mejor tiempo de cada
/// circuito durante la sesión.
/// </summary>
public sealed class RaceSystem(IRandom random)
{
    public const int MaxWindows = 4;

    /// <summary>Circuitos recientes que no se repiten al pedir uno nuevo (el último, nunca).</summary>
    private const int RecentCircuits = 3;

    /// <summary>Intentos de trazar un circuito distinto de los recientes antes de conformarse.</summary>
    private const int CircuitAttempts = 24;

    /// <summary>Segundos que el resultado se queda a la vista al terminar.</summary>
    public const double ResultDuration = 6;

    /// <summary>Segundos que una baliza puede estar oculta (tapada o minimizada) antes de saltarla.</summary>
    public const double LostCheckpointGrace = 4;

    private readonly Dictionary<string, double> _records = [];
    private readonly List<string> _recent = [];
    private IReadOnlyList<RaceCheckpoint>? _lastCircuit;
    private double _missingTime;

    public Race? Current { get; private set; }

    public bool IsActive => Current is { IsActive: true };

    /// <summary>Hay un circuito anterior que se puede repetir para batir el récord.</summary>
    public bool CanRepeat => _lastCircuit is not null;

    /// <summary>Mejor tiempo del último circuito, si se ha terminado alguna vez.</summary>
    public double? LastCircuitRecord => _lastCircuit is { } circuit && _records.TryGetValue(Race.KeyOf(circuit), out var record) ? record : null;

    public static int Points(RaceMedal medal, int windows) =>
        (medal switch
        {
            RaceMedal.Gold => 300,
            RaceMedal.Silver => 180,
            RaceMedal.Bronze => 100,
            _ => 0,
        }) + 40 * windows;

    public RaceStartResult Start(World world, LanderBody body, bool repeat, List<GameEvent> events)
    {
        if (body.State is BodyState.Destroyed or BodyState.Held || world.IsEmpty)
        {
            return RaceStartResult.NotReady;
        }

        IReadOnlyList<RaceCheckpoint>? circuit;
        if (repeat)
        {
            circuit = _lastCircuit;
            if (circuit is null || !IsUsable(world, body, circuit))
            {
                return RaceStartResult.CircuitGone;
            }
        }
        else
        {
            circuit = BuildCircuit(world, body);
            if (circuit is null)
            {
                return RaceStartResult.NoCircuit;
            }
        }

        if (IsActive)
        {
            Fail(RaceFailure.Abandoned, body, events);
        }

        var par = ParTime(world, body, circuit);
        var key = Race.KeyOf(circuit);
        var race = new Race
        {
            Checkpoints = circuit,
            ParSeconds = par,
            TimeLimit = Math.Max(90, par * 3),
            CircuitKey = key,
            PreviousRecord = _records.TryGetValue(key, out var record) ? record : null,
        };

        if (body.IsFlying)
        {
            race.Status = RaceStatus.Running;
        }

        _lastCircuit = circuit;
        _recent.Remove(key);
        _recent.Add(key);
        if (_recent.Count > RecentCircuits)
        {
            _recent.RemoveAt(0);
        }

        _missingTime = 0;
        Current = race;
        events.Add(new GameEvent(GameEventKind.RaceStarted, body.Position) { Race = race });
        return RaceStartResult.Started;
    }

    public void Update(World world, LanderBody body, double dt, List<GameEvent> events)
    {
        if (Current is not { } race)
        {
            return;
        }

        if (!race.IsActive)
        {
            race.ResultAge += dt;
            if (race.ResultAge >= ResultDuration)
            {
                Current = null;
            }

            return;
        }

        if (race.Status == RaceStatus.Running)
        {
            race.Elapsed += dt;
            if (race.Elapsed >= race.TimeLimit)
            {
                Fail(RaceFailure.TimeUp, body, events);
                return;
            }
        }

        // Una ventana cerrada o tapada del todo durante un rato deja de contar.
        if (race.NextCheckpoint is { IsBase: false } next && Race.TargetPlatform(world, next) is null)
        {
            _missingTime += dt;
            if (_missingTime >= LostCheckpointGrace)
            {
                _missingTime = 0;
                Advance(race, body, events, skipped: true);
            }
        }
        else
        {
            _missingTime = 0;
        }
    }

    public void OnTakeoff()
    {
        if (Current is { Status: RaceStatus.Ready } race)
        {
            race.Status = RaceStatus.Running;
        }
    }

    public void OnTouchdown(Platform platform, LandingQuality quality, LanderBody body, List<GameEvent> events)
    {
        if (Current is not { } race || race.NextCheckpoint is not { } next || quality == LandingQuality.Crash)
        {
            return;
        }

        var reached = next.IsBase ? platform.IsBase : platform.OwnerId == next.OwnerId;
        if (!reached)
        {
            return;
        }

        race.Status = RaceStatus.Running;
        race.LastLegPenalized = quality == LandingQuality.Hard;
        if (race.LastLegPenalized)
        {
            race.Penalties += Race.HardLandingPenalty;
            race.Elapsed += Race.HardLandingPenalty;
        }

        Advance(race, body, events, skipped: false);
    }

    public void OnCrash(LanderBody body, List<GameEvent> events)
    {
        if (IsActive)
        {
            Fail(RaceFailure.Crashed, body, events);
        }
    }

    /// <summary>Volver al modo automático abandona la carrera.</summary>
    public void OnModeChanged(ControlMode mode, LanderBody body, List<GameEvent> events)
    {
        if (mode == ControlMode.Auto && IsActive)
        {
            Fail(RaceFailure.Abandoned, body, events);
        }
    }

    public void Abandon(LanderBody body, List<GameEvent> events)
    {
        if (IsActive)
        {
            Fail(RaceFailure.Abandoned, body, events);
        }
    }

    /// <summary>Punto donde posarse en una baliza; para la base, el suelo más cercano a <paramref name="from"/>.</summary>
    public static Vec2? TargetPoint(World world, RaceCheckpoint checkpoint, Vec2 from, double margin)
    {
        if (!checkpoint.IsBase)
        {
            return Race.TargetPlatform(world, checkpoint) is { } platform ? new Vec2(platform.CenterX, platform.Y) : null;
        }

        if (world.NearestGround(from) is not { } ground)
        {
            return null;
        }

        var x = ground.Width > margin * 2 ? Math.Clamp(from.X, ground.Left + margin, ground.Right - margin) : ground.CenterX;
        return new Vec2(x, ground.Y);
    }

    private void Advance(Race race, LanderBody body, List<GameEvent> events, bool skipped)
    {
        race.Next++;
        events.Add(new GameEvent(GameEventKind.RaceCheckpoint, body.Position) { Race = race, Magnitude = skipped ? 0 : 1 });
        if (race.Next >= race.Checkpoints.Count)
        {
            Finish(race, body, events);
        }
    }

    private void Finish(Race race, LanderBody body, List<GameEvent> events)
    {
        race.Status = RaceStatus.Finished;
        race.Medal = Race.MedalFor(race.Elapsed, race.ParSeconds);
        race.IsNewRecord = race.PreviousRecord is not { } previous || race.Elapsed < previous;
        if (race.IsNewRecord)
        {
            _records[race.CircuitKey] = race.Elapsed;
        }

        events.Add(new GameEvent(GameEventKind.RaceFinished, body.Position)
        {
            Race = race,
            Points = Points(race.Medal, race.WindowCount),
        });
    }

    private void Fail(RaceFailure reason, LanderBody body, List<GameEvent> events)
    {
        var race = Current!;
        race.Status = RaceStatus.Failed;
        race.Failure = reason;
        events.Add(new GameEvent(GameEventKind.RaceFailed, body.Position) { Race = race });
    }

    private static double MinWidth(LanderBody body) => body.Height * 2.4;

    /// <summary>Ventanas donde se puede poner una baliza: anchas y con sitio por encima para llegar.</summary>
    private static List<Platform> Candidates(World world, LanderBody body) =>
        world.Platforms
            .Where(p => p.Kind == PlatformKind.Window && p.Width >= MinWidth(body)
                && world.GetMonitor(p.MonitorIndex) is { } monitor && p.Y - monitor.Bounds.Top >= body.Height * 1.6)
            .GroupBy(p => p.OwnerId)
            .Select(g => g.MaxBy(p => p.Width)!)
            .ToList();

    private static bool IsUsable(World world, LanderBody body, IReadOnlyList<RaceCheckpoint> circuit)
    {
        var owners = Candidates(world, body).Select(p => p.OwnerId).ToHashSet();
        return circuit.All(c => c.IsBase || owners.Contains(c.OwnerId));
    }

    /// <summary>
    /// Traza un circuito nuevo y, si puede, distinto de los últimos: aunque solo haya dos o tres ventanas, cambian el
    /// orden, cuántas balizas hay (una ventana puede volver a salir, nunca dos veces seguidas) y dónde se acaba (en la
    /// base o, si salía de una ventana, de vuelta en ella). Null si no hay al menos dos ventanas donde poner balizas.
    /// </summary>
    private List<RaceCheckpoint>? BuildCircuit(World world, LanderBody body)
    {
        var current = body.Support?.OwnerId;
        var windows = Candidates(world, body);
        var start = windows.Find(p => p.OwnerId == current);
        if (windows.Count < 2)
        {
            return null;
        }

        List<RaceCheckpoint>? fallback = null;
        for (var attempt = 0; attempt < CircuitAttempts; attempt++)
        {
            var circuit = RandomCircuit(windows, start, body);
            var key = Race.KeyOf(circuit);
            if (!_recent.Contains(key))
            {
                return circuit;
            }

            // Si todo se ha corrido hace poco, al menos que no sea el mismo que el último.
            if (fallback is null && _recent[^1] != key)
            {
                fallback = circuit;
            }
        }

        return fallback ?? RandomCircuit(windows, start, body);
    }

    /// <summary>Un circuito al azar: cada baliza, con más probabilidad cuanto más lejos esté de la anterior.</summary>
    private List<RaceCheckpoint> RandomCircuit(List<Platform> windows, Platform? start, LanderBody body)
    {
        // Con pocas ventanas también hay carreras cortas; con muchas, casi siempre las cuatro balizas.
        var distinct = windows.Count;
        var length = distinct >= MaxWindows && random.Chance(0.6) ? MaxWindows : 2 + random.Next(MaxWindows - 1);
        var uses = new Dictionary<long, int>();
        var circuit = new List<RaceCheckpoint>();
        var from = body.Position;
        var previous = start?.OwnerId ?? 0;
        while (circuit.Count < length)
        {
            var weights = windows
                .Select(p =>
                {
                    // Nunca la misma dos veces seguidas; repetir una ya visitada, solo si no quedan otras por visitar.
                    var used = uses.GetValueOrDefault(p.OwnerId);
                    if (p.OwnerId == previous || used >= 2 || (used > 0 && windows.Any(w => w.OwnerId != previous && !uses.ContainsKey(w.OwnerId))))
                    {
                        return 0;
                    }

                    var distance = Vec2.Distance(from, new Vec2(p.CenterX, p.Y));
                    return distance < body.Height * 4 ? distance * 0.1 : distance;
                })
                .ToList();
            var index = random.PickWeighted(weights);
            if (index < 0)
            {
                break;
            }

            var chosen = windows[index];
            uses[chosen.OwnerId] = uses.GetValueOrDefault(chosen.OwnerId) + 1;
            circuit.Add(new RaceCheckpoint(chosen.OwnerId, chosen.AppId));
            from = new Vec2(chosen.CenterX, chosen.Y);
            previous = chosen.OwnerId;
        }

        // Saliendo de una ventana, a veces la meta es volver a ella; si no, la base.
        circuit.Add(start is not null && previous != start.OwnerId && random.Chance(0.35)
            ? new RaceCheckpoint(start.OwnerId, start.AppId)
            : new RaceCheckpoint(0, null));
        return circuit;
    }

    /// <summary>
    /// Tiempo de referencia: lo que tarda un buen piloto en cada tramo (despegar, desplazarse y posarse con
    /// calma), con un ritmo que se ajusta al tamaño del lander.
    /// </summary>
    internal static double ParTime(World world, LanderBody body, IReadOnlyList<RaceCheckpoint> circuit)
    {
        const double perLeg = 2.5;
        var scale = Math.Max(0.1, body.Scale);
        var from = body.Position;
        var total = 0.0;
        foreach (var checkpoint in circuit)
        {
            if (TargetPoint(world, checkpoint, from, body.Height) is not { } to)
            {
                continue;
            }

            var dx = Math.Abs(to.X - from.X);
            var dy = to.Y - from.Y;
            var climb = Math.Max(0, -dy);
            var descent = Math.Max(0, dy);
            total += perLeg + dx / (190 * scale) + climb / (140 * scale) + descent / (220 * scale);
            from = to;
        }

        return Math.Round(total * 2) / 2;
    }
}
