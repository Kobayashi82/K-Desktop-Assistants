using Kumo.Core.Geometry;

namespace Kumo.Core.Cloud;

/// <summary>Cómo se mueve el cuerpo hacia <see cref="KumoCloud.Home"/>.</summary>
public enum Motion
{
    /// <summary>Flota en su sitio, meciéndose despacio.</summary>
    Hover,

    /// <summary>Vuela hasta allí (más deprisa, sin mecerse, soltando nubecitas).</summary>
    Fly,

    /// <summary>Quieto en el punto exacto (pulsar un botón, llevar una ventana).</summary>
    Hold,

    /// <summary>Lo llevas con el ratón: sigue al punto con un muelle duro.</summary>
    Follow,
}

/// <summary>
/// La nube de Kumo: un cúmulo de bolas que nunca tiene dos veces la misma forma (pasa entre cuatro colocaciones y cada bulto
/// se mueve y respira a su ritmo), con un muelle por bola que lo hace blandito al moverse, nubecitas que se separan del cuerpo
/// y se evaporan, y las partículas de lo que hace. Todo con paso fijo (<see cref="TimeStep"/>); el dibujo lo hace otro (ver
/// <see cref="CloudField"/> para el contorno).
/// </summary>
/// <remarks>
/// Las bolas van en unidades de Kumo (<see cref="Unit"/> píxeles cada una) relativas al centro del cuerpo; el cuerpo, las
/// nubecitas y las partículas, en píxeles de pantalla. Es el boceto de Kumo (fase 17) pasado a C#.
/// </remarks>
public sealed class KumoCloud
{
    public const double TimeStep = 1.0 / 120;
    public const int BallCount = 10;

    /// <summary>La bola rectangular (bocadillo, abrazo): índice <see cref="BallCount"/>.</summary>
    public const int Box = BallCount;

    private const int MaxPuffs = 18;
    private const int MaxSteps = 40;

    /// <summary>Las cuatro formas de cúmulo entre las que va pasando: [x, y, radio]; la bola 0 es el núcleo.</summary>
    private static readonly double[][] Variants =
    [
        [0, 0.05, 0.62, -0.52, 0.1, 0.44, 0.54, 0.12, 0.42, -0.31, -0.26, 0.37, 0.31, -0.24, 0.4, -0.86, 0.28, 0.27, 0.88, 0.3, 0.26, 0, 0.36, 0.42, -0.42, 0.36, 0.34, 0.44, 0.38, 0.33],
        [0.05, 0.08, 0.58, -0.6, 0.05, 0.46, 0.62, 0.16, 0.38, -0.51, -0.2, 0.35, 0.14, -0.2, 0.39, -1, 0.26, 0.27, 0.98, 0.32, 0.24, 0.05, 0.36, 0.42, -0.5, 0.36, 0.34, 0.55, 0.38, 0.3],
        [0, 0.06, 0.6, -0.5, 0.16, 0.4, 0.5, 0.04, 0.46, -0.14, -0.18, 0.35, 0.44, -0.36, 0.39, -0.82, 0.32, 0.25, 0.88, 0.26, 0.3, 0, 0.37, 0.42, -0.4, 0.37, 0.33, 0.42, 0.37, 0.34],
        [0, 0.04, 0.64, -0.5, 0.12, 0.42, 0.5, 0.12, 0.42, 0.02, -0.32, 0.39, -0.44, -0.12, 0.32, -0.84, 0.3, 0.26, 0.84, 0.3, 0.26, 0, 0.36, 0.42, -0.42, 0.36, 0.34, 0.42, 0.36, 0.34],
    ];

    /// <summary>Lo más alto que llegan los bultos de arriba (unidades): por debajo de la cúpula del núcleo.</summary>
    private const double HeadTop = -0.42;

    /// <summary>Lo grande que es la cara (unidades: los ojos van a 0,17 × esto del centro).</summary>
    private const double FaceSize = 1.2;

    private static readonly double[] FingerDistance = [0.82, 1.12, 1.42, 1.7, 1.96];
    private static readonly double[] FingerRadius = [0.3, 0.24, 0.205, 0.185, 0.175];

    private readonly IRandom _random;
    private readonly Target[] _targets = new Target[BallCount + 1];
    private readonly Dictionary<string, double> _emitters = [];
    private double _accumulator;
    private Vec2 _previousVelocity;
    private bool _skipAcceleration;
    private double _nextWisp;
    private double _blinkAt;
    private double _blinkLeft;
    private double _seed;

    public KumoCloud(IRandom random, double unit = 44)
    {
        _random = random;
        Unit = unit;
        Time = random.Range(0, 10);
        _seed = random.Range(0, 4);
        for (var i = 0; i < Balls.Length; i++)
        {
            Balls[i] = new Ball();
        }

        var v = Variants[0];
        for (var i = 0; i < BallCount; i++)
        {
            Balls[i].X = v[i * 3];
            Balls[i].Y = v[i * 3 + 1];
            Balls[i].R = v[i * 3 + 2];
        }

        _nextWisp = Time + random.Range(3, 7);
        _blinkAt = Time + 2;
    }

    /// <summary>Píxeles por unidad (el tamaño: unos 44 a escala 1).</summary>
    public double Unit { get; set; }

    public double Time { get; private set; }

    /// <summary>Las bolas (la última, <see cref="Box"/>, es la rectangular).</summary>
    public Ball[] Balls { get; } = new Ball[BallCount + 1];

    public List<Puff> Puffs { get; } = [];

    public ParticleSystem Particles { get; } = new();

    /// <summary>Centro del cuerpo (píxeles).</summary>
    public Vec2 Position { get; private set; }

    public Vec2 Velocity { get; private set; }

    /// <summary>Adónde va (o donde flota).</summary>
    public Vec2 Home { get; set; }

