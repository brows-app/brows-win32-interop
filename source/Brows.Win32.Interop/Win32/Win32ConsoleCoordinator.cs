using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Brows.Win32;

internal sealed class Win32ConsoleCoordinator {
    private const int StandardInputHandle = -10;
    private const int StandardHandleCount = 3;
    private const int WriteChunkLength = 4096;

    private readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        Locker = new();

    private readonly IWin32ConsoleNative Native;
    private readonly IWin32ConsoleOutput Output;

    private Session Current { get; set; }

    private void CaptureAttachedRestoration(Session session) {
        for (var index = 0; index < session.SavedHandles.Length; index++) {
            if (!session.InstalledHandleKnown[index]) {
                session.RestoreHandles[index] = true;
                continue;
            }
            var handle = Native.GetStandardHandle(StandardInputHandle - index);
            session.RestoreHandles[index] = Native.AreSameHandle(
                session.InstalledHandleIdentities[index],
                session.InstalledHandles[index],
                handle);
        }
    }

    private void CaptureDetachedHandles(Session session) {
        for (var index = 0; index < session.SavedHandles.Length; index++) {
            var shouldCapture = session.RestoreHandles[index] && !session.DetachedHandleKnown[index];
            if (!shouldCapture) {
                continue;
            }
            try {
                var handle = Native.GetStandardHandle(StandardInputHandle - index);
                session.DetachedHandles[index] = handle;
                session.DetachedHandleIdentities[index] = Native.DuplicateStandardHandle(handle);
                session.DetachedHandleKnown[index] = true;
            }
            catch {
                session.RestoreHandles[index] = false;
                session.HandleRestored[index] = true;
                throw;
            }
        }
    }

    private void Cleanup(Session session) {
        session.Active = false;
        if (session.Attached) {
            CaptureAttachedRestoration(session);
        }
        session.Retired = true;
        if (!session.OutRestored) {
            var restore = session.PublishedOut is not null && Output.Out == session.PublishedOut;
            if (restore) {
                Output.Out = session.SavedOut;
            }
            session.OutRestored = true;
        }
        if (!session.ErrorRestored) {
            var restore = session.PublishedError is not null && Output.Error == session.PublishedError;
            if (restore) {
                Output.Error = session.SavedError;
            }
            session.ErrorRestored = true;
        }
        session.Handle?.Dispose();
        if (session.Attached) {
            Native.Free();
            session.Attached = false;
        }
        CaptureDetachedHandles(session);
        for (var index = 0; index < session.SavedHandles.Length; index++) {
            if (session.HandleRestored[index]) {
                continue;
            }
            if (!session.RestoreHandles[index]) {
                session.HandleRestored[index] = true;
                continue;
            }
            if (!session.DetachedHandleKnown[index]) {
                session.HandleRestored[index] = true;
                continue;
            }
            var handle = Native.GetStandardHandle(StandardInputHandle - index);
            var sameObject = Native.AreSameHandle(
                session.DetachedHandleIdentities[index],
                session.DetachedHandles[index],
                handle);
            if (!sameObject) {
                session.HandleRestored[index] = true;
                continue;
            }
            Native.SetStandardHandle(StandardInputHandle - index, session.SavedHandles[index]);
            session.HandleRestored[index] = true;
        }
        DisposeHandleIdentities(session);
        Current = null;
    }

    private void DisposeHandleIdentities(Session session) {
        for (var index = 0; index < session.SavedHandles.Length; index++) {
            session.InstalledHandleIdentities[index]?.Dispose();
            session.DetachedHandleIdentities[index]?.Dispose();
        }
    }

    private void Write(Session session, TextWriter fallback, string text) {
        if (text is null) {
            return;
        }
        lock (Locker) {
            if (!session.Retired) {
                var offset = 0;
                while (offset < text.Length) {
                    var count = Math.Min(WriteChunkLength, text.Length - offset);
                    var splitsSurrogate = offset + count < text.Length
                        && char.IsHighSurrogate(text[offset + count - 1])
                        && char.IsLowSurrogate(text[offset + count]);
                    if (splitsSurrogate) {
                        count--;
                    }
                    var written = Native.WriteOutput(session.Handle, text.Substring(offset, count));
                    var invalidProgress = written <= 0 || written > count;
                    if (invalidProgress) {
                        throw new IOException("The console write did not make valid progress.");
                    }
                    offset += written;
                }
                return;
            }
        }
        /*
         * Application writers can take unrelated locks or reenter the service.
         * Invoke them only after releasing the console lifecycle gate.
         */
        fallback.Write(text);
    }

