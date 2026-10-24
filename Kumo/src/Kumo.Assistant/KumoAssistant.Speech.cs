using System.Windows.Media;
using KDesktop.Assistant.Sdk;

namespace Kumo.Assistant;

/// <summary>
/// Texto propio (SDK 1.15, manifiesto <c>"speech": "own"</c>): la nube se hace bocadillo (<see cref="Kumo.Core.Cloud.CloudPose.Bubble"/>)
/// y el texto va dentro. Pendiente (17.3): de momento dice que no y sale el bocadillo de K-Desktop.
/// </summary>
public sealed partial class KumoAssistant
{
    /// <summary>Lo que enseña en su bocadillo (null: nada).</summary>
    private SpeechText? _speech;

    public bool ShowSpeech(SpeechText speech) => false;

    public void HideSpeech() => _speech = null;

    /// <summary>El texto dentro del bocadillo (17.3).</summary>
    private void DrawSpeech(DrawingContext dc)
    {
        if (_speech is null)
        {
            return;
        }

        _ = dc;
    }

    public Task OnExtraAsync(string extraId, CancellationToken ct) => Task.CompletedTask;
}
