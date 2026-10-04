using Brows.Composition;

namespace Brows.Win32;

public interface IWin32InteropServices : IExport {
    IWin32FileOperation FileOperation(string directory);
}
