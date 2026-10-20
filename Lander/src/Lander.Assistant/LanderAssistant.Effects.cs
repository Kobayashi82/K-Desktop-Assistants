using Lander.Assistant.Audio;
using Lander.Assistant.Rendering;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Physics;
using Lander.Core.Racing;
using Sdk = KDesktop.Assistant.Sdk;

namespace Lander.Assistant;

/// <summary>Partículas, sonidos y avisos: lo que se ve y se oye de cada suceso del juego.</summary>
public sealed partial class LanderAssistant
{
    private double _antenna;
    private double _antennaVelocity;
    private Vec2 _lastVelocity;
    private double _gustCooldown;
    private double _notificationCooldown;

    private bool AntennaMoving => _antenna != 0 || _antennaVelocity != 0;

    private void UpdateEffects(double now, double elapsed)
    {
        var body = _game.Body;
        var visual = _game.Visual;
        var scale = body.Scale;

        if (body.State != BodyState.Destroyed && visual.MainFlame > 0.03)
        {
            var exhaust = -body.Up;
            var nozzle = BodyToWorld(body, _skin.Nozzle);
            _particles.Exhaust(nozzle, exhaust, body.Velocity, visual.MainFlame, scale, _settings.Trail, now, elapsed);

            var below = _world.PlatformBelow(body.Position.X, body.FeetY - 1);
            if (below is not null)
            {
                var height = below.Y - body.FeetY;
                var reach = body.Height * 1.6;
                if (height < reach)
                {
                    _particles.GroundDust(new Vec2(body.Position.X, below.Y), visual.MainFlame * (1 - height / reach), scale, elapsed);
                }
            }
        }

        if (body.State != BodyState.Destroyed && body.Damage > 0.45)
        {
            _particles.DamageWisp(body.Position, body.Damage, scale, elapsed);
        }

        // Bocanadas de los propulsores de giro y traslación: salen hacia fuera de cada uno.
        if (body.State != BodyState.Destroyed)
        {
            var side = Vec2.UpFromAngle(body.Angle + Math.PI / 2);
            EmitRcs(0, _skin.LeftJetPoint, -side, visual.LeftJet, elapsed);
            EmitRcs(1, _skin.RightJetPoint, side, visual.RightJet, elapsed);
            EmitRcs(2, _skin.LowerLeftJetPoint, -side, visual.LowerLeftJet, elapsed);
            EmitRcs(3, _skin.LowerRightJetPoint, side, visual.LowerRightJet, elapsed);
        }

        UpdateAntenna(body, elapsed);
        _particles.Update(elapsed);

        var engineLevel = !_sensors.QuietMode && body.State != BodyState.Destroyed && _exitStart is null ? visual.MainFlame : 0;
        _audio.SetEngine(engineLevel, body.Position.X, _span, elapsed);
    }

