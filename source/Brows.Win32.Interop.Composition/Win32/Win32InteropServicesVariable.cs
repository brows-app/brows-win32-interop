using Brows.Composition;
using Brows.Threading;

namespace Brows.Win32;

public sealed class Win32InteropServicesVariable : IExportVariable {
    public STAThreadPool ThreadPool { get; set; }
}