    public Motion Motion { get; private set; } = Motion.Hover;

    /// <summary>Velocidad máxima al volar (píxeles por segundo).</summary>
    public double FlySpeed { get; private set; } = 600;

    public CloudPose Pose { get; private set; } = CloudPose.Idle;

    /// <summary>Segundos desde que cambió de <see cref="Pose"/>.</summary>
    public double PoseTime { get; private set; }

    /// <summary>Cuánto cambia de forma (0…1; el boceto, 0,6).</summary>
    public double Morph { get; set; } = 0.6;

    /// <summary>Cuántas nubecitas suelta al volar (0…1).</summary>
    public double Trail { get; set; } = 0.55;

    /// <summary>Lo blandos que son los bordes (0…1).</summary>
    public double Edge { get; set; } = 0.45;

    /// <summary>A qué apunta el brazo o el dedo (píxeles).</summary>
    public Vec2 Aim { get; set; }

    /// <summary>Lo estirado que está el dedo (1 = llega a 2 unidades del centro).</summary>
    public double Reach { get; set; } = 1;

    /// <summary>Medio ancho y medio alto del bocadillo o del abrazo (unidades).</summary>
    public (double HalfWidth, double HalfHeight) Bubble { get; set; } = (1.5, 0.6);

    /// <summary>El nivel de su voz mientras habla (0…1).</summary>
    public double VoiceLevel { get; set; }

    /// <summary>Lo que oye mientras escucha (0…1); sin micrófono, un latido de mentira.</summary>
    public double HearLevel { get; set; } = -1;

    /// <summary>El cursor (píxeles), para mirarlo.</summary>
    public Vec2? Cursor { get; set; }

    /// <summary>Lo condensado que está (0 = cúmulo con bultos; 1 = gota lisa).</summary>
    public double Smooth { get; private set; }

    /// <summary>Lo blando del borde ahora (lo que se dibuja).</summary>
    public double Soft { get; private set; } = 0.2;

    /// <summary>Lo que se ve el cuerpo (al deshacerse, baja a 0).</summary>
    public double Alpha { get; private set; } = 1;

    /// <summary>Lo gris que está (algo salió mal).</summary>
    public double Tint { get; private set; }

    /// <summary>Lo inclinado que va (radianes, hacia donde vuela).</summary>
    public double Tilt { get; private set; }

    /// <summary>El saltito de contento (píxeles, hacia arriba negativo).</summary>
    public double Bob { get; private set; }

    /// <summary>Dónde se dibuja el centro (la posición con el saltito).</summary>
    public Vec2 RenderPosition => new(Position.X, Position.Y + Bob);

    /// <summary>La cara, relativa a la bola 0 (unidades), y su tamaño.</summary>
    public Vec2 FaceOffset { get; private set; } = new(0, -0.03);

    public double FaceScale { get; private set; } = FaceSize;

    /// <summary>Hacia dónde mira (desplazamiento de los ojos, unidades).</summary>
    public Vec2 Look { get; private set; }

    public CloudEyes Eyes { get; private set; }

    public CloudMouth Mouth { get; private set; }

    /// <summary>Lo cerrados que tiene los párpados al parpadear (0…1).</summary>
    public double Lid { get; private set; }

    /// <summary>Lo que pesa el brillo de gota (0 = mate; 1 = liso y brillante).</summary>
    public double Gloss => Smooth;

    /// <summary>Lo deprisa que va (unidades por segundo).</summary>
    public double Speed => Unit > 0 ? Velocity.Length / Unit : 0;

    /// <summary>Ha llegado adonde iba y casi no se mueve.</summary>
    public bool Arrived => Vec2.Distance(Position, Home) < Math.Max(3, Unit * 0.08) && Speed < 0.8;

    /// <summary>Hay algo moviéndose de forma visible: hace falta dibujar a cada fotograma.</summary>
    public bool IsLively =>
        Motion is Motion.Fly or Motion.Follow || Speed > 0.15 || HasFastPuffs || PoseTime < 0.6
        || Pose is CloudPose.Bubble or CloudPose.Talk or CloudPose.Listen or CloudPose.Point or CloudPose.Press or CloudPose.Wave
            or CloudPose.Happy or CloudPose.Surprise or CloudPose.Dissolve or CloudPose.Tickle or CloudPose.Hug or CloudPose.Gather;

    /// <summary>
    /// Alguna nubecita va deprisa (al volar, en un estallido): las que se evaporan despacio se ven bien a menos fotogramas (con ellas
    /// a cada fotograma, en reposo dibujaba el doble).
    /// </summary>
    public bool HasFastPuffs => Puffs.Exists(p => p.VX * p.VX + p.VY * p.VY > 0.36 * Unit * Unit);

    /// <summary>Lo pone en un sitio de golpe (sin vuelo ni estirones).</summary>
    public void Place(Vec2 position)
    {
        Position = position;
        Home = position;
        Velocity = default;
        _previousVelocity = default;
        _skipAcceleration = true;
    }

    /// <summary>Flota donde está (o en <paramref name="spot"/>).</summary>
    public void Hover(Vec2? spot = null)
    {
        Motion = Motion.Hover;
        if (spot is { } s)
        {
            Home = s;
        }
    }

    /// <summary>Vuela hasta <paramref name="target"/> a <paramref name="speed"/> píxeles por segundo como mucho.</summary>
    public void FlyTo(Vec2 target, double speed)
    {
        Motion = Motion.Fly;
        Home = target;
        FlySpeed = Math.Max(40, speed);
    }

    /// <summary>Quieto en un punto exacto (sin mecerse).</summary>
    public void Hold(Vec2 target)
    {
        Motion = Motion.Hold;
        Home = target;
    }