    private void HandleGameEvent(GameEvent gameEvent)
    {
        var scale = _game.Body.Scale;
        var position = gameEvent.Position;
        var feet = new Vec2(position.X, position.Y + LanderBody.BaseHalfHeight * scale);
        switch (gameEvent.Kind)
        {
            case GameEventKind.TookOff:
                if (_game.Mode == ControlMode.Auto)
                {
                    var autopilot = _game.Autopilot;
                    Log.Stats($"Despega: {autopilot.Purpose}, estilo {autopilot.Style}, destino {autopilot.Destination?.Kind.ToString() ?? "ninguno"}.");
                }

                _particles.DustBurst(feet, 0.25, scale);
                break;

            case GameEventKind.Touchdown:
            {
                var landing = gameEvent.Landing!;
                if (_game.Body.Support?.Platform is { Kind: Core.Terrain.PlatformKind.Content } content)
                {
                    Log.Stats($"Posado en el contenido de {content.WindowHandle:X} ({content.AppId}) en ({_game.Body.Position.X:0}, {content.Y:0}), {landing.Quality}.");
                }

                var intensity = Math.Clamp(landing.VerticalSpeed / (170 * scale), 0, 1);
                _particles.DustBurst(feet, 0.25 + intensity, scale);
                _particles.Ring(feet, scale, 0.2 + intensity);
                if (landing.Quality == LandingQuality.Hard)
                {
                    _particles.Sparks(feet, new Vec2(0, -1), 10, scale);
                    Play(Sound.HardThud);
                }
                else
                {
                    Play(Sound.LegClick, 0.6);
                }

                if (gameEvent.Manual && landing.Quality == LandingQuality.Perfect)
                {
                    Play(Sound.PerfectChime);
                    _particles.Sparkles(position, 26 * scale, Gfx.Rgb(0xFFD23F), 10, scale);
                }

                break;
            }

            case GameEventKind.Crashed:
                _particles.Explosion(position, scale, _skin.DebrisColors);
                Play(Sound.Explosion);
                break;

            case GameEventKind.Respawned:
                _particles.Beam(position, scale);
                _particles.Sparkles(position, 30 * scale, Gfx.Rgb(0x7DE3FF), 16, scale);
                Play(Sound.Respawn);
                break;

            case GameEventKind.SupportLost:
            case GameEventKind.Launched:
            case GameEventKind.Toppled:
            case GameEventKind.Startled:
                Play(Sound.Pop, 0.6);
                break;

            case GameEventKind.Slipped:
                _particles.Sparks(feet, new Vec2(-Math.Sign(_game.Body.Velocity.X), -0.4), 3, scale);
                Play(Sound.Scrape, 0.6);
                break;

            case GameEventKind.HitCeiling:
                _particles.Sparks(position - new Vec2(0, 24 * scale), new Vec2(0, 1), 5, scale);
                Play(Sound.HardThud, 0.35);
                break;

            case GameEventKind.HitWall:
                _particles.Sparks(position, new Vec2(-Math.Sign(_game.Body.Velocity.X), 0), 4, scale);
                Play(Sound.HardThud, 0.35);
                break;

            case GameEventKind.Boosted:
                Play(Sound.Boost);
                break;

            case GameEventKind.LegsDeployed when _game.Body.IsFlying:
                Play(Sound.LegClick, 0.5);
                break;

            case GameEventKind.FuelEmpty:
                Play(Sound.Pop);
                break;

            case GameEventKind.ModeChanged:
                UpdateGameKeys();
                Play(Sound.Pop, 0.7);
                _host.Log.Info(gameEvent.Mode == ControlMode.Manual ? "Modo manual." : "Modo automático.");
                break;

            case GameEventKind.Celebrated:
                _particles.Confetti(position - new Vec2(0, 20 * scale), scale);
                Play(Sound.PerfectChime, 0.7);
                break;

            case GameEventKind.LevelUp:
                _particles.Confetti(position - new Vec2(0, 20 * scale), scale, 40);
                Play(Sound.Fanfare);
                var unlocked = gameEvent.Unlocks.Count > 0
                    ? Texts.Format("level.unlocked", string.Join(", ", gameEvent.Unlocks.Select(u => Texts.Get("unlock." + u.Id, u.Name))))
                    : Texts.Get("level.keepGoing");
                _host.Notify(Texts.Format("level.title", gameEvent.Level), unlocked);
                PublishPanel();
                break;

            case GameEventKind.MissionOffered:
                Play(Sound.Pop, 0.8);
                if (gameEvent.Mission is { Status: Core.Missions.MissionStatus.Offered } offered)
                {
                    NotifyMissionOffer(offered);
                }

                break;

            case GameEventKind.MissionStarted:
                Play(Sound.Pop, 0.8);
                break;

            case GameEventKind.ChangedMind:
                Play(Sound.Pop, 0.35);
                Log.Stats($"Cambia de idea: ahora va a {_game.Autopilot.Destination?.Kind.ToString() ?? "ninguna parte"}.");
                break;

            case GameEventKind.MissionCompleted:
                _particles.Confetti(position - new Vec2(0, 20 * scale), scale, 36);
                Play(Sound.Fanfare);
                break;

            case GameEventKind.CrystalCollected:
                _particles.Sparkles(position, 22 * scale, Gfx.Rgb(0x5CF2FF), 14, scale);
                Play(Sound.Crystal);
                break;

            case GameEventKind.RaceStarted:
                Play(Sound.Pop, 0.8);
                _host.Log.Info($"Carrera: {gameEvent.Race!.Checkpoints.Count} balizas ({gameEvent.Race.CircuitKey}), tiempo de referencia {Race.FormatTime(gameEvent.Race.ParSeconds)} s.");
                break;

            case GameEventKind.RaceCheckpoint when gameEvent.Magnitude > 0:
                _particles.Sparkles(position, 24 * scale, Gfx.Rgb(0x60CDFF), 12, scale);
                Play(Sound.Crystal, 0.8);
                break;

            case GameEventKind.RaceFinished:
            {
                var race = gameEvent.Race!;
                _particles.Confetti(position - new Vec2(0, 20 * scale), scale, 44);
                Play(Sound.Fanfare);
                RecordRace(race);
                PublishPanel();
                _host.Log.Info($"Carrera terminada en {Race.FormatTime(race.Elapsed)} s (referencia {Race.FormatTime(race.ParSeconds)} s, penalización {race.Penalties:0} s): {race.Medal}{(race.IsNewRecord ? ", récord" : string.Empty)}.");
                break;
            }

            case GameEventKind.RaceFailed:
                Play(Sound.Pop, 0.6);
                _host.Log.Info($"Carrera perdida: {gameEvent.Race!.Failure}.");
                break;

            case GameEventKind.Flipped:
                _particles.Sparkles(position, 28 * scale, Gfx.Rgb(0xFFE08A), 12, scale);
                Play(Sound.Whoosh, 0.8);
                Play(Sound.Crystal, 0.4);
                break;

            case GameEventKind.EngineCut:
            {
                var body = _game.Body;
                _particles.EnginePuff(BodyToWorld(body, _skin.Nozzle), -body.Up, body.Velocity, scale, _settings.Trail, _now);
                Play(Sound.Puff, 0.5);
                break;
            }

            case GameEventKind.Ignition:
            {
                // Encendido a fondo de golpe: fogonazo en la tobera y la llama se estira un instante.
                var body = _game.Body;
                _particles.NozzleFlash(BodyToWorld(body, _skin.Nozzle), -body.Up, scale);
                _game.Visual.BoostFlash = Math.Max(_game.Visual.BoostFlash, 0.3);
                Play(Sound.Boost, 0.8);
                break;
            }

            case GameEventKind.Petted:
                _particles.Hearts(position - new Vec2(0, 22 * scale), scale);
                Play(Sound.PerfectChime, 0.45);
                break;

            case GameEventKind.Greeted:
                Play(Sound.Pop, 0.6);
                break;

            case GameEventKind.HourChime:
                _particles.Confetti(position - new Vec2(0, 20 * scale), scale, 30);
                Play(Sound.PerfectChime, 0.8);
                break;

            case GameEventKind.AchievementUnlocked:
                var achievement = gameEvent.Achievement!;
                Play(Sound.PerfectChime, 0.8);
                _host.Log.Info($"Logro: {achievement.Name}.");
                _host.Notify(Texts.Format("achievement.title", Texts.Get("ach." + achievement.Id, achievement.Name)), Texts.Get("ach." + achievement.Id + ".d", achievement.Description));
                break;
        }
    }

