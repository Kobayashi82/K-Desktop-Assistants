using Lander.Core.Geometry;
using Lander.Core.Terrain;

namespace Lander.Core.Physics;

/// <summary>
/// Integra el movimiento del lander con paso fijo. Tanto el jugador como la IA solo le piden empuje
/// mediante <see cref="ThrustInput"/>, así que los dos se comportan exactamente igual.
/// </summary>
public sealed class PhysicsEngine(PhysicsConfig config)
{
    private const double ContactEpsilon = 0.75;
    private const double MaxHeldSpeed = 1400;
    private const double LaunchDamping = 0.75;
    private const double MaxFollowJump = 250;
    private const double MaxPlatformSpeed = 3000;

    public PhysicsConfig Config { get; set; } = config;

    public void Step(LanderBody body, in ThrustInput input, World world, double dt, List<PhysicsEvent> events)
    {
        body.StateTime += dt;
        body.BoostCooldown = Math.Max(0, body.BoostCooldown - dt);

        switch (body.State)
        {
            case BodyState.Flying:
                StepFlying(body, input, world, dt, events);
                break;
            case BodyState.Landed:
                StepLanded(body, input, world, dt, events);
                break;
            case BodyState.Held:
            case BodyState.Destroyed:
                break;
        }
    }

    /// <summary>Mueve el lander hacia el cursor mientras se arrastra; se balancea como un péndulo.</summary>
    public void StepHeld(LanderBody body, Vec2 target, double dt)
    {
        if (body.State != BodyState.Held || dt <= 0)
        {
            return;
        }

        body.StateTime += dt;
        var rawVelocity = (target - body.Position) / dt;
        var previous = body.HeldVelocity;
        body.HeldVelocity = MathUtil.Damp(previous, rawVelocity, 18, dt).ClampLength(MaxHeldSpeed * body.Scale);
        var acceleration = (body.HeldVelocity - previous) / dt;

        var swing = Math.Clamp(-acceleration.X / body.Height * 0.25, -60, 60);
        body.AngularVelocity += (swing - 40 * body.Angle - 5 * body.AngularVelocity) * dt;
        body.Angle = Math.Clamp(body.Angle + body.AngularVelocity * dt, -1.2, 1.2);
        body.Position = target;
        body.Velocity = body.HeldVelocity;
        body.LegsDeploy = MathUtil.MoveTowards(body.LegsDeploy, 1, Config.LegDeployRate * dt);
    }

    /// <summary>Suelta el lander con la velocidad que llevaba el ratón.</summary>
    public static void Release(LanderBody body)
    {
        if (body.State == BodyState.Held)
        {
            body.Fly(body.HeldVelocity.ClampLength(900 * body.Scale));
        }
    }

