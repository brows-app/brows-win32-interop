namespace Brows.Win32.Win32FileOperations;

/// <summary>
/// Specifies a source file to move into the batch destination directory.
/// </summary>
public sealed record MoveFile {
    /// <summary>
    /// Gets the path of the source file.
    /// </summary>
    public string Path { get; init; }
}
