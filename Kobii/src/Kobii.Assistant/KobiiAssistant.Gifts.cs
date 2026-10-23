using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Sdk;
using Kobii.Assistant.Audio;
using Kobii.Assistant.Rendering;
using Kobii.Core.Creature;
using Kobii.Core.Geometry;
using SdkRect = KDesktop.Assistant.Sdk.PixelRect;

namespace Kobii.Assistant;

/// <summary>
/// Lo que le das (extras del menú, del panel y de los comandos): comida, bebida y juguetes —la pelota, el peluche y la
/// cama—, que se quedan en el escritorio hasta que los guardas (también entre sesiones). Los juguetes se cogen y se lanzan
/// con el ratón como él: si le lanzas la pelota, va a por ella y te la trae; un clic la hace botar. Van en una superficie
/// propia por detrás de él, que solo se redibuja cuando algo cambia.
/// </summary>
public sealed partial class KobiiAssistant
{
    private const string FeedExtra = "kobii.feed";
    private const string DrinkExtra = "kobii.drink";
    private const string BallExtra = "kobii.ball";
    private const string PlushExtra = "kobii.plush";
    private const string BedExtra = "kobii.bed";
    private const string PutAwayExtra = "kobii.toys.away";

    /// <summary>A partir de esta velocidad (DIP/s), soltar un juguete es lanzarlo.</summary>
    private const double ThrowSpeedDip = 380;

    private static readonly FoodKind[] Snacks = [FoodKind.Apple, FoodKind.Berries, FoodKind.Cookie];
    private static readonly FoodKind[] Drinks = [FoodKind.Juice, FoodKind.Milk, FoodKind.Water];

    private readonly Dictionary<string, ExtraState> _extraStates = [];
    private readonly List<ToyHit> _toyHits = [];
    private readonly Random _giftRandom = new();
    private ISurface _toys = null!;
    private int _toysSignature;
    private bool _toysShown;
    private KobiiProgress _progress = new();
    private bool _toysRestored;

    /// <summary>La zona de clic de un juguete y lo que el ratón está haciendo con él.</summary>
    private sealed class ToyHit(Toy toy, IHitArea hit)
    {
        public Toy Toy { get; } = toy;

        public IHitArea Hit { get; } = hit;

        public bool Pressed { get; set; }

        public bool Dragging { get; set; }

        public Vec2 PressPoint { get; set; }

        public Vec2 DragOffset { get; set; }

        public Vec2? Cursor { get; set; }

        public (int, int, int, int) Key { get; set; } = (int.MinValue, 0, 0, 0);

        public bool Shown { get; set; }
    }

    private void AttachGifts()
    {
        _toys = _host.Rendering.CreateSurface(SurfaceLayer.Back);
        _progress = KobiiProgress.Read(Path.Combine(_host.DataDirectory, "progress.json"));
    }

    /// <summary>Los juguetes que dejaste fuera la última vez vuelven a caer cerca de él al arrancar.</summary>
    private void RestoreToys()
    {
        if (_toysRestored)
        {
            return;
        }

        _toysRestored = true;
        foreach (var kind in _progress.Toys)
        {
            _game.GiveToy(kind, _world);
        }

        SyncToyHits();
        UpdateExtraStates();
        PublishPanel();
    }