    private void StepFlying(LanderBody body, in ThrustInput input, World world, double dt, List<PhysicsEvent> events)
    {
        var config = Config;
        var scale = body.Scale;
        var hasFuel = body.Fuel > 0;
        var thrustFactor = 1 - config.MaxDamageThrustPenalty * Math.Clamp(body.Damage, 0, 1);
        var burn = 0.0;

        UpdateLegs(body, input.RetractLegs ? 0 : 1, dt, events);

        // Rotación
        var angularAcceleration = 0.0;
        var rotate = Math.Clamp(input.Rotate, -1, 1);
        if (hasFuel && rotate != 0)
        {
            angularAcceleration += rotate * config.RotationAcceleration;
            burn += Math.Abs(rotate) * config.RotationBurnRate * dt;
        }

        // Sin combustible no hay propulsores, pero los giróscopos (eléctricos) siguen enderezando la nave.
        if (input.Stabilize || !hasFuel)
        {
            angularAcceleration += -body.Angle * config.StabilizerStiffness - body.AngularVelocity * config.StabilizerDamping;
        }

        var angularVelocity = body.AngularVelocity + angularAcceleration * dt;
        angularVelocity *= Math.Max(0, 1 - config.AngularDrag * dt);
        body.AngularVelocity = Math.Clamp(angularVelocity, -config.MaxAngularSpeed, config.MaxAngularSpeed);
        body.Angle = MathUtil.WrapAngle(body.Angle + body.AngularVelocity * dt);

        // Traslación
        var acceleration = new Vec2(0, config.Gravity * scale) + body.ExternalAcceleration;
        var throttle = Math.Clamp(input.Throttle, 0, 1);
        if (hasFuel && throttle > 0)
        {
            acceleration += body.Up * (throttle * config.MainThrust * scale * thrustFactor);
            burn += throttle * config.MainBurnRate * dt;
        }

        var lateral = Math.Clamp(input.Lateral, -1, 1);
        if (hasFuel && lateral != 0)
        {
            acceleration += new Vec2(lateral * config.LateralThrust * scale * thrustFactor, 0);
            burn += Math.Abs(lateral) * config.LateralBurnRate * dt;
        }

        // Sin combustible cae como cualquier otra cosa: solo el motor lo sostiene.
        var velocity = (body.Velocity + acceleration * dt) * Math.Max(0, 1 - config.LinearDrag * dt);

        if (input.Boost && body.BoostCooldown <= 0 && body.Fuel >= config.BoostFuelCost * 0.5)
        {
            velocity += body.Up * (config.BoostImpulse * scale);
            burn += config.BoostFuelCost;
            body.BoostCooldown = config.BoostCooldown;
            events.Add(new PhysicsEvent(PhysicsEventKind.Boosted));
        }

        ConsumeFuel(body, burn, events);

        var previous = body.Position;
        body.Velocity = velocity;
        body.Position = previous + velocity * dt;

        ResolveBarriers(body, previous, world, events);
        if (ResolvePlatforms(body, previous, world, input.LandOn, events))
        {
            return;
        }

        EnsureInBounds(body, world, events);
    }

