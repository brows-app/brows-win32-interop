using Brows.Threading;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.Win32;

[TestFixture, NonParallelizable]
public sealed class Win32ShellServiceTest {
    private string TempDirectory;

    private static void CreateShortcut(string shortcutPath, string targetPath) {
        var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true);
        var shellObject = Activator.CreateInstance(shellType);
        var shortcutObject = default(object);
        try {
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcutPath);
            dynamic shortcut = shortcutObject;
            shortcut.TargetPath = targetPath;
            shortcut.Save();
        }
        finally {
            if (shortcutObject != null) {
                Marshal.FinalReleaseComObject(shortcutObject);
            }
            Marshal.FinalReleaseComObject(shellObject);
        }
    }

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
    public void ExecuteDefault_WhenFileDoesNotExist_ReportsShellExecuteResult() {
        var missing = Path.Combine(TempDirectory, "missing.txt");
        using var service = new Win32ShellService(threadPool: null);

        var exception = Assert.ThrowsAsync<Win32Exception>(async () => await service.ExecuteDefault(missing));

        Assert.That(exception.NativeErrorCode, Is.EqualTo(2));
    }

    [Test]
    public void ExecuteDefault_WithInvalidExecutable_ReportsNativeError() {
        var invalidExecutable = Path.Combine(TempDirectory, "invalid.exe");
        File.WriteAllText(invalidExecutable, "This is not an executable.");
        using var service = new Win32ShellService(threadPool: null);

        var exception = Assert.ThrowsAsync<Win32Exception>(async () => await service.ExecuteDefault(invalidExecutable));

        Assert.That(exception.NativeErrorCode, Is.EqualTo(216));
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
    [NonParallelizable]
    public async Task ExecuteDefault_WithExecutable_WhenDocumentPathIsRelative_UsesCallerDirectory() {
        static string CreateMarkerScript(string markerPath, string markerContents) {
            var temporaryMarkerPath = markerPath + ".tmp";
            var escapedMarkerPath = markerPath.Replace("\"", "\"\"");
            var escapedTemporaryMarkerPath = temporaryMarkerPath.Replace("\"", "\"\"");
            return "Set fso = CreateObject(\"Scripting.FileSystemObject\")\r\n" +
                   $"Set marker = fso.CreateTextFile(\"{escapedTemporaryMarkerPath}\", True)\r\n" +
                   $"marker.Write \"{markerContents}\"\r\n" +
                   "marker.Close\r\n" +
                   $"fso.MoveFile \"{escapedTemporaryMarkerPath}\", \"{escapedMarkerPath}\"\r\n";
        }

        var previousDirectory = Environment.CurrentDirectory;
        var callerDirectory = Path.Combine(TempDirectory, "caller directory");
        var executableDirectory = Path.Combine(TempDirectory, "executable directory");
        Directory.CreateDirectory(callerDirectory);
        Directory.CreateDirectory(executableDirectory);

        var documentName = "document with spaces.vbs";
        var markerPath = Path.Combine(TempDirectory, "opened document.txt");
        var callerDocumentPath = Path.Combine(callerDirectory, documentName);
        var conflictingDocumentPath = Path.Combine(executableDirectory, documentName);
        var systemScriptHostPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "wscript.exe");
        var executablePath = Path.Combine(
            executableDirectory, $"wscript host {Guid.NewGuid():N}.exe");

        File.WriteAllText(callerDocumentPath, CreateMarkerScript(markerPath, "caller"));
        File.WriteAllText(conflictingDocumentPath, CreateMarkerScript(markerPath, "executable"));
        File.Copy(systemScriptHostPath, executablePath);

        using var service = new Win32ShellService(threadPool: null);
        async Task OpenDocumentAndWait(string documentPath) {
            var markerCreated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var markerWatcher = new FileSystemWatcher(TempDirectory, Path.GetFileName(markerPath));
            markerWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
            markerWatcher.Created += (_, _) => markerCreated.TrySetResult(true);
            markerWatcher.Changed += (_, _) => markerCreated.TrySetResult(true);
            markerWatcher.Renamed += (_, _) => markerCreated.TrySetResult(true);
            markerWatcher.EnableRaisingEvents = true;

            await service.ExecuteDefault(documentPath, executablePath);

            var completed = await Task.WhenAny(markerCreated.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.That(completed, Is.SameAs(markerCreated.Task), "The child process did not run the document.");

            var childProcesses = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath));
            foreach (var childProcess in childProcesses) {
                using (childProcess) {
                    var exited = await Task.Run(() => childProcess.WaitForExit(15_000));
                    Assert.That(exited, Is.True, "The child process did not exit.");
                }
            }
        }

        try {
            Environment.CurrentDirectory = callerDirectory;
            await OpenDocumentAndWait(documentName);
            Assert.That(File.ReadAllText(markerPath), Is.EqualTo("caller"));

            File.Delete(markerPath);
            await OpenDocumentAndWait(callerDocumentPath);
        }
        finally {
            Environment.CurrentDirectory = previousDirectory;
        }

        Assert.That(File.ReadAllText(markerPath), Is.EqualTo("caller"));
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
        CreateShortcut(shortcutPath, target);

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
    public async Task GetLinkPath_WhenShortcutIsInCurrentDirectory_ReturnsTargetPath() {
        var currentDirectory = Directory.GetCurrentDirectory();
        try {
            Directory.SetCurrentDirectory(TempDirectory);
            var target = Path.Combine(TempDirectory, "target.txt");
            var shortcutPath = Path.Combine(TempDirectory, "target.lnk");
            File.WriteAllText(target, "target");
            CreateShortcut(shortcutPath, target);
            using var service = new Win32ShellService(threadPool: null);

            var result = await service.GetLinkPath("target.lnk", CancellationToken.None);

            Assert.That(result, Is.EqualTo(target).IgnoreCase);
        }
        finally {
            Directory.SetCurrentDirectory(currentDirectory);
        }
    }

    [Test]
    public async Task GetLinkPath_WhenShortcutPathHasRelativeDirectory_ReturnsTargetPath() {
        var currentDirectory = Directory.GetCurrentDirectory();
        var shortcutDirectory = Path.Combine(TempDirectory, "shortcuts");
        Directory.CreateDirectory(shortcutDirectory);
        try {
            Directory.SetCurrentDirectory(TempDirectory);
            var target = Path.Combine(TempDirectory, "target.txt");
            var shortcutPath = Path.Combine(shortcutDirectory, "target.lnk");
            File.WriteAllText(target, "target");
            CreateShortcut(shortcutPath, target);
            using var service = new Win32ShellService(threadPool: null);

            var result = await service.GetLinkPath(
                Path.Combine("shortcuts", "target.lnk"), CancellationToken.None);

            Assert.That(result, Is.EqualTo(target).IgnoreCase);
        }
        finally {
            Directory.SetCurrentDirectory(currentDirectory);
        }
    }

    [Test]
    public async Task GetLinkPath_WhenCurrentDirectoryChangesWhileQueued_UsesCallersDirectory() {
        var currentDirectory = Directory.GetCurrentDirectory();
        var callerDirectory = Path.Combine(TempDirectory, "caller");
        var workerDirectory = Path.Combine(TempDirectory, "worker");
        var callerShortcutDirectory = Path.Combine(callerDirectory, "shortcuts");
        var workerShortcutDirectory = Path.Combine(workerDirectory, "shortcuts");
        Directory.CreateDirectory(callerShortcutDirectory);
        Directory.CreateDirectory(workerShortcutDirectory);
        var callerTarget = Path.Combine(callerDirectory, "target.txt");
        var workerTarget = Path.Combine(workerDirectory, "target.txt");
        File.WriteAllText(callerTarget, "caller target");
        File.WriteAllText(workerTarget, "worker target");
        CreateShortcut(Path.Combine(callerShortcutDirectory, "target.lnk"), callerTarget);
        CreateShortcut(Path.Combine(workerShortcutDirectory, "target.lnk"), workerTarget);

        using var workerStarted = new ManualResetEventSlim();
        using var releaseWorker = new ManualResetEventSlim();
        var pool = new STAThreadPool(
            nameof(GetLinkPath_WhenCurrentDirectoryChangesWhileQueued_UsesCallersDirectory)) {
            WorkerCountMax = 1,
        };
        try {
            var blocker = pool.Work(
                name: "BlockLookup",
                work: () => {
                    workerStarted.Set();
                    if (!releaseWorker.Wait(TimeSpan.FromSeconds(10))) {
                        throw new TimeoutException("The queued shortcut lookup was not released.");
                    }
                },
                cancellationToken: CancellationToken.None);
            Assert.That(workerStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);

            Directory.SetCurrentDirectory(callerDirectory);
            using var service = new Win32ShellService(pool);
            var lookup = service.GetLinkPath(
                Path.Combine("shortcuts", "target.lnk"), CancellationToken.None);

            Directory.SetCurrentDirectory(workerDirectory);
            releaseWorker.Set();
            var blockerCompletion = await Task.WhenAny(blocker, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(blockerCompletion, Is.SameAs(blocker));
            await blocker;
            var lookupCompletion = await Task.WhenAny(lookup, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(lookupCompletion, Is.SameAs(lookup));
            var result = await lookup;

            Assert.That(result, Is.EqualTo(callerTarget).IgnoreCase);
        }
        finally {
            releaseWorker.Set();
            Directory.SetCurrentDirectory(currentDirectory);
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
