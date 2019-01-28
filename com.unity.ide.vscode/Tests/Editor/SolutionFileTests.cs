using System;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Compilation;
using UnityEngine;

namespace VSCodeEditor.Editor_spec
{
    [TestFixture]
    public class SolutionProject
    {
        static string ProjectName
        {
            get
            {
                string[] s = Application.dataPath.Split('/');
                string projectName = s[s.Length - 2];
                return projectName;
            }
        }
        static string s_SolutionFile = $"{ProjectName}.sln";

        [OneTimeSetUp]
        public void OneTimeSetUp() {
            File.Delete(s_SolutionFile);
        }

        [SetUp]
        public void SetUp() {
            var codeEditor = new VSCodeScriptEditor(new Mock<IDiscovery>().Object, new ProjectGeneration());
            codeEditor.CreateIfDoesntExist();
        }

        [TearDown]
        public void Dispose() {
            File.Delete(s_SolutionFile);
        }

        [Test]
        public void CreatesSolutionFileIfFileDoesntExist()
        {
            Assert.IsTrue(File.Exists(s_SolutionFile));
        }

        [Test]
        public void HeaderFormat_MatchesVS2010()
        {
            string[] syncedSolutionText = File.ReadAllLines(s_SolutionFile);

            Assert.IsTrue(syncedSolutionText.Length >= 4);
            Assert.AreEqual(@"", syncedSolutionText[0]);
            Assert.AreEqual(@"Microsoft Visual Studio Solution File, Format Version 11.00", syncedSolutionText[1]);
            Assert.AreEqual(@"# Visual Studio 2010", syncedSolutionText[2]);
            Assert.IsTrue(syncedSolutionText[3].StartsWith("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\")"));
        }

        [Test]
        public void IsUTF8Encoded()
        {
            var bom = new byte[4];
            using (var file = new FileStream(s_SolutionFile, FileMode.Open, FileAccess.Read))
            {
                file.Read(bom, 0, 4);
            }

            // Check for UTF8 BOM - using StreamReader & Assert.AreEqual(Encoding.UTF8, CurrentEncoding); fails despite CurrentEncoding appearing to be UTF8 when viewed in the debugger
            Assert.AreEqual(0xEF, bom[0]);
            Assert.AreEqual(0xBB, bom[1]);
            Assert.AreEqual(0xBF, bom[2]);
        }

        [Test]
        public void SyncOnlyForSomeAssetTypesOnReimport()
        {
            IEnumerable<string> precompiledAssetImport = new[] { "reimport.dll" };
            IEnumerable<string> asmdefAssetImport = new[] { "reimport.asmdef" };
            IEnumerable<string> otherAssetImport = new[] { "reimport.someOther" };

            var projectGeneration = new ProjectGeneration(new FileInfo(s_SolutionFile).DirectoryName);
            Assert.IsTrue(File.Exists(s_SolutionFile));

            var precompiledAssemblySyncIfNeeded = projectGeneration.SyncIfNeeded(Enumerable.Empty<string>().ToArray(), precompiledAssetImport);
            var asmdefSyncIfNeeded = projectGeneration.SyncIfNeeded(Enumerable.Empty<string>().ToArray(), asmdefAssetImport);
            var someOtherSyncIfNeeded = projectGeneration.SyncIfNeeded(Enumerable.Empty<string>().ToArray(), otherAssetImport);

            Assert.IsTrue(precompiledAssemblySyncIfNeeded);
            Assert.IsTrue(asmdefSyncIfNeeded);
            Assert.IsFalse(someOtherSyncIfNeeded);
        }
    }
}