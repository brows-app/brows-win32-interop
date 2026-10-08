using Microsoft.Win32.SafeHandles;
using System;

namespace Brows.Win32;

internal interface IWin32ConsoleNative {
    void Allocate();
    void Free();
    IntPtr GetStandardHandle(int kind);
    SafeFileHandle DuplicateStandardHandle(IntPtr handle);
    bool AreSameHandle(SafeFileHandle knownObject, IntPtr knownValue, IntPtr candidate);
    void SetStandardHandle(int kind, IntPtr value);
    void RegisterControlHandler();
    SafeFileHandle OpenOutput();
    int WriteOutput(SafeFileHandle handle, string text);
}
