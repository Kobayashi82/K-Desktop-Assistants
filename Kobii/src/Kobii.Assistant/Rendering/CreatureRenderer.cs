using System.IO;
using System.Runtime.InteropServices;
using Kobii.Core.Animation;
using Kobii.Core.Creature;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace Kobii.Assistant.Rendering;

/// <summary>Constantes del shader de la criatura (mismo orden y alineación que el cbuffer de Creature.hlsl).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct CreatureConstants
{
    public float ResolutionX, ResolutionY, OriginX, OriginY;
    public float Unit, Time, Form, Hatch;
    public float LookX, LookY, Blink, Squash;
    public float Lean, Mouth, Expression, Blush;
    public float Glow, Crack, Wobble, Facing;
    public float WalkPhase, Walk, Opacity, Antenna;
    public float Flash, ShadowDrop, ShadowAlpha, Shape;
    public float Pale, Dirt, Ghost, Wet;
    public float RigOffsetX, RigOffsetY, Spin, Hunch;
    public float HandLX, HandLY, HandRX, HandRY;
    public float FrontL, FrontR, ShowArms, FeetFront;
    public float FootLX, FootLY, FootRX, FootRY;
    public float EarL, EarR, SproutBend, HeadTilt;
    public float FaceX, FaceY, Elder, Shine;
    public float ElbowL, ElbowR, GripL, GripR;
    public float FootLTurn, FootRTurn, MouthOpen, Chew;
    public float Cheeks, Tongue, PassMode, ClipX;
    public float ClipSide, HandOverL, HandOverR, Pajamas;
    public float Nightcap, CapPad1, CapPad2, CapPad3;
    public ColorF Body, Belly, Accent, Shell, Spots;

    /// <summary>Copia el esqueleto de la animación.</summary>
    public void SetRig(in Rig rig)
    {
        (RigOffsetX, RigOffsetY, Spin, Hunch) = ((float)rig.OffsetX, (float)rig.OffsetY, (float)rig.Spin, (float)rig.Hunch);
        (HandLX, HandLY, HandRX, HandRY) = ((float)rig.HandLX, (float)rig.HandLY, (float)rig.HandRX, (float)rig.HandRY);
        (FrontL, FrontR, ShowArms, FeetFront) = ((float)rig.FrontL, (float)rig.FrontR, (float)rig.ShowArms, (float)rig.FeetFront);
        (FootLX, FootLY, FootRX, FootRY) = ((float)rig.FootLX, (float)rig.FootLY, (float)rig.FootRX, (float)rig.FootRY);
        (EarL, EarR, SproutBend, HeadTilt) = ((float)rig.EarL, (float)rig.EarR, (float)rig.Sprout, (float)rig.HeadTilt);
        (FaceX, FaceY) = ((float)rig.FaceX, (float)rig.FaceY);
        (ElbowL, ElbowR, GripL, GripR) = ((float)rig.ElbowL, (float)rig.ElbowR, (float)rig.GripL, (float)rig.GripR);
        (FootLTurn, FootRTurn, MouthOpen, Chew) = ((float)rig.FootLTurn, (float)rig.FootRTurn, (float)rig.MouthOpen, (float)rig.Chew);
        (Cheeks, Tongue) = ((float)rig.Cheeks, (float)rig.Tongue);
    }

    /// <summary>El esqueleto que llevan estas constantes (para colocar los objetos en la mano).</summary>
    public readonly Rig GetRig() => new()
    {
        OffsetX = RigOffsetX,
        OffsetY = RigOffsetY,
        Spin = Spin,
        Hunch = Hunch,
        HandLX = HandLX,
        HandLY = HandLY,
        HandRX = HandRX,
        HandRY = HandRY,
        FrontL = FrontL,
        FrontR = FrontR,
        ShowArms = ShowArms,
        FeetFront = FeetFront,
        FootLX = FootLX,
        FootLY = FootLY,
        FootRX = FootRX,
        FootRY = FootRY,
        EarL = EarL,
        EarR = EarR,
        Sprout = SproutBend,
        HeadTilt = HeadTilt,
        FaceX = FaceX,
        FaceY = FaceY,
        ElbowL = ElbowL,
        ElbowR = ElbowR,
        GripL = GripL,
        GripR = GripR,
        FootLTurn = FootLTurn,
        FootRTurn = FootRTurn,
        MouthOpen = MouthOpen,
        Chew = Chew,
        Cheeks = Cheeks,
        Tongue = Tongue,
    };

    public void SetPalette(Palette palette)
    {
        Body = ColorF.FromRgb(palette.Body);
        Belly = ColorF.FromRgb(palette.Belly);
        Accent = ColorF.FromRgb(palette.Accent);
        Shell = ColorF.FromRgb(palette.Shell);
        Spots = ColorF.FromRgb(palette.Spots);
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct ColorF(float R, float G, float B, float A)
{
    public static ColorF FromRgb(uint rgb, float alpha = 1) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
}

/// <summary>Los dos puntos de entrada del shader ya compilados.</summary>
internal sealed record CompiledShader(byte[] Vertex, byte[] Pixel);

/// <summary>
/// Dibuja la criatura en la GPU: un triángulo que cubre el lienzo y un pixel shader que evalúa la forma (SDF) en cada
/// píxel (el de Kobii AI). Funciona sobre cualquier destino de render: el de la superficie del host o una textura para
/// las vistas previas y las pruebas.
/// </summary>
internal sealed class CreatureRenderer : IDisposable
{
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11BlendState _blend;

    public unsafe CreatureRenderer(ID3D11Device device, ID3D11DeviceContext context, CompiledShader shader)
    {
        _context = context;
        _vertexShader = device.CreateVertexShader(shader.Vertex);
        _pixelShader = device.CreatePixelShader(shader.Pixel);
        _constants = device.CreateBuffer(new BufferDescription((uint)((sizeof(CreatureConstants) + 15) / 16 * 16), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

        // Mezcla «encima» con alfa premultiplicado.
        _blend = device.CreateBlendState(new BlendDescription(Blend.One, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha));
    }

    /// <summary>Dibuja la criatura sobre <paramref name="target"/> (que ya debe estar limpio).</summary>
    public unsafe void Draw(ID3D11RenderTargetView target, int width, int height, in CreatureConstants constants)
    {
        var mapped = _context.Map(_constants, 0, MapMode.WriteDiscard);
        *(CreatureConstants*)mapped.DataPointer = constants;
        _context.Unmap(_constants, 0);

        _context.OMSetRenderTargets(target);
        _context.OMSetBlendState(_blend);
        _context.RSSetViewport(0, 0, width, height);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetInputLayout(null);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_pixelShader);
        _context.PSSetConstantBuffer(0, _constants);
        _context.Draw(3, 0);

        // El host dibuja después con Direct2D en el mismo dispositivo: que no se encuentre nada nuestro puesto.
        _context.ClearState();
    }

    public void Dispose()
    {
        _blend.Dispose();
        _constants.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }

    /// <summary>
    /// Compila el shader (o lo lee de la caché, por su contenido: solo se compila la primera vez de cada versión).
    /// Tarda; se llama fuera del hilo del motor.
    /// </summary>
    public static CompiledShader Compile(string cacheFolder, Action<string>? log = null)
    {
        var source = LoadSource();
        return new CompiledShader(Cached(source, "VSMain", "vs_5_0", cacheFolder, log), Cached(source, "PSMain", "ps_5_0", cacheFolder, log));
    }

    private static string LoadSource()
    {
        using var stream = typeof(CreatureRenderer).Assembly.GetManifestResourceStream("Kobii.Assistant.Shaders.Creature.hlsl")
            ?? throw new InvalidOperationException("Falta el shader Creature.hlsl en los recursos.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] Cached(string source, string entryPoint, string profile, string folder, Action<string>? log)
    {
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{source}|{entryPoint}|{profile}")))[..20];
        var cache = Path.Combine(folder, $"{entryPoint}-{key}.cso");
        try
        {
            if (File.Exists(cache))
            {
                return File.ReadAllBytes(cache);
            }
        }
        catch (IOException)
        {
            // Se compila otra vez.
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = Compiler.Compile(source, entryPoint, "Creature.hlsl", profile, out var blob, out var errors);
        byte[] bytes;
        using (errors)
        {
            if (result.Failure || blob is null)
            {
                throw new InvalidOperationException($"No se pudo compilar el shader ({entryPoint}): {errors?.AsString() ?? result.ToString()}");
            }
        }

        using (blob)
        {
            bytes = blob.AsBytes();
        }

        log?.Invoke($"Shader {entryPoint} compilado en {clock.Elapsed.TotalSeconds:0.00} s.");
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.EnumerateFiles(folder, $"{entryPoint}-*.cso"))
            {
                File.Delete(old);
            }

            File.WriteAllBytes(cache, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin caché: la próxima vez se compila otra vez.
        }

        return bytes;
    }
}
