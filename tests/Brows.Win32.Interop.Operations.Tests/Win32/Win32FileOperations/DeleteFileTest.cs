namespace Brows.Win32.Win32FileOperations;

[TestFixture]
public sealed class DeleteFileTest {
    [Test]
    public void With_ChangesNameWithoutChangingOriginal() {
        var original = new DeleteFile { Name = "first.txt" };
        var changed = original with { Name = "second.txt" };

        using (Assert.EnterMultipleScope()) {
            Assert.That(original.Name, Is.EqualTo("first.txt"));
            Assert.That(changed.Name, Is.EqualTo("second.txt"));
            Assert.That(changed, Is.Not.EqualTo(original));
        }
    }
}
