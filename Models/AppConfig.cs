namespace FileClean.Models;

public sealed class AppConfig
{
    public List<FolderEntry> Folders { get; set; } = [];

    public List<string> ExcludedFolders { get; set; } = [];

    public List<string> CustomExtensions { get; set; } = [];

    public string? ExportFolder { get; set; }
}
