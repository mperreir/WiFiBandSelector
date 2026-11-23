using System.Text.RegularExpressions;
using WiFiBandSelector.Resources;

namespace WiFiBandSelector;

/// <summary>Setting family exposed by an NDIS driver.</summary>
public enum SettingFamily
{
    /// <summary>Soft preference: both bands remain allowed.</summary>
    Preference,

    /// <summary>Hard lock: only one band is allowed.</summary>
    Lock,

    /// <summary>Roaming aggressiveness (handover threshold between access points).</summary>
    Roaming,
}

/// <summary>Radio band, as inferred from a driver label.</summary>
public enum Band
{
    Band24,
    Band5,
    Band6,
}

public static class BandInfo
{
    public static string Describe(Band band) => band switch
    {
        Band.Band24 => Strings.Band24,
        Band.Band5 => Strings.Band5,
        Band.Band6 => Strings.Band6,
        _ => "?",
    };

    /// <summary>Infers a band from a driver label ("Prefer 5GHz", "2.4G Only").</summary>
    public static Band? ParseLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var s = label.ToLowerInvariant().Replace(',', '.');

        // Check 2.4 GHz first: "2.4G" must not be read as 5 or 6 GHz.
        if (s.Contains("2.4"))
            return Band.Band24;

        if (Regex.IsMatch(s, @"(?<![\d.])6\s*(\.0)?\s*ghz"))
            return Band.Band6;
        if (Regex.IsMatch(s, @"(?<![\d.])5\s*(\.0)?\s*ghz"))
            return Band.Band5;
        if (Regex.IsMatch(s, @"(?<![\d.])6\s*g(?!hz)"))
            return Band.Band6;
        if (Regex.IsMatch(s, @"(?<![\d.])5\s*g(?!hz)"))
            return Band.Band5;

        return null;
    }

    public static Band? FromFrequencyMhz(int mhz) => mhz <= 0
        ? null
        : mhz < 2500 ? Band.Band24
        : mhz < 5925 ? Band.Band5
        : Band.Band6;
}

/// <summary>One possible value of a driver parameter, as declared by the driver.</summary>
public sealed class SettingChoice
{
    public SettingChoice(string code, string label)
    {
        Code = code;
        Label = label;
        DisplayLabel = CleanLabel(label);
        Band = BandInfo.ParseLabel(label);
    }

    /// <summary>Code written to the registry ("0", "1", ...).</summary>
    public string Code { get; }

    /// <summary>Raw driver label, kept for diagnostics.</summary>
    public string Label { get; }

    /// <summary>Label without its leading number, for the menu.</summary>
    public string DisplayLabel { get; }

    public Band? Band { get; }

    private static string CleanLabel(string label)
    {
        // "1. All Band" -> "All Band"; "2.4G Only" is preserved (no space after the dot).
        var s = Regex.Replace(label, @"^\s*\d+\s*[\.\)\-:]\s+", string.Empty);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length == 0 ? label.Trim() : s;
    }
}

