using Brows.Operations;

namespace Brows.Win32;

[TestFixture]
public sealed class Win32ProgressSinkTest {
    [Test]
    public void UpdateProgress_ReportsWorkPointsWithNoFileSizeScaling() {
        var progress = new Mock<IOperationProgress>();
        var sink = new Win32ProgressSink(progress.Object, CancellationToken.None);

        var result = typeof(Win32ProgressSink).GetMethod("UpdateProgress").Invoke(sink, [uint.MaxValue, 123U]);

        Assert.That(result.ToString(), Is.EqualTo("S_OK"));
        Assert.That(progress.Invocations, Has.Count.EqualTo(1));
        var report = progress.Invocations[0];
        using (Assert.EnterMultipleScope()) {
            Assert.That(report.Method.Name, Is.EqualTo(nameof(IOperationProgress.Change)));
            Assert.That(report.Arguments[1], Is.EqualTo(123L));
            Assert.That(report.Arguments[3], Is.EqualTo((long)uint.MaxValue));
            Assert.That(report.Arguments[8], Is.EqualTo(OperationProgressKind.None));
        }
    }
}
