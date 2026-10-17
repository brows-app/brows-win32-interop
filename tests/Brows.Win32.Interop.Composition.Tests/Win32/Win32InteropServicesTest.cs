using Brows.Composition;
using Brows.Threading;
using Brows.Win32.Tests;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.Win32;

#if !NETFRAMEWORK
[SupportedOSPlatform("windows")]
#endif
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

    private static async Task<IImport> InitializeImports(
        Win32InteropServicesVariable variable,
        params IExport[] exports) {
        var environment = new Mock<IImportEnvironment>();
        environment.SetupGet(item => item.ImportInfo).Returns(ImportInfo.Listed(
            variables: () => new ImportVariables(variable),
            list: () => exports));
        /*
         * Create an isolated agent because Imports.Init initializes process-wide state only once.
         * Population, variable configuration, and shutdown still run through the real container.
         */
        var agentType = typeof(Imports).Assembly.GetType(
            "Brows.Composition.ImportAgents.ImportAgent", throwOnError: true);
        var imports = (IImport)Activator.CreateInstance(
            agentType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { environment.Object },
            culture: null);
        try {
            var initialize = agentType.GetMethod("Init", BindingFlags.Instance | BindingFlags.Public);
            await ((Task)initialize.Invoke(imports, new object[] { CancellationToken.None }));
            return imports;
        }
        catch {
            imports.Kill();
            throw;
        }
    }

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
        }) {
            ThreadPoolFactory = ThreadPoolFactories.Mock(),
        };
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

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Composition_WhenPoolIsSupplied_UsesCallerOwnedPool(
        bool factoryAvailable,
        bool providerAvailable) {
        var pool = new Mock<ISTAThreadPool>(MockBehavior.Strict);
        pool.Setup(item => item.Work(
                nameof(Win32ShellService.ExecuteDefault), It.IsAny<Action>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        var services = new Win32InteropServices();
        var variable = new Win32InteropServicesVariable { ThreadPool = pool.Object };
        var factory = new Mock<ISTAThreadPoolFactory>(MockBehavior.Strict);
        var provider = new Mock<ISTAThreadPoolProvider>(MockBehavior.Strict);
        var exports = new List<IExport> { services };
        if (factoryAvailable) {
            exports.Add(factory.Object);
        }
        if (providerAvailable) {
            exports.Add(provider.Object);
        }
        var imports = await InitializeImports(variable, exports.ToArray());
        try {
            var exported = imports.Find<IWin32InteropServices>(throwIfNotFound: true);
            await exported.ExecuteDefault("unused");
            pool.Verify(item => item.Work(
                nameof(Win32ShellService.ExecuteDefault), It.IsAny<Action>(), CancellationToken.None), Times.Once);
            Assert.That(services.ThreadPoolFactory, Is.SameAs(factoryAvailable ? factory.Object : null));
            Assert.That(services.ThreadPoolProvider, Is.SameAs(providerAvailable ? provider.Object : null));
        }
        finally {
            imports.Kill();
        }
        pool.Verify(item => item.Empty(), Times.Never);
        provider.Verify(item => item.GetThreadPool(It.IsAny<object>()), Times.Never);
        factory.Verify(item => item.CreateThreadPool(
            It.IsAny<string>(),
            It.IsAny<STAThreadPoolOptions>()),
            Times.Never);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Composition_WhenProviderReturnsPool_UsesExternallyOwnedPool(bool factoryAvailable) {
        var pool = new Mock<ISTAThreadPool>(MockBehavior.Strict);
        pool.Setup(item => item.Work(
                nameof(Win32ShellService.ExecuteDefault), It.IsAny<Action>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        var services = new Win32InteropServices();
        var provider = new Mock<ISTAThreadPoolProvider>(MockBehavior.Strict);
        provider.Setup(item => item.GetThreadPool(services)).Returns(pool.Object);
        var factory = new Mock<ISTAThreadPoolFactory>(MockBehavior.Strict);
        IExport[] exports = factoryAvailable
            ? [services, provider.Object, factory.Object]
            : [services, provider.Object];
        var imports = await InitializeImports(new Win32InteropServicesVariable(), exports);
        try {
            await imports.Find<IWin32InteropServices>(throwIfNotFound: true).ExecuteDefault("unused");
            pool.Verify(item => item.Work(
                nameof(Win32ShellService.ExecuteDefault), It.IsAny<Action>(), CancellationToken.None), Times.Once);
            Assert.That(services.ThreadPoolProvider, Is.SameAs(provider.Object));
        }
        finally {
            imports.Kill();
        }
        provider.Verify(item => item.GetThreadPool(services), Times.Once);
        factory.Verify(item => item.CreateThreadPool(
            It.IsAny<string>(), It.IsAny<STAThreadPoolOptions>()), Times.Never);
        pool.Verify(item => item.Empty(), Times.Never);
    }

    [Test]
    public async Task Composition_WhenProviderReturnsNull_FallsBackToOwnedFactoryPool() {
        var pool = new Mock<ISTAThreadPool>(MockBehavior.Strict);
        pool.Setup(item => item.Work(
                nameof(Win32ShellService.ExecuteDefault), It.IsAny<Action>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        pool.Setup(item => item.Empty());
        var services = new Win32InteropServices();
        var provider = new Mock<ISTAThreadPoolProvider>(MockBehavior.Strict);
        provider.Setup(item => item.GetThreadPool(services)).Returns((ISTAThreadPool)null);
        var factory = new Mock<ISTAThreadPoolFactory>(MockBehavior.Strict);
        factory.Setup(item => item.CreateThreadPool(nameof(Win32InteropServices), It.IsAny<STAThreadPoolOptions>()))
            .Returns(pool.Object);
        var imports = await InitializeImports(
            new Win32InteropServicesVariable(), services, provider.Object, factory.Object);
        try {
            await imports.Find<IWin32InteropServices>(throwIfNotFound: true).ExecuteDefault("unused");
            pool.Verify(item => item.Work(
                nameof(Win32ShellService.ExecuteDefault), It.IsAny<Action>(), CancellationToken.None), Times.Once);
        }
        finally {
            imports.Kill();
        }
        provider.Verify(item => item.GetThreadPool(services), Times.Once);
        factory.Verify(item => item.CreateThreadPool(
            nameof(Win32InteropServices), It.IsAny<STAThreadPoolOptions>()), Times.Once);
        pool.Verify(item => item.Empty(), Times.Once);
    }

    [Test]
    public void Composition_WhenProviderReturnsNullAndFactoryIsMissing_ReportsMissingPool() {
        var services = new Win32InteropServices();
        var provider = new Mock<ISTAThreadPoolProvider>(MockBehavior.Strict);
        provider.Setup(item => item.GetThreadPool(services)).Returns((ISTAThreadPool)null);

        Assert.That(
            async () => await InitializeImports(new Win32InteropServicesVariable(), services, provider.Object),
            Throws.TypeOf<TargetInvocationException>()
                .With.InnerException.Message.EqualTo("Unable to create an STA thread pool."));

        provider.Verify(item => item.GetThreadPool(services), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Vary_WhenImportsAreAlreadyAvailable_SelectsPoolAndPreservesOwnership(bool providerReturnsPool) {
        var pool = new Mock<ISTAThreadPool>(MockBehavior.Strict);
        pool.Setup(item => item.Work(
                nameof(Win32ShellService.ExecuteDefault), It.IsAny<Action>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        pool.Setup(item => item.Empty());
        var provider = new Mock<ISTAThreadPoolProvider>(MockBehavior.Strict);
        var factory = new Mock<ISTAThreadPoolFactory>(MockBehavior.Strict);
        var services = new Win32InteropServices {
            ThreadPoolProvider = provider.Object,
            ThreadPoolFactory = factory.Object,
        };
        provider.Setup(item => item.GetThreadPool(services)).Returns(providerReturnsPool ? pool.Object : null);
        if (!providerReturnsPool) {
            factory.Setup(item => item.CreateThreadPool(nameof(Win32InteropServices), It.IsAny<STAThreadPoolOptions>()))
                .Returns(pool.Object);
        }
        try {
            await ((IExportAndVary<Win32InteropServicesVariable>)services).Vary(null, CancellationToken.None);
            await ((IWin32InteropServices)services).ExecuteDefault("unused");
            pool.Verify(item => item.Work(
                nameof(Win32ShellService.ExecuteDefault), It.IsAny<Action>(), CancellationToken.None), Times.Once);
        }
        finally {
            ((IExportAndKill)services).Kill();
        }
        provider.Verify(item => item.GetThreadPool(services), Times.Once);
        factory.Verify(item => item.CreateThreadPool(
            nameof(Win32InteropServices), It.IsAny<STAThreadPoolOptions>()),
            providerReturnsPool ? Times.Never() : Times.Once());
        pool.Verify(item => item.Empty(), providerReturnsPool ? Times.Never() : Times.Once());
    }

    [Test]
    public async Task Composition_WhenFactoryCreatesPool_EmptiesOwnedPoolOnShutdown() {
        var pool = new Mock<ISTAThreadPool>(MockBehavior.Strict);
        pool.Setup(item => item.Work(
                nameof(Win32ShellService.ExecuteDefault), It.IsAny<Action>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        pool.Setup(item => item.Empty());
        var factory = new Mock<ISTAThreadPoolFactory>(MockBehavior.Strict);
        factory.Setup(item => item.CreateThreadPool(nameof(Win32InteropServices), It.IsAny<STAThreadPoolOptions>()))
            .Returns(pool.Object);
        var services = new Win32InteropServices();
        var imports = await InitializeImports(new Win32InteropServicesVariable(), services, factory.Object);
        try {
            var exported = imports.Find<IWin32InteropServices>(throwIfNotFound: true);
            await exported.ExecuteDefault("unused");
            pool.Verify(item => item.Work(
                nameof(Win32ShellService.ExecuteDefault), It.IsAny<Action>(), CancellationToken.None), Times.Once);
            Assert.That(services.ThreadPoolFactory, Is.SameAs(factory.Object));
        }
        finally {
            imports.Kill();
        }
        factory.Verify(
            item => item.CreateThreadPool(
                nameof(Win32InteropServices), It.IsAny<STAThreadPoolOptions>()), Times.Once);
        pool.Verify(item => item.Empty(), Times.Once);
    }

    [Test]
    public void Composition_WhenNeitherPoolNorFactoryIsSupplied_ReportsMissingPool() {
        var services = new Win32InteropServices();
        Assert.That(
            async () => await InitializeImports(new Win32InteropServicesVariable(), services),
            Throws.TypeOf<TargetInvocationException>()
                .With.InnerException.Message.EqualTo("Unable to create an STA thread pool."));
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
