using System.Globalization;
using System.IO;
using System.Text.Json;
using FileClean.Models;

namespace FileClean.Services;

public sealed class ConfigService
{
    private const string ConfigFileName = "fileclean-config.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string StorageDirectory => AppContext.BaseDirectory;

    public string ConfigPath => Path.Combine(StorageDirectory, ConfigFileName);

    public async Task<AppConfig> LoadAsync()
    {
        if (!File.Exists(ConfigPath))
        {
            return new AppConfig();
        }

        try
        {
            await using var stream = File.OpenRead(ConfigPath);
            var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, JsonOptions);
            return NormalizeConfig(config);
        }
        catch
        {
            return new AppConfig();
        }
    }

    public async Task<AppConfig> SaveAsync(AppConfig config)
    {
        var normalized = NormalizeConfig(config);
        Directory.CreateDirectory(StorageDirectory);

        await using var stream = File.Create(ConfigPath);
        await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions);
        return normalized;
    }

    public async Task<string> ExportAsync(AppConfig config)
    {
        var normalized = NormalizeConfig(config);

        if (string.IsNullOrWhiteSpace(normalized.ExportFolder))
        {
            throw new InvalidOperationException("请先选择导出目录。");
        }

        Directory.CreateDirectory(normalized.ExportFolder);

        var payload = new ConfigExportPayload
        {
            ExportedAt = DateTimeOffset.Now,
            App = "FileClean",
            Version = 1,
            Data = normalized
        };

        var filePath = Path.Combine(normalized.ExportFolder, CreateExportFileName());
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions);
        return filePath;
    }

    public async Task<AppConfig> ImportAsync(AppConfig currentConfig, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("请选择要导入的 JSON 配置文件。", filePath);
        }

        await using var stream = File.OpenRead(filePath);
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        AppConfig? incoming = null;
        if (root.TryGetProperty("data", out var dataElement))
        {
            incoming = dataElement.Deserialize<AppConfig>(JsonOptions);
        }
        else
        {
            incoming = root.Deserialize<AppConfig>(JsonOptions);
        }

        var merged = MergeConfigs(currentConfig, incoming);
        return await SaveAsync(merged);
    }

    public static string CreateExportFileName()
    {
        var now = DateTime.Now;
        return $"FileClean_exportConfig_{now:yyyy-MM-dd HH_mm_ss}.json";
    }

    public static AppConfig NormalizeConfig(AppConfig? config)
    {
        if (config is null)
        {
            return new AppConfig();
        }

        var folders = new List<FolderEntry>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in config.Folders)
        {
            if (string.IsNullOrWhiteSpace(folder.Path))
            {
                continue;
            }

            var trimmedPath = folder.Path.Trim();
            if (!seenFolders.Add(trimmedPath))
            {
                continue;
            }

            folders.Add(new FolderEntry
            {
                Path = trimmedPath,
                Enabled = folder.Enabled
            });
        }

        var excludedFolders = NormalizeFolderPaths(config.ExcludedFolders);
        var extensions = NormalizeExtensions(config.CustomExtensions);
        var exportFolder = string.IsNullOrWhiteSpace(config.ExportFolder)
            ? null
            : config.ExportFolder.Trim();

        return new AppConfig
        {
            Folders = folders,
            ExcludedFolders = excludedFolders,
            CustomExtensions = extensions,
            ExportFolder = exportFolder
        };
    }

    public static List<string> NormalizeFolderPaths(IEnumerable<string>? folderPaths)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folderPath in folderPaths ?? [])
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                continue;
            }

            var value = folderPath.Trim();
            if (!seen.Add(value))
            {
                continue;
            }

            normalized.Add(value);
        }

        normalized.Sort(StringComparer.OrdinalIgnoreCase);
        return normalized;
    }

    public static List<string> NormalizeExtensions(IEnumerable<string>? extensions)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var extension in extensions ?? [])
        {
            var value = NormalizeExtension(extension);
            if (value is null || !seen.Add(value))
            {
                continue;
            }

            normalized.Add(value);
        }

        normalized.Sort(StringComparer.OrdinalIgnoreCase);
        return normalized;
    }

    public static string? NormalizeExtension(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim().ToLower(CultureInfo.InvariantCulture);
        var normalized = trimmed.StartsWith('.') ? trimmed : $".{trimmed}";
        return normalized.Length > 1 && normalized.Skip(1).All(char.IsLetterOrDigit)
            ? normalized
            : null;
    }

    private static AppConfig MergeConfigs(AppConfig currentConfig, AppConfig? importedConfig)
    {
        var current = NormalizeConfig(currentConfig);
        var incoming = NormalizeConfig(importedConfig);

        return NormalizeConfig(new AppConfig
        {
            Folders = current.Folders.Concat(incoming.Folders).ToList(),
            ExcludedFolders = current.ExcludedFolders.Concat(incoming.ExcludedFolders).ToList(),
            CustomExtensions = current.CustomExtensions.Concat(incoming.CustomExtensions).ToList(),
            ExportFolder = incoming.ExportFolder ?? current.ExportFolder
        });
    }

    private sealed class ConfigExportPayload
    {
        public DateTimeOffset ExportedAt { get; init; }

        public string App { get; init; } = "FileClean";

        public int Version { get; init; }

        public AppConfig Data { get; init; } = new();
    }
}
