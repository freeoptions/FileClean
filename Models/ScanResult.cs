using System.Collections.ObjectModel;

namespace FileClean.Models;

public sealed class ScanResult
{
    public ObservableCollection<DuplicateGroup> Groups { get; init; } = [];

    public ScanSummary Summary { get; init; } = new();

    public SupportedExtensions SupportedExtensions { get; init; } = new();
}
