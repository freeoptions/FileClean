using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using FileClean.Converters;
using XamlAnimatedGif;
using WpfImage = System.Windows.Controls.Image;

namespace FileClean.Controls;

public sealed class FilePreviewImage : WpfImage
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(FilePreviewImage), new PropertyMetadata(null, PreviewChanged));

    public static readonly DependencyProperty DecodePixelWidthProperty = DependencyProperty.Register(
        nameof(DecodePixelWidth), typeof(int), typeof(FilePreviewImage), new PropertyMetadata(720, PreviewChanged));

    private FileStream? _gifStream;
    private string? _loadedPath;
    private int _loadedDecodeWidth;
    private Window? _window;

    public FilePreviewImage()
    {
        AnimationBehavior.SetRepeatBehavior(this, RepeatBehavior.Forever);
        // 按需解码，避免将整张动图的所有帧缓存到内存。
        AnimationBehavior.SetCacheFramesInMemory(this, false);
        Loaded += PreviewLoaded;
        Unloaded += PreviewUnloaded;
        IsVisibleChanged += (_, _) => UpdatePreview();
    }

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public int DecodePixelWidth
    {
        get => (int)GetValue(DecodePixelWidthProperty);
        set => SetValue(DecodePixelWidthProperty, value);
    }

    private static void PreviewChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        ((FilePreviewImage)sender).UpdatePreview();
    }

    private void PreviewLoaded(object sender, RoutedEventArgs e)
    {
        DetachWindow();
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.StateChanged += WindowStateChanged;
        }

        UpdatePreview();
    }

    private void PreviewUnloaded(object sender, RoutedEventArgs e)
    {
        DetachWindow();
        ReleasePreview();
    }

    private void WindowStateChanged(object? sender, EventArgs e) => UpdatePreview();

    private void DetachWindow()
    {
        if (_window is not null)
        {
            _window.StateChanged -= WindowStateChanged;
            _window = null;
        }
    }

    private void UpdatePreview()
    {
        if (!IsLoaded || !IsVisible || _window?.WindowState == WindowState.Minimized
            || string.IsNullOrWhiteSpace(FilePath))
        {
            ReleasePreview();
            return;
        }

        if (string.Equals(_loadedPath, FilePath, StringComparison.OrdinalIgnoreCase)
            && _loadedDecodeWidth == DecodePixelWidth)
        {
            return;
        }

        ReleasePreview();
        _loadedPath = FilePath;
        _loadedDecodeWidth = DecodePixelWidth;

        if (!string.Equals(Path.GetExtension(FilePath), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            Source = FileImageConverter.GetOrLoad(FilePath, DecodePixelWidth);
            return;
        }

        try
        {
            // 允许回收站移动正在预览的文件；切换、隐藏、关闭时关闭本次流。
            _gifStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous);
            AnimationBehavior.SetSourceStream(this, _gifStream);
        }
        catch (IOException)
        {
            ReleasePreview();
        }
        catch (UnauthorizedAccessException)
        {
            ReleasePreview();
        }
    }

    private void ReleasePreview()
    {
        // 先让播放器停止，再释放流；库会丢弃切换前尚未完成的异步加载。
        AnimationBehavior.SetSourceStream(this, null);
        _gifStream?.Dispose();
        _gifStream = null;
        _loadedPath = null;
        Source = null;
    }
}
