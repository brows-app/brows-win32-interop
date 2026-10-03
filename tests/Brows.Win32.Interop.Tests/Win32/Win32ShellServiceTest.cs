using Brows.Threading;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.Win32;

[TestFixture]
public sealed class Win32ShellServiceTest {
    private string TempDirectory;

    [SetUp]
    public void SetUp() {
        TempDirectory = Path.Combine(Path.GetTempPath(), nameof(Win32ShellServiceTest), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(TempDirectory)) {
            Directory.Delete(TempDirectory, recursive: true);
        }
    }

    [Test]
    public void ExecuteDefault_WhenFileDoesNotExist_ThrowsWin32Exception() {
        var missing = Path.Combine(TempDirectory, "missing.txt");
        using var service = new Win32ShellService(threadPool: null);

        Assert.That(async () => await service.ExecuteDefault(missing), Throws.TypeOf<Win32Exception>());
    }

    [Test]
    public void ExecuteDefault_WithExecutable_WhenTokenIsCanceled_DoesNotLaunch() {
        var file = Path.Combine(TempDirectory, "document.txt");
        var executable = Path.Combine(TempDirectory, "missing.exe");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var service = new Win32ShellService(threadPool: null);

        Assert.That(
            async () => await service.ExecuteDefault(file, executable, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void ExecuteDefault_WithMissingExecutable_ThrowsWin32Exception() {
        var file = Path.Combine(TempDirectory, "document.txt");
        var missingExecutable = Path.Combine(TempDirectory, "missing.exe");
        File.WriteAllText(file, "document");
        using var service = new Win32ShellService(threadPool: null);

        Assert.That(
            async () => await service.ExecuteDefault(file, missingExecutable),
            Throws.TypeOf<Win32Exception>());
    }

    [Test]
    public void ExecuteDefault_WhenTokenIsCanceled_DoesNotLaunch() {
        var missing = Path.Combine(TempDirectory, "missing.txt");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var service = new Win32ShellService(threadPool: null);

        Assert.That(
            async () => await service.ExecuteDefault(missing, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void ExecuteProperties_WhenTokenIsCanceled_DoesNotOpenProperties() {
        var missing = Path.Combine(TempDirectory, "missing.txt");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var service = new Win32ShellService(threadPool: null);

        Assert.That(
            async () => await service.ExecuteProperties(missing, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task GetLinkPath_WhenFileIsNotShortcut_ReturnsNull() {
        var file = Path.Combine(TempDirectory, "document.txt");
        File.WriteAllText(file, "document");
        using var service = new Win32ShellService(threadPool: null);

        var result = await service.GetLinkPath(file, CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetLinkPath_WhenShortcutExists_ReturnsTargetPath() {
        var target = Path.Combine(TempDirectory, "target.txt");
        var shortcutPath = Path.Combine(TempDirectory, "target.lnk");
        File.WriteAllText(target, "target");

        var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true);
        var shellObject = Activator.CreateInstance(shellType);
        object shortcutObject = null;
        try {
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcutPath);
            dynamic shortcut = shortcutObject;
            shortcut.TargetPath = target;
            shortcut.Save();
        }
        finally {
            if (shortcutObject != null) {
                Marshal.FinalReleaseComObject(shortcutObject);
            }
            Marshal.FinalReleaseComObject(shellObject);
        }

        var pool = new STAThreadPool(nameof(GetLinkPath_WhenShortcutExists_ReturnsTargetPath));
        try {
            using var service = new Win32ShellService(pool);
            var result = await service.GetLinkPath(shortcutPath, CancellationToken.None);

            Assert.That(result, Is.EqualTo(target).IgnoreCase);
        }
        finally {
            pool.Empty();
        }
    }

    [Test]
    public void GetLinkPath_WhenTokenIsCanceled_ThrowsOperationCanceledException() {
        var shortcut = Path.Combine(TempDirectory, "missing.lnk");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var service = new Win32ShellService(threadPool: null);

        Assert.That(
            async () => await service.GetLinkPath(shortcut, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void PublicMethods_AfterDispose_ThrowObjectDisposedException() {
        var missing = Path.Combine(TempDirectory, "missing.txt");
        using var service = new Win32ShellService(threadPool: null);
        service.Dispose();

        using (Assert.EnterMultipleScope()) {
            Assert.That(async () => await service.ExecuteDefault(missing),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(async () => await service.ExecuteDefault(missing, missing),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(async () => await service.ExecuteProperties(missing),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(async () => await service.GetLinkPath(missing, CancellationToken.None),
                Throws.TypeOf<ObjectDisposedException>());
        }
    }
}
