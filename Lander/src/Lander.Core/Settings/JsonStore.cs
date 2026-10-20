using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Lander.Core.Progression;

namespace Lander.Core.Settings;

[JsonSerializable(typeof(LanderSettings))]
[JsonSerializable(typeof(PilotProgress))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    IgnoreReadOnlyProperties = true)]
public sealed partial class LanderJsonContext : JsonSerializerContext;

/// <summary>
/// Lee y escribe un objeto en un archivo JSON. La escritura es atómica: se escribe a un archivo temporal
/// y se reemplaza, para no dejar nunca un archivo a medias si el proceso se corta.
/// </summary>
public sealed class JsonStore<T>(string path, JsonTypeInfo<T> typeInfo)
    where T : class
{
    public string Path { get; } = path;

    /// <summary>Carga el archivo. Si no existe o está dañado devuelve null (y aparta el dañado).</summary>
    public T? Load()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(Path);
            return JsonSerializer.Deserialize(stream, typeInfo);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryBackupCorrupt();
            return null;
        }
    }

    public void Save(T value)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = Path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, typeInfo);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, Path, overwrite: true);
    }

    private void TryBackupCorrupt()
    {
        try
        {
            File.Copy(Path, Path + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin copia de seguridad: se sobrescribirá con valores por defecto.
        }
    }
}
