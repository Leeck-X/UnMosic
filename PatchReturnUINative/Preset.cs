using System.Text.Json.Serialization;

namespace PatchReturnUINative;

/// <summary>一条预设</summary>
public class Preset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("function")]
    public string Function { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("typeFilter")]
    public string? TypeFilter { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    public override string ToString() => Name;
}

/// <summary>presets.json 根结构</summary>
public class PresetFile
{
    [JsonPropertyName("presets")]
    public List<Preset> Presets { get; set; } = new();
}

public static class Presets
{
    /// <summary>内置通用马赛克预设(首次运行写入 presets.json)</summary>
    public static List<Preset> BuiltIn => new()
    {
        new() { Name = "(自定义 - 不预填)", Function = "", Value = "" },
        new() { Name = "FnDrawMosaic  → false  (关闭马赛克绘制)", Function = "FnDrawMosaic", Value = "false" },
        new() { Name = "DrawMosaic    → false  (马赛克开关)", Function = "DrawMosaic", Value = "false" },
        new() { Name = "IsMosaicEnabled → false", Function = "IsMosaicEnabled", Value = "false" },
        new() { Name = "get_DrawGlOnly → false (UI 开关)", Function = "get_DrawGlOnly", Value = "false" },
        new() { Name = "GetMosaicSize → 0.01f (尺寸最小化)", Function = "GetMosaicSize", Value = "0.01f" },
        new() { Name = "MosaicAlpha   → 0f    (透明度清零)", Function = "MosaicAlpha", Value = "0f" },
        new() { Name = "MosaicStrength→ 0f    (强度清零)", Function = "MosaicStrength", Value = "0f" },
        new() { Name = "MosaicEnabled → false", Function = "MosaicEnabled", Value = "false" },
    };

    /// <summary>从 exe 同目录读 presets.json; 不存在则用内置并写一份</summary>
    public static List<Preset> Load(string exeDir)
    {
        string path = Path.Combine(exeDir, "presets.json");
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var file = System.Text.Json.JsonSerializer.Deserialize<PresetFile>(json);
                if (file?.Presets is { Count: > 0 } ps) return ps;
            }
            catch { /* 解析失败时回退到内置 */ }
        }
        try
        {
            var file = new PresetFile { Presets = BuiltIn };
            var json = System.Text.Json.JsonSerializer.Serialize(file,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
        return BuiltIn;
    }

    /// <summary>获取 exe 实际所在目录(避开单文件发布的临时解压目录)</summary>
    public static string GetExeDir()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath)) return Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
        }
        catch { }
        return AppContext.BaseDirectory;
    }
}
