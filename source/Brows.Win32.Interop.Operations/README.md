# Brows.Win32.Interop.Operations

Batch file operations through the Windows Shell `IFileOperation` API. Queue copies, moves, creates, deletes, and renames against a directory, then execute the batch on an STA worker.

## Install

```powershell
dotnet add package Brows.Win32.Interop.Operations
```

This package depends on `Brows.Win32.Interop` and `Brows.Operations`. It targets .NET Framework 4.6.2 and 4.8, as well as the platform-neutral .NET 8 and 10 targets; the file-operation types are annotated `[SupportedOSPlatform("windows")]`. It must run on Windows.

## Copy a file

The example below creates a concrete `STAThreadPool`. Install its implementation package separately:

```powershell
dotnet add package Brows.Win32.STAThreadPool
```

On .NET 8 and 10, the project consuming this implementation must target `net8.0-windows` or
`net10.0-windows`, respectively. The `ISTAThreadPool` interface is supplied by
`Brows.Win32.STAThreadPool.Contracts`; use an interface variable to call `Work` and `Empty`.

```csharp
using Brows.Threading;
using Brows.Win32;
using Brows.Win32.Win32FileOperations;
using System.Threading;

string sourceFilePath = @"C:\Data\report.txt";
string destinationDirectory = @"C:\Archive";
ISTAThreadPool pool = new STAThreadPool("file-operations");
try {
    var batch = new Win32FileOperation(destinationDirectory, pool);
    batch.CopyFiles.Add(new CopyFile { Path = sourceFilePath });
    bool hadWork = await batch.Operate(progress: null, token: CancellationToken.None);
}
finally {
    pool.Empty();
}
```

`Directory` is the destination for `CopyFiles`, `MoveFiles`, and `CreateFiles`, and the containing directory for `DeleteFiles` and `RenameFiles`. `CopyFile` and `MoveFile` use source paths; `DeleteFile` uses `Name`; `RenameFile` uses `OldName` and `NewName`; `CreateFile` uses `Name` and `Attributes`.

The `Operate` result is `false` for an empty batch and `true` when a nonempty batch reaches native execution; it does not report the result of each item. Errors are surfaced by the returned task. The cancellation token is checked before and during queueing and at Shell progress callbacks. Work runs on the supplied `Brows.Threading.ISTAThreadPool` from the `Brows.Win32.STAThreadPool.Contracts` package, which the caller owns.

Options such as `Silent`, `NoConfirmation`, `RenameOnCollision`, and `RecycleOnDelete` control Windows Shell behavior. With the default `Silent = false`, Windows can show its own progress UI. To receive progress through `IOperationProgress`, set `Silent = true` and pass a progress receiver to `Operate`. For batches created through the Composition export, configure the Shell UI owner window with `Win32InteropServicesVariable.OnGetOwnerWindow` before the export creates its services. See the [Composition README](../Brows.Win32.Interop.Composition/README.md#file-operation-owner-window) for setup. The callback runs on the STA worker during `Operate` and must not synchronously wait on a thread that could be waiting for the batch.

## License

MIT.
