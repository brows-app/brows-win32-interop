using Brows.Operations;
using Brows.Win32.InteropServices;
using Brows.Win32.InteropServices.ComTypes;
using Brows.Win32.PlatformInvoke;
using System.Runtime.Versioning;

namespace Brows.Win32;

#if !NETFRAMEWORK
[SupportedOSPlatform("windows")]
#endif
internal sealed class Win32ProgressSink : FileOperationProgressSink {
    private volatile bool CancellationReturned;

    private HRESULT GetCancellationResult() {
        if (CancellationToken.IsCancellationRequested) {
            CancellationReturned = true;
            return HRESULTExtension.FromWin32(winerror.ERROR_CANCELLED);
        }
        return HRESULT.S_OK;
    }

    internal bool CancellationReported => CancellationReturned;

    public IOperationProgress Progress { get; }
    public CancellationToken CancellationToken { get; }

    public Win32ProgressSink(IOperationProgress progress, CancellationToken cancellationToken) {
        Progress = progress;
        CancellationToken = cancellationToken;
    }

    public sealed override HRESULT StartOperations() => GetCancellationResult();

    public sealed override HRESULT PreRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName) => GetCancellationResult();

    public sealed override HRESULT PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) => GetCancellationResult();

    public sealed override HRESULT PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) => GetCancellationResult();

    public sealed override HRESULT PreDeleteItem(uint dwFlags, IShellItem psiItem) => GetCancellationResult();

    public sealed override HRESULT PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, string pszNewName) => GetCancellationResult();

    public sealed override HRESULT UpdateProgress(uint iWorkTotal, uint iWorkSoFar) {
        var cancellationResult = GetCancellationResult();
        if (cancellationResult != HRESULT.S_OK) {
            return cancellationResult;
        }
        /*
         * Shell progress values are estimated work points, not byte counts.
         */
        Progress?.Change(
            setProgress: iWorkSoFar,
            setTarget: iWorkTotal,
            kind: OperationProgressKind.None);
        return base.UpdateProgress(iWorkTotal, iWorkSoFar);
    }
}
