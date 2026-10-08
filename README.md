# brows-win32-interop

Windows interop libraries for .NET applications. The solution contains three NuGet packages:

| Package | Purpose |
| --- | --- |
| [Brows.Win32.Interop](source/Brows.Win32.Interop/README.md) | Win32 and COM bindings, Shell execution and shortcut resolution, file identity and case-sensitivity queries, and explicit diagnostic console management. |
| [Brows.Win32.Interop.Operations](source/Brows.Win32.Interop.Operations/README.md) | Batched Windows Shell file operations: copy, move, create, delete, and rename. |
| [Brows.Win32.Interop.Composition](source/Brows.Win32.Interop.Composition/README.md) | Brows.Composition exports for Win32 queries, Shell actions, and batched file operations. |

All packages support .NET Framework 4.6.2 and 4.8 and .NET 8 and 10 on Windows. They require Windows at runtime. See
each package README for installation and API examples.

## Build and test

Use the .NET SDK selected by `global.json` on Windows:

```powershell
dotnet restore brows-win32-interop.slnx
dotnet build brows-win32-interop.slnx --no-restore --configuration Release
dotnet test brows-win32-interop.slnx --no-restore --configuration Release --no-build
dotnet pack brows-win32-interop.slnx --no-restore --configuration Release --no-build
```

The solution also builds a non-packable WinExe helper for isolated native console tests.
The test projects are not packable. Each source package includes its own README at the package root.

## License

[MIT](LICENSE).
