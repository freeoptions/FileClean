namespace FileClean.Models;

public sealed class DuplicateItem : NotifyObject
{
    private bool _isSelected;
    private bool _isPreviewFocus;

    public string GroupId { get; set; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Extension { get; init; } = string.Empty;

    public long Size { get; init; }

    public long ModifiedTicksUtc { get; init; }

    public string Md5 { get; init; } = string.Empty;

    public string PreviewKind { get; init; } = "none";

    public bool IsKeepCandidate { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsPreviewFocus
    {
        get => _isPreviewFocus;
        set => SetProperty(ref _isPreviewFocus, value);
    }

    public string SizeText => FormatBytes(Size);

    public string KindText => PreviewKind switch
    {
        "image" => "图片",
        "video" => "视频",
        _ => "文件"
    };

    public string KeepText => IsKeepCandidate ? "保留" : string.Empty;

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;

        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value.ToString(value >= 100 || index == 0 ? "0" : "0.##")} {units[index]}";
    }
}
