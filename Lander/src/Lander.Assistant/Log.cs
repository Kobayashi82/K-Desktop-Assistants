using KDesktop.Assistant.Sdk;

namespace Lander.Assistant;

/// <summary>
/// El registro del lander: va al del host (assistant.log de K-Desktop, con el id delante). Estático para que las piezas
/// que vienen del Lander independiente lo sigan usando igual; cada carga del asistente tiene su propio contexto de
/// ensamblados, así que no se mezcla con otra.
/// </summary>
internal static class Log
{
    public static IAssistantLog? Target { get; set; }

    public static void Debug(string message) => Target?.Debug(message);

    public static void Info(string message) => Target?.Info(message);

    public static void Warn(string message, Exception? ex = null) => Target?.Warn(message, ex);

    public static void Error(string message, Exception? ex = null) => Target?.Error(message, ex);

    /// <summary>Cifras de diagnóstico (solo en compilaciones Debug del host).</summary>
    public static void Stats(string message) => Target?.Debug(message);
}
