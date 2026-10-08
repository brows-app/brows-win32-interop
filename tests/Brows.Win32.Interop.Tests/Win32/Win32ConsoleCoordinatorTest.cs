using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.Win32;

[TestFixture]
public sealed class Win32ConsoleCoordinatorTest {
    private const int StandardInput = -10;
    private const int StandardOutput = -11;
    private const int StandardError = -12;

    private static readonly int[] StandardHandleKinds = {
        StandardInput,
        StandardOutput,
        StandardError,
    };

    private static ConsoleTestContext CreateContext(
        FakeConsoleOutput output = null,
        IWin32ConsoleOutput managedOutput = null) {
        return new ConsoleTestContext(output, managedOutput);
    }

    [Test]
    public void ShowConsole_WhenAllocationFails_ThrowsCapturedErrorWithoutChangingState() {
        var context = CreateContext();
        var originalOut = context.Output.Out;
        var originalError = context.Output.Error;
        var allocationFailure = new Win32Exception(5);
        context.Native.Setup(native => native.Allocate()).Throws(allocationFailure);

        var exception = Assert.Throws<Win32Exception>(() => context.Coordinator.ShowConsole());

        Assert.That(exception, Is.SameAs(allocationFailure));
        Assert.That(exception.NativeErrorCode, Is.EqualTo(5));
        Assert.That(context.Output.Out, Is.SameAs(originalOut));
        Assert.That(context.Output.Error, Is.SameAs(originalError));
        Assert.That(context.NativeHandles, Is.EqualTo(context.OriginalNativeHandles));
        context.Native.Verify(native => native.Free(), Times.Never);
        context.Native.Verify(native => native.RegisterControlHandler(), Times.Never);
        context.Native.Verify(native => native.OpenOutput(), Times.Never);
        context.Native.Verify(
            native => native.SetStandardHandle(It.IsAny<int>(), It.IsAny<IntPtr>()),
            Times.Never);
    }

    [Test]
    public void ShowConsole_WhenSetupFails_RollsBackAndCanBeRetried() {
        var context = CreateContext();
        var originalOut = context.Output.Out;
        var originalError = context.Output.Error;
        var setupFailure = new Win32Exception(123);
        var failFirstOpen = true;
        context.Native.Setup(native => native.OpenOutput()).Returns(() => {
            if (failFirstOpen) {
                failFirstOpen = false;
                throw setupFailure;
            }
            return context.CreateOutputHandle();
        });

        var exception = Assert.Throws<Win32Exception>(() => context.Coordinator.ShowConsole());

        Assert.That(exception, Is.SameAs(setupFailure));
        Assert.That(context.Output.Out, Is.SameAs(originalOut));
        Assert.That(context.Output.Error, Is.SameAs(originalError));
        Assert.That(context.NativeHandles, Is.EqualTo(context.OriginalNativeHandles));
        Assert.That(context.Coordinator.ShowConsole(), Is.True);
        Assert.That(context.Coordinator.FreeConsole(), Is.True);
        context.Native.Verify(native => native.Allocate(), Times.Exactly(2));
        context.Native.Verify(native => native.Free(), Times.Exactly(2));
    }

    [Test]
    public void ShowConsole_WhenManagedErrorWriterAssignmentFails_RestoresOutAndRollsBack() {
        var output = new FakeConsoleOutput();
        var assignmentFailure = new Win32Exception(87);
        var managedOutput = new FailingErrorConsoleOutput(output, assignmentFailure);
        var context = CreateContext(output, managedOutput);
        var originalOut = output.Out;
        var originalError = output.Error;

        var exception = Assert.Throws<Win32Exception>(() => context.Coordinator.ShowConsole());

        Assert.That(exception, Is.SameAs(assignmentFailure));
        Assert.That(output.Out, Is.SameAs(originalOut));
        Assert.That(output.Error, Is.SameAs(originalError));
        Assert.That(context.NativeHandles, Is.EqualTo(context.OriginalNativeHandles));
        context.Native.Verify(native => native.Free(), Times.Once);
        Assert.That(context.Coordinator.ShowConsole(), Is.True);
        Assert.That(context.Coordinator.FreeConsole(), Is.True);
    }

