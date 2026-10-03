using Brows.Win32.PlatformInvoke;
using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Brows.Win32;

/// <summary>
/// Queries Windows file identity and directory case-sensitivity settings.
/// </summary>
public sealed class Win32KernelService : Win32BaseService {
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

    private static bool PathsAreEquivalentCore(string path1, string path2) {
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

    private protected sealed override void DisposeCore() {
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
            return PathsAreEquivalentCore(path1, path2);
        }
        finally {
            EndOperation();
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
            return core(path);
        }
        finally {
            EndOperation();
        }
        static bool core(string path) {
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
