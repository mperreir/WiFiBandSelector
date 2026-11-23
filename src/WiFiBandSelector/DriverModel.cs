using System.Text.RegularExpressions;

namespace WiFiBandSelector;

/// <summary>Famille de réglage exposée par un pilote NDIS.</summary>
public enum SettingFamily
{
    /// <summary>Préférence souple : les deux bandes restent permises.</summary>
    Preference,

    /// <summary>Verrou dur : une seule bande est permise.</summary>
    Lock,

    /// <summary>Agressivité d'itinérance (seuil de bascule entre points d'accès).</summary>
    Roaming,
}

/// <summary>Bande radio, telle qu'on sait la déduire d'un libellé de pilote.</summary>
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
        Band.Band24 => "2,4 GHz",
        Band.Band5 => "5 GHz",
        Band.Band6 => "6 GHz",
        _ => "?",
    };

    /// <summary>Déduit une bande d'un libellé localisé (« Préférer la bande 5GHz », « 2.4G Only »).</summary>
    public static Band? ParseLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var s = label.ToLowerInvariant().Replace(',', '.');

        // 2,4 GHz d'abord : « 2.4G » ne doit pas être lu comme du 5 ou du 6.
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

/// <summary>Une valeur possible d'un paramètre de pilote, telle que le pilote la déclare.</summary>
public sealed class SettingChoice
{
    public SettingChoice(string code, string label)
    {
        Code = code;
        Label = label;
        DisplayLabel = CleanLabel(label);
        Band = BandInfo.ParseLabel(label);
    }

    /// <summary>Code écrit dans le registre (« 0 », « 1 », …).</summary>
    public string Code { get; }

    /// <summary>Libellé brut du pilote, conservé pour le diagnostic.</summary>
    public string Label { get; }

    /// <summary>Libellé débarrassé de sa numérotation, pour le menu.</summary>
    public string DisplayLabel { get; }

    public Band? Band { get; }

    private static string CleanLabel(string label)
    {
        // « 1. All Band » → « All Band » ; « 2.4G Only » est préservé (pas d'espace après le point).
        var s = Regex.Replace(label, @"^\s*\d+\s*[\.\)\-:]\s+", string.Empty);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length == 0 ? label.Trim() : s;
    }
}

/// <summary>Un paramètre avancé lu sous <c>Ndi\params</c>.</summary>
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

/// <summary>Un paramètre classé dans une famille, prêt à alimenter le menu.</summary>
public sealed class DriverSetting
{
    public SettingFamily Family { get; init; }
    public required string Keyword { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<SettingChoice> Choices { get; init; }
    public string? DefaultCode { get; init; }

    /// <summary>Code courant, relu du registre à chaque rafraîchissement.</summary>
    public string CurrentCode { get; internal set; } = string.Empty;

    public SettingChoice? Current => Choices.FirstOrDefault(
        c => string.Equals(c.Code, CurrentCode, StringComparison.OrdinalIgnoreCase));

    public string CurrentDisplay => Current?.DisplayLabel ?? CurrentCode;

    public Band? CurrentBand => Current?.Band;

    public SettingChoice? WithCode(string code) => Choices.FirstOrDefault(
        c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    public SettingChoice? FindDefault() => DefaultCode is null ? null : WithCode(DefaultCode);
}

/// <summary>Résultat du classement des paramètres d'une carte.</summary>
public sealed class AdapterCapabilities
{
    public DriverSetting? Preference { get; init; }
    public DriverSetting? Lock { get; init; }
    public DriverSetting? Roaming { get; init; }

    /// <summary>Paramètres ignorés et raison, pour le rapport de diagnostic.</summary>
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

/// <summary>Instantané d'une carte et de ses paramètres, vivant ou rejoué depuis une fixture.</summary>
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
/// Classement des paramètres d'un pilote en trois familles.
/// Aucun nom de carte, aucun code de valeur et aucun libellé n'est codé en dur :
/// les règles portent sur le nom du mot-clé (non localisé) et, en dernier recours,
/// sur la présence de bandes dans les libellés.
/// </summary>
public static class SettingClassifier
{
    /// <summary>Mots-clés qui contiennent « band » sans parler de bande radio.</summary>
    private static readonly string[] Blacklist =
    {
        "wfd", "miracast", "wol", "wake", "offload", "amsdu", "rekey",
        "txpower", "bwselection", "bandwidth",
    };

    /// <summary>Mots-clés connus, classés d'office et prioritaires.</summary>
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

    /// <summary>Famille d'un mot-clé, ou <c>null</c> s'il n'a rien à voir avec la bande.</summary>
    public static SettingFamily? ClassifyKeyword(string keyword)
    {
        if (IsBlacklisted(keyword))
            return null;

        if (Known.TryGetValue(keyword, out var known))
            return known;

        var k = keyword.ToLowerInvariant();

        // L'ordre compte : RoamingPreferredBandType contient « roam » mais règle la bande.
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
                    notes.Add($"{p.Keyword} — « {p.DisplayName} » ignoré (moins de deux valeurs)");
                continue;
            }

            if (IsBlacklisted(p.Keyword))
            {
                notes.Add($"{p.Keyword} — « {p.DisplayName} » exclu (liste noire)");
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

        // Repli par les libellés : un seul paramètre non classé dont les valeurs
        // nomment explicitement deux bandes différentes en GHz.
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
            notes.Add($"{p.Keyword} — « {p.DisplayName} » non reconnu");

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
                notes.Add($"{chosen.Param.Keyword} retenu pour {FamilyName(family)} ; écartés : {others}");
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
                notes.Add($"{setting.Keyword} : aucune valeur ne nomme une bande, le verrou sera écrit sans vérification");

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
        SettingFamily.Preference => "préférence",
        SettingFamily.Lock => "verrouillage",
        SettingFamily.Roaming => "itinérance",
        _ => family.ToString(),
    };
}
