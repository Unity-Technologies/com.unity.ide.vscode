using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor.PackageManager.ValidationSuite;

namespace VSCodeEditor
{
    public class Package
    {
        [Test]
        public void Validate()
        {
            Assert.True(ValidationSuite.ValidatePackage("com.unity.ide.vscode@1.0.7", ValidationType.LocalDevelopment));
        }
    }
}
