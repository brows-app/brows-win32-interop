# Brows.Win32.Interop

Windows Shell and file-system interop for .NET. This package provides higher-level services for Shell execution, shortcut resolution, file identity, stored path casing, directory case-sensitivity checks, and explicit diagnostic console management, plus lower-level Win32 and COM bindings used by other Brows packages.

## Install

```powershell
dotnet add package Brows.Win32.Interop
```

The package targets .NET Framework 4.6.2 and 4.8, and .NET 8 and 10 on Windows. It calls Windows APIs and must run on Windows.

## Use the services

```csharp
using Brows.Win32;
using System.Threading;

string firstPath = @"C:\Data\report.txt";
string secondPath = @"C:\Data\report-link.txt";
string directoryPath = @"C:\Data";
string documentPath = firstPath;
string shortcutPath = @"C:\Data\report.lnk";

using var kernel = new Win32KernelService();
bool sameFile = kernel.PathsAreEquivalent(firstPath, secondPath);
bool caseSensitive = kernel.PathIsCaseSensitive(directoryPath);
string storedPath = kernel.GetStoredPath(@"C:\DATA\REPORT.TXT");

using var shell = new Win32ShellService(threadPool: null);
await shell.ExecuteDefault(documentPath, CancellationToken.None);
string targetPath = await shell.GetLinkPath(shortcutPath, CancellationToken.None);
```

`PathsAreEquivalent` compares the identity of existing files or directories, rather than their path text. `PathIsCaseSensitive` queries a directory's case-sensitivity flag and returns `false` when the query is unsupported. Other query failures throw.

`GetStoredPath` returns an absolute path with the stored casing of each existing file or directory component beneath the volume or share root. Relative paths are resolved against the current directory, while the root spelling is retained from that resolved path. The path and its components must exist and be accessible; invalid paths throw `ArgumentException`, and native lookup failures throw `Win32Exception`.

Short (8.3) names expand to their stored long names. Native lookups of ordinary drive and UNC paths support long
paths without requiring the host's Win32 long-path opt-in; managed runtime path-validation settings still apply.
Explicit extended paths (`\\?\C:\...` or `\\?\UNC\server\share\...`) retain their prefix and Windows semantics:
use backslashes and omit `.` and `..` navigation. Bare device objects are rejected with `ArgumentException`.
Alternate data streams are unsupported; an ordinary ADS path throws `NotSupportedException` on .NET Framework
and a native lookup failure throws `Win32Exception`. Paths exceeding the runtime's permitted length throw
`PathTooLongException`.

`ExecuteDefault` runs the default Shell verb for a path. Its overload `ExecuteDefault(file, with, cancellationToken)` opens `file` with the specified executable. `ExecuteProperties` opens the Properties verb. These methods return `Task`; await them to observe errors. Shell work runs on an STA worker. An optional cancellation token can stop work before dispatch, but cannot interrupt a native Shell call already in progress.

`GetLinkPath` resolves a `.lnk` shortcut and returns its target path, or `null` for a non-shortcut or a lookup failure. Cancellation is propagated. `Win32ShellService` can use a supplied `Brows.Threading.STAThreadPool`; passing `null` creates a pool owned by the service. Dispose the service after its operations finish.

## Diagnostic console

A GUI application, including WPF, can create a dedicated diagnostic console:

~~~csharp
using Brows.Win32;
using System;

using var kernel = new Win32KernelService();
kernel.ShowConsole();
Console.WriteLine("Diagnostic output");
Console.Error.WriteLine("Diagnostic error");
kernel.FreeConsole(); // Call explicitly if release is desired before disposing the service.
~~~

The console is shared by the process. ShowConsole returns true when it creates a
session and false when that session is already active. FreeConsole returns true
when it completes cleanup and false when there is no session created by these
services. Any live kernel service can free it. **Disposing a service leaves the
console and its output routing intact:**

~~~csharp
using (var temporaryKernel = new Win32KernelService()) {
    temporaryKernel.ShowConsole();
}
Console.WriteLine("The console is still available.");
using var cleanupKernel = new Win32KernelService();
cleanupKernel.FreeConsole();
~~~

ShowConsole temporarily routes managed Console.Out and Console.Error to the
console even if they were initialized before allocation or native stdout/stderr
were redirected. Explicit release restores saved managed writers and native
standard-handle entries while preserving caller-installed replacements. Coordinate
external writer, handle, and console changes with these operations. Console.In,
blocking input, cached Is*Redirected values, and other Console API state are not
managed by this diagnostic-output service.

On Windows 10 and later, native standard handles are compared by object identity,
so a caller replacement is preserved even when it reuses the console handle's
numeric value. Earlier Windows versions compare numeric values; keep handles
installed by console allocation open until FreeConsole returns true.

An unrelated pre-existing console remains intact: ShowConsole throws
Win32Exception if allocation fails, and FreeConsole does not detach it. Native
setup/release errors are surfaced. Failed cleanup can be retried through any live
kernel service; ShowConsole rejects a session awaiting cleanup. If setup and its
rollback both fail, an AggregateException reports both errors.

Explicit release discards console history, and showing again creates a fresh
session. Other attached processes can keep their console visible. These methods
manage the application's native association, not the entire terminal host window.
Normal local desktop allocation is intended; pseudoconsole, remote, or hidden
startup configurations can have different presentation behavior.

For WPF, invoke FreeConsole from the GUI's dismissal command when desired, before
service disposal or export shutdown. Console Ctrl+C and Ctrl+Break are consumed
for diagnostics; native allocation/release reset the control-handler table, so
applications with their own handlers must coordinate registration. Closing the
native console window can terminate the WPF process and is not equivalent to
FreeConsole. Stop/await log producers as appropriate before explicit cleanup;
synchronous native writes can block in a paused console host.

## License

MIT.