    public Task OnExtraAsync(string extraId, CancellationToken ct)
    {
        if (!_game.IsInitialized)
        {
            return Task.CompletedTask;
        }

        switch (extraId)
        {
            case FeedExtra:
                _game.Give(Snacks[_giftRandom.Next(Snacks.Length)], _world);
                break;
            case DrinkExtra:
                _game.Give(Drinks[_giftRandom.Next(Drinks.Length)], _world);
                break;
            case BallExtra:
                GiveToy(ToyKind.Ball);
                break;
            case PlushExtra:
                GiveToy(ToyKind.Plush);
                break;
            case BedExtra:
                GiveToy(ToyKind.Bed);
                break;
            case PutAwayExtra:
                foreach (var kind in _game.Playground.Toys.Where(t => !t.Ephemeral).Select(t => t.Kind).ToList())
                {
                    _game.PutAwayToy(kind);
                }

                Play(Sound.Pop, 0.5, _game.Body.Feet.X);
                SaveToys();
                break;
            default:
                if (!OnTouchExtra(extraId))
                {
                    _host.Log.Warn($"Extra desconocido: {extraId}");
                    return Task.CompletedTask;
                }

                break;
        }

        _host.Log.Debug($"Kobii: extra {extraId}");
        SyncToyHits();
        UpdateExtraStates();
        _host.RequestTick();
        return Task.CompletedTask;
    }

    private void GiveToy(ToyKind kind)
    {
        _game.GiveToy(kind, _world);
        Play(Sound.Pop, 0.5, _game.Body.Feet.X);
        SaveToys();
    }

    /// <summary>Recuerda qué juguetes tuyos hay fuera (para la próxima vez).</summary>
    private void SaveToys()
    {
        var toys = _game.Playground.Toys.Where(t => !t.Ephemeral).Select(t => t.Kind).Order().ToList();
        if (toys.SequenceEqual(_progress.Toys))
        {
            return;
        }

        _progress = _progress with { Toys = toys };
        _progress.Save(Path.Combine(_host.DataDirectory, "progress.json"), _host.Log);
    }

    /// <summary>Lo que se cuenta en su panel (y se guarda).</summary>
    private void Count(Func<KobiiProgress, KobiiProgress> change)
    {
        _progress = change(_progress);
        _progress.Save(Path.Combine(_host.DataDirectory, "progress.json"), _host.Log);
        PublishPanel();
    }

    private void PublishPanel() => _host.PublishPanel(new JsonObject
    {
        ["stats"] = new JsonObject
        {
            ["meals"] = _progress.Meals,
            ["fetched"] = _progress.Fetched,
            ["butterflies"] = _progress.Butterflies,
        },
    });

    /// <summary>«Guardar los juguetes» solo cuando hay alguno tuyo fuera; los que ya están fuera no se ofrecen otra vez.</summary>
    private void UpdateExtraStates()
    {
        var toys = _game.Playground.Toys.Where(t => !t.Ephemeral).Select(t => t.Kind).ToHashSet();
        SetExtra(BallExtra, toys.Contains(ToyKind.Ball) ? ExtraState.Hidden : ExtraState.Shown);
        SetExtra(PlushExtra, toys.Contains(ToyKind.Plush) ? ExtraState.Hidden : ExtraState.Shown);
        SetExtra(BedExtra, toys.Contains(ToyKind.Bed) ? ExtraState.Hidden : ExtraState.Shown);
        SetExtra(PutAwayExtra, toys.Count > 0 ? ExtraState.Shown : ExtraState.Hidden);
        UpdateTouchExtraStates();
    }

    private void SetExtra(string id, ExtraState state)
    {
        if (_extraStates.TryGetValue(id, out var current) && current == state)
        {
            return;
        }

        _extraStates[id] = state;
        _host.SetExtraState(id, state);
    }

    /// <summary>Una zona de clic por juguete (y fuera las de los que se han guardado).</summary>
    private void SyncToyHits()
    {
        for (var i = _toyHits.Count - 1; i >= 0; i--)
        {
            if (!_game.Playground.Toys.Contains(_toyHits[i].Toy))
            {
                _toyHits[i].Hit.Dispose();
                _toyHits.RemoveAt(i);
            }
        }

        foreach (var toy in _game.Playground.Toys)
        {
            if (!_toyHits.Any(h => h.Toy == toy))
            {
                var hit = _host.Pointer.CreateHitArea("toy." + toy.Kind.ToString().ToLowerInvariant());
                hit.Cursor = PointerCursor.Hand;
                _toyHits.Add(new ToyHit(toy, hit));
            }
        }
    }

