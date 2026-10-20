namespace Lander.Core.Vision;

/// <summary>
/// Ventanas cuyo contenido nunca se mira: gestores de contraseñas, pantallas del sistema con credenciales,
/// banca, navegación privada e inicios de sesión, además de las aplicaciones que añada el usuario. Se
/// decide por el nombre del proceso y por el título de la ventana, sin mirar sus píxeles.
/// </summary>
public static class ContentPrivacy
{
    /// <summary>Procesos excluidos siempre (nombre en minúsculas, sin «.exe»).</summary>
    public static IReadOnlySet<string> BuiltInApps { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Gestores de contraseñas
        "keepass", "keepassxc", "1password", "bitwarden", "lastpass", "dashlane", "enpass", "nordpass",
        "roboform", "keeper", "keeperpasswordmanager", "proton pass", "protonpass", "pwsafe", "passwordsafe",
        "stickypassword", "zohovault", "keepassium",

        // Credenciales y seguridad de Windows
        "credentialuibroker", "consent", "logonui", "lockapp", "securityhealthhost", "msedgewebview2_auth",

        // Autenticadores
        "authy", "authy desktop", "winauth",
    };

    /// <summary>Palabras del título (sin distinguir mayúsculas) que excluyen la ventana.</summary>
    public static IReadOnlyList<string> BuiltInTitleWords { get; } =
    [
        // Banca y pagos
        "banco", "banca", "bank", "bizum", "paypal", "tarjeta", "wallet", "cartera",

        // Contraseñas e inicios de sesión
        "contraseña", "password", "iniciar sesión", "inicio de sesión", "sign in", "log in", "login",
        "credencial", "credential",

        // Navegación privada
        "inprivate", "incógnito", "incognito", "private browsing", "navegación privada",
    ];

    /// <summary>Indica si no se debe analizar el contenido de esa ventana.</summary>
    /// <param name="appId">Proceso de la ventana (minúsculas, sin extensión) o null si no se conoce.</param>
    /// <param name="title">Título actual de la ventana.</param>
    /// <param name="userApps">Aplicaciones añadidas por el usuario en los ajustes.</param>
    public static bool IsExcluded(string? appId, string? title, IReadOnlyCollection<string>? userApps = null)
    {
        // Sin saber de qué aplicación es, mejor no mirar.
        if (string.IsNullOrEmpty(appId) || BuiltInApps.Contains(appId))
        {
            return true;
        }

        if (userApps is not null)
        {
            foreach (var app in userApps)
            {
                if (string.Equals(NormalizeApp(app), appId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (!string.IsNullOrEmpty(title))
        {
            foreach (var word in BuiltInTitleWords)
            {
                if (title.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>«Bitwarden.exe» → «bitwarden».</summary>
    public static string NormalizeApp(string app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var name = app.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.ToLowerInvariant();
    }
}
