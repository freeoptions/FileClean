using System.Collections.ObjectModel;

namespace FileClean.Models;

public sealed class DuplicateGroup
{
    public string Id { get; init; } = string.Empty;

    public string Md5 { get; init; } = string.Empty;

    public long TotalSize { get; set; }

    public string KeepRecommendation { get; set; } = string.Empty;

    public ObservableCollection<DuplicateItem> Items { get; init; } = [];

    public int DuplicateCount => Items.Count;

    public long ReclaimableBytes => Items.Count < 2 ? 0 : TotalSize - Items.Min(item => item.Size);

    public string TotalSizeText => DuplicateItem.FormatBytes(TotalSize);

    public string ReclaimableText => DuplicateItem.FormatBytes(ReclaimableBytes);
}
