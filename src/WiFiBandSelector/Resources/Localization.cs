using System.Globalization;

namespace WiFiBandSelector.Resources;

/// <summary>
/// Formats localized resource strings with the current culture.
/// The strongly typed generator only creates properties, so templates use this helper.
/// </summary>
internal static class Localization
{
    public static string FormatWith(this string template, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, template, args);
}
