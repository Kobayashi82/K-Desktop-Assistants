using Kobii.Core.Animation;
using Kobii.Core.Geometry;
using Kobii.Core.Terrain;

namespace Kobii.Core.Creature;

/// <summary>
/// Comer y beber, paso a paso (el de Kobii AI, sin hambre ni sed): va hasta la comida que le das, la mira con ganas, se
/// agacha y la coge, o se la saca él de detrás de la espalda; se la lleva a la boca abriéndola, da un mordisco (y a la
/// comida le falta un trozo), mastica con los mofletes llenos, traga y vuelve a empezar hasta acabársela; al final se
/// relame y se da palmaditas en la barriga. Las bebidas se cogen con las dos manos y se inclinan para beber a tragos.
/// </summary>
/// <remarks>
/// La coreografía mueve la comida (su centro, en el espacio de la criatura) y las manos van a su alrededor:
/// así comida y manos nunca se separan, y el motor gráfico interpola las dos igual entre pasos.
/// </remarks>
public sealed partial class KobiiGame
{
    private readonly List<FoodItem> _foods = [];

    /// <summary>La comida que hay (en el escritorio o en sus manos), para dibujarla.</summary>
    public IReadOnlyList<FoodItem> FoodItems => _foods;

    /// <summary>Con una mano, la comida va por encima del puño (en radios de la comida), para que se vea.</summary>
    public const double OneHandHold = 0.8;

    private const RigMask MealMask = RigMask.Mouth | RigMask.Face | RigMask.Head;

    private MealPlan? _eat;

    private enum MealStep
    {
        /// <summary>Se lleva la mano a la espalda a por algo de comer (lo que se saca él).</summary>
        Rummage,

        /// <summary>Va andando hasta la comida.</summary>
        Approach,

        /// <summary>Se para y la mira con ganas.</summary>
        Notice,

        /// <summary>Se agacha y la coge con una mano.</summary>
        Reach,

        /// <summary>Se incorpora con ella; si come con las dos manos, la otra se une a medio camino.</summary>
        Lift,

        /// <summary>Se la lleva a la boca (abriéndola) o inclina el vaso.</summary>
        ToMouth,

        /// <summary>El mordisco (o los tragos, si bebe).</summary>
        Bite,

        /// <summary>Mastica con los mofletes llenos mientras baja la mano.</summary>
        Chew,

        /// <summary>Traga.</summary>
        Swallow,

        /// <summary>Baja el vaso.</summary>
        Lower,

        /// <summary>Se relame, se limpia la boca o se da palmaditas en la barriga.</summary>
        Finish,
    }

    /// <summary>Lo que está comiendo y por dónde va.</summary>
    private sealed class MealPlan(FoodItem food)
    {
        public FoodItem Food { get; } = food;

        public MealStep Step { get; set; }

        public double Time { get; set; }

        /// <summary>Mordiscos o sorbos dados.</summary>
        public int Bites { get; set; }

        /// <summary>Mordiscos o sorbos que necesita para acabárselo.</summary>
        public int Total { get; set; } = 3;

        public bool TwoHands { get; set; }

        /// <summary>Lado hacia el que está la comida (la mano que la coge).</summary>
        public double Side { get; set; } = 1;

        /// <summary>Más de 1, más despacio (los mayores); menos, más rápido (las crías).</summary>
        public double Pace { get; set; } = 1;

        /// <summary>Ya ha dado el mordisco de este paso.</summary>
        public bool Chomped { get; set; }

        /// <summary>Ya se lo ha comido.</summary>
        public bool Done { get; set; }

        /// <summary>Dónde estaban las manos y la comida al empezar el paso (para salir de ahí en arco).</summary>
        public Vec2 FromLead { get; set; }

        public Vec2 FromOther { get; set; }

        public Vec2 FromCenter { get; set; }

        /// <summary>Centro de la comida ahora (en el espacio de la criatura).</summary>
        public Vec2 Center { get; set; }

        public bool Drinks => Food.Kind.IsDrink();
    }

    /// <summary>Radio de la comida en píxeles (el mismo con el que la dibuja el motor gráfico).</summary>
    public double FoodRadius => 9 * Body.Scale;

