using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace com.unity.ide.vscode
{
    interface IFileIO
    {
        bool Exists(string fileName);

        void WriteAllText(string fileName, string content);

        void CreateDirectory(string pathName);
    }

    class FileIO : IFileIO
    {
        readonly MD5 m_Hasher = MD5.Create();
        readonly Dictionary<string, byte[]> m_FileHashes = new Dictionary<string, byte[]>();

        public bool Exists(string fileName)
        {
            return File.Exists(fileName);
        }

        public void WriteAllText(string filename, string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = m_Hasher.ComputeHash(bytes);

            // If MD5 of file content matches the last thing we wrote, don't write it again.
            if (m_FileHashes.TryGetValue(filename, out var previousHash)
                && BitConverter.ToInt64(hash, 0) == BitConverter.ToInt64(previousHash, 0)
                && BitConverter.ToInt64(hash, 8) == BitConverter.ToInt64(previousHash, 8)
                && File.Exists(filename))
            {
                return;
            }
            m_FileHashes[filename] = hash;

            File.WriteAllBytes(filename, bytes);
        }

        public void CreateDirectory(string pathName)
        {
            Directory.CreateDirectory(pathName);
        }
    }
}
