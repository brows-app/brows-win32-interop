using Brows.Win32.PlatformInvoke;
using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Brows.Win32;

/// <summary>
/// Queries Windows file identity, directory case-sensitivity settings, and stored path casing,
/// and manages a shared diagnostic console.
/// </summary>
/// <remarks>
/// Disposing this service does not release the shared console. Call FreeConsole explicitly if release is desired.
/// On Windows 10 and later, native standard-handle ownership is checked by object identity. Earlier Windows versions
/// compare numeric values, so keep handles installed by console allocation open until FreeConsole completes.
/// </remarks>
public sealed class Win32KernelService : Win32BaseService {
    private readonly Win32ConsoleCoordinator ConsoleCoordinator;

    private static bool IsNtfs(SafeFileHandle hFile) {
        var fileSystemName = new StringBuilder(261);
        var result = kernel32.GetVolumeInformationByHandleW(
            hFile,
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            fileSystemName,
            (uint)fileSystemName.Capacity);
        return result && string.Equals(
            fileSystemName.ToString(),
            "NTFS",
            StringComparison.OrdinalIgnoreCase);
    }

    private protected sealed override void DisposeCore() {
    }

    internal Win32KernelService(Win32ConsoleCoordinator consoleCoordinator) {
        ConsoleCoordinator = consoleCoordinator ?? throw new ArgumentNullException(nameof(consoleCoordinator));
    }

    /// <summary>
    /// Initializes a new instance of the Win32 kernel service.
    /// </summary>
    public Win32KernelService() : this(Win32ConsoleCoordinator.Default) {
    }

    /// <summary>
    /// Allocates a shared diagnostic console and routes managed output and error to it.
    /// </summary>
    /// <returns>
    /// True when a console was created; false when the shared diagnostic console is already active.
    /// </returns>
    /// <remarks>
    /// The console belongs to the process and remains allocated after this service is disposed.
    /// Call FreeConsole explicitly through any live kernel service to release it if desired.
    /// An existing unrelated console is left intact. Managed output is temporarily redirected;
    /// console input and other Console API state are not managed. Closing the native console can terminate
    /// the application, including a WPF application. Use FreeConsole to dismiss it without that close event.
    /// Diagnostic Ctrl+C and Ctrl+Break are consumed. Native control handlers must be coordinated with
    /// console allocation and release. These methods do not control a shared terminal host's window.
    /// </remarks>
    /// <exception cref="Win32Exception">
    /// Native allocation or setup failed, including when the process already has an unrelated console.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A previous console cleanup must be completed by calling FreeConsole first.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Console setup and its rollback both failed; FreeConsole can retry the pending cleanup.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This service has been disposed.
    /// </exception>
    public bool ShowConsole() {
        BeginOperation();
        try {
            return ConsoleCoordinator.ShowConsole();
        }
        finally {
            EndOperation();
        }
    }

    /// <summary>
    /// Releases the shared diagnostic console and restores output routing.
    /// </summary>
    /// <returns>
    /// True when shared console cleanup completed; false when there is no session created by these services.
    /// </returns>
    /// <remarks>
    /// Any live kernel service can release the shared console. Service disposal never releases it.
    /// This method leaves unrelated console associations intact. Release discards the session's output history;
    /// a later ShowConsole creates a fresh console. Other attached processes can keep their console visible.
    /// Caller-installed replacement writers and handles are preserved. Failed cleanup remains available
    /// for retry through any live kernel service, even after this instance is disposed. On Windows 10 and later,
    /// native handles are compared by object identity. Earlier Windows versions compare numeric values; keep handles
    /// installed by console allocation open until cleanup returns true.
    /// </remarks>
    /// <exception cref="Win32Exception">
    /// Native release or standard-handle restoration failed.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This service has been disposed.
    /// </exception>
    public bool FreeConsole() {
        BeginOperation();
        try {
            return ConsoleCoordinator.FreeConsole();
        }
        finally {
            EndOperation();
        }
    }