    /// <summary>Lo llevas con el ratón.</summary>
    public void Follow(Vec2 target)
    {
        Motion = Motion.Follow;
        Home = target;
    }

    public void SetPose(CloudPose pose)
    {
        if (pose == Pose)
        {
            return;
        }

        Pose = pose;
        PoseTime = 0;
        var u = Unit;
        switch (pose)
        {
            case CloudPose.Surprise:
                foreach (var b in Balls.AsSpan(1, BallCount - 1))
                {
                    var a = Math.Atan2(b.Y, b.X);
                    AddPuff(new Vec2(Position.X + b.X * u * 1.2, Position.Y + b.Y * u * 1.2), new Vec2(Math.Cos(a), Math.Sin(a)) * 1.8 * u, 0.12 * u, 0.55);
                }

                break;
            case CloudPose.Happy:
                Burst(ParticleKind.Star, new Vec2(Position.X, Position.Y - 0.3 * u), 12);
                break;
        }
    }

    /// <summary>De golpe deshecho en niebla: lo siguiente que haga, lo hará condensándose (la entrada de «aparecer de la niebla»).</summary>
    public void Condense()
    {
        Alpha = 0;
        Soft = 1.3;
        foreach (var b in Balls.AsSpan(0, BallCount))
        {
            b.X *= 2.1;
            b.Y *= 2.1;
            b.R *= 0.7;
        }

        for (var i = 0; i < 26; i++)
        {
            var a = _random.Range(0, Math.Tau);
            var d = _random.Range(0.6, 1.8) * Unit;
            Particles.Add(new Particle
            {
                Kind = ParticleKind.Mist,
                X = Position.X + Math.Cos(a) * d,
                Y = Position.Y + Math.Sin(a) * d * 0.7,
                VX = -Math.Cos(a) * 0.5 * Unit,
                VY = -Math.Sin(a) * 0.35 * Unit,
                Life = _random.Range(0.9, 1.5),
                Size = _random.Range(0.3, 0.6) * Unit,
                Alpha = 0.22,
            });
        }
    }

    /// <summary>Avanza la nube <paramref name="dt"/> segundos (en pasos de <see cref="TimeStep"/>).</summary>
    public void Step(double dt)
    {
        _accumulator += Math.Max(0, dt);
        var steps = 0;
        while (_accumulator >= TimeStep && steps < MaxSteps)
        {
            StepOnce(TimeStep);
            _accumulator -= TimeStep;
            steps++;
        }

        if (steps == MaxSteps)
        {
            _accumulator = 0;
        }
    }

