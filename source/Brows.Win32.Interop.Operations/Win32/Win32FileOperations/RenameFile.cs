namespace Brows.Win32.Win32FileOperations;

/// <summary>
/// Specifies an item to rename within the batch directory.
/// </summary>
public sealed record RenameFile {
    /// <summary>
    /// Gets the current name of the item.
    /// </summary>
    public string OldName { get; init; }

    /// <summary>
    /// Gets the new name for the item.
    /// </summary>
    public string NewName { get; init; }
}
