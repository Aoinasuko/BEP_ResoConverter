using System.Text.Json;

namespace nadena.dev.resonity.remote.puppeteer;

public sealed class ConverterSettings
{
    public bool lockSaving { get; set; } = true;
    public bool asAvatar { get; set; } = true;
    public bool useStandardSize { get; set; }
    public float standardHeight { get; set; } = 1.8f;
    public bool verifyAfterExport { get; set; }
    public JsonElement expressions { get; set; }
    public static ConverterSettings Read(string? path) => string.IsNullOrEmpty(path)
        ? new ConverterSettings()
        : JsonSerializer.Deserialize<ConverterSettings>(File.ReadAllText(path)) ?? new ConverterSettings();
}
