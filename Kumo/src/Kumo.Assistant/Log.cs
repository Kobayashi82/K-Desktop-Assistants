using KDesktop.Assistant.Sdk;

namespace Kumo.Assistant;

/// <summary>
/// El registro de Kumo: va al del host (assistant.log de K-Desktop, con el id delante). Estático para que cualquier
/// pieza lo use; cada carga del asistente tiene su propio contexto de ensamblados, así que no se mezcla con otra.
/// </summary>
internal static class Log
{
    public static IAssistantLog? Target { get; set; }

    public static void Debug(string message) => Target?.Debug(message);

    public static void Info(string message) => Target?.Info(message);

    public static void Warn(string message, Exception? ex = null) => Target?.Warn(message, ex);

    public static void Error(string message, Exception? ex = null) => Target?.Error(message, ex);
}
