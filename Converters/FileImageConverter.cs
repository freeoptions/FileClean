using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace FileClean.Converters;

public sealed class FileImageConverter : IValueConverter
{
    private const int MaxCachedImages = 96;
    private static readonly ConcurrentDictionary<string, BitmapImage> ImageCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> CacheOrder = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string filePath || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        var decodePixelWidth = parameter is string widthText && int.TryParse(widthText, out var width)
            ? width
            : 720;
        return GetOrLoad(filePath, decodePixelWidth);
    }

    public static BitmapImage? GetOrLoad(string filePath, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            var cacheKey = CreateCacheKey(fileInfo, decodePixelWidth);
            if (ImageCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = Math.Clamp(decodePixelWidth, 120, 2200);
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            ImageCache[cacheKey] = bitmap;
            CacheOrder.Enqueue(cacheKey);
            TrimCache();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static void Preload(string filePath, int decodePixelWidth)
    {
        _ = GetOrLoad(filePath, decodePixelWidth);
    }

    private static string CreateCacheKey(FileInfo fileInfo, int decodePixelWidth)
    {
        return $"{fileInfo.FullName}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}|{decodePixelWidth}";
    }

    private static void TrimCache()
    {
        while (ImageCache.Count > MaxCachedImages && CacheOrder.TryDequeue(out var cacheKey))
        {
            ImageCache.TryRemove(cacheKey, out _);
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