    private void Play(Sound sound, double gain = 1)
    {
        if (!_sensors.QuietMode)
        {
            _audio.Play(sound, gain, _game.Body.Position.X, _span);
        }
    }

    /// <summary>
    /// Viento: un scroll fuerte cerca del lander lo empuja hacia el lado contrario al cursor. El host avisa de los scrolls
    /// fuertes (unas 3,5 muescas seguidas), que es lo que el Lander independiente medía con la rueda.
    /// </summary>
    private void Gust(Sdk.SystemEvent.FastScroll scroll)
    {
        var body = _game.Body;
        var cursor = new Vec2(scroll.Position.X, scroll.Position.Y);
        if (_gustCooldown > 0 || _hidden || Vec2.Distance(cursor, body.Position) >= body.Height * 6)
        {
            return;
        }

        var direction = Math.Sign(body.Position.X - cursor.X);
        if (direction == 0)
        {
            direction = 1;
        }

        var notches = 3.5 + 8.5 * Math.Clamp(Math.Abs(scroll.Strength), 0, 1);
        var strength = 260 + 45 * notches;
        _game.Notify(new WindGust(new Vec2(direction * strength, -strength * 0.15), 0.6));
        _gustCooldown = 1.2;
    }

    /// <summary>Muelle de la antena: se inclina con las aceleraciones laterales y vuelve oscilando.</summary>
    private void UpdateAntenna(LanderBody body, double elapsed)
    {
        if (elapsed <= 0 || elapsed > 0.1)
        {
            // Tras una pausa larga (o al reaparecer) no hay inercia que valga.
            _antenna = 0;
            _antennaVelocity = 0;
            _lastVelocity = body.Velocity;
            return;
        }

        var acceleration = (body.Velocity - _lastVelocity) / elapsed;
        _lastVelocity = body.Velocity;
        var lateral = acceleration.X * Math.Cos(body.Angle) + acceleration.Y * Math.Sin(body.Angle);
        var push = Math.Clamp(-lateral / (body.Scale * 900), -1.5, 1.5) - body.AngularVelocity * 0.05;
        for (var t = 0.0; t < elapsed; t += 1.0 / 240)
        {
            var dt = Math.Min(1.0 / 240, elapsed - t);
            _antennaVelocity += (-170 * _antenna - 6 * _antennaVelocity + push * 40) * dt;
            _antenna = Math.Clamp(_antenna + _antennaVelocity * dt, -0.5, 0.5);
        }

        if (Math.Abs(_antenna) < 0.002 && Math.Abs(_antennaVelocity) < 0.02)
        {
            _antenna = 0;
            _antennaVelocity = 0;
        }
    }

    /// <summary>Bocanada de un propulsor: <paramref name="outward"/> es hacia dónde sale el gas.</summary>
    private void EmitRcs(int jet, System.Windows.Point local, Vec2 outward, double strength, double elapsed)
    {
        if (strength < 0.12)
        {
            return;
        }

        var body = _game.Body;
        var scale = body.Scale;
        var at = BodyToWorld(body, local) + outward * (2.5 * scale);
        _particles.Rcs(jet, at, outward, body.Velocity, strength, scale, elapsed);
    }

    private static Vec2 BodyToWorld(LanderBody body, System.Windows.Point local)
    {
        var scaled = new Vec2(local.X * body.Scale, local.Y * body.Scale);
        return body.Position + Rotate(scaled, body.Angle);
    }

    private static Vec2 Rotate(Vec2 v, double angle)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        return new Vec2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
