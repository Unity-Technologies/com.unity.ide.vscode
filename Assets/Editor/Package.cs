using NUnit.Framework;
using UnityEditor.PackageManager.ValidationSuite;

namespace VSCodeEditor
{
    public class Package
    {
        [Test]
        public void Validate()
        {
            const string package = "com.unity.ide.vscode@1.1.2";
            var result = ValidationSuite.ValidatePackage(package, ValidationType.LocalDevelopment);
            UnityEngine.Debug.Log(ValidationSuite.GetValidationSuiteReport(package));
            Assert.True(result);
        }
    }
}