    internal static Win32ConsoleCoordinator Default { get; } =
        new(new Win32ConsoleNative(), new Win32ConsoleOutput());

    internal Win32ConsoleCoordinator(IWin32ConsoleNative native, IWin32ConsoleOutput output) {
        Native = native ?? throw new ArgumentNullException(nameof(native));
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }

    internal bool ShowConsole() {
        lock (Locker) {
            if (Current is not null) {
                if (Current.Active) {
                    return false;
                }
                throw new InvalidOperationException("Console cleanup must finish before showing another console.");
            }
            var session = new Session(Output.Out, Output.Error);
            for (var index = 0; index < session.SavedHandles.Length; index++) {
                session.SavedHandles[index] = Native.GetStandardHandle(StandardInputHandle - index);
            }
            Native.Allocate();
            Current = session;
            try {
                for (var index = 0; index < session.InstalledHandles.Length; index++) {
                    var installedHandle = Native.GetStandardHandle(StandardInputHandle - index);
                    var installedHandleIdentity = Native.DuplicateStandardHandle(installedHandle);
                    session.InstalledHandles[index] = installedHandle;
                    session.InstalledHandleIdentities[index] = installedHandleIdentity;
                    session.InstalledHandleKnown[index] = true;
                }
                Native.RegisterControlHandler();
                session.Handle = Native.OpenOutput();
                session.PublishedOut = TextWriter.Synchronized(new ConsoleWriter(this, session, session.SavedOut));
                Output.Out = session.PublishedOut;
                session.PublishedOut = Output.Out;
                session.PublishedError = TextWriter.Synchronized(new ConsoleWriter(this, session, session.SavedError));
                Output.Error = session.PublishedError;
                session.PublishedError = Output.Error;
                session.Active = true;
                return true;
            }
            catch (Exception setupError) {
                try {
                    Cleanup(session);
                }
                catch (Exception cleanupError) {
                    throw new AggregateException("Console setup and rollback failed.", setupError, cleanupError);
                }
                throw;
            }
        }
    }

    internal bool FreeConsole() {
        lock (Locker) {
            if (Current is null) {
                return false;
            }
            Cleanup(Current);
            return true;
        }
    }

    private sealed class Session {
        public TextWriter SavedOut { get; }
        public TextWriter SavedError { get; }
        public IntPtr[] SavedHandles { get; } = new IntPtr[StandardHandleCount];
        public IntPtr[] InstalledHandles { get; } = new IntPtr[StandardHandleCount];
        public IntPtr[] DetachedHandles { get; } = new IntPtr[StandardHandleCount];
        public SafeFileHandle[] InstalledHandleIdentities { get; } = new SafeFileHandle[StandardHandleCount];
        public SafeFileHandle[] DetachedHandleIdentities { get; } = new SafeFileHandle[StandardHandleCount];
        public bool[] InstalledHandleKnown { get; } = new bool[StandardHandleCount];
        public bool[] DetachedHandleKnown { get; } = new bool[StandardHandleCount];
        public bool[] RestoreHandles { get; } = new bool[StandardHandleCount];
        public bool[] HandleRestored { get; } = new bool[StandardHandleCount];
        public TextWriter PublishedOut { get; set; }
        public TextWriter PublishedError { get; set; }
        public SafeFileHandle Handle { get; set; }
        public bool Active { get; set; }
        public bool Attached { get; set; } = true;
        public bool Retired { get; set; }
        public bool OutRestored { get; set; }
        public bool ErrorRestored { get; set; }

        public Session(TextWriter savedOut, TextWriter savedError) {
            SavedOut = savedOut;
            SavedError = savedError;
        }
    }

    private sealed class ConsoleWriter : TextWriter {
        private readonly Win32ConsoleCoordinator Coordinator;
        private readonly Session Session;
        private readonly TextWriter Fallback;

        public override Encoding Encoding => Encoding.Unicode;

        public ConsoleWriter(Win32ConsoleCoordinator coordinator, Session session, TextWriter fallback) {
            Coordinator = coordinator;
            Session = session;
            Fallback = fallback;
        }

        public override void Write(string value) {
            Coordinator.Write(Session, Fallback, value);
        }

        public override void Write(char value) {
            Write(value.ToString());
        }

        public override void Write(char[] buffer, int index, int count) {
            Write(new string(buffer, index, count));
        }
    }
}
