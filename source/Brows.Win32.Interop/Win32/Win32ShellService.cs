using Brows.Threading;
using Brows.Win32.InteropServices;
using Brows.Win32.PlatformInvoke;
using Domore.Logs;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Brows.Win32;

/// <summary>
/// Executes Windows Shell verbs and resolves shortcut targets on STA workers.
/// </summary>
public sealed class Win32ShellService : Win32BaseService {
    private readonly bool ThreadPoolOwned;
    private readonly STAThreadPool ThreadPool;

    private static readonly ILog Log = Logging.For(typeof(Win32ShellService));

    private static void Execute(string file, string parameters, string verb) {
        var info = new SHELLEXECUTEINFOW {
            cbSize = (uint)Marshal.SizeOf<SHELLEXECUTEINFOW>(),
            fMask = (uint)(SEE_MASK.NOASYNC |
                           SEE_MASK.INVOKEIDLIST |
                           SEE_MASK.FLAG_NO_UI |
                           SEE_MASK.FLAG_LOG_USAGE),
            lpDirectory = Path.GetDirectoryName(file),
            lpFile = file,
            lpParameters = parameters,
            lpVerb = verb,
            nShow = (int)SW.SHOW,
        };
        var success = shell32.ShellExecuteExW(ref info);
        if (success == false) {
            throw new Win32Exception();
        }
    }

    private async Task ExecuteAsync(string file,
                                    string parameters,
                                    string verb,
                                    string name,
                                    CancellationToken cancellationToken) {
        BeginOperation();
        try {
            var work = ThreadPool.Work(
                name: name,
                work: () => Execute(file, parameters, verb),
                cancellationToken: cancellationToken);
            await work.ConfigureAwait(false);
        }
        finally {
            EndOperation();
        }
    }

    private protected sealed override void DisposeCore() {
        try {
            if (ThreadPoolOwned) {
                ThreadPool.Empty();
            }
        }
        catch (Exception ex) {
            if (Log.Warn()) {
                Log.Warn(ex);
            }
        }
    }

    /// <summary>
    /// Creates a Shell service with a supplied STA pool or a service-owned pool.
    /// </summary>
    /// <param name="threadPool">The STA pool to use, or <see langword="null"/> to create one.</param>
    public Win32ShellService(STAThreadPool threadPool) {
        ThreadPool = threadPool ?? new(nameof(Win32ShellService));
        ThreadPoolOwned = ThreadPool != threadPool;
    }

    /// <summary>
    /// Runs the default Shell action for a file or other Shell path.
    /// </summary>
    /// <param name="file">The path to execute.</param>
    /// <param name="cancellationToken">Cancels work before it begins on an STA worker.</param>
    /// <returns>A task that completes when the Shell execution call finishes.</returns>
    public Task ExecuteDefault(string file, CancellationToken cancellationToken = default) {
        return ExecuteAsync(
            file, parameters: null, verb: null, name: nameof(ExecuteDefault), cancellationToken);
    }

    /// <summary>
    /// Opens a file with the specified executable using the default Shell action.
    /// </summary>
    /// <param name="file">The file to open.</param>
    /// <param name="with">The executable to open the file with.</param>
    /// <param name="cancellationToken">Cancels work before it begins on an STA worker.</param>
    /// <returns>A task that completes when the Shell execution call finishes.</returns>
    public Task ExecuteDefault(string file, string with, CancellationToken cancellationToken = default) {
        return ExecuteAsync(
            file: with, parameters: $"\"{file}\"", verb: null, name: nameof(ExecuteDefault), cancellationToken);
    }

    /// <summary>
    /// Opens the Windows Properties action for a Shell path.
    /// </summary>
    /// <param name="file">The path whose properties to open.</param>
    /// <param name="cancellationToken">Cancels work before it begins on an STA worker.</param>
    /// <returns>A task that completes when the Shell execution call finishes.</returns>
    public Task ExecuteProperties(string file, CancellationToken cancellationToken = default) {
        return ExecuteAsync(
            file, parameters: null, verb: "properties", name: nameof(ExecuteProperties), cancellationToken);
    }

    /// <summary>
    /// Resolves the target path of a Windows shortcut.
    /// </summary>
    /// <param name="file">The path of the shortcut to resolve.</param>
    /// <param name="cancellationToken">Cancels the lookup before it begins on an STA worker.</param>
    /// <returns>The shortcut target, or <see langword="null"/> for a non-shortcut or lookup failure.</returns>
    public async Task<string> GetLinkPath(string file, CancellationToken cancellationToken) {
        BeginOperation();
        try {
            return await core(file, cancellationToken).ConfigureAwait(false);
        }
        finally {
            EndOperation();
        }
        async Task<string> core(string file, CancellationToken cancellationToken) {
            if (cancellationToken.IsCancellationRequested) {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var ext = Path.GetExtension(file);
            if (ext?.EndsWith("lnk", StringComparison.OrdinalIgnoreCase) != true) {
                return null;
            }
            try {
                var shortcutPath = Path.GetFullPath(file);
                var work = ThreadPool.Work(
                    name: nameof(GetLinkPath),
                    work: () => {
                        using (var wrapper = new ShellWrapper()) {
                            return wrapper.UseShell(wrapped => {
                                var shell = (dynamic)wrapped;
                                var folder = shell.NameSpace(Path.GetDirectoryName(shortcutPath));
                                var folderItem = folder?.ParseName(Path.GetFileName(shortcutPath));
                                var link = folderItem?.GetLink;
                                var path = link?.Path;
                                return path;
                            });
                        }
                    },
                    cancellationToken: cancellationToken);
                return await work.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception ex) {
                if (Log.Warn()) {
                    Log.Warn(ex);
                }
                return null;
            }
        }
    }
}
