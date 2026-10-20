using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Terrain;

namespace Lander.Core.Missions;

/// <summary>Ofrece misiones cortas de vez en cuando y sigue su progreso.</summary>
public sealed class MissionSystem(IRandom random)
{
    public const double OfferDuration = 25;
    private const int CrystalCount = 3;

    private double _nextOfferIn = double.NaN;
    private MissionFrequency _frequency = MissionFrequency.Normal;

    public Mission? Current { get; private set; }

    /// <summary>Sin misiones mientras dura una carrera: se retira la que haya y no se ofrecen más.</summary>
    public bool Suspended { get; set; }

    public MissionFrequency Frequency
    {
        get => _frequency;
        set
        {
            if (_frequency != value)
            {
                _frequency = value;
                _nextOfferIn = double.NaN;
            }
        }
    }

    /// <summary>Puntos por recoger un cristal (además de la recompensa final).</summary>
    public static int CrystalPoints => 40;

    public void Update(World world, LanderBody body, ControlMode mode, Sensors sensors, double dt, List<GameEvent> events)
    {
        if (Suspended)
        {
            if (Current is not null)
            {
                Finish(MissionStatus.Expired, body, events);
            }

            return;
        }

        if (Current is null)
        {
            // Con las misiones desactivadas no se ofrece ninguna sola, pero una pedida desde el menú
            // sigue su curso.
            if (Frequency == MissionFrequency.Off)
            {
                return;
            }

            if (double.IsNaN(_nextOfferIn))
            {
                _nextOfferIn = NextOfferDelay() * 0.5;
            }

            _nextOfferIn -= dt;
            if (_nextOfferIn <= 0 && sensors.UserIdleSeconds < 60 && !sensors.QuietMode
                && body.State != BodyState.Destroyed && !world.IsEmpty)
            {
                Offer(world, body, mode, events);
                _nextOfferIn = NextOfferDelay();
            }

            return;
        }

        var mission = Current;
        mission.TimeLeft -= dt;
        if (mission.TimeLeft <= 0)
        {
            Finish(MissionStatus.Expired, body, events);
            return;
        }

        if (mission is { Status: MissionStatus.Active, Kind: MissionKind.CollectCrystals } && mode == ControlMode.Manual
            && body.State is BodyState.Flying or BodyState.Landed)
        {
            var radius = body.Height * 0.9;
            for (var i = mission.Crystals.Count - 1; i >= 0; i--)
            {
                if (Vec2.Distance(mission.Crystals[i], body.Position) < radius)
                {
                    var crystal = mission.Crystals[i];
                    mission.Crystals.RemoveAt(i);
                    events.Add(new GameEvent(GameEventKind.CrystalCollected, crystal)
                    {
                        Mission = mission,
                        Points = CrystalPoints,
                    });
                }
            }

            if (mission.Crystals.Count == 0)
            {
                Finish(MissionStatus.Completed, body, events);
            }
        }
    }

    /// <summary>Tomar el control durante la oferta acepta la misión.</summary>
    public void OnModeChanged(ControlMode mode, World world, LanderBody body, List<GameEvent> events)
    {
        if (mode == ControlMode.Manual && !Suspended && Current is { Status: MissionStatus.Offered })
        {
            Activate(world, body, events);
        }
    }

    public void OnTakeoff(Platform? from, LanderBody body)
    {
        if (Current is { Status: MissionStatus.Active, Kind: MissionKind.RoundTrip, Stage: 0 } mission && from is { IsBase: true })
        {
            mission.Stage = 1;
            mission.OriginX = body.Position.X;
        }
    }

    public void OnTouchdown(Platform platform, LandingQuality quality, bool manual, World world, LanderBody body, List<GameEvent> events)
    {
        if (Current is not { Status: MissionStatus.Active } mission || !manual || quality == LandingQuality.Crash)
        {
            return;
        }

        switch (mission.Kind)
        {
            case MissionKind.LandOnApp when platform.AppId is not null && platform.AppId == mission.AppId:
            case MissionKind.Delivery when platform.MonitorIndex == mission.TargetMonitor:
                Finish(MissionStatus.Completed, body, events);
                break;

            case MissionKind.RoundTrip:
                if (mission.Stage == 1 && platform.Kind == PlatformKind.Window)
                {
                    var monitorWidth = world.GetMonitor(platform.MonitorIndex)?.Bounds.Width ?? 1000;
                    if (Math.Abs(body.Position.X - mission.OriginX) >= monitorWidth * 0.25)
                    {
                        mission.Stage = 2;
                    }
                }
                else if (mission.Stage == 1 && platform.IsBase)
                {
                    mission.Stage = 0;
                }
                else if (mission.Stage == 2 && platform.IsBase)
                {
                    mission.Stage = 3;
                    Finish(MissionStatus.Completed, body, events);
                }

                break;
        }
    }

    public void OnCrash(LanderBody body, List<GameEvent> events)
    {
        switch (Current)
        {
            case { Status: MissionStatus.Active, Kind: MissionKind.RoundTrip } mission:
                mission.Stage = 0;
                break;
            case { Status: MissionStatus.Active, Kind: MissionKind.Delivery }:
                // El paquete no sobrevive.
                Finish(MissionStatus.Expired, body, events);
                break;
        }
    }

