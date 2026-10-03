using System.IO;

namespace Brows.Win32.Win32FileOperations;

[TestFixture]
public sealed class CreateFileTest {
    [Test]
    public void With_ChangesNameAndPreservesAttributes() {
        var original = new CreateFile { Name = "first", Attributes = FileAttributes.Directory };
        var changed = original with { Name = "second" };

        using (Assert.EnterMultipleScope()) {
            Assert.That(original.Name, Is.EqualTo("first"));
            Assert.That(changed.Name, Is.EqualTo("second"));
            Assert.That(changed.Attributes, Is.EqualTo(FileAttributes.Directory));
            Assert.That(changed, Is.Not.EqualTo(original));
        }
    }
}
