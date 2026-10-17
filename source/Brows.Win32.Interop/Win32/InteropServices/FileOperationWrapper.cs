using Brows.Win32.InteropServices.ComTypes;
using System;
using System.Runtime.Versioning;

namespace Brows.Win32.InteropServices;

#if !NETFRAMEWORK
[SupportedOSPlatform("windows")]
#endif
internal sealed class FileOperationWrapper : ComObjectWrapper<IFileOperation> {
    protected sealed override IFileOperation Factory() {
        return (IFileOperation)Activator.CreateInstance(CLSID.Managed.Type.FileOperation);
    }

    public TResult UseFileOperation<TResult>(Func<IFileOperation, TResult> action) {
        return UseComObject(action);
    }
}