    private void StepLanded(LanderBody body, in ThrustInput input, World world, double dt, List<PhysicsEvent> events)
    {
        var config = Config;
        var scale = body.Scale;
        var support = body.Support!;
        var gravity = config.Gravity * scale;

        UpdateLegs(body, 1, dt, events);

        if (!world.TryGetOwnerOrigin(support.OwnerId, out var origin))
        {
            body.Fly(support.Velocity.WithY(Math.Max(0, support.Velocity.Y)));
            events.Add(new PhysicsEvent(PhysicsEventKind.SupportLost, Platform: support.Platform));
            return;
        }

        // Muestra nueva del movimiento de la plataforma.
        var detachCandidateVy = body.Velocity.Y + gravity * dt;
        if (world.Timestamp != support.SampleTime)
        {
            var elapsed = world.Timestamp - support.SampleTime;
            var delta = origin - support.Origin;
            support.Origin = origin;
            support.SampleTime = world.Timestamp;

            // Un salto brusco (maximizar, restaurar, mover a otro monitor) es un teletransporte, no un
            // movimiento: la ventana desaparece de debajo y el lander se queda donde estaba.
            if (delta.Length > MaxFollowJump * scale)
            {
                body.Fly(Vec2.Zero);
                events.Add(new PhysicsEvent(PhysicsEventKind.SupportLost, Platform: support.Platform));
                return;
            }

            if (elapsed > 0 && elapsed < 0.5)
            {
                var previousVelocity = support.Velocity;
                var rawVelocity = (delta / elapsed).ClampLength(MaxPlatformSpeed * scale);
                support.Velocity = MathUtil.Damp(previousVelocity, rawVelocity, 12, elapsed);
                support.Acceleration = (support.Velocity - previousVelocity) / elapsed;
                detachCandidateVy = body.Velocity.Y + gravity * elapsed;
            }
            else
            {
                support.Velocity = Vec2.Zero;
                support.Acceleration = Vec2.Zero;
            }
        }
        else
        {
            support.Acceleration = Vec2.Zero;
        }

        var platformVelocity = support.Velocity;

        // Horizontal: las patas agarran hasta cierto límite; más allá, resbala.
        double x;
        var dv = platformVelocity.X - body.Velocity.X;
        var grip = config.GripAcceleration * scale * dt;
        if (Math.Abs(dv) <= grip)
        {
            body.Velocity = body.Velocity.WithX(platformVelocity.X);
            x = origin.X + support.LocalX;
        }
        else
        {
            var vx = body.Velocity.X + Math.Sign(dv) * grip;
            body.Velocity = body.Velocity.WithX(vx);
            x = body.Position.X + vx * dt;
            support.LocalX = x - origin.X;
            events.Add(new PhysicsEvent(PhysicsEventKind.Slipped, Math.Abs(dv)));
        }

        var segment = world.FindSegment(support.OwnerId, x);
        if (segment is null)
        {
            body.Position = body.Position.WithX(x);
            body.Fly(body.Velocity.WithY(Math.Max(0, platformVelocity.Y)));
            events.Add(new PhysicsEvent(PhysicsEventKind.SupportLost, Platform: support.Platform));
            return;
        }

        support.Platform = segment;

        // Vertical: si la plataforma cae más rápido que la gravedad, o frena en seco al subir, se separa. El
        // contenido de una ventana no empuja como una ventana arrastrada: al subir con el scroll y pararse, el
        // lander sigue apoyado; si baja más deprisa que la gravedad, sí se separa.
        var separates = detachCandidateVy < platformVelocity.Y - config.DetachTolerance * scale;
        if (separates && segment.Kind == PlatformKind.Content && platformVelocity.Y <= 0)
        {
            separates = false;
        }

        if (separates)
        {
            var launch = Math.Clamp(detachCandidateVy * LaunchDamping, -config.MaxLaunchSpeed * scale, config.MaxLaunchSpeed * scale);
            body.Position = new Vec2(x, segment.Y - body.HalfHeight - 1);
            body.Fly(new Vec2(body.Velocity.X, launch));
            events.Add(new PhysicsEvent(launch < 0 ? PhysicsEventKind.Launched : PhysicsEventKind.SupportLost, Math.Abs(launch), segment));
            return;
        }

        body.Position = new Vec2(x, segment.Y - body.HalfHeight);
        body.Velocity = body.Velocity.WithY(platformVelocity.Y);

        // Balanceo: la aceleración de la ventana inclina la nave (terremoto).
        var sway = Math.Clamp(-support.Acceleration.X / body.Height * config.SwayFactor, -90, 90);
        body.AngularVelocity += (sway - config.LandedStiffness * body.Angle - config.LandedDamping * body.AngularVelocity) * dt;
        body.Angle += body.AngularVelocity * dt;
        if (Math.Abs(body.Angle) > config.ToppleAngle)
        {
            var side = Math.Sign(body.Angle);
            body.Position = body.Position.WithY(body.Position.Y - 1);
            body.Fly(new Vec2(platformVelocity.X + side * 60 * scale, Math.Min(platformVelocity.Y, 0) - 90 * scale));
            body.AngularVelocity = side * 2.5;
            events.Add(new PhysicsEvent(PhysicsEventKind.Toppled, Platform: segment));
            return;
        }

        // Base: repostar y reparar. En cualquier otro sitio, una recarga muy lenta para no quedarse atascado.
        if (segment.IsBase)
        {
            body.Fuel = Math.Min(body.FuelCapacity, body.Fuel + config.BaseRefuelRate * dt);
            body.Damage = Math.Max(0, body.Damage - config.BaseRepairRate * dt);
        }
        else
        {
            body.Fuel = Math.Min(body.FuelCapacity, body.Fuel + config.TrickleRefuelRate * dt);
        }

        // Dejarse caer: atraviesa la ventana o el borde de contenido en el que está (la base, no).
        if (input.DropThrough && !segment.IsBase)
        {
            body.PassThrough = segment.OwnerId;
            body.Fly(new Vec2(body.Velocity.X, Math.Max(0, platformVelocity.Y) + 70 * scale));
            body.Position = body.Position.WithY(body.Position.Y + 2 * scale);
            events.Add(new PhysicsEvent(PhysicsEventKind.TookOff, Platform: segment));
            return;
        }

        // Despegue
        var hasFuel = body.Fuel > 0;
        var thrustFactor = 1 - config.MaxDamageThrustPenalty * Math.Clamp(body.Damage, 0, 1);
        var lift = hasFuel ? Math.Clamp(input.Throttle, 0, 1) * config.MainThrust * scale * thrustFactor * Math.Cos(body.Angle) : 0;
        var canBoost = input.Boost && body.BoostCooldown <= 0 && body.Fuel >= config.BoostFuelCost * 0.5;
        if (canBoost || lift > gravity * 1.02)
        {
            body.Fly(body.Velocity);
            events.Add(new PhysicsEvent(PhysicsEventKind.TookOff, Platform: segment));
            StepFlying(body, input, world, dt, events);
        }
    }

