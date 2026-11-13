using Brows.Operations;
using Brows.Win32.PlatformInvoke;
using System.Threading;

namespace Brows.Win32;

[TestFixture]
public sealed class Win32ProgressSinkTest {
    [Test]
    public void UpdateProgress_ReportsWorkPointsWithNoFileSizeScaling() {
        var progress = new Mock<IOperationProgress>();
        var sink = new Win32ProgressSink(progress.Object, CancellationToken.None);

        var result = sink.UpdateProgress(uint.MaxValue, 123U);

        Assert.That(result, Is.EqualTo(HRESULT.S_OK));
        Assert.That(progress.Invocations, Has.Count.EqualTo(1));
        var report = progress.Invocations[0];
        using (Assert.EnterMultipleScope()) {
            Assert.That(report.Method.Name, Is.EqualTo(nameof(IOperationProgress.Change)));
            Assert.That(report.Arguments[1], Is.EqualTo(123L));
            Assert.That(report.Arguments[3], Is.EqualTo((long)uint.MaxValue));
            Assert.That(report.Arguments[8], Is.EqualTo(OperationProgressKind.None));
            Assert.That(sink.CancellationReported, Is.False);
        }
    }

    [Test]
    public void UpdateProgress_WhenTokenIsCanceled_ReturnsCancellationAndRecordsIt() {
        var progress = new Mock<IOperationProgress>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sink = new Win32ProgressSink(progress.Object, cancellation.Token);

        var result = sink.UpdateProgress(2U, 1U);

        using (Assert.EnterMultipleScope()) {
            Assert.That(result, Is.EqualTo(HRESULTExtension.FromWin32(winerror.ERROR_CANCELLED)));
            Assert.That(sink.CancellationReported, Is.True);
            Assert.That(progress.Invocations, Is.Empty);
        }
    }
}
