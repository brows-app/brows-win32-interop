namespace Brows.Win32.Win32FileOperations;

/// <summary>
/// Specifies an item to delete from the batch directory.
/// </summary>
public sealed record DeleteFile {
    /// <summary>
    /// Gets the name of the item to delete.
    /// </summary>
    public string Name { get; init; }
}
