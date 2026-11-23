using System.Text.Json;
using System.Text.Json.Serialization;
using WiFiBandSelector;

namespace WiFiBandSelector.Tests;

/// <summary>Loads a serialized registry snapshot without touching the machine's registry.</summary>
internal static class Fixture
{
    public static DriverSnapshot Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".json");
        Assert.True(File.Exists(path), $"Fixture introuvable : {path}");

        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<FileModel>(json, JsonOptions)
                   ?? throw new InvalidDataException($"Fixture vide : {name}");

        return new DriverSnapshot
        {
            KeyPath = file.KeyPath,
            DriverDesc = file.DriverDesc,
            IfType = file.IfType,
            Params = file.Params.Select(p => new DriverParam
            {
                Keyword = p.Keyword,
                ParamDesc = p.ParamDesc,
                DefaultValue = p.Default,
                IsEnum = p.IsEnum,
                CurrentValue = p.Current,
                Choices = p.Choices.Select(c => new SettingChoice(c.Code, c.Label)).ToList(),
            }).ToList(),
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class FileModel
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("driverDesc")] public string DriverDesc { get; set; } = "";
        [JsonPropertyName("keyPath")] public string KeyPath { get; set; } = "";
        [JsonPropertyName("ifType")] public int IfType { get; set; } = 71;
        [JsonPropertyName("params")] public List<ParamModel> Params { get; set; } = new();
    }

    private sealed class ParamModel
    {
        [JsonPropertyName("keyword")] public string Keyword { get; set; } = "";
        [JsonPropertyName("paramDesc")] public string? ParamDesc { get; set; }
        [JsonPropertyName("default")] public string? Default { get; set; }
        [JsonPropertyName("current")] public string? Current { get; set; }
        [JsonPropertyName("isEnum")] public bool IsEnum { get; set; }
        [JsonPropertyName("choices")] public List<ChoiceModel> Choices { get; set; } = new();
    }

    private sealed class ChoiceModel
    {
        [JsonPropertyName("code")] public string Code { get; set; } = "";
        [JsonPropertyName("label")] public string Label { get; set; } = "";
    }
}
