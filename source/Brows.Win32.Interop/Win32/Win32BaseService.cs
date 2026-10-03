using System;
using System.Threading;

namespace Brows.Win32;

/// <summary>
/// Provides operation tracking and disposal coordination for Windows services.
/// </summary>
/// <remarks>
/// Disposing a service prevents new operations and waits synchronously for active operations to finish
/// before releasing the service's resources.
/// </remarks>
public abstract class Win32BaseService : IDisposable {
    private readonly object DisposeLock = new();

    private int ActiveOperations;
    private bool Disposing;
    private bool Disposed;

    private protected Win32BaseService() {
    }

    private protected void BeginOperation() {
        lock (DisposeLock) {
            if (Disposed || Disposing) {
                throw new ObjectDisposedException(objectName: GetType().Name);
            }
            ActiveOperations++;
        }
    }

    private protected void EndOperation() {
        lock (DisposeLock) {
            ActiveOperations--;
            if (ActiveOperations == 0) {
                Monitor.PulseAll(DisposeLock);
            }
        }
    }

    private protected abstract void DisposeCore();

    /// <summary>
    /// Releases the resources used by this service.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when called by <see cref="Dispose()"/>.
    /// </param>
    /// <remarks>
    /// Managed resources are released only when <paramref name="disposing"/> is <see langword="true"/>.
    /// In that case, active operations are allowed to finish before the service-specific cleanup runs.
    /// </remarks>
    protected virtual void Dispose(bool disposing) {
        if (disposing) {
            lock (DisposeLock) {
                if (Disposed) {
                    return;
                }
                if (Disposing) {
                    while (Disposing) {
                        Monitor.Wait(DisposeLock);
                    }
                    return;
                }
                Disposing = true;
                while (ActiveOperations != 0) {
                    Monitor.Wait(DisposeLock);
                }
            }
            try {
                DisposeCore();
            }
            finally {
                lock (DisposeLock) {
                    Disposed = true;
                    Disposing = false;
                    Monitor.PulseAll(DisposeLock);
                }
            }
        }
    }

    /// <summary>
    /// Stops new operations, waits for active operations to finish, and releases the service's resources.
    /// </summary>
    /// <remarks>
    /// This method blocks until active operations finish. Avoid calling it on a UI thread when an active
    /// operation may need that thread to complete.
    /// </remarks>
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
