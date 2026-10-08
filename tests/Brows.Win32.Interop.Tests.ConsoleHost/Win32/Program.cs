using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Brows.Win32;

internal static class Program {
    private static TextWriter Reporter;

    private static void Report(string key, string value) {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        Reporter.Write(key);
        Reporter.Write('\t');
        Reporter.Write(Convert.ToBase64String(bytes));
        Reporter.WriteLine();
        Reporter.Flush();
    }

    private static void RunLifecycle() {
        var cachedOut = Console.Out;
        var cachedError = Console.Error;
        Report("cached_writers_read_before_show", (cachedOut is not null && cachedError is not null).ToString());

        var initiatingService = new Win32KernelService();
        var firstShow = initiatingService.ShowConsole();
        var secondShow = initiatingService.ShowConsole();
        Native.HideConsoleWindow();
        Console.Out.WriteLine("console-lifecycle-π-雪");
        Console.Error.WriteLine("console-error-π-雪");
        Console.Out.Flush();
        Console.Error.Flush();
        initiatingService.Dispose();

        Console.Out.WriteLine("after-service-dispose-π");
        Console.Error.WriteLine("error-after-service-dispose-雪");
        Console.Out.Flush();
        Console.Error.Flush();
        var bufferAfterDispose = Native.ReadConsoleBuffer();

        var releaseService = new Win32KernelService();
        var released = releaseService.FreeConsole();
        var releasedAgain = releaseService.FreeConsole();
        var reopened = releaseService.ShowConsole();
        Native.HideConsoleWindow();
        Console.Out.WriteLine("console-reopened-雪");
        Console.Out.Flush();
        var bufferAfterReopen = Native.ReadConsoleBuffer();
        var releasedReopened = releaseService.FreeConsole();
        releaseService.Dispose();

        Report("first_show", firstShow.ToString());
        Report("second_show", secondShow.ToString());
        Report("buffer_after_dispose", bufferAfterDispose);
        Report("released_by_fresh_service", released.ToString());
        Report("released_again", releasedAgain.ToString());
        Report("reopened", reopened.ToString());
        Report("buffer_after_reopen", bufferAfterReopen);
        Report("released_reopened", releasedReopened.ToString());
    }

    private static void RunExistingConsole() {
        if (!Native.AllocConsole()) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        Native.HideConsoleWindow();
        Native.WriteConsoleText("preexisting-console-buffer-marker\r\n");
        var consoleWindowBefore = Native.GetConsoleWindow();
        var outputHandleBefore = Native.GetStdHandle(Native.STD_OUTPUT_HANDLE);
        var errorHandleBefore = Native.GetStdHandle(Native.STD_ERROR_HANDLE);
        var bufferBeforeShow = Native.ReadConsoleBuffer();
        var service = new Win32KernelService();
        var showError = string.Empty;
        try {
            service.ShowConsole();
        }
        catch (Exception exception) {
            showError = exception.GetType().FullName;
        }
        var consoleAssociationPreserved = Native.GetConsoleWindow() == consoleWindowBefore;
        var outputHandlePreserved = Native.GetStdHandle(Native.STD_OUTPUT_HANDLE) == outputHandleBefore;
        var errorHandlePreserved = Native.GetStdHandle(Native.STD_ERROR_HANDLE) == errorHandleBefore;
        var bufferAfterShow = Native.ReadConsoleBuffer();
        var freeWithoutOwnedSession = service.FreeConsole();
        var associationPreservedAfterFree = Native.GetConsoleWindow() == consoleWindowBefore;
        var bufferAfterFree = Native.ReadConsoleBuffer();
        service.Dispose();
        var nativeFree = Native.FreeConsole();

        Report("show_error", showError);
        Report("console_association_preserved", consoleAssociationPreserved.ToString());
        Report("output_handle_preserved", outputHandlePreserved.ToString());
        Report("error_handle_preserved", errorHandlePreserved.ToString());
        Report("buffer_before_show", bufferBeforeShow);
        Report("buffer_after_show", bufferAfterShow);
        Report("buffer_after_free", bufferAfterFree);
        Report("free_without_owned_session", freeWithoutOwnedSession.ToString());
        Report("association_preserved_after_free", associationPreservedAfterFree.ToString());
        Report("native_free", nativeFree.ToString());
    }

    private static void RunRedirection() {
        Console.Out.WriteLine("redirect-before-show-out");
        Console.Error.WriteLine("redirect-before-show-error");
        Console.Out.Flush();
        Console.Error.Flush();

        var service = new Win32KernelService();
        var shown = service.ShowConsole();
        Native.HideConsoleWindow();
        Console.Out.WriteLine("redirect-console-buffer-marker");
        Console.Error.WriteLine("redirect-console-error-marker");
        Console.Out.Flush();
        Console.Error.Flush();
        var buffer = Native.ReadConsoleBuffer();
        var released = service.FreeConsole();
        Console.Out.WriteLine("redirect-after-free-out");
        Console.Error.WriteLine("redirect-after-free-error");
        Console.Out.Flush();
        Console.Error.Flush();
        service.Dispose();

        Report("shown", shown.ToString());
        Report("buffer", buffer);
        Report("released", released.ToString());
    }

