namespace Brows.Win32.Win32FileOperations;

/// <summary>
/// Specifies a source file to copy into the batch destination directory.
/// </summary>
public sealed record CopyFile {
    /// <summary>
    /// Gets the path of the source file.
    /// </summary>
    public string Path { get; init; }
}
