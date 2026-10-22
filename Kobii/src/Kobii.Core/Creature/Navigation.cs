using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Creature;

/// <summary>Cómo pasa de un sitio a otro.</summary>
public enum MoveKind
{
    /// <summary>Andando por la misma plataforma.</summary>
    Walk,

    /// <summary>Un salto preparado (el arco se calcula y se comprueba con la física de verdad).</summary>
    Jump,

    /// <summary>Trepa por el lado de una ventana hasta su barra de título.</summary>
    ClimbUp,

    /// <summary>Se deja caer desde el borde de su plataforma (de un saltito).</summary>
    Drop,

    /// <summary>Se descuelga de la barra de título (donde esté, sin ir al borde) y se suelta.</summary>
    HangDrop,
}

/// <summary>Un tramo de una ruta.</summary>
/// <param name="Kind">Cómo se hace.</param>
/// <param name="To">Plataforma a la que llega.</param>
/// <param name="StartX">Dónde empieza (dónde coge impulso, dónde se agarra a la ventana…).</param>
/// <param name="EndX">Dónde acaba.</param>
/// <param name="Velocity">Velocidad del salto o de la caída (en el mundo).</param>
/// <param name="EdgeX">Borde de la ventana por el que trepa.</param>
/// <param name="Side">Hacia dónde está la ventana desde donde trepa (−1 o 1), o hacia dónde salta.</param>
/// <param name="Window">Ventana por la que trepa.</param>
public sealed record RouteStep(MoveKind Kind, Platform To, double StartX, double EndX, Vec2 Velocity = default, double EdgeX = 0, int Side = 1, long Window = 0);

/// <summary>Lo que es capaz de hacer para moverse (según su edad y si sabe trepar).</summary>
/// <param name="JumpHeight">Lo más alto que salta (píxeles).</param>
/// <param name="JumpDistance">Lo más lejos que salta (píxeles).</param>
/// <param name="Climb">Sabe trepar por las ventanas y saltar entre ellas (si no, solo puede bajar).</param>
/// <param name="Walls">Tiene las manos libres para trepar por los lados (si lleva algo, solo salta y baja).</param>
public readonly record struct Reach(double JumpHeight, double JumpDistance, bool Climb, bool Walls = true);

/// <summary>
/// Planificador de saltos y rutas sobre el terreno (como el piloto automático de Lander): calcula el arco de
/// cada salto y lo comprueba simulando la física de verdad paso a paso, y busca el camino más corto entre
/// plataformas andando, saltando, trepando por los lados de las ventanas o dejándose caer.
/// </summary>
public static class Navigation
{
    /// <summary>
    /// Al trepar, el borde de la ventana le queda a esta fracción de su media anchura del centro: el borde es una
    /// pared (como en un juego de plataformas) y él se pega a ella de lado, con las manos y los pies en la pared.
    /// </summary>
    public const double ClimbOffset = 0.95;

    /// <summary>Al descolgarse, los pies le quedan a esta profundidad bajo la barra (en alturas del cuerpo).</summary>
    public const double HangDepth = 0.85;

    private const double StepSeconds = 1.0 / 60;

    /// <summary>
    /// Velocidad para saltar desde <paramref name="takeoffX"/> (en <paramref name="from"/>) hasta
    /// <paramref name="landingX"/> en <paramref name="to"/>, o null si no se puede (no llega, se da con el
    /// techo o caería en otra plataforma por el camino).
    /// </summary>
    public static Vec2? PlanJump(World world, CreatureBody body, Platform from, double takeoffX, Platform to, double landingX, Reach reach)
    {
        var scale = body.Scale;
        var h = body.Height;
        var g = CreatureBody.GravityDip * scale;
        var k = CreatureBody.AirDrag;
        var dx = landingX - takeoffX;
        if (Math.Abs(dx) > reach.JumpDistance)
        {
            return null;
        }

        // Cima del arco: por encima de lo más alto, con más margen cuanto más lejos.
        var apex = Math.Min(from.Y, to.Y) - Math.Max(h * 0.6, Math.Abs(dx) * 0.22);
        if (world.MonitorAt(new Vec2(takeoffX, from.Y - 1)) is { } monitor)
        {
            apex = Math.Max(apex, monitor.Bounds.Top + h * 1.15);
        }

        var rise = from.Y - apex;
        if (rise > reach.JumpHeight || apex > Math.Min(from.Y, to.Y) - h * 0.25)
        {
            return null;
        }

        var vy = -Math.Sqrt(2 * g * rise);
        var flight = -vy / g + Math.Sqrt(2 * Math.Max(0, to.Y - apex) / g);
        var vx = dx * k / (1 - Math.Exp(-k * flight));

        // Se afina con la física de verdad: donde cae, se corrige la velocidad.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (Simulate(world, body, new Vec2(takeoffX, from.Y), new Vec2(vx, vy)) is not { } landing)
            {
                return null;
            }

            if (landing.Platform.OwnerId != to.OwnerId || !to.ContainsX(landing.X))
            {
                if (attempt > 2)
                {
                    return null;
                }
            }
            else if (Math.Abs(landing.X - landingX) < h * 0.35)
            {
                return new Vec2(vx, vy);
            }

            vx -= (landing.X - landingX) / Math.Max(0.2, landing.Seconds) * 1.05;
        }