    private void UpdateLegs(LanderBody body, double target, double dt, List<PhysicsEvent> events)
    {
        var before = body.LegsDeploy;
        body.LegsDeploy = MathUtil.MoveTowards(before, target, Config.LegDeployRate * dt);
        if (before < 1 && body.LegsDeploy >= 1)
        {
            events.Add(new PhysicsEvent(PhysicsEventKind.LegsDeployed));
        }
        else if (before > 0 && body.LegsDeploy <= 0)
        {
            events.Add(new PhysicsEvent(PhysicsEventKind.LegsRetracted));
        }
    }

    private static void ConsumeFuel(LanderBody body, double amount, List<PhysicsEvent> events)
    {
        if (amount <= 0 || body.Fuel <= 0)
        {
            return;
        }

        body.Fuel = Math.Max(0, body.Fuel - amount);
        if (body.Fuel <= 0)
        {
            events.Add(new PhysicsEvent(PhysicsEventKind.FuelEmpty));
        }
    }

    private void ResolveBarriers(LanderBody body, Vec2 previous, World world, List<PhysicsEvent> events)
    {
        var halfWidth = body.HalfWidth;
        var halfHeight = body.HalfHeight;

        foreach (var barrier in world.Barriers)
        {
            var position = body.Position;
            var velocity = body.Velocity;
            switch (barrier.Kind)
            {
                case BarrierKind.Ceiling:
                    if (barrier.Spans(position.X) && previous.Y - halfHeight >= barrier.Position - ContactEpsilon
                        && position.Y - halfHeight < barrier.Position)
                    {
                        body.Position = position.WithY(barrier.Position + halfHeight);
                        if (velocity.Y < 0)
                        {
                            body.Velocity = velocity.WithY(-velocity.Y * Config.CeilingRestitution);
                            events.Add(new PhysicsEvent(PhysicsEventKind.HitCeiling, -velocity.Y));
                        }
                    }

                    break;

                // En un monitor sin barra de tareas el suelo coincide con el borde inferior: ahí manda la
                // plataforma (para poder posarse), no la red de seguridad.
                case BarrierKind.Floor:
                    if (barrier.Spans(position.X) && previous.Y + halfHeight <= barrier.Position + ContactEpsilon
                        && position.Y + halfHeight > barrier.Position && !HasGroundAt(world, position.X, barrier.Position))
                    {
                        body.Position = position.WithY(barrier.Position - halfHeight);
                        if (velocity.Y > 0)
                        {
                            body.Velocity = velocity.WithY(-velocity.Y * Config.CeilingRestitution);
                            events.Add(new PhysicsEvent(PhysicsEventKind.HitWall, velocity.Y));
                        }
                    }

                    break;

                case BarrierKind.LeftWall:
                    if (barrier.Spans(position.Y) && previous.X - halfWidth >= barrier.Position - ContactEpsilon
                        && position.X - halfWidth < barrier.Position)
                    {
                        body.Position = position.WithX(barrier.Position + halfWidth);
                        if (velocity.X < 0)
                        {
                            body.Velocity = velocity.WithX(-velocity.X * Config.WallRestitution);
                            events.Add(new PhysicsEvent(PhysicsEventKind.HitWall, -velocity.X));
                        }
                    }

                    break;

                case BarrierKind.RightWall:
                    if (barrier.Spans(position.Y) && previous.X + halfWidth <= barrier.Position + ContactEpsilon
                        && position.X + halfWidth > barrier.Position)
                    {
                        body.Position = position.WithX(barrier.Position - halfWidth);
                        if (velocity.X > 0)
                        {
                            body.Velocity = velocity.WithX(-velocity.X * Config.WallRestitution);
                            events.Add(new PhysicsEvent(PhysicsEventKind.HitWall, velocity.X));
                        }
                    }

                    break;
            }
        }
    }