    /// <summary>
    /// Se pone a comer: <paramref name="food"/>, que está en el escritorio (va hasta ella), o, sin comida, algo que se saca
    /// él de detrás de la espalda (<paramref name="own"/>). False si ahora no puede.
    /// </summary>
    public bool Eat(FoodItem? food = null, FoodKind own = FoodKind.Apple)
    {
        if (!CanPlay || Activity == Activity.Sleep)
        {
            return false;
        }

        if (food is null)
        {
            food = new FoodItem(own, new CreatureBody(FoodItem.SizeDip, Body.Scale)) { Own = true };
            _foods.Add(food);
        }

        StartRoutine(new MealRoutine(this, food));
        return true;
    }

    /// <summary>Le das algo de comer o de beber: cae desde un poco más arriba de él y va a por ello en cuanto puede.</summary>
    public FoodItem Give(FoodKind kind, World world, double? x = null)
    {
        var food = new FoodItem(kind, new CreatureBody(FoodItem.SizeDip, Body.Scale) { Restitution = 0.25, MinBounceDip = 300, FrictionDip = 900 });
        var at = x ?? Body.Feet.X + (Visual.FacingTarget < 0 ? -1 : 1) * Body.Height * 1.4;
        if (Body.Support is { } support)
        {
            var margin = Body.Height * 0.6;
            at = Math.Clamp(at, support.Left + margin, Math.Max(support.Left + margin, support.Right - margin));
        }

        var monitor = world.MonitorAt(Body.Center) ?? world.NearestMonitor(Body.Center);
        var top = monitor?.Bounds.Top ?? Body.Feet.Y - 400;
        food.Body.Feet = new Vec2(at, Math.Max(top + food.Body.Height + 4, Body.Feet.Y - Body.Height * 3.5));
        food.Body.Velocity = new Vec2(0, -60 * Body.Scale);
        _foods.Add(food);
        return food;
    }

    /// <summary>La comida en el escritorio cae y se posa; si se queda mucho rato sin que se la coma, desaparece.</summary>
    private void StepFoods(World world, double dt)
    {
        for (var i = _foods.Count - 1; i >= 0; i--)
        {
            var food = _foods[i];
            if (food.Held || food.Eaten)
            {
                continue;
            }

            food.Body.ScaleToward(Body.Scale, dt);
            if (food.Body.Step(world, dt) is { } landing && landing.Speed > 300 * Body.Scale)
            {
                Visual.AddEmote(EmoteKind.Dust, landing.Point, Body.Scale, 2);
            }

            food.RestingSeconds = food.Body.IsGrounded ? food.RestingSeconds + dt : 0;
            if (food.RestingSeconds > 120 || (food.Own && food.RestingSeconds > 8))
            {
                _foods.RemoveAt(i);
                Visual.AddEmote(EmoteKind.Sparkle, food.Body.Feet, Body.Scale, 3);
            }
        }

        // Le has dado algo: en cuanto puede, va a por ello.
        if (Activity is Activity.Idle or Activity.Walk && CanPlay && _foods.FirstOrDefault(f => !f.Own && !f.Held && f.Body.IsGrounded
                && f.Body.SupportId == Body.SupportId) is { } given)
        {
            Eat(given);
        }
    }

    /// <summary>Comer como rutina: lo deja si le interrumpen (y la comida se le cae).</summary>
    private sealed class MealRoutine(KobiiGame game, FoodItem food) : Routine(game)
    {
        public override PlayKind Kind => PlayKind.Meal;

        public override Pose? Pose => Game._eat?.Step == MealStep.Reach ? Animation.Pose.Crouch : null;

        public override void Begin() => Game.StartMeal(food);

        public override void Stop() => Game.AbandonMeal();

        protected override bool Update(World world, Sensors sensors, double dt) => Game.StepEat(dt);
    }

    private void StartMeal(FoodItem food)
    {
        var plan = new MealPlan(food);
        plan.TwoHands = plan.Drinks;
        plan.Total = plan.Drinks ? 2 : food.Kind == FoodKind.Berries ? 3 : 3;
        plan.Side = Visual.FacingTarget < 0 ? -1 : 1;
        _eat = plan;
        if (food.Own)
        {
            plan.FromLead = Animator.Rig.Hand(plan.Side, Form);
            plan.FromOther = Animator.Rig.Hand(-plan.Side, Form);
            plan.Step = MealStep.Rummage;
        }
    }

