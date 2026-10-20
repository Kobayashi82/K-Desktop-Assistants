using System.Diagnostics;
using Lander.Core.Game;
using Lander.Core.Geometry;
using Lander.Core.Terrain;
using Lander.Core.Vision;

namespace Lander.Assistant.Vision;

/// <summary>
/// Bordes del contenido de las ventanas para posarse en ellos (opción experimental). Mira la pantalla solo
/// cuando el juego lo pide (<see cref="ContentInterest"/>), solo en el monitor del lander, como mucho dos
/// veces por segundo por ventana y nunca si la región no ha cambiado. Las imágenes se analizan en memoria y
/// se descartan: nunca se guardan ni salen del proceso. Se usa siempre desde el hilo del motor.
/// </summary>
/// <remarks>
/// La capa de dibujo es del host y la comparten todos los asistentes, así que no se excluye de la captura (eso la
/// quitaría también de las capturas y grabaciones del usuario): se ignora la zona donde está dibujado el lander.
/// </remarks>
internal sealed class ContentVision : IDisposable
{
    /// <summary>Entre dos escaneos de la misma ventana.</summary>
    private const double ScanInterval = 0.5;

    /// <summary>Sin interés en una ventana durante este tiempo, se olvidan sus bordes.</summary>
    private const double ForgetAfter = 3;

    /// <summary>Sin interés en nada durante este tiempo, se cierra la duplicación del escritorio.</summary>
    private const double CloseAfter = 6;

    private const double RetryAfterFailure = 20;

    private const int MaxWindowsChoosing = 3;
    private const int MaxWindowsManual = 4;

    /// <summary>Lo que puede moverse un borde entre dos lecturas seguidas durante el scroll (en DIP).</summary>
    private const double MaxScrollStepDip = 160;

    /// <summary>Tras el último movimiento medido, se sigue mirando a cada fotograma este tiempo.</summary>
    private const double FollowLinger = 0.6;

    /// <summary>Tras girar la rueda sobre la ventana, ídem.</summary>
    private const double WheelLinger = 0.6;

    /// <summary>Sin scroll, cada cuánto se comprueba como mucho si el borde se ha movido (sin leer nada si no).</summary>
    private const double FollowIdleInterval = 0.1;

    /// <summary>Constante de tiempo del suavizado de la altura publicada.</summary>
    private const double SmoothTime = 0.04;

    /// <summary>Un salto mayor que esto no se suaviza (es otro sitio, no un desplazamiento).</summary>
    private const int MaxSmoothedJump = 400;

    /// <summary>
    /// Tras moverse o cambiar de tamaño una ventana, lo que se espera antes de volver a mirarla. La captura del escritorio
    /// llega un poco después que la posición de la ventana: medida mientras se mueve, el contenido parecería desplazarse
    /// (el lander daba saltos y se quedaba atrás). Mientras tanto, los bordes van con la ventana, como su barra de título.
    /// </summary>
    private const double MoveSettle = 0.3;

    private readonly Dictionary<long, WindowState> _states = [];
    private readonly ContentEdgeDetector _detector = new();
    private readonly GrayImage _image = new(0, 0);
    private readonly List<ContentEdge> _edges = [];
    private readonly List<WindowInfo> _targets = [];
    private readonly List<PixelRect> _unknown = [];
    private readonly List<PixelRect> _unknownLocal = [];
    private DesktopDuplicator? _duplicator;
    private List<ContentSurface> _surfaces = [];
    private double _lastNeeded = double.NegativeInfinity;
    private double _retryAt;
    private readonly Dictionary<(long Window, int Edge), double> _shownY = [];
    private readonly HashSet<(long Window, int Edge)> _seen = [];
    private double _lastPublish;
    private bool _smoothing;
    private PendingScan? _pending;
    private long _followWindow;
    private double _wheelAt = double.NegativeInfinity;
    private bool _enabled;

    /// <summary>Bordes confirmados, listos para el terreno.</summary>
    public IReadOnlyList<ContentSurface> Surfaces => _surfaces;

    /// <summary>Opción activada en los ajustes.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (!value)
            {
                Reset();
            }

