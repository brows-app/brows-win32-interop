using Brows.Win32.InteropServices.ComTypes;
using System;

namespace Brows.Win32.InteropServices;

internal sealed class ShellWrapper : ComObjectWrapper<IShell> {
    protected sealed override IShell Factory() {
        return (IShell)Activator.CreateInstance(CLSID.Managed.Type.Shell);
    }

    public TResult UseShell<TResult>(Func<IShell, TResult> action) {
        return UseComObject(action);
    }
}
