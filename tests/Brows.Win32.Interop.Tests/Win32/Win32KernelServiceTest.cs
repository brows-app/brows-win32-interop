using System.ComponentModel;
using System.IO;

namespace Brows.Win32;

[TestFixture]
public sealed class Win32KernelServiceTest {
    private string TempDirectory;

    private static string GetExtendedPath(string path) {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) {
            return @"\\?\UNC\" + path.Substring(2);
        }
        return @"\\?\" + path;
    }

    [SetUp]
    public void SetUp() {
        TempDirectory = Path.Combine(Path.GetTempPath(), nameof(Win32KernelServiceTest), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(TempDirectory)) {
            Directory.Delete(GetExtendedPath(TempDirectory), recursive: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GetStoredPath_ReturnsStoredFileAndDirectorySpelling(bool directoryOnly) {
        var directory = Path.Combine(TempDirectory, "MixedCaseDirectory");
        var file = Path.Combine(directory, "MixedCaseFile.txt");
        Directory.CreateDirectory(directory);
        File.WriteAllText(file, "probe");
        using var service = new Win32KernelService();
        var query = Path.Combine(TempDirectory, "mixedcasedirectory");
        if (!directoryOnly) {
            query = Path.Combine(query, "mixedcasefile.TXT");
        }
        var expected = Path.Combine(service.GetStoredPath(TempDirectory), "MixedCaseDirectory");
        if (!directoryOnly) {
            expected = Path.Combine(expected, "MixedCaseFile.txt");
        }

        Assert.That(service.GetStoredPath(query), Is.EqualTo(expected));
    }

    [Test, NonParallelizable]
    public void GetStoredPath_WhenRelative_ResolvesAgainstCurrentDirectory() {
        var previousDirectory = Directory.GetCurrentDirectory();
        var file = Path.Combine(TempDirectory, "RelativeFile.txt");
        File.WriteAllText(file, "probe");
        using var service = new Win32KernelService();
        var expected = Path.Combine(service.GetStoredPath(TempDirectory), "RelativeFile.txt");
        try {
            Directory.SetCurrentDirectory(TempDirectory);

            Assert.That(service.GetStoredPath(@".\RelativeFile.txt"), Is.EqualTo(expected));
        }
        finally {
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    [Test]
    public void GetStoredPath_WhenComponentIsMissing_ThrowsWin32Exception() {
        using var service = new Win32KernelService();

        Assert.That(() => service.GetStoredPath(Path.Combine(TempDirectory, "missing", "file.txt")),
            Throws.TypeOf<Win32Exception>());
    }

    [Test]
    public void GetStoredPath_WhenNull_ThrowsArgumentNullException() {
        using var service = new Win32KernelService();

        Assert.That(() => service.GetStoredPath(null), Throws.TypeOf<ArgumentNullException>());
    }

    [TestCase("")]
    [TestCase("*")]
    [TestCase("?")]
    public void GetStoredPath_WhenInvalid_ThrowsArgumentException(string path) {
        using var service = new Win32KernelService();

        Assert.That(() => service.GetStoredPath(path), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void GetStoredPath_WhenRootOnly_ReturnsRoot() {
        var root = Path.GetPathRoot(TempDirectory);
        using var service = new Win32KernelService();

        Assert.That(service.GetStoredPath(root), Is.EqualTo(root));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GetStoredPath_WhenLong_ReturnsStoredPath(bool extendedInput) {
        var directory = TempDirectory;
        while (directory.Length < 350) {
            directory = Path.Combine(directory, "MixedCaseDirectory1234567890");
        }
        var file = Path.Combine(directory, "MixedCaseFile.txt");
        Directory.CreateDirectory(GetExtendedPath(directory));
        File.WriteAllText(GetExtendedPath(file), "probe");
        using var service = new Win32KernelService();
        var expected = service.GetStoredPath(TempDirectory) + file.Substring(TempDirectory.Length);
        var query = file;
        if (extendedInput) {
            query = GetExtendedPath(file);
            expected = GetExtendedPath(expected);
        }

        Assert.That(service.GetStoredPath(query), Is.EqualTo(expected));
    }

    [Test]
    public void GetStoredPath_WhenDriveDevicePath_ReturnsStoredPath() {
        var file = Path.Combine(TempDirectory, "DevicePathFile.txt");
        File.WriteAllText(file, "probe");
        using var service = new Win32KernelService();
        var expected = @"\\.\" + Path.Combine(service.GetStoredPath(TempDirectory), "DevicePathFile.txt");

        Assert.That(service.GetStoredPath(@"\\.\" + file), Is.EqualTo(expected));
    }

    [Test]
    public void GetStoredPath_WhenDriveDeviceRoot_ReturnsRootWithSeparator() {
        var root = @"\\.\" + Path.GetPathRoot(TempDirectory);
        using var service = new Win32KernelService();

        Assert.That(service.GetStoredPath(root), Is.EqualTo(root));
    }

    [TestCase("nul")]
    [TestCase("CON")]
    [TestCase("COM1")]
    [TestCase(@"\\.\PhysicalDrive0")]
    [TestCase(@"\\?\nul")]
    public void GetStoredPath_WhenBareDevice_ThrowsArgumentException(string path) {
        using var service = new Win32KernelService();

        Assert.That(() => service.GetStoredPath(path), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void GetStoredPath_WhenOrdinaryComponentEndsInPeriodAndSpace_NormalizesName() {
        var directory = Path.Combine(TempDirectory, "MixedCaseDirectory");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "File.txt");
        File.WriteAllText(file, "probe");
        using var service = new Win32KernelService();
        var expected = Path.Combine(service.GetStoredPath(TempDirectory), "MixedCaseDirectory", "File.txt");
        var query = Path.Combine(TempDirectory, "MixedCaseDirectory. ", "File.txt");

        Assert.That(service.GetStoredPath(query), Is.EqualTo(expected));
    }

    [TestCase("LiteralFile.")]
    [TestCase("nul")]
    public void GetStoredPath_WhenExtendedNameIsLiteral_PreservesName(string name) {
        var file = GetExtendedPath(Path.Combine(TempDirectory, name));
        File.WriteAllText(file, "probe");
        using var service = new Win32KernelService();
        var expected = GetExtendedPath(Path.Combine(service.GetStoredPath(TempDirectory), name));

        Assert.That(service.GetStoredPath(file), Is.EqualTo(expected));
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
            Assert.That(() => service.GetStoredPath(TempDirectory),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => service.PathIsCaseSensitive(TempDirectory),
                Throws.TypeOf<ObjectDisposedException>());
        }
    }
}