    /// <summary>El ratón sobre un juguete: un clic le da un toquecito; arrastrar es cogerlo, y soltarlo deprisa, lanzarlo.</summary>
    private void OnToyPointer(PointerEvent e)
    {
        if (_toyHits.FirstOrDefault(h => h.Hit.Id == e.Area) is not { } host)
        {
            return;
        }

        var at = new Vec2(e.Position.X, e.Position.Y);
        var toy = host.Toy;
        if (e.Kind is not PointerEventKind.DragMove)
        {
            _host.Log.Debug($"Kobii: ratón {e.Kind} en {toy.Kind}");
        }

        switch (e.Kind)
        {
            case PointerEventKind.Down:
                host.Pressed = true;
                host.PressPoint = at;
                break;

            case PointerEventKind.DragStart when toy.Free:
                host.Dragging = true;
                host.Cursor = at;
                host.DragOffset = toy.Body.Feet - (host.Pressed ? host.PressPoint : at);
                toy.Body.Grab();
                toy.ThrownByYou = false;
                host.Hit.Cursor = PointerCursor.Grabbing;
                break;

            case PointerEventKind.DragMove:
                host.Cursor = at;
                break;

            case PointerEventKind.DragEnd:
                EndToyPress(host, at);
                break;

            case PointerEventKind.Click:
                host.Pressed = false;
                Nudge(toy, at);
                break;

            case PointerEventKind.Up when !host.Dragging:
                host.Pressed = false;
                break;
        }

        _host.RequestTick();
    }

    /// <summary>Suelta el juguete arrastrado: si iba deprisa, lo has lanzado (y va a por la pelota).</summary>
    private static void EndToyPress(ToyHit host, Vec2 at)
    {
        var toy = host.Toy;
        if (host.Dragging && toy.Body.IsHeld)
        {
            toy.Body.Release();
            if (toy.Body.Velocity.Length / toy.Body.Scale > ThrowSpeedDip)
            {
                toy.ThrownByYou = toy.Kind == ToyKind.Ball;
                toy.ThrowOrigin = at;
            }
        }

        host.Dragging = false;
        host.Pressed = false;
        host.Cursor = null;
        host.Hit.Cursor = PointerCursor.Hand;
    }

    /// <summary>Un clic: la pelota sale botando (y va a por ella); el peluche da un saltito y la cama se espachurra.</summary>
    private void Nudge(Toy toy, Vec2 at)
    {
        if (!toy.Free)
        {
            return;
        }

        var s = toy.Body.Scale;
        switch (toy.Kind)
        {
            case ToyKind.Ball:
                toy.Body.Launch(new Vec2((_giftRandom.NextDouble() * 180 - 90) * s, -(420 + _giftRandom.NextDouble() * 140) * s));
                toy.ThrownByYou = true;
                toy.ThrowOrigin = at;
                Play(Sound.Kick, 0.5, at.X);
                break;
            case ToyKind.Plush:
                toy.Body.Launch(new Vec2(0, -260 * s));
                toy.Bounce(-1.5);
                Play(Sound.Boing, 0.3, at.X);
                break;
            default:
                toy.Bounce(-2.2);
                Play(Sound.Boing, 0.25, at.X);
                break;
        }
    }

    /// <summary>Antes de cada paso de simulación: los juguetes que llevas con el ratón siguen al cursor.</summary>
    private void DragToys()
    {
        foreach (var host in _toyHits)
        {
            if (host.Dragging && host.Toy.Body.IsHeld && (host.Cursor ?? _sensors.Cursor) is { } cursor)
            {
                host.Toy.Body.MoveHeld(cursor + host.DragOffset, KobiiGame.TimeStep);
            }
        }
    }

    private bool ToysMoving => _game.Playground.IsMoving || _toyHits.Any(h => h.Pressed || h.Dragging);

