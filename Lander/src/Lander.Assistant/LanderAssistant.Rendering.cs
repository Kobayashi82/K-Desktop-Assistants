using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using Lander.Assistant.Rendering;
using Lander.Core.Autopilot;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Missions;
using Lander.Core.Physics;
using Lander.Core.Racing;
using Lander.Core.Terrain;
using SdkRect = KDesktop.Assistant.Sdk.PixelRect;

namespace Lander.Assistant;

/// <summary>
/// Dibujo del lander en su superficie de la capa del host. Solo se presenta un fotograma cuando cambia algo visible (se
/// compara una firma del estado); entre dos pasos de física se interpola, también posado sobre una ventana que se mueve.
/// </summary>
public sealed partial class LanderAssistant
{
    private const double WindowSizeFactor = 5.8;
    private const double BlinkPeriod = 4.6;
    private const double BlinkLength = 0.16;

    private readonly List<MarkerSpec> _markerSpecs = [];
    private readonly Dictionary<long, ImageSource?> _appIcons = [];
    private ISurface _surface = null!;
    private IHitArea _hit = null!;
    private (int X, int Y, int Size, int Angle) _hitKey = (int.MinValue, 0, 0, 0);
    private bool _hitShown;
    private int _lastSignature;
    private bool _forceRender = true;
    private double _lastMarkerRender;
    private double _lightClock;

    /// <summary>Rectángulo del último fotograma (las partículas fuera de él no se ven).</summary>
    private Rect _frame = Rect.Empty;

    /// <summary>El faro aún se está encendiendo o apagando.</summary>
    private bool LightFading => _scene.LandingLight > 0;

    private void Render(double now)
    {
        var body = _game.Body;
        var visual = _game.Visual;

        // Entre dos pasos de física (60 por segundo) se interpola, también posado sobre una ventana que se mueve; solo
        // un salto brusco (reaparecer, mudarse de monitor) se dibuja tal cual.
        var alpha = Math.Clamp(_accumulator / LanderGame.TimeStep, 0, 1);
        var jumped = (body.Position - _previousPosition).LengthSquared > body.Height * body.Height * 4;
        var position = body.State == BodyState.Held || jumped ? body.Position : Vec2.Lerp(_previousPosition, body.Position, alpha);
        var angle = jumped ? body.Angle : _previousAngle + MathUtil.WrapAngle(body.Angle - _previousAngle) * alpha;

        var size = Math.Max(120, (int)Math.Ceiling(WindowSizeFactor * body.Height / 2) * 2);
        var x = (int)Math.Round(position.X) - size / 2;
        var y = (int)Math.Round(position.Y) - size / 2;

        // En reposo, el tiempo de las animaciones avanza a saltos para no redibujar sin necesidad.
        var lively = IsActive(now);
        var away = !lively && IsUserAway() && body.IsLanded;
        var animationTime = away ? Math.Floor(now) : lively ? now : Math.Floor(now * 4) / 4;
        var bubbleAge = away ? Math.Floor(visual.BubbleAge * 2) / 2 : lively || visual.IsAnimating ? visual.BubbleAge : Math.Floor(visual.BubbleAge * 4) / 4;
        var blink = BlinkAt(now);

        // Al salir se desvanece; al reaccionar a una orden, un destello de color (ver Gestures).
        var exit = FadeAt(now);

        _scene.Position = position;
        _scene.Angle = angle;
        _scene.Scale = body.Scale;
        _scene.Visible = body.State != BodyState.Destroyed;
        _scene.Opacity = visual.Opacity * (1 - exit);
        _scene.Skin = _skin;
        _scene.Dark = _sensors.DarkMode;
        _scene.Time = animationTime;
        _scene.TopLimit = (_world.MonitorAt(position) ?? _world.NearestMonitor(position))?.Bounds.Top ?? double.MinValue;
        _scene.Pose = new LanderPose
        {
            LegsDeploy = body.LegsDeploy,
            Expression = visual.Expression,
            Look = visual.Look,
            Blink = blink,
            Night = _sensors.DarkMode,
            Time = animationTime,
            Damage = body.Damage,
            MainFlame = visual.MainFlame,
            LeftJet = visual.LeftJet,
            RightJet = visual.RightJet,
            LowerLeftJet = visual.LowerLeftJet,
            LowerRightJet = visual.LowerRightJet,
            BoostFlash = visual.BoostFlash,
            CarryingPackage = visual.CarryingPackage,
        };
        ApplyAnimations(position, body, visual, now);
        _scene.Bubble = visual.Bubble;
        _scene.BubbleAge = bubbleAge;
        _scene.BubbleIcon = visual.Bubble is { Icon: BubbleIcon.App } bubble ? AppIcon(bubble.AppHandle) : null;
        _scene.BubbleHint = MissionHint();
        _scene.ShowHud = visual.ShowHud;
        _scene.Fuel = body.FuelFraction;
        _scene.Damage = body.Damage;
        _scene.LegsDown = _game.ManualLegsDown;
        _scene.Mission = _game.Missions.Current;
        _scene.MissionIcon = _scene.Mission is { Kind: MissionKind.LandOnApp } mission ? AppIcon(mission.AppHandle) : null;
        _scene.Popups = visual.Popups;
        var race = _game.Races.Current;
        _scene.Race = race;
        var raceSignature = race is null ? 0 : HashCode.Combine(race.Status, race.Next, race.Medal, (int)(race.Elapsed * 10), (int)(race.ResultAge * 4));

        // Se dibuja con precisión de subpíxel: también cuenta como cambio.
        var subpixel = HashCode.Combine((int)(position.X * 8), (int)(position.Y * 8), (int)(exit * 30), _reaction?.GetHashCode() ?? 0,
            (int)(_scene.Pose.Signal * 40), _scene.Pose.SignalColor);
        var animation = HashCode.Combine((int)(_scene.Pose.Compress * 20), (int)(_scene.Pose.Yawn * 20), (int)(_scene.Pose.AntennaSway * 100),
            (int)(_scene.Offset.X * 10), (int)(_scene.Offset.Y * 10), (int)(_scene.Tilt * 300), (int)(_scene.ShadowStrength * 30), (int)(_scene.GroundGlow * 30) * 37 + (int)(_scene.LandingLight * 30));
        var signature = HashCode.Combine(
            raceSignature,
            subpixel,
            animation,
            HashCode.Combine(x, y, size, (int)(angle * 300), (int)(body.LegsDeploy * 40), visual.Expression, (int)(visual.Look.X * 20), (int)(visual.Look.Y * 20)),
            HashCode.Combine(blink > 0.5, (int)(visual.MainFlame * 50), (int)(visual.Opacity * 40), visual.Bubble, (int)(bubbleAge * 30), visual.Popups.Count, (int)(body.FuelFraction * 60)),
            HashCode.Combine(_sensors.DarkMode ? (int)(animationTime * 4) : 0, visual.ShowHud, _skin, body.State, (int)(body.Damage * 20), _scene.Mission?.Status, (int)((_scene.Mission?.TimeLeft ?? 0) * 2), _scene.BubbleHint));

        if (!_forceRender && signature == _lastSignature && _particles.Count == 0 && (away || !visual.IsAnimating) && _reaction is null)
        {
            return;
        }

        _forceRender = false;
        _lastSignature = signature;
        var window = new Rect(x, y, size, size);
        _frame = window;
        var reaction = ReactionGlow(now);
        _surface.Present(SdkRect.FromSize(x, y, size, size), dc =>
        {
            if (reaction is { } glow)
            {
                dc.DrawEllipse(Rendering.Skins.LanderParts.Glow(glow.Color, glow.Strength), null, new Point(position.X, position.Y), body.Height * 1.1, body.Height * 1.1);
            }

            SceneRenderer.Draw(dc, _scene, _particles, window);
        });
    }

