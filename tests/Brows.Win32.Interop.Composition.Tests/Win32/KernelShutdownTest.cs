using Brows.Composition;
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
        var killedField = typeof(Win32InteropServices).GetField(
            "Killed", BindingFlags.Instance | BindingFlags.NonPublic);

        try {
            var killed = SpinWait.SpinUntil(
                () => (bool)killedField.GetValue(Services),
                TimeSpan.FromSeconds(10));
            Assert.That(killed, Is.True, "Shutdown did not enter its lifecycle state.");
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
