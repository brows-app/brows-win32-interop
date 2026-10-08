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
    private const uint DuplicateSameAccess = 2;
    private const int InvalidHandleError = 6;

    private static readonly CompareObjectHandlesDelegate CompareObjectHandles = LoadCompareObjectHandles();
    private readonly kernel32.ConsoleControlHandler Handler = HandleControl;

    private static CompareObjectHandlesDelegate LoadCompareObjectHandles() {
        var module = kernel32.GetModuleHandleW("kernelbase.dll");
        if (module == IntPtr.Zero) {
            return null;
        }
        var procedure = kernel32.GetProcAddress(module, "CompareObjectHandles");
        if (procedure == IntPtr.Zero) {
            return null;
        }
        return Marshal.GetDelegateForFunctionPointer<CompareObjectHandlesDelegate>(procedure);
    }

    private static bool HandleControl(uint kind) {
        var isDiagnosticKey = kind == ControlC || kind == ControlBreak;
        return isDiagnosticKey;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool CompareObjectHandlesDelegate(IntPtr first, IntPtr second);

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

    SafeFileHandle IWin32ConsoleNative.DuplicateStandardHandle(IntPtr handle) {
        var handleIsInvalid = handle == IntPtr.Zero || handle == new IntPtr(-1);
        if (handleIsInvalid) {
            return null;
        }
        var currentProcess = kernel32.GetCurrentProcess();
        var duplicated = kernel32.DuplicateHandle(
            currentProcess,
            handle,
            currentProcess,
            out var duplicate,
            0,
            false,
            DuplicateSameAccess);
        if (!duplicated) {
            var error = Marshal.GetLastWin32Error();
            if (error == InvalidHandleError) {
                return null;
            }
            throw new Win32Exception(error);
        }
        return new SafeFileHandle(duplicate, ownsHandle: true);
    }

    bool IWin32ConsoleNative.AreSameHandle(SafeFileHandle knownObject,
                                           IntPtr knownValue,
                                           IntPtr candidate) {
        var knownObjectIsUsable = knownObject is not null && !knownObject.IsClosed && !knownObject.IsInvalid;
        if (!knownObjectIsUsable) {
            var sameNumericValue = knownValue == candidate;
            return sameNumericValue && !kernel32.GetHandleInformation(candidate, out _);
        }
        var candidateIsValid = kernel32.GetHandleInformation(candidate, out _);
        if (!candidateIsValid) {
            return knownValue == candidate;
        }
        if (CompareObjectHandles is not null) {
            return CompareObjectHandles(knownObject.DangerousGetHandle(), candidate);
        }
        return knownValue == candidate;
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
