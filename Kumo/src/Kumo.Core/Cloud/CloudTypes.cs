namespace Kumo.Core.Cloud;

/// <summary>Lo que hace la nube con su forma (cada una tiene su colocación de las bolas, su cara y su suavidad).</summary>
public enum CloudPose
{
    /// <summary>El cúmulo de siempre: cambia de forma sin parar.</summary>
    Idle,

    /// <summary>Condensado en una gota lisa y brillante.</summary>
    Drop,

    /// <summary>Hecho bocadillo: liso, con la cara en una esquina y sitio para el texto (<see cref="KumoCloud.Bubble"/>).</summary>
    Bubble,

    /// <summary>Hablando sin bocadillo propio: late un poco con la voz.</summary>
    Talk,

    /// <summary>Pensando: se le escapan burbujitas por arriba.</summary>
    Think,

    /// <summary>Pensando a fondo: medio condensado, con el ceño fruncido y burbujas más deprisa.</summary>
    ThinkHard,

    /// <summary>Escuchando: se ensancha y los lados laten con lo que oye.</summary>
    Listen,

    /// <summary>Señala con un brazo de nube hacia <see cref="KumoCloud.Aim"/>.</summary>
    Point,

    /// <summary>Estira un dedo de nube hasta <see cref="KumoCloud.Aim"/> (lo que mide, <see cref="KumoCloud.Reach"/>).</summary>
    Press,

    /// <summary>Contento: se hincha, ojos de alegría y un saltito.</summary>
    Happy,

    /// <summary>Sorpresa: se infla de golpe y suelta nubecitas.</summary>
    Surprise,

    /// <summary>Algo salió mal: gris, aplastado y con llovizna.</summary>
    Error,

    /// <summary>Durmiendo: aplanado y respirando despacio.</summary>
    Sleep,

    /// <summary>Se deshace en niebla (y, si se quiere, se vuelve a juntar).</summary>
    Dissolve,

    /// <summary>Sin saber qué hacer (sin cerebro): los lados caen y pone cara de «ni idea».</summary>
    Shrug,

    /// <summary>Saluda con un brazo de nube que se mueve de lado a lado.</summary>
    Wave,

    /// <summary>Esperando un sí o un no: ladeado, mirándote.</summary>
    Wait,

    /// <summary>Haciendo algo para ti: un poco condensado y atento.</summary>
    Busy,

    /// <summary>Abrazando una ventana por la barra de título (<see cref="KumoCloud.Bubble"/> = lo que abarca).</summary>
    Hug,

    /// <summary>Cosquillas: se retuerce y se ríe.</summary>
    Tickle,

    /// <summary>
    /// Juntando fuerzas (cargando el cerebro): ojos cerrados, aspira y la niebla de alrededor llega en nubecitas que se funden con él.
    /// </summary>
    Gather,
}

public enum CloudEyes { Normal, Wide, Happy, Sad, Closed, Focused, Wink, Half }

public enum CloudMouth { Smile, Big, Talk, O, Frown, Flat, None, Wavy }

/// <summary>Una bola de la nube con su muelle (unidades de Kumo, relativas al centro del cuerpo).</summary>
public sealed class Ball
{
    public double X { get; set; }
    public double Y { get; set; }
    public double R { get; set; }
    public double HalfWidth { get; set; }
    public double HalfHeight { get; set; }
    public double VX { get; set; }
    public double VY { get; set; }
    public double VR { get; set; }
    public double VW { get; set; }
    public double VH { get; set; }

    public FieldBlob Blob => new(X, Y, Math.Max(0, R), Math.Max(0, HalfWidth), Math.Max(0, HalfHeight));
}

/// <summary>Una nubecita suelta (píxeles de pantalla): está en el mismo campo, así que se separa del cuerpo y se evapora.</summary>
public sealed class Puff
{
    public double X { get; set; }
    public double Y { get; set; }
    public double VX { get; set; }
    public double VY { get; set; }
    public double R0 { get; set; }
    public double R { get; set; }
    public double T { get; set; }
    public double Life { get; set; }

    /// <summary>Lo que sube sola (en unidades por segundo²; las del vuelo, poco).</summary>
    public double Lift { get; set; } = 0.1;
}
