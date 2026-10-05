using Brows.Composition;
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
    public void UseServices_WhenInnerTaskCompletesUnderCallerContext_DoesNotPostContinuation() {
        var services = new Win32InteropServices();
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
