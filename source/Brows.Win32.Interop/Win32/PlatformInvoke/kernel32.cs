using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Brows.Win32.PlatformInvoke;

[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "N/A")]
internal partial class kernel32 {
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal delegate bool ConsoleControlHandler(uint controlType);

    public delegate PROGRESS ProgressRoutine(
        long TotalFileSize,
        long TotalBytesTransferred,
        long StreamSize,
        long StreamBytesTransferred,
        uint dwStreamNumber,
        CALLBACK dwCallbackReason,
        IntPtr hSourceFile,
        IntPtr hDestinationFile,
        IntPtr lpData);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        FILE_INFO_BY_HANDLE_CLASS fileInformationClass,
        out FILE_ID_INFO lpFileInformation,
        uint dwBufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVolumeInformationByHandleW(
        SafeFileHandle hFile,
        IntPtr lpVolumeNameBuffer,
        uint nVolumeNameSize,
        IntPtr lpVolumeSerialNumber,
        IntPtr lpMaximumComponentLength,
        IntPtr lpFileSystemFlags,
        StringBuilder lpFileSystemNameBuffer,
        uint nFileSystemNameSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.U4)] FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        [MarshalAs(UnmanagedType.U4)] FileMode dwCreationDisposition,
        [MarshalAs(UnmanagedType.U4)] FileAttributes dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindFirstFileW(
        string lpFileName,
        out WIN32_FIND_DATAW lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindClose(IntPtr hFindFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CopyFileExW(
        string lpExistingFileName,
        string lpNewFileName,
        ProgressRoutine lpProgressRoutine,
        IntPtr lpData,
        ref int pbCancel,
        COPY_FILE dwCopyFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveFileWithProgressW(
        string lpExistingFileName,
        string lpNewFileName,
        ProgressRoutine lpProgressRoutine,
        IntPtr lpData,
        MOVEFILE dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandleW(
        [In, Optional, MarshalAs(UnmanagedType.LPWStr)] string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(
        [In] IntPtr hDevice,
        [In] uint dwIoControlCode,
        [In, Optional] IntPtr lpInBuffer,
        [In] uint nInBufferSize,
        [Out, Optional] IntPtr lpOutBuffer,
        [In] uint nOutBufferSize,
        [Out, Optional] out uint lpBytesReturned,
        [In, Out, Optional] IntPtr lpOverlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindFirstChangeNotificationW(
      [In] string lpPathName,
      [In] bool bWatchSubtree,
      [In, MarshalAs(UnmanagedType.U4)] FILE_NOTIFY_CHANGE dwNotifyFilter);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FindNextChangeNotification([In] IntPtr hChangeHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FindCloseChangeNotification([In] IntPtr hChangeHandle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ReadDirectoryChangesW(
      [In] IntPtr hDirectory,
      [Out] IntPtr lpBuffer,
      [In] uint nBufferLength,
      [In] bool bWatchSubtree,
      [In] uint dwNotifyFilter,
      [Out, Optional] out uint lpBytesReturned,
      [In, Out, Optional] IntPtr lpOverlapped,
      [In, Optional] IntPtr lpCompletionRoutine);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    public static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetStdHandle(int kind);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetStdHandle(int kind, IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetConsoleCtrlHandler(
        ConsoleControlHandler handler,
        [MarshalAs(UnmanagedType.Bool)] bool add);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteConsoleW(
        SafeFileHandle handle,
        string text,
        uint length,
        out uint written,
        IntPtr reserved);
}
