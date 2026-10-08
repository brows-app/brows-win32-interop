using System;
using System.IO;

namespace Brows.Win32;

internal sealed class Win32ConsoleOutput : IWin32ConsoleOutput {
    TextWriter IWin32ConsoleOutput.Out {
        get => Console.Out;
        set => Console.SetOut(value);
    }

    TextWriter IWin32ConsoleOutput.Error {
        get => Console.Error;
        set => Console.SetError(value);
    }
}
