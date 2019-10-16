using Moq;
using NUnit.Framework;
using Unity.CodeEditor;

namespace VSCodeEditor.Tests
{
    [TestFixture]
    public class VSCodeScriptEditorTests
    {
        IExternalCodeEditor editor;

        [SetUp]
        public void OneTimeSetUp()
        {
            var discovery = new Mock<IDiscovery>();
            var generator = new Mock<IGenerator>();
            editor = new VSCodeScriptEditor(discovery.Object, generator.Object);
        }

        [TearDown]
        public void Dispose()
        {
            CodeEditor.Unregister(editor);
        }

        [Test]
        public void WillNotOpenUnknownExtensions()
        {
            Assert.False(editor.OpenProject("/file/with/unknown.extension", 1, 1));
        }
    }
}
