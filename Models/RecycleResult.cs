namespace FileClean.Models;

public sealed class RecycleResult
{
    public List<string> Moved { get; } = [];

    public List<(string Path, string Message)> Failed { get; } = [];
}
