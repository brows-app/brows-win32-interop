using Brows.Composition;
using Brows.Threading;
using Domore.Logs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.Win32;

internal sealed class Win32InteropServices : IWin32InteropServices,
                                             IExportAndVary,
                                             IExportAndKill {
    private static readonly ILog Log = Logging.For(typeof(Win32InteropServices));

    private readonly Lazy<ServiceWrapper> LazyServices;
    private readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        Locker = new();

    private bool Killed;
    private bool ThreadPoolOwned { get; set; }
    private STAThreadPool ThreadPool { get; set; }

    Task IExportAndVary.Vary(IExportVariables variables, CancellationToken token) {
        throw new NotImplementedException();
    }

    void IExportAndKill.Kill() {
        ServiceWrapper services;
        STAThreadPool threadPool;
        bool threadPoolOwned;
        lock (Locker) {
            if (Killed) {
                return;
            }
            Killed = true;
            services = LazyServices.IsValueCreated ? LazyServices.Value : null;
            threadPool = ThreadPool;
            threadPoolOwned = ThreadPoolOwned;
        }
        try {
            services?.Dispose();
        }
        catch (Exception ex) {
            if (Log.Error()) {
                Log.Error(ex);
            }
        }
        try {
            if (threadPoolOwned) {
                threadPool.Empty();
            }
        }
        catch (Exception ex) {
            if (Log.Error()) {
                Log.Error(ex);
            }
        }
    }

    IWin32FileOperation IWin32InteropServices.FileOperation(string directory) {
        return new Win32FileOperation(directory, ThreadPool);
    }

    private sealed class ServiceWrapper : IDisposable {
        public Win32KernelService Kernel { get; }
        public Win32ShellService Shell { get; }

        public STAThreadPool ThreadPool { get; }

        public ServiceWrapper(STAThreadPool threadPool) {
            ThreadPool = threadPool;
            Kernel = new();
            Shell = new(ThreadPool);
        }

        public void Dispose() {
            try {
                Shell.Dispose();
            }
            catch (Exception ex) {
                if (Log.Error()) {
                    Log.Error(ex);
                }
            }
            try {
                Kernel.Dispose();
            }
            catch (Exception ex) {
                if (Log.Error()) {
                    Log.Error(ex);
                }
            }
        }
    }
}
