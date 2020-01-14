using System.IO;
using NUnit.Framework;

namespace com.unity.ide.vscode.tests
{
    public class FileIOTests
    {
        private FileIO m_FileIO;
        private string m_Temp;

        [SetUp]
        public void SetUp()
        {
            m_FileIO = new FileIO();

            var temp = Path.Combine(Path.GetTempPath(), "VSCodeEditor-" + Path.GetRandomFileName());
            Directory.CreateDirectory(temp);
            m_Temp = temp;
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Temp != null)
                Directory.Delete(m_Temp, recursive: true);
        }

        [Test]
        public void FileIO_RewritesIfContentChanged()
        {
            var path = m_Temp + "/hello.txt";

            m_FileIO.WriteAllText(path, "hello");
            Assert.That(File.ReadAllText(path), Is.EqualTo("hello"));

            File.WriteAllText(path, "sneak changes past FileIO");
            m_FileIO.WriteAllText(path, "hello");
            Assert.That(File.ReadAllText(path), Is.EqualTo("sneak changes past FileIO"), "FileIO rewrote file though it should not have");

            m_FileIO.WriteAllText(path, "new content");
            Assert.That(File.ReadAllText(path), Is.EqualTo("new content"));
        }

        [Test]
        public void FileIO_RewritesIfFileDeleted()
        {
            var path = m_Temp + "/hello.txt";

            m_FileIO.WriteAllText(path, "hello");
            Assert.That(File.ReadAllText(path), Is.EqualTo("hello"));

            File.Delete(path);

            m_FileIO.WriteAllText(path, "hello");
            Assert.That(File.ReadAllText(path), Is.EqualTo("hello"));
        }
    }
}
