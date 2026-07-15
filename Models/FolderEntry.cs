namespace FileClean.Models;

public sealed class FolderEntry : NotifyObject
{
    private string _path = string.Empty;
    private bool _enabled = true;

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }
}
