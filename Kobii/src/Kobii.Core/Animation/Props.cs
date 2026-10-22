namespace Kobii.Core.Animation;

/// <summary>Objetos que puede llevar en la mano (los dibuja el motor gráfico junto a la mano).</summary>
public enum Prop
{
    None,

    /// <summary>Una pelota de colores (la bota, la abraza…).</summary>
    Ball,

    Book,

    /// <summary>El bastón de los mayores.</summary>
    Cane,

    Flower,

    /// <summary>Pincel con la punta de colores.</summary>
    Brush,

    /// <summary>Una taza humeante.</summary>
    Teacup,

    /// <summary>Un globo atado con un hilo.</summary>
    Balloon,

    /// <summary>Una hoja grande, como paraguas.</summary>
    Leaf,

    /// <summary>Pompero.</summary>
    Wand,

    Microphone,

    /// <summary>Una almohada para la siesta.</summary>
    Pillow,

    /// <summary>Su peluche (el juguete que se lleva en brazos).</summary>
    Plush,

    /// <summary>Una escobita (barre cuando vacías la papelera).</summary>
    Broom,
}

/// <summary>Lo que lleva puesto.</summary>
public enum Wear
{
    None,

    /// <summary>Gafas puestas.</summary>
    Glasses,

    /// <summary>Gafas subidas en la frente (y buscándolas por todas partes).</summary>
    GlassesUp,
}

public static class Props
{
    public static string DisplayName(this Prop prop) => prop switch
    {
        Prop.Ball => "Pelota",
        Prop.Book => "Libro",
        Prop.Cane => "Bastón",
        Prop.Flower => "Flor",
        Prop.Brush => "Pincel",
        Prop.Teacup => "Taza",
        Prop.Balloon => "Globo",
        Prop.Leaf => "Hoja",
        Prop.Wand => "Pompero",
        Prop.Microphone => "Micrófono",
        Prop.Pillow => "Almohada",
        Prop.Plush => "Peluche",
        Prop.Broom => "Escoba",
        _ => "Nada",
    };

    /// <summary>Se sujeta con las dos manos, delante del cuerpo.</summary>
    public static bool TwoHanded(this Prop prop) => prop is Prop.Book or Prop.Pillow or Prop.Plush or Prop.Broom;
}
