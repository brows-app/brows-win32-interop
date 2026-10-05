using Brows.Composition;
using Brows.Threading;
using Domore.Logs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.Win32;

internal sealed class Win32InteropServices : IWin32InteropServices,
                                             IExportAndVary<Win32InteropServicesVariable>,
                                             IExportAndKill {
    private static readonly ILog Log = Logging.For(typeof(Win32InteropServices));

    private readonly Lazy<ServiceWrapper> LazyServices;
    private readonly object Locker = new();

    private bool Killing;
    private bool Killed;
    private bool ThreadPoolOwned;
    private STAThreadPool ThreadPool;
    private int ActiveFacadeOperations;

    private STAThreadPool ThreadPoolNotNull =>
        ThreadPool ?? throw new InvalidOperationException("The STA thread pool is null.");

    private Task<T> UseServices<T>(Func<ServiceWrapper, CancellationToken, Task<T>> function,
                                CancellationToken cancellationToken) {
        if (function is null) {
            throw new ArgumentNullException(nameof(function));
        }
        ServiceWrapper services;
        lock (Locker) {
            if (Killing || Killed) {
                throw new InvalidOperationException("The Win32 interop services have already been killed.");
            }
            services = LazyServices.Value;
            ActiveFacadeOperations++;
        }
        return TrackServicesOperation(services, function, cancellationToken);
    }

    private async Task<T> TrackServicesOperation<T>(ServiceWrapper services,
                                                    Func<ServiceWrapper, CancellationToken, Task<T>> function,
                                                    CancellationToken cancellationToken) {
        try {
            return await function(services, cancellationToken).ConfigureAwait(false);
        }
        finally {
            lock (Locker) {
                ActiveFacadeOperations--;
                Monitor.PulseAll(Locker);
            }
        }
    }

    private Task UseServices(Func<ServiceWrapper, CancellationToken, Task> function,
                             CancellationToken cancellationToken) {
        if (function is null) {
            throw new ArgumentNullException(nameof(function));
        }
        return UseServices(cancellationToken: cancellationToken, function: async (services, cancellationToken) => {
            await function(services, cancellationToken).ConfigureAwait(false);
            return 0;
        });
    }

    public Win32InteropServices() {
        LazyServices = new(() => {
            lock (Locker) {
                if (Killing || Killed) {
                    throw new InvalidOperationException("The Win32 interop services have already been killed.");
                }
                return new(ThreadPoolNotNull);
            }
        });
    }

    Task IExportAndVary<Win32InteropServicesVariable>.Vary(Win32InteropServicesVariable variable,
                                                           CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromCanceled(cancellationToken);
        }
        var threadPool = variable?.ThreadPool;
        lock (Locker) {
            if (Killing || Killed) {
                throw new InvalidOperationException("The Win32 interop services have already been killed.");
            }
            if (LazyServices.IsValueCreated) {
                throw new InvalidOperationException("The Win32 interop services have already been created.");
            }
            ThreadPool = threadPool ?? new(nameof(Win32InteropServices));
            ThreadPoolOwned = ThreadPool != threadPool;
        }
        return Task.CompletedTask;
    }

    void IExportAndKill.Kill() {
        ServiceWrapper services;
        STAThreadPool threadPool;
        bool threadPoolOwned;
        lock (Locker) {
            if (Killed) {
                return;
            }
            if (Killing) {
                while (!Killed) {
                    Monitor.Wait(Locker);
                }
                return;
            }
            Killing = true;
            while (ActiveFacadeOperations > 0) {
                Monitor.Wait(Locker);
            }
            services = LazyServices.IsValueCreated ? LazyServices.Value : null;
            threadPool = ThreadPool;
            threadPoolOwned = ThreadPoolOwned;
        }
        try {
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
                    threadPool?.Empty();
                }
            }
            catch (Exception ex) {
                if (Log.Error()) {
                    Log.Error(ex);
                }
            }
        }
        finally {
            lock (Locker) {
                Killed = true;
                Monitor.PulseAll(Locker);
            }
        }
    }

    IWin32FileOperation IWin32InteropServices.FileOperation(string directory) {
        return new Win32FileOperation(directory, ThreadPoolNotNull);
    }

    Task<bool> IWin32InteropServices.PathsAreEquivalent(string path1,
                                                        string path2,
                                                        CancellationToken cancellationToken) {
        return UseServices(
            cancellationToken: cancellationToken,
            function: (services, cancellationToken) => {
                return Task.Run(() => services.Kernel.PathsAreEquivalent(path1, path2), cancellationToken);
            });
    }

    Task<bool> IWin32InteropServices.PathIsCaseSensitive(string path, CancellationToken cancellationToken) {
        return UseServices(
            cancellationToken: cancellationToken,
            function: (services, cancellationToken) => {
                return Task.Run(() => services.Kernel.PathIsCaseSensitive(path), cancellationToken);
            });
    }

    Task IWin32InteropServices.ExecuteDefault(string file, CancellationToken cancellationToken) {
        return UseServices(
            cancellationToken: cancellationToken,
            function: (services, cancellationToken) => {
                return services.Shell.ExecuteDefault(file, cancellationToken);
            });
    }

    Task IWin32InteropServices.ExecuteDefault(string file, string with, CancellationToken cancellationToken) {
        return UseServices(
            cancellationToken: cancellationToken,
            function: (services, cancellationToken) => {
                return services.Shell.ExecuteDefault(file, with, cancellationToken);
            });
    }

    Task IWin32InteropServices.ExecuteProperties(string file, CancellationToken cancellationToken) {
        return UseServices(
            cancellationToken: cancellationToken,
            function: (services, cancellationToken) => {
                return services.Shell.ExecuteProperties(file, cancellationToken);
            });
    }

    Task<string> IWin32InteropServices.GetLinkPath(string file, CancellationToken cancellationToken) {
        return UseServices(
            cancellationToken: cancellationToken,
            function: (services, cancellationToken) => {
                return services.Shell.GetLinkPath(file, cancellationToken);
            });
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
