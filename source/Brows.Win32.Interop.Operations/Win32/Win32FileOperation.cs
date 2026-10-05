using Brows.Operations;
using Brows.Threading;
using Brows.Win32.InteropServices;
using Brows.Win32.InteropServices.ComTypes;
using Brows.Win32.PlatformInvoke;
using Brows.Win32.Win32FileOperations;
using Domore.Logs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Brows.Win32;

/// <summary>
/// Queues a batch of Windows Shell file operations for execution on an STA worker.
/// </summary>
internal sealed class Win32FileOperation : IWin32FileOperation {
    private static readonly ILog Log = Logging.For(typeof(Win32FileOperation));

    private uint Flags;
    private IFileOperation FileOperation;
    private IOperationProgress OperationProgress;
    private CancellationToken CancellationToken;

    private ShellItemWrapper DirectoryWrap => _DirectoryWrap ??= new ShellItemWrapper(Directory);
    private ShellItemWrapper _DirectoryWrap;

    private bool Iterate<T>(IReadOnlyList<T> list, Action<T> act) {
        if (act is null) {
            throw new ArgumentNullException(nameof(act));
        }
        if (list is null) {
            return false;
        }
        if (list.Count == 0) {
            return false;
        }
        var acted = false;
        foreach (var item in list) {
            if (CancellationToken.IsCancellationRequested) {
                CancellationToken.ThrowIfCancellationRequested();
            }
            if (item is not null) {
                act(item);
                acted = true;
            }
        }
        return acted;
    }

    private bool Delete() {
        return Iterate(DeleteFiles, item => {
            var path = Path.Combine(Directory, item.Name);
            using (var itemWrap = new ShellItemWrapper(path)) {
                var hr = itemWrap.UseShellItem(item => {
                    return FileOperation.DeleteItem(item, null);
                });
                hr.ThrowOnError();
            }
        });
    }

    private bool Rename() {
        return Iterate(RenameFiles, item => {
            var oldPath = Path.Combine(Directory, item.OldName);
            var newName = item.NewName;
            using (var itemWrap = new ShellItemWrapper(oldPath)) {
                var hr = itemWrap.UseShellItem(item => {
                    return FileOperation.RenameItem(item, newName, null);
                });
                hr.ThrowOnError();
            }
        });
    }

    private bool Copy() {
        return Iterate(CopyFiles, item => {
            var path = item.Path;
            using (var itemWrap = new ShellItemWrapper(path)) {
                var hr = itemWrap.UseShellItem(pathItem => {
                    return DirectoryWrap.UseShellItem(directoryItem => {
                        return FileOperation.CopyItem(pathItem, directoryItem, null, null);
                    });
                });
                hr.ThrowOnError();
            }
        });
    }

    private bool Move() {
        return Iterate(MoveFiles, item => {
            using (var itemWrap = new ShellItemWrapper(item.Path)) {
                var hr = itemWrap.UseShellItem(pathItem => {
                    return DirectoryWrap.UseShellItem(directoryItem => {
                        return FileOperation.MoveItem(pathItem, directoryItem, null, null);
                    });
                });
                hr.ThrowOnError();
            }
        });
    }

    private bool Create() {
        return Iterate(CreateFiles, item => {
            var hr = DirectoryWrap.UseShellItem(directoryItem => {
                return FileOperation.NewItem(directoryItem, item.Attributes, item.Name, null, null);
            });
            hr.ThrowOnError();
        });
    }

    private IntPtr GetOwnerWindow() {
        var callback = OnGetOwnerWindow;
        if (callback is not null) {
            return callback();
        }
        return IntPtr.Zero;
    }

