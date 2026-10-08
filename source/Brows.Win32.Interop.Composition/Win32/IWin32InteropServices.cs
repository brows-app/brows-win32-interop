using Brows.Composition;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace Brows.Win32;

/// <summary>
/// Provides asynchronous access to Win32 file queries, console management, and Windows Shell actions through
/// Brows.Composition.
/// </summary>
public interface IWin32InteropServices : IExport {
    /// <summary>
    /// Creates a batch of Windows Shell file operations for a directory.
    /// </summary>
    /// <param name="directory">
    /// The destination for copy, move, and create operations, and the containing directory for delete and rename
    /// operations.
    /// </param>
    /// <returns>A batch that can be populated and executed with <see cref="IWin32FileOperation.Operate"/>.</returns>
    IWin32FileOperation FileOperation(string directory);

    /// <summary>
    /// Determines whether two existing paths identify the same file or directory.
    /// </summary>
    /// <param name="path1">The first existing file or directory path to compare.</param>
    /// <param name="path2">The second existing file or directory path to compare.</param>
    /// <param name="cancellationToken">
    /// Cancels the query before it starts. A native query that is already running cannot be interrupted.
    /// </param>
    /// <returns>
    /// A task whose result is <see langword="true"/> when both paths have the same volume and file identity;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="Win32Exception">A path cannot be opened or its file identity cannot be queried.</exception>
    /// <exception cref="OperationCanceledException">The query is canceled before it starts.</exception>
    /// <exception cref="InvalidOperationException">The composition export has been shut down.</exception>
    Task<bool> PathsAreEquivalent(string path1, string path2, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries whether a directory is configured for case-sensitive name lookup.
    /// </summary>
    /// <param name="path">The path of the directory to query.</param>
    /// <param name="cancellationToken">
    /// Cancels the query before it starts. A native query that is already running cannot be interrupted.
    /// </param>
    /// <returns>
    /// A task whose result is <see langword="true"/> when case-sensitive lookup is enabled, or
    /// <see langword="false"/> when it is disabled or the query is unsupported.
    /// </returns>
    /// <exception cref="Win32Exception">The directory cannot be opened.</exception>
    /// <exception cref="IOException">The native query fails for a reason other than an unsupported query.</exception>
    /// <exception cref="OperationCanceledException">The query is canceled before it starts.</exception>
    /// <exception cref="InvalidOperationException">The composition export has been shut down.</exception>
    Task<bool> PathIsCaseSensitive(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates and configures the shared process console for diagnostic output.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the operation before it is dispatched. A native operation that has started cannot be interrupted.
    /// </param>
    /// <returns>
    /// A task whose result is <see langword="true"/> when this call creates the shared console session, or
    /// <see langword="false"/> when that session is already active.
    /// </returns>
    /// <remarks>
    /// The console session and output routing are shared by live kernel services in this process. They remain active
    /// after this export is killed; call <see cref="FreeConsole(CancellationToken)"/> explicitly to release them. An
    /// unrelated native console association is not detached to make allocation succeed.
    /// </remarks>
    /// <exception cref="Win32Exception">
    /// The console cannot be allocated or configured.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Console setup and its rollback both fail.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The operation is canceled before dispatch.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A prior cleanup has failed and must be completed, or the composition export has been shut down.
    /// </exception>
    Task<bool> ShowConsole(CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the console session created by the shared Win32 kernel services.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the operation before it is dispatched. A native operation that has started cannot be interrupted.
    /// </param>
    /// <returns>
    /// A task whose result is <see langword="true"/> when this call completes cleanup of the shared session, or
    /// <see langword="false"/> when no session created by the shared services exists.
    /// </returns>
    /// <remarks>
    /// Releasing the session affects every service using the shared console. Killing this export does not release it.
    /// </remarks>
    /// <exception cref="Win32Exception">
    /// Native console cleanup fails.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The operation is canceled before dispatch.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The composition export has been shut down.
    /// </exception>
    Task<bool> FreeConsole(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the default Windows Shell action for a file or other Shell path.
    /// </summary>
    /// <param name="file">The path on which to run the default action.</param>
    /// <param name="cancellationToken">
    /// Cancels work before the Shell call starts. A Shell call that is already running cannot be interrupted.
    /// </param>
    /// <returns>A task that completes when the Shell execution call finishes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">The Shell cannot run the default action.</exception>
    /// <exception cref="OperationCanceledException">The operation is canceled before the Shell call starts.</exception>
    /// <exception cref="InvalidOperationException">The composition export has been shut down.</exception>
    Task ExecuteDefault(string file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a file with the specified executable using the default Windows Shell action.
    /// </summary>
    /// <param name="file">The file path to pass as an argument to the executable.</param>
    /// <param name="with">The executable to run.</param>
    /// <param name="cancellationToken">
    /// Cancels work before the Shell call starts. A Shell call that is already running cannot be interrupted.
    /// </param>
    /// <returns>A task that completes when the Shell execution call finishes.</returns>
    /// <exception cref="ArgumentException">The file path is not valid.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="with"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">The Shell cannot run the executable.</exception>
    /// <exception cref="OperationCanceledException">The operation is canceled before the Shell call starts.</exception>
    /// <exception cref="InvalidOperationException">The composition export has been shut down.</exception>
    Task ExecuteDefault(string file, string with, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the Windows Properties action for a Shell path.
    /// </summary>
    /// <param name="file">The path whose properties to open.</param>
    /// <param name="cancellationToken">
    /// Cancels work before the Shell call starts. A Shell call that is already running cannot be interrupted.
    /// </param>
    /// <returns>A task that completes when the Shell execution call finishes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">The Shell cannot open the Properties action.</exception>
    /// <exception cref="OperationCanceledException">The operation is canceled before the Shell call starts.</exception>
    /// <exception cref="InvalidOperationException">The composition export has been shut down.</exception>
    Task ExecuteProperties(string file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the target path of a Windows shortcut.
    /// </summary>
    /// <param name="file">The path of the shortcut to resolve.</param>
    /// <param name="cancellationToken">Cancels the lookup before it starts on an STA worker.</param>
    /// <returns>
    /// A task whose result is the shortcut target path, or <see langword="null"/> when the path is not a shortcut
    /// or the lookup fails. The target is not checked for existence.
    /// </returns>
    /// <exception cref="OperationCanceledException">The lookup is canceled before it starts.</exception>
    /// <exception cref="InvalidOperationException">The composition export has been shut down.</exception>
    Task<string> GetLinkPath(string file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an absolute path with the stored casing of each existing file-system component below the path root.
    /// </summary>
    /// <param name="path">
    /// The existing file or directory path.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the lookup before it starts. A native query that is already running cannot be interrupted.
    /// </param>
    /// <returns>
    /// A task whose result is the absolute path with each component below its root using its stored spelling.
    /// </returns>
    /// <remarks>
    /// Relative paths are resolved against the current directory. The root spelling comes from that resolved path.
    /// Short (8.3) names are expanded to their stored long names. Native lookups of ordinary drive and UNC paths
    /// use extended-length paths, without requiring the host to opt in to Win32 long-path support.
    /// Explicit extended paths retain Windows extended-path semantics: use backslashes and omit . and .. navigation.
    /// Bare device objects and alternate data streams are not supported. Managed path validation, including
    /// .NET Framework host path-handling settings, still applies.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is invalid, contains wildcards, or identifies a non-file-system device.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The runtime rejects the path format, such as an alternate data stream path on .NET Framework.
    /// </exception>
    /// <exception cref="PathTooLongException">
    /// The path exceeds the runtime's permitted length.
    /// </exception>
    /// <exception cref="Win32Exception">
    /// The path cannot be opened or a path component cannot be found or enumerated.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The lookup is canceled before it starts.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The composition export has been shut down.
    /// </exception>
    Task<string> GetStoredPath(string path, CancellationToken cancellationToken = default);
}
