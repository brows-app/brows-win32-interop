using Brows.Composition;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Brows.Win32;

[TestFixture]
public sealed class Win32InteropServicesTest {
    private Win32InteropServices Services { get; set; }
    private Mock<IWin32ConsoleNative> Native { get; set; }
    private Mock<IWin32ConsoleOutput> Output { get; set; }
    private Win32ConsoleCoordinator Coordinator { get; set; }
    private Dictionary<int, IntPtr> StandardHandles { get; set; }
    private TextWriter OriginalOut { get; set; }
    private TextWriter OriginalError { get; set; }
    private TextWriter CurrentOut { get; set; }
    private TextWriter CurrentError { get; set; }
    private int KernelFactoryCallCount { get; set; }

    [SetUp]
    public void SetUp() {
        StandardHandles = new() {
            [-10] = new IntPtr(10),
            [-11] = new IntPtr(11),
            [-12] = new IntPtr(12),
        };
        OriginalOut = new StringWriter();
        OriginalError = new StringWriter();
        CurrentOut = OriginalOut;
        CurrentError = OriginalError;
        Native = new();
        Native.Setup(native => native.Allocate());
        Native.Setup(native => native.Free());
        Native.Setup(native => native.RegisterControlHandler());
        Native.Setup(native => native.GetStandardHandle(It.IsAny<int>()))
            .Returns((int kind) => StandardHandles[kind]);
        Native.Setup(native => native.DuplicateStandardHandle(It.IsAny<IntPtr>()))
            .Returns((IntPtr handle) => new SafeFileHandle(handle, ownsHandle: false));
        Native.Setup(native => native.AreSameHandle(
                It.IsAny<SafeFileHandle>(),
                It.IsAny<IntPtr>(),
                It.IsAny<IntPtr>()))
            .Returns((SafeFileHandle knownObject, IntPtr knownValue, IntPtr candidate) => knownValue == candidate);
        Native.Setup(native => native.SetStandardHandle(It.IsAny<int>(), It.IsAny<IntPtr>()))
            .Callback<int, IntPtr>((kind, value) => StandardHandles[kind] = value);
        Native.Setup(native => native.OpenOutput())
            .Returns(() => new SafeFileHandle(new IntPtr(100), ownsHandle: false));
        Native.Setup(native => native.WriteOutput(It.IsAny<SafeFileHandle>(), It.IsAny<string>()))
            .Returns((SafeFileHandle handle, string text) => text.Length);
        Output = new();
        Output.SetupGet(output => output.Out).Returns(() => CurrentOut);
        Output.SetupSet(output => output.Out = It.IsAny<TextWriter>())
            .Callback<TextWriter>(writer => CurrentOut = writer);
        Output.SetupGet(output => output.Error).Returns(() => CurrentError);
        Output.SetupSet(output => output.Error = It.IsAny<TextWriter>())
            .Callback<TextWriter>(writer => CurrentError = writer);
        Coordinator = new(Native.Object, Output.Object);
        KernelFactoryCallCount = 0;
        Services = new(() => {
            KernelFactoryCallCount++;
            return new Win32KernelService(Coordinator);
        });
        ((IExportAndVary<Win32InteropServicesVariable>)Services).Vary(null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    [TearDown]
    public void TearDown() {
        try {
            ((IExportAndKill)Services).Kill();
            using var kernel = new Win32KernelService(Coordinator);
            kernel.FreeConsole();
        }
        finally {
            OriginalOut.Dispose();
            OriginalError.Dispose();
        }
    }

    [Test]
    public async Task ConsoleMethods_WhenCalled_ReturnSharedSessionResults() {
        var services = (IWin32InteropServices)Services;

        Assert.That(await services.ShowConsole(), Is.True);
        Assert.That(await services.ShowConsole(), Is.False);
        Assert.That(await services.FreeConsole(), Is.True);
        Assert.That(await services.FreeConsole(), Is.False);

        Native.Verify(native => native.Allocate(), Times.Once);
        Native.Verify(native => native.Free(), Times.Once);
    }

    [Test]
    public void ConsoleMethods_WhenTokenIsCanceled_ReturnCanceledTasksWithoutCreatingServices() {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var services = (IWin32InteropServices)Services;

        var show = services.ShowConsole(cancellation.Token);
        var free = services.FreeConsole(cancellation.Token);

        Assert.That(show.IsCanceled, Is.True);
        Assert.That(free.IsCanceled, Is.True);
        Assert.That(KernelFactoryCallCount, Is.Zero);
        Native.Verify(native => native.Allocate(), Times.Never);
        Native.Verify(native => native.Free(), Times.Never);
    }

    [Test]
    public async Task ShowConsole_WhenCanceledAfterDispatch_CompletesAndKillPreservesSession() {
        using var allocationStarted = new ManualResetEventSlim();
        using var releaseAllocation = new ManualResetEventSlim();
        Native.Setup(native => native.Allocate()).Callback(() => {
            allocationStarted.Set();
            if (!releaseAllocation.Wait(TimeSpan.FromSeconds(10))) {
                throw new TimeoutException("The blocked allocation was not released.");
            }
        });
        using var cancellation = new CancellationTokenSource();
        var services = (IWin32InteropServices)Services;
        var show = services.ShowConsole(cancellation.Token);
        var kill = Task.Factory.StartNew(
            () => ((IExportAndKill)Services).Kill(),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        try {
            Assert.That(allocationStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);
            var killingField = typeof(Win32InteropServices).GetField(
                "Killing",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var killing = SpinWait.SpinUntil(
                () => (bool)killingField.GetValue(Services),
                TimeSpan.FromSeconds(10));
            Assert.That(killing, Is.True, "Shutdown did not enter its lifecycle state.");
            Assert.That(kill.IsCompleted, Is.False, "Shutdown returned while the admitted operation was pending.");
            cancellation.Cancel();
            releaseAllocation.Set();

            Assert.That(await show, Is.True);
            var shutdown = Task.WhenAll(show, kill);
            var completion = await Task.WhenAny(shutdown, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(completion, Is.SameAs(shutdown));
            await kill;

            Native.Verify(native => native.Free(), Times.Never);
            Assert.That(CurrentOut, Is.Not.SameAs(OriginalOut));
            Assert.That(CurrentError, Is.Not.SameAs(OriginalError));

            using var freshKernel = new Win32KernelService(Coordinator);
            Assert.That(freshKernel.FreeConsole(), Is.True);
            Native.Verify(native => native.Free(), Times.Once);
        }
        finally {
            releaseAllocation.Set();
            if (!show.IsCompleted) {
                await show;
            }
            if (!kill.IsCompleted) {
                await kill;
            }
        }
    }

    [Test]
    public void ShowConsole_WhenAllocationFails_PropagatesFailureAndReleasesTracking() {
        Native.Setup(native => native.Allocate()).Throws(new Win32Exception(5));
        var services = (IWin32InteropServices)Services;

        Assert.That(
            async () => await services.ShowConsole(),
            Throws.TypeOf<Win32Exception>());

        var kill = Task.Run(() => ((IExportAndKill)Services).Kill());
        Assert.That(Task.WhenAny(kill, Task.Delay(TimeSpan.FromSeconds(10))).GetAwaiter().GetResult(), Is.SameAs(kill));
        kill.GetAwaiter().GetResult();
    }

    [Test]
    public async Task FreeConsole_WhenNativeReleaseFails_PropagatesFailureAndCanBeRetried() {
        var releaseAttempts = 0;
        Native.Setup(native => native.Free()).Callback(() => {
            releaseAttempts++;
            if (releaseAttempts == 1) {
                throw new Win32Exception(5);
            }
        });
        var services = (IWin32InteropServices)Services;
        Assert.That(await services.ShowConsole(), Is.True);

        Assert.That(
            async () => await services.FreeConsole(),
            Throws.TypeOf<Win32Exception>());

        Assert.That(await services.FreeConsole(), Is.True);
        Native.Verify(native => native.Free(), Times.Exactly(2));
    }

    [Test]
    public void ConsoleMethods_AfterKill_ThrowInvalidOperationException() {
        ((IExportAndKill)Services).Kill();
        var services = (IWin32InteropServices)Services;

        Assert.That(() => services.ShowConsole(), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => services.FreeConsole(), Throws.TypeOf<InvalidOperationException>());
    }
}
