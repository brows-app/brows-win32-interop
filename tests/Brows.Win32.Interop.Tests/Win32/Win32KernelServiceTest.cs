using System.ComponentModel;
using System.IO;

namespace Brows.Win32;

[TestFixture]
public sealed class Win32KernelServiceTest {
    private string TempDirectory;

    [SetUp]
    public void SetUp() {
        TempDirectory = Path.Combine(Path.GetTempPath(), nameof(Win32KernelServiceTest), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(TempDirectory)) {
            Directory.Delete(TempDirectory, recursive: true);
        }
    }

    [Test]
    public void PathsAreEquivalent_ComparesFileAndDirectoryIdentity() {
        var first = Path.Combine(TempDirectory, "first.txt");
        var second = Path.Combine(TempDirectory, "second.txt");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        using var service = new Win32KernelService();

        using (Assert.EnterMultipleScope()) {
            Assert.That(service.PathsAreEquivalent(first, first), Is.True);
            Assert.That(service.PathsAreEquivalent(first, Path.Combine(TempDirectory, ".", "first.txt")), Is.True);
            Assert.That(service.PathsAreEquivalent(first, second), Is.False);
            Assert.That(service.PathsAreEquivalent(TempDirectory, TempDirectory), Is.True);
        }
    }

    [Test]
    public void PathsAreEquivalent_WhenPathIsMissing_ThrowsWin32Exception() {
        var missing = Path.Combine(TempDirectory, "missing.txt");
        var existing = Path.Combine(TempDirectory, "existing.txt");
        File.WriteAllText(existing, "existing");
        using var service = new Win32KernelService();

        Assert.That(() => service.PathsAreEquivalent(missing, existing), Throws.TypeOf<Win32Exception>());
    }

    [Test]
    public void PathIsCaseSensitive_AgreesWithFileLookupInDirectory() {
        var actualCase = Path.Combine(TempDirectory, "case-probe.txt");
        var differentCase = Path.Combine(TempDirectory, "CASE-PROBE.TXT");
        File.WriteAllText(actualCase, "probe");
        using var service = new Win32KernelService();

        Assert.That(service.PathIsCaseSensitive(TempDirectory), Is.EqualTo(!File.Exists(differentCase)));
    }

    [Test]
    public void PathIsCaseSensitive_WhenPathIsMissing_ThrowsWin32Exception() {
        var missing = Path.Combine(TempDirectory, "missing");
        using var service = new Win32KernelService();

        Assert.That(() => service.PathIsCaseSensitive(missing), Throws.TypeOf<Win32Exception>());
    }

    [Test]
    public void PublicMethods_AfterDispose_ThrowObjectDisposedException() {
        using var service = new Win32KernelService();
        service.Dispose();

        using (Assert.EnterMultipleScope()) {
            Assert.That(() => service.PathsAreEquivalent(TempDirectory, TempDirectory),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => service.PathIsCaseSensitive(TempDirectory),
                Throws.TypeOf<ObjectDisposedException>());
        }
    }
}
