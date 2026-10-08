using System.IO;

namespace Brows.Win32;

internal interface IWin32ConsoleOutput {
    TextWriter Out { get; set; }
    TextWriter Error { get; set; }
}