    private void StepOnce(double dt)
    {
        Time += dt;
        PoseTime += dt;
        var u = Unit;
        var pose = Pose;

        // El cuerpo va hacia su sitio con un muelle; flotando se mece despacio.
        var (k, z) = Motion switch
        {
            Motion.Fly => (28.0, 0.72),
            Motion.Hold => (40.0, 0.95),
            Motion.Follow => (90.0, 0.8),
            _ => (8.0, 0.9),
        };
        var target = Home;
        if (Motion == Motion.Hover)
        {
            target += new Vec2(Math.Sin(Time * 0.63) * 0.12 * u, Math.Sin(Time * 1.07 + 1) * 0.09 * u);
            if (pose == CloudPose.Sleep)
            {
                target += new Vec2(0, 0.25 * u);
            }
        }

        var c = 2 * Math.Sqrt(k) * z;
        var velocity = Velocity + (k * (target - Position) - c * Velocity) * dt;
        if (Motion == Motion.Fly)
        {
            velocity = velocity.ClampLength(FlySpeed);
        }

        Velocity = velocity;
        Position += Velocity * dt;

        var acceleration = (Velocity - _previousVelocity) / dt;
        _previousVelocity = Velocity;
        if (_skipAcceleration)
        {
            acceleration = default;
            _skipAcceleration = false;
        }

        var am = acceleration.Length / u;
        if (am > 60)
        {
            acceleration *= 60 / am;
        }

        Bob = pose == CloudPose.Happy ? -Math.Abs(Math.Sin(PoseTime * 9)) * 0.24 * u * Math.Max(0, 1 - PoseTime / 2.1) : 0;
        Smooth = MathUtil.Lerp(Smooth, SmoothOf(pose), Math.Min(1, dt * 2.4));

        var shape = Shape(pose, out var face, out var faceScale, out var eyes, out var mouth, out var look);
        var speed = Speed;

        // Se inclina hacia donde va y se estira a lo largo del movimiento.
        Tilt = MathUtil.Lerp(Tilt, Math.Clamp(Velocity.X / u * 0.035, -0.28, 0.28), Math.Min(1, dt * 6));
        var ct = Math.Cos(Tilt);
        var st = Math.Sin(Tilt);
        var stretch = speed > 0.25 ? Math.Min(0.45, speed * 0.04) : 0;
        var dir = speed > 0.25 ? Velocity / (speed * u) : default;
        for (var i = 0; i < BallCount; i++)
        {
            ref var t = ref shape[i];
            var x = t.X * ct - t.Y * st;
            var y = t.X * st + t.Y * ct;
            if (stretch > 0)
            {
                var along = x * dir.X + y * dir.Y;
                var px = x - along * dir.X;
                var py = y - along * dir.Y;
                var np = along * (1 + stretch) - stretch * 0.25;
                x = dir.X * np + px * (1 - stretch * 0.4);
                y = dir.Y * np + py * (1 - stretch * 0.4);
            }

            t.X = x;
            t.Y = y;
        }

        // Cada bola sigue a su sitio con su muelle; las de fuera, con más retraso (se queda atrás al acelerar).
        for (var i = 0; i <= BallCount; i++)
        {
            var b = Balls[i];
            var t = shape[i];
            var kk = i == 0 ? 130 : i == Box ? 60 : 72 - i * 3.2;
            var cc = 2 * Math.Sqrt(kk) * (i == Box ? 0.8 : 0.5);
            var lag = i == 0 ? 0.08 : i == Box ? 0.05 : 0.16 + 0.035 * i;
            b.VX += (kk * (t.X - b.X) - cc * b.VX - acceleration.X / u * lag) * dt;
            b.VY += (kk * (t.Y - b.Y) - cc * b.VY - acceleration.Y / u * lag) * dt;
            b.VR += (kk * (t.R - b.R) - cc * b.VR) * dt;
            b.VW += (kk * (t.W - b.HalfWidth) - cc * b.VW) * dt;
            b.VH += (kk * (t.H - b.HalfHeight) - cc * b.VH) * dt;
            b.X += b.VX * dt;
            b.Y += b.VY * dt;
            b.R += b.VR * dt;
            b.HalfWidth += b.VW * dt;
            b.HalfHeight += b.VH * dt;
        }

        // La cara va sobre la bola 0 (se tambalea con el cuerpo).
        var follow = Math.Min(1, dt * 9);
        var rel = new Vec2(face.X - shape[0].X, face.Y - shape[0].Y);
        FaceOffset = Vec2.Lerp(FaceOffset, rel, follow);
        FaceScale = MathUtil.Lerp(FaceScale, faceScale, follow);
        Eyes = eyes;
        Mouth = mouth;
        if (Cursor is { } cursor && pose is CloudPose.Idle or CloudPose.Listen or CloudPose.Happy or CloudPose.Drop or CloudPose.Wait or CloudPose.Tickle)
        {
            var f = FacePosition;
            var m = cursor - f;
            var ml = Math.Max(1e-6, m.Length);
            var near = Math.Min(1, ml / (3 * u));
            look = new Vec2(m.X / ml * 0.07 * near, m.Y / ml * 0.06 * near);
        }
        else if (Motion == Motion.Fly && speed > 0.5)
        {
            look = new Vec2(Velocity.X / (speed * u) * 0.07, Velocity.Y / (speed * u) * 0.05);
        }

        Look = Vec2.Lerp(Look, look, Math.Min(1, dt * 8));

        var softTarget = MathUtil.Lerp(0.06 + Edge * 0.32, 0.07, Smooth * 0.6);
        var alphaTarget = 1.0;
        if (pose == CloudPose.Dissolve && PoseTime < 1.1)
        {
            softTarget = 1.3;
            alphaTarget = 0;
        }

        var rate = Math.Min(1, dt * (pose == CloudPose.Dissolve || Alpha < 0.5 ? 2.6 : 4));
        Soft = MathUtil.Lerp(Soft, softTarget, rate);
        Alpha = MathUtil.Lerp(Alpha, alphaTarget, rate);
        Tint = MathUtil.Lerp(Tint, pose == CloudPose.Error ? 1 : pose == CloudPose.Shrug ? 0.28 : 0, Math.Min(1, dt * 2.5));

        if (Time > _blinkAt)
        {
            _blinkLeft = 0.17;
            _blinkAt = Time + _random.Range(2.4, 5.6);
        }

        _blinkLeft = Math.Max(0, _blinkLeft - dt);
        Lid = _blinkLeft > 0 ? Math.Sin(Math.PI * (1 - _blinkLeft / 0.17)) : 0;

        Emit(dt, speed);
        StepPuffs(dt);
        Particles.Update(dt, u);
    }

    /// <summary>Dónde está la cara en pantalla (píxeles).</summary>
    public Vec2 FacePosition
    {
        get
        {
            var core = Balls[0];
            return new Vec2(RenderPosition.X + (core.X + FaceOffset.X) * Unit, RenderPosition.Y + (core.Y + FaceOffset.Y) * Unit);
        }
    }

    /// <summary>El próximo momento en que empieza un parpadeo (para despertar justo a tiempo estando quieto).</summary>
    public double NextBlink => _blinkLeft > 0 ? Time : _blinkAt;

    private static double SmoothOf(CloudPose pose) => pose switch
    {
        CloudPose.Bubble or CloudPose.Drop => 1,
        CloudPose.Point or CloudPose.Press or CloudPose.Wave => 0.75,
        CloudPose.ThinkHard => 0.45,
        CloudPose.Listen => 0.3,
        CloudPose.Surprise => 0.5,
        CloudPose.Busy or CloudPose.Hug => 0.25,
        CloudPose.Gather => 0.35,
        CloudPose.Think => 0.15,
        _ => 0,
    };

    private struct Target
    {
        public double X;
        public double Y;
        public double R;
        public double W;
        public double H;

        public Target(double x, double y, double r, double w = 0, double h = 0)
        {
            X = x;
            Y = y;
            R = r;
            W = w;
            H = h;
        }
    }

