using WiFiBandSelector;
using WiFiBandSelector.Resources;

namespace WiFiBandSelector.Tests;

public sealed class ClassificationTests
{
    [Theory]
    [InlineData("mediatek-mt7921", "PreferredBand", "BandSelection", "RoamIndicateTh", "LowPowerEnable")]
    [InlineData("intel-ax211", "RoamingPreferredBandType", null, "RoamingAggressiveness", null)]
    [InlineData("realtek-rtl8852be", "PreferredBand", null, "RoamingAggressiveness", null)]
    [InlineData("exotic-band-mode", "RadioMode", null, null, null)]
    [InlineData("no-band", null, null, "RoamingAggressiveness", null)]
    public void Fixture_is_classified_as_expected(
        string fixture, string? preference, string? lockKeyword, string? roaming, string? powerSaving)
    {
        var snapshot = Fixture.Load(fixture);
        var capabilities = SettingClassifier.Classify(snapshot.Params);

        Assert.Equal(preference, capabilities.Preference?.Keyword);
        Assert.Equal(lockKeyword, capabilities.Lock?.Keyword);
        Assert.Equal(roaming, capabilities.Roaming?.Keyword);
        Assert.Equal(powerSaving, capabilities.PowerSaving?.Keyword);
        Assert.Equal(preference is not null || lockKeyword is not null, capabilities.IsSupported);
    }

    [Fact]
    public void MediaTek_lists_all_three_bands_without_a_capability_heuristic()
    {
        var capabilities = SettingClassifier.Classify(Fixture.Load("mediatek-mt7921").Params);

        Assert.Equal(Band.Band24, capabilities.Preference!.WithCode("1")!.Band);
        Assert.Equal(Band.Band5, capabilities.Preference.WithCode("2")!.Band);
        Assert.Equal(Band.Band6, capabilities.Preference.WithCode("3")!.Band);

        // This driver's lock does not expose the 6 GHz band: do not invent it.
        Assert.Equal(new Band?[] { null, Band.Band24, Band.Band5 },
            capabilities.Lock!.Choices.Select(c => c.Band).ToArray());
    }

    [Fact]
    public void Blacklisted_parameters_are_reported_once()
    {
        var capabilities = SettingClassifier.Classify(Fixture.Load("mediatek-mt7921").Params);

        Assert.Contains(
            Strings.NoteBlacklisted.FormatWith("AMSDURx", "AMSDU Rx"),
            capabilities.Notes);
        Assert.DoesNotContain(
            capabilities.Notes,
            n => n.Contains("WfdGOOperatingChannel", StringComparison.Ordinal)
                 && n.Contains(SettingClassifier.FamilyName(SettingFamily.Preference), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("WfdGOOperatingChannel")]
    [InlineData("BWSelection6G")]
    [InlineData("DisableWakeOnPattern")]
    [InlineData("AMSDUTx")]
    [InlineData("TxPowerLevel")]
    public void Keywords_without_radio_band_meaning_are_excluded(string keyword)
    {
        Assert.True(SettingClassifier.IsBlacklisted(keyword));
        Assert.Null(SettingClassifier.ClassifyKeyword(keyword));
    }

    [Fact]
    public void Intel_wireless_mode_is_not_taken_for_a_band_lock()
    {
        var capabilities = SettingClassifier.Classify(Fixture.Load("intel-ax211").Params);
        Assert.Null(capabilities.Lock);
        Assert.Null(SettingClassifier.ClassifyKeyword("WirelessMode"));
    }

    [Fact]
    public void Current_and_default_codes_come_from_the_registry_snapshot()
    {
        var capabilities = SettingClassifier.Classify(Fixture.Load("mediatek-mt7921").Params);

        Assert.Equal("2", capabilities.Preference!.CurrentCode);
        Assert.Equal("2", capabilities.Preference.DefaultCode);
        Assert.Equal("Préférer la bande 5 GHz", capabilities.Preference.CurrentDisplay);
        Assert.Equal("0", capabilities.Lock!.CurrentCode);
    }

    [Fact]
    public void A_parameter_with_a_single_choice_is_ignored()
    {
        var param = new DriverParam
        {
            Keyword = "PreferredBand",
            ParamDesc = "Bande privilégiée",
            IsEnum = true,
            CurrentValue = "0",
            Choices = new[] { new SettingChoice("0", "Aucune préférence") },
        };

        var capabilities = SettingClassifier.Classify(new[] { param });
        Assert.Null(capabilities.Preference);
        Assert.Contains(
            Strings.NoteIgnored.FormatWith("PreferredBand", "Bande privilégiée"),
            capabilities.Notes);
    }

    [Fact]
    public void Power_saving_is_exposed_from_the_driver_enum()
    {
        var capabilities = SettingClassifier.Classify(Fixture.Load("mediatek-mt7921").Params);
        var power = capabilities.PowerSaving!;

        Assert.Equal(SettingFamily.PowerSaving, power.Family);
        Assert.Equal("Power Saving", power.Title);
        Assert.Equal(new[] { "0", "1" }, power.Choices.Select(c => c.Code).ToArray());
        Assert.Equal("1", power.CurrentCode);
        Assert.Equal("1", power.DefaultCode);
        Assert.Equal("Auto", power.CurrentDisplay);
        Assert.Equal("Auto", power.FindDefault()!.DisplayLabel);
        Assert.All(power.Choices, choice => Assert.Null(choice.Band));
    }

    [Theory]
    [InlineData("LowPowerEnable")]
    [InlineData("PowerSaveMode")]
    [InlineData("PowerSavingMode")]
    [InlineData("WirelessPowerManagement")]
    public void Power_saving_keywords_are_recognized(string keyword)
    {
        Assert.Equal(SettingFamily.PowerSaving, SettingClassifier.ClassifyKeyword(keyword));
    }

    [Theory]
    [InlineData("DisableWakeOnMagic")]
    [InlineData("DisableWakeOnPattern")]
    [InlineData("WakeOnLan")]
    [InlineData("TxPowerLevel")]
    [InlineData("MaxTxPower")]
    public void Wake_and_transmit_power_stay_excluded(string keyword)
    {
        Assert.True(SettingClassifier.IsBlacklisted(keyword));
        Assert.Null(SettingClassifier.ClassifyKeyword(keyword));
    }
}
