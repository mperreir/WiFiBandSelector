using WiFiBandSelector;

namespace WiFiBandSelector.Tests;

public sealed class BandInfoTests
{
    [Theory]
    [InlineData("Préférer la bande 5 GHz", Band.Band5)]
    [InlineData("Préférer bande 2,4 GHz", Band.Band24)]
    [InlineData("2.4G Only", Band.Band24)]
    [InlineData("5G Only", Band.Band5)]
    [InlineData("Prefer 6GHz", Band.Band6)]
    [InlineData("1. All Band", null)]
    [InlineData("Auto", null)]
    [InlineData("802.11ax", null)]
    public void Band_is_read_from_the_driver_label(string label, Band? expected) =>
        Assert.Equal(expected, BandInfo.ParseLabel(label));

    [Theory]
    [InlineData(2412, Band.Band24)]
    [InlineData(2462, Band.Band24)]
    [InlineData(5180, Band.Band5)]
    [InlineData(5905, Band.Band5)]
    [InlineData(5955, Band.Band6)]
    [InlineData(7125, Band.Band6)]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    public void Band_is_read_from_a_frequency(int mhz, Band? expected) =>
        Assert.Equal(expected, BandInfo.FromFrequencyMhz(mhz));

    [Theory]
    [InlineData("1. All Band", "All Band")]
    [InlineData("3. 5G Only", "5G Only")]
    [InlineData("2.4G Only", "2.4G Only")]
    [InlineData("Activé(e)", "Activé(e)")]
    public void Menu_label_drops_the_enumeration_prefix(string raw, string expected) =>
        Assert.Equal(expected, new SettingChoice("0", raw).DisplayLabel);

    [Fact]
    public void Choice_code_is_kept_as_a_string_for_non_numeric_driver_enums()
    {
        var capability = SettingClassifier.Classify(Fixture.Load("exotic-band-mode").Params);
        Assert.Equal("five", capability.Preference!.CurrentCode);
        Assert.Equal("Prefer 5 GHz", capability.Preference.CurrentDisplay);
    }
}