    /// <summary>El cúmulo vivo: va de una de las cuatro formas a la siguiente mientras cada bulto se mueve y respira a su ritmo.</summary>
    private void Cumulus(Span<Target> into, double sx = 1, double sy = 1, double dy = 0)
    {
        var m = Morph;
        var t = Time;
        var k = MathUtil.Lerp(1, 0.52, Smooth);
        var rk = 1 + (1 - k) * 0.38;
        var phase = ((t / 8 + _seed) % 4 + 4) % 4;
        var a = (int)Math.Floor(phase);
        var w = SmoothStep(0.45, 1, phase - a);
        var va = Variants[a];
        var vb = Variants[(a + 1) % 4];
        var v0 = Variants[0];
        var mv = Math.Clamp(m * 1.5, 0, 1);
        for (var i = 0; i < BallCount; i++)
        {
            var x = MathUtil.Lerp(v0[i * 3], MathUtil.Lerp(va[i * 3], vb[i * 3], w), mv);
            var y = MathUtil.Lerp(v0[i * 3 + 1], MathUtil.Lerp(va[i * 3 + 1], vb[i * 3 + 1], w), mv);
            var r = MathUtil.Lerp(v0[i * 3 + 2], MathUtil.Lerp(va[i * 3 + 2], vb[i * 3 + 2], w), mv);
            var s = i * 1.618;
            var low = y > 0.25;
            if (i > 0)
            {
                x += m * 0.06 * (Math.Sin(t * 0.37 + s * 2.1) + 0.6 * Math.Sin(t * 0.71 + s * 3.3));
                y += m * (low ? 0.015 : 0.035) * (Math.Sin(t * 0.43 + s * 1.7) + 0.6 * Math.Sin(t * 0.89 + s * 2.9));
                r *= 1 + m * 0.1 * Math.Sin(t * 0.53 + s * 2.7);
            }

            r *= 1 + 0.025 * Math.Sin(t * 2 + i * 1.3);

            // Los de arriba nunca asoman por encima del núcleo: la cabeza es una cúpula lisa (con ellos encima parecía una
            // joroba); hacen de hombros.
            if (i is 3 or 4)
            {
                y = Math.Max(y, HeadTop + r);
            }

            into[i] = new Target(x * k * sx, y * k * sy + dy, r * rk);
        }

        into[Box] = new Target(0, 0, 0);
    }

    private Span<Target> Shape(CloudPose pose, out Vec2 face, out double faceScale, out CloudEyes eyes, out CloudMouth mouth, out Vec2 look)
    {
        var p = _targets.AsSpan();
        var t = Time;
        faceScale = FaceSize;
        eyes = CloudEyes.Normal;
        mouth = CloudMouth.Smile;
        look = default;
        Vec2? faceAt = null;
        switch (pose)
        {
            case CloudPose.Bubble:
            {
                var (hw, hh) = Bubble;
                hw = Math.Max(0.6, hw);
                hh = Math.Max(0.35, hh);
                var corner = Math.Min(0.55, hh * 0.85);
                p[Box] = new Target(0, 0, corner, hw - corner, hh - corner);

                // Arriba y abajo, bultos que asoman un poco del borde (esponjoso, sin dejar de ser liso); dos hacen la colita.
                var r = Math.Min(0.5, hh * 0.7);
                for (var i = 0; i < 8; i++)
                {
                    var fx = (i % 4 + 0.5) / 4;
                    var x = MathUtil.Lerp(-hw + corner * 0.8, hw - corner * 0.8, fx) + 0.04 * Math.Sin(t * 0.7 + i * 1.9);
                    var y = i < 4 ? -hh + r * 0.88 : hh - r * 0.94;
                    p[i] = new Target(x, y + 0.012 * Math.Sin(t * 3 + i), r * (1 + 0.03 * Math.Sin(t * 1.3 + i * 2.1)));
                }

                p[8] = new Target(-hw * 0.55, hh + 0.24, 0.2 + 0.01 * Math.Sin(t * 3));
                p[9] = new Target(-hw * 0.7, hh + 0.55, 0.12);
                faceAt = new Vec2(-hw + 0.5, 0);
                faceScale = 0.9;
                mouth = CloudMouth.Talk;
                break;
            }

            case CloudPose.Talk:
                Cumulus(p, 1 + VoiceLevel * 0.04, 1 + VoiceLevel * 0.05);
                mouth = CloudMouth.Talk;
                break;

            case CloudPose.Think or CloudPose.ThinkHard:
            {
                var hard = pose == CloudPose.ThinkHard;
                Cumulus(p, hard ? 0.95 : 1, hard ? 0.9 : 0.94);
                for (var k = 0; k < 2; k++)
                {
                    var ph = (t * (hard ? 0.7 : 0.42) + k * 0.5) % 1;
                    p[8 + k] = new Target(0.5 + ph * 0.65, -0.42 - ph * 1.1, 0.25 * (1 - ph * 0.55));
                }

                look = new Vec2(0.05, -0.06);
                mouth = CloudMouth.Flat;
                eyes = hard ? CloudEyes.Focused : CloudEyes.Normal;
                break;
            }

            case CloudPose.Listen:
            {
                Cumulus(p, 1.1, 1);
                var level = HearLevel >= 0 ? HearLevel * 0.35 : 0.12 + 0.1 * Math.Sin(t * 8) + 0.06 * Math.Sin(t * 13.7);
                p[5] = new Target(-0.95, 0.1, 0.3 * (1 + level));
                p[6] = new Target(0.95, 0.1, 0.3 * (1 + level));
                eyes = CloudEyes.Wide;
                break;
            }

            case CloudPose.Point or CloudPose.Press or CloudPose.Wave:
            {
                var dir = pose == CloudPose.Wave ? WaveDirection(t) : AimDirection();
                Cumulus(p, 0.9, 0.9);
                var ext = pose == CloudPose.Wave ? 0.72 : pose == CloudPose.Point ? Math.Min(1, PoseTime * 3) : Reach;
                for (var j = 0; j < 5; j++)
                {
                    var d = FingerDistance[j] * ext + (pose == CloudPose.Point ? 0.04 * Math.Sin(t * 5) : 0);
                    p[5 + j] = new Target(dir.X * d, dir.Y * d + 0.05, FingerRadius[j]);
                }

                faceAt = new Vec2(dir.X * 0.12, 0.03 + dir.Y * 0.06);
                look = new Vec2(dir.X * 0.07, dir.Y * 0.06);
                eyes = pose == CloudPose.Press && Reach > 0.98 ? CloudEyes.Happy : CloudEyes.Normal;
                break;
            }

            case CloudPose.Drop:
                Cumulus(p);
                for (var i = 0; i < BallCount; i++)
                {
                    p[i].R *= 1 + 0.035 * Math.Sin(t * 3 + i * 0.4);
                }

                look = new Vec2(0, -0.02);
                break;

            case CloudPose.Happy:
                Cumulus(p, 1.04, 1.04);
                eyes = CloudEyes.Happy;
                mouth = CloudMouth.Big;
                break;

            case CloudPose.Tickle:
                Cumulus(p, 1.03 + 0.04 * Math.Sin(t * 22), 1 - 0.04 * Math.Sin(t * 22));
                for (var i = 1; i < BallCount; i++)
                {
                    p[i].X += 0.05 * Math.Sin(t * 25 + i * 1.7);
                    p[i].Y += 0.03 * Math.Sin(t * 19 + i * 2.3);
                }

                eyes = CloudEyes.Happy;
                mouth = CloudMouth.Big;
                break;

            case CloudPose.Surprise:
            {
                var f = PoseTime < 0.2 ? 1 : 0;
                Cumulus(p, 1 + 0.3 * f, 1 + 0.3 * f);
                for (var i = 0; i < BallCount; i++)
                {
                    p[i].R *= 1 + 0.2 * f;
                }

                eyes = CloudEyes.Wide;
                mouth = CloudMouth.O;
                break;
            }

            case CloudPose.Error:
                Cumulus(p, 1.06, 0.86, 0.1);
                eyes = CloudEyes.Sad;
                mouth = CloudMouth.Frown;
                look = new Vec2(0, 0.04);
                break;

            case CloudPose.Shrug:
                Cumulus(p, 1.05, 0.92, 0.04);
                p[5].Y += 0.3;
                p[6].Y += 0.3;
                p[5].X -= 0.08;
                p[6].X += 0.08;
                eyes = CloudEyes.Half;
                mouth = CloudMouth.Wavy;
                break;

            case CloudPose.Sleep:
            {
                var breath = 1 + 0.05 * Math.Sin(t * 1.6);
                Cumulus(p, 1.15, 0.68, 0.2);
                for (var i = 0; i < BallCount; i++)
                {
                    p[i].X *= breath;
                    p[i].R *= breath;
                }

                eyes = CloudEyes.Closed;
                mouth = CloudMouth.None;
                break;
            }

            case CloudPose.Dissolve:
            {
                var outward = PoseTime < 1.1;
                var spread = outward ? 1 + PoseTime * 1.1 : 1 + Math.Max(0, 2.1 - PoseTime) * 1.1;
                Cumulus(p, spread, spread);
                if (outward)
                {
                    for (var i = 0; i < BallCount; i++)
                    {
                        p[i].R *= 1 - PoseTime * 0.25;
                    }
                }

                break;
            }

            case CloudPose.Wait:
                Cumulus(p, 1, 1);
                Rotate(p, 0.12 * Math.Sin(t * 1.3));
                eyes = CloudEyes.Wide;
                mouth = CloudMouth.Flat;
                break;

            case CloudPose.Gather:
            {
                // Aspira despacio y se va llenando: cada vez un poco más grande, latiendo.
                var fill = Math.Min(1, PoseTime / 6);
                var breath = 0.9 + fill * 0.08 + 0.035 * Math.Sin(t * 3.4);
                Cumulus(p, breath, breath);
                eyes = CloudEyes.Closed;
                mouth = CloudMouth.O;
                look = new Vec2(0, -0.03);
                break;
            }

            case CloudPose.Busy:
                Cumulus(p, 0.96, 0.96);
                eyes = CloudEyes.Focused;
                mouth = CloudMouth.Flat;
                look = new Vec2(0.04 * Math.Sin(t * 1.7), 0.02);
                break;

            case CloudPose.Hug:
            {
                var (hw, hh) = Bubble;
                hw = Math.Max(0.8, hw);
                hh = Math.Max(0.3, hh);
                var corner = Math.Min(0.4, hh);
                Cumulus(p, Math.Max(1, hw / 1.2), 0.8, -hh * 0.55);
                p[Box] = new Target(0, 0.05, corner, hw - corner, hh - corner);
                faceAt = new Vec2(0, -hh * 0.45);
                eyes = CloudEyes.Focused;
                mouth = CloudMouth.Smile;
                break;
            }

            default:
                Cumulus(p);
                break;
        }

        var core = p[0];
        face = faceAt ?? new Vec2(core.X, core.Y - 0.03 * MathUtil.Lerp(1, 0.52, Smooth));
        return p;
    }

