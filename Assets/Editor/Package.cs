using NUnit.Framework;
using UnityEditor.PackageManager.ValidationSuite;

namespace VSCodeEditor
{
    public class Package
    {
        [Test]
        public void Validate()
        {
            Assert.True(ValidationSuite.ValidatePackage("com.unity.ide.vscode@1.1.2", ValidationType.LocalDevelopment));
        }
    }
}
