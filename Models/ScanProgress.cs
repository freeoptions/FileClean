namespace FileClean.Models;

public sealed class ScanProgress
{
    public string Stage { get; set; } = "idle";

    public string Message { get; set; } = "准备就绪";

    public string CurrentPath { get; set; } = string.Empty;

    public int DirectoriesVisited { get; set; }

    public int FilesVisited { get; set; }

    public int ExcludedDirectories { get; set; }

    public int CandidateFiles { get; set; }

    public int QuickFingerprintedFiles { get; set; }

    public int TotalQuickFingerprintTargets { get; set; }

    public int HashedFiles { get; set; }

    public int TotalHashTargets { get; set; }

    public int CacheHits { get; set; }
}