    /// <summary>Lo deja a medias (se le coge, se cae…): si tenía la comida en la mano, se le cae.</summary>
    private void AbandonMeal()
    {
        if (_eat is not { } plan)
        {
            return;
        }

        _eat = null;
        var food = plan.Food;
        if (food.Own && !food.Held)
        {
            // Aún no lo había sacado.
            _foods.Remove(food);
            return;
        }

        if (food.Held && !food.Eaten)
        {
            food.Held = false;
            food.Tilt = 0;
            food.Body.Feet = WorldPoint(food.HeldLocal) + new Vec2(0, FoodRadius);
            food.Body.Launch(new Vec2(plan.Side * 40 * Body.Scale, -80 * Body.Scale));
            food.RestingSeconds = 0;
        }
    }

    private bool StepEat(double dt)
    {
        if (_eat is not { } plan || (!plan.Done && (plan.Food.Eaten || !_foods.Contains(plan.Food))))
        {
            AbandonMeal();
            return false;
        }

        if (plan.Step == MealStep.Approach)
        {
            return Approach(plan);
        }

        Body.WalkVelocity = 0;
        if (plan.Step != MealStep.Rummage)
        {
            Visual.FacingTarget = plan.Side * 0.6;
        }

        plan.Time += dt / plan.Pace;
        plan.Food.PreviousHeldLocal = plan.Food.HeldLocal;
        var rig = default(Rig);
        var mask = MealMask | (plan.TwoHands ? RigMask.Hands : plan.Side < 0 ? RigMask.LeftHand : RigMask.RightHand);
        switch (plan.Step)
        {
            case MealStep.Rummage:
                StepRummage(plan, ref rig);
                break;
            case MealStep.Notice:
                StepNotice(plan, ref rig);
                break;
            case MealStep.Reach:
                StepReach(plan, ref rig);
                break;
            case MealStep.Lift:
                StepLift(plan, ref rig);
                break;
            case MealStep.ToMouth:
                StepToMouth(plan, ref rig);
                break;
            case MealStep.Bite:
                if (plan.Drinks)
                {
                    StepGulps(plan, ref rig, dt);
                }
                else
                {
                    StepBite(plan, ref rig);
                }

                break;
            case MealStep.Chew:
                StepChew(plan, ref rig);
                break;
            case MealStep.Swallow:
                StepSwallow(plan, ref rig);
                break;
            case MealStep.Lower:
                StepLower(plan, ref rig);
                break;
            case MealStep.Finish:
                mask = MealMask | RigMask.Hands;
                if (StepFinish(plan, ref rig))
                {
                    return false;
                }

                break;
        }

        Animator.Override(rig, mask);
        return true;
    }

    /// <summary>
    /// La mano a la espalda mirando por encima del hombro y, ¡puf!, vuelve con algo de comer (de ahí lo sube al pecho como
    /// si lo hubiera cogido del suelo).
    /// </summary>
    private void StepRummage(MealPlan plan, ref Rig rig)
    {
        const double duration = 0.5;
        var behind = new Vec2(-plan.Side * 0.32, -0.62);
        var u = Easing.Segment(plan.Time, 0, duration * 0.7);
        HoldHands(plan, ref rig, Easing.Arc(plan.FromLead, behind, new Vec2(0, 0.1), u), plan.FromOther, front: 0, grip: u);
        Visual.FacingTarget = -plan.Side * 0.35;
        Visual.LookTarget = new Vec2(-plan.Side * 0.8, 0.2);
        rig.HeadTilt = -plan.Side * 0.10 * u;
        if (plan.Time >= duration)
        {
            var item = plan.Food;
            item.Held = true;
            item.HeldSide = plan.Side;
            item.TwoHands = plan.TwoHands;
            item.BiteSide = -plan.Side;
            item.HeldLocal = behind;
            item.PreviousHeldLocal = behind;
            plan.Center = behind;
            Raise(GameEventKind.Pulled, text: "out");
            Visual.SetExpression(item.Kind.IsFavorite() ? Expression.Love : Expression.Joy, 1.2);
            Visual.Bounce(1.2);
            Next(plan, MealStep.Lift);
        }
    }