            Log.Info(value ? "Contenido de las ventanas: activado (experimental)." : "Contenido de las ventanas: desactivado.");
        }
    }

    /// <summary>Título de una ventana (para las reglas de privacidad).</summary>
    public Func<long, string> TitleOf { get; init; } = _ => string.Empty;

    /// <summary>Aplicaciones que el usuario ha excluido.</summary>
    public IReadOnlyCollection<string> UserExcludedApps { get; set; } = [];

    /// <summary>Anota en el registro cada scroll medido (<c>--stats</c>).</summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Más zonas de la pantalla con algo dibujado por el lander (las líneas de los suelos del modo manual): no se
    /// analizan, como la del propio lander. Sus bordes confirmados se conservan aunque queden tapados.
    /// </summary>
    public IReadOnlyList<PixelRect> Masks { get; set; } = [];

    /// <summary>Altura del lander en DIP (de los ajustes).</summary>
    public double LanderSizeDip { get; set; } = 48;

    // Estadísticas para --stats.
    public int Scans { get; set; }

    public int Reuses { get; set; }

    public long ScanTicks { get; set; }

    public int Follows { get; set; }

    public long FollowTicks { get; set; }

    public bool IsCapturing => _duplicator is not null;

    public DesktopDuplicator? Duplicator => _duplicator;

    /// <summary>
    /// Cuándo necesita volver a ejecutarse: enseguida si hay una lectura en la GPU, cuando toque el próximo
    /// escaneo, o nunca si no hay nada que mirar.
    /// </summary>
    public double NextWake(double now)
    {
        if (!_enabled || _targets.Count == 0)
        {
            return double.PositiveInfinity;
        }

        if (_pending is not null || _smoothing || now - _wheelAt < WheelLinger)
        {
            return now + 1.0 / 60;
        }

        if (_duplicator is null && now < _retryAt)
        {
            return _retryAt;
        }

        var next = double.PositiveInfinity;
        foreach (var window in _targets)
        {
            if (_states.TryGetValue(window.Handle, out var state))
            {
                if (now < state.FollowUntil)
                {
                    return now + 1.0 / 60;
                }

                if (now - state.MovedAt < MoveSettle)
                {
                    next = Math.Min(next, state.MovedAt + MoveSettle);
                }

                next = Math.Min(next, Math.Max(state.LastScan + ScanInterval, state.FollowUntil));
                if (window.Handle == _followWindow)
                {
                    next = Math.Min(next, state.NextFollow);
                }
            }
        }

        return Math.Max(next, now + 1.0 / 30);
    }

    /// <summary>Los monitores han cambiado.</summary>
    public void OnMonitorsChanged() => CloseDuplicator();

    /// <summary>Cambia el tamaño del lander: los bordes vistos ya no sirven.</summary>
    public void OnLanderSizeChanged(double sizeDip)
    {
        LanderSizeDip = sizeDip;
        Reset();
    }

    /// <summary>
    /// Mira lo que haga falta según <paramref name="interest"/>. Devuelve true si han cambiado los bordes
    /// confirmados (hay que reconstruir el terreno).
    /// </summary>
    /// <param name="windows">Ventanas que forman terreno, en orden Z.</param>
    /// <param name="landerRect">Zona que ocupa el lander dibujado, en pantalla (se ignora al analizar).</param>
    public bool Update(
        double now,
        ContentInterest interest,
        IReadOnlyList<WindowInfo> windows,
        IReadOnlyList<MonitorInfo> monitors,
        Vec2 lander,
        PixelRect landerRect,
        long foreground)
    {
        if (!_enabled)
        {
            return false;
        }

        // Posado en un borde o volviendo a él (esperando en el aire a que pare la página): hay que seguirlo.
        _followWindow = interest.Need is ContentNeed.Landed or ContentNeed.Destination ? interest.Window : 0;
        var monitor = MonitorOf(monitors, lander);
        SelectTargets(interest, windows, monitors, monitor, lander, foreground);

        foreach (var window in _targets)
        {
            if (!_states.TryGetValue(window.Handle, out var state))
            {
                state = new WindowState();
                _states[window.Handle] = state;
            }

            state.LastWanted = now;
            if (state.SeenBounds != window.Bounds)
            {
                if (state.HasBounds)
                {
                    state.MovedAt = now;
                }

                state.SeenBounds = window.Bounds;
                state.HasBounds = true;
            }
        }

        var changed = Forget(now, windows);
        if (_targets.Count == 0 || monitor is null)
        {
            if (now - _lastNeeded > CloseAfter)
            {
                CloseDuplicator();
            }

            return Publish(now) || changed;
        }

        _lastNeeded = now;
        if (!EnsureDuplicator(monitor, now))
        {
            return Publish(now) || changed;
        }

        var duplicator = _duplicator!;
        duplicator.Poll(now);
        if (!duplicator.HasFrame)
        {
            return Publish(now) || changed;
        }

        var scale = monitor.DpiScale;
        var options = new ContentEdgeOptions
        {
            Clearance = (int)Math.Ceiling(LanderSizeDip * scale),
            MinLength = (int)Math.Ceiling(LanderSizeDip * 0.75 * 3 * scale),
        };

        // Una lectura encolada en un ciclo anterior: se analiza en cuanto la GPU la termina.
        if (_pending is { } pending)
        {
            if (duplicator.TryFinishRead(_image, out var region))
            {
                _pending = null;
                var window = _targets.Find(w => w.Handle == pending.Handle);
                if (window is not null && window.Bounds == pending.Bounds && _states.TryGetValue(window.Handle, out var state))
                {
                    if (pending.FollowEdge is { } edgeId)
                    {
                        Follow(window, state, edgeId, region, landerRect, scale, now);
                    }
                    else
                    {
                        Analyze(window, state, region, options, landerRect, now);
                    }
                }
            }
            else if (!duplicator.IsReading || now - pending.Started > 1)
            {
                duplicator.CancelRead();
                _pending = null;
            }

            return Publish(now) || changed;
        }

        // Posado sobre un borde (o volviendo a él): lo primero es seguirlo si se mueve.
        if (interest.Need is ContentNeed.Landed or ContentNeed.Destination && BeginFollow(interest, duplicator, monitor, now))
        {
            return Publish(now) || changed;
        }

        foreach (var window in _targets)
        {
            var state = _states[window.Handle];
            if (now - state.LastScan >= ScanInterval && now >= state.FollowUntil && now - state.MovedAt >= MoveSettle
                && BeginScan(window, state, duplicator, monitor, now))
            {
                break;
            }
        }

        return Publish(now) || changed;
    }

    /// <summary>
    /// La rueda del ratón ha girado sobre <paramref name="window"/>. Si es la ventana en la que está posado,
    /// se sigue su contenido al ritmo de la pantalla durante un momento. Devuelve true si importa.
    /// </summary>
    public bool NotifyWheel(double now, long window)
    {
        if (!_enabled || window == 0 || window != _followWindow)
        {
            return false;
        }

        _wheelAt = now;
        return true;
    }

    /// <summary>
    /// Encola la lectura de una tira alrededor del borde sobre el que está posado si su franja ha cambiado.
    /// Devuelve true si la ha encolado.
    /// </summary>
    private bool BeginFollow(ContentInterest interest, DesktopDuplicator duplicator, MonitorInfo monitor, double now)
    {
        var window = _targets.Find(w => w.Handle == interest.Window);
        if (window is null || !_states.TryGetValue(window.Handle, out var state) || now < state.NextFollow || now - state.MovedAt < MoveSettle)
        {
            return false;
        }

        var edgeId = Core.Terrain.Platform.ContentEdgeId(interest.Owner);
        if (state.Tracker.Find(edgeId) is not { } edge)
        {
            return false;
        }

        var bounds = window.Bounds;
        var band = new PixelRect(
            bounds.Left + edge.Left, bounds.Top + edge.Y - ContentEdgeTracker.BandAbove,
            bounds.Left + edge.Right, bounds.Top + edge.Y + ContentEdgeTracker.BandBelow);
        var since = state.LastFollowCheck;
        state.LastFollowCheck = now;

        // Haciendo scroll, a cada fotograma; si no, como mucho cuatro veces por segundo (hay aplicaciones que
        // repintan la ventana entera ante cualquier cambio).
        var scrolling = now < state.FollowUntil || now - _wheelAt < WheelLinger;
        state.NextFollow = now + (scrolling ? 0 : FollowIdleInterval);
        if (duplicator.ChangedFraction(band, since) == 0)
        {
            return false;
        }

        var margin = (int)Math.Ceiling(MaxScrollStepDip * monitor.DpiScale);
        var strip = new PixelRect(band.Left, band.Top - margin, band.Right, band.Bottom + margin)
            .Intersect(bounds)
            .Intersect(monitor.Bounds);
        if (strip.IsEmpty || !duplicator.BeginRead(strip))
        {
            return false;
        }

        _pending = new PendingScan(window.Handle, bounds, now, edgeId);
        return true;
    }

    /// <summary>Mide cuánto se ha movido el borde y lo mueve (con él, provisionalmente, el resto de la página).</summary>
    private void Follow(WindowInfo window, WindowState state, int edgeId, PixelRect region, PixelRect landerRect, double scale, double now)
    {
        if (state.Tracker.Find(edgeId) is not { } edge)
        {
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var bounds = window.Bounds;
        var offsetX = region.Left - bounds.Left;
        var offsetY = region.Top - bounds.Top;
        _unknownLocal.Clear();
        if (landerRect.Intersects(region))
        {
            _unknownLocal.Add(Offset(landerRect, -bounds.Left, -bounds.Top));
        }

        foreach (var mask in Masks)
        {
            if (mask.Intersects(region))
            {
                _unknownLocal.Add(Offset(mask, -bounds.Left, -bounds.Top));
            }
        }

        var maxShift = (int)Math.Ceiling(MaxScrollStepDip * scale);
        var match = ScrollMatcher.Find(edge, _image, offsetX, offsetY, maxShift, state.LastShift, _unknownLocal);
        var clearance = LanderSizeDip * scale;
        if (!match.Found || edge.Y + match.Shift - clearance < 0 || edge.Y + match.Shift >= bounds.Height)
        {
            // Ya no está (ha salido de la vista o la página ha cambiado): el lander se cae.
            state.Tracker.Remove(edgeId);
            if (Verbose)
            {
                Log.Stats($"Scroll en {window.Handle:X}: el borde {edgeId} se ha perdido (diferencia {match.Difference:0.0}, desplazamiento {match.Shift}).");
            }
        }
        else if (match.Shift != 0)
        {
            foreach (var other in state.Tracker.Edges)
            {
                ContentEdgeTracker.Shift(other, match.Shift);
            }

            ContentEdgeTracker.Refresh(edge, _image, offsetX, offsetY, _unknownLocal);
            state.FollowUntil = now + FollowLinger;
            state.NextFollow = now;
            if (Verbose)
            {
                Log.Stats($"Scroll en {window.Handle:X}: borde {edgeId} {match.Shift:+0;-0} px, ahora en y={bounds.Top + edge.Y} (diferencia {match.Difference:0.0}).");
            }
        }
        else
        {
            // Lo de alrededor puede cambiar poco a poco sin moverse: la plantilla se actualiza.
            ContentEdgeTracker.Refresh(edge, _image, offsetX, offsetY, _unknownLocal);
        }

        state.LastShift = match.Shift;
        FollowTicks += Stopwatch.GetTimestamp() - start;
        Follows++;
    }

    /// <summary>
    /// Primer paso de un escaneo. Si la región no ha cambiado desde el anterior, lo da por hecho sin leer
    /// nada; si no, encola la lectura. Devuelve true si ha encolado una lectura (una por ciclo).
    /// </summary>
    private bool BeginScan(WindowInfo window, WindowState state, DesktopDuplicator duplicator, MonitorInfo monitor, double now)
    {
        var region = window.Bounds.Intersect(monitor.Bounds);
        if (region.IsEmpty)
        {
            return false;
        }

        if (state.LastScan > double.NegativeInfinity && state.ScannedBounds == window.Bounds && duplicator.ChangedFraction(region, state.LastScan) == 0)
        {
            state.Tracker.ConfirmUnchanged();
            state.LastScan = now;
            Reuses++;
            return false;
        }

        if (!duplicator.BeginRead(region))
        {
            return false;
        }

        _pending = new PendingScan(window.Handle, window.Bounds, now);
        return true;
    }

    /// <summary>Segundo paso: detecta los bordes en la región leída y los sigue.</summary>
    private void Analyze(WindowInfo window, WindowState state, PixelRect region, ContentEdgeOptions options, PixelRect landerRect, double now)
    {
        var start = Stopwatch.GetTimestamp();
        var bounds = window.Bounds;
        _unknown.Clear();
        _unknownLocal.Clear();
        if (landerRect.Intersects(region))
        {
            _unknown.Add(Offset(landerRect, -region.Left, -region.Top));
            _unknownLocal.Add(Offset(landerRect, -bounds.Left, -bounds.Top));
        }

        foreach (var mask in Masks)
        {
            if (mask.Intersects(region))
            {
                _unknown.Add(Offset(mask, -region.Left, -region.Top));
                _unknownLocal.Add(Offset(mask, -bounds.Left, -bounds.Top));
            }
        }

        // Lo que cambia entre escaneos (vídeos, animaciones) no se confirma: el seguimiento compara la franja
        // de cada borde píxel a píxel. Los rectángulos modificados no sirven para esto: hay aplicaciones
        // (Electron, por ejemplo) que marcan la ventana entera ante cualquier cambio.
        _edges.Clear();
        _detector.Detect(_image, options, _edges, _unknown);
        state.Tracker.Update(_image, region.Left - bounds.Left, region.Top - bounds.Top, _edges, _unknownLocal);
        state.LastScan = now;
        state.ScannedBounds = bounds;
        ScanTicks += Stopwatch.GetTimestamp() - start;
        Scans++;
    }

    /// <summary>Ventanas que mirar ahora, la más interesante primero.</summary>
    private void SelectTargets(ContentInterest interest, IReadOnlyList<WindowInfo> windows, IReadOnlyList<MonitorInfo> monitors, MonitorInfo? monitor, Vec2 lander, long foreground)
    {
        _targets.Clear();
        if (monitor is null || interest.Need == ContentNeed.None)
        {
            return;
        }

        var height = LanderSizeDip * monitor.DpiScale;
        switch (interest.Need)
        {
            case ContentNeed.Landed:
            case ContentNeed.Destination:
                foreach (var window in windows)
                {
                    if (window.Handle == interest.Window && IsCandidate(window, monitors, monitor, height))
                    {
                        _targets.Add(window);
                    }
                }

                // Pilotando a mano, las de alrededor siguen siendo plataformas del juego.
                if (interest.Piloting)
                {
                    AddCandidates(windows.Where(w => w.Handle != interest.Window).ToList(), monitors, monitor, height, foreground, MaxWindowsManual - 1, near: lander);
                }

                break;

            case ContentNeed.ChoosingSoon:
                AddCandidates(windows, monitors, monitor, height, foreground, MaxWindowsChoosing, near: null);
                break;

            case ContentNeed.Manual:
                AddCandidates(windows, monitors, monitor, height, foreground, MaxWindowsManual, near: lander);
                break;
        }
    }

    private void AddCandidates(IReadOnlyList<WindowInfo> windows, IReadOnlyList<MonitorInfo> monitors, MonitorInfo monitor, double height, long foreground, int max, Vec2? near)
    {
        // Pilotando, las ventanas alrededor del lander; eligiendo destino, las más grandes. La activa, antes.
        var reach = new PixelRect(
            (int)(near?.X - height * 25 ?? int.MinValue / 2), (int)(near?.Y - height * 15 ?? int.MinValue / 2),
            (int)(near?.X + height * 25 ?? int.MaxValue / 2), (int)(near?.Y + height * 15 ?? int.MaxValue / 2));
        var candidates = windows
            .Where(w => w.Bounds.Intersects(reach) && IsCandidate(w, monitors, monitor, height))
            .OrderByDescending(w => w.Handle == foreground)
            .ThenByDescending(w => w.Bounds.Intersect(monitor.Bounds).Area)
            .Take(max);
        _targets.AddRange(candidates);
    }

    /// <summary>Del monitor del lander, con sitio para algún borde y sin datos que proteger.</summary>
    private bool IsCandidate(WindowInfo window, IReadOnlyList<MonitorInfo> monitors, MonitorInfo monitor, double height)
    {
        var visible = window.Bounds.Intersect(monitor.Bounds);
        if (visible.Width < height * 0.75 * 3 || visible.Height < height * 2 || MonitorOf(monitors, window.Bounds.Center)?.Index != monitor.Index)
        {
            return false;
        }

        return !ContentPrivacy.IsExcluded(window.AppId, TitleOf(window.Handle), UserExcludedApps);
    }

    /// <summary>Olvida las ventanas que ya no interesan o que han desaparecido.</summary>
    private bool Forget(double now, IReadOnlyList<WindowInfo> windows)
    {
        var changed = false;
        foreach (var (handle, state) in _states)
        {
            if (now - state.LastWanted > ForgetAfter || !windows.Any(w => w.Handle == handle))
            {
                changed |= state.Tracker.Confirmed.Any();
                _states.Remove(handle);
            }
        }

        return changed;
    }

    /// <summary>
    /// Recalcula la lista de bordes confirmados. Devuelve true si ha cambiado. La altura publicada se acerca
    /// a la medida con suavidad: las medidas del scroll llegan a saltos (un fotograma sí y otro no) y la
    /// física los vería como tirones.
    /// </summary>
    private bool Publish(double now)
    {
        var k = 1 - Math.Exp(-Math.Clamp(now - _lastPublish, 0, 0.25) / SmoothTime);
        _lastPublish = now;
        _smoothing = false;
        var surfaces = new List<ContentSurface>();
        _seen.Clear();
        foreach (var (handle, state) in _states)
        {
            foreach (var edge in state.Tracker.Confirmed)
            {
                var key = (handle, edge.Id);
                _seen.Add(key);
                double shown = edge.Y;
                if (_shownY.TryGetValue(key, out var previous) && Math.Abs(edge.Y - previous) < MaxSmoothedJump)
                {
                    shown = previous + (edge.Y - previous) * k;
                    if (Math.Abs(edge.Y - shown) < 0.5)
                    {
                        shown = edge.Y;
                    }
                }

                _smoothing |= shown != edge.Y;
                _shownY[key] = shown;
                surfaces.Add(new ContentSurface(handle, edge.Id, edge.Left, edge.Right, (int)Math.Round(shown)));
            }
        }

        foreach (var key in _shownY.Keys)
        {
            if (!_seen.Contains(key))
            {
                _shownY.Remove(key);
            }
        }

        if (surfaces.SequenceEqual(_surfaces))
        {
            return false;
        }

        _surfaces = surfaces;
        return true;
    }

    private bool EnsureDuplicator(MonitorInfo monitor, double now)
    {
        if (_duplicator is { IsLost: false } current && current.Bounds == monitor.Bounds)
        {
            return true;
        }

        CloseDuplicator();
        if (now < _retryAt)
        {
            return false;
        }

        _duplicator = DesktopDuplicator.TryCreate(null, monitor.Bounds, now);
        if (_duplicator is null)
        {
            _retryAt = now + RetryAfterFailure;
            return false;
        }

        // Los escaneos anteriores eran de otra duplicación: no se puede saber qué ha cambiado desde entonces.
        foreach (var state in _states.Values)
        {
            state.LastScan = double.NegativeInfinity;
        }

        return true;
    }

    private void Reset()
    {
        _states.Clear();
        _shownY.Clear();
        _surfaces = [];
        CloseDuplicator();
    }

    private void CloseDuplicator()
    {
        _duplicator?.Dispose();
        _duplicator = null;
        _pending = null;
    }

    private static MonitorInfo? MonitorOf(IReadOnlyList<MonitorInfo> monitors, Vec2 point)
    {
        MonitorInfo? best = null;
        var bestDistance = double.MaxValue;
        foreach (var monitor in monitors)
        {
            var distance = monitor.Bounds.DistanceSquaredTo(point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = monitor;
            }
        }

        return best;
    }

    private static PixelRect Offset(PixelRect rect, int dx, int dy) => new(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy);

    public void Dispose() => Reset();

    /// <summary>Lectura encolada: un escaneo de la ventana o, con <see cref="FollowEdge"/>, una tira para seguir el scroll.</summary>
    private readonly record struct PendingScan(long Handle, PixelRect Bounds, double Started, int? FollowEdge = null);

    private sealed class WindowState
    {
        public ContentEdgeTracker Tracker { get; } = new();

        public double LastScan { get; set; } = double.NegativeInfinity;

        public double LastFollowCheck { get; set; } = double.NegativeInfinity;

        public double NextFollow { get; set; }

        /// <summary>Hasta cuándo se considera que la página se está desplazando.</summary>
        public double FollowUntil { get; set; }

        /// <summary>Último desplazamiento medido, para prever el siguiente.</summary>
        public int LastShift { get; set; }

        public double LastWanted { get; set; }

        public PixelRect ScannedBounds { get; set; }

        /// <summary>Dónde estaba la ventana la última vez que se miró (para saber si se está moviendo).</summary>
        public PixelRect SeenBounds { get; set; }

        public bool HasBounds { get; set; }

        /// <summary>Cuándo se movió o cambió de tamaño por última vez.</summary>
        public double MovedAt { get; set; } = double.NegativeInfinity;
    }
}
