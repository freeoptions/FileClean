namespace FileClean.Models;

public sealed class SupportedExtensions
{
    public List<string> Images { get; init; } = [];

    public List<string> Videos { get; init; } = [];

    public List<string> Custom { get; init; } = [];
}
