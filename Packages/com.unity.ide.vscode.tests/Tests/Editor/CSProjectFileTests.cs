using NUnit.Framework;
using Moq;
using System;
using System.IO;
using UnityEditor.Compilation;
using UnityEngine;
using System.Collections.Generic;

namespace VSCodeEditor.Editor_spec
{
    [TestFixture]
    public class CSProjectFileTests
    {

        [Test]
        public void FilesNotContributedAnAssemblyWillNotGetAdded()
        {
            var mock = new Mock<IAssemblyNameProvider>();
            var files = new[]
            {
                "File.cs",
            };
            var island = new Assembly("Assembly2", "/User/Test/Assembly2.dll", files, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
            mock.Setup(x => x.GetAllAssemblies(It.IsAny<Func<string, bool>>())).Returns(new[] { island });
            mock.Setup(x => x.GetAssemblyNameFromScriptPath(It.IsAny<string>())).Returns(string.Empty);
            mock.Setup(x => x.GetAllAssetPaths()).Returns(new[] { "File/Not/In/Assembly.hlsl" });
            var synchronizer = new ProjectGeneration(Directory.GetParent(Application.dataPath).FullName, mock.Object);
            synchronizer.Sync();
            var csprojContent = File.ReadAllText(synchronizer.ProjectFile(island));
            StringAssert.DoesNotContain("NotExist.hlsl", csprojContent);
        }

        [Test]
        public void RelativePackages_GetsPathResolvedCorrectly()
        {
            var mock = new Mock<IAssemblyNameProvider>();
            var files = new[]
            {
                "/FullPath/ExamplePackage/Packages/Asset.cs",
            };
            var island = new Assembly("ExamplePackage", "/FullPath/Example/ExamplePackage/ExamplePackage.dll", files, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
            mock.Setup(x => x.GetAllAssemblies(It.IsAny<Func<string, bool>>())).Returns(new[] { island });
            mock.Setup(x => x.GetAssemblyNameFromScriptPath(It.IsAny<string>())).Returns(string.Empty);
            mock.Setup(x => x.GetAllAssetPaths()).Returns(new[] { "/FullPath/ExamplePackage/Packages/Asset.cs" });
            mock.Setup(x => x.FindForAssetPath("/FullPath/ExamplePackage/Packages/Asset.cs")).Returns(default(UnityEditor.PackageManager.PackageInfo));

            var synchronizer = new ProjectGeneration("/FullPath/Example", mock.Object);
            var syncPaths = new Dictionary<string, string>();
            synchronizer.Settings = new TestSettings { ShouldSync = false, SyncPath = syncPaths };

            synchronizer.Sync();

            StringAssert.Contains("\\FullPath\\ExamplePackage\\Packages\\Asset.cs", syncPaths[Path.Combine("/FullPath/Example", $"{island.name}.csproj")]);
        }
    }
}