    private bool Work() {
        if (CancellationToken.IsCancellationRequested) {
            CancellationToken.ThrowIfCancellationRequested();
        }
        using (var fopw = new FileOperationWrapper()) {
            return fopw.UseFileOperation(fop => {
                FileOperation = fop;
                Flags = FlagsInit();
                var performOperations =
                    Copy() |
                    Move() |
                    Create() |
                    Delete() |
                    Rename();
                if (performOperations == false) {
                    return false;
                }
                var
                hr = FileOperation.SetOperationFlags(Flags);
                hr.ThrowOnError();

                var window = GetOwnerWindow();
                if (window != IntPtr.Zero) {
                    hr = FileOperation.SetOwnerWindow(window);
                    hr.ThrowOnError();
                }
                var progressSink = new Win32ProgressSink(
                    Silent ? OperationProgress : null,
                    CancellationToken);
                hr = FileOperation.Advise(progressSink, out var progressSinkCookie);
                hr.ThrowOnError();
                try {
                    CancellationToken.ThrowIfCancellationRequested();
                    var performHr = FileOperation.PerformOperations();
                    var abortedHr = FileOperation.GetAnyOperationsAborted(out var aborted);
                    performHr.ThrowOnError();
                    abortedHr.ThrowOnError();

                    if (aborted) {
                        if (Log.Warn()) {
                            Log.Warn($"Work aborted");
                        }
                    }
                    return true;
                }
                finally {
                    try {
                        hr = FileOperation.Unadvise(progressSinkCookie);
                        hr.ThrowOnError();
                    }
                    catch (Exception ex) {
                        if (Log.Warn()) {
                            Log.Warn(
                                $"Failure during {nameof(IFileOperation)}.{nameof(IFileOperation.Unadvise)}.", ex);
                        }
                    }
                }
            });
        }
    }

    internal Action OnOperationStarting { get; set; }

    internal Action OnOperationFinished { get; set; }

    internal uint FlagsInit() {
        var fof = FOF.NOCONFIRMMKDIR;
        if (NoConfirmation) fof |= FOF.NOCONFIRMATION;
        if (NoErrorUI) fof |= FOF.NOERRORUI;
        if (Silent) fof |= FOF.SILENT;
        if (AllowUndo) fof |= FOF.ALLOWUNDO;
        if (RenameOnCollision) fof |= FOF.RENAMEONCOLLISION;

        var fofx = FOFX.SHOWELEVATIONPROMPT;
        if (EarlyFailure) fofx |= FOFX.EARLYFAILURE;
        if (AddUndoRecord) fofx |= FOFX.ADDUNDORECORD;
        if (RecycleOnDelete) fofx |= FOFX.RECYCLEONDELETE;
        if (PreserveFileExtensions) fofx |= FOFX.PRESERVEFILEEXTENSIONS;

        return (uint)fof | (uint)fofx;
    }

    /// <summary>
    /// Gets or sets the source files to copy into <see cref="Directory"/>.
    /// </summary>
    public List<CopyFile> CopyFiles {
        get => _CopyFiles ??= [];
        set => _CopyFiles = value;
    }
    private List<CopyFile> _CopyFiles;

    /// <summary>
    /// Gets or sets the source files to move into <see cref="Directory"/>.
    /// </summary>
    public List<MoveFile> MoveFiles {
        get => _MoveFiles ??= [];
        set => _MoveFiles = value;
    }
    private List<MoveFile> _MoveFiles;

    /// <summary>
    /// Gets or sets the names of items to delete from <see cref="Directory"/>.
    /// </summary>
    public List<DeleteFile> DeleteFiles {
        get => _DeleteFiles ??= [];
        set => _DeleteFiles = value;
    }
    private List<DeleteFile> _DeleteFiles;

    /// <summary>
    /// Gets or sets the items to create in <see cref="Directory"/>.
    /// </summary>
    public List<CreateFile> CreateFiles {
        get => _CreateFiles ??= [];
        set => _CreateFiles = value;
    }
    private List<CreateFile> _CreateFiles;

    /// <summary>
    /// Gets or sets the items to rename in <see cref="Directory"/>.
    /// </summary>
    public List<RenameFile> RenameFiles {
        get => _RenameFiles ??= [];
        set => _RenameFiles = value;
    }
    private List<RenameFile> _RenameFiles;

    /// <summary>
    /// Gets or sets whether the Shell should add an undo record for the batch.
    /// </summary>
    public bool AddUndoRecord { get; set; }

    /// <summary>
    /// Gets or sets whether the Shell should allow the batch to be undone.
    /// </summary>
    public bool AllowUndo { get; set; }

    /// <summary>
    /// Gets or sets whether the Shell should fail early when an operation cannot be completed.
    /// </summary>
    public bool EarlyFailure { get; set; }

