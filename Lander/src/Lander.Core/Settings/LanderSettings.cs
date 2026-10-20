using Lander.Core.Game;
using Lander.Core.Progression;

namespace Lander.Core.Settings;

/// <summary>Combinación de teclas global (código de tecla virtual de Windows + modificadores).</summary>
public sealed record HotkeySetting
{
    public static HotkeySetting Default { get; } = new() { Control = true, Alt = true, VirtualKey = 0x4C };

    public bool Control { get; init; }

    public bool Alt { get; init; }

    public bool Shift { get; init; }

    public bool Windows { get; init; }

    public int VirtualKey { get; init; }

    public bool HasModifier => Control || Alt || Shift || Windows;
}

/// <summary>Ajustes del usuario. Se guardan como JSON en la carpeta de datos del usuario.</summary>
public sealed class LanderSettings
{
    public const double MinSize = 32;
    public const double MaxSize = 96;

    public int Version { get; set; } = 1;

    /// <summary>Altura del lander en DIP (48 por defecto).</summary>
    public double LanderSize { get; set; } = 48;

    public double Shyness { get; set; } = 0.5;

    public double Activity { get; set; } = 1;

    public MissionFrequency MissionFrequency { get; set; } = MissionFrequency.Normal;

    public bool SoundEnabled { get; set; }

    public double Volume { get; set; } = 0.5;

    public bool StartWithWindows { get; set; }

    public string Skin { get; set; } = Unlocks.DefaultSkin;

    public string Trail { get; set; } = Unlocks.DefaultTrail;

    public HotkeySetting ToggleHotkey { get; set; } = HotkeySetting.Default;

    /// <summary>Permite coger el lander con el ratón y lanzarlo.</summary>
    public bool AllowDragging { get; set; } = true;

    public int ManualTimeoutSeconds { get; set; } = 30;

    public bool ReactToNotifications { get; set; } = true;

    /// <summary>Torpeza del piloto automático, de 0 (nunca se equivoca) a 1 (se despista a menudo).</summary>
    public double PilotErrors { get; set; } = 0.5;

    /// <summary>Ganas de acrobacias del piloto automático, de 0 (ninguna) a 1 (muchas).</summary>
    public double Acrobatics { get; set; } = 0.5;

    /// <summary>Visita el reloj de la barra de tareas a las horas en punto.</summary>
    public bool VisitClock { get; set; } = true;

    public bool Paused { get; set; }

    /// <summary>
    /// Posarse en el contenido de las ventanas (experimental): mira la pantalla en busca de bordes claros.
    /// Desactivado por defecto; las imágenes solo se analizan en memoria.
    /// </summary>
    public bool LandOnContent { get; set; }

    /// <summary>
    /// Aplicaciones (nombre del proceso, con o sin «.exe») cuyo contenido no se analiza nunca, además de las
    /// que excluye siempre <see cref="Vision.ContentPrivacy"/>.
    /// </summary>
    public List<string> ContentExcludedApps { get; set; } = [];

    public GameOptions ToGameOptions() => new()
    {
        LanderSizeDip = LanderSize,
        Shyness = Shyness,
        Activity = Activity,
        MissionFrequency = MissionFrequency,
        ManualTimeoutSeconds = ManualTimeoutSeconds,
        PilotErrors = PilotErrors,
        Acrobatics = Acrobatics,
        VisitClock = VisitClock,
    };

    /// <summary>Corrige valores fuera de rango (por ejemplo, de un archivo editado a mano).</summary>
    public LanderSettings Normalize()
    {
        LanderSize = Math.Clamp(double.IsFinite(LanderSize) ? LanderSize : 48, MinSize, MaxSize);
        Shyness = Math.Clamp(double.IsFinite(Shyness) ? Shyness : 0.5, 0, 1);
        Activity = Math.Clamp(double.IsFinite(Activity) ? Activity : 1, 0.25, 3);
        Volume = Math.Clamp(double.IsFinite(Volume) ? Volume : 0.5, 0, 1);
        ManualTimeoutSeconds = Math.Clamp(ManualTimeoutSeconds, 5, 600);
        PilotErrors = Math.Clamp(double.IsFinite(PilotErrors) ? PilotErrors : 0.5, 0, 1);
        Acrobatics = Math.Clamp(double.IsFinite(Acrobatics) ? Acrobatics : 0.5, 0, 1);
        if (!Enum.IsDefined(MissionFrequency))
        {
            MissionFrequency = MissionFrequency.Normal;
        }

        if (Unlocks.Find(Skin) is not { Kind: UnlockKind.Skin })
        {
            Skin = Unlocks.DefaultSkin;
        }

        if (Unlocks.Find(Trail) is not { Kind: UnlockKind.Trail })
        {
            Trail = Unlocks.DefaultTrail;
        }

        if (ToggleHotkey is null || ToggleHotkey.VirtualKey <= 0 || !ToggleHotkey.HasModifier)
        {
            ToggleHotkey = HotkeySetting.Default;
        }

        ContentExcludedApps = (ContentExcludedApps ?? [])
            .Where(app => !string.IsNullOrWhiteSpace(app))
            .Select(Vision.ContentPrivacy.NormalizeApp)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return this;
    }

    public LanderSettings Clone()
    {
        var clone = (LanderSettings)MemberwiseClone();
        clone.ContentExcludedApps = [.. ContentExcludedApps ?? []];
        return clone;
    }
}
