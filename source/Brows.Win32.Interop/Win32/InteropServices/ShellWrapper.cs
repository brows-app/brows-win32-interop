using Brows.Win32.InteropServices.ComTypes;
using System;
using System.Runtime.Versioning;

namespace Brows.Win32.InteropServices;

#if !NETFRAMEWORK
[SupportedOSPlatform("windows")]
#endif
internal sealed class ShellWrapper : ComObjectWrapper<IShell> {
    protected sealed override IShell Factory() {
        return (IShell)Activator.CreateInstance(CLSID.Managed.Type.Shell);
    }

    public TResult UseShell<TResult>(Func<IShell, TResult> action) {
        return UseComObject(action);
    }
}
