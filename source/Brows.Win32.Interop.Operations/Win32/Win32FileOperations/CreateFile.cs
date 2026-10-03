using System.IO;

namespace Brows.Win32.Win32FileOperations;

/// <summary>
/// Specifies an item to create in the batch directory.
/// </summary>
public sealed record CreateFile {
    /// <summary>
    /// Gets the name of the item to create.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Gets the attributes to assign to the new item.
    /// </summary>
    public FileAttributes Attributes { get; init; }
}