    /// <summary>
    /// Animaciones puramente visuales: suspensión al posarse, estirarse, bostezar, saltito de alegría, saludo, meneo,
    /// vibración con el motor a tope, antena con inercia y la sombra sobre la superficie.
    /// </summary>
    private void ApplyAnimations(Vec2 position, LanderBody body, LanderVisual visual, double now)
    {
        static double Progress(double remaining, double duration) => remaining > 0 ? 1 - remaining / duration : 0;
        static double Envelope(double remaining, double duration) => remaining > 0 ? Math.Sin(Math.PI * Progress(remaining, duration)) : 0;

        var stretch = Envelope(visual.StretchTime, LanderVisual.StretchDuration);
        var yawn = Envelope(visual.YawnTime, LanderVisual.YawnDuration);
        var compress = visual.Squash * 3.2 - stretch * 2.4 - yawn * 0.9 + visual.CrouchLevel * 3;

        var hop = 9 * Envelope(visual.BounceTime, LanderVisual.BounceDuration);
        var tilt = 0.0;
        if (visual.WaveTime > 0)
        {
            var p = Progress(visual.WaveTime, LanderVisual.WaveDuration);
            tilt += Math.Sin(p * Math.PI * 4) * 0.22 * (1 - p * 0.6);
        }

        if (visual.WiggleTime > 0)
        {
            var p = Progress(visual.WiggleTime, LanderVisual.WiggleDuration);
            tilt += Math.Sin(p * Math.PI * 6) * 0.1 * (1 - p);
            hop += Math.Abs(Math.Sin(p * Math.PI * 3)) * 2.5 * (1 - p);
        }

        // Con el motor fuerte, la nave vibra un poco (nada que se note en la física).
        var shake = visual.MainFlame > 0.35 ? (visual.MainFlame - 0.35) / 0.65 : 0;
        var jitter = new Vec2(Math.Sin(now * 97) * 0.35, Math.Cos(now * 83) * 0.25) * shake;

        _scene.Offset = new Vec2(jitter.X, jitter.Y - hop);
        _scene.Tilt = tilt;
        _scene.Pose = _scene.Pose with { Compress = compress, Yawn = Math.Pow(yawn, 0.7), AntennaSway = _antenna };
        UpdateSignal(now);

        // Sombra y resplandor sobre la superficie de debajo.
        var feet = position.Y + body.HalfHeight + compress * body.Scale * 0.2;
        _scene.ShadowY = null;
        _scene.ShadowStrength = 0;
        _scene.GroundGlow = 0;
        var lightTarget = 0.0;
        if (body.State != BodyState.Destroyed && _world.PlatformBelow(position.X, feet - 2) is { } below)
        {
            var gap = Math.Max(0, below.Y - feet);
            var reach = body.Height * 2.2;
            if (gap < reach)
            {
                _scene.ShadowY = below.Y;
                _scene.ShadowStrength = 1 - gap / reach;
                _scene.GroundGlow = visual.MainFlame * Math.Clamp(1 - gap / (body.Height * 1.6), 0, 1);
            }

            // De noche enciende el faro al bajar hacia donde se va a posar (o al pilotarlo a mano).
            var approaching = _game.Mode == ControlMode.Manual || _game.Autopilot.State == AutopilotState.Landing;
            if (_sensors.DarkMode && body.IsFlying && approaching && gap > body.Height * 0.15 && gap < body.Height * 2.3)
            {
                lightTarget = Math.Clamp((body.Height * 2.3 - gap) / (body.Height * 0.6), 0, 1);
                _scene.LightSurfaceY = below.Y;
            }
        }

        // El faro se enciende y se apaga con suavidad (y se apaga solo al posarse).
        var dt = Math.Clamp(now - _lightClock, 0, 0.1);
        _lightClock = now;
        _scene.LandingLight = MathUtil.Damp(_scene.LandingLight, lightTarget, lightTarget > _scene.LandingLight ? 6 : 4, dt);
        if (_scene.LandingLight < 0.01 && lightTarget <= 0)
        {
            _scene.LandingLight = 0;
        }
    }

