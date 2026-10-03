using Brows.Threading;
using Brows.Win32.Win32FileOperations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.Win32;

[TestFixture]
public sealed class Win32FileOperationTest {
    private string TempDirectory;
    private string DestinationDirectory;
    private STAThreadPool ThreadPool;

    [SetUp]
    public void SetUp() {
        TempDirectory = Path.Combine(Path.GetTempPath(), nameof(Win32FileOperationTest), Guid.NewGuid().ToString("N"));
        DestinationDirectory = Path.Combine(TempDirectory, "destination");
        Directory.CreateDirectory(DestinationDirectory);
        ThreadPool = new STAThreadPool(nameof(Win32FileOperationTest));
    }

    [TearDown]
    public void TearDown() {
        ThreadPool?.Empty();
        if (Directory.Exists(TempDirectory)) {
            Directory.Delete(TempDirectory, recursive: true);
        }
    }

    private Win32FileOperation NewOperation() {
        return new Win32FileOperation(DestinationDirectory, ThreadPool) {
            NoConfirmation = true,
            NoErrorUI = true,
            RecycleOnDelete = false,
            Silent = true,
        };
    }

    [Test]
    public void Constructor_WithNullThreadPool_ThrowsArgumentNullException() {
        Assert.That(
            () => new Win32FileOperation(DestinationDirectory, threadPool: null),
            Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("threadPool"));
    }

    [Test]
    public void Constructor_ExposesDirectoryAndThreadPool() {
        var operation = NewOperation();

        using (Assert.EnterMultipleScope()) {
            Assert.That(operation.Directory, Is.EqualTo(DestinationDirectory));
            Assert.That(operation.ThreadPool, Is.SameAs(ThreadPool));
        }
    }

    [Test]
    public void OperationLists_StartEmptyAndCanBeReplaced() {
        var operation = NewOperation();
        var copies = new System.Collections.Generic.List<CopyFile> { new() { Path = "copy.txt" } };
        operation.CopyFiles = copies;

        using (Assert.EnterMultipleScope()) {
            Assert.That(operation.CopyFiles, Is.SameAs(copies));
            Assert.That(operation.MoveFiles, Is.Empty);
            Assert.That(operation.CreateFiles, Is.Empty);
            Assert.That(operation.DeleteFiles, Is.Empty);
            Assert.That(operation.RenameFiles, Is.Empty);
        }
    }

    [Test]
    public void FlagsInit_CombinesTheConfiguredShellFlags() {
        var operation = new Win32FileOperation(DestinationDirectory, ThreadPool);
        var defaultFlags = operation.FlagsInit();
        operation.AddUndoRecord = true;
        operation.AllowUndo = true;
        operation.EarlyFailure = true;
        operation.NoConfirmation = true;
        operation.NoErrorUI = true;
        operation.PreserveFileExtensions = true;
        operation.RenameOnCollision = true;
        operation.Silent = true;
        var enabledFlags = operation.FlagsInit();

        using (Assert.EnterMultipleScope()) {
            Assert.That(operation.RecycleOnDelete, Is.True);
            Assert.That(defaultFlags, Is.EqualTo(0x000C0200u));
            Assert.That(enabledFlags, Is.EqualTo(0x203C065Cu));
        }
    }

    [Test]
    public async Task Operate_WithNoQueuedItems_ReturnsFalse() {
        var operation = NewOperation();

        var performed = await operation.Operate(progress: null, token: CancellationToken.None);

        Assert.That(performed, Is.False);
    }

    [Test]
    public void Operate_WhenTokenAlreadyCanceled_DoesNotRun() {
        var operation = NewOperation();
        operation.CreateFiles.Add(new CreateFile { Name = "not-created.txt", Attributes = FileAttributes.Normal });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using (Assert.EnterMultipleScope()) {
            Assert.That(
                async () => await operation.Operate(progress: null, token: cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(File.Exists(Path.Combine(DestinationDirectory, "not-created.txt")), Is.False);
        }
    }

    [Test]
    public async Task Operate_CopiesFileIntoDestination() {
        var source = Path.Combine(TempDirectory, "source.txt");
        var destination = Path.Combine(DestinationDirectory, "source.txt");
        File.WriteAllText(source, "copied content");
        var operation = NewOperation();
        operation.CopyFiles.Add(new CopyFile { Path = source });

        var performed = await operation.Operate(progress: null, token: CancellationToken.None);

        using (Assert.EnterMultipleScope()) {
            Assert.That(performed, Is.True);
            Assert.That(File.ReadAllText(source), Is.EqualTo("copied content"));
            Assert.That(File.ReadAllText(destination), Is.EqualTo("copied content"));
        }
    }

    [Test]
    public async Task Operate_MovesFileIntoDestination() {
        var source = Path.Combine(TempDirectory, "source.txt");
        var destination = Path.Combine(DestinationDirectory, "source.txt");
        File.WriteAllText(source, "moved content");
        var operation = NewOperation();
        operation.MoveFiles.Add(new MoveFile { Path = source });

        var performed = await operation.Operate(progress: null, token: CancellationToken.None);

        using (Assert.EnterMultipleScope()) {
            Assert.That(performed, Is.True);
            Assert.That(File.Exists(source), Is.False);
            Assert.That(File.ReadAllText(destination), Is.EqualTo("moved content"));
        }
    }

    [Test]
    public async Task Operate_CreatesFileInDestination() {
        var created = Path.Combine(DestinationDirectory, "created.txt");
        var operation = NewOperation();
        operation.CreateFiles.Add(new CreateFile { Name = "created.txt", Attributes = FileAttributes.Normal });

        var performed = await operation.Operate(progress: null, token: CancellationToken.None);

        using (Assert.EnterMultipleScope()) {
            Assert.That(performed, Is.True);
            Assert.That(File.Exists(created), Is.True);
        }
    }

    [Test]
    public async Task Operate_DeletesFileFromDestination() {
        var deleted = Path.Combine(DestinationDirectory, "deleted.txt");
        File.WriteAllText(deleted, "delete me");
        var operation = NewOperation();
        operation.DeleteFiles.Add(new DeleteFile { Name = "deleted.txt" });

        var performed = await operation.Operate(progress: null, token: CancellationToken.None);

        using (Assert.EnterMultipleScope()) {
            Assert.That(performed, Is.True);
            Assert.That(File.Exists(deleted), Is.False);
        }
    }

    [Test]
    public async Task Operate_RenamesFileInDestination() {
        var oldPath = Path.Combine(DestinationDirectory, "old.txt");
        var newPath = Path.Combine(DestinationDirectory, "new.txt");
        File.WriteAllText(oldPath, "renamed content");
        var operation = NewOperation();
        operation.RenameFiles.Add(new RenameFile { OldName = "old.txt", NewName = "new.txt" });

        var performed = await operation.Operate(progress: null, token: CancellationToken.None);

        using (Assert.EnterMultipleScope()) {
            Assert.That(performed, Is.True);
            Assert.That(File.Exists(oldPath), Is.False);
            Assert.That(File.ReadAllText(newPath), Is.EqualTo("renamed content"));
        }
    }
}