    /// <summary>
    /// Gets an absolute path with the stored casing of each existing file-system component below the path root.
    /// </summary>
    /// <param name="path">
    /// The existing file or directory path.
    /// </param>
    /// <returns>
    /// The absolute path with each component below its root using its stored spelling.
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
    /// <exception cref="ObjectDisposedException">
    /// The service has been disposed.
    /// </exception>
    public string GetStoredPath(string path) {
        BeginOperation();
        try {
            if (path is null) {
                throw new ArgumentNullException(nameof(path));
            }
            return getStoredPathCore(path);
        }
        finally {
            EndOperation();
        }
        static string getStoredPathCore(string path) {
            var fullPath = Path.GetFullPath(path);
            var rootPath = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(rootPath)) {
                throw new ArgumentException("The path must have a root.", nameof(path));
            }
            var pathIsExtended = fullPath.StartsWith(@"\\?\", StringComparison.Ordinal);
            var pathIsDevice = fullPath.StartsWith(@"\\.\", StringComparison.Ordinal);
            var rootHasDevicePrefix = pathIsExtended || pathIsDevice;
            if (rootHasDevicePrefix) {
                var deviceRoot = rootPath.Substring(4).TrimEnd(Path.DirectorySeparatorChar);
                var rootIsDrive = deviceRoot.Length == 2
                    && char.IsLetter(deviceRoot[0])
                    && deviceRoot[1] == Path.VolumeSeparatorChar;
                var rootIsShare = deviceRoot.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase);
                var rootIsVolume = deviceRoot.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase)
                    && Guid.TryParse(deviceRoot.Substring(6), out _);
                var rootIsFileSystem = rootIsDrive || rootIsShare || rootIsVolume;
                if (!rootIsFileSystem) {
                    throw new ArgumentException(
                        "The path must identify a file-system file or directory.",
                        nameof(path));
                }
            }
            var wildcardSearchStart = pathIsExtended ? 4 : 0;
            var pathHasWildcards = fullPath.IndexOf('*') >= 0 || fullPath.IndexOf('?', wildcardSearchStart) >= 0;
            if (pathHasWildcards) {
                throw new ArgumentException("The path must not contain wildcard characters.", nameof(path));
            }
            var rootLength = rootPath.Length;
            var rootSeparatorIsMissing = rootLength < fullPath.Length
                && fullPath[rootLength] == Path.DirectorySeparatorChar;
            if (rootSeparatorIsMissing) {
                rootPath += Path.DirectorySeparatorChar;
                rootLength++;
            }
            var storedPath = rootPath;
            var nativePath = rootPath;
            if (!rootHasDevicePrefix) {
                nativePath = rootPath.StartsWith(@"\\", StringComparison.Ordinal)
                    ? @"\\?\UNC\" + rootPath.Substring(2)
                    : @"\\?\" + rootPath;
            }
            var pathIsRoot = fullPath.Length == rootLength;
            if (pathIsRoot) {
                using (var hRoot = kernel32.CreateFileW(
                    nativePath,
                    0,
                    FileShare.ReadWrite | FileShare.Delete,
                    IntPtr.Zero,
                    FileMode.Open,
                    (FileAttributes)FILE_FLAG.BACKUP_SEMANTICS,
                    IntPtr.Zero)) {
                    if (hRoot.IsInvalid) {
                        throw new Win32Exception();
                    }
                }
                return storedPath;
            }
            var components = fullPath.Substring(rootLength).Split(Path.DirectorySeparatorChar);
            foreach (var component in components) {
                if (string.IsNullOrEmpty(component)) {
                    continue;
                }
                /*
                 * Extended queries bypass Win32's trimming of ordinary components. GetFullPath can leave
                 * trailing periods and spaces on intermediate components, so retain that lookup behavior here.
                 */
                var queryComponent = rootHasDevicePrefix ? component : component.TrimEnd(' ', '.');
                var findHandle = kernel32.FindFirstFileW(
                    appendComponent(nativePath, queryComponent),
                    out var findData);
                if (findHandle == new IntPtr(-1)) {
                    throw new Win32Exception();
                }
                try {
                    storedPath = appendComponent(storedPath, findData.FileName);
                    nativePath = appendComponent(nativePath, findData.FileName);
                }
                finally {
                    kernel32.FindClose(findHandle);
                }
            }
            return storedPath;
        }
        static string appendComponent(string directory, string component) {
            if (directory[directory.Length - 1] == Path.DirectorySeparatorChar) {
                return directory + component;
            }
            return directory + Path.DirectorySeparatorChar + component;
        }
    }

    /// <summary>
    /// Determines whether two existing paths identify the same file or directory.
    /// </summary>
    /// <param name="path1">The first path to compare.</param>
    /// <param name="path2">The second path to compare.</param>
    /// <returns><see langword="true"/> when both paths have the same volume and file identity.</returns>
    public bool PathsAreEquivalent(string path1, string path2) {
        BeginOperation();
        try {
            return pathsAreEquivalentCore(path1, path2);
        }
        finally {
            EndOperation();
        }
        static bool pathsAreEquivalentCore(string path1, string path2) {
            using (var hFile1 = kernel32.CreateFileW(path1,
                                                     0,
                                                     FileShare.ReadWrite | FileShare.Delete,
                                                     IntPtr.Zero,
                                                     FileMode.Open,
                                                     (FileAttributes)FILE_FLAG.BACKUP_SEMANTICS,
                                                     IntPtr.Zero)) {
                if (hFile1.IsInvalid) {
                    throw new Win32Exception();
                }
                using (var hFile2 = kernel32.CreateFileW(path2,
                                                         0,
                                                         FileShare.ReadWrite | FileShare.Delete,
                                                         IntPtr.Zero,
                                                         FileMode.Open,
                                                         (FileAttributes)FILE_FLAG.BACKUP_SEMANTICS,
                                                         IntPtr.Zero)) {
                    if (hFile2.IsInvalid) {
                        throw new Win32Exception();
                    }
                    var fileIdInfo1 = default(FILE_ID_INFO);
                    var result1 = kernel32.GetFileInformationByHandleEx(
                        hFile1,
                        FILE_INFO_BY_HANDLE_CLASS.FileIdInfo,
                        out fileIdInfo1,
                        (uint)Marshal.SizeOf<FILE_ID_INFO>());
                    var error1 = result1 ? 0 : Marshal.GetLastWin32Error();
                    var fileIdInfo2 = default(FILE_ID_INFO);
                    var result2 = kernel32.GetFileInformationByHandleEx(
                        hFile2,
                        FILE_INFO_BY_HANDLE_CLASS.FileIdInfo,
                        out fileIdInfo2,
                        (uint)Marshal.SizeOf<FILE_ID_INFO>());
                    var error2 = result2 ? 0 : Marshal.GetLastWin32Error();
                    if (result1 && result2) {
                        return
                            fileIdInfo1.VolumeSerialNumber == fileIdInfo2.VolumeSerialNumber &&
                            fileIdInfo1.FileId.IdentifierLow == fileIdInfo2.FileId.IdentifierLow &&
                            fileIdInfo1.FileId.IdentifierHigh == fileIdInfo2.FileId.IdentifierHigh;
                    }
                    if (IsNtfs(hFile1) && IsNtfs(hFile2)) {
                        var fileInfo1 = default(BY_HANDLE_FILE_INFORMATION);
                        if (kernel32.GetFileInformationByHandle(hFile1, out fileInfo1) == false) {
                            throw new Win32Exception();
                        }
                        var fileInfo2 = default(BY_HANDLE_FILE_INFORMATION);
                        if (kernel32.GetFileInformationByHandle(hFile2, out fileInfo2) == false) {
                            throw new Win32Exception();
                        }
                        return
                            fileInfo1.VolumeSerialNumber == fileInfo2.VolumeSerialNumber &&
                            fileInfo1.FileIndexHigh == fileInfo2.FileIndexHigh &&
                            fileInfo1.FileIndexLow == fileInfo2.FileIndexLow;
                    }
                    throw new Win32Exception(result1 ? error2 : error1);
                }
            }
        }
    }

    /// <summary>
    /// Queries whether a directory is configured for case-sensitive name lookup.
    /// </summary>
    /// <param name="path">The directory path to query.</param>
    /// <returns><see langword="true"/> when case-sensitive lookup is enabled; <see langword="false"/> when it is disabled or the query is unsupported.</returns>
    /// <exception cref="IOException">The native query fails for a reason other than an unsupported query.</exception>
    public bool PathIsCaseSensitive(string path) {
        BeginOperation();
        try {
            return pathIsCaseSensitiveCore(path);
        }
        finally {
            EndOperation();
        }
        static bool pathIsCaseSensitiveCore(string path) {
            const uint FILE_CS_FLAG_CASE_SENSITIVE_DIR = 0x00000001;
            const uint FILE_READ_ATTRIBUTES = 0x00000080;
            var hFile = kernel32.CreateFileW(
                lpFileName: path,
                dwDesiredAccess: FILE_READ_ATTRIBUTES,
                dwShareMode: FileShare.ReadWrite | FileShare.Delete,
                lpSecurityAttributes: IntPtr.Zero,
                dwCreationDisposition: FileMode.Open,
                dwFlagsAndAttributes: (FileAttributes)FILE_FLAG.BACKUP_SEMANTICS,
                hTemplateFile: IntPtr.Zero);
            using (hFile) {
                if (hFile.IsInvalid) {
                    throw new Win32Exception();
                }
                var iosb = new IO_STATUS_BLOCK();
                var fcsi = new FILE_CASE_SENSITIVE_INFORMATION();
                var size = (uint)Marshal.SizeOf<FILE_CASE_SENSITIVE_INFORMATION>();
                var status = ntdll.NtQueryInformationFile(
                    hFile,
                    ref iosb,
                    ref fcsi,
                    size,
                    FILE_INFORMATION_CLASS.FileCaseSensitiveInformation);
                switch (status) {
                    case NTSTATUS.STATUS_SUCCESS:
                        return FILE_CS_FLAG_CASE_SENSITIVE_DIR == (fcsi.Flags & FILE_CS_FLAG_CASE_SENSITIVE_DIR);
                    case NTSTATUS.STATUS_NOT_IMPLEMENTED:
                    case NTSTATUS.STATUS_NOT_SUPPORTED:
                    case NTSTATUS.STATUS_INVALID_INFO_CLASS:
                        return false;
                    default:
                        throw new IOException(
                            $"NtQueryInformationFile failed with NTSTATUS 0x{(uint)status:X8}.");
                }
            }
        }
    }
}
