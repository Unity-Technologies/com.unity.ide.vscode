using NUnit.Framework;

namespace com.unity.ide.vscode.tests
{
    public class UtilityTests
    {
        [TestCase("", "")]
        [TestCase(".gz", ".gz")]
        [TestCase("foo.", ".")]
        [TestCase("foo.tar.gz", ".gz")]
        [TestCase("hello", "")]
        [TestCase("hello.txt", ".txt")]
        [TestCase("../foo/bar/hello.txt", ".txt")]
        [TestCase(@"c:\hello", "")]
        public void GetExtension(string path, string result)
        {
            Utility.GetExtension(path, out var start);
            Assert.That(path.Substring(start), Is.EqualTo(result));
        }

        [TestCase("", "")]
        [TestCase("foo.", "foo")]
        [TestCase("foo.tar.gz", "foo.tar")]
        [TestCase("hello.txt", "hello")]
        [TestCase("../foo/bar/hello.txt", "hello")]
        [TestCase(@"c:\hello", "hello")]
        public void GetFileNameWithoutExtension(string path, string result)
        {
            Utility.GetFileNameWithoutExtension(path, out var start, out var end);
            Assert.That(path.Substring(start, end - start), Is.EqualTo(result));
        }

        [TestCase("", ".txt", false)]
        [TestCase("hello", ".txt", false)]
        [TestCase("hello.tx", ".txt", false)]
        [TestCase("hello.txt", ".txt", true)]
        [TestCase("hello.TxT", ".txt", true)]
        [TestCase("hello.txts", ".txt", false)]
        public void HasFileExtension(string path, string ext, bool result)
        {
            Assert.That(Utility.HasFileExtension(path, ext), Is.EqualTo(result));
        }

        [TestCase("", false)]
        [TestCase("/", true)]
        [TestCase("c", false)]
        [TestCase("c:", true)]
        [TestCase("hello.txt", false)]
        [TestCase("/hello.txt", true)]
        [TestCase(@"c:\hello.txt", true)]
        public void IsPathRooted(string path, bool result)
        {
            Assert.That(Utility.IsPathRooted(path), Is.EqualTo(result));
        }
    }
}