    private static void Rotate(Span<Target> p, double angle)
    {
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        for (var i = 0; i < BallCount; i++)
        {
            var x = p[i].X;
            var y = p[i].Y;
            p[i].X = x * c - y * s;
            p[i].Y = x * s + y * c;
        }
    }

    /// <summary>Hacia dónde apunta el brazo (unitario).</summary>
    private Vec2 AimDirection()
    {
        var d = Aim - Position;
        return d.LengthSquared < 1 ? new Vec2(0.78, -0.62).Normalized() : d.Normalized();
    }

    /// <summary>El brazo de saludar: hacia arriba y a un lado, moviéndose de lado a lado.</summary>
    private Vec2 WaveDirection(double t)
    {
        var side = Aim.X < Position.X ? -1 : 1;
        var angle = -Math.PI / 2 + side * (0.75 + 0.45 * Math.Sin(t * 9));
        return new Vec2(Math.Cos(angle), Math.Sin(angle));
    }

    /// <summary>Dónde llega la punta del dedo (píxeles) estirado del todo.</summary>
    public double FingerLength => (FingerDistance[^1] + FingerRadius[^1] * 0.6) * Unit;

    // ---------- nubecitas ----------

    public void AddPuff(Vec2 at, Vec2 velocity, double radius, double life, double lift = 0.1)
    {
        if (Puffs.Count >= MaxPuffs)
        {
            return;
        }

        Puffs.Add(new Puff { X = at.X, Y = at.Y, VX = velocity.X, VY = velocity.Y, R0 = radius, R = radius * 0.5, Life = life, Lift = lift });
    }

