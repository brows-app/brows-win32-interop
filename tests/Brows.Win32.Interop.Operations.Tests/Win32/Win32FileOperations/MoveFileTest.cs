namespace Brows.Win32.Win32FileOperations;

[TestFixture]
public sealed class MoveFileTest {
    [Test]
    public void With_ChangesPathWithoutChangingOriginal() {
        var original = new MoveFile { Path = "first.txt" };
        var changed = original with { Path = "second.txt" };

        using (Assert.EnterMultipleScope()) {
            Assert.That(original.Path, Is.EqualTo("first.txt"));
            Assert.That(changed.Path, Is.EqualTo("second.txt"));
            Assert.That(changed, Is.Not.EqualTo(original));
        }
    }
}