    private static void RunControlEvents() {
        var service = new Win32KernelService();
        var shown = service.ShowConsole();
        Native.HideConsoleWindow();
        using (var controlCObserved = new ManualResetEvent(false)) {
            using (var controlBreakObserved = new ManualResetEvent(false)) {
                Native.ConsoleCtrlHandler observer = controlType => {
                    if (controlType == Native.CTRL_C_EVENT) {
                        controlCObserved.Set();
                    }
                    if (controlType == Native.CTRL_BREAK_EVENT) {
                        controlBreakObserved.Set();
                    }
                    return false;
                };
                if (!Native.SetConsoleCtrlHandler(observer, true)) {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                try {
                    var controlCGenerated = Native.GenerateConsoleCtrlEvent(Native.CTRL_C_EVENT, 0);
                    var controlCWasObserved = controlCGenerated && controlCObserved.WaitOne(5000);
                    if (controlCWasObserved) {
                        Console.Out.WriteLine("alive-after-ctrl-c");
                        Console.Out.Flush();
                    }
                    var controlBreakGenerated = Native.GenerateConsoleCtrlEvent(Native.CTRL_BREAK_EVENT, 0);
                    var controlBreakWasObserved = controlBreakGenerated && controlBreakObserved.WaitOne(5000);
                    if (controlBreakWasObserved) {
                        Console.Out.WriteLine("alive-after-ctrl-break");
                        Console.Out.Flush();
                    }
                    var buffer = Native.ReadConsoleBuffer();

                    Report("shown", shown.ToString());
                    Report("ctrl_c_generated", controlCGenerated.ToString());
                    Report("ctrl_c_observed", controlCWasObserved.ToString());
                    Report("ctrl_break_generated", controlBreakGenerated.ToString());
                    Report("ctrl_break_observed", controlBreakWasObserved.ToString());
                    Report("buffer", buffer);
                }
                finally {
                    var observerRemoved = Native.SetConsoleCtrlHandler(observer, false);
                    GC.KeepAlive(observer);
                    var released = service.FreeConsole();
                    service.Dispose();
                    Report("observer_removed", observerRemoved.ToString());
                    Report("released", released.ToString());
                }
            }
        }
    }

    private static class Native {
        public const int STD_OUTPUT_HANDLE = -11;
        public const int STD_ERROR_HANDLE = -12;
        public const uint CTRL_C_EVENT = 0;
        public const uint CTRL_BREAK_EVENT = 1;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public delegate bool ConsoleCtrlHandler(uint controlType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleCtrlHandler(
            ConsoleCtrlHandler handler,
            [MarshalAs(UnmanagedType.Bool)] bool add);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", EntryPoint = "WriteConsoleW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteConsole(
            SafeFileHandle hConsoleOutput,
            string lpBuffer,
            uint nNumberOfCharsToWrite,
            out uint lpNumberOfCharsWritten,
            IntPtr lpReserved);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleScreenBufferInfo(
            SafeFileHandle hConsoleOutput,
            out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "ReadConsoleOutputCharacterW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern bool ReadConsoleOutputCharacter(
            SafeFileHandle hConsoleOutput,
            StringBuilder lpCharacter,
            uint nLength,
            COORD dwReadCoord,
            out uint lpNumberOfCharsRead);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static void HideConsoleWindow() {
            var window = GetConsoleWindow();
            if (window != IntPtr.Zero) {
                ShowWindow(window, 0);
            }
        }

        public static void WriteConsoleText(string text) {
            const uint GENERIC_WRITE = 0x40000000;
            const uint FILE_SHARE_READ = 0x00000001;
            const uint FILE_SHARE_WRITE = 0x00000002;
            const uint OPEN_EXISTING = 3;

            using (var output = CreateFile(
                "CONOUT$",
                GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero)) {
                if (output.IsInvalid) {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                if (!WriteConsole(output, text, (uint)text.Length, out var charsWritten, IntPtr.Zero)) {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                if (charsWritten != text.Length) {
                    throw new IOException("The console accepted only part of the test marker.");
                }
            }
        }

        public static string ReadConsoleBuffer() {
            const uint GENERIC_READ = 0x80000000;
            const uint FILE_SHARE_READ = 0x00000001;
            const uint FILE_SHARE_WRITE = 0x00000002;
            const uint OPEN_EXISTING = 3;

            using (var output = CreateFile(
                "CONOUT$",
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero)) {
                if (output.IsInvalid) {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                if (!GetConsoleScreenBufferInfo(output, out var info)) {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var bufferLength = (uint)Math.Min(
                    (int)info.Size.X * info.Size.Y,
                    16384);
                var buffer = new StringBuilder((int)bufferLength);
                if (!ReadConsoleOutputCharacter(
                    output,
                    buffer,
                    bufferLength,
                    new COORD(0, 0),
                    out var charactersRead)) {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                return buffer.ToString(0, (int)charactersRead);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD {
            public short X;
            public short Y;

            public COORD(short x, short y) {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SMALL_RECT {
            public short Left;
            public short Top;
            public short Right;
            public short Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CONSOLE_SCREEN_BUFFER_INFO {
            public COORD Size;
            public COORD CursorPosition;
            public ushort Attributes;
            public SMALL_RECT Window;
            public COORD MaximumWindowSize;
        }
    }

    public static int Main(string[] args) {
        if (args.Length != 2) {
            return 2;
        }

        using (var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.Out)) {
            pipe.Connect(30000);
            using (var reporter = new StreamWriter(pipe, new UTF8Encoding(false))) {
                reporter.AutoFlush = true;
                Reporter = reporter;
                try {
                    Native.FreeConsole();
                    switch (args[0]) {
                        case "lifecycle":
                            RunLifecycle();
                            break;
                        case "existing-console":
                            RunExistingConsole();
                            break;
                        case "redirection":
                            RunRedirection();
                            break;
                        case "control-events":
                            RunControlEvents();
                            break;
                        default:
                            throw new ArgumentException("Unknown scenario.", nameof(args));
                    }
                    return 0;
                }
                catch (Exception exception) {
                    Report("exception", exception.ToString());
                    return 1;
                }
            }
        }
    }

}
