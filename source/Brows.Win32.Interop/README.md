# Brows.Win32.Interop

Windows Shell and file-system interop for .NET. This package provides higher-level services for Shell execution, shortcut resolution, file identity, stored path casing, and directory case-sensitivity checks, plus lower-level Win32 and COM bindings used by other Brows packages.

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

`ExecuteDefault` runs the default Shell verb for a path. Its overload `ExecuteDefault(file, with, cancellationToken)` opens `file` with the specified executable. `ExecuteProperties` opens the Properties verb. These methods return `Task`; await them to observe errors. Shell work runs on an STA worker. An optional cancellation token can stop work before dispatch, but cannot interrupt a native Shell call already in progress.

`GetLinkPath` resolves a `.lnk` shortcut and returns its target path, or `null` for a non-shortcut or a lookup failure. Cancellation is propagated. `Win32ShellService` can use a supplied `Brows.Threading.STAThreadPool`; passing `null` creates a pool owned by the service. Dispose the service after its operations finish.

## License

MIT.
