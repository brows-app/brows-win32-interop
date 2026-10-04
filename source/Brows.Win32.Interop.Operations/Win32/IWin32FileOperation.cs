using Brows.Operations;
using Brows.Threading;
using Brows.Win32.Win32FileOperations;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Brows.Win32;

/// <summary>
/// Queues a batch of Windows Shell file operations for execution on an STA worker.
/// </summary>
public interface IWin32FileOperation {
    /// <summary>
    /// Gets or sets whether the Shell should add an undo record for the batch.
    /// </summary>
    bool AddUndoRecord { get; set; }

    /// <summary>
    /// Gets or sets whether the Shell should allow the batch to be undone.
    /// </summary>
    bool AllowUndo { get; set; }

    /// <summary>
    /// Gets or sets the source files to copy into <see cref="Directory"/>.
    /// </summary>
    List<CopyFile> CopyFiles { get; set; }

    /// <summary>
    /// Gets or sets the items to create in <see cref="Directory"/>.
    /// </summary>
    List<CreateFile> CreateFiles { get; set; }

    /// <summary>
    /// Gets or sets the names of items to delete from <see cref="Directory"/>.
    /// </summary>
    List<DeleteFile> DeleteFiles { get; set; }

    /// <summary>
    /// Gets the destination for copy, move, and create operations and the containing directory for delete and rename operations.
    /// </summary>
    string Directory { get; }

    /// <summary>
    /// Gets or sets whether the Shell should fail early when an operation cannot be completed.
    /// </summary>
    bool EarlyFailure { get; set; }

    /// <summary>
    /// Gets or sets the source files to move into <see cref="Directory"/>.
    /// </summary>
    List<MoveFile> MoveFiles { get; set; }

    /// <summary>
    /// Gets or sets whether the Shell should suppress confirmation prompts.
    /// </summary>
    bool NoConfirmation { get; set; }

    /// <summary>
    /// Gets or sets whether the Shell should suppress error dialogs.
    /// </summary>
    bool NoErrorUI { get; set; }

    /// <summary>
    /// Gets or sets whether renaming after a collision should preserve file extensions.
    /// </summary>
    bool PreserveFileExtensions { get; set; }

    /// <summary>
    /// Gets or sets whether deleted items should be sent to the Recycle Bin when possible.
    /// </summary>
    bool RecycleOnDelete { get; set; }

    /// <summary>
    /// Gets or sets the items to rename in <see cref="Directory"/>.
    /// </summary>
    List<RenameFile> RenameFiles { get; set; }

    /// <summary>
    /// Gets or sets whether the Shell should generate a new name on a collision.
    /// </summary>
    bool RenameOnCollision { get; set; }

    /// <summary>
    /// Gets or sets whether Shell progress UI is suppressed in favor of the supplied progress receiver.
    /// </summary>
    bool Silent { get; set; }

    /// <summary>
    /// Gets the caller-owned STA pool used to execute this batch.
    /// </summary>
    STAThreadPool ThreadPool { get; }

    /// <summary>
    /// Executes the queued operations through the Windows Shell.
    /// </summary>
    /// <param name="progress">Receives Shell progress when <see cref="Silent"/> is enabled, or <see langword="null"/>.</param>
    /// <param name="token">Cancels queued work and requests cancellation during Shell progress callbacks.</param>
    /// <returns><see langword="true"/> when at least one operation was queued for native execution; otherwise, <see langword="false"/>.</returns>
    /// <remarks>The result does not indicate whether every individual operation succeeded.</remarks>
    Task<bool> Operate(IOperationProgress progress, CancellationToken token);
}
