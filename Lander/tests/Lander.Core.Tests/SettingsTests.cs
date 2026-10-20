using Lander.Core.Game;
using Lander.Core.Progression;
using Lander.Core.Settings;

namespace Lander.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lander-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Defaults_follow_the_design()
    {
        var settings = new LanderSettings();

        Assert.Equal(48, settings.LanderSize);
        Assert.False(settings.SoundEnabled);
        Assert.False(settings.StartWithWindows);
        Assert.Equal(30, settings.ManualTimeoutSeconds);
        Assert.True(settings.ToggleHotkey.Control);
        Assert.True(settings.ToggleHotkey.Alt);
        Assert.Equal('L', settings.ToggleHotkey.VirtualKey);
    }

    [Fact]
    public void Normalize_fixes_out_of_range_values()
    {
        var settings = new LanderSettings
        {
            LanderSize = 1000,
            Shyness = -3,
            Volume = double.NaN,
            Skin = "does-not-exist",
            Trail = "classic",
            ManualTimeoutSeconds = 0,
            ToggleHotkey = new HotkeySetting { VirtualKey = 0x4C },
        }.Normalize();

        Assert.Equal(LanderSettings.MaxSize, settings.LanderSize);
        Assert.Equal(0, settings.Shyness);
        Assert.Equal(0.5, settings.Volume);
        Assert.Equal(Unlocks.DefaultSkin, settings.Skin);
        Assert.Equal(Unlocks.DefaultTrail, settings.Trail);
        Assert.Equal(5, settings.ManualTimeoutSeconds);
        Assert.Equal(HotkeySetting.Default, settings.ToggleHotkey);
    }

    [Fact]
    public void Settings_roundtrip_through_json()
    {
        var store = new JsonStore<LanderSettings>(Path.Combine(_directory, "settings.json"), LanderJsonContext.Default.LanderSettings);
        var settings = new LanderSettings
        {
            LanderSize = 64,
            PilotErrors = 0.8,
            MissionFrequency = MissionFrequency.High,
            SoundEnabled = true,
            Skin = "apollo",
            ToggleHotkey = new HotkeySetting { Control = true, Shift = true, VirtualKey = 0x4D },
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(64, loaded.LanderSize);
        Assert.Equal(0.8, loaded.PilotErrors);
        Assert.Equal(MissionFrequency.High, loaded.MissionFrequency);
        Assert.True(loaded.SoundEnabled);
        Assert.Equal("apollo", loaded.Skin);
        Assert.Equal(settings.ToggleHotkey, loaded.ToggleHotkey);
        Assert.Contains("\"missionFrequency\": \"High\"", File.ReadAllText(store.Path));
    }

    [Fact]
    public void Progress_roundtrips_through_json()
    {
        var store = new JsonStore<PilotProgress>(Path.Combine(_directory, "progress.json"), LanderJsonContext.Default.PilotProgress);

        store.Save(new PilotProgress { TotalPoints = 1234, BestStreak = 7, FlightSeconds = 99.5 });
        var loaded = store.Load()!;

        Assert.Equal(1234, loaded.TotalPoints);
        Assert.Equal(7, loaded.BestStreak);
        Assert.Equal(99.5, loaded.FlightSeconds);
    }

    [Fact]
    public void Corrupt_file_loads_as_null_and_is_backed_up()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ this is not json");
        var store = new JsonStore<LanderSettings>(path, LanderJsonContext.Default.LanderSettings);

        Assert.Null(store.Load());
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void Missing_file_loads_as_null()
    {
        var store = new JsonStore<LanderSettings>(Path.Combine(_directory, "nope.json"), LanderJsonContext.Default.LanderSettings);

        Assert.Null(store.Load());
    }
}
