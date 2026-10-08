# Brows.Win32.Interop.Composition

Composition exports for the Win32 file queries and Windows Shell actions in `Brows.Win32.Interop`. The
`IWin32InteropServices` interface also creates batched Shell file operations.

## Install

```powershell
dotnet add package Brows.Win32.Interop.Composition
```

This package depends on `Brows.Composition` and `Brows.Win32.Interop.Operations`, which in turn depends on
`Brows.Win32.Interop`. It targets .NET Framework 4.6.2 and 4.8, and .NET 8 and 10 on Windows. It calls Windows APIs
and must run on Windows.

## Use the exported services

Have the host's Brows.Composition setup supply `IWin32InteropServices` to the code that needs it:

```csharp
using Brows.Win32;
using System.Threading;
using System.Threading.Tasks;

public sealed class DocumentActions(IWin32InteropServices services) {
    public async Task OpenShortcutAsync(string shortcutPath, CancellationToken cancellationToken) {
        var targetPath = await services.GetLinkPath(shortcutPath, cancellationToken);
        if (targetPath is not null) {
            await services.ExecuteDefault(targetPath, cancellationToken);
        }
    }

    public Task<bool> IsSameFileAsync(string path1, string path2, CancellationToken cancellationToken) {
        return services.PathsAreEquivalent(path1, path2, cancellationToken);
    }

    public Task<bool> DirectoryIsCaseSensitiveAsync(string directory, CancellationToken cancellationToken) {
        return services.PathIsCaseSensitive(directory, cancellationToken);
    }
}
```

`PathsAreEquivalent` compares the identity of existing files or directories, rather than their path text.
`PathIsCaseSensitive` returns `false` when case-sensitive lookup is disabled or the query is unsupported; other query
failures are reported by the returned task. `ExecuteDefault` runs a path's default Shell action, while its overload
`ExecuteDefault(file, with, cancellationToken)` passes `file` to the specified executable. `ExecuteProperties` opens the
Properties action. These Shell calls run on an STA worker, and their returned tasks report errors. Cancellation can stop
queued work before the native call starts; it cannot interrupt a native call already running.

`GetLinkPath` returns a shortcut's target, or `null` if the path is not a shortcut or lookup fails. It does not check
whether the target exists. Cancellation is propagated through the returned task.

`GetStoredPath` returns an absolute path with the stored casing of each existing file-system component beneath its
volume or share root. The root spelling comes from the resolved input path. The path must exist, and the caller must be
able to enumerate its components. Relative paths are resolved against the current directory. Cancellation can stop
the lookup before it starts, but cannot interrupt a native directory query already in progress.

Short (8.3) names expand to their stored long names. Native lookups of ordinary drive and UNC paths support long
paths without requiring the host's Win32 long-path opt-in; managed runtime path-validation settings still apply.
Explicit extended paths (`\\?\C:\...` or `\\?\UNC\server\share\...`) retain their prefix and Windows semantics:
use backslashes and omit `.` and `..` navigation. Bare device objects are rejected with `ArgumentException`.
Alternate data streams are unsupported; an ordinary ADS path throws `NotSupportedException` on .NET Framework
and a native lookup failure throws `Win32Exception`. Paths exceeding the runtime's permitted length throw
`PathTooLongException`.

### Console diagnostics

Use `ShowConsole` to allocate the shared process console and route `Console.Out` and `Console.Error` to it. Release the
session explicitly with `FreeConsole` when the application is finished with diagnostic output:

```csharp
using System;

await services.ShowConsole(cancellationToken);
Console.WriteLine("Diagnostic output");
await services.FreeConsole(cancellationToken);
```

`ShowConsole` returns `false` when the shared session is already active. It allocates a dedicated console; if the process
already has an unrelated native console association, allocation fails without detaching it. `FreeConsole` returns
`false` when there is no session created by these services. Both operations affect every live kernel service in the
process, so coordinate explicit release with other application components. Composition export shutdown does not release
the console or restore its output routing; call `FreeConsole` before host shutdown when the application wants to close
the session. A fresh `Win32KernelService` can release it after the Composition export has been killed. Cancellation can
stop a call before dispatch, but it cannot interrupt a native operation that has started. Native allocation, setup, or
cleanup failures are reported by the returned task; failed cleanup remains available for an explicit retry through a
live service.

Use `FileOperation(directory)` to create and execute a batch of Shell file operations. See
[`Brows.Win32.Interop.Operations`](../Brows.Win32.Interop.Operations/README.md) for batch configuration and progress.

### File-operation owner window

To make Shell UI for file-operation batches owned by an application window, configure
`Win32InteropServicesVariable.OnGetOwnerWindow` through the host's Brows.Composition variable configuration before
the export creates its services:

```csharp
var variable = new Win32InteropServicesVariable {
    OnGetOwnerWindow = () => ownerWindowHandle,
};
```

Supply `variable` to the host's configuration for this export. The callback applies to batches created by the
export, runs on an STA worker during `Operate`, and must return promptly without synchronously waiting on a thread
that could be waiting for the batch. Return `IntPtr.Zero` to run Shell UI without an owner window.

## License

MIT.
