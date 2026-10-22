// Kobii: criatura dibujada con campos de distancia (SDF) en un único pixel shader.
//
// Espacio de la criatura: origen en los pies, x a la derecha, y hacia abajo, en «unidades» (la criatura
// mide 2 unidades de alto). La salida es color premultiplicado (sRGB) para componerla con transparencia.

cbuffer Frame : register(b0)
{
    float2 Resolution;
    float2 Origin;        // pies de la criatura, en píxeles del lienzo

    float Unit;           // píxeles por unidad
    float Time;
    float Form;           // 0 huevo, 1 cría, 2 pequeño, 3 joven, 4 adulto
    float Hatch;          // animación de nacimiento 0..1 (negativo: ninguna)

    float2 Look;          // mirada -1..1
    float Blink;          // 0 abiertos, 1 cerrados
    float Squash;         // 1 normal, <1 aplastado, >1 estirado

    float Lean;           // inclinación (radianes)
    float Mouth;          // apertura al hablar o comer
    float Expression;     // índice de Kobii.Core.Creature.Expression
    float Blush;

    float Glow;           // calor del huevo
    float Crack;          // grietas del huevo 0..1
    float Wobble;         // balanceo del huevo (radianes)
    float Facing;         // -1 izquierda .. 1 derecha

    float WalkPhase;
    float Walk;           // intensidad del paso
    float Opacity;
    float Antenna;        // brillo del brote al pensar

    float Flash;          // destello al crecer
    float ShadowDrop;     // distancia (unidades) de los pies al suelo
    float ShadowAlpha;
    float Shape;          // familia de formas (especie)

    float Pale;           // enfermo o débil: pálido y verdoso
    float Dirt;           // suciedad 0..1: manchas
    float Ghost;          // despedida 0..1: sube como un fantasmita (0: ninguna)
    float Wet;            // mojado tras el baño

    // Esqueleto (Kobii.Core.Animation.Rig): desplazamientos respecto al reposo; todo a 0 es estar de pie.
    float RigOffsetX;     // desplazamiento del cuerpo entero
    float RigOffsetY;
    float Spin;           // giro alrededor del centro del cuerpo
    float Hunch;          // encorvado (mayores)

    float HandLX;         // manos, respecto a su sitio
    float HandLY;
    float HandRX;
    float HandRY;

    float FrontL;         // la mano va por delante del cuerpo
    float FrontR;
    float ShowArms;       // bracitos aunque sea una cría
    float FeetFront;      // pies por delante (sentado)

    float FootLX;
    float FootLY;
    float FootRX;
    float FootRY;

    float EarL;           // giro de las orejas
    float EarR;
    float SproutBend;     // curvatura extra del brote
    float HeadTilt;       // inclinación de la cabeza

    float FaceX;          // desplazamiento de la cara
    float FaceY;
    float Elder;          // lo avanzada que está su vejez (forma 5)
    float Shine;          // pelo brillante después de cepillarle (0..1)

    float ElbowL;         // giro del codo respecto a su sitio natural
    float ElbowR;
    float GripL;          // mano cerrada (agarrando algo)
    float GripR;

    float FootLTurn;      // giro de cada pie al dar el paso
    float FootRTurn;
    float MouthOpen;      // boca abierta de par en par (a punto de morder)
    float Chew;           // mandíbula al masticar

    float Cheeks;         // mofletes llenos
    float TongueOut;      // lengua fuera relamiéndose (el signo dice el lado)
    float PassMode;       // 0 la criatura entera; 1 solo las manos que sujetan algo (por encima de los objetos)
    float ClipX;          // escondite: no se dibuja más allá de esta línea (x o y, en píxeles del lienzo)…

    float ClipSide;       // …por este lado (±1 izquierda/derecha, ±2 arriba/abajo, 0 nada)
    float HandOverL;      // en la segunda pasada, esta mano va por encima de lo que sujeta
    float HandOverR;
    float Pajamas;        // pijama puesto (modo oscuro) 0..1

    float Nightcap;       // gorro de dormir 0..1 (al ponérselo baja desde arriba)
    float CapPad1;
    float CapPad2;
    float CapPad3;

    float4 BodyColor;
    float4 BellyColor;
    float4 AccentColor;
    float4 ShellColor;
    float4 SpotColor;
};

// Expresiones (mismo orden que el enum de C#).
static const int ExNeutral = 0, ExHappy = 1, ExJoy = 2, ExLove = 3, ExSad = 4, ExSurprised = 5, ExCurious = 6,
    ExSleepy = 7, ExSleeping = 8, ExEating = 9, ExAngry = 10, ExConfused = 11, ExThinking = 12, ExDizzy = 13,
    ExSick = 14, ExDisgusted = 15, ExPain = 16, ExYawn = 17, ExWink = 18;

static const float3 EyeDark = float3(0.17, 0.14, 0.20);
static const float3 MouthDark = float3(0.42, 0.15, 0.22);
static const float3 Tongue = float3(1.00, 0.55, 0.60);
static const float3 BlushPink = float3(1.00, 0.56, 0.64);
static const float3 SickGreen = float3(0.74, 0.84, 0.62);
static const float3 DirtBrown = float3(0.45, 0.33, 0.22);
static const float3 GhostBlue = float3(0.86, 0.93, 1.00);
static const float3 HaloGold = float3(1.00, 0.86, 0.35);
static const float3 BrowWhite = float3(0.97, 0.96, 0.93);
static const float3 AutumnLeaf = float3(0.88, 0.50, 0.20);
static const float2 BodyPivot = float2(0.0, -0.95);
static const float2 HeadCenter = float2(0.0, -1.0);

float4 VSMain(uint id : SV_VertexID) : SV_Position
{
    float2 uv = float2((id << 1) & 2, id & 2);
    return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}

// ---------------------------------------------------------------- Primitivas