    /// <summary>
    /// Gets or sets whether the Shell should suppress confirmation prompts.
    /// </summary>
    public bool NoConfirmation { get; set; }

    /// <summary>
    /// Gets or sets whether the Shell should suppress error dialogs.
    /// </summary>
    public bool NoErrorUI { get; set; }

    /// <summary>
    /// Gets or sets whether renaming after a collision should preserve file extensions.
    /// </summary>
    public bool PreserveFileExtensions { get; set; }

    /// <summary>
    /// Gets or sets whether deleted items should be sent to the Recycle Bin when possible.
    /// </summary>
    public bool RecycleOnDelete { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the Shell should generate a new name on a collision.
    /// </summary>
    public bool RenameOnCollision { get; set; }

    /// <summary>
    /// Gets or sets whether Shell progress UI is suppressed in favor of the supplied progress receiver.
    /// </summary>
    public bool Silent { get; set; }

    /// <summary>
    /// Gets or sets the callback that supplies the owner window for Shell UI shown by the batch.
    /// </summary>
    /// <remarks>
    /// The callback runs on the STA worker during <see cref="Operate"/> and must return promptly. It must not
    /// synchronously wait on a thread that could be waiting for the batch, or the batch can hang. Return
    /// <see cref="IntPtr.Zero"/> to run without an owner window, which is the default.
    /// </remarks>
    public Func<IntPtr> OnGetOwnerWindow { get; set; }

    /// <summary>
    /// Gets the destination for copy, move, and create operations and the containing directory for delete and rename operations.
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// Gets the caller-owned STA pool used to execute this batch.
    /// </summary>
    public STAThreadPool ThreadPool { get; }

    /// <summary>
    /// Creates a file-operation batch for a directory and STA pool.
    /// </summary>
    /// <param name="directory">The destination or containing directory for queued items.</param>
    /// <param name="threadPool">The STA pool that will run the batch.</param>
    public Win32FileOperation(string directory, STAThreadPool threadPool) {
        Directory = directory;
        ThreadPool = threadPool ?? throw new ArgumentNullException(nameof(threadPool));
    }

    /// <summary>
    /// Executes the queued operations through the Windows Shell.
    /// </summary>
    /// <param name="progress">Receives Shell progress when <see cref="Silent"/> is enabled, or <see langword="null"/>.</param>
    /// <param name="token">Cancels queued work and requests cancellation during Shell progress callbacks.</param>
    /// <returns><see langword="true"/> when at least one operation was queued for native execution; otherwise, <see langword="false"/>.</returns>
    /// <remarks>The result does not indicate whether every individual operation succeeded.</remarks>
    public async Task<bool> Operate(IOperationProgress progress, CancellationToken token) {
        var agent = new Win32FileOperation(Directory, ThreadPool) {
            AddUndoRecord = AddUndoRecord,
            AllowUndo = AllowUndo,
            CancellationToken = token,
            CopyFiles = _CopyFiles?.ToList(),
            CreateFiles = _CreateFiles?.ToList(),
            DeleteFiles = _DeleteFiles?.ToList(),
            EarlyFailure = EarlyFailure,
            OnGetOwnerWindow = OnGetOwnerWindow,
            OnOperationFinished = OnOperationFinished,
            OnOperationStarting = OnOperationStarting,
            MoveFiles = _MoveFiles?.ToList(),
            NoConfirmation = NoConfirmation,
            NoErrorUI = NoErrorUI,
            OperationProgress = progress,
            PreserveFileExtensions = PreserveFileExtensions,
            RecycleOnDelete = RecycleOnDelete,
            RenameFiles = _RenameFiles?.ToList(),
            RenameOnCollision = RenameOnCollision,
            Silent = Silent
        };
        var onOperationStarting = agent.OnOperationStarting;
        if (onOperationStarting is not null) {
            onOperationStarting();
        }
        var work = Task.Run(cancellationToken: token, function: () => ThreadPool.Work(
                name: nameof(Win32FileOperation),
                work: agent.Work,
                cancellationToken: token));
        try {
            return await work.ConfigureAwait(false);
        }
        finally {
            using var _ = agent._DirectoryWrap;
            var onOperationFinished = agent.OnOperationFinished;
            if (onOperationFinished is not null) {
                onOperationFinished();
            }
        }
    }
}