    private bool Approach(MealPlan plan)
    {
        var food = plan.Food;
        if (!food.Body.IsGrounded)
        {
            Body.WalkVelocity = 0;
            return true;
        }

        var side = Math.Sign(food.Body.Feet.X - Body.Feet.X);
        plan.Side = side == 0 ? (Visual.FacingTarget < 0 ? -1 : 1) : side;

        // Se para con la comida al alcance de la mano, delante de él.
        var reach = Unit * 1.18;
        var target = food.Body.Feet.X - plan.Side * reach;
        if (Math.Abs(target - Body.Feet.X) > 4 * Body.Scale && WalkTowards(target, 1.3))
        {
            return true;
        }

        Body.WalkVelocity = 0;
        if (Math.Abs(food.Body.Feet.X - Body.Feet.X) > reach * 1.7 || food.Body.SupportId != Body.SupportId)
        {
            // No llega (está al otro lado de un borde): se rinde.
            _eat = null;
            return false;
        }

        Animator.Side = plan.Side;
        Visual.SetExpression(food.Kind.IsFavorite() ? Expression.Love : Expression.Joy, 0.6);
        Visual.Bounce(1.2);
        Next(plan, MealStep.Notice);
        return true;
    }

    private void StepNotice(MealPlan plan, ref Rig rig)
    {
        // Mira la comida y se le hace la boca agua.
        Visual.LookTarget = new Vec2(plan.Side * 0.7, 0.8);
        rig.MouthOpen = 0.18 * Easing.Pulse(plan.Time, 0.15, 0.2);
        rig.HeadTilt = plan.Side * 0.08 * Easing.Segment(plan.Time, 0, 0.3);
        HoldHands(plan, ref rig, plan.FromLead, plan.FromOther, front: 0, grip: 0);
        if (plan.Time >= 0.35)
        {
            Next(plan, MealStep.Reach);
        }
    }

    private void StepReach(MealPlan plan, ref Rig rig)
    {
        // Se agacha (lo hace la postura) y una mano va en arco hasta la comida y la agarra; la otra, si va a
        // hacer falta, se prepara.
        const double duration = 0.5;
        var u = Easing.Segment(plan.Time, 0, duration);
        var food = FoodLocal(plan);
        plan.Center = food;
        var (lead, _) = HandsAround(plan, food);
        var other = plan.TwoHands ? Easing.Arc(plan.FromOther, WaitingHand(plan), new Vec2(0, -0.05), u) : plan.FromOther;
        HoldHands(plan, ref rig, Easing.Arc(plan.FromLead, lead, new Vec2(0, -0.22), u), other, front: 0,
            grip: Easing.Segment(plan.Time, duration * 0.75, duration * 0.3), otherGrip: 0);
        Visual.LookTarget = new Vec2(plan.Side * 0.6, 0.9);
        rig.HeadTilt = plan.Side * 0.10;
        rig.FaceY = 0.03 * u;
        if (plan.Time >= duration + 0.06)
        {
            var item = plan.Food;
            item.Held = true;
            item.HeldSide = plan.Side;
            item.TwoHands = plan.TwoHands;
            item.BiteSide = -plan.Side;
            item.HeldLocal = food;
            item.PreviousHeldLocal = food;
            Visual.SetExpression(Expression.Happy, 2);
            Next(plan, MealStep.Lift);
        }
    }

    private void StepLift(MealPlan plan, ref Rig rig)
    {
        // Se incorpora con la comida hacia el pecho; con las dos manos, la otra la coge a medio camino.
        const double duration = 0.5;
        var u = Easing.Segment(plan.Time, 0, duration);
        var center = Easing.Arc(plan.FromCenter, ChestPoint(plan), new Vec2(plan.Side * 0.08, -0.08), u);
        var (lead, other) = HandsAround(plan, center);
        var join = Easing.Segment(plan.Time, duration * 0.25, duration * 0.7);
        var front = Easing.Segment(plan.Time, duration * 0.2, duration * 0.5);
        HoldHands(plan, ref rig, lead, Easing.Arc(plan.FromOther, other, new Vec2(0, -0.04), join), front, grip: 1,
            otherGrip: Easing.Segment(plan.Time, duration * 0.8, duration * 0.2), otherFront: join);
        PlaceFood(plan, center);
        Visual.LookTarget = new Vec2(plan.Side * 0.4, 0.5);
        if (plan.Time >= duration)
        {
            Next(plan, MealStep.ToMouth);
        }
    }

