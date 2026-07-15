using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FileClean.Models;

namespace FileClean.Services;

public sealed class DuplicateScanner
{
    private const int QuickHashBytes = 128 * 1024;
    private const int MaxWarningCount = 120;
    private const string HashCacheFileName = "fileclean-hash-cache.json";

    private static readonly string[] DefaultImageExtensions =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".heif", ".avif", ".svg"
    ];

    private static readonly string[] DefaultVideoExtensions =
    [
        ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".flv", ".webm", ".m4v", ".mpeg", ".mpg", ".ts", ".mts", ".m2ts", ".3gp", ".3g2", ".ogv"
    ];

    private readonly string _hashCachePath = Path.Combine(AppContext.BaseDirectory, HashCacheFileName);
    private readonly int _quickFingerprintConcurrency;
    private readonly int _fullHashConcurrency;

    public DuplicateScanner()
    {
        var cpuCount = Math.Max(1, Environment.ProcessorCount);
        _quickFingerprintConcurrency = Math.Max(4, Math.Min(cpuCount * 2, 12));
        _fullHashConcurrency = Math.Max(2, Math.Min(cpuCount, 6));
    }

    public async Task<ScanResult> ScanAsync(
        AppConfig config,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var normalizedConfig = ConfigService.NormalizeConfig(config);
        var enabledFolders = normalizedConfig.Folders.Where(folder => folder.Enabled).ToList();
        var excludedFolders = NormalizeExcludedFolders(normalizedConfig.ExcludedFolders);
        var supportedExtensions = GetSupportedExtensions(normalizedConfig.CustomExtensions);
        var scanState = new ScanState();
        var counters = new ScanCounters();

        Report(progress, "collecting", "正在收集媒体文件...", scanState, counters);

        await Task.Run(() =>
        {
            foreach (var folder in enabledFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CollectSupportedFiles(folder.Path, scanState, supportedExtensions, excludedFolders, progress, counters, cancellationToken);
                Report(progress, "collecting", $"已读取目录：{folder.Path}", scanState, counters, folder.Path);
            }
        }, cancellationToken);

        var duplicateSizeBuckets = scanState.CandidateFiles
            .GroupBy(file => file.Size)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToList();

        counters.TotalQuickFingerprintTargets = duplicateSizeBuckets.Count;
        Report(progress, "fingerprinting", "正在做快速指纹筛选...", scanState, counters);

        var fingerprintedItems = await RunWithConcurrencyAsync(
            duplicateSizeBuckets,
            _quickFingerprintConcurrency,
            async (file, token) =>
            {
                try
                {
                    var fingerprint = await CreateQuickFingerprintAsync(file.Path, file.Size, token);
                    return file with { Fingerprint = fingerprint };
                }
                catch
                {
                    PushWarning(scanState, $"快速指纹计算失败：{file.Path}");
                    return null;
                }
                finally
                {
                    Interlocked.Increment(ref counters.QuickFingerprintedFiles);
                    Report(progress, "fingerprinting", "正在做快速指纹筛选...", scanState, counters);
                }
            },
            cancellationToken);

        var fullHashTargets = fingerprintedItems
            .Where(file => file is not null)
            .Cast<CandidateFile>()
            .GroupBy(file => $"{file.Size}:{file.Fingerprint}")
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToList();

        counters.TotalHashTargets = fullHashTargets.Count;
        Report(progress, "hashing", "正在计算完整 MD5...", scanState, counters);

        var hashCache = new ConcurrentDictionary<string, HashCacheEntry>(
            await LoadHashCacheAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);
        var cacheChanged = 0;

        var hashedItems = await RunWithConcurrencyAsync(
            fullHashTargets,
            _fullHashConcurrency,
            async (file, token) =>
            {
                var cacheKey = CreateCacheKey(file.Path);
                var canReuseCache = hashCache.TryGetValue(cacheKey, out var cached)
                    && cached.Size == file.Size
                    && cached.ModifiedTicksUtc == file.ModifiedTicksUtc
                    && !string.IsNullOrWhiteSpace(cached.Md5);

                try
                {
                    if (canReuseCache)
                    {
                        Interlocked.Increment(ref counters.CacheHits);
                        return CreateDuplicateItem(file, cached!.Md5);
                    }

                    var md5 = await HashFileMd5Async(file.Path, token);
                    hashCache[cacheKey] = new HashCacheEntry
                    {
                        Path = file.Path,
                        Size = file.Size,
                        ModifiedTicksUtc = file.ModifiedTicksUtc,
                        Md5 = md5
                    };
                    Interlocked.Exchange(ref cacheChanged, 1);
                    return CreateDuplicateItem(file, md5);
                }
                catch
                {
                    PushWarning(scanState, $"完整 MD5 计算失败：{file.Path}");
                    return null;
                }
                finally
                {
                    if (!canReuseCache)
                    {
                        Interlocked.Increment(ref counters.HashedFiles);
                    }

                    Report(progress, "hashing", "正在计算完整 MD5...", scanState, counters);
                }
            },
            cancellationToken);

        if (cacheChanged == 1)
        {
            await SaveHashCacheAsync(
                hashCache.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                cancellationToken);
        }

        var groups = hashedItems
            .Where(item => item is not null)
            .Cast<DuplicateItem>()
            .GroupBy(item => item.Md5)
            .Where(group => group.Count() > 1)
            .Select(group => CreateDuplicateGroup(group.Key, group.ToList()))
            .OrderByDescending(group => group.ReclaimableBytes)
            .ToList();

        var summary = new ScanSummary
        {
            ScannedFolders = enabledFolders.Count,
            DirectoriesVisited = scanState.DirectoriesVisited,
            FilesVisited = scanState.FilesVisited,
            ExcludedDirectories = scanState.ExcludedDirectories,
            CandidateFiles = scanState.CandidateFiles.Count,
            QuickFingerprintFiles = counters.QuickFingerprintedFiles,
            HashedFiles = counters.HashedFiles,
            CacheHits = counters.CacheHits,
            DuplicateGroups = groups.Count,
            DuplicateFiles = groups.Sum(group => group.Items.Count),
            ReclaimableBytes = groups.Sum(group => group.ReclaimableBytes),
            Warnings = scanState.Warnings.ToList()
        };

        Report(
            progress,
            "done",
            groups.Count > 0 ? $"扫描完成，找到 {groups.Count} 组重复文件。" : "扫描完成，暂未发现重复文件。",
            scanState,
            counters);

        return new ScanResult
        {
            Groups = new ObservableCollection<DuplicateGroup>(groups),
            Summary = summary,
            SupportedExtensions = new SupportedExtensions
            {
                Images = DefaultImageExtensions.ToList(),
                Videos = DefaultVideoExtensions.ToList(),
                Custom = normalizedConfig.CustomExtensions
            }
        };
    }

    private static HashSet<string> GetSupportedExtensions(IEnumerable<string> customExtensions)
    {
        return new HashSet<string>(
            DefaultImageExtensions.Concat(DefaultVideoExtensions).Concat(ConfigService.NormalizeExtensions(customExtensions)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> NormalizeExcludedFolders(IEnumerable<string> excludedFolders)
    {
        return ConfigService.NormalizeFolderPaths(excludedFolders)
            .Select(TryNormalizeDirectoryPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsExcludedPath(string directoryPath, IReadOnlyList<string> excludedFolders)
    {
        if (excludedFolders.Count == 0)
        {
            return false;
        }

        var normalizedDirectory = TryNormalizeDirectoryPath(directoryPath);
        if (normalizedDirectory is null)
        {
            return false;
        }

        return excludedFolders.Any(excludedFolder =>
            string.Equals(normalizedDirectory, excludedFolder, StringComparison.OrdinalIgnoreCase)
            || normalizedDirectory.StartsWith($"{excludedFolder}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryNormalizeDirectoryPath(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(folderPath.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    private static void CollectSupportedFiles(
        string rootFolder,
        ScanState scanState,
        HashSet<string> supportedExtensions,
        IReadOnlyList<string> excludedFolders,
        IProgress<ScanProgress>? progress,
        ScanCounters counters,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootFolder))
        {
            PushWarning(scanState, $"目录不存在：{rootFolder}");
            return;
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootFolder);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();

            if (IsExcludedPath(currentDirectory, excludedFolders))
            {
                scanState.ExcludedDirectories++;
                Report(progress, "collecting", $"已跳过排除目录：{currentDirectory}", scanState, counters, currentDirectory);
                continue;
            }

            FileSystemInfo[] entries;

            try
            {
                entries = new DirectoryInfo(currentDirectory).GetFileSystemInfos();
            }
            catch
            {
                PushWarning(scanState, $"无法访问目录：{currentDirectory}");
                continue;
            }

            scanState.DirectoriesVisited++;
            if (scanState.DirectoriesVisited % 30 == 0)
            {
                Report(progress, "collecting", "正在收集媒体文件...", scanState, counters, currentDirectory);
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (entry is DirectoryInfo directory)
                {
                    if (IsExcludedPath(directory.FullName, excludedFolders))
                    {
                        scanState.ExcludedDirectories++;
                        Report(progress, "collecting", $"已跳过排除目录：{directory.FullName}", scanState, counters, directory.FullName);
                    }
                    else
                    {
                        pendingDirectories.Push(directory.FullName);
                    }

                    continue;
                }

                if (entry is not FileInfo file)
                {
                    continue;
                }

                scanState.FilesVisited++;
                var extension = file.Extension.ToLowerInvariant();
                if (!supportedExtensions.Contains(extension))
                {
                    continue;
                }

                try
                {
                    file.Refresh();
                    if (!file.Exists || file.Length <= 0)
                    {
                        continue;
                    }

                    scanState.CandidateFiles.Add(new CandidateFile(
                        file.FullName,
                        file.Name,
                        extension,
                        file.Length,
                        file.LastWriteTimeUtc.Ticks,
                        null));
                }
                catch
                {
                    PushWarning(scanState, $"读取文件信息失败：{file.FullName}");
                }

                if (scanState.FilesVisited % 400 == 0)
                {
                    Report(progress, "collecting", "正在收集媒体文件...", scanState, counters, currentDirectory);
                }
            }
        }
    }

    private static async Task<string> CreateQuickFingerprintAsync(
        string filePath,
        long size,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, true);
        using var sha1 = SHA1.Create();
        var sizeBytes = BitConverter.GetBytes(size);
        sha1.TransformBlock(sizeBytes, 0, sizeBytes.Length, null, 0);

        if (size <= QuickHashBytes * 2L)
        {
            await HashRangeAsync(stream, sha1, 0, size, cancellationToken);
        }
        else
        {
            await HashRangeAsync(stream, sha1, 0, QuickHashBytes, cancellationToken);
            await HashRangeAsync(stream, sha1, Math.Max(0, size - QuickHashBytes), QuickHashBytes, cancellationToken);
        }

        sha1.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha1.Hash ?? []).ToLowerInvariant();
    }

    private static async Task HashRangeAsync(
        FileStream stream,
        HashAlgorithm hash,
        long position,
        long bytesToRead,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(QuickHashBytes, Math.Max(1, (int)Math.Min(bytesToRead, QuickHashBytes)))];
        stream.Seek(position, SeekOrigin.Begin);
        var remaining = bytesToRead;

        while (remaining > 0)
        {
            var readSize = (int)Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, readSize), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            hash.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }
    }

    private static async Task<string> HashFileMd5Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024, true);
        using var md5 = MD5.Create();
        var hash = await md5.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<List<TOutput?>> RunWithConcurrencyAsync<TInput, TOutput>(
        IReadOnlyList<TInput> items,
        int concurrency,
        Func<TInput, CancellationToken, Task<TOutput?>> worker,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var results = new TOutput?[items.Count];
        var cursor = -1;
        var workerCount = Math.Min(Math.Max(1, concurrency), items.Count);

        var tasks = Enumerable.Range(0, workerCount).Select(async _ =>
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = Interlocked.Increment(ref cursor);
                if (index >= items.Count)
                {
                    break;
                }

                results[index] = await worker(items[index], cancellationToken);
            }
        });

        await Task.WhenAll(tasks);
        return results.ToList();
    }

    private static DuplicateItem CreateDuplicateItem(CandidateFile file, string md5)
    {
        return new DuplicateItem
        {
            Path = file.Path,
            Name = file.Name,
            Extension = file.Extension,
            Size = file.Size,
            ModifiedTicksUtc = file.ModifiedTicksUtc,
            Md5 = md5,
            PreviewKind = CreatePreviewKind(file.Extension)
        };
    }

    private static DuplicateGroup CreateDuplicateGroup(string md5, List<DuplicateItem> items)
    {
        var sortedItems = items
            .OrderBy(item => item.Path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var keepCandidate = GetKeepCandidate(sortedItems);
        var group = new DuplicateGroup
        {
            Id = md5,
            Md5 = md5,
            TotalSize = sortedItems.Sum(item => item.Size),
            KeepRecommendation = keepCandidate.Path
        };

        foreach (var item in sortedItems)
        {
            item.GroupId = md5;
            item.IsKeepCandidate = string.Equals(item.Path, keepCandidate.Path, StringComparison.OrdinalIgnoreCase);
            group.Items.Add(item);
        }

        return group;
    }

    private static DuplicateItem GetKeepCandidate(IEnumerable<DuplicateItem> items)
    {
        return items
            .OrderBy(item => item.Path.Length)
            .ThenBy(item => item.Path, StringComparer.CurrentCultureIgnoreCase)
            .First();
    }

    private static string CreatePreviewKind(string extension)
    {
        if (DefaultImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return "image";
        }

        if (DefaultVideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return "video";
        }

        return "none";
    }

    private async Task<Dictionary<string, HashCacheEntry>> LoadHashCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_hashCachePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_hashCachePath);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, HashCacheEntry>>(stream, cancellationToken: cancellationToken)
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SaveHashCacheAsync(Dictionary<string, HashCacheEntry> cache, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AppContext.BaseDirectory);
        await using var stream = File.Create(_hashCachePath);
        await JsonSerializer.SerializeAsync(stream, cache, cancellationToken: cancellationToken);
    }

    private static string CreateCacheKey(string filePath)
    {
        return filePath.ToLowerInvariant();
    }

    private static void PushWarning(ScanState scanState, string message)
    {
        if (scanState.Warnings.Count < MaxWarningCount)
        {
            scanState.Warnings.Add(message);
        }
    }

    private static void Report(
        IProgress<ScanProgress>? progress,
        string stage,
        string message,
        ScanState scanState,
        ScanCounters counters,
        string currentPath = "")
    {
        progress?.Report(new ScanProgress
        {
            Stage = stage,
            Message = message,
            CurrentPath = currentPath,
            DirectoriesVisited = scanState.DirectoriesVisited,
            FilesVisited = scanState.FilesVisited,
            ExcludedDirectories = scanState.ExcludedDirectories,
            CandidateFiles = scanState.CandidateFiles.Count,
            QuickFingerprintedFiles = counters.QuickFingerprintedFiles,
            TotalQuickFingerprintTargets = counters.TotalQuickFingerprintTargets,
            HashedFiles = counters.HashedFiles,
            TotalHashTargets = counters.TotalHashTargets,
            CacheHits = counters.CacheHits
        });
    }

    private sealed record CandidateFile(
        string Path,
        string Name,
        string Extension,
        long Size,
        long ModifiedTicksUtc,
        string? Fingerprint);

    private sealed class ScanState
    {
        public int DirectoriesVisited;

        public int FilesVisited;

        public int ExcludedDirectories;

        public List<CandidateFile> CandidateFiles { get; } = [];

        public List<string> Warnings { get; } = [];
    }

    private sealed class ScanCounters
    {
        public int TotalQuickFingerprintTargets;

        public int QuickFingerprintedFiles;

        public int TotalHashTargets;

        public int HashedFiles;

        public int CacheHits;
    }

    private sealed class HashCacheEntry
    {
        public string Path { get; init; } = string.Empty;

        public long Size { get; init; }

        public long ModifiedTicksUtc { get; init; }

        public string Md5 { get; init; } = string.Empty;
    }
}