    [Test]
    public void ShowConsole_WhenRollbackFails_RetainsPendingCleanupForRetry() {
        var context = CreateContext();
        var setupFailure = new Win32Exception(123);
        var rollbackFailure = new Win32Exception(6);
        var failFirstOpen = true;
        context.Native.Setup(native => native.OpenOutput()).Returns(() => {
            if (failFirstOpen) {
                failFirstOpen = false;
                throw setupFailure;
            }
            return context.CreateOutputHandle();
        });
        context.FailNextFree(rollbackFailure);

        var exception = Assert.Throws<AggregateException>(() => context.Coordinator.ShowConsole());

        Assert.That(exception.Flatten().InnerExceptions, Does.Contain(setupFailure));
        Assert.That(exception.Flatten().InnerExceptions, Does.Contain(rollbackFailure));
        Assert.That(
            () => context.Coordinator.ShowConsole(),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(context.Coordinator.FreeConsole(), Is.True);
        Assert.That(context.Coordinator.ShowConsole(), Is.True);
        Assert.That(context.Coordinator.FreeConsole(), Is.True);
        context.Native.Verify(native => native.Allocate(), Times.Exactly(2));
        context.Native.Verify(native => native.Free(), Times.Exactly(3));
    }

    [Test]
    public void FreeConsole_WhenNativeDetachFails_RetainsCleanupForRetry() {
        var context = CreateContext();
        Assert.That(context.Coordinator.ShowConsole(), Is.True);
        var outputHandle = context.OpenedOutputHandles[0];
        var detachFailure = new Win32Exception(6);
        context.FailNextFree(detachFailure);

        var exception = Assert.Throws<Win32Exception>(() => context.Coordinator.FreeConsole());

        Assert.That(exception, Is.SameAs(detachFailure));
        Assert.That(outputHandle.IsClosed, Is.True);
        Assert.That(context.Output.Out, Is.SameAs(context.Output.OriginalOut));
        Assert.That(context.Output.Error, Is.SameAs(context.Output.OriginalError));
        Assert.That(
            () => context.Coordinator.ShowConsole(),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(context.Coordinator.FreeConsole(), Is.True);
        Assert.That(context.Coordinator.FreeConsole(), Is.False);
        context.Native.Verify(native => native.Free(), Times.Exactly(2));
        context.Native.Verify(native => native.OpenOutput(), Times.Once);
    }

    [Test]
    public void FreeConsole_WhenStandardHandleRestoreFailsAfterDetach_RetriesOnlyThatRestore() {
        var context = CreateContext();
        Assert.That(context.Coordinator.ShowConsole(), Is.True);
        var restoreFailure = new Win32Exception(6);
        context.FailNextStandardHandleSet(StandardError, restoreFailure);

        var exception = Assert.Throws<Win32Exception>(() => context.Coordinator.FreeConsole());

        Assert.That(exception, Is.SameAs(restoreFailure));
        Assert.That(context.FreeCount, Is.EqualTo(1));
        var setCountsAfterFailure = new Dictionary<int, int>(context.StandardHandleSetCounts);
        Assert.That(context.NativeHandles[StandardInput], Is.EqualTo(context.OriginalNativeHandles[StandardInput]));
        Assert.That(context.NativeHandles[StandardOutput], Is.EqualTo(context.OriginalNativeHandles[StandardOutput]));
        Assert.That(context.NativeHandles[StandardError], Is.Not.EqualTo(context.OriginalNativeHandles[StandardError]));

        Assert.That(context.Coordinator.FreeConsole(), Is.True);

        Assert.That(context.FreeCount, Is.EqualTo(1));
        Assert.That(context.NativeHandles, Is.EqualTo(context.OriginalNativeHandles));
        foreach (var kind in StandardHandleKinds) {
            var expectedSetCount = setCountsAfterFailure[kind] + (kind == StandardError ? 1 : 0);
            Assert.That(context.StandardHandleSetCounts[kind], Is.EqualTo(expectedSetCount));
        }
    }

    [Test]
    public void FreeConsole_PreservesCallerReplacementsAndRestoresOwnedEntries() {
        var context = CreateContext();
        Assert.That(context.Coordinator.ShowConsole(), Is.True);
        var replacementOut = new StringWriter();
        context.Output.Out = replacementOut;
        var replacementNativeOutput = new IntPtr(9001);
        context.SetExternalStandardHandle(StandardOutput, replacementNativeOutput);

        Assert.That(context.Coordinator.FreeConsole(), Is.True);

        Assert.That(context.Output.Out, Is.SameAs(replacementOut));
        Assert.That(context.Output.Error, Is.SameAs(context.Output.OriginalError));
        Assert.That(context.NativeHandles[StandardOutput], Is.EqualTo(replacementNativeOutput));
        Assert.That(context.NativeHandles[StandardInput], Is.EqualTo(context.OriginalNativeHandles[StandardInput]));
        Assert.That(context.NativeHandles[StandardError], Is.EqualTo(context.OriginalNativeHandles[StandardError]));
        context.Native.Verify(native => native.Free(), Times.Once);
    }

    [Test]
    public void ShowConsole_OutputWriterWritesUnicodeAcrossPartialNativeWrites() {
        var context = CreateContext();
        var nativeWrites = new List<string>();
        context.Native.Setup(native => native.WriteOutput(It.IsAny<SafeFileHandle>(), It.IsAny<string>()))
            .Returns<SafeFileHandle, string>((_, text) => {
                var count = Math.Min(257, text.Length);
                nativeWrites.Add(text.Substring(0, count));
                return count;
            });
        Assert.That(context.Coordinator.ShowConsole(), Is.True);
        var value = new string('x', 4095) + "🚀AΩ世界Z" + new string('y', 5000);

        context.Output.Out.Write(value);

        Assert.That(string.Concat(nativeWrites), Is.EqualTo(value));
        Assert.That(nativeWrites.Count, Is.GreaterThan(1));
        Assert.That(context.Coordinator.FreeConsole(), Is.True);
    }

    [Test]
    public void ShowConsole_OutputWriterRejectsNativeWritesThatMakeNoProgress() {
        var context = CreateContext();
        context.Native.Setup(native => native.WriteOutput(It.IsAny<SafeFileHandle>(), It.IsAny<string>()))
            .Returns(0);
        Assert.That(context.Coordinator.ShowConsole(), Is.True);

        Assert.That(
            () => context.Output.Out.Write("console output"),
            Throws.InstanceOf<IOException>());

        Assert.That(context.Coordinator.FreeConsole(), Is.True);
    }

    [Test]
    public void FreeConsole_RetainedWriterUsesSavedFallbackAfterReopen() {
        var context = CreateContext();
        var nativeWriteCount = 0;
        context.Native.Setup(native => native.WriteOutput(It.IsAny<SafeFileHandle>(), It.IsAny<string>()))
            .Callback<SafeFileHandle, string>((_, _) => nativeWriteCount++)
            .Returns<SafeFileHandle, string>((_, text) => text.Length);
        Assert.That(context.Coordinator.ShowConsole(), Is.True);
        var retainedWriter = context.Output.Out;
        Assert.That(context.Coordinator.FreeConsole(), Is.True);
        Assert.That(context.Coordinator.ShowConsole(), Is.True);
        var nativeWriteCountBeforeRetainedWrite = nativeWriteCount;

        retainedWriter.Write("old session");

        Assert.That(context.Output.OriginalOut.ToString(), Does.Contain("old session"));
        Assert.That(nativeWriteCount, Is.EqualTo(nativeWriteCountBeforeRetainedWrite));
        context.Output.Out.Write("current session");
        Assert.That(nativeWriteCount, Is.GreaterThan(nativeWriteCountBeforeRetainedWrite));
        Assert.That(context.RegisterCount, Is.EqualTo(2));
        Assert.That(context.Coordinator.FreeConsole(), Is.True);
    }

    [Test]
    public async Task ShowConsole_ConcurrentCallsShareOneSession() {
        var context = CreateContext();
        using var allocationEntered = new ManualResetEventSlim();
        using var releaseAllocation = new ManualResetEventSlim();
        context.Native.Setup(native => native.Allocate()).Callback(() => {
            allocationEntered.Set();
            if (!releaseAllocation.Wait(TimeSpan.FromSeconds(10))) {
                throw new TimeoutException("The allocation test was not released.");
            }
            context.MarkAllocated();
        });
        using var firstService = new Win32KernelService(context.Coordinator);
        using var secondService = new Win32KernelService(context.Coordinator);
        var firstShow = Task.Run(() => firstService.ShowConsole());
        Assert.That(allocationEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        using var secondCallStarted = new ManualResetEventSlim();
        var secondShow = Task.Run(() => {
            secondCallStarted.Set();
            return secondService.ShowConsole();
        });
        Assert.That(secondCallStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);
        releaseAllocation.Set();

        var completed = Array.Empty<bool>();
        try {
            var allShows = Task.WhenAll(firstShow, secondShow);
            var completedTask = await Task.WhenAny(allShows, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(completedTask, Is.SameAs(allShows));
            completed = await allShows;
        }
        finally {
            releaseAllocation.Set();
        }

        Assert.That(completed, Is.EquivalentTo(new[] { true, false }));
        context.Native.Verify(native => native.Allocate(), Times.Once);
        Assert.That(secondService.FreeConsole(), Is.True);
    }

    [Test]
    public async Task FreeConsole_FromAnotherServiceWaitsForShowToFinish() {
        var context = CreateContext();
        using var allocationEntered = new ManualResetEventSlim();
        using var releaseAllocation = new ManualResetEventSlim();
        context.Native.Setup(native => native.Allocate()).Callback(() => {
            allocationEntered.Set();
            if (!releaseAllocation.Wait(TimeSpan.FromSeconds(10))) {
                throw new TimeoutException("The allocation test was not released.");
            }
            context.MarkAllocated();
        });
        using var showingService = new Win32KernelService(context.Coordinator);
        using var freeingService = new Win32KernelService(context.Coordinator);
        var show = Task.Run(() => showingService.ShowConsole());
        Assert.That(allocationEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        using var freeCallStarted = new ManualResetEventSlim();
        var free = Task.Run(() => {
            freeCallStarted.Set();
            return freeingService.FreeConsole();
        });
        Assert.That(freeCallStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);
        releaseAllocation.Set();

        var results = Array.Empty<bool>();
        try {
            var operations = Task.WhenAll(show, free);
            var completedTask = await Task.WhenAny(operations, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(completedTask, Is.SameAs(operations));
            results = await operations;
        }
        finally {
            releaseAllocation.Set();
        }

        Assert.That(results, Is.EqualTo(new[] { true, true }));
        Assert.That(freeingService.FreeConsole(), Is.False);
        context.Native.Verify(native => native.Allocate(), Times.Once);
        context.Native.Verify(native => native.Free(), Times.Once);
    }

    [Test]
    public async Task Dispose_WaitsForAdmittedConsoleCallWithoutReleasingSession() {
        var context = CreateContext();
        using var allocationEntered = new ManualResetEventSlim();
        using var releaseAllocation = new ManualResetEventSlim();
        context.Native.Setup(native => native.Allocate()).Callback(() => {
            allocationEntered.Set();
            if (!releaseAllocation.Wait(TimeSpan.FromSeconds(10))) {
                throw new TimeoutException("The allocation test was not released.");
            }
            context.MarkAllocated();
        });
        var service = new Win32KernelService(context.Coordinator);
        var show = Task.Run(() => service.ShowConsole());
        Assert.That(allocationEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        using var disposeStarted = new ManualResetEventSlim();
        using var disposeFinished = new ManualResetEventSlim();
        var dispose = Task.Run(() => {
            disposeStarted.Set();
            service.Dispose();
            disposeFinished.Set();
        });
        Assert.That(disposeStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);

        try {
            var disposeReturnedBeforeOperation = disposeFinished.Wait(TimeSpan.FromMilliseconds(100));
            Assert.That(disposeReturnedBeforeOperation, Is.False);
            releaseAllocation.Set();
            var operations = Task.WhenAll(show, dispose);
            var completedTask = await Task.WhenAny(operations, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(completedTask, Is.SameAs(operations));
            await operations;
            Assert.That(await show, Is.True);
        }
        finally {
            releaseAllocation.Set();
        }

        Assert.That(disposeFinished.IsSet, Is.True);
        context.Native.Verify(native => native.Free(), Times.Never);
        context.Output.Out.Write("after service disposal");
        context.Native.Verify(
            native => native.WriteOutput(It.IsAny<SafeFileHandle>(), "after service disposal"),
            Times.Once);
        using var releasingService = new Win32KernelService(context.Coordinator);
        Assert.That(releasingService.FreeConsole(), Is.True);
        context.Native.Verify(native => native.Free(), Times.Once);
    }

    [Test]
    public async Task FreeConsole_WaitsForInFlightNativeWriteBeforeClosingHandle() {
        var context = CreateContext();
        using var writeEntered = new ManualResetEventSlim();
        using var releaseWrite = new ManualResetEventSlim();
        using var nativeFreeEntered = new ManualResetEventSlim();
        context.Native.Setup(native => native.WriteOutput(It.IsAny<SafeFileHandle>(), It.IsAny<string>()))
            .Callback<SafeFileHandle, string>((_, _) => {
                writeEntered.Set();
                if (!releaseWrite.Wait(TimeSpan.FromSeconds(10))) {
                    throw new TimeoutException("The console write test was not released.");
                }
            })
            .Returns<SafeFileHandle, string>((_, text) => text.Length);
        context.Native.Setup(native => native.Free()).Callback(() => nativeFreeEntered.Set());
        using var writingService = new Win32KernelService(context.Coordinator);
        using var freeingService = new Win32KernelService(context.Coordinator);
        Assert.That(writingService.ShowConsole(), Is.True);
        var outputHandle = context.OpenedOutputHandles[0];
        var writer = context.Output.Out;
        var write = Task.Run(() => writer.Write("in flight"));
        Assert.That(writeEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        using var freeCallStarted = new ManualResetEventSlim();
        var free = Task.Run(() => {
            freeCallStarted.Set();
            return freeingService.FreeConsole();
        });
        Assert.That(freeCallStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);

        try {
            var nativeFreeStartedBeforeWriteFinished = nativeFreeEntered.Wait(TimeSpan.FromMilliseconds(100));
            Assert.That(nativeFreeStartedBeforeWriteFinished, Is.False);
        }
        finally {
            releaseWrite.Set();
        }

        var operations = Task.WhenAll(write, free);
        var completedTask = await Task.WhenAny(operations, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.That(completedTask, Is.SameAs(operations));
        await operations;
        Assert.That(await free, Is.True);
        Assert.That(outputHandle.IsClosed, Is.True);
        Assert.That(nativeFreeEntered.IsSet, Is.True);
    }

    [Test]
    public async Task FreeConsole_RetainedWriterCanReenterCoordinatorWithoutHoldingGate() {
        var coordinator = default(Win32ConsoleCoordinator);
        var fallback = new ReentrantTextWriter(() => coordinator.ShowConsole());
        var output = new FakeConsoleOutput(fallback);
        var context = CreateContext(output);
        coordinator = context.Coordinator;
        Assert.That(coordinator.ShowConsole(), Is.True);
        var retainedWriter = output.Out;
        Assert.That(coordinator.FreeConsole(), Is.True);

        var write = Task.Run(() => retainedWriter.Write("reenter"));
        var completedTask = await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(7)));

        Assert.That(completedTask, Is.SameAs(write));
        await write;
        Assert.That(fallback.ReentryResult, Is.True);
        Assert.That(coordinator.FreeConsole(), Is.True);
    }

    private sealed class ConsoleTestContext {
        private readonly Dictionary<int, IntPtr> CurrentStandardHandles = new();
        private readonly Dictionary<int, IntPtr> InitialStandardHandles = new();
        private readonly Dictionary<int, IntPtr> AllocatedStandardHandles = new();
        private readonly Dictionary<int, int> HandleSetCounts = new();
        private readonly Dictionary<int, Win32Exception> StandardHandleSetFailures = new();
        private readonly Queue<Win32Exception> FreeFailures = new();
        private int NextOutputHandle = 1000;

        internal Mock<IWin32ConsoleNative> Native { get; }

        internal FakeConsoleOutput Output { get; }

        internal Win32ConsoleCoordinator Coordinator { get; }

        internal Dictionary<int, IntPtr> NativeHandles => CurrentStandardHandles;

        internal Dictionary<int, IntPtr> OriginalNativeHandles => InitialStandardHandles;

        internal Dictionary<int, int> StandardHandleSetCounts => HandleSetCounts;

        internal List<SafeFileHandle> OpenedOutputHandles { get; } = new();

        internal int FreeCount { get; private set; }

        internal int RegisterCount { get; private set; }

        internal ConsoleTestContext(FakeConsoleOutput output, IWin32ConsoleOutput managedOutput) {
            Native = new Mock<IWin32ConsoleNative>();
            Output = output ?? new FakeConsoleOutput();
            for (var index = 0; index < StandardHandleKinds.Length; index++) {
                var kind = StandardHandleKinds[index];
                InitialStandardHandles.Add(kind, new IntPtr(100 + index));
                AllocatedStandardHandles.Add(kind, new IntPtr(200 + index));
                CurrentStandardHandles.Add(kind, InitialStandardHandles[kind]);
                HandleSetCounts.Add(kind, 0);
            }
            Native.Setup(native => native.GetStandardHandle(It.IsAny<int>()))
                .Returns<int>(kind => CurrentStandardHandles[kind]);
            Native.Setup(native => native.SetStandardHandle(It.IsAny<int>(), It.IsAny<IntPtr>()))
                .Callback<int, IntPtr>((kind, value) => {
                    if (StandardHandleSetFailures.TryGetValue(kind, out var failure)) {
                        StandardHandleSetFailures.Remove(kind);
                        throw failure;
                    }
                    CurrentStandardHandles[kind] = value;
                    HandleSetCounts[kind]++;
                });
            Native.Setup(native => native.Allocate()).Callback(MarkAllocated);
            Native.Setup(native => native.Free()).Callback(() => {
                FreeCount++;
                if (FreeFailures.Count > 0) {
                    throw FreeFailures.Dequeue();
                }
            });
            Native.Setup(native => native.RegisterControlHandler()).Callback(() => RegisterCount++);
            Native.Setup(native => native.OpenOutput()).Returns(CreateOutputHandle);
            Native.Setup(native => native.WriteOutput(It.IsAny<SafeFileHandle>(), It.IsAny<string>()))
                .Returns<SafeFileHandle, string>((_, text) => text.Length);
            Coordinator = new Win32ConsoleCoordinator(Native.Object, managedOutput ?? Output);
        }

        internal void MarkAllocated() {
            foreach (var kind in StandardHandleKinds) {
                CurrentStandardHandles[kind] = AllocatedStandardHandles[kind];
            }
        }

        internal SafeFileHandle CreateOutputHandle() {
            var handle = new SafeFileHandle(new IntPtr(NextOutputHandle++), ownsHandle: false);
            OpenedOutputHandles.Add(handle);
            return handle;
        }

        internal void FailNextFree(Win32Exception exception) {
            FreeFailures.Enqueue(exception);
        }

        internal void FailNextStandardHandleSet(int kind, Win32Exception exception) {
            StandardHandleSetFailures.Add(kind, exception);
        }

        internal void SetExternalStandardHandle(int kind, IntPtr value) {
            CurrentStandardHandles[kind] = value;
        }
    }

    private sealed class FakeConsoleOutput : IWin32ConsoleOutput {
        internal TextWriter OriginalOut { get; }

        internal TextWriter OriginalError { get; }

        internal FakeConsoleOutput(TextWriter originalOut = null) {
            OriginalOut = originalOut ?? new StringWriter();
            OriginalError = new StringWriter();
            Out = OriginalOut;
            Error = OriginalError;
        }

        public TextWriter Out { get; set; }

        public TextWriter Error { get; set; }
    }

    private sealed class FailingErrorConsoleOutput : IWin32ConsoleOutput {
        private readonly FakeConsoleOutput Output;
        private Win32Exception Failure;

        internal FailingErrorConsoleOutput(FakeConsoleOutput output, Win32Exception failure) {
            Output = output;
            Failure = failure;
        }

        public TextWriter Out {
            get => Output.Out;
            set => Output.Out = value;
        }

        public TextWriter Error {
            get => Output.Error;
            set {
                if (Failure is not null) {
                    var failure = Failure;
                    Failure = null;
                    throw failure;
                }
                Output.Error = value;
            }
        }
    }

    private sealed class ReentrantTextWriter : TextWriter {
        private readonly Func<bool> Reenter;

        internal bool ReentryResult { get; private set; }

        internal ReentrantTextWriter(Func<bool> reenter) {
            Reenter = reenter;
        }

        public override Encoding Encoding => Encoding.Unicode;

        public override void Write(string value) {
            var reentry = Task.Run(() => Reenter());
            var completed = Task.WhenAny(reentry, Task.Delay(TimeSpan.FromSeconds(5)))
                .GetAwaiter()
                .GetResult();
            if (completed != reentry) {
                throw new TimeoutException("The fallback writer could not reenter the coordinator.");
            }
            ReentryResult = reentry.GetAwaiter().GetResult();
        }
    }
}