    private void HideLander()
    {
        _surface.Hide();
        _hit.Hide();
        _hitShown = false;
        _hitKey = (int.MinValue, 0, 0, 0);
    }

    /// <summary>
    /// La zona de clic tiene la forma del cuerpo del lander (girada con él) y solo aparece cuando el cursor se acerca:
    /// así no hay que moverla en cada fotograma de vuelo.
    /// </summary>
    private void UpdateInteraction()
    {
        var body = _game.Body;
        var near = _sensors.Cursor is { } cursor && Vec2.Distance(cursor, body.Position) < body.Height * 3;
        var wanted = _settings.AllowDragging && !_hidden && _exitStart is null && body.State != BodyState.Destroyed && _surface.IsVisible
            && (near || _pressed || _dragging);
        if (!wanted)
        {
            if (_hitShown && !_pressed && !_dragging)
            {
                _hit.Hide();
                _hitShown = false;
                _hitKey = (int.MinValue, 0, 0, 0);
            }

            return;
        }

        var size = Math.Max(16, (int)Math.Ceiling(body.Height * 1.5));
        var key = ((int)Math.Round(body.Position.X), (int)Math.Round(body.Position.Y), size, (int)(body.Angle * 50));
        if (key == _hitKey && _hitShown)
        {
            return;
        }

        _hitKey = key;
        _hitShown = true;
        var halfWidth = body.HalfWidth * 1.1;
        var halfHeight = body.HalfHeight;
        var corners = new[] { new Vec2(-halfWidth, -halfHeight), new Vec2(halfWidth, -halfHeight), new Vec2(halfWidth, halfHeight), new Vec2(-halfWidth, halfHeight) };
        _hit.Place(new HitShape.Polygon(corners.Select(c => body.Position + Rotate(c, body.Angle)).Select(p => new Point(p.X, p.Y)).ToList()));
    }