    private void StepToMouth(MealPlan plan, ref Rig rig)
    {
        // A la boca, que se abre al acercarse (o, si bebe, inclina el vaso y echa la cabeza atrás).
        var duration = plan.Drinks ? 0.42 : 0.32;
        var u = Easing.Segment(plan.Time, 0, duration);
        var center = Easing.Arc(plan.FromCenter, MouthFoodPoint(plan), new Vec2(plan.Side * 0.08, 0.02), u);
        Hold(plan, ref rig, center, grip: 1);
        Visual.LookTarget = new Vec2(plan.Side * 0.5, 0.2);
        if (plan.Drinks)
        {
            plan.Food.Tilt = -plan.Side * 1.0 * u;
            rig.HeadTilt = -plan.Side * 0.12 * u;
            rig.FaceY = -0.02 * u;
            rig.MouthOpen = 0.25 * Easing.Segment(plan.Time, duration * 0.5, duration * 0.5);
        }
        else
        {
            rig.MouthOpen = Easing.Segment(plan.Time, duration * 0.35, duration * 0.65, Ease.Out);
            rig.HeadTilt = plan.Side * 0.06 * u;
        }

        if (plan.Time >= duration)
        {
            Next(plan, MealStep.Bite);
        }
    }

    private void StepBite(MealPlan plan, ref Rig rig)
    {
        // ¡Ñam! La boca se cierra de golpe, la cabeza da un tironcito y a la comida le falta un trozo.
        const double duration = 0.12;
        Hold(plan, ref rig, MouthFoodPoint(plan), grip: 1);
        rig.MouthOpen = 1 - Easing.Segment(plan.Time, 0, duration * 0.6, Ease.Snap);
        rig.FaceY = 0.035 * Easing.Pulse(plan.Time, 0.05, 0.08);
        rig.HeadTilt = plan.Side * 0.06;
        rig.Cheeks = Easing.Segment(plan.Time, 0.03, 0.12, Ease.Out);
        if (plan.Bites < plan.Total && plan.Time >= 0.04 && !plan.Chomped)
        {
            plan.Chomped = true;
            plan.Bites++;
            plan.Food.Bites = plan.Bites;
            plan.Food.Remaining = 1 - (double)plan.Bites / plan.Total;
            Visual.Bounce(-1.1);
            Visual.SetExpression(Expression.Eating, 1.5);
            Visual.AddEmote(EmoteKind.Crumbs, WorldPoint(MouthFoodPoint(plan)), Body.Scale, 3);
            Raise(GameEventKind.Chomp);
            if (plan.Bites >= plan.Total)
            {
                // Último mordisco: se acabó la comida (las manos se quedan vacías).
                Swallowed(plan);
            }
        }

        if (plan.Time >= duration)
        {
            Next(plan, MealStep.Chew);
        }
    }

    private void StepChew(MealPlan plan, ref Rig rig)
    {
        // Mastica con los mofletes llenos (cuatro veces) mientras baja la comida.
        const double duration = 0.95;
        var lower = Easing.Segment(plan.Time, 0.05, 0.35, Ease.Out);
        Hold(plan, ref rig, Vec2.Lerp(plan.FromCenter, ChestPoint(plan), lower), grip: plan.Food.Eaten ? 1 - Easing.Segment(plan.Time, 0, 0.2) : 1);
        var jaw = 0.5 - 0.5 * Math.Cos(Math.Min(1, plan.Time / duration) * 4 * Math.Tau);
        rig.Chew = jaw;
        rig.Cheeks = 1 - 0.15 * jaw;
        rig.FaceY = 0.015 * jaw;
        rig.HeadTilt = plan.Side * 0.04 + 0.03 * Math.Sin(plan.Time * 9);
        Visual.LookTarget = new Vec2(plan.Side * 0.3, 0.3);
        if (plan.Time >= duration)
        {
            Next(plan, MealStep.Swallow);
        }
    }

    private void StepSwallow(MealPlan plan, ref Rig rig)
    {
        // Glup: se le deshinchan los mofletes de golpe y se estira un poquito.
        const double duration = 0.2;
        Hold(plan, ref rig, ChestPoint(plan), grip: plan.Food.Eaten ? 0 : 1);
        rig.Cheeks = 1 - Easing.Segment(plan.Time, 0, 0.12, Ease.Snap);
        rig.FaceY = -0.015 * Easing.Pulse(plan.Time, 0.06, 0.12);
        if (plan.Time < 1.0 / 60)
        {
            Visual.Bounce(1.3);
        }

        if (plan.Time >= duration)
        {
            Next(plan, plan.Bites >= plan.Total ? MealStep.Finish : MealStep.ToMouth);
        }
    }

