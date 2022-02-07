using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace VSCodeEditor.Tests
{
    class MockFileIO : IFileIO
    {
        Dictionary<string, string> fileToContent = new Dictionary<string, string>();
        public int WriteTimes { get; private set; }
        public int ReadTimes { get; private set; }
        public int ExistTimes { get; private set; }

        public bool Exists(string fileName)
        {
            ++ExistTimes;
            return fileToContent.ContainsKey(fileName);
        }

        public string ReadAllText(string fileName)
        {
            ++ReadTimes;
            return fileToContent[fileName];
        }

        public void WriteAllText(string fileName, string content)
        {
            ++WriteTimes;
            var utf8 = Encoding.UTF8;
            byte[] utfBytes = utf8.GetBytes(content);
            fileToContent[fileName] = utf8.GetString(utfBytes, 0, utfBytes.Length);
        }
        
        public string EscapedRelativePathFor(string file, string projectDirectory)
        {
            return file.NormalizePath().StartsWith($"{projectDirectory}{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                ? file.Substring(projectDirectory.Length + 1)
                : file.NormalizePath();
        }

        public void DeleteFile(string fileName)
        {
            if (!fileToContent.ContainsKey(fileName))
            {
                throw new Exception($"{fileName}: has not been created.");
            }

            fileToContent.Remove(fileName);
        }

        public void CreateDirectory(string pathName)
        {
            fileToContent[pathName] = "";
        }
    }

    public class MockFileIOTests
    {
        MockFileIO m_FileIo;

        [SetUp]
        public void SetUp()
        {
            m_FileIo = new MockFileIO();
        }

        [Test]
        public void WhenWrite_Exists()
        {
            var fileName = "fileName";
            m_FileIo.WriteAllText(fileName, "");
            Assert.True(m_FileIo.Exists(fileName));
        }

        [Test]
        public void BeforeWrite_DoesNotExist()
        {
            var fileName = "fileName";
            Assert.False(m_FileIo.Exists(fileName));
        }

        [Test]
        public void WhenWrite_CanRead()
        {
            var fileName = "fileName";
            var content = "content";
            m_FileIo.WriteAllText(fileName, content);
            Assert.AreEqual(content, m_FileIo.ReadAllText(fileName));
        }

        [Test]
        public void WriteTwice_WillOverwriteContent()
        {
            var fileName = "fileName";
            var content = "content";
            var content2 = "content2";
            m_FileIo.WriteAllText(fileName, content);
            m_FileIo.WriteAllText(fileName, content2);
            Assert.AreEqual(content2, m_FileIo.ReadAllText(fileName));
        }

        [Test]
        public void WhenWrite_ThenDelete_FillDoesNotExist()
        {
            var fileName = "fileName";
            var content = "content";
            m_FileIo.WriteAllText(fileName, content);
            m_FileIo.DeleteFile(fileName);

            Assert.IsFalse(m_FileIo.Exists(fileName), "File Should not exist are deleting it");
        }

        [Test]
        public void BeforeWrite_IfDelete_ExceptionOccurs()
        {
            var fileName = "fileName";

            var exception = Assert.Throws<Exception>(() => m_FileIo.DeleteFile(fileName));

            StringAssert.AreEqualIgnoringCase($"{fileName}: has not been created.", exception.Message);
        }

        [Test]
        public void BeforeWrite_Read_CausesFailure()
        {
            var fileName = "fileName";
            Assert.Throws<KeyNotFoundException>(() => m_FileIo.ReadAllText(fileName));
        }

        [Test]
        public void CallingExist_IncreaseCounter()
        {
            m_FileIo.Exists("fileName");
            Assert.AreEqual(1, m_FileIo.ExistTimes);
            m_FileIo.Exists("fileName2");
            Assert.AreEqual(2, m_FileIo.ExistTimes);
        }

        [Test]
        public void CallingWrite_IncreaseCounter()
        {
            var fileName = "fileName";
            m_FileIo.WriteAllText(fileName, "");
            Assert.AreEqual(1, m_FileIo.WriteTimes);
            m_FileIo.WriteAllText(fileName, "");
            Assert.AreEqual(2, m_FileIo.WriteTimes);
        }

        [Test]
        public void CallingRead_IncreaseCounter()
        {
            var fileName = "fileName";
            m_FileIo.WriteAllText(fileName, "");
            m_FileIo.ReadAllText(fileName);
            Assert.AreEqual(1, m_FileIo.ReadTimes);
            m_FileIo.ReadAllText(fileName);
            Assert.AreEqual(2, m_FileIo.ReadTimes);
        }
    }
}
