using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Brows.Win32.InteropServices;

internal abstract class ComObjectWrapper<T> : IDisposable where T : class {
    private readonly Lazy<T> LazyComObject;
    private readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        DisposeLocker = new();

    private bool Disposed;

    private T ComObjectFactory() {
        ThrowIfDisposed();
        return Factory();
    }

    private void ThrowIfDisposed() {
#if NET
        ObjectDisposedException.ThrowIf(Disposed, this);
#else
        if (Disposed) {
            throw new ObjectDisposedException(objectName: GetType().Name);
        }
#endif
    }

    protected abstract T Factory();

    protected T ComObject =>
        UseComObject(comObject => comObject);

    protected TResult UseComObject<TResult>(Func<T, TResult> action) {
        if (action is null) {
            throw new ArgumentNullException(nameof(action));
        }
        lock (DisposeLocker) {
            ThrowIfDisposed();
            return action(LazyComObject.Value);
        }
    }

    protected ComObjectWrapper() {
        LazyComObject = new(ComObjectFactory);
    }

    protected virtual void Dispose(bool disposing) {
        if (disposing) {
            var comObject = LazyComObject.IsValueCreated ? LazyComObject.Value : null;
            if (comObject is not null) {
                if (Marshal.IsComObject(comObject)) {
                    var referenceCount = Marshal.FinalReleaseComObject(comObject);
                    if (referenceCount != 0) {
                        /*
                         * TODO: What does this mean?
                         */
                    }
                }
            }
        }
    }

    public void Dispose() {
        lock (DisposeLocker) {
            if (Disposed) {
                return;
            }
            Disposed = true;
        }
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
