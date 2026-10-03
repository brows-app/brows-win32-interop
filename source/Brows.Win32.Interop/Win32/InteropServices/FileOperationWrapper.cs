using Brows.Win32.InteropServices.ComTypes;
using System;

namespace Brows.Win32.InteropServices;

internal sealed class FileOperationWrapper : ComObjectWrapper<IFileOperation> {
    protected sealed override IFileOperation Factory() {
        return (IFileOperation)Activator.CreateInstance(CLSID.Managed.Type.FileOperation);
    }

    public TResult UseFileOperation<TResult>(Func<IFileOperation, TResult> action) {
        return UseComObject(action);
    }
}