    /// <summary>¿Hay pantalla completa donde está ese juguete? (Entonces ni se dibuja ni se puede coger.)</summary>
    private bool Covered(Toy toy) =>
        (_world.MonitorAt(toy.Center) ?? _world.NearestMonitor(toy.Center)) is { } monitor && _host.World.System.FullscreenMonitors.Contains(monitor.Index);

    /// <summary>Las zonas de clic siguen a los juguetes sueltos.</summary>
    private void UpdateToyHits()
    {
        foreach (var host in _toyHits)
        {
            var toy = host.Toy;
            if (_hidden || _exitStart is not null || toy.Held || Covered(toy))
            {
                if (host.Shown)
                {
                    host.Shown = false;
                    host.Key = (int.MinValue, 0, 0, 0);
                    host.Hit.Hide();
                }

                continue;
            }

            var center = toy.Center;
            var rx = Math.Max(toy.Body.HalfWidth, toy.Radius) + 3;
            var ry = toy.Body.Height * 0.55 + 3;
            var key = ((int)Math.Round(center.X - rx), (int)Math.Round(center.Y - ry), (int)Math.Round(rx * 2), (int)Math.Round(ry * 2));
            if (key == host.Key)
            {
                continue;
            }

            host.Key = key;
            host.Shown = true;
            host.Hit.Place(new HitShape.Ellipse(SdkRect.FromSize(key.Item1, key.Item2, Math.Max(1, key.Item3), Math.Max(1, key.Item4))));
        }
    }

    /// <summary>Dibuja los juguetes sueltos (por detrás de Kobii; de la cama en la que está, solo lo de detrás) si algo ha cambiado.</summary>
    private void RenderToys(double alpha)
    {
        var shown = _hidden || _exitStart is not null ? [] : _game.Playground.Toys.Where(t => !t.Held && !Covered(t)).ToList();
        if (shown.Count == 0)
        {
            if (_toysShown)
            {
                _toysShown = false;
                _toysSignature = 0;
                _toys.Hide();
            }

            return;
        }

        var hash = new HashCode();
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        // La cama primero: lo pequeño que haya junto a ella se ve por delante.
        var poses = new List<(Toy Toy, Vec2 Feet, double Spin, ToyPart Part)>(shown.Count);
        foreach (var toy in shown.OrderBy(t => t.Kind == ToyKind.Bed ? 0 : 1))
        {
            var feet = toy.Body.IsHeld || toy.Body.IsGrounded ? toy.Body.Feet : Vec2.Lerp(toy.PreviousFeet, toy.Body.Feet, alpha);
            var spin = toy.PreviousSpin + (toy.Spin - toy.PreviousSpin) * alpha;
            var part = _game.Bed == toy ? ToyPart.Back : ToyPart.All;
            poses.Add((toy, feet, spin, part));
            hash.Add((int)Math.Round(feet.X * 4));
            hash.Add((int)Math.Round(feet.Y * 4));
            hash.Add((int)Math.Round(spin * 100));
            hash.Add((int)Math.Round(toy.Squash * 200));
            hash.Add(part);
            hash.Add(toy.Body.Scale);
            var reach = Math.Max(toy.Body.HalfWidth * 1.1, toy.Radius * 1.6) + 4;
            left = Math.Min(left, feet.X - reach);
            right = Math.Max(right, feet.X + reach);
            top = Math.Min(top, feet.Y - toy.Body.Height * 1.6 - 4);
            bottom = Math.Max(bottom, feet.Y + toy.Body.Height * 0.4 + 4);
        }

        var signature = hash.ToHashCode();
        if (_toysShown && signature == _toysSignature)
        {
            return;
        }

        _toysShown = true;
        _toysSignature = signature;
        var x = (int)Math.Floor(left);
        var y = (int)Math.Floor(top);
        var width = Math.Clamp((int)Math.Ceiling(right) - x, 1, 8192);
        var height = Math.Clamp((int)Math.Ceiling(bottom) - y, 1, 4096);
        _toys.Present(SdkRect.FromSize(x, y, width, height), dc =>
        {
            foreach (var (toy, feet, spin, part) in poses)
            {
                ScenePainter.DrawToy(dc, toy.Kind, new System.Windows.Point(feet.X, feet.Y - toy.Radius), toy.Radius, spin, toy.Squash, toy.Body.IsGrounded, 1, part);
            }
        });
    }

