using Brows.Win32.PlatformInvoke;
using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Brows.Win32;

internal sealed class Win32ConsoleNative : IWin32ConsoleNative {
    private const uint GenericWrite = 0x40000000;
    private const uint ControlC = 0;
    private const uint ControlBreak = 1;

    private readonly kernel32.ConsoleControlHandler Handler = HandleControl;

    private static bool HandleControl(uint kind) {
        var isDiagnosticKey = kind == ControlC || kind == ControlBreak;
        return isDiagnosticKey;
    }

    void IWin32ConsoleNative.Allocate() {
        if (!kernel32.AllocConsole()) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    void IWin32ConsoleNative.Free() {
        if (!kernel32.FreeConsole()) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    IntPtr IWin32ConsoleNative.GetStandardHandle(int kind) {
        return kernel32.GetStdHandle(kind);
    }

    void IWin32ConsoleNative.SetStandardHandle(int kind, IntPtr value) {
        if (!kernel32.SetStdHandle(kind, value)) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    void IWin32ConsoleNative.RegisterControlHandler() {
        if (!kernel32.SetConsoleCtrlHandler(Handler, true)) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    SafeFileHandle IWin32ConsoleNative.OpenOutput() {
        var handle = kernel32.CreateFileW(
            "CONOUT$", GenericWrite, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
        if (handle.IsInvalid) {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        return handle;
    }

    int IWin32ConsoleNative.WriteOutput(SafeFileHandle handle, string text) {
        if (!kernel32.WriteConsoleW(handle, text, (uint)text.Length, out var written, IntPtr.Zero)) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        return checked((int)written);
    }
}
