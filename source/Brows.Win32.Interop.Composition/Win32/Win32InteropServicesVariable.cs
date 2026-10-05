using Brows.Composition;
using Brows.Threading;
using System;

namespace Brows.Win32;

/// <summary>
/// Configures the STA worker pool used by the <see cref="IWin32InteropServices"/> composition export.
/// </summary>
public sealed class Win32InteropServicesVariable : IExportVariable {
    /// <summary>
    /// Gets or sets the STA pool used for Shell actions and batched file operations.
    /// </summary>
    /// <value>The pool to use, or <see langword="null"/> to create a pool owned by the export.</value>
    /// <remarks>
    /// Apply this configuration before the export creates its underlying services. A supplied pool remains
    /// caller-owned and is not emptied when the export shuts down.
    /// </remarks>
    public STAThreadPool ThreadPool { get; set; }

    /// <summary>
    /// Gets or sets the callback that supplies the owner window for Shell UI shown by file-operation batches.
    /// </summary>
    /// <value>
    /// The delegate to invoke for an owner window handle, or <see langword="null"/> to run batches without one.
    /// </value>
    /// <remarks>
    /// Apply this configuration before the export creates its underlying services. The delegate runs on an STA
    /// worker while a batch executes; it must return promptly and must not synchronously wait on a thread that
    /// could be waiting for the batch, or the batch can hang.
    /// </remarks>
    public Func<IntPtr> OnGetOwnerWindow { get; set; }
}