    private void HideToys()
    {
        _toys.Hide();
        _toysShown = false;
        _toysSignature = 0;
        foreach (var host in _toyHits)
        {
            host.Hit.Hide();
            host.Shown = false;
            host.Key = (int.MinValue, 0, 0, 0);
        }
    }

    /// <summary>Lo que cuenta su panel y suena de los juguetes que le das.</summary>
    private void HandleGiftEvent(GameEvent gameEvent)
    {
        switch (gameEvent.Kind)
        {
            case GameEventKind.Fetched:
                _host.Log.Debug("Kobii: te ha traído la pelota");
                Play(Sound.Chime, 0.45, gameEvent.Position.X);
                Count(p => p with { Fetched = p.Fetched + 1 });
                break;
            case GameEventKind.Hugged:
                Play(Sound.Squeak, 0.4, gameEvent.Position.X);
                break;
            case GameEventKind.BedBounced:
                Play(Sound.Boing, 0.2 + 0.25 * gameEvent.Magnitude, gameEvent.Position.X);
                break;
            case GameEventKind.Ate:
                Count(p => p with { Meals = p.Meals + 1 });
                break;
            case GameEventKind.Clapped when gameEvent.Text == "caught":
                Count(p => p with { Butterflies = p.Butterflies + 1 });
                break;
        }
    }

    /// <summary>Dónde está cada juguete (para la línea de cifras del registro).</summary>
    private string ToysForLog() => string.Concat(_game.Playground.Toys.Select(t => FormattableString.Invariant($"; {t.Kind} {t.Body.Feet.X:0},{t.Body.Feet.Y:0}{(t.Held ? " en las manos" : string.Empty)}")));

    private void DisposeGifts()
    {
        foreach (var host in _toyHits)
        {
            host.Hit.Dispose();
        }

        _toyHits.Clear();
        _toys.Dispose();
    }
}

/// <summary>Lo que se guarda de Kobii (progress.json): los juguetes tuyos que hay fuera y lo que cuenta su panel.</summary>
internal sealed record KobiiProgress
{
    public IReadOnlyList<ToyKind> Toys { get; init; } = [];

    public int Meals { get; init; }

    public int Fetched { get; init; }

    public int Butterflies { get; init; }

    public static KobiiProgress Read(string path)
    {
        try
        {
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject json)
            {
                return new KobiiProgress();
            }

            var toys = (json["toys"] as JsonArray ?? [])
                .Select(n => n?.GetValueKind() == JsonValueKind.String && Enum.TryParse<ToyKind>(n.GetValue<string>(), ignoreCase: true, out var kind) ? kind : (ToyKind?)null)
                .OfType<ToyKind>()
                .Distinct()
                .Order()
                .ToList();
            return new KobiiProgress
            {
                Toys = toys,
                Meals = Number(json, "meals"),
                Fetched = Number(json, "fetched"),
                Butterflies = Number(json, "butterflies"),
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new KobiiProgress();
        }
    }

    public void Save(string path, IAssistantLog log)
    {
        var json = new JsonObject
        {
            ["toys"] = new JsonArray([.. Toys.Select(t => JsonValue.Create(t.ToString().ToLowerInvariant()))]),
            ["meals"] = Meals,
            ["fetched"] = Fetched,
            ["butterflies"] = Butterflies,
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"No se pudo guardar el progreso de Kobii: {ex.Message}");
        }
    }

    private static int Number(JsonObject json, string key) =>
        json[key] is JsonValue value && value.TryGetValue<int>(out var number) ? Math.Max(0, number) : 0;
}
