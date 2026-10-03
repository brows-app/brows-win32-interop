namespace Brows.Win32;

[TestFixture]
public sealed class Win32BaseServiceTest {
    private sealed class TestService : Win32BaseService {
        public int DisposeCount { get; private set; }

        public void Touch() {
            BeginOperation();
            EndOperation();
        }

        private protected override void DisposeCore() {
            DisposeCount++;
        }
    }

    [Test]
    public void Dispose_WhenCalledMoreThanOnce_ReleasesResourcesOnce() {
        var service = new TestService();

        service.Dispose();
        service.Dispose();

        Assert.That(service.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_PreventsNewOperations() {
        var service = new TestService();
        service.Dispose();

        Assert.That(() => service.Touch(), Throws.TypeOf<ObjectDisposedException>());
    }
}