    private void StepGulps(MealPlan plan, ref Rig rig, double dt)
    {
        // Bebe a tragos con el vaso inclinado: a cada trago baja el nivel y se le mueve la garganta.
        const double duration = 0.28 * 3;
        Hold(plan, ref rig, MouthFoodPoint(plan), grip: 1);
        plan.Food.Tilt = -plan.Side * (1.0 + 0.15 * Math.Min(1, plan.Time / duration));
        var gulp = (plan.Time / 0.28) % 1;
        rig.Cheeks = 0.35 * Easing.Pulse(gulp * 0.28, 0.08, 0.14);
        rig.HeadTilt = -plan.Side * 0.14;
        rig.FaceY = -0.02 - 0.012 * Easing.Pulse(gulp * 0.28, 0.08, 0.14);
        rig.MouthOpen = 0.2;
        Visual.SetExpression(Expression.Eating, 0.5);
        var start = 1 - (double)plan.Bites / plan.Total;
        plan.Food.Remaining = Math.Max(0, start - Math.Min(1, plan.Time / duration) / plan.Total);
        _emoteTimer -= dt;
        if (_emoteTimer <= 0)
        {
            _emoteTimer = 0.28;
            Visual.Bounce(0.6);
            Raise(GameEventKind.Gulp);
        }

        if (plan.Time >= duration)
        {
            plan.Bites++;
            if (plan.Bites >= plan.Total)
            {
                plan.Food.Remaining = 0;
                Swallowed(plan);
            }

            Next(plan, MealStep.Lower);
        }
    }

    private void StepLower(MealPlan plan, ref Rig rig)
    {
        // Baja el vaso al pecho y suelta un «¡aaah!».
        const double duration = 0.36;
        var u = Easing.Segment(plan.Time, 0, duration);
        Hold(plan, ref rig, Vec2.Lerp(plan.FromCenter, ChestPoint(plan), u), grip: plan.Food.Eaten ? 1 - u : 1);
        plan.Food.Tilt = -plan.Side * 1.15 * (1 - u);
        rig.HeadTilt = -plan.Side * 0.14 * (1 - u);
        rig.MouthOpen = 0.5 * Easing.Pulse(plan.Time, 0.1, 0.26);
        if (plan.Time >= duration)
        {
            Next(plan, plan.Bites >= plan.Total ? MealStep.Finish : MealStep.ToMouth);
        }
    }

    /// <summary>El final: se limpia la boca (si ha bebido) o se relame y se da palmaditas. True al acabar.</summary>
    private bool StepFinish(MealPlan plan, ref Rig rig)
    {
        var t = plan.Time;
        const double duration = 1.25;
        if (plan.Drinks)
        {
            // Se limpia la boca con el dorso de la mano.
            var wipe = Gestures.Evaluate(Gesture.Wipe, Math.Min(t, 0.6), new PoseContext(plan.Side, Form, Facing: Visual.Facing));
            rig = Rig.Mix(rig, wipe, plan.Side < 0 ? RigMask.LeftHand : RigMask.RightHand, Easing.Pulse(t, 0.15, 0.6));
        }
        else
        {
            // Se relame (primero hacia un lado y luego hacia el otro) y se da palmaditas en la barriga.
            rig.Tongue = plan.Side * Easing.Pulse(t, 0.15, 0.25) - plan.Side * Easing.Pulse(t - 0.4, 0.12, 0.2);
            var pat = Gestures.Evaluate(Gesture.PatBelly, t, new PoseContext(plan.Side, Form));
            rig = Rig.Mix(rig, pat, RigMask.Hands, Easing.Segment(t, 0.3, 0.3) * (1 - Easing.Segment(t, duration - 0.25, 0.25)));
        }

        if (t >= duration)
        {
            _eat = null;
            return true;
        }

        return false;
    }

    /// <summary>Se lo ha acabado: lo celebra (con corazones si es de lo que más le gusta).</summary>
    private void Swallowed(MealPlan plan)
    {
        if (plan.Done)
        {
            return;
        }

        plan.Done = true;
        var food = plan.Food;
        food.Eaten = true;
        food.Held = false;
        _foods.Remove(food);
        if (!food.Own)
        {
            Notice(0.05);
        }

        Visual.SetExpression(Expression.Joy, 2.2);
        Raise(GameEventKind.Ate, text: food.Kind.Id());
        if (food.Kind.IsFavorite())
        {
            Visual.AddEmote(EmoteKind.Heart, HeadPoint, Body.Scale, 2);
        }
    }

