using Lander.Core.Progression;

namespace Lander.Assistant.Tests;

/// <summary>Los textos del juego, en los seis idiomas de K-Desktop.</summary>
public sealed class TextsTests
{
    [Fact]
    public void Every_row_has_the_six_languages() =>
        Assert.All(Texts.Rows, row => Assert.True(row.Count == 6, $"«{row.Key}» tiene {row.Count} idiomas"));

    [Fact]
    public void Every_achievement_and_unlock_is_translated()
    {
        foreach (var achievement in Achievements.All)
        {
            Assert.Single(Texts.All("ach." + achievement.Id).Take(1));
            Assert.Single(Texts.All("ach." + achievement.Id + ".d").Take(1));
        }

        foreach (var unlock in Unlocks.All)
        {
            Assert.Single(Texts.All("unlock." + unlock.Id).Take(1));
        }
    }

    [Theory]
    [InlineData("en", "Level 3!")]
    [InlineData("pt-BR", "Nível 3!")]
    [InlineData("ja", "Level 3!")]
    public void Language_follows_KDesktop_and_falls_back_to_English(string language, string expected)
    {
        Texts.Language = language;
        try
        {
            Assert.Equal(expected, Texts.Format("level.title", 3));
        }
        finally
        {
            Texts.Language = "es";
        }
    }
}