float2 Rot(float2 p, float a)
{
    float c = cos(a), s = sin(a);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

float SdCircle(float2 p, float r) { return length(p) - r; }

float SdEllipse(float2 p, float2 r)
{
    float k0 = length(p / r);
    float k1 = length(p / (r * r));
    return k0 * (k0 - 1.0) / max(k1, 1e-5);
}

float SdSegment(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float h = saturate(dot(pa, ba) / dot(ba, ba));
    return length(pa - ba * h);
}

float SdBox(float2 p, float2 b)
{
    float2 d = abs(p) - b;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}

// Ojo apretado «>» (side = -1, ojo izquierdo) o «<» (side = 1): la punta mira hacia la nariz.
float SdChevron(float2 p, float side)
{
    float dir = -side;
    float2 tip = float2(0.075 * dir, 0.0);
    return min(SdSegment(p, float2(-0.055 * dir, -0.075), tip), SdSegment(p, float2(-0.055 * dir, 0.075), tip));
}

float SMin(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// Huevo exacto de Inigo Quilez (y hacia arriba): círculo inferior ra en el origen, superior rb a altura he.
float SdEggUp(float2 p, float he, float ra, float rb)
{
    float ce = 0.5 * (he * he - (ra - rb) * (ra - rb)) / (ra - rb);
    p.x = abs(p.x);
    if (p.y < 0.0) return length(p) - ra;
    if (p.y * ce - p.x * he > he * ce) return length(float2(p.x, p.y - he)) - rb;
    return length(float2(p.x + ce, p.y)) - (ce + ra);
}

float Dot2(float2 v) { return dot(v, v); }

// Corazón de Inigo Quilez (y hacia arriba, de 0 a ~1).
float SdHeartUp(float2 p)
{
    p.x = abs(p.x);
    if (p.y + p.x > 1.0) return sqrt(Dot2(p - float2(0.25, 0.75))) - sqrt(2.0) / 4.0;
    return sqrt(min(Dot2(p - float2(0.0, 1.0)), Dot2(p - 0.5 * max(p.x + p.y, 0.0)))) * sign(p.x - p.y);
}

// Arco: trozo de anillo de radio r y grosor t; up=1 conserva la mitad superior, up=-1 la inferior.
float SdArc(float2 p, float2 c, float r, float t, float up)
{
    float2 q = p - c;
    float ring = abs(length(q) - r) - t;
    float sideOf = up * q.y; // dentro si q.y va en el sentido pedido (y hacia abajo)
    return max(ring, sideOf);
}

float Fill(float d, float aa) { return saturate(0.5 - d / aa); }

float4 Layer(float3 color, float alpha) { return float4(color * alpha, alpha); }

float4 Over(float4 dst, float4 src) { return src + dst * (1.0 - src.a); }

float Tri(float t) { return abs(frac(t) * 2.0 - 1.0) * 2.0 - 1.0; }

// ---------------------------------------------------------------- Formas de Kobii

// 0 cría .. 1 adulto (y mayor).
float GrowOf(float form) { return saturate((form - 1.0) / 3.0); }

// Lo mayor que se ve: la forma 5 ya lo es un poco y la vejez avanzada lo acentúa.
float ElderLook() { return Form > 4.5 ? 0.55 + 0.45 * saturate(Elder) : 0.0; }

// Encorvado: la cabeza se adelanta (hacia donde mira) y baja un poco.
float2 HunchShift() { return float2(clamp(Facing * 2.0, -1.0, 1.0) * 0.12, 0.09) * Hunch; }

// Espacio de la cabeza: se mueve con el encorvado y se inclina (orejas, brote y cara).
float2 HeadSpace(float2 q) { return Rot(q - HunchShift() - HeadCenter, -HeadTilt) + HeadCenter; }

// Manos y hombros (las posiciones de reposo son las mismas que en Kobii.Core.Animation.Rig).
static const float ArmLength = 0.70;

// Al girarse hacia un lado, los hombros se desplazan con la cara (el de atrás se adelanta): vista de tres cuartos.
float2 ShoulderPos(float side, float form) { return float2(side * (0.70 - 0.04 * GrowOf(form)) + Facing * 0.14, -0.92); }

float2 HandPos(float side, float form)
{
    float2 rest = float2(side * (0.96 - 0.04 * GrowOf(form)), -0.54);
    return rest + (side < 0.0 ? float2(HandLX, HandLY) : float2(HandRX, HandRY));
}

float FrontOf(float side) { return side < 0.0 ? FrontL : FrontR; }

float GripOf(float side) { return saturate(side < 0.0 ? GripL : GripR); }

// Las crías no sacan los bracitos salvo para algún gesto (y entonces salen poco a poco).
float ArmScale(float form) { return form >= 1.5 ? 1.0 : saturate(ShowArms); }

// Curva de Bézier cuadrática: distancia al punto más cercano y su posición en la curva, t (Inigo Quilez).
float2 SdBezier(float2 pos, float2 A, float2 B, float2 C)
{
    float2 a = B - A;
    float2 b = A - 2.0 * B + C;
    float2 c = a * 2.0;
    float2 d = A - pos;
    float bb = dot(b, b);
    if (bb < 1e-6)
    {
        // Brazo estirado: una recta.
        float2 pa = pos - A, ca = C - A;
        float h = saturate(dot(pa, ca) / max(dot(ca, ca), 1e-6));
        return float2(length(pa - ca * h), h);
    }

    float kk = 1.0 / bb;
    float kx = kk * dot(a, b);
    float ky = kk * (2.0 * dot(a, a) + dot(d, b)) / 3.0;
    float kz = kk * dot(d, a);
    float p = ky - kx * kx;
    float q = kx * (2.0 * kx * kx - 3.0 * ky) + kz;
    float h = q * q + 4.0 * p * p * p;
    if (h >= 0.0)
    {
        h = sqrt(h);
        float2 x = (float2(h, -h) - q) / 2.0;
        float2 uv = sign(x) * pow(abs(x), 1.0 / 3.0);
        float t = saturate(uv.x + uv.y - kx);
        return float2(length(d + (c + b * t) * t), t);
    }

    float z = sqrt(-p);
    float v = acos(clamp(q / (p * z * 2.0), -1.0, 1.0)) / 3.0;
    float m = cos(v);
    float n = sin(v) * 1.732050808;
    float2 t2 = saturate(float2(m + m, -n - m) * z - kx);
    float2 q0 = d + (c + b * t2.x) * t2.x;
    float2 q1 = d + (c + b * t2.y) * t2.y;
    float d0 = dot(q0, q0), d1 = dot(q1, q1);
    return d0 < d1 ? float2(sqrt(d0), t2.x) : float2(sqrt(d1), t2.y);
}

// Punto de control del brazo: si la mano está más cerca que el largo del brazo, el codo sale hacia fuera
// y abajo (girado lo que diga ElbowL/R). Es el mismo cálculo que Rig.Elbow en C#.
float2 ArmControl(float side, float form, out float2 shoulder, out float2 hand)
{
    shoulder = ShoulderPos(side, form);
    hand = HandPos(side, form);
    float2 reach = hand - shoulder;
    float d = length(reach);
    float2 middle = (shoulder + hand) * 0.5;
    if (d < 1e-4) return middle;
    // En reposo el brazo cuelga casi recto (si no, parecerían alitas); se dobla del todo al apartar la mano.
    float2 rest = float2(side * (0.96 - 0.04 * GrowOf(form)), -0.54);
    float h = sqrt(max(0.0, ArmLength * ArmLength * 0.25 - d * d * 0.25)) * (0.2 + 0.8 * smoothstep(0.0, 0.3, length(hand - rest)));
    float2 normal = float2(-reach.y, reach.x) / d;
    float2 outward = Rot(float2(side, 0.7), side * (side < 0.0 ? ElbowL : ElbowR));
    if (dot(normal, outward) < 0.0) normal = -normal;
    return middle + normal * h * 2.0;
}

// Lo que no cambia de un píxel a otro de cada brazo se calcula una sola vez (PrepareArms): así el shader se
// compila deprisa y cuesta poco.
struct ArmShape
{
    float2 shoulder;
    float2 control;
    float2 hand;
    float2 dir;
    float2 across;
    float thumbSide;
    float scale;
    float grip;
};

static ArmShape ArmLeft;
static ArmShape ArmRight;

ArmShape MakeArm(float side, float form)
{
    ArmShape a;
    a.scale = ArmScale(form);
    a.control = ArmControl(side, form, a.shoulder, a.hand);
    float2 dir = a.hand - a.control;
    a.dir = dot(dir, dir) > 1e-8 ? normalize(dir) : float2(side, 0.0);
    a.across = float2(-a.dir.y, a.dir.x);
    a.thumbSide = dot(a.across, float2(-side * 0.6, -1.0)) >= 0.0 ? 1.0 : -1.0;
    a.grip = GripOf(side);
    return a;
}

void PrepareArms(float form)
{
    ArmLeft = MakeArm(-1.0, form);
    ArmRight = MakeArm(1.0, form);
}

ArmShape ArmOf(float side)
{
    if (side < 0.0) return ArmLeft;
    return ArmRight;
}

// El brazo: un tubo curvo, más gordito en el hombro que en la muñeca.
float SdArmTube(float2 q, float side, float form)
{
    ArmShape a = ArmOf(side);
    if (a.scale < 0.02) return 1e3;
    float2 bt = SdBezier(q, a.shoulder, a.control, a.hand);
    return bt.x - lerp(0.12, 0.085, bt.y) * a.scale;
}

// La mano: una manopla alargada en la dirección del antebrazo, con el pulgar hacia arriba y hacia dentro.
// Al cerrarse (agarrar algo) se hace un puño redondo con el pulgar por delante.
float SdHand(float2 q, float side, float form)
{
    ArmShape a = ArmOf(side);
    float s = a.scale;
    if (s < 0.02) return 1e3;
    float2 rel = q - a.hand;
    float2 local = float2(dot(rel, a.dir), dot(rel, a.across));
    float grip = a.grip;
    float palm = SdEllipse(local - float2(0.03 * (1.0 - grip), 0.0) * s, float2(lerp(0.155, 0.13, grip), lerp(0.12, 0.125, grip)) * s);
    float2 thumb = float2(lerp(0.0, 0.07, grip), a.thumbSide * lerp(0.115, 0.055, grip)) * s;
    return SMin(palm, SdCircle(local - thumb, lerp(0.055, 0.05, grip) * s), 0.03 * s);
}

float SdArm(float2 q, float side, float form)
{
    return SMin(SdArmTube(q, side, form), SdHand(q, side, form), 0.04);
}

// Cuerpo tipo «mochi»: una bola con la base más ancha. Crece y se estira con la edad.
float SdBody(float2 q, float form)
{
    float grow = GrowOf(form);
    float top = SdCircle(q - float2(0, -0.98 - 0.05 * grow) - HunchShift(), 0.80 - 0.04 * grow);
    float base = SdEllipse(q - float2(0, -0.55), float2(0.92 - 0.06 * grow, 0.52 + 0.04 * grow));
    float d = SMin(top, base, 0.35);

    // Bracitos a los lados (los que van por delante se dibujan aparte, encima). Al pasar por delante, el
    // del lado se mete en el cuerpo mientras aparece el de delante: así no salta de golpe.
    if (FrontL < 0.95) d = SMin(d, SdArm(q, -1.0, form) + 0.35 * smoothstep(0.25, 0.95, FrontL), 0.08);
    if (FrontR < 0.95) d = SMin(d, SdArm(q, 1.0, form) + 0.35 * smoothstep(0.25, 0.95, FrontR), 0.08);

    // Mofletes llenos: la cara se ensancha a la altura de la boca.
    if (Cheeks > 0.01)
    {
        float2 h = HeadSpace(q);
        float shift = Facing * 0.16 * 0.7;
        float puff = SMin(SdCircle(h - float2(-0.60 + shift, -0.80 - 0.03 * grow), 0.27 * Cheeks), SdCircle(h - float2(0.60 + shift, -0.80 - 0.03 * grow), 0.27 * Cheeks), 0.05);
        d = SMin(d, puff, 0.12);
    }

    if (form >= 2.5)
    {
        // Orejitas (cada una con su giro).
        float2 h = HeadSpace(q);
        float2 earR = h - float2(0.48, -1.72);
        float2 earL = float2(-h.x, h.y) - float2(0.48, -1.72);
        d = SMin(d, SdEllipse(Rot(earR, 0.45 + EarR), float2(0.15, 0.24)), 0.12);
        d = SMin(d, SdEllipse(Rot(earL, 0.45 + EarL), float2(0.15, 0.24)), 0.12);
    }

    return d;
}

// Pies: el paso (levantar, avanzar y girar la punta) lo calcula el esqueleto.
float SdFeet(float2 q)
{
    float l = SdEllipse(Rot(q - float2(-0.40 + FootLX, -0.10 + FootLY), -FootLTurn), float2(0.25, 0.14));
    float r = SdEllipse(Rot(q - float2(0.40 + FootRX, -0.10 + FootRY), -FootRTurn), float2(0.25, 0.14));
    return min(l, r);
}

// Brote de la cabeza: tallo curvo con una yema (cría) o una hoja (mayores). De mayor se dobla un poco.
float SproutSway() { return 0.10 * sin(Time * 1.7) * 0.5 + SproutBend + 0.10 * ElderLook(); }

float SdSproutStem(float2 q, float top)
{
    return SdSegment(q, float2(0, top + 0.08), float2(0.10 + SproutSway(), top - 0.24 + 0.06 * abs(SproutBend))) - 0.035;
}

float2 SproutTip(float top)
{
    return float2(0.10 + SproutSway(), top - 0.26 + 0.06 * abs(SproutBend));
}

// Altura aparente del cuerpo (perfil circular) para el sombreado.
float HeightOf(float d)
{
    float t = saturate(-d / 0.55);
    return sqrt(1.0 - (1.0 - t) * (1.0 - t));
}

float3 Shade(float3 base, float2 q, float d, float form, out float diffuse)
{
    // Normal por diferencias hacia delante (dos muestras más en vez de cuatro: el cuerpo es suave).
    const float e = 0.02;
    float h0 = HeightOf(d);
    float hx = HeightOf(SdBody(q + float2(e, 0), form)) - h0;
    float hy = HeightOf(SdBody(q + float2(0, e), form)) - h0;
    float3 n = normalize(float3(-hx / e * 0.35, -hy / e * 0.35, 1.0));
    float3 l = normalize(float3(-0.45, -0.75, 0.85));
    diffuse = saturate(dot(n, l));
    float3 h = normalize(l + float3(0, 0, 1));
    // Mojado: más oscuro y con un brillo más intenso y concentrado.
    float spec = pow(saturate(dot(n, h)), lerp(36.0, 70.0, max(Wet, 0.6 * Shine))) * (0.38 + 0.55 * Wet + 0.40 * Shine);
    float rim = pow(1.0 - n.z, 2.0) * 0.18;
    float ground = lerp(0.88, 1.0, saturate(-q.y / 0.35));
    return (base * (0.70 + 0.40 * diffuse) * (1.0 - 0.1 * Wet) * (1.0 + 0.06 * Shine) + spec + rim * base * (1.0 + Shine)) * ground;
}

float4 DrawEye(float4 acc, float2 q, float2 c, float2 size, int ex, float blink, float aa, float side)
{
    float lineW = 0.028;
    if (ex == ExSleeping)
    {
        float d = SdArc(q, c - float2(0, 0.05), size.x * 0.85, lineW, -1.0);
        return Over(acc, Layer(EyeDark, Fill(d, aa)));
    }

    // Bostezo: los ojos apretados, más hundidos que dormido.
    if (ex == ExYawn)
    {
        float d = SdArc(q, c - float2(0, 0.02), size.x * 0.75, lineW * 1.25, -1.0);
        return Over(acc, Layer(EyeDark, Fill(d, aa)));
    }

    if (ex == ExJoy || ex == ExEating || (ex == ExHappy && blink > 0.5) || (ex == ExWink && side > 0.0))
    {
        float d = SdArc(q, c + float2(0, 0.07), size.x * 0.85, lineW, 1.0);
        return Over(acc, Layer(EyeDark, Fill(d, aa)));
    }

    if (ex == ExLove)
    {
        float2 hp = (q - c - float2(0, size.y * 0.75)) / (size.y * 1.45);
        float d = SdHeartUp(float2(hp.x, -hp.y)) * size.y * 1.45;
        acc = Over(acc, Layer(float3(0.93, 0.25, 0.38), Fill(d, aa)));
        float hl = SdCircle(q - c - float2(-0.05, -0.04), 0.03);
        return Over(acc, Layer(float3(1, 1, 1), Fill(hl, aa) * 0.9));
    }

    // Dolor: los dos ojos apretados (> <). Asco: solo uno.
    if (ex == ExPain || (ex == ExDisgusted && side < 0.0))
    {
        float d = SdChevron(q - c, side) - lineW;
        return Over(acc, Layer(EyeDark, Fill(d, aa)));
    }

    if (ex == ExDizzy)
    {
        float2 p = q - c;
        float d = min(SdSegment(p, float2(-0.09, -0.09), float2(0.09, 0.09)), SdSegment(p, float2(-0.09, 0.09), float2(0.09, -0.09))) - lineW;
        return Over(acc, Layer(EyeDark, Fill(d, aa)));
    }

    if (ex == ExSurprised)
    {
        float white = SdEllipse(q - c, size * 1.12);
        acc = Over(acc, Layer(EyeDark, Fill(white - 0.025, aa)));
        acc = Over(acc, Layer(float3(1, 1, 1), Fill(white, aa)));
        float pupil = SdCircle(q - c - Look * 0.03, size.x * 0.48);
        acc = Over(acc, Layer(EyeDark, Fill(pupil, aa)));
        float hl = SdCircle(q - c - Look * 0.03 - float2(-0.03, -0.035), 0.022);
        return Over(acc, Layer(float3(1, 1, 1), Fill(hl, aa)));
    }

    // Ojo normal: óvalo oscuro con dos brillos. Al parpadear se aplasta hasta ser una línea.
    float open = ex == ExSleepy ? 0.45 : ex == ExSick ? 0.55 : ex == ExDisgusted ? 0.75 : 1.0;
    if (ex == ExConfused && side > 0) open *= 0.6;
    open *= 1.0 - saturate(blink);
    if (open < 0.12)
    {
        float d = SdArc(q, c - float2(0, 0.04), size.x * 0.8, lineW * 0.9, -1.0);
        return Over(acc, Layer(EyeDark, Fill(d, aa)));
    }

    float2 eyeSize = float2(size.x, size.y * open);
    float d = SdEllipse(q - c, eyeSize);

    // Párpados: tristeza y enfermedad (caídos hacia fuera) y enfado (hacia dentro).
    if (ex == ExSad || ex == ExAngry || ex == ExSick)
    {
        float slope = (ex == ExAngry ? 0.6 : -0.55) * side;
        float lid = (q.y - c.y) + (q.x - c.x) * slope + size.y * 0.35;
        d = max(d, -lid);
    }

    acc = Over(acc, Layer(EyeDark, Fill(d, aa)));
    float big = SdCircle(q - c - float2(-0.045, -0.06) * float2(1, open), 0.052 * min(1.0, open + 0.3));
    float small = SdCircle(q - c - float2(0.045, 0.05) * float2(1, open), 0.024 * open);
    acc = Over(acc, Layer(float3(1, 1, 1), Fill(big, aa)));
    acc = Over(acc, Layer(float3(1, 1, 1), Fill(small, aa) * 0.85));
    return acc;
}

float4 DrawMouth(float4 acc, float2 q, float2 c, int ex, float mouth, float aa)
{
    float lineW = 0.022;
    bool open = ex == ExJoy || ex == ExLove || mouth > 0.08;
    float2 m = q - c;

    // Comiendo: la boca abierta de par en par justo antes del mordisco…
    if (MouthOpen > 0.05)
    {
        float tall = 0.025 + 0.10 * MouthOpen;
        float wide = 0.065 + 0.05 * MouthOpen;
        float2 center = float2(0, 0.01 + 0.03 * MouthOpen);
        float bite = SdEllipse(m - center, float2(wide, tall));
        acc = Over(acc, Layer(MouthDark, Fill(bite, aa)));
        float tongue = max(SdEllipse(m - center - float2(0, tall * 0.6), float2(wide * 0.6, tall * 0.5)), bite);
        return Over(acc, Layer(Tongue, Fill(tongue, aa)));
    }

    // …y masticando con la boca llena: cerrada y pequeñita, sube y baja con la mandíbula.
    if (Chew > 0.001 || Cheeks > 0.15)
    {
        float jaw = saturate(Chew);
        float munch = SdEllipse(m - float2(0, 0.015 * jaw), float2(0.05 - 0.012 * jaw, 0.008 + 0.034 * jaw));
        return Over(acc, Layer(MouthDark, Fill(munch - lineW * 0.4 * (1.0 - jaw), aa)));
    }

    // Bostezo: la boca muy abierta, con la lengua.
    if (ex == ExYawn)
    {
        float yawn = SdEllipse(m - float2(0, 0.02), float2(0.085, 0.12));
        acc = Over(acc, Layer(MouthDark, Fill(yawn, aa)));
        float tongue = max(SdEllipse(m - float2(0, 0.10), float2(0.055, 0.05)), yawn);
        return Over(acc, Layer(Tongue, Fill(tongue, aa)));
    }

    // Dolor: dientes apretados.
    if (ex == ExPain)
    {
        float teeth = SdBox(m, float2(0.075, 0.032)) - 0.012;
        acc = Over(acc, Layer(MouthDark, Fill(teeth - lineW, aa)));
        acc = Over(acc, Layer(float3(1, 1, 1), Fill(teeth, aa)));
        float gap = max(abs(m.y) - 0.006, teeth + 0.004);
        return Over(acc, Layer(MouthDark, Fill(gap, aa)));
    }

    // Asco: boca torcida con la lengua fuera.
    if (ex == ExDisgusted)
    {
        float tongue = max(SdEllipse(m - float2(0.025, 0.04), float2(0.042, 0.05)), -m.y);
        acc = Over(acc, Layer(MouthDark, Fill(tongue - lineW, aa)));
        acc = Over(acc, Layer(Tongue, Fill(tongue, aa)));
        float lip = SdSegment(m, float2(-0.07, 0.012), float2(0.07, -0.008)) - lineW;
        return Over(acc, Layer(MouthDark, Fill(lip, aa)));
    }

    // Enfermo: boca ondulada (si no está hablando).
    if (ex == ExSick && !open)
    {
        float wave = max(abs(m.y - 0.016 * sin(m.x * 60.0)) - lineW * 0.9, abs(m.x) - 0.085);
        return Over(acc, Layer(MouthDark, Fill(wave, aa)));
    }

    if (ex == ExSurprised || ex == ExDizzy)
    {
        float d = SdEllipse(q - c, float2(0.055, 0.07));
        return Over(acc, Layer(MouthDark, Fill(d, aa)));
    }

    if (open)
    {
        float h = (ex == ExJoy || ex == ExLove) ? max(0.10, 0.05 + 0.09 * mouth) : 0.03 + 0.09 * mouth;
        float w = (ex == ExJoy || ex == ExLove) ? 0.13 : 0.085;
        float2 p = q - c;
        // Media elipse (la parte de abajo) para la sonrisa abierta; elipse entera al hablar.
        float d = (ex == ExJoy || ex == ExLove) ? max(SdEllipse(p - float2(0, -0.01), float2(w, h)), -p.y - 0.01) : SdEllipse(p, float2(w, h));
        acc = Over(acc, Layer(MouthDark, Fill(d, aa)));
        float tongue = max(SdEllipse(p - float2(0, h * 0.75), float2(w * 0.6, h * 0.55)), d);
        return Over(acc, Layer(Tongue, Fill(tongue, aa)));
    }

    if (ex == ExSad)
    {
        float d = SdArc(q, c + float2(0, 0.06), 0.075, lineW, 1.0);
        return Over(acc, Layer(MouthDark, Fill(d, aa)));
    }

    if (ex == ExAngry || ex == ExConfused)
    {
        float d = SdSegment(q - c, float2(-0.06, 0.01), float2(0.06, -0.01)) - lineW;
        return Over(acc, Layer(MouthDark, Fill(d, aa)));
    }

    if (ex == ExSleeping)
    {
        float d = SdEllipse(q - c - float2(0, 0.01), float2(0.03, 0.025 + 0.01 * sin(Time * 2.0)));
        return Over(acc, Layer(MouthDark, Fill(d, aa)));
    }

    float r = (ex == ExHappy || ex == ExEating || ex == ExWink) ? 0.085 : 0.065;
    float d = SdArc(q, c - float2(0, 0.03), r, lineW, -1.0);
    return Over(acc, Layer(MouthDark, Fill(d, aa)));
}

// ---------------------------------------------------------------- Pijama y gorro de dormir (modo oscuro)

// Color del pijama: el de los detalles de su paleta, en tono pastel.
float3 PajamaColor() { return lerp(AccentColor.rgb, float3(1.0, 1.0, 1.0), 0.45); }

static const float3 PajamaTrim = float3(0.98, 0.97, 0.95);

// Tela de rayas (verticales, que se abren un poco con la curva de la barriga).
float3 PajamaCloth(float2 q, float diffuse, float shift)
{
    float3 cloth = PajamaColor();
    float x = (q.x - shift * 0.6) / (1.0 + 0.25 * abs(q.y + 0.5));
    float stripe = smoothstep(0.35, 0.65, 0.5 + 0.5 * sin(x * 22.0));
    cloth = lerp(cloth, lerp(cloth, float3(1.0, 1.0, 1.0), 0.55), stripe * 0.6);
    return cloth * (0.80 + 0.26 * diffuse);
}

// La camiseta del pijama: todo el cuerpo por debajo de un cuello en pico (menos las manos), con su ribete y
// dos botones. El cuello sigue a la cara cuando se gira.
float4 DrawPajamas(float4 acc, float2 q, float d, float form, float diffuse, float shift, float aa)
{
    float cx = shift * 0.8;
    float collar = -0.52 - 0.16 * saturate(abs(q.x - cx) / 0.42);
    float hands = min(SdHand(q, -1.0, form), SdHand(q, 1.0, form));
    float shirt = max(max(d, collar - q.y), -hands);
    acc = Over(acc, Layer(PajamaCloth(q, diffuse, shift), Fill(shirt, aa) * Pajamas));
    float trim = max(max(abs(q.y - collar) - 0.035, d + 0.02), -hands);
    acc = Over(acc, Layer(PajamaTrim * (0.85 + 0.2 * diffuse), Fill(trim, aa) * Pajamas));
    float buttons = min(SdCircle(q - float2(cx * 0.7, -0.36), 0.045), SdCircle(q - float2(cx * 0.6, -0.16), 0.045));
    return Over(acc, Layer(AccentColor.rgb * 0.75, Fill(max(buttons, d + 0.03), aa) * Pajamas));
}

// El gorro de dormir: tapa lo alto de la cabeza (entre las orejas) y cae hacia un lado, meciéndose, con su
// pompón. Al ponérselo baja desde arriba; al quitárselo, sube.
float4 DrawNightcap(float4 acc, float2 h, float form, float aa)
{
    float shown = saturate(Nightcap * 3.0);
    float2 p = h + float2(0.0, (1.0 - saturate(Nightcap)) * 0.9);
    float grow = GrowOf(form);
    float headY = -0.98 - 0.05 * grow;
    float headR = 0.80 - 0.04 * grow;
    float brimY = headY - headR * 0.74;
    float sway = 0.035 * sin(Time * 1.3);

    float dome = max(SdCircle(p - float2(0.0, headY), headR + 0.03), p.y - brimY);
    float c1 = SdCircle(p - float2(-0.08, brimY - 0.28), 0.34);
    float c2 = SdCircle(p - float2(-0.34 + sway, brimY - 0.50), 0.21);
    float c3 = SdCircle(p - float2(-0.60 + sway * 1.5, brimY - 0.52), 0.13);
    float c4 = SdCircle(p - float2(-0.78 + sway * 2.0, brimY - 0.38), 0.08);
    float cap = SMin(dome, SMin(SMin(SMin(c1, c2, 0.14), c3, 0.12), c4, 0.10), 0.10);
    float brim = SdEllipse(p - float2(0.0, brimY + 0.02), float2(headR * 0.74, 0.10));
    float pom = SdCircle(p - float2(-0.84 + sway * 2.0, brimY - 0.24), 0.12);

    // Rayas que dan la vuelta al gorro y una luz suave desde arriba a la izquierda.
    float3 cloth = PajamaColor();
    float rings = smoothstep(0.4, 0.6, 0.5 + 0.5 * sin((p.y - brimY) * 26.0 + p.x * 6.0));
    cloth = lerp(cloth, lerp(cloth, float3(1.0, 1.0, 1.0), 0.55), rings * 0.6);
    float light = 0.84 + 0.22 * saturate(0.5 - (p.x + 0.1) * 0.6 - (p.y - brimY) * 0.4);
    float all = min(min(cap, brim), pom);
    acc = Over(acc, Layer(PajamaColor() * 0.42, Fill(all - 0.045, aa) * shown));
    acc = Over(acc, Layer(cloth * light, Fill(cap, aa) * shown));
    acc = Over(acc, Layer(PajamaTrim * (0.9 + 0.1 * light), Fill(brim, aa) * shown));
    return Over(acc, Layer(float3(1.0, 1.0, 1.0), Fill(pom, aa) * shown));
}

// Color de un brazo que va por delante: algo más claro hacia la mano, de donde viene la luz.
float3 ArmColor(float2 q, float side, float form, float3 body)
{
    float2 hand = HandPos(side, form);
    float lit = saturate(0.5 - dot(q - hand, float2(0.55, 0.85)) * 2.5);
    return body * (0.88 + 0.22 * lit);
}

// Un brazo por delante del cuerpo (aplaudir, taparse los ojos, sujetar un libro…), con su contorno. Aparece
// fundiéndose mientras el del lado se mete en el cuerpo.
float4 DrawFrontArm(float4 acc, float2 q, float side, float form, float3 body, float3 outline, float aa)
{
    float shown = smoothstep(0.05, 0.75, FrontOf(side));
    if (shown <= 0.0) return acc;
    float d = SdArm(q, side, form);
    if (d > 0.2) return acc;
    acc = Over(acc, Layer(outline, Fill(d - max(0.045, 1.2 * aa), aa) * shown));
    acc = Over(acc, Layer(ArmColor(q, side, form, body), Fill(d, aa) * shown));
    if (Pajamas > 0.01)
    {
        // La manga del pijama (hasta la muñeca).
        float sleeve = max(SdArmTube(q, side, form), -SdHand(q, side, form));
        acc = Over(acc, Layer(PajamaCloth(q, 0.6, 0.0), Fill(sleeve, aa) * shown * Pajamas));
    }

    return acc;
}

// Segunda pasada: la mano que agarra algo se dibuja otra vez por encima del objeto (que se pinta entre las
// dos pasadas), así los dedos lo rodean; el antebrazo se queda detrás. Si el brazo va por delante, la mano
// lleva su contorno salvo en la muñeca (donde sigue el brazo); si va a un lado, solo el contorno del cuerpo.
float4 DrawHandOver(float4 acc, float2 q, float side, float form, float3 body, float3 outline, float aa)
{
    float hand = SdHand(q, side, form);
    if (hand > 0.2) return acc;
    if (FrontOf(side) >= 0.5)
    {
        float w = max(0.045, 1.2 * aa);
        float wrist = Fill(SdArmTube(q, side, form) + w * 0.5, aa);
        acc = Over(acc, Layer(outline, Fill(hand - w, aa) * (1.0 - Fill(hand, aa)) * (1.0 - wrist)));
        return Over(acc, Layer(ArmColor(q, side, form, body), Fill(hand, aa)));
    }

    float d = SdBody(q, form);
    float outlineW = max(0.05, 1.3 * aa);
    float near = Fill(hand - outlineW * 1.5, aa);
    acc = Over(acc, Layer(outline, Fill(d - outlineW, aa) * (1.0 - Fill(d, aa)) * near));
    float diffuse;
    float3 shaded = Shade(body, q, d, form, diffuse);
    return Over(acc, Layer(shaded, Fill(max(hand, d), aa)));
}

// Rasgos de mayor: cejas blancas y pobladas que caen hacia fuera y patas de gallo.
float4 DrawElderFace(float4 acc, float2 f, float2 eye, float side, float3 body, float elder, float aa)
{
    float2 brow = Rot(f - eye - float2(side * 0.02, -0.215), -side * 0.28);
    float tuft = min(SdEllipse(brow, float2(0.12, 0.045)), SdCircle(brow - float2(side * 0.08, 0.025), 0.045));
    acc = Over(acc, Layer(body * 0.45, Fill(tuft - 0.018, aa) * elder));
    acc = Over(acc, Layer(BrowWhite, Fill(tuft, aa) * elder));

    float2 o = eye + float2(side * 0.17, 0.0);
    float lines = min(SdSegment(f, o, o + float2(side * 0.07, -0.035)), SdSegment(f, o + float2(0, 0.03), o + float2(side * 0.07, 0.055))) - 0.011;
    return Over(acc, Layer(body * 0.42, Fill(lines, aa) * 0.6 * elder));
}

// Color del cuerpo: enfermo o débil se apaga y tira a verde; de mayor se aclara y pierde color.
float3 BodyTint()
{
    float elder = ElderLook();
    float3 body = BodyColor.rgb;
    float lum = dot(body, float3(0.30, 0.59, 0.11));
    body = lerp(body, lerp(body, float3(lum, lum, lum) * 1.06 + 0.07, 0.65), 0.45 * elder);
    return lerp(body, lerp(float3(lum, lum, lum), SickGreen * (0.55 + 0.5 * lum), 0.6), Pale * 0.6);
}

// Segunda pasada: solo las manos que agarran algo.
float4 DrawHandsOver(float4 acc, float2 q, float form, float aa)
{
    PrepareArms(form);
    float3 body = BodyTint();
    float3 outline = body * 0.42;
    if (HandOverL > 0.5) acc = DrawHandOver(acc, q, -1.0, form, body, outline, aa);
    if (HandOverR > 0.5) acc = DrawHandOver(acc, q, 1.0, form, body, outline, aa);
    return acc;
}

// Kobii completo (cuerpo, cara, brote) en espacio local q. scale encoge para la animación de nacimiento.
float4 DrawKobii(float4 acc, float2 q, float form, float aa, float pop)
{
    q /= pop;
    aa /= pop;
    PrepareArms(form);
    int ex = (int)round(Expression);
    float elder = ElderLook();
    float3 body = BodyTint();
    float3 outline = body * 0.42;

    // Patas (detrás del cuerpo; sentado, por delante).
    float feet = SdFeet(q);
    float3 feetColor = body * 0.82;
    if (FeetFront < 0.5)
    {
        acc = Over(acc, Layer(outline, Fill(feet - 0.05, aa)));
        acc = Over(acc, Layer(feetColor, Fill(feet, aa)));
    }

    // Brote: tallo y yema u hoja. La yema se ilumina cuando piensa; de mayor, la hoja es de otoño.
    float2 h = HeadSpace(q);
    float grow = GrowOf(form);
    float top = -1.72 - 0.08 * grow;
    float stem = SdSproutStem(h, top);
    float2 tip = SproutTip(top);
    float budSize = 0.10 + 0.04 * grow;
    float bud = form >= 1.5
        ? SdEllipse(Rot(h - tip - float2(0.10, -0.02), -0.6 - 0.5 * elder), float2(budSize * 1.7, budSize * 0.8))
        : SdCircle(h - tip, budSize);
    float3 budColor = lerp(lerp(AccentColor.rgb, AutumnLeaf, 0.85 * elder), float3(1.0, 0.95, 0.55), Antenna);
    if (Antenna > 0.01)
    {
        float halo = exp(-max(bud, 0.0) * 9.0) * Antenna * 0.6;
        acc = Over(acc, Layer(float3(1.0, 0.92, 0.45), halo));
    }

    float sprout = min(stem, bud);
    acc = Over(acc, Layer(outline, Fill(sprout - 0.045, aa)));
    acc = Over(acc, Layer(body * 0.9, Fill(stem, aa)));
    acc = Over(acc, Layer(budColor, Fill(bud, aa)));

    // Cuerpo con contorno y sombreado de vinilo blando.
    float d = SdBody(q, form);
    float outlineW = max(0.05, 1.3 * aa);
    acc = Over(acc, Layer(outline, Fill(d - outlineW, aa)));
    float diffuse;
    float3 shaded = Shade(body, q, d, form, diffuse);
    acc = Over(acc, Layer(shaded, Fill(d, aa)));

    // Barriga más clara.
    float belly = max(SdEllipse(q - float2(0, -0.50), float2(0.56, 0.38)), d + 0.10);
    float3 bellyShaded = lerp(BellyColor.rgb, SickGreen, Pale * 0.3) * (0.82 + 0.25 * diffuse);
    acc = Over(acc, Layer(bellyShaded, Fill(belly, aa * 3.0) * 0.9));

    // En modo oscuro, con pijama.
    if (Pajamas > 0.01)
    {
        acc = DrawPajamas(acc, q, d, form, diffuse, Facing * 0.16 + Look.x * 0.05, aa);
    }

    // Recién cepillado: destellos que titilan sobre el pelo.
    if (Shine > 0.05)
    {
        static const float3 glints[4] =
        {
            float3(-0.46, -1.36, 0.0), float3(0.52, -0.82, 1.7), float3(0.18, -1.58, 3.1), float3(-0.62, -0.62, 4.6),
        };
        [unroll]
        for (int g = 0; g < 4; g++)
        {
            float2 gp = q - glints[g].xy;
            float twinkle = saturate(sin(Time * 2.3 + glints[g].z) * 1.6 - 0.6);
            float size = 0.09 * twinkle;
            float star = min(SdEllipse(gp, float2(size, size * 0.18) + 0.001), SdEllipse(gp, float2(size * 0.18, size) + 0.001));
            acc = Over(acc, Layer(float3(1, 1, 1), Fill(max(star, d + 0.04), aa) * Shine * twinkle));
        }
    }

    // Suciedad: manchas que van apareciendo de una en una.
    if (Dirt > 0.01)
    {
        static const float2 smudges[6] =
        {
            float2(-0.50, -0.38), float2(0.55, -0.60), float2(0.18, -1.38),
            float2(-0.32, -1.52), float2(0.32, -0.22), float2(-0.66, -0.95),
        };
        static const float2 sizes[6] =
        {
            float2(0.13, 0.08), float2(0.10, 0.07), float2(0.09, 0.06),
            float2(0.11, 0.06), float2(0.08, 0.05), float2(0.07, 0.09),
        };
        [unroll]
        for (int i = 0; i < 6; i++)
        {
            float shown = saturate((Dirt - i / 6.0) * 5.0);
            float smudge = max(SdEllipse(Rot(q - smudges[i], 0.4 * i), sizes[i]), d + 0.05);
            acc = Over(acc, Layer(DirtBrown * (0.8 + 0.2 * diffuse), Fill(smudge, aa * 2.5) * 0.6 * shown));
        }
    }

    // Cara: se desplaza hacia donde mira, se inclina con la cabeza y se mueve al asentir o negar.
    float2 f = h - float2(FaceX, FaceY);
    float shift = Facing * 0.16 + Look.x * 0.05;
    float2 eyeSize = float2(0.13, 0.17) * (form >= 1.5 ? 0.92 : 1.0);
    float eyeY = -1.02 - 0.04 * grow + Look.y * 0.04;
    float2 eyeL = float2(-0.31 + shift, eyeY);
    float2 eyeR = float2(0.31 + shift, eyeY);
    if (Pale > 0.25)
    {
        // Ojeras.
        float bags = min(SdArc(f, eyeL + float2(0, 0.09), 0.11, 0.016, -1.0), SdArc(f, eyeR + float2(0, 0.09), 0.11, 0.016, -1.0));
        acc = Over(acc, Layer(body * 0.5, Fill(bags, aa * 1.5) * saturate(Pale - 0.25) * 0.8));
    }

    acc = DrawEye(acc, f, eyeL, eyeSize, ex, Blink, aa, -1.0);
    acc = DrawEye(acc, f, eyeR, eyeSize, ex, Blink, aa, 1.0);
    if (elder > 0.01)
    {
        acc = DrawElderFace(acc, f, eyeL, -1.0, body, elder, aa);
        acc = DrawElderFace(acc, f, eyeR, 1.0, body, elder, aa);
    }

    // Mejillas.
    float blushAlpha = (0.30 + 0.45 * Blush + (ex == ExLove || ex == ExJoy ? 0.2 : 0.0)) * (1.0 - 0.7 * Pale) * (1.0 - 0.3 * elder);
    float2 cheekSize = float2(0.14, 0.075) * (1.0 + float2(0.45, 0.9) * Cheeks);
    float2 cheekPosL = float2(-0.55 - 0.05 * Cheeks + shift * 0.7, -0.80 - 0.03 * grow);
    float2 cheekPosR = float2(0.55 + 0.05 * Cheeks + shift * 0.7, -0.80 - 0.03 * grow);
    float cheekL = SdEllipse(f - cheekPosL, cheekSize);
    float cheekR = SdEllipse(f - cheekPosR, cheekSize);
    acc = Over(acc, Layer(BlushPink, Fill(min(cheekL, cheekR), aa * 4.0) * saturate(blushAlpha + 0.25 * Cheeks)));
    if (Cheeks > 0.05)
    {
        // Brillo en los mofletes hinchados.
        float shine = min(SdCircle(f - cheekPosL - float2(-0.03, -0.05), 0.035), SdCircle(f - cheekPosR - float2(-0.03, -0.05), 0.035));
        acc = Over(acc, Layer(float3(1, 1, 1), Fill(shine, aa * 2.0) * 0.6 * Cheeks));
    }

    float2 mouthAt = float2(shift * 0.9, -0.76 - 0.03 * grow);
    acc = DrawMouth(acc, f, mouthAt, ex, Mouth, aa);

    // Relamerse: la lengua asoma por un lado de la boca.
    if (abs(TongueOut) > 0.02)
    {
        float lick = abs(TongueOut);
        float tongueSide = TongueOut > 0.0 ? 1.0 : -1.0;
        float2 tp = Rot(f - mouthAt - float2(tongueSide * 0.07, 0.02), -tongueSide * 0.5);
        float tongue = SdEllipse(tp - float2(0, 0.03 * lick), float2(0.04, 0.055) * lick + 0.002);
        acc = Over(acc, Layer(MouthDark, Fill(tongue - 0.016, aa)));
        acc = Over(acc, Layer(Tongue, Fill(tongue, aa)));
    }

    // El gorro de dormir, con el brote asomando por encima.
    if (Nightcap > 0.01)
    {
        acc = DrawNightcap(acc, h, form, aa);
        acc = Over(acc, Layer(outline, Fill(sprout - 0.045, aa)));
        acc = Over(acc, Layer(body * 0.9, Fill(stem, aa)));
        acc = Over(acc, Layer(budColor, Fill(bud, aa)));
    }

    // Lo que va por delante: los pies al estar sentado y los brazos que cruzan el cuerpo.
    if (FeetFront >= 0.5)
    {
        acc = Over(acc, Layer(outline, Fill(feet - 0.05, aa)));
        acc = Over(acc, Layer(feetColor, Fill(feet, aa)));
    }

    acc = DrawFrontArm(acc, q, -1.0, form, body, outline, aa);
    acc = DrawFrontArm(acc, q, 1.0, form, body, outline, aa);

    // Destello al crecer.
    if (Flash > 0.0)
    {
        float inside = Fill(min(min(d, feet), sprout) - outlineW, aa);
        acc = Over(acc, Layer(float3(1, 1, 1), inside * Flash));
    }

    return acc;
}

// ---------------------------------------------------------------- Huevo

static const float EggHe = 0.86, EggRa = 0.72, EggRb = 0.42;

float SdEgg(float2 q)
{
    return SdEggUp(float2(q.x, -q.y - EggRa), EggHe, EggRa, EggRb);
}

float CrackY(float x)
{
    return -1.18 + 0.09 * Tri(x * 2.2 + 0.25);
}

float4 DrawShell(float4 acc, float2 q, float d, float aa, float crack)
{
    float3 shell = ShellColor.rgb;
    float3 outline = lerp(SpotColor.rgb, shell, 0.2) * 0.45;
    acc = Over(acc, Layer(outline, Fill(d - max(0.05, 1.3 * aa), aa)));

    // Sombreado con el mismo perfil que el cuerpo.
    const float e = 0.015;
    float hx = HeightOf(SdEgg(q + float2(e, 0))) - HeightOf(SdEgg(q - float2(e, 0)));
    float hy = HeightOf(SdEgg(q + float2(0, e))) - HeightOf(SdEgg(q - float2(0, e)));
    float3 n = normalize(float3(-hx / (2 * e) * 0.35, -hy / (2 * e) * 0.35, 1.0));
    float3 l = normalize(float3(-0.45, -0.75, 0.85));
    float diffuse = saturate(dot(n, l));
    float spec = pow(saturate(dot(n, normalize(l + float3(0, 0, 1)))), 40.0) * 0.45;

    // Manchas del color de la cría que va a nacer.
    float spots = SdCircle(q - float2(-0.32, -0.55), 0.17);
    spots = min(spots, SdCircle(q - float2(0.30, -0.98), 0.14));
    spots = min(spots, SdCircle(q - float2(-0.12, -1.42), 0.10));
    spots = min(spots, SdCircle(q - float2(0.40, -0.40), 0.09));
    spots = min(spots, SdCircle(q - float2(-0.42, -1.08), 0.08));
    float3 base = lerp(shell, SpotColor.rgb, Fill(spots, aa));
    float3 color = base * (0.74 + 0.34 * diffuse) + spec;
    color *= lerp(0.9, 1.0, saturate(-q.y / 0.3));
    acc = Over(acc, Layer(color, Fill(d, aa)));

    // Grietas: aparecen de izquierda a derecha y dejan escapar luz.
    if (crack > 0.0)
    {
        float reveal = -0.75 + crack * 1.5;
        float y = CrackY(q.x);
        float crackLine = abs(q.y - y) - 0.028;
        crackLine = max(crackLine, q.x - reveal);
        crackLine = max(crackLine, d + 0.02);
        float3 crackColor = lerp(outline, float3(1.0, 0.85, 0.35), saturate(crack * 1.6 - 0.6));
        float light = saturate(crack * 1.6 - 0.6);
        float halo = exp(-max(abs(q.y - y), 0.0) * 14.0) * light * 0.5 * Fill(max(d, q.x - reveal), aa);
        acc = Over(acc, Layer(float3(1.0, 0.9, 0.5), halo));
        acc = Over(acc, Layer(crackColor, Fill(crackLine, aa)));
    }

    return acc;
}

float4 DrawEgg(float4 acc, float2 p, float aa)
{
    float2 q = Rot(p, -Wobble);
    float d = SdEgg(q);

    // Aura de calor.
    if (Glow > 0.01)
    {
        float aura = exp(-max(d, 0.0) * 4.0) * Glow * 0.55 * step(0.0, d);
        acc = Over(acc, Layer(float3(1.0, 0.70, 0.30), aura));
    }

    return DrawShell(acc, q, d, aa, Crack);
}

// Nacimiento: el huevo brilla y tiembla; luego la parte de arriba del cascarón sale volando y aparece la cría
// (que dibuja PSMain, con el rebote de HatchPop, entre medias de las dos funciones de aquí).
float HatchBurst() { return saturate((Hatch - 0.42) / 0.58); }

float HatchPop(float burst) { return lerp(0.55, 1.0, 1.0 - pow(1.0 - saturate(burst * 1.6), 3.0)) + 0.06 * sin(burst * 12.0) * (1.0 - burst); }

float4 DrawHatchShell(float4 acc, float2 p, float aa)
{
    float2 q = Rot(p, -Wobble);
    float d = SdEgg(q);
    float aura = exp(-max(d, 0.0) * 3.0) * (0.5 + Hatch) * step(0.0, d);
    acc = Over(acc, Layer(float3(1.0, 0.85, 0.45), aura));
    return DrawShell(acc, q, d, aa, 1.0);
}

float4 DrawHatchPieces(float4 acc, float2 p, float aa, float burst)
{
    // Mitad de abajo del cascarón, delante de la cría; se hunde y se desvanece al final.
    float fadeBottom = 1.0 - saturate((burst - 0.65) / 0.35);
    float2 qb = p - float2(0, -0.25 * saturate((burst - 0.65) / 0.35));
    float bottom = max(SdEgg(qb), CrackY(qb.x) - qb.y);
    float4 piece = DrawShell(float4(0, 0, 0, 0), qb, bottom, aa, 0.0);
    acc = Over(acc, piece * fadeBottom);

    // Mitad de arriba: sale despedida girando.
    float2 lidCenter = float2(0.0, -1.55);
    float2 qt = Rot(p - float2(0.7 * burst, -2.2 * burst + 1.4 * burst * burst) - lidCenter, -0.7 * burst) + lidCenter;
    float topPiece = max(SdEgg(qt), qt.y - CrackY(qt.x));
    float4 lid = DrawShell(float4(0, 0, 0, 0), qt, topPiece, aa, 0.0);
    acc = Over(acc, lid * (1.0 - saturate((burst - 0.35) / 0.4)));

    // Destello inicial.
    float flash = saturate(1.0 - burst * 4.0);
    float glow = exp(-length(p - float2(0, -1.0)) * 1.8) * flash;
    return Over(acc, Layer(float3(1.0, 0.95, 0.7), glow));
}

// ---------------------------------------------------------------- Principal

float4 PSMain(float4 position : SV_Position) : SV_Target
{
    float2 p = (position.xy - Origin) / Unit;
    float aa = 1.0 / Unit;

    // Escondido detrás de algo: lo que queda al otro lado de su borde no se ve. ClipSide ±1 es un borde
    // vertical (una ventana, el lado de la pantalla); ±2, uno horizontal (agachado tras la barra de tareas).
    if (ClipSide != 0.0)
    {
        float axis = abs(ClipSide) > 1.5 ? position.y : position.x;
        if ((axis - ClipX) * sign(ClipSide) > 0.0)
        {
            return float4(0, 0, 0, 0);
        }
    }

    // Fuera de la zona de la criatura (y de su sombra) no hay nada que dibujar.
    float rise = Ghost > 0.0 ? 2.0 * Ghost : 0.0;
    if (length(p - float2(0, -1.0 - rise)) > 2.8 + rise * 0.3 && abs(p.y - ShadowDrop) > 0.4)
    {
        return float4(0, 0, 0, 0);
    }

    float4 acc = float4(0, 0, 0, 0);
    bool handsOnly = PassMode > 0.5;

    // Sombra en el suelo: más pequeña y tenue cuanto más alto está.
    float lift = saturate(ShadowDrop / 6.0);
    float shadowWidth = (Form < 0.5 && Hatch < 0 ? 0.62 : 0.85) * (1.0 - 0.5 * lift);
    float shadow = SdEllipse(p - float2(0, ShadowDrop), float2(shadowWidth, 0.11 * (1.0 - 0.4 * lift)));
    if (!handsOnly)
    {
        acc = Over(acc, Layer(float3(0, 0, 0), Fill(shadow, 0.18) * ShadowAlpha * (1.0 - lift)));
    }

    // Inclinación y rebote alrededor de los pies.
    float2 q = Rot(p, -Lean);
    float sy = Squash;
    float sx = 1.0 / sqrt(max(Squash, 0.3));
    q = float2(q.x / sx, q.y / sy);
    float scaleAA = aa / min(sx, sy);

    // Esqueleto: el cuerpo entero se desplaza y gira alrededor de su centro (tumbarse, rodar, bailar…).
    q -= float2(RigOffsetX, RigOffsetY);
    q = Rot(q - BodyPivot, -Spin) + BodyPivot;

    if (handsOnly)
    {
        acc = Form >= 0.5 && Hatch < 0.0 && Ghost <= 0.0 ? DrawHandsOver(acc, q, Form, scaleAA) : acc;
    }
    else if (Form < 0.5 && Hatch < 0.0)
    {
        acc = DrawEgg(acc, q, scaleAA);
    }
    else
    {
        float burst = Hatch >= 0.0 ? HatchBurst() : 1.0;
        if (burst <= 0.0)
        {
            acc = DrawHatchShell(acc, q, scaleAA);
        }
        else
        {
            // La criatura se dibuja con una sola llamada (el compilador la copia entera en cada una): la normal,
            // la que sale del huevo y el fantasmita de la despedida solo cambian sus datos.
            bool ghost = Ghost > 0.0;
            float2 g = q + float2(-0.16 * sin(Time * 2.2) * saturate(Ghost * 3.0), rise);
            float pop = Hatch >= 0.0 ? HatchPop(burst) : ghost ? 1.0 - 0.12 * Ghost : 1.0;
            float4 creature = DrawKobii(float4(0, 0, 0, 0), ghost ? g : q, Hatch >= 0.0 ? 1.0 : Form, scaleAA, pop);
            if (ghost)
            {
                // Despedida: sube meciéndose, translúcido, con una aureola, y se desvanece.
                creature.rgb = lerp(creature.rgb, GhostBlue * creature.a, 0.7);
                float fade = 0.8 * (1.0 - Ghost * Ghost);
                acc = Over(acc, creature * fade);
                float halo = abs(SdEllipse(g - float2(0.0, -2.12), float2(0.34, 0.085))) - 0.032;
                acc = Over(acc, Layer(HaloGold, Fill(halo, scaleAA) * fade));
            }
            else
            {
                acc = Over(acc, creature);
            }

            if (Hatch >= 0.0)
            {
                acc = DrawHatchPieces(acc, q, scaleAA, burst);
            }
        }
    }

    // Premultiplicado válido: los brillos no pueden pasar del alfa (si no, se queman al componer).
    acc.rgb = min(acc.rgb, acc.a);
    return acc * Opacity;
}
