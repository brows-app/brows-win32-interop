using Brows.Composition;
using Brows.Threading;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.Win32;

[TestFixture]
public sealed class ShellContextTest {
    [Test]
    public async Task FileOperation_WhenCallerContextIsNotPumped_CompletesAndReleasesShutdown() {
        var services = new Win32InteropServices();
        var pool = new STAThreadPool(nameof(FileOperation_WhenCallerContextIsNotPumped_CompletesAndReleasesShutdown)) {
            WorkerCountMax = 1,
        };
        var context = new RecordingSynchronizationContext();
        using var workerStarted = new ManualResetEventSlim();
        using var releaseWorker = new ManualResetEventSlim();
        Task blocker = null;
        Task<bool> operation = null;
        Task kill = null;
        try {
            await ((IExportAndVary<Win32InteropServicesVariable>)services).Vary(
                new Win32InteropServicesVariable { ThreadPool = pool }, CancellationToken.None);
            blocker = pool.Work(
                name: "BlockBatch",
                work: () => {
                    workerStarted.Set();
                    if (!releaseWorker.Wait(TimeSpan.FromSeconds(10))) {
                        throw new TimeoutException("The queued batch was not released.");
                    }
                },
                cancellationToken: CancellationToken.None);
            Assert.That(workerStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);

            var batch = ((IWin32InteropServices)services).FileOperation(Environment.CurrentDirectory);
            var previousContext = SynchronizationContext.Current;
            try {
                SynchronizationContext.SetSynchronizationContext(context);
                operation = batch.Operate(progress: null, token: CancellationToken.None);
            }
            finally {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
            kill = Task.Factory.StartNew(
                () => ((IExportAndKill)services).Kill(),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            releaseWorker.Set();
            await blocker;
            var shutdown = Task.WhenAll(operation, kill);
            var completion = await Task.WhenAny(shutdown, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.That(
                completion,
                Is.SameAs(shutdown),
                "Batch completion or shutdown waited for the caller's synchronization context.");
            Assert.That(await operation, Is.False);
        }
        finally {
            releaseWorker.Set();
            if (blocker is not null) {
                await blocker;
            }
            if (operation is not null && !operation.IsCompleted) {
                var cleanupDeadline = DateTime.UtcNow.AddSeconds(10);
                while (!operation.IsCompleted && DateTime.UtcNow < cleanupDeadline) {
                    context.RunPostedCallbacks();
                    await Task.Delay(10);
                }
            }
            if (operation is not null) {
                await operation;
            }
            if (kill is null) {
                kill = Task.Factory.StartNew(
                    () => ((IExportAndKill)services).Kill(),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
            var killCompletion = await Task.WhenAny(kill, Task.Delay(TimeSpan.FromSeconds(10)));
            context.RunPostedCallbacks();
            Assert.That(killCompletion, Is.SameAs(kill), "Shutdown did not finish during test cleanup.");
            await kill;
            pool.Empty();
        }
    }

    [Test]
    public void UseServices_WhenInnerTaskCompletesUnderCallerContext_DoesNotPostContinuation() {
        var services = new Win32InteropServices();
        ((IExportAndVary<Win32InteropServicesVariable>)services).Vary(null, CancellationToken.None);
        var innerTask = new TaskCompletionSource<bool>();
        var context = new RecordingSynchronizationContext();
        Task operation = null;

        try {
            var useServices = typeof(Win32InteropServices).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(method => method.Name == "UseServices" && !method.IsGenericMethod);
            var serviceWrapper = typeof(Win32InteropServices).GetNestedType("ServiceWrapper", BindingFlags.NonPublic);
            var callbackType = typeof(Func<,,>).MakeGenericType(
                serviceWrapper, typeof(CancellationToken), typeof(Task));
            var servicesParameter = Expression.Parameter(serviceWrapper);
            var cancellationTokenParameter = Expression.Parameter(typeof(CancellationToken));
            var callback = Expression.Lambda(
                callbackType,
                Expression.Constant(innerTask.Task, typeof(Task)),
                servicesParameter,
                cancellationTokenParameter).Compile();

            var previousContext = SynchronizationContext.Current;
            try {
                SynchronizationContext.SetSynchronizationContext(context);
                operation = (Task)useServices.Invoke(
                    services, new object[] { callback, CancellationToken.None });
            }
            finally {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            innerTask.SetResult(true);
            var outcome = Task.WhenAny(operation, context.CallbackPosted, Task.Delay(TimeSpan.FromSeconds(10)))
                .GetAwaiter().GetResult();
            var completed = ReferenceEquals(outcome, operation);
            context.RunPostedCallbacks();
            Assert.That(completed, Is.True, "The nongeneric adapter posted its continuation to the caller context.");
            Assert.That(operation.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }
        finally {
            innerTask.TrySetResult(true);
            context.RunPostedCallbacks();
            if (operation is not null && !operation.IsCompleted) {
                _ = Task.WhenAny(operation, context.CallbackPosted, Task.Delay(TimeSpan.FromSeconds(10)))
                    .GetAwaiter().GetResult();
                context.RunPostedCallbacks();
            }
            ((IExportAndKill)services).Kill();
        }
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext {
        private ConcurrentQueue<(SendOrPostCallback Callback, object State)> PostedCallbacks { get; } = new();
        private TaskCompletionSource<bool> PostedSignal { get; } = new();

        public Task CallbackPosted => PostedSignal.Task;

        public override void Post(SendOrPostCallback callback, object state) {
            PostedCallbacks.Enqueue((callback, state));
            PostedSignal.TrySetResult(true);
        }

        public void RunPostedCallbacks() {
            while (PostedCallbacks.TryDequeue(out var posted)) {
                posted.Callback(posted.State);
            }
        }
    }
}
