using Brows.Composition;
using Brows.Threading;

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
}
