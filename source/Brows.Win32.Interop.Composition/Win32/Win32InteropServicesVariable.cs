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
    /// <value>
    /// The pool to use, or <see langword="null"/> to resolve a pool through the host's optional
    /// <see cref="ISTAThreadPoolProvider"/> import, falling back to its <see cref="ISTAThreadPoolFactory"/> import.
    /// </value>
    /// <remarks>
    /// Apply this configuration before the export creates its underlying services. A supplied pool takes precedence
    /// over both imports and remains caller-owned. Otherwise, the provider is called with the interop export instance
    /// as its target. A provider-returned pool remains externally owned. Neither is emptied by the export on shutdown;
    /// the caller, provider, or host manages its lifetime. If the provider is absent or returns null, the export creates
    /// a pool through the factory and empties that owned pool on shutdown. Configuration fails if no pool is available.
    /// </remarks>
    public ISTAThreadPool ThreadPool { get; set; }

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
