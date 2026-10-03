using Brows.Operations;
using Brows.Win32.InteropServices;
using Brows.Win32.InteropServices.ComTypes;
using Brows.Win32.PlatformInvoke;

namespace Brows.Win32;

internal sealed class Win32ProgressSink : FileOperationProgressSink {
    private HRESULT CancellationResult => CancellationToken.IsCancellationRequested
        ? HRESULTExtension.FromWin32(winerror.ERROR_CANCELLED)
        : HRESULT.S_OK;

    public IOperationProgress Progress { get; }
    public CancellationToken CancellationToken { get; }

    public Win32ProgressSink(IOperationProgress progress, CancellationToken cancellationToken) {
        Progress = progress;
        CancellationToken = cancellationToken;
    }

    public sealed override HRESULT StartOperations() => CancellationResult;

    public sealed override HRESULT PreRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName) => CancellationResult;

    public sealed override HRESULT PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) => CancellationResult;

    public sealed override HRESULT PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) => CancellationResult;

    public sealed override HRESULT PreDeleteItem(uint dwFlags, IShellItem psiItem) => CancellationResult;

    public sealed override HRESULT PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, string pszNewName) => CancellationResult;

    public sealed override HRESULT UpdateProgress(uint iWorkTotal, uint iWorkSoFar) {
        var cancellationResult = CancellationResult;
        if (cancellationResult != HRESULT.S_OK) {
            return cancellationResult;
        }
        Progress?.Change(setProgress: iWorkSoFar, setTarget: iWorkTotal);
        return base.UpdateProgress(iWorkTotal, iWorkSoFar);
    }
}