        return null;
    }

    /// <summary>Dónde cae algo lanzado desde <paramref name="feet"/> (la misma física que el cuerpo).</summary>
    public static (Platform Platform, double X, double Seconds)? Simulate(World world, CreatureBody body, Vec2 feet, Vec2 velocity, double maxSeconds = 4)
    {
        var probe = new CreatureBody(body.HeightDip, body.Scale) { Feet = feet };
        probe.Launch(velocity);
        for (var t = 0.0; t < maxSeconds; t += StepSeconds)
        {
            if (probe.Step(world, StepSeconds) is { } landing)
            {
                return (landing.Platform, probe.Feet.X, t);
            }
        }

        return null;
    }

    /// <summary>La trayectoria de algo lanzado desde <paramref name="feet"/> (para dibujar la puntería).</summary>
    public static List<Vec2> Trajectory(World world, CreatureBody body, Vec2 feet, Vec2 velocity, double maxSeconds = 2.5)
    {
        var probe = new CreatureBody(body.HeightDip, body.Scale) { Feet = feet };
        probe.Launch(velocity);
        var points = new List<Vec2> { feet };
        for (var t = 0.0; t < maxSeconds; t += StepSeconds)
        {
            var landed = probe.Step(world, StepSeconds) is not null;
            points.Add(probe.Feet);
            if (landed)
            {
                break;
            }
        }

        return points;
    }

    /// <summary>
    /// Ruta desde donde está hasta <paramref name="targetX"/> en <paramref name="target"/>: el camino más corto
    /// combinando andar, saltar, trepar y dejarse caer. Null si no hay forma de llegar.
    /// </summary>
    public static List<RouteStep>? Route(World world, CreatureBody body, Platform target, double targetX, Reach reach, int maxMoves = 5)
    {
        if (body.Support is not { } start || !body.IsGrounded)
        {
            return null;
        }

        var margin = body.HalfWidth + 6 * body.Scale;
        targetX = Math.Clamp(targetX, target.Left + margin, Math.Max(target.Left + margin, target.Right - margin));
        if (Same(start, target))
        {
            return [new RouteStep(MoveKind.Walk, target, body.Feet.X, targetX)];
        }

        // Dijkstra sobre las plataformas (son pocas): cada movimiento cuesta su tiempo aproximado.
        var best = new Dictionary<Platform, (double Cost, double X, Platform? From, RouteStep? Step, int Moves)>(ReferenceEqualityComparer.Instance)
        {
            [start] = (0, body.Feet.X, null, null, 0),
        };
        var open = new PriorityQueue<Platform, double>();
        open.Enqueue(start, 0);
        var walkSpeed = Math.Max(1, body.Height * 1.2);
        while (open.TryDequeue(out var platform, out var cost))
        {
            var node = best[platform];
            if (cost > node.Cost + 1e-9)
            {
                continue;
            }

            if (Same(platform, target))
            {
                break;
            }

            if (node.Moves >= maxMoves)
            {
                continue;
            }

            foreach (var step in Moves(world, body, platform, node.X, reach))
            {
                var walk = Math.Abs(step.StartX - node.X) / walkSpeed;
                var effort = step.Kind switch
                {
                    MoveKind.Jump => 1.2,
                    MoveKind.ClimbUp => 1 + Math.Abs(platform.Y - step.To.Y) / (body.Height * 1.3),
                    MoveKind.HangDrop => 1.6,
                    _ => 0.8,
                };
                // Al llegar a la plataforma de destino, cuenta también lo que le queda por andar hasta el sitio.
                var rest = Same(step.To, target) ? Math.Abs(step.EndX - targetX) / walkSpeed : 0;
                var total = cost + walk + effort + rest;
                var key = Canonical(world, step.To);
                if (!best.TryGetValue(key, out var known) || total < known.Cost)
                {
                    best[key] = (total, step.EndX, platform, step, node.Moves + 1);
                    open.Enqueue(key, total);
                }
            }
        }

        var goal = best.Keys.FirstOrDefault(p => Same(p, target));
        if (goal is null)
        {
            return null;
        }

        var route = new List<RouteStep>();
        for (var p = goal; best[p].Step is { } step; p = best[p].From!)
        {
            route.Add(step);
        }

        route.Reverse();
        route.Add(new RouteStep(MoveKind.Walk, target, route[^1].EndX, targetX));
        return route;
    }

    /// <summary>Lo que puede hacer desde una plataforma: saltar a otras, trepar por una ventana o dejarse caer.</summary>
    private static IEnumerable<RouteStep> Moves(World world, CreatureBody body, Platform from, double x, Reach reach)
    {
        var h = body.Height;
        var margin = body.HalfWidth + 6 * body.Scale;
        var monitor = from.MonitorIndex;

        // Dejarse caer por cada extremo (siempre se puede, aunque no sepa trepar).
        foreach (var side in new[] { -1, 1 })
        {
            var edge = side < 0 ? from.Left : from.Right;
            var outside = edge + side * body.HalfWidth * 1.3;
            if (from.IsBase || world.PlatformBelow(outside, from.Y + 1) is not { } below || Neighbour(world, from, outside, h))
            {
                continue;
            }

            // Se acerca al borde todo lo que puede andando (hasta tenerlo a 0,8 medias anchuras) y salta hacia fuera.
            yield return new RouteStep(MoveKind.Drop, below, edge - side * body.HalfWidth * 0.85, outside, DropVelocity(body, side, body.HalfWidth * 0.85), EdgeX: edge, Side: side);
        }

        // Descolgarse de la barra de título (con las manos libres, aunque no sepa trepar): donde está o encima de
        // alguna plataforma de más abajo (por ejemplo, con dos ventanas una al lado de la otra que ocupan todo el
        // monitor, por los bordes no hay por dónde bajar).
        if (!from.IsBase && reach.Walls)
        {
            var spots = new List<double> { x };
            foreach (var p in world.Platforms)
            {
                if (p.Y > from.Y + h && p.Left < from.Right && from.Left < p.Right)
                {
                    spots.Add(Math.Clamp(x, p.Left + margin, Math.Max(p.Left + margin, p.Right - margin)));
                }
            }

            var targets = new List<Platform>();
            foreach (var spot in spots)
            {
                var at = Math.Clamp(spot, from.Left + margin, Math.Max(from.Left + margin, from.Right - margin));
                if (world.PlatformBelow(at, from.Y + 1) is not { } below || below.Y < from.Y + (HangDepth + 0.3) * h
                    || !below.ContainsX(at, body.HalfWidth * 0.5) || targets.Any(t => Same(t, below)))
                {
                    continue;
                }

                targets.Add(below);
                yield return new RouteStep(MoveKind.HangDrop, below, at, at, Side: at >= x ? 1 : -1, Window: from.OwnerId);
            }
        }

        if (!reach.Climb)
        {
            yield break;
        }

        // Saltar a otra plataforma del mismo monitor.
        foreach (var to in world.Platforms)
        {
            if (to.MonitorIndex != monitor || Same(to, from) || to.Width < margin * 2.2)
            {
                continue;
            }

            // Despega del punto de su plataforma más cercano al destino y cae un poco hacia dentro.
            var landing = Math.Clamp(Math.Clamp(x, to.Left + margin * 1.5, to.Right - margin * 1.5), to.Left + margin, to.Right - margin);
            var takeoff = Math.Clamp(landing, from.Left + margin, from.Right - margin);
            if (to.Y < from.Y && WindowOf(world, to) is { } box && box.Bottom >= from.Y - 2
                && takeoff > box.Left - margin && takeoff < box.Right + margin)
            {
                // La ventana llega hasta su suelo (como una caja): despega desde fuera y cae junto a su borde.
                var left = x < (box.Left + box.Right) / 2.0;
                var outside = left ? box.Left - margin : box.Right + margin;
                if (outside > from.Left + margin && outside < from.Right - margin)
                {
                    takeoff = outside;
                    landing = Math.Clamp(left ? to.Left + margin * 1.5 : to.Right - margin * 1.5, to.Left + margin, to.Right - margin);
                }
            }
            else if (Math.Abs(takeoff - landing) < h * 0.2 && to.Y < from.Y)
            {
                // Justo debajo: salta en vertical.
                takeoff = Math.Clamp(landing - Math.Sign(landing - x + 1e-6) * h * 0.4, from.Left + margin, from.Right - margin);
            }

            if (PlanJump(world, body, from, takeoff, to, landing, reach) is { } velocity)
            {
                yield return new RouteStep(MoveKind.Jump, to, takeoff, landing, velocity, Side: Math.Sign(landing - takeoff + 1e-6));
            }
        }

        // Trepar por el lado de una ventana que esté por encima de esta plataforma (con las manos libres).
        if (!reach.Walls)
        {
            yield break;
        }

        foreach (var window in world.Windows)
        {
            var r = window.Bounds;
            if (r.Bottom > from.Y + 2 || from.Y - r.Bottom > reach.JumpHeight || r.Top > from.Y - h * 1.5)
            {
                continue;
            }

            foreach (var side in new[] { 1, -1 })
            {
                // side = 1: trepa por el borde izquierdo (la ventana queda a su derecha).
                var edge = side > 0 ? (double)r.Left : r.Right;
                var start = edge - side * body.HalfWidth * ClimbOffset;
                if (start < from.Left + margin * 0.8 || start > from.Right - margin * 0.8)
                {
                    continue;
                }

                var top = edge + side * h * 0.75;
                var onTop = world.SegmentsOf(window.Handle).FirstOrDefault(p => p.ContainsX(top) && p.Y < from.Y);
                if (onTop is not null && top > onTop.Left + margin * 0.5 && top < onTop.Right - margin * 0.5)
                {
                    yield return new RouteStep(MoveKind.ClimbUp, onTop, start, top, EdgeX: edge, Side: side, Window: window.Handle);
                }
            }
        }
    }

    /// <summary>
    /// El saltito para dejarse caer desde <paramref name="gap"/> píxeles del borde: lo bastante largo para pasar el
    /// borde con holgura antes de volver a bajar a su altura (si no, caería otra vez en la misma plataforma).
    /// </summary>
    public static Vec2 DropVelocity(CreatureBody body, int side, double gap)
    {
        var vy = 220 * body.Scale;
        var airborne = 2 * vy / (GravityDipScaled(body));
        var vx = Math.Max(160 * body.Scale, (gap + body.HalfWidth * 0.6) / airborne * 1.15);
        return new Vec2(side * vx, -vy);
    }

    private static double GravityDipScaled(CreatureBody body) => CreatureBody.GravityDip * body.Scale;

    /// <summary>
    /// Justo pasado el borde hay otra plataforma a casi la misma altura (dos ventanas una al lado de la otra):
    /// el saltito para dejarse caer acabaría en ella, no abajo.
    /// </summary>
    private static bool Neighbour(World world, Platform from, double outside, double h)
    {
        foreach (var p in world.Platforms)
        {
            if (!Same(p, from) && p.Y >= from.Y - h * 0.4 && p.Y <= from.Y + 1 && p.ContainsX(outside, -h * 0.5))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>La misma plataforma (mismo dueño y se solapan), aunque el terreno se haya reconstruido.</summary>
    public static bool Same(Platform a, Platform b) =>
        a.OwnerId == b.OwnerId && Math.Abs(a.Y - b.Y) < 1 && a.Left < b.Right && b.Left < a.Right;

    /// <summary>Los límites de la ventana dueña de una plataforma (null si es el suelo).</summary>
    private static PixelRect? WindowOf(World world, Platform platform)
    {
        if (platform.IsBase)
        {
            return null;
        }

        foreach (var window in world.Windows)
        {
            if (window.Handle == platform.OwnerId)
            {
                return window.Bounds;
            }
        }

        return null;
    }

    /// <summary>La plataforma del mundo que corresponde a otra (para no duplicar nodos).</summary>
    public static Platform Canonical(World world, Platform platform) =>
        world.Platforms.FirstOrDefault(p => Same(p, platform)) ?? platform;
}
