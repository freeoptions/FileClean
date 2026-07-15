using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileClean.Converters;
using FileClean.Models;
using FileClean.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WinForms = System.Windows.Forms;

namespace FileClean;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int FocusPreviewDecodeWidth = 520;
    private const int AdjacentPreviewPreloadRadius = 4;

    private static readonly HashSet<string> BuiltInExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".heif", ".avif", ".svg",
        ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".flv", ".webm", ".m4v", ".mpeg", ".mpg", ".ts", ".mts", ".m2ts", ".3gp", ".3g2", ".ogv"
    };

    private readonly ConfigService _configService = new();
    private readonly DuplicateScanner _scanner = new();
    private readonly SystemFileService _systemFileService = new();
    private readonly IProgress<ScanProgress> _scanProgress;
    private AppConfig _config = new();
    private ObservableCollection<DuplicateGroup> _duplicateGroups = [];
    private ScanSummary _summary = new();
    private DuplicateItem? _selectedDuplicateItem;
    private DuplicateItem? _previewItem;
    private string _statusMessage = "先固定常用目录，再勾选本次参与扫描的目录，然后开始扫描。";
    private string _progressDetailText = "等待扫描。";
    private string _extensionInput = string.Empty;
    private string _toastText = string.Empty;
    private Visibility _toastVisibility = Visibility.Collapsed;
    private bool _isScanning;
    private bool _isProgressIndeterminate;
    private double _progressPercent;
    private bool _configLoaded;
    private int _toastVersion;
    private CancellationTokenSource? _scanCancellation;
    private WinForms.NotifyIcon? _notifyIcon;
    private bool _isExitRequested;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _scanProgress = new Progress<ScanProgress>(HandleScanProgress);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal bool IsSmokeTestMode { get; init; }

    public ObservableCollection<FolderEntry> Folders { get; } = [];

    public ObservableCollection<string> ExcludedFolders { get; } = [];

    public ObservableCollection<string> CustomExtensions { get; } = [];

    public ObservableCollection<DuplicateGroup> DuplicateGroups
    {
        get => _duplicateGroups;
        private set
        {
            if (SetProperty(ref _duplicateGroups, value))
            {
                OnPropertyChanged(nameof(HasGroupsVisibility));
                OnPropertyChanged(nameof(NoGroupsVisibility));
                OnPropertyChanged(nameof(HasSelectedPaths));
                OnPropertyChanged(nameof(CurrentPreviewPositionText));
            }
        }
    }

    public ScanSummary Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public DuplicateItem? SelectedDuplicateItem
    {
        get => _selectedDuplicateItem;
        private set
        {
            if (ReferenceEquals(_selectedDuplicateItem, value))
            {
                return;
            }

            if (_selectedDuplicateItem is not null)
            {
                _selectedDuplicateItem.IsPreviewFocus = false;
            }

            _selectedDuplicateItem = value;
            if (_selectedDuplicateItem is not null)
            {
                _selectedDuplicateItem.IsPreviewFocus = true;
            }

            OnPropertyChanged(nameof(SelectedDuplicateItem));
            OnPropertyChanged(nameof(HasSelectedItemVisibility));
            OnPropertyChanged(nameof(NoSelectedItemVisibility));
            OnPropertyChanged(nameof(FocusImageVisibility));
            OnPropertyChanged(nameof(FocusVideoVisibility));
            OnPropertyChanged(nameof(FocusFileVisibility));
            OnPropertyChanged(nameof(CurrentPreviewPositionText));
            PreloadAdjacentPreviewImages();
            ScrollFocusedResultIntoView();

            if (PreviewItem is not null)
            {
                PreviewItem = _selectedDuplicateItem;
            }
        }
    }

    public DuplicateItem? PreviewItem
    {
        get => _previewItem;
        private set
        {
            if (SetProperty(ref _previewItem, value))
            {
                OnPropertyChanged(nameof(PreviewOverlayVisibility));
                OnPropertyChanged(nameof(PreviewOverlayImageVisibility));
                OnPropertyChanged(nameof(PreviewOverlayVideoVisibility));
                OnPropertyChanged(nameof(PreviewOverlayFileVisibility));
                OnPropertyChanged(nameof(PreviewItemUri));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ProgressDetailText
    {
        get => _progressDetailText;
        private set => SetProperty(ref _progressDetailText, value);
    }

    public string ExtensionInput
    {
        get => _extensionInput;
        set => SetProperty(ref _extensionInput, value);
    }

    public string ExportFolderText => string.IsNullOrWhiteSpace(_config.ExportFolder)
        ? "尚未设置导出目录"
        : _config.ExportFolder;

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(CanStartScan));
                OnPropertyChanged(nameof(HasSelectedPaths));
            }
        }
    }

    public bool CanStartScan => !IsScanning;

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public bool HasSelectedPaths => DuplicateGroups.SelectMany(group => group.Items).Any(item => item.IsSelected) && !IsScanning;

    public Visibility NoFolderVisibility => Folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoExcludedFolderVisibility => ExcludedFolders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HasGroupsVisibility => DuplicateGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoGroupsVisibility => DuplicateGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HasSelectedItemVisibility => SelectedDuplicateItem is not null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoSelectedItemVisibility => SelectedDuplicateItem is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FocusImageVisibility => SelectedDuplicateItem?.PreviewKind == "image" ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FocusVideoVisibility => SelectedDuplicateItem?.PreviewKind == "video" ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FocusFileVisibility => SelectedDuplicateItem is not null
        && SelectedDuplicateItem.PreviewKind is not "image" and not "video"
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility PreviewOverlayVisibility => PreviewItem is not null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PreviewOverlayImageVisibility => PreviewItem?.PreviewKind == "image" ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PreviewOverlayVideoVisibility => PreviewItem?.PreviewKind == "video" ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PreviewOverlayFileVisibility => PreviewItem is not null
        && PreviewItem.PreviewKind is not "image" and not "video"
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Uri? PreviewItemUri => PreviewItem is null ? null : new Uri(PreviewItem.Path, UriKind.Absolute);

    public string CurrentPreviewPositionText
    {
        get
        {
            if (SelectedDuplicateItem is null)
            {
                return "未选择文件";
            }

            var items = GetPreviewBrowseItems();
            var index = items.FindIndex(item => string.Equals(item.Path, SelectedDuplicateItem.Path, StringComparison.OrdinalIgnoreCase));
            return index >= 0
                ? $"当前预览 {index + 1}/{items.Count}：{SelectedDuplicateItem.Name}"
                : $"当前预览：{SelectedDuplicateItem.Name}";
        }
    }

    public string ToastText
    {
        get => _toastText;
        private set => SetProperty(ref _toastText, value);
    }

    public Visibility ToastVisibility
    {
        get => _toastVisibility;
        private set => SetProperty(ref _toastVisibility, value);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupTray();
        ApplyConfig(await _configService.LoadAsync());
        _configLoaded = true;

        if (IsSmokeTestMode)
        {
            await Dispatcher.InvokeAsync(ExitApplication);
        }
    }

    private void SetupTray()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "build", "icon.ico");
        Icon icon;
        try
        {
            icon = File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
        }
        catch
        {
            icon = (Icon)SystemIcons.Application.Clone();
        }

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "FileClean - 重复文件清理",
            Visible = true,
            ContextMenuStrip = new WinForms.ContextMenuStrip()
        };

        _notifyIcon.ContextMenuStrip.Items.Add("显示软件", null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
        _notifyIcon.ContextMenuStrip.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
    }

    private void ShowMainWindow()
    {
        ShowInTaskbar = true;
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Maximized;
        }

        Activate();
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        DisposeTrayIcon();
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExitRequested)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
        ShowToast("FileClean 已最小化到托盘。");
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeTrayIcon();
        _scanCancellation?.Dispose();
        base.OnClosed(e);
    }

    private void DisposeTrayIcon()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var notifyIcon = _notifyIcon;
        _notifyIcon = null;
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip?.Dispose();
        notifyIcon.Icon?.Dispose();
        notifyIcon.Dispose();
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _systemFileService.PickFolder("选择要固定扫描的目录");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (Folders.Any(item => string.Equals(item.Path, folder, StringComparison.OrdinalIgnoreCase)))
        {
            ShowToast("这个目录已经在固定列表里了。");
            return;
        }

        Folders.Add(new FolderEntry { Path = folder, Enabled = true });
        await SaveConfigFromUiAsync();
        ShowToast("目录已添加，并默认参与本次扫描。");
    }

    private async void FolderEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_configLoaded)
        {
            await SaveConfigFromUiAsync();
        }
    }

    private async void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: string folderPath })
        {
            return;
        }

        var item = Folders.FirstOrDefault(folder => string.Equals(folder.Path, folderPath, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }

        Folders.Remove(item);
        await SaveConfigFromUiAsync();
        ShowToast("已从固定目录列表中移除。");
    }

    private async void AddExcludedFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _systemFileService.PickFolder("选择要排除的目录");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (ExcludedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            ShowToast("这个目录已经在排除列表里了。");
            return;
        }

        ExcludedFolders.Add(folder);
        SortExcludedFolders();
        await SaveConfigFromUiAsync();
        OnPropertyChanged(nameof(NoExcludedFolderVisibility));
        ShowToast("排除目录已添加，扫描时会整目录跳过。");
    }

    private async void RemoveExcludedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: string folderPath })
        {
            return;
        }

        var item = ExcludedFolders.FirstOrDefault(folder => string.Equals(folder, folderPath, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }

        ExcludedFolders.Remove(item);
        await SaveConfigFromUiAsync();
        OnPropertyChanged(nameof(NoExcludedFolderVisibility));
        ShowToast("已移除排除目录。");
    }

    private async void AddExtension_Click(object sender, RoutedEventArgs e)
    {
        var normalized = ConfigService.NormalizeExtension(ExtensionInput);
        if (normalized is null)
        {
            ShowToast("后缀格式不正确，示例：psd 或 .psd。");
            return;
        }

        if (BuiltInExtensions.Contains(normalized))
        {
            ShowToast("这个后缀已经是内置支持格式。");
            return;
        }

        if (CustomExtensions.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            ShowToast("这个后缀已经存在，不能重复添加。");
            return;
        }

        CustomExtensions.Add(normalized);
        SortCustomExtensions();
        ExtensionInput = string.Empty;
        await SaveConfigFromUiAsync();
        ShowToast("自定义后缀已添加。");
    }

    private async void RemoveExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: string extension })
        {
            return;
        }

        CustomExtensions.Remove(extension);
        await SaveConfigFromUiAsync();
        ShowToast("自定义后缀已删除。");
    }

    private async void PickExportFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _systemFileService.PickFolder("选择配置导出目录");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        _config.ExportFolder = folder;
        await SaveConfigFromUiAsync();
        OnPropertyChanged(nameof(ExportFolderText));
        ShowToast("导出目录已更新。");
    }

    private async void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveConfigFromUiAsync();
            await _configService.ExportAsync(_config);
            ShowToast("配置已导出到指定位置");
        }
        catch (Exception error)
        {
            ShowToast(error.Message);
        }
    }

    private async void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var filePath = _systemFileService.PickJsonFile();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            var imported = await _configService.ImportAsync(_config, filePath);
            ApplyConfig(imported);
            ShowToast("配置已成功导入并合并。");
        }
        catch (Exception error)
        {
            ShowToast(error.Message);
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (Folders.All(folder => !folder.Enabled))
        {
            ShowToast("请至少勾选一个要参与扫描的目录。");
            return;
        }

        await SaveConfigFromUiAsync();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var startedAt = DateTimeOffset.Now;

        DuplicateGroups = [];
        Summary = new ScanSummary();
        SelectedDuplicateItem = null;
        ClearAllSelections();
        IsScanning = true;
        IsProgressIndeterminate = true;
        ProgressPercent = 0;
        StatusMessage = "正在扫描目录：先按大小筛选，再做快速指纹，最后对高疑似候选计算完整 MD5。";
        ProgressDetailText = "正在准备扫描任务。";

        try
        {
            var result = await _scanner.ScanAsync(_config, _scanProgress, _scanCancellation.Token);
            DuplicateGroups = result.Groups;
            Summary = result.Summary;
            SelectedDuplicateItem = DuplicateGroups.FirstOrDefault()?.Items.FirstOrDefault();
            ProgressPercent = 100;
            IsProgressIndeterminate = false;

            var duration = FormatDuration(DateTimeOffset.Now - startedAt);
            StatusMessage = result.Groups.Count > 0
                ? $"扫描完成，找到 {result.Summary.DuplicateGroups} 组重复文件，可释放 {result.Summary.ReclaimableText}。本次耗时 {duration}。"
                : $"扫描完成，当前未发现重复文件。本次耗时 {duration}。";
            ProgressDetailText = $"目录 {result.Summary.DirectoriesVisited} 个，文件 {result.Summary.FilesVisited} 个，候选 {result.Summary.CandidateFiles} 个，跳过排除目录 {result.Summary.ExcludedDirectories} 个。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "扫描已取消。";
            ProgressDetailText = "扫描任务已取消。";
            ShowToast("扫描已取消。");
        }
        catch (Exception error)
        {
            StatusMessage = error.Message;
            ProgressDetailText = "扫描过程中出现错误。";
            ShowToast(error.Message);
        }
        finally
        {
            IsScanning = false;
            IsProgressIndeterminate = false;
        }
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e)
    {
        _scanCancellation?.Cancel();
    }

    private void SelectDuplicates_Click(object sender, RoutedEventArgs e)
    {
        var selectedCount = 0;
        foreach (var group in DuplicateGroups)
        {
            foreach (var item in group.Items)
            {
                item.IsSelected = !item.IsKeepCandidate;
                if (item.IsSelected)
                {
                    selectedCount++;
                }
            }
        }

        RefreshSelectionState();
        StatusMessage = selectedCount > 0
            ? $"已按“全路径最短者保留”规则勾选 {selectedCount} 个重复文件。"
            : "当前没有可勾选的重复文件。";
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        ClearAllSelections();
        StatusMessage = "已取消当前所有勾选。";
    }

    private void DuplicateCheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfCheckBox { DataContext: DuplicateItem item })
        {
            return;
        }

        if (item.IsSelected)
        {
            var group = DuplicateGroups.FirstOrDefault(candidate => candidate.Id == item.GroupId);
            if (group is not null && group.Items.All(candidate => candidate.IsSelected))
            {
                item.IsSelected = false;
                ShowToast("同一组内至少要保留 1 个文件，不能全部勾选。");
            }
        }

        RefreshSelectionState();
    }

    private void DuplicateGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: DuplicateItem item } grid)
        {
            SelectedDuplicateItem = item;
            ClearResultGridSelections(grid);
        }
    }

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string filePath })
        {
            _systemFileService.ShowInFolder(filePath);
        }
    }

    private void ForwardResultsMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ResultsScroll.ScrollToVerticalOffset(ResultsScroll.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void BrowsePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var step = e.Delta < 0 ? 1 : -1;
        if (SelectAdjacentPreviewItem(step))
        {
            e.Handled = true;
        }
    }

    private bool SelectAdjacentPreviewItem(int step)
    {
        var previewableItems = GetPreviewBrowseItems();

        if (previewableItems.Count == 0)
        {
            return false;
        }

        var currentIndex = SelectedDuplicateItem is null
            ? -1
            : previewableItems.FindIndex(item => string.Equals(item.Path, SelectedDuplicateItem.Path, StringComparison.OrdinalIgnoreCase));
        var nextIndex = currentIndex < 0
            ? (step > 0 ? 0 : previewableItems.Count - 1)
            : (currentIndex + step + previewableItems.Count) % previewableItems.Count;

        SelectedDuplicateItem = previewableItems[nextIndex];
        if (PreviewItem is not null)
        {
            PreviewItem = SelectedDuplicateItem;
        }

        return true;
    }

    private List<DuplicateItem> GetPreviewBrowseItems()
    {
        var previewableItems = DuplicateGroups
            .SelectMany(group => group.Items)
            .Where(item => item.PreviewKind is "image" or "video")
            .ToList();

        return previewableItems.Count > 0
            ? previewableItems
            : DuplicateGroups.SelectMany(group => group.Items).ToList();
    }

    private void PreloadAdjacentPreviewImages()
    {
        if (SelectedDuplicateItem is null)
        {
            return;
        }

        var items = GetPreviewBrowseItems();
        var currentIndex = items.FindIndex(item => string.Equals(item.Path, SelectedDuplicateItem.Path, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0 || items.Count < 2)
        {
            return;
        }

        var nextItems = Enumerable.Range(1, Math.Min(AdjacentPreviewPreloadRadius, items.Count - 1))
            .SelectMany(distance => new[]
            {
                items[(currentIndex - distance + items.Count) % items.Count],
                items[(currentIndex + distance) % items.Count]
            })
            .DistinctBy(item => item.Path)
            .ToList();

        foreach (var item in nextItems.Where(item => item.PreviewKind == "image"))
        {
            _ = Task.Run(() => FileImageConverter.Preload(item.Path, FocusPreviewDecodeWidth));
        }
    }

    private void ScrollFocusedResultIntoView()
    {
        if (SelectedDuplicateItem is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (SelectedDuplicateItem is null)
            {
                return;
            }

            var grid = FindDataGridForItem(ResultsScroll, SelectedDuplicateItem);
            if (grid is null)
            {
                return;
            }

            grid.ScrollIntoView(SelectedDuplicateItem);
            grid.UpdateLayout();

            if (grid.ItemContainerGenerator.ContainerFromItem(SelectedDuplicateItem) is FrameworkElement row)
            {
                row.BringIntoView();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static DataGrid? FindDataGridForItem(DependencyObject root, DuplicateItem item)
    {
        if (root is DataGrid grid && grid.Items.Contains(item))
        {
            return grid;
        }

        var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            var match = FindDataGridForItem(child, item);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private void ClearResultGridSelections(DataGrid? exceptGrid = null)
    {
        foreach (var grid in FindVisualChildren<DataGrid>(ResultsScroll))
        {
            if (!ReferenceEquals(grid, exceptGrid))
            {
                grid.UnselectAll();
                grid.SelectedItem = null;
                grid.CurrentCell = new DataGridCellInfo();
            }
        }

        if (exceptGrid is not null)
        {
            exceptGrid.UnselectAll();
            exceptGrid.SelectedItem = null;
            exceptGrid.CurrentCell = new DataGridCellInfo();
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void OpenPreview_Click(object sender, MouseButtonEventArgs e)
    {
        if (SelectedDuplicateItem is null)
        {
            return;
        }

        PreviewItem = ReferenceEquals(PreviewItem, SelectedDuplicateItem) ? null : SelectedDuplicateItem;
        e.Handled = true;
    }

    private void ClosePreviewOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        ClosePreviewOverlay();
        e.Handled = true;
    }

    private void ClosePreviewOverlay()
    {
        PreviewItem = null;
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && PreviewItem is not null)
        {
            ClosePreviewOverlay();
            e.Handled = true;
        }
    }

    private void ShowSelectedInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDuplicateItem is not null)
        {
            _systemFileService.ShowInFolder(SelectedDuplicateItem.Path);
        }
    }

    private void MoveToTrash_Click(object sender, RoutedEventArgs e)
    {
        var selectedPaths = DuplicateGroups
            .SelectMany(group => group.Items)
            .Where(item => item.IsSelected)
            .Select(item => item.Path)
            .ToList();

        if (selectedPaths.Count == 0)
        {
            return;
        }

        var result = _systemFileService.MoveToRecycleBin(selectedPaths);
        RebuildGroupsAfterMove(result.Moved);
        RefreshSelectionState();

        StatusMessage = result.Failed.Count > 0
            ? $"已移动到回收站 {result.Moved.Count} 个文件，另有 {result.Failed.Count} 个失败。"
            : $"已移动到回收站 {result.Moved.Count} 个文件。";
        ShowToast(StatusMessage);
    }

    private async Task SaveConfigFromUiAsync()
    {
        _config = await _configService.SaveAsync(new AppConfig
        {
            Folders = Folders.Select(folder => new FolderEntry
            {
                Path = folder.Path,
                Enabled = folder.Enabled
            }).ToList(),
            ExcludedFolders = ExcludedFolders.ToList(),
            CustomExtensions = CustomExtensions.ToList(),
            ExportFolder = _config.ExportFolder
        });

        OnPropertyChanged(nameof(ExportFolderText));
        OnPropertyChanged(nameof(NoFolderVisibility));
        OnPropertyChanged(nameof(NoExcludedFolderVisibility));
    }

    private void ApplyConfig(AppConfig config)
    {
        _config = ConfigService.NormalizeConfig(config);
        Folders.Clear();
        foreach (var folder in _config.Folders)
        {
            Folders.Add(new FolderEntry
            {
                Path = folder.Path,
                Enabled = folder.Enabled
            });
        }

        CustomExtensions.Clear();
        ExcludedFolders.Clear();
        foreach (var folderPath in _config.ExcludedFolders)
        {
            ExcludedFolders.Add(folderPath);
        }

        foreach (var extension in _config.CustomExtensions)
        {
            CustomExtensions.Add(extension);
        }

        OnPropertyChanged(nameof(ExportFolderText));
        OnPropertyChanged(nameof(NoFolderVisibility));
        OnPropertyChanged(nameof(NoExcludedFolderVisibility));
    }

    private void SortExcludedFolders()
    {
        var sorted = ExcludedFolders.OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase).ToList();
        ExcludedFolders.Clear();
        foreach (var folder in sorted)
        {
            ExcludedFolders.Add(folder);
        }
    }

    private void SortCustomExtensions()
    {
        var sorted = CustomExtensions.OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase).ToList();
        CustomExtensions.Clear();
        foreach (var extension in sorted)
        {
            CustomExtensions.Add(extension);
        }
    }

    private void HandleScanProgress(ScanProgress progress)
    {
        StatusMessage = progress.Message;
        ProgressDetailText = CreateProgressDetailText(progress);

        if (progress.Stage == "collecting")
        {
            IsProgressIndeterminate = true;
            ProgressPercent = 0;
            return;
        }

        IsProgressIndeterminate = false;

        if (progress.Stage == "fingerprinting")
        {
            ProgressPercent = progress.TotalQuickFingerprintTargets <= 0
                ? 35
                : Math.Clamp((double)progress.QuickFingerprintedFiles / progress.TotalQuickFingerprintTargets * 72, 12, 72);
            return;
        }

        if (progress.Stage == "hashing")
        {
            var completed = progress.HashedFiles + progress.CacheHits;
            ProgressPercent = progress.TotalHashTargets <= 0
                ? 86
                : Math.Clamp(76 + (double)completed / progress.TotalHashTargets * 22, 76, 98);
            return;
        }

        if (progress.Stage == "done")
        {
            ProgressPercent = 100;
        }
    }

    private static string CreateProgressDetailText(ScanProgress progress)
    {
        var currentPath = string.IsNullOrWhiteSpace(progress.CurrentPath)
            ? string.Empty
            : $" 当前：{progress.CurrentPath}";

        return progress.Stage switch
        {
            "collecting" => $"收集中：目录 {progress.DirectoriesVisited} 个，文件 {progress.FilesVisited} 个，候选 {progress.CandidateFiles} 个，跳过排除目录 {progress.ExcludedDirectories} 个。{currentPath}",
            "fingerprinting" => $"快速指纹：{progress.QuickFingerprintedFiles}/{progress.TotalQuickFingerprintTargets} 个，已收集候选 {progress.CandidateFiles} 个。",
            "hashing" => $"完整 MD5：{progress.HashedFiles + progress.CacheHits}/{progress.TotalHashTargets} 个，缓存命中 {progress.CacheHits} 个。",
            "done" => $"完成：目录 {progress.DirectoriesVisited} 个，文件 {progress.FilesVisited} 个，候选 {progress.CandidateFiles} 个，跳过排除目录 {progress.ExcludedDirectories} 个。",
            _ => progress.Message
        };
    }

    private void ClearAllSelections()
    {
        foreach (var item in DuplicateGroups.SelectMany(group => group.Items))
        {
            item.IsSelected = false;
        }

        RefreshSelectionState();
    }

    private void RefreshSelectionState()
    {
        OnPropertyChanged(nameof(HasSelectedPaths));
    }

    private void RebuildGroupsAfterMove(IReadOnlyCollection<string> movedPaths)
    {
        if (movedPaths.Count == 0)
        {
            return;
        }

        var movedSet = new HashSet<string>(movedPaths, StringComparer.OrdinalIgnoreCase);
        var nextGroups = new ObservableCollection<DuplicateGroup>();

        foreach (var group in DuplicateGroups)
        {
            var nextItems = group.Items.Where(item => !movedSet.Contains(item.Path)).ToList();
            if (nextItems.Count < 2)
            {
                continue;
            }

            var keepCandidate = nextItems
                .OrderBy(item => item.Path.Length)
                .ThenBy(item => item.Path, StringComparer.CurrentCultureIgnoreCase)
                .First();
            var nextGroup = new DuplicateGroup
            {
                Id = group.Id,
                Md5 = group.Md5,
                TotalSize = nextItems.Sum(item => item.Size),
                KeepRecommendation = keepCandidate.Path
            };

            foreach (var item in nextItems)
            {
                item.IsSelected = false;
                item.IsKeepCandidate = string.Equals(item.Path, keepCandidate.Path, StringComparison.OrdinalIgnoreCase);
                nextGroup.Items.Add(item);
            }

            nextGroups.Add(nextGroup);
        }

        DuplicateGroups = nextGroups;
        Summary = new ScanSummary
        {
            ScannedFolders = Summary.ScannedFolders,
            DirectoriesVisited = Summary.DirectoriesVisited,
            FilesVisited = Summary.FilesVisited,
            ExcludedDirectories = Summary.ExcludedDirectories,
            CandidateFiles = Summary.CandidateFiles,
            QuickFingerprintFiles = Summary.QuickFingerprintFiles,
            HashedFiles = Summary.HashedFiles,
            CacheHits = Summary.CacheHits,
            DuplicateGroups = nextGroups.Count,
            DuplicateFiles = nextGroups.Sum(group => group.Items.Count),
            ReclaimableBytes = nextGroups.Sum(group => group.ReclaimableBytes),
            Warnings = Summary.Warnings
        };
        SelectedDuplicateItem = nextGroups.FirstOrDefault()?.Items.FirstOrDefault();
    }

    private async void ShowToast(string text)
    {
        ToastText = text;
        ToastVisibility = Visibility.Visible;
        var currentVersion = ++_toastVersion;
        await Task.Delay(2800);
        if (currentVersion == _toastVersion)
        {
            ToastVisibility = Visibility.Collapsed;
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 1)
        {
            return "不足 1 秒";
        }

        var totalSeconds = (int)Math.Round(duration.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        var parts = new List<string>();

        if (hours > 0)
        {
            parts.Add($"{hours} 小时");
        }

        if (minutes > 0)
        {
            parts.Add($"{minutes} 分钟");
        }

        if (seconds > 0 || parts.Count == 0)
        {
            parts.Add($"{seconds} 秒");
        }

        return string.Join(" ", parts);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
