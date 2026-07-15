using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FileClean.Models;
using WinForms = System.Windows.Forms;

namespace FileClean.Services;

public sealed class SystemFileService
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    public string? PickFolder(string description = "选择目录")
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? PickJsonFile()
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = "选择要导入的 JSON 配置文件",
            Filter = "JSON 配置文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.FileName : null;
    }

    public RecycleResult MoveToRecycleBin(IEnumerable<string> filePaths)
    {
        var result = new RecycleResult();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingPaths = new List<string>();

        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            try
            {
                var fullPath = Path.GetFullPath(filePath);
                if (!seen.Add(fullPath))
                {
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    result.Failed.Add((fullPath, "文件不存在。"));
                    continue;
                }

                existingPaths.Add(fullPath);
            }
            catch (Exception error)
            {
                result.Failed.Add((filePath, error.Message));
            }
        }

        if (existingPaths.Count == 0)
        {
            return result;
        }

        var operation = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = BuildShellPathList(existingPaths),
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
        };

        var operationCode = SHFileOperation(ref operation);
        foreach (var filePath in existingPaths)
        {
            if (!File.Exists(filePath))
            {
                result.Moved.Add(filePath);
                continue;
            }

            var message = operation.fAnyOperationsAborted
                ? "批量移动到回收站时被系统取消。"
                : $"批量移动到回收站失败，Shell 返回码：{operationCode}。";
            result.Failed.Add((filePath, message));
        }

        return result;
    }

    private static string BuildShellPathList(IEnumerable<string> filePaths)
    {
        return string.Join("\0", filePaths) + "\0\0";
    }

    public void ShowInFolder(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (File.Exists(filePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true
            });
            return;
        }

        var directory = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOperation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;

        public uint wFunc;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;

        public ushort fFlags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;

        public IntPtr hNameMappings;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }
}