    /// <summary>Fuerza una oferta inmediata (menú de la bandeja). Devuelve si se ha podido ofrecer.</summary>
    public bool OfferNow(World world, LanderBody body, ControlMode mode, List<GameEvent> events, MissionKind? kind = null)
    {
        if (Current is not null || Suspended || world.IsEmpty)
        {
            return false;
        }

        var mission = kind is { } forced ? Create(forced, world, body) : Create(world, body);
        if (mission is null)
        {
            return false;
        }

        Offer(mission, world, body, mode, events);
        return true;
    }

    private void Offer(World world, LanderBody body, ControlMode mode, List<GameEvent> events)
    {
        if (Create(world, body) is { } mission)
        {
            Offer(mission, world, body, mode, events);
        }
    }

    private void Offer(Mission mission, World world, LanderBody body, ControlMode mode, List<GameEvent> events)
    {
        mission.TimeLeft = OfferDuration;
        Current = mission;
        events.Add(new GameEvent(GameEventKind.MissionOffered, body.Position) { Mission = mission });

        if (mode == ControlMode.Manual)
        {
            Activate(world, body, events);
        }
    }

    private Mission? Create(World world, LanderBody body)
    {
        var kinds = new List<MissionKind> { MissionKind.LandOnApp, MissionKind.CollectCrystals, MissionKind.RoundTrip, MissionKind.Delivery };
        while (kinds.Count > 0)
        {
            var index = random.Next(kinds.Count);
            if (Create(kinds[index], world, body) is { } mission)
            {
                return mission;
            }

            kinds.RemoveAt(index);
        }

        return null;
    }

    private Mission? Create(MissionKind kind, World world, LanderBody body) => kind switch
    {
        MissionKind.LandOnApp => CreateLandOnApp(world, body),
        MissionKind.CollectCrystals => new Mission { Kind = MissionKind.CollectCrystals, Reward = 250, Duration = 150 },
        MissionKind.RoundTrip when world.Platforms.Any(p => p.Kind == PlatformKind.Window) =>
            new Mission { Kind = MissionKind.RoundTrip, Reward = 300, Duration = 180 },
        MissionKind.Delivery when world.Monitors.Count > 1 =>
            new Mission { Kind = MissionKind.Delivery, Reward = 400, Duration = 150 },
        _ => null,
    };

    private Mission? CreateLandOnApp(World world, LanderBody body)
    {
        var current = body.Support?.OwnerId;
        var minWidth = body.Height * 2;
        var apps = world.Platforms
            .Where(p => p.Kind == PlatformKind.Window && p.AppId is not null && p.OwnerId != current && p.Width >= minWidth)
            .GroupBy(p => p.AppId!)
            .Select(g => g.MaxBy(p => p.Width)!)
            .ToList();
        if (apps.Count == 0)
        {
            return null;
        }

        var target = random.Pick(apps);
        return new Mission
        {
            Kind = MissionKind.LandOnApp,
            Reward = 200,
            Duration = 120,
            AppId = target.AppId,
            AppHandle = target.OwnerId,
        };
    }

    private void Activate(World world, LanderBody body, List<GameEvent> events)
    {
        var mission = Current!;
        mission.Status = MissionStatus.Active;
        mission.TimeLeft = mission.Duration;

        switch (mission.Kind)
        {
            case MissionKind.CollectCrystals:
                SpawnCrystals(world, body, mission);
                break;
            case MissionKind.Delivery:
                var current = world.MonitorAt(body.Position)?.Index ?? world.PrimaryMonitor?.Index ?? 0;
                var others = world.Monitors.Where(m => m.Index != current).ToList();
                mission.TargetMonitor = others.Count > 0 ? random.Pick(others).Index : current;
                break;
            case MissionKind.RoundTrip:
                mission.Stage = 0;
                break;
        }

        events.Add(new GameEvent(GameEventKind.MissionStarted, body.Position) { Mission = mission });
    }

    private void SpawnCrystals(World world, LanderBody body, Mission mission)
    {
        var monitor = world.MonitorAt(body.Position) ?? world.PrimaryMonitor;
        if (monitor is null)
        {
            return;
        }

        var area = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        var height = body.Height;
        for (var attempt = 0; attempt < 200 && mission.Crystals.Count < CrystalCount; attempt++)
        {
            var point = new Vec2(
                random.Range(area.Left + height * 2, area.Right - height * 2),
                random.Range(area.Top + height * 2, area.Bottom - height * 3));

            if (Vec2.Distance(point, body.Position) < height * 5
                || mission.Crystals.Any(c => Vec2.Distance(c, point) < height * 4))
            {
                continue;
            }

            mission.Crystals.Add(point);
        }

        mission.TotalCrystals = mission.Crystals.Count;
    }

    private void Finish(MissionStatus status, LanderBody body, List<GameEvent> events)
    {
        var mission = Current!;
        mission.Status = status;
        Current = null;
        events.Add(new GameEvent(
            status == MissionStatus.Completed ? GameEventKind.MissionCompleted : GameEventKind.MissionExpired,
            body.Position)
        {
            Mission = mission,
            Points = status == MissionStatus.Completed ? mission.Reward : 0,
        });
    }

    private double NextOfferDelay() => Frequency switch
    {
        MissionFrequency.Low => random.Range(900, 1500),
        MissionFrequency.High => random.Range(120, 240),
        _ => random.Range(360, 720),
    };
}