    private void StepPuffs(double dt)
    {
        var u = Unit;
        for (var i = Puffs.Count - 1; i >= 0; i--)
        {
            var p = Puffs[i];
            p.T += dt;
            if (p.T >= p.Life)
            {
                Puffs.RemoveAt(i);
                continue;
            }

            var f = p.T / p.Life;
            p.VX *= 1 - 1.4 * dt;
            p.VY *= 1 - 1.4 * dt;
            p.VY -= p.Lift * u * dt;
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            p.R = p.R0 * (f < 0.18 ? MathUtil.Lerp(0.5, 1, f / 0.18) : 1 - SmoothStep(0.25, 1, f));
        }
    }

    /// <summary>Suelta unas nubecitas alrededor (un estornudo, una sorpresa, un adiós).</summary>
    public void PuffBurst(int count, double speed = 1.6)
    {
        var u = Unit;
        for (var i = 0; i < count; i++)
        {
            var a = _random.Range(0, Math.Tau);
            var from = Balls[1 + _random.Next(BallCount - 1)];
            AddPuff(new Vec2(RenderPosition.X + from.X * u, RenderPosition.Y + from.Y * u),
                new Vec2(Math.Cos(a), Math.Sin(a)) * speed * u * _random.Range(0.7, 1.2), _random.Range(0.09, 0.15) * u, _random.Range(0.5, 0.9));
        }
    }

    // ---------- partículas ----------

    public void Burst(ParticleKind kind, Vec2 at, int count = 1)
    {
        var u = Unit;
        for (var n = 0; n < count; n++)
        {
            switch (kind)
            {
                case ParticleKind.Star or ParticleKind.Sparkle:
                {
                    var a = -Math.PI / 2 + _random.Range(-1.3, 1.3);
                    var v = _random.Range(1.2, 2.5) * u;
                    Particles.Add(new Particle { Kind = kind, X = at.X, Y = at.Y, VX = Math.Cos(a) * v, VY = Math.Sin(a) * v, Life = _random.Range(0.8, 1.2), Size = _random.Range(0.06, 0.12) * u, Side = _random.Range(0, 6) });
                    break;
                }

                case ParticleKind.Ring:
                    Particles.Add(new Particle { Kind = kind, X = at.X, Y = at.Y, Life = 0.5, Size = 0.55 * u });
                    break;
                case ParticleKind.Heart or ParticleKind.Note:
                    Particles.Add(new Particle { Kind = kind, X = at.X + _random.Range(-0.4, 0.4) * u, Y = at.Y, VX = _random.Range(-0.2, 0.2) * u, VY = -_random.Range(0.5, 0.8) * u, Life = _random.Range(1.4, 2), Size = 0.22 * u, Side = _random.Range(0, 6) });
                    break;
                case ParticleKind.Question or ParticleKind.Exclamation:
                    Particles.Add(new Particle { Kind = kind, X = at.X, Y = at.Y, VY = -0.35 * u, Life = 1.6, Size = 0.36 * u });
                    break;
                default:
                    Particles.Add(new Particle { Kind = kind, X = at.X, Y = at.Y, Life = 1, Size = 0.2 * u });
                    break;
            }
        }
    }

    private int Count(string key, double rate, double dt)
    {
        var value = (_emitters.TryGetValue(key, out var v) ? v : 0) + rate * dt;
        var n = (int)Math.Floor(value);
        _emitters[key] = value - n;
        return n;
    }

    private void Emit(double dt, double speed)
    {
        var u = Unit;
        var pose = Pose;
        var at = RenderPosition;

        // Al volar, se le desprenden nubecitas por detrás.
        if (speed > 1.3 && Trail > 0.01)
        {
            var dir = Velocity / (speed * u);
            for (var n = Count("puff", Trail * Math.Min(11, speed * 1.7), dt); n > 0; n--)
            {
                Ball best = Balls[1];
                var bestScore = double.MinValue;
                for (var i = 1; i < BallCount; i++)
                {
                    var q = Balls[i];
                    var score = -(q.X * dir.X + q.Y * dir.Y) + _random.NextDouble() * 0.5;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = q;
                    }
                }

                var jitter = (_random.NextDouble() - 0.5) * best.R * u * 0.9;
                AddPuff(
                    new Vec2(at.X + best.X * u - dir.X * best.R * u * 0.5 - dir.Y * jitter, at.Y + best.Y * u - dir.Y * best.R * u * 0.5 + dir.X * jitter),
                    new Vec2(Velocity.X * 0.22 + (_random.NextDouble() - 0.5) * 0.3 * u, Velocity.Y * 0.22 + (_random.NextDouble() - 0.5) * 0.3 * u),
                    _random.Range(0.11, 0.2) * u * (0.75 + 0.35 * Trail), _random.Range(0.55, 1.05));
            }
        }

        // De vez en cuando se evapora un jirón de un bulto, como un cúmulo de verdad.
        if (pose is CloudPose.Idle or CloudPose.Think && Time > _nextWisp && Morph > 0.05)
        {
            _nextWisp = Time + 4 + _random.NextDouble() * 5 / (0.4 + Morph);
            var q = Balls[3 + _random.Next(4)];
            var dl = Math.Max(1e-6, Math.Sqrt(q.X * q.X + q.Y * q.Y));
            var ux = q.X / dl;
            var uy = q.Y / dl;
            AddPuff(new Vec2(at.X + (q.X + ux * q.R * 0.6) * u, at.Y + (q.Y + uy * q.R * 0.6) * u), new Vec2(ux * 0.28 * u, uy * 0.28 * u - 0.1 * u), _random.Range(0.1, 0.15) * u, 2.2);
        }