    private void UpdateMarkers(double now)
    {
        _markerSpecs.Clear();
        var body = _game.Body;
        if (!_hidden && _exitStart is null && _game.Mode == ControlMode.Manual && _game.Races.Current is { IsActive: true } race)
        {
            AddRaceMarkers(race, body);
        }
        else if (!_hidden && _exitStart is null && _game.Mode == ControlMode.Manual && _game.Missions.Current is { Status: MissionStatus.Active } mission)
        {
            var scale = body.Scale;
            switch (mission.Kind)
            {
                case MissionKind.CollectCrystals:
                    foreach (var crystal in mission.Crystals)
                    {
                        _markerSpecs.Add(new MarkerSpec(MarkerKind.Crystal, crystal, scale));
                    }

                    break;

                case MissionKind.LandOnApp:
                    var target = _world.Platforms.Where(p => p.Kind == PlatformKind.Window && p.AppId == mission.AppId).MaxBy(p => p.Width);
                    if (target is not null)
                    {
                        _markerSpecs.Add(new MarkerSpec(MarkerKind.Beacon, new Vec2(target.CenterX, target.Y), scale, BubbleIcon.App, AppIcon(target.OwnerId)));
                    }

                    break;

                case MissionKind.Delivery:
                    if (_world.GroundOf(mission.TargetMonitor) is { } ground)
                    {
                        _markerSpecs.Add(new MarkerSpec(MarkerKind.Beacon, new Vec2(ground.CenterX, ground.Y), scale, BubbleIcon.Package));
                    }

                    break;

                case MissionKind.RoundTrip when mission.Stage != 1 && !body.IsOnBase:
                    if (_world.NearestGround(body.Position) is { } home)
                    {
                        var x = Math.Clamp(body.Position.X, home.Left + 60 * scale, home.Right - 60 * scale);
                        _markerSpecs.Add(new MarkerSpec(MarkerKind.Beacon, new Vec2(x, home.Y), scale, BubbleIcon.Home));
                    }

                    break;
            }
        }

        // Las balizas que deja para una orden (una ventana «siempre visible»).
        foreach (var mark in _errandMarks)
        {
            _markerSpecs.Add(new MarkerSpec(MarkerKind.Beacon, mark.At, body.Scale, BubbleIcon.Flag));
        }

        if (_markerSpecs.Count == 0)
        {
            if (_markers.HasVisible)
            {
                _markers.HideAll();
            }

            return;
        }

        if (now - _lastMarkerRender >= 1.0 / 30)
        {
            _lastMarkerRender = now;
            _markers.Render(_markerSpecs, now, _sensors.DarkMode);
        }
    }

    /// <summary>La próxima baliza con su número (o la casa, si toca volver) y, más tenue, la siguiente.</summary>
    private void AddRaceMarkers(Race race, LanderBody body)
    {
        var scale = body.Scale;
        var from = body.Position;
        for (var i = race.Next; i < race.Checkpoints.Count && i <= race.Next + 1; i++)
        {
            var checkpoint = race.Checkpoints[i];
            if (checkpoint.IsBase && body.IsOnBase)
            {
                break;
            }

            if (RaceSystem.TargetPoint(_world, checkpoint, from, 60 * scale) is not { } point)
            {
                continue;
            }

            var label = checkpoint.IsBase ? null : (i + 1).ToString(CultureInfo.CurrentCulture);
            var markerScale = i == race.Next ? scale : scale * 0.8;
            _markerSpecs.Add(new MarkerSpec(MarkerKind.Beacon, point, markerScale, BubbleIcon.Home, Label: label, Alpha: i == race.Next ? 1 : 0.45));
            from = point;
        }
    }

    /// <summary>Icono del programa de una ventana (misiones y bocadillos), guardado mientras viva la ventana.</summary>
    private ImageSource? AppIcon(long handle)
    {
        if (handle == 0)
        {
            return null;
        }

        if (!_appIcons.TryGetValue(handle, out var icon))
        {
            if (_appIcons.Count > 64)
            {
                _appIcons.Clear();
            }

            icon = _host.Windows.AppIcon(handle);
            _appIcons[handle] = icon;
        }

        return icon;
    }

    /// <summary>Parpadeo: unas 13 veces por minuto, a intervalos irregulares.</summary>
    private static double BlinkAt(double time)
    {
        var cycle = Math.Floor(time / BlinkPeriod);
        var phase = time - BlinkStart(cycle);
        return phase is >= 0 and < BlinkLength ? 1 : 0;
    }

    /// <summary>Próximo instante en que el ojo se cierra o se abre (para despertar justo entonces).</summary>
    private static double NextBlinkEdge(double time)
    {
        var cycle = Math.Floor(time / BlinkPeriod);
        for (var k = 0; k < 3; k++)
        {
            var start = BlinkStart(cycle + k);
            if (start > time)
            {
                return start;
            }

            if (start + BlinkLength > time)
            {
                return start + BlinkLength;
            }
        }

        return time + BlinkPeriod;
    }

    private static double BlinkStart(double cycle)
    {
        var offset = (Math.Sin(cycle * 12.9898) * 43758.5453 % 1 + 1) % 1 * 2.5;
        return cycle * BlinkPeriod + offset;
    }
}
