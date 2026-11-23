using System.Globalization;
using WiFiBandSelector;
using WiFiBandSelector.Resources;

namespace WiFiBandSelector.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("fr-FR", "Préférence", "2,4 GHz", "oui")]
    [InlineData("en-US", "Preference", "2.4 GHz", "yes")]
    [InlineData("de-DE", "Preference", "2.4 GHz", "yes")]
    public void Resources_follow_the_system_UI_culture(
        string culture, string family, string band24, string yes)
    {
        using var scope = new UiCultureScope(culture);

        Assert.Equal(family, Strings.FamilyPreference);
        Assert.Equal(band24, BandInfo.Describe(Band.Band24));
        Assert.Equal(yes, Strings.Yes);
    }

    [Theory]
    [InlineData("fr-FR", "2,4 GHz n'apparaît pas dans l'analyse des réseaux.")]
    [InlineData("en-US", "2.4 GHz does not appear in the network scan.")]
    public void Formatted_messages_use_the_current_culture(string culture, string expectedPrefix)
    {
        using var scope = new UiCultureScope(culture);

        var message = Strings.BandNotVisible.FormatWith(BandInfo.Describe(Band.Band24));
        Assert.StartsWith(expectedPrefix, message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classification_notes_are_localized()
    {
        var snapshot = Fixture.Load("mediatek-mt7921");

        string note;
        using (new UiCultureScope("fr-FR"))
            note = string.Join("|", SettingClassifier.Classify(snapshot.Params).Notes);

        Assert.Contains("liste noire", note, StringComparison.Ordinal);

        using (new UiCultureScope("en-US"))
            note = string.Join("|", SettingClassifier.Classify(snapshot.Params).Notes);

        Assert.Contains("blacklist", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_labels_are_localized()
    {
        using var scope = new UiCultureScope("fr-FR");
        Assert.StartsWith("WiFiBandSelector ", Diagnostics.BuildReport(), StringComparison.Ordinal);
        Assert.Contains("rapport de diagnostic", Diagnostics.BuildReport(), StringComparison.Ordinal);
    }

    private sealed class UiCultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture;
        private readonly CultureInfo _previousUiCulture;

        public UiCultureScope(string name)
        {
            _previousCulture = CultureInfo.CurrentCulture;
            _previousUiCulture = CultureInfo.CurrentUICulture;
            var culture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }
}