    private static bool HasGroundAt(World world, double x, double y)
    {
        foreach (var platform in world.Platforms)
        {
            if (platform.Kind == PlatformKind.Ground && Math.Abs(platform.Y - y) < 1 && platform.ContainsX(x))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Detecta el cruce de las patas con una plataforma (solo desde arriba) y resuelve el aterrizaje.</summary>
    private bool ResolvePlatforms(LanderBody body, Vec2 previous, World world, long landOn, List<PhysicsEvent> events)
    {
        var halfHeight = body.HalfHeight;
        var previousFeet = previous.Y + halfHeight;
        var feet = body.Position.Y + halfHeight;
        var x = body.Position.X;

        // La que atraviesa vuelve a contar en cuanto queda por encima de las patas, se aleja a un lado o se vuelve a subir.
        if (body.PassThrough != 0 && !world.SegmentsOf(body.PassThrough)
                .Any(p => p.ContainsX(x, -body.HalfWidth) && feet > p.Y - ContactEpsilon && feet < p.Y + body.Height))
        {
            body.PassThrough = 0;
        }

        if (body.Velocity.Y < 0)
        {
            return false;
        }

        Platform? hit = null;
        foreach (var platform in world.Platforms)
        {
            if ((landOn != ThrustInput.AnyPlatform && platform.OwnerId != landOn && platform.Kind != PlatformKind.Ground)
                || platform.OwnerId == body.PassThrough)
            {
                continue;
            }

            if (previousFeet <= platform.Y + ContactEpsilon && feet >= platform.Y && platform.ContainsX(x)
                && (hit is null || platform.Y < hit.Y))
            {
                hit = platform;
            }
        }

        if (hit is null)
        {
            return false;
        }

        var foot = body.FootOffset;
        var bothFeet = x - foot >= hit.Left && x + foot <= hit.Right;
        var landing = LandingEvaluator.Evaluate(
            body.Velocity.X, body.Velocity.Y, body.Angle, body.LegsDeploy >= 0.6, bothFeet, Config, body.Scale);

        body.Position = body.Position.WithY(hit.Y - halfHeight);
        if (landing.Quality == LandingQuality.Crash)
        {
            body.Damage = Math.Min(1, body.Damage + Config.CrashDamage);
            body.Destroy();
            events.Add(new PhysicsEvent(PhysicsEventKind.Crashed, landing.VerticalSpeed, hit, landing));
            return true;
        }

        if (landing.Quality == LandingQuality.Hard)
        {
            body.Damage = Math.Min(1, body.Damage + Config.HardLandingDamage);
        }

        var angularVelocity = body.AngularVelocity;
        body.Land(hit, world.Timestamp);
        body.AngularVelocity = angularVelocity * 0.3;
        events.Add(new PhysicsEvent(PhysicsEventKind.Touchdown, landing.VerticalSpeed, hit, landing));
        return true;
    }

    private static void EnsureInBounds(LanderBody body, World world, List<PhysicsEvent> events)
    {
        if (world.IsEmpty || world.MonitorAt(body.Position) is not null)
        {
            return;
        }

        var monitor = world.NearestMonitor(body.Position)!;
        var bounds = monitor.Bounds;
        var halfWidth = body.HalfWidth;
        var halfHeight = body.HalfHeight;
        var x = Math.Clamp(body.Position.X, bounds.Left + halfWidth, Math.Max(bounds.Left + halfWidth, bounds.Right - halfWidth));
        var y = Math.Clamp(body.Position.Y, bounds.Top + halfHeight, Math.Max(bounds.Top + halfHeight, bounds.Bottom - halfHeight));
        var velocity = body.Velocity;
        if (x != body.Position.X)
        {
            velocity = velocity.WithX(0);
        }

        if (y != body.Position.Y)
        {
            velocity = velocity.WithY(0);
        }

        body.Position = new Vec2(x, y);
        body.Velocity = velocity;
        events.Add(new PhysicsEvent(PhysicsEventKind.OutOfBounds));
    }
}
