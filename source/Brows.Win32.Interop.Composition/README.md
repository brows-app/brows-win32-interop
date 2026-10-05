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

Use `FileOperation(directory)` to create and execute a batch of Shell file operations. See
[`Brows.Win32.Interop.Operations`](../Brows.Win32.Interop.Operations/README.md) for batch configuration and progress.

## License

MIT.
