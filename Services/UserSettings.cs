using System.IO;
using System.Text.Json;

namespace CSVEditor.Services;

/// <summary>轻量用户设置（字号等），按 本地应用数据 -> 程序目录 的顺序尝试读写。</summary>
public static class UserSettings
{
    private static readonly string[] CandidateDirectories =
    [
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CSVEditor"),
        Path.Combine(AppContext.BaseDirectory, "settings"),
    ];

    private static string? _resolvedPath;

    private sealed class Model
    {
        public double FontSize { get; set; } = 13;
    }

    public static double LoadFontSize()
    {
        var size = Read().FontSize;
        return size is >= 10 and <= 24 ? size : 13;
    }

    public static void SaveFontSize(double fontSize)
    {
        var model = Read();
        model.FontSize = fontSize;
        Write(model);
    }

    private static Model Read()
    {
        foreach (var directory in CandidateDirectories)
        {
            try
            {
                var path = Path.Combine(directory, "settings.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var model = JsonSerializer.Deserialize<Model>(json);
                    if (model is not null)
                    {
                        _resolvedPath = path;
                        return model;
                    }
                }
            }
            catch (Exception)
            {
                // 该位置不可读，尝试下一个
            }
        }

        return new Model();
    }

    private static void Write(Model model)
    {
        var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });

        if (_resolvedPath is not null && TryWrite(_resolvedPath, json))
        {
            return;
        }

        foreach (var directory in CandidateDirectories)
        {
            var path = Path.Combine(directory, "settings.json");
            if (TryWrite(path, json))
            {
                _resolvedPath = path;
                return;
            }
        }
    }

    private static bool TryWrite(string path, string json)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception)
        {
            // 该位置不可写，尝试下一个；完全不可写时仅失去持久化能力
            return false;
        }
    }
}