        if (pose == CloudPose.Dissolve && PoseTime < 1.1)
        {
            for (var n = Count("mist", 30, dt); n > 0; n--)
            {
                var q = Balls[_random.Next(BallCount)];
                var a = _random.Range(0, Math.Tau);
                var r = Math.Max(0.05, q.R) * u;
                Particles.Add(new Particle
                {
                    Kind = ParticleKind.Mist,
                    X = at.X + q.X * u + Math.Cos(a) * r,
                    Y = at.Y + q.Y * u + Math.Sin(a) * r,
                    VX = Math.Cos(a) * 0.3 * u,
                    VY = Math.Sin(a) * 0.3 * u - 0.1 * u,
                    Life = _random.Range(1.4, 2.2),
                    Size = _random.Range(0.25, 0.6) * u,
                    Alpha = 0.2,
                    Grey = Tint > 0.5,
                });
            }
        }

        // Juntando fuerzas: nubecitas que llegan de alrededor (sobre todo de arriba) y se funden con el cuerpo.
        if (pose == CloudPose.Gather)
        {
            for (var n = Count("gather", 4.5, dt); n > 0; n--)
            {
                var a = -Math.PI / 2 + _random.Range(-1.9, 1.9);
                var d = _random.Range(2.0, 2.5) * u;
                var dir = new Vec2(Math.Cos(a), Math.Sin(a) * 0.8);
                AddPuff(new Vec2(at.X + dir.X * d, at.Y + dir.Y * d), new Vec2(-dir.X, -dir.Y) * _random.Range(2.2, 2.6) * u, _random.Range(0.12, 0.18) * u, _random.Range(0.95, 1.15), 0);
            }
        }

        if (pose == CloudPose.Sleep && Count("z", 0.75, dt) > 0)
        {
            Particles.Add(new Particle { Kind = ParticleKind.Z, X = at.X + 0.45 * u, Y = at.Y - 0.2 * u, VX = 0.15 * u, VY = -0.45 * u, Life = 2.2, Size = 0.24 * u });
        }

        if (pose == CloudPose.Error)
        {
            for (var n = Count("rain", 16, dt); n > 0; n--)
            {
                Particles.Add(new Particle { Kind = ParticleKind.Rain, X = at.X + (_random.NextDouble() - 0.5) * 1.7 * u, Y = at.Y + 0.55 * u, VY = _random.Range(2.4, 3.4) * u, Life = 0.65, Size = 0.13 * u });
            }
        }

        if (pose == CloudPose.Listen && Count("wave", 2.2, dt) > 0)
        {
            foreach (var side in (ReadOnlySpan<int>)[-1, 1])
            {
                Particles.Add(new Particle { Kind = ParticleKind.Wave, X = at.X + side * 1.15 * u, Y = at.Y + 0.1 * u, VX = side * 0.6 * u, Life = 0.9, Size = 0.3 * u, Side = side });
            }
        }
    }

    // ---------- forma para dibujar ----------

    /// <summary>Lo que llegan las bolas más allá de su radio ahora (más condensado o más blando, más lejos).</summary>
    public double FieldReach => MathUtil.Lerp(1.34, 1.6, Smooth) + Math.Max(0, Soft - 0.3) * 1.2;

    /// <summary>Las bolas del cuerpo y las nubecitas, en unidades relativas a <see cref="RenderPosition"/>.</summary>
    public int Blobs(Span<FieldBlob> into)
    {
        var n = 0;
        foreach (var b in Balls)
        {
            if (b.R > 0.01 && n < into.Length)
            {
                into[n++] = b.Blob with { Radius = Math.Max(0.02, b.R) };
            }
        }

        var origin = RenderPosition;
        foreach (var p in Puffs)
        {
            if (p.R > 0.6 && n < into.Length)
            {
                into[n++] = new FieldBlob((p.X - origin.X) / Unit, (p.Y - origin.Y) / Unit, p.R / Unit);
            }
        }

        return n;
    }

    /// <summary>Centro ponderado y lo que se aleja del centro lo más lejano (unidades).</summary>
    public (Vec2 Center, double Extent) Centroid()
    {
        double sx = 0, sy = 0, sw = 0;
        foreach (var q in Balls.AsSpan(0, BallCount))
        {
            var w = q.R * q.R;
            sx += q.X * w;
            sy += q.Y * w;
            sw += w;
        }

        var cx = sw > 0 ? sx / sw : 0;
        var cy = sw > 0 ? sy / sw : 0;
        var ext = 0.0;
        foreach (var q in Balls)
        {
            if (q.R > 0.01)
            {
                var dx = Math.Max(Math.Abs(q.X - cx) - q.HalfWidth, 0);
                var dy = Math.Max(Math.Abs(q.Y - cy) - q.HalfHeight, 0);
                ext = Math.Max(ext, Math.Sqrt(dx * dx + dy * dy) + q.R);
            }
        }

        return (new Vec2(cx, cy), ext);
    }

    /// <summary>Lo que ocupa el cuerpo (unidades relativas al centro), sin nubecitas.</summary>
    public (double Left, double Top, double Right, double Bottom) BodyExtent()
    {
        double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
        foreach (var q in Balls)
        {
            if (q.R <= 0.01)
            {
                continue;
            }

            l = Math.Min(l, q.X - q.HalfWidth - q.R);
            r = Math.Max(r, q.X + q.HalfWidth + q.R);
            t = Math.Min(t, q.Y - q.HalfHeight - q.R);
            b = Math.Max(b, q.Y + q.HalfHeight + q.R);
        }

        return l > r ? (-1, -1, 1, 1) : (l, t, r, b);
    }

    public static double SmoothStep(double a, double b, double x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