/// <summary>An advanced parameter read from <c>Ndi\params</c>.</summary>
public sealed class DriverParam
{
    public required string Keyword { get; init; }
    public string? ParamDesc { get; init; }
    public string? DefaultValue { get; init; }
    public bool IsEnum { get; init; }
    public IReadOnlyList<SettingChoice> Choices { get; init; } = Array.Empty<SettingChoice>();
    public string? CurrentValue { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(ParamDesc) ? Keyword : ParamDesc!;
}

/// <summary>A parameter assigned to a family, ready to feed the menu.</summary>
public sealed class DriverSetting
{
    public SettingFamily Family { get; init; }
    public required string Keyword { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<SettingChoice> Choices { get; init; }
    public string? DefaultCode { get; init; }

    /// <summary>Current code, re-read from the registry on every refresh.</summary>
    public string CurrentCode { get; internal set; } = string.Empty;

    public SettingChoice? Current => Choices.FirstOrDefault(
        c => string.Equals(c.Code, CurrentCode, StringComparison.OrdinalIgnoreCase));

    public string CurrentDisplay => Current?.DisplayLabel ?? CurrentCode;

    public Band? CurrentBand => Current?.Band;

    public SettingChoice? WithCode(string code) => Choices.FirstOrDefault(
        c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    public SettingChoice? FindDefault() => DefaultCode is null ? null : WithCode(DefaultCode);
}

/// <summary>Result of classifying an adapter's parameters.</summary>
public sealed class AdapterCapabilities
{
    public DriverSetting? Preference { get; init; }
    public DriverSetting? Lock { get; init; }
    public DriverSetting? Roaming { get; init; }

    /// <summary>Ignored parameters and their reasons, for the diagnostic report.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public bool IsSupported => Preference is not null || Lock is not null;

    public DriverSetting? For(SettingFamily family) => family switch
    {
        SettingFamily.Preference => Preference,
        SettingFamily.Lock => Lock,
        SettingFamily.Roaming => Roaming,
        _ => null,
    };
}

/// <summary>Snapshot of an adapter and its parameters, live or replayed from a fixture.</summary>
public sealed class DriverSnapshot
{
    public required string KeyPath { get; init; }
    public required string DriverDesc { get; init; }
    public string? NetCfgInstanceId { get; init; }
    public int IfType { get; init; }
    public string? InterfaceAlias { get; set; }
    public Guid? InterfaceGuid { get; set; }
    public bool InterfaceUp { get; set; }
    public required IReadOnlyList<DriverParam> Params { get; init; }

    public DriverParam? Find(string keyword) => Params.FirstOrDefault(
        p => string.Equals(p.Keyword, keyword, StringComparison.OrdinalIgnoreCase));

    public string DisplayName => string.IsNullOrWhiteSpace(InterfaceAlias)
        ? DriverDesc
        : $"{DriverDesc} ({InterfaceAlias})";
}

/// <summary>
/// Classifies driver parameters into three families.
/// No adapter name, value code or label is hard-coded: rules rely on the keyword
/// name (not localized) and, as a last resort, on bands mentioned in labels.
/// </summary>
public static class SettingClassifier
{
    /// <summary>Keywords that contain "band" without referring to a radio band.</summary>
    private static readonly string[] Blacklist =
    {
        "wfd", "miracast", "wol", "wake", "offload", "amsdu", "rekey",
        "txpower", "bwselection", "bandwidth",
    };

    /// <summary>Known keywords, classified directly and given priority.</summary>
    private static readonly Dictionary<string, SettingFamily> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bandselection"] = SettingFamily.Lock,
        ["preferredband"] = SettingFamily.Preference,
        ["roamingpreferredbandtype"] = SettingFamily.Preference,
        ["roamingaggressiveness"] = SettingFamily.Roaming,
        ["roamindicateth"] = SettingFamily.Roaming,
    };

    public static bool IsBlacklisted(string keyword)
    {
        var k = keyword.ToLowerInvariant();
        return Blacklist.Any(k.Contains);
    }

    /// <summary>Family of a keyword, or <c>null</c> if it is unrelated to bands.</summary>
    public static SettingFamily? ClassifyKeyword(string keyword)
    {
        if (IsBlacklisted(keyword))
            return null;

        if (Known.TryGetValue(keyword, out var known))
            return known;

        var k = keyword.ToLowerInvariant();

        // Order matters: RoamingPreferredBandType contains "roam" but controls the band.
        if (k.Contains("band") && (k.Contains("select") || k.Contains("lock")))
            return SettingFamily.Lock;
        if (k.Contains("band") && k.Contains("prefer"))
            return SettingFamily.Preference;
        if (k.Contains("roam"))
            return SettingFamily.Roaming;

        return null;
    }

    public static AdapterCapabilities Classify(IEnumerable<DriverParam> parameters)
    {
        var notes = new List<string>();
        var candidates = new List<(DriverParam Param, SettingFamily Family)>();
        var unclassified = new List<DriverParam>();

        foreach (var p in parameters)
        {
            if (!p.IsEnum || p.Choices.Count < 2)
            {
                if (p.IsEnum)
                    notes.Add(Strings.NoteIgnored.FormatWith(p.Keyword, p.DisplayName));
                continue;
            }

            if (IsBlacklisted(p.Keyword))
            {
                notes.Add(Strings.NoteBlacklisted.FormatWith(p.Keyword, p.DisplayName));
                continue;
            }

            var family = ClassifyKeyword(p.Keyword);
            if (family is null)
            {
                unclassified.Add(p);
                continue;
            }

            candidates.Add((p, family.Value));
        }

        // Fallback through labels: a single unclassified parameter whose values
        // explicitly name two different bands in GHz.
        if (candidates.All(c => c.Family != SettingFamily.Preference))
        {
            var byLabel = unclassified
                .Where(p => p.Choices.Count(c => c.Band is not null) >= 2)
                .Where(p => p.Choices.Where(c => c.Band is not null).Select(c => c.Band).Distinct().Count() >= 2)
                .ToList();

            if (byLabel.Count == 1)
                candidates.Add((byLabel[0], SettingFamily.Preference));
        }

        foreach (var p in unclassified)
            notes.Add(Strings.NoteUnrecognized.FormatWith(p.Keyword, p.DisplayName));

        DriverSetting? Best(SettingFamily family)
        {
            var list = candidates.Where(c => c.Family == family).ToList();
            if (list.Count == 0)
                return null;

            var chosen = list
                .OrderByDescending(c => Known.ContainsKey(c.Param.Keyword) ? 100 : 0)
                .ThenByDescending(c => c.Param.Keyword.Length <= 24 ? 10 : 0)
                .ThenBy(c => c.Param.Keyword, StringComparer.OrdinalIgnoreCase)
                .First();

            if (list.Count > 1)
            {
                var others = string.Join(", ", list.Where(c => c != chosen).Select(c => c.Param.Keyword));
                notes.Add(Strings.NoteSelected.FormatWith(chosen.Param.Keyword, FamilyName(family), others));
            }

            var setting = new DriverSetting
            {
                Family = family,
                Keyword = chosen.Param.Keyword,
                Title = chosen.Param.DisplayName,
                Choices = chosen.Param.Choices,
                DefaultCode = chosen.Param.DefaultValue,
                CurrentCode = chosen.Param.CurrentValue ?? string.Empty,
            };

            if (family == SettingFamily.Lock && setting.Choices.All(c => c.Band is null))
                notes.Add(Strings.NoteNoBandValue.FormatWith(setting.Keyword));

            return setting;
        }

        return new AdapterCapabilities
        {
            Preference = Best(SettingFamily.Preference),
            Lock = Best(SettingFamily.Lock),
            Roaming = Best(SettingFamily.Roaming),
            Notes = notes,
        };
    }

    public static string FamilyName(SettingFamily family) => family switch
    {
        SettingFamily.Preference => Strings.FamilyNamePreference,
        SettingFamily.Lock => Strings.FamilyNameLock,
        SettingFamily.Roaming => Strings.FamilyNameRoaming,
        _ => family.ToString(),
    };
}
