namespace FileClean.Models;

public sealed class ScanSummary
{
    public int ScannedFolders { get; set; }

    public int DirectoriesVisited { get; set; }

    public int FilesVisited { get; set; }

    public int ExcludedDirectories { get; set; }

    public int CandidateFiles { get; set; }

    public int QuickFingerprintFiles { get; set; }

    public int HashedFiles { get; set; }

    public int CacheHits { get; set; }

    public int DuplicateGroups { get; set; }

    public int DuplicateFiles { get; set; }

    public long ReclaimableBytes { get; set; }

    public List<string> Warnings { get; set; } = [];

    public string ReclaimableText => DuplicateItem.FormatBytes(ReclaimableBytes);
}