    private void Next(MealPlan plan, MealStep step)
    {
        plan.FromLead = Animator.Rig.Hand(plan.Side, Form);
        plan.FromOther = Animator.Rig.Hand(-plan.Side, Form);
        plan.FromCenter = plan.Center;
        plan.Step = step;
        plan.Time = 0;
        plan.Chomped = false;
        _emoteTimer = 0;
    }

    /// <summary>Lleva la comida a <paramref name="center"/> con las manos alrededor (por delante del cuerpo).</summary>
    private void Hold(MealPlan plan, ref Rig rig, Vec2 center, double grip)
    {
        var (lead, other) = HandsAround(plan, center);
        HoldHands(plan, ref rig, lead, other, front: 1, grip: grip);
        PlaceFood(plan, center);
    }

    private static void PlaceFood(MealPlan plan, Vec2 center)
    {
        plan.Center = center;
        plan.Food.HeldLocal = center;
    }

    /// <summary>Pone las manos (la que sujeta y, si come con las dos, también la otra).</summary>
    private static void HoldHands(MealPlan plan, ref Rig rig, Vec2 lead, Vec2 other, double front, double grip, double? otherGrip = null, double? otherFront = null)
    {
        var form = Form;
        rig.SetHand(plan.Side, lead, form, front: false, grip: grip);
        SetFront(ref rig, plan.Side, front);
        if (plan.TwoHands)
        {
            rig.SetHand(-plan.Side, other, form, front: false, grip: otherGrip ?? grip);
            SetFront(ref rig, -plan.Side, otherFront ?? front);
        }
    }

    private static void SetFront(ref Rig rig, double side, double front)
    {
        if (side < 0)
        {
            rig.FrontL = front;
        }
        else
        {
            rig.FrontR = front;
        }
    }

    /// <summary>Dónde van las manos para sujetar la comida con el centro en <paramref name="center"/>.</summary>
    private (Vec2 Lead, Vec2 Other) HandsAround(MealPlan plan, Vec2 center)
    {
        var r = FoodRadius / Unit;
        if (!plan.TwoHands)
        {
            // Con una mano, por debajo: la comida asoma por encima del puño.
            return (center + new Vec2(0, r * OneHandHold), Rig.RestHand(-plan.Side, Form));
        }

        var spread = r * (plan.Drinks ? 0.78 : 0.95) + 0.03;
        return (center + new Vec2(plan.Side * spread, r * 0.1), center + new Vec2(-plan.Side * spread, r * 0.1));
    }

    /// <summary>Dónde espera la otra mano mientras la primera coge la comida del suelo.</summary>
    private static Vec2 WaitingHand(MealPlan plan) => Rig.RestHand(-plan.Side, Form) + new Vec2(plan.Side * 0.12, -0.10);

    /// <summary>Centro de la comida en el suelo, en el espacio de la criatura.</summary>
    private Vec2 FoodLocal(MealPlan plan)
    {
        var center = plan.Food.Body.Feet - new Vec2(0, FoodRadius);
        return CreatureSpace.ToLocal(center, Animator.Rig, Body.Feet, Unit, Visual.Squash * (1 + Animator.Rig.Stretch), Visual.Lean);
    }

    /// <summary>Delante del pecho (con dos manos) o junto al moflete (con una).</summary>
    private Vec2 ChestPoint(MealPlan plan) => plan.TwoHands
        ? new(plan.Side * 0.06 + Visual.Facing * 0.08, -0.60)
        : new(plan.Side * 0.56 + Visual.Facing * 0.05, -0.80);

    /// <summary>Junto a la boca: ahí se acerca la comida para morder (o el borde del vaso para beber).</summary>
    private Vec2 MouthFoodPoint(MealPlan plan)
    {
        var r = FoodRadius / Unit;
        var mouth = new Vec2(Visual.Facing * 0.144, -0.76 - 0.03 * Rig.Grow(Form));
        if (plan.Drinks)
        {
            return mouth + new Vec2(plan.Side * 0.06, r * 0.85);
        }

        return plan.TwoHands
            ? mouth + new Vec2(plan.Side * r * 0.45, r * 0.45)
            : mouth + new Vec2(plan.Side * (r + 0.05), 0.0);
    }
}
