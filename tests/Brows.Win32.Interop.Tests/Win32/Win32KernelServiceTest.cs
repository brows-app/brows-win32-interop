using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace Brows.Win32;

[TestFixture]
public sealed class Win32KernelServiceTest {
    private const int ChildTimeoutMilliseconds = 30000;

    private string TempDirectory;

    private static string GetExtendedPath(string path) {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) {
            return @"\\?\UNC\" + path.Substring(2);
        }
        return @"\\?\" + path;
    }

    private static ConsoleTestContext CreateConsoleContext() {
        return new ConsoleTestContext();
    }

    private sealed record ChildResult {
        public IReadOnlyDictionary<string, string> Values { get; }
        public int ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public string Diagnostics { get; }

        public ChildResult(
            IReadOnlyDictionary<string, string> values,
            int exitCode,
            string standardOutput,
            string standardError,
            string diagnostics) {
            Values = values;
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
            Diagnostics = diagnostics;
        }
    }

    private static string GetRequiredValue(ChildResult result, string key) {
        if (!result.Values.TryGetValue(key, out var value)) {
            throw new AssertionException($"The console helper did not report '{key}'. {result.Diagnostics}");
        }
        return value;
    }

    private static bool GetBoolean(ChildResult result, string key) {
        var value = GetRequiredValue(result, key);
        if (!bool.TryParse(value, out var parsed)) {
            throw new AssertionException($"The console helper reported an invalid boolean for '{key}': {value}.");
        }
        return parsed;
    }

    private static ChildResult RunHelper(string scenario, bool redirectStandardStreams = false) {
        var pipeName = "Brows.Win32.Interop.Tests.ConsoleHost." + Guid.NewGuid().ToString("N");
        var helperPath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "ConsoleHost",
            "Brows.Win32.Interop.Tests.ConsoleHost.exe");
        if (!File.Exists(helperPath)) {
            throw new FileNotFoundException(
                "The console integration helper was not copied to the test output.",
                helperPath);
        }

        using (var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous)) {
            var startInfo = new ProcessStartInfo(helperPath) {
                Arguments = $"\"{scenario}\" \"{pipeName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(helperPath)
            };
            if (redirectStandardStreams) {
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
            }

            using (var process = new Process { StartInfo = startInfo }) {
                if (!process.Start()) {
                    throw new InvalidOperationException("The console integration helper could not be started.");
                }

                Task<string> outputTask = null;
                Task<string> errorTask = null;
                if (redirectStandardStreams) {
                    outputTask = process.StandardOutput.ReadToEndAsync();
                    errorTask = process.StandardError.ReadToEndAsync();
                }

                var connected = Task.Run(() => pipe.WaitForConnection());
                if (!connected.Wait(ChildTimeoutMilliseconds)) {
                    KillChild(process);
                    throw new TimeoutException("The console integration helper did not connect to its status pipe.");
                }

                var reportTask = Task.Run(() => {
                    using (var reader = new StreamReader(pipe, new UTF8Encoding(false))) {
                        return reader.ReadToEnd();
                    }
                });
                if (!process.WaitForExit(ChildTimeoutMilliseconds)) {
                    KillChild(process);
                    throw new TimeoutException($"The console integration helper scenario '{scenario}' timed out.");
                }
                if (!reportTask.Wait(5000)) {
                    throw new TimeoutException("The console integration helper did not close its status pipe.");
                }
                if (redirectStandardStreams) {
                    var standardStreamsDidNotClose = !outputTask.Wait(5000) || !errorTask.Wait(5000);
                    if (standardStreamsDidNotClose) {
                        throw new TimeoutException("The redirected standard streams did not close.");
                    }
                }

                var standardOutput = redirectStandardStreams ? outputTask.Result : string.Empty;
                var standardError = redirectStandardStreams ? errorTask.Result : string.Empty;
                var values = ParseReport(reportTask.Result);
                var diagnostics = $"exit code {process.ExitCode}; stdout: {standardOutput}; stderr: {standardError}";
                var result = new ChildResult(values, process.ExitCode, standardOutput, standardError, diagnostics);
                if (process.ExitCode != 0) {
                    throw new AssertionException(
                        $"The console integration helper scenario '{scenario}' failed. {diagnostics} " +
                        $"Reported exception: {GetOptionalValue(result, "exception")}");
                }
                return result;
            }
        }
    }

    private static string GetOptionalValue(ChildResult result, string key) {
        return result.Values.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static Dictionary<string, string> ParseReport(string report) {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = report.Split(new[] { (char)13, (char)10 }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines) {
            var separator = line.IndexOf('\t');
            if (separator <= 0) {
                continue;
            }
            var key = line.Substring(0, separator);
            var encoded = line.Substring(separator + 1);
            values[key] = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        return values;
    }

    private static void KillChild(Process process) {
        try {
            if (!process.HasExited) {
                process.Kill();
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException) {
        }
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

    [Test]
    public void ConsoleSession_RemainsUsableAfterServiceDisposalAndCanBeFreedByAnotherService() {
        var context = CreateConsoleContext();
        using var initiatingService = new Win32KernelService(context.Coordinator);
        using var disposingService = new Win32KernelService(context.Coordinator);
        using var releasingService = new Win32KernelService(context.Coordinator);
        Assert.That(initiatingService.ShowConsole(), Is.True);
        var routedOut = context.Output.Out;

        initiatingService.Dispose();
        disposingService.Dispose();

        context.Native.Verify(native => native.Free(), Times.Never);
        context.Native.Verify(native => native.RegisterControlHandler(), Times.Once);
        Assert.That(context.Output.Out, Is.SameAs(routedOut));
        context.Output.Out.Write("still routed");
        context.Native.Verify(
            native => native.WriteOutput(It.IsAny<SafeFileHandle>(), "still routed"),
            Times.Once);

        Assert.That(releasingService.FreeConsole(), Is.True);

        Assert.That(context.Output.Out, Is.SameAs(context.Output.OriginalOut));
        Assert.That(context.Output.Error, Is.SameAs(context.Output.OriginalError));
        context.Native.Verify(native => native.Free(), Times.Once);
        Assert.That(
            () => initiatingService.ShowConsole(),
            Throws.TypeOf<ObjectDisposedException>());
        Assert.That(
            () => initiatingService.FreeConsole(),
            Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void ConsoleMethods_AfterDispose_ThrowObjectDisposedException() {
        var context = CreateConsoleContext();
        using var service = new Win32KernelService(context.Coordinator);
        service.Dispose();

        using (Assert.EnterMultipleScope()) {
            Assert.That(() => service.ShowConsole(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => service.FreeConsole(), Throws.TypeOf<ObjectDisposedException>());
        }

        context.Native.Verify(native => native.Allocate(), Times.Never);
        context.Native.Verify(native => native.Free(), Times.Never);
    }

    private sealed class ConsoleTestContext {
        internal Mock<IWin32ConsoleNative> Native { get; }

        internal FakeConsoleOutput Output { get; }

        internal Win32ConsoleCoordinator Coordinator { get; }

        internal ConsoleTestContext() {
            Native = new Mock<IWin32ConsoleNative>();
            Output = new FakeConsoleOutput();
            Native.Setup(native => native.GetStandardHandle(It.IsAny<int>()))
                .Returns<int>(kind => new IntPtr(-kind));
            Native.Setup(native => native.SetStandardHandle(It.IsAny<int>(), It.IsAny<IntPtr>()));
            Native.Setup(native => native.Allocate());
            Native.Setup(native => native.Free());
            Native.Setup(native => native.RegisterControlHandler());
            Native.Setup(native => native.OpenOutput())
                .Returns(new SafeFileHandle(new IntPtr(1200), ownsHandle: false));
            Native.Setup(native => native.WriteOutput(It.IsAny<SafeFileHandle>(), It.IsAny<string>()))
                .Returns<SafeFileHandle, string>((_, text) => text.Length);
            Coordinator = new Win32ConsoleCoordinator(Native.Object, Output);
        }
    }

    private sealed class FakeConsoleOutput : IWin32ConsoleOutput {
        internal TextWriter OriginalOut { get; }

        internal TextWriter OriginalError { get; }

        internal FakeConsoleOutput() {
            OriginalOut = new StringWriter();
            OriginalError = new StringWriter();
            Out = OriginalOut;
            Error = OriginalError;
        }

        public TextWriter Out { get; set; }

        public TextWriter Error { get; set; }
    }

    [Test]
    public void ShowConsole_DisposeAndFreshService_Free_PreservesOutputAndStartsFreshBuffer() {
        var result = RunHelper("lifecycle");

        Assert.That(GetBoolean(result, "cached_writers_read_before_show"), Is.True);
        Assert.That(GetBoolean(result, "first_show"), Is.True);
        Assert.That(GetBoolean(result, "second_show"), Is.False);
        Assert.That(GetRequiredValue(result, "buffer_after_dispose"), Does.Contain("console-lifecycle-π-雪"));
        Assert.That(GetRequiredValue(result, "buffer_after_dispose"), Does.Contain("console-error-π-雪"));
        Assert.That(GetRequiredValue(result, "buffer_after_dispose"), Does.Contain("after-service-dispose-π"));
        Assert.That(GetRequiredValue(result, "buffer_after_dispose"), Does.Contain("error-after-service-dispose-雪"));
        Assert.That(GetBoolean(result, "released_by_fresh_service"), Is.True);
        Assert.That(GetBoolean(result, "released_again"), Is.False);
        Assert.That(GetBoolean(result, "reopened"), Is.True);
        Assert.That(GetRequiredValue(result, "buffer_after_reopen"), Does.Contain("console-reopened-雪"));
        Assert.That(GetRequiredValue(result, "buffer_after_reopen"), Does.Not.Contain("console-lifecycle-π-雪"));
        Assert.That(GetBoolean(result, "released_reopened"), Is.True);
    }

    [Test]
    public void ShowConsole_WhenAnotherConsoleIsAlreadyAssociated_ThrowsAndPreservesIt() {
        var result = RunHelper("existing-console");

        Assert.That(GetRequiredValue(result, "show_error"), Is.EqualTo(typeof(Win32Exception).FullName));
        Assert.That(GetBoolean(result, "console_association_preserved"), Is.True);
        Assert.That(GetBoolean(result, "output_handle_preserved"), Is.True);
        Assert.That(GetBoolean(result, "error_handle_preserved"), Is.True);
        var bufferBeforeShow = GetRequiredValue(result, "buffer_before_show");
        Assert.That(bufferBeforeShow, Does.Contain("preexisting-console-buffer-marker"));
        Assert.That(GetRequiredValue(result, "buffer_after_show"), Is.EqualTo(bufferBeforeShow));
        Assert.That(GetRequiredValue(result, "buffer_after_free"), Is.EqualTo(bufferBeforeShow));
        Assert.That(GetBoolean(result, "free_without_owned_session"), Is.False);
        Assert.That(GetBoolean(result, "association_preserved_after_free"), Is.True);
        Assert.That(GetBoolean(result, "native_free"), Is.True);
    }

    [Test]
    public void ShowAndFreeConsole_WhenStandardStreamsAreRedirected_RoutesAndRestoresThem() {
        var result = RunHelper("redirection", redirectStandardStreams: true);

        Assert.That(GetBoolean(result, "shown"), Is.True);
        Assert.That(GetRequiredValue(result, "buffer"), Does.Contain("redirect-console-buffer-marker"));
        Assert.That(GetRequiredValue(result, "buffer"), Does.Contain("redirect-console-error-marker"));
        Assert.That(GetBoolean(result, "released"), Is.True);
        Assert.That(result.StandardOutput, Does.Contain("redirect-before-show-out"));
        Assert.That(result.StandardOutput, Does.Contain("redirect-after-free-out"));
        Assert.That(result.StandardError, Does.Contain("redirect-before-show-error"));
        Assert.That(result.StandardError, Does.Contain("redirect-after-free-error"));
    }

    [Test]
    public void ConsoleControlHandler_ConsumesControlEventsInIsolatedChildProcess() {
        var result = RunHelper("control-events");

        Assert.That(GetBoolean(result, "shown"), Is.True);
        Assert.That(GetBoolean(result, "ctrl_c_generated"), Is.True);
        Assert.That(GetBoolean(result, "ctrl_c_observed"), Is.True);
        Assert.That(GetBoolean(result, "ctrl_break_generated"), Is.True);
        Assert.That(GetBoolean(result, "ctrl_break_observed"), Is.True);
        Assert.That(GetRequiredValue(result, "buffer"), Does.Contain("alive-after-ctrl-c"));
        Assert.That(GetRequiredValue(result, "buffer"), Does.Contain("alive-after-ctrl-break"));
        Assert.That(GetBoolean(result, "observer_removed"), Is.True);
        Assert.That(GetBoolean(result, "released"), Is.True);
    }
}
