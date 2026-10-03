namespace Brows.Win32.Win32FileOperations;

[TestFixture]
public sealed class RenameFileTest {
    [Test]
    public void With_ChangesNewNameAndPreservesOldName() {
        var original = new RenameFile { OldName = "old.txt", NewName = "first.txt" };
        var changed = original with { NewName = "second.txt" };

        using (Assert.EnterMultipleScope()) {
            Assert.That(original.NewName, Is.EqualTo("first.txt"));
            Assert.That(changed.OldName, Is.EqualTo("old.txt"));
            Assert.That(changed.NewName, Is.EqualTo("second.txt"));
            Assert.That(changed, Is.Not.EqualTo(original));
        }
    }
}
