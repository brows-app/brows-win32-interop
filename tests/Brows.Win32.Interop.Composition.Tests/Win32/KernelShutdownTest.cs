using Brows.Composition;
using Brows.Threading;
using Brows.Win32.Win32FileOperations;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.Win32;

[TestFixture, NonParallelizable]
public sealed class KernelShutdownTest {
    private Win32InteropServices Services { get; set; }
    private string TempDirectory { get; set; }

    private static Task<int> UseServices(Win32InteropServices services, Task<int> operation) {
        var serviceWrapperType = typeof(Win32InteropServices).GetNestedType(
            "ServiceWrapper", BindingFlags.NonPublic);
        var delegateType = typeof(Func<,,>).MakeGenericType(
            serviceWrapperType, typeof(CancellationToken), typeof(Task<int>));
        var serviceParameter = Expression.Parameter(serviceWrapperType, "service");
        var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        var function = Expression.Lambda(
            delegateType,
            Expression.Constant(operation, typeof(Task<int>)),
            serviceParameter,
            cancellationTokenParameter).Compile();
        var method = typeof(Win32InteropServices).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "UseServices" && candidate.IsGenericMethodDefinition);
        return (Task<int>)method.MakeGenericMethod(typeof(int)).Invoke(
            services,
            new object[] { function, CancellationToken.None });
    }

    [SetUp]
    public void SetUp() {
        Services = new();
        ((IExportAndVary<Win32InteropServicesVariable>)Services).Vary(null, CancellationToken.None);
        TempDirectory = Path.Combine(Path.GetTempPath(), nameof(KernelShutdownTest), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);
    }

    [TearDown]
    public void TearDown() {
        ((IExportAndKill)Services).Kill();
        if (Directory.Exists(TempDirectory)) {
            Directory.Delete(TempDirectory, recursive: true);
        }
    }

    [Test]
    public async Task Kill_WhenFacadeOperationIsPending_WaitsForItBeforeDisposingServices() {
        var operation = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingOperation = UseServices(Services, operation.Task);
        var kill = Task.Factory.StartNew(
            () => ((IExportAndKill)Services).Kill(),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var killingField = typeof(Win32InteropServices).GetField(
            "Killing", BindingFlags.Instance | BindingFlags.NonPublic);

        try {
            var killing = SpinWait.SpinUntil(
                () => (bool)killingField.GetValue(Services),
                TimeSpan.FromSeconds(10));
            Assert.That(killing, Is.True, "Shutdown did not enter its lifecycle state.");
            var killCompletion = await Task.WhenAny(kill, Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.That(
                killCompletion,
                Is.Not.SameAs(kill),
                "Shutdown returned while an admitted facade operation was pending.");
        }
        finally {
            operation.TrySetResult(1);
            var cleanup = Task.WhenAll(pendingOperation, kill);
            var cleanupCompletion = await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(
                cleanupCompletion,
                Is.SameAs(cleanup),
                "Shutdown did not finish after the operation completed.");
            await cleanup;
        }
    }

    [Test]
    public async Task Kill_WhenCalledConcurrently_EveryCallerWaitsForShutdownToFinish() {
        var operation = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingOperation = UseServices(Services, operation.Task);
        var firstKill = StartKill();
        var secondKill = StartKill();
        var killingField = typeof(Win32InteropServices).GetField(
            "Killing", BindingFlags.Instance | BindingFlags.NonPublic);

        try {
            var killing = SpinWait.SpinUntil(
                () => (bool)killingField.GetValue(Services),
                TimeSpan.FromSeconds(10));
            Assert.That(killing, Is.True, "Shutdown did not enter its lifecycle state.");
            var concurrentCompletion = await Task.WhenAny(
                firstKill,
                secondKill,
                Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.That(
                concurrentCompletion,
                Is.Not.SameAs(firstKill),
                "The first Kill returned while an admitted facade operation was pending.");
            Assert.That(
                concurrentCompletion,
                Is.Not.SameAs(secondKill),
                "A concurrent Kill returned before shutdown finished.");
        }
        finally {
            operation.TrySetResult(1);
            var cleanup = Task.WhenAll(pendingOperation, firstKill, secondKill);
            var cleanupCompletion = await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(
                cleanupCompletion,
                Is.SameAs(cleanup),
                "Shutdown did not finish after the operation completed.");
            await cleanup;
        }

        Task StartKill() {
            return Task.Factory.StartNew(
                () => ((IExportAndKill)Services).Kill(),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    [Test]
    public async Task Kill_WhenFileOperationBatchIsPending_WaitsForItToFinish() {
        var pool = new STAThreadPool(nameof(Win32InteropServices)) {
            WorkerCountMax = 1,
        };
        try {
            Services = new();
            ((IExportAndVary<Win32InteropServicesVariable>)Services).Vary(
                new Win32InteropServicesVariable { ThreadPool = pool },
                CancellationToken.None);
            var source = Path.Combine(TempDirectory, "source.txt");
            File.WriteAllText(source, "source");
            using var workerStarted = new ManualResetEventSlim();
            using var releaseWorker = new ManualResetEventSlim();
            var blocker = pool.Work(
                name: "BlockBatch",
                work: () => {
                    workerStarted.Set();
                    if (!releaseWorker.Wait(TimeSpan.FromSeconds(10))) {
                        throw new TimeoutException("The queued blocker was not released.");
                    }
                },
                cancellationToken: CancellationToken.None);
            Assert.That(workerStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);
            var batch = ((IWin32InteropServices)Services).FileOperation(TempDirectory);
            batch.CopyFiles.Add(new CopyFile { Path = source });
            batch.NoConfirmation = true;
            batch.NoErrorUI = true;
            batch.Silent = true;
            var operate = batch.Operate(progress: null, token: CancellationToken.None);
            var kill = Task.Factory.StartNew(
                () => ((IExportAndKill)Services).Kill(),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            try {
                var killCompletion = await Task.WhenAny(kill, Task.Delay(TimeSpan.FromMilliseconds(500)));
                Assert.That(
                    killCompletion,
                    Is.Not.SameAs(kill),
                    "Shutdown returned while a file-operation batch was pending.");
            }
            finally {
                releaseWorker.Set();
                var cleanup = Task.WhenAll(blocker, operate, kill);
                var cleanupCompletion = await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(15)));
                Assert.That(
                    cleanupCompletion,
                    Is.SameAs(cleanup),
                    "Shutdown did not finish after the batch completed.");
                await cleanup;
            }
        }
        finally {
            pool.Empty();
        }
    }

    [Test]
    public void Operate_WhenServicesWereKilled_ThrowsInvalidOperationException() {
        var services = new Win32InteropServices();
        ((IExportAndVary<Win32InteropServicesVariable>)services).Vary(null, CancellationToken.None);
        var batch = ((IWin32InteropServices)services).FileOperation(TempDirectory);
        ((IExportAndKill)services).Kill();

        Assert.That(
            async () => await batch.Operate(progress: null, token: CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void FileOperation_WhenVaryWasNotCalled_ThrowsInvalidOperationException() {
        var services = new Win32InteropServices();

        Assert.That(
            () => ((IWin32InteropServices)services).FileOperation(TempDirectory),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void FileOperation_WhenVaryWasCalled_ReturnsOperation() {
        var services = new Win32InteropServices();
        ((IExportAndVary<Win32InteropServicesVariable>)services).Vary(null, CancellationToken.None);

        var operation = ((IWin32InteropServices)services).FileOperation(TempDirectory);

        Assert.That(operation, Is.Not.Null);
        ((IExportAndKill)services).Kill();
    }

    [Test]
    public async Task PathsAreEquivalent_WhenPathsIdentifySameDirectory_ReturnsTrue() {
        var result = await ((IWin32InteropServices)Services).PathsAreEquivalent(
            TempDirectory,
            Path.Combine(TempDirectory, "."),
            CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public void PathsAreEquivalent_WhenPathDoesNotExist_ThrowsWin32Exception() {
        var missingPath = Path.Combine(TempDirectory, "missing");

        Assert.That(
            async () => await ((IWin32InteropServices)Services).PathsAreEquivalent(
                missingPath,
                TempDirectory,
                CancellationToken.None),
            Throws.TypeOf<Win32Exception>());
    }

    [Test]
    public void PathsAreEquivalent_WhenTokenIsCanceled_ReturnsCanceledTask() {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await ((IWin32InteropServices)Services).PathsAreEquivalent(
                TempDirectory,
                TempDirectory,
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task PathIsCaseSensitive_WhenDirectoryExists_ReturnsResult() {
        var actualCase = Path.Combine(TempDirectory, "case-probe.txt");
        var differentCase = Path.Combine(TempDirectory, "CASE-PROBE.TXT");
        File.WriteAllText(actualCase, "probe");
        var result = await ((IWin32InteropServices)Services).PathIsCaseSensitive(TempDirectory, CancellationToken.None);

        Assert.That(result, Is.EqualTo(!File.Exists(differentCase)));
    }

    [Test]
    public void PathIsCaseSensitive_WhenPathDoesNotExist_ThrowsWin32Exception() {
        var missingPath = Path.Combine(TempDirectory, "missing");

        Assert.That(
            async () => await ((IWin32InteropServices)Services).PathIsCaseSensitive(
                missingPath,
                CancellationToken.None),
            Throws.TypeOf<Win32Exception>());
    }

    [Test]
    public void PathIsCaseSensitive_WhenTokenIsCanceled_ReturnsCanceledTask() {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () => await ((IWin32InteropServices)Services).PathIsCaseSensitive(
                TempDirectory,
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task PathsAreEquivalent_AfterKill_ThrowsInvalidOperationException() {
        await ((IWin32InteropServices)Services).PathsAreEquivalent(
            TempDirectory,
            TempDirectory,
            CancellationToken.None);
        ((IExportAndKill)Services).Kill();

        Assert.That(
            () => ((IWin32InteropServices)Services).PathsAreEquivalent(
                TempDirectory,
                TempDirectory,
                CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
    }
}
