using NUnit.Framework;
using Moq;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.CodeEditor;

namespace VSCodeEditor.Tests
{
    [TestFixture]
    class DetermineScriptEditor
    {
        [TestCase("/Applications/Visual Studio Code.app")]
        [TestCase("/Applications/Visual Studio Code - Insiders.app")]
        [TestCase("/Applications/VS Code.app")]
        [TestCase("/Applications/Code.app")]
        [TestCase("/home/thatguy/vscode/code")]
        [UnityPlatform(RuntimePlatform.OSXEditor)]
        public void OSXPathDiscovery(string path)
        {
            Discover(path);
        }

        [TestCase(@"C:\Program Files\Microsoft VS Code\bin\code.cmd")]
        [TestCase(@"C:\Program Files\Microsoft VS Code\Code.exe")]
        [TestCase(@"C:\Program Files\Microsoft VS Code Insiders\bin\code-insiders.cmd")]
        [TestCase(@"C:\Program Files\Microsoft VS Code Insiders\Code.exe")]
        [TestCase(@"C:\Users\Username\AppData\Local\Programs\Microsoft VS Code\bin\code.cmd")]
        [TestCase(@"C:\Users\Username\AppData\Local\Programs\Microsoft VS Code\Code.exe")]
        [TestCase(@"C:\Users\Username\AppData\Local\Programs\Microsoft VS Code Insiders\bin\code-insiders.cmd")]
        [TestCase(@"C:\Users\Username\AppData\Local\Programs\Microsoft VS Code Insiders\Code.exe")]
        [UnityPlatform(RuntimePlatform.WindowsEditor)]
        public void WindowsPathDiscovery(string path)
        {
            Discover(path);
        }

        [TestCase("/usr/bin/code")]
        [TestCase("/bin/code")]
        [TestCase("/usr/local/bin/code")]
        [TestCase("/var/lib/flatpak/exports/bin/com.visualstudio.code")]
        [TestCase("/snap/current/bin/code")]
        [UnityPlatform(RuntimePlatform.LinuxEditor)]
        public void LinuxPathDiscovery(string path)
        {
            Discover(path);
        }

        static void Discover(string path)
        {
            var discovery = new Mock<IDiscovery>();
            var generator = new Mock<IGenerator>();

            discovery.Setup(x => x.PathCallback()).Returns(new[]
            {
                new CodeEditor.Installation
                {
                    Path = path,
                    Name = path.Contains("Insiders") ? "Visual Studio Code Insiders" : "Visual Studio Code"
                }
            });

            var editor = new VSCodeScriptEditor(discovery.Object, generator.Object);

            editor.TryGetInstallationForPath(path, out var installation);

            Assert.AreEqual(path, installation.Path);
        }
    }
}
