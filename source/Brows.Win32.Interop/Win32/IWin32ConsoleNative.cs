using Microsoft.Win32.SafeHandles;
using System;

namespace Brows.Win32;

internal interface IWin32ConsoleNative {
    void Allocate();
    void Free();
    IntPtr GetStandardHandle(int kind);
    void SetStandardHandle(int kind, IntPtr value);
    void RegisterControlHandler();
    SafeFileHandle OpenOutput();
    int WriteOutput(SafeFileHandle handle, string text);
}
