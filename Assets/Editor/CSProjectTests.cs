using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace VSCodeEditor.Editor_spec
{
    [Serializable]
    public class CSProject
    {
        [SerializeField]
        List<string> m_PathsToDelete = new List<string>();
        [SerializeField]
        List<string> m_DirectoriesToDelete = new List<string>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var pathToDelete in m_PathsToDelete)
            {
                if (File.Exists(pathToDelete))
                {
                    File.Delete(pathToDelete);
                }

                if (File.Exists(pathToDelete + ".meta"))
                {
                    File.Delete(pathToDelete + ".meta");
                }
            }

            foreach (var directoryToDelete in m_DirectoriesToDelete)
            {
                if (Directory.Exists(directoryToDelete))
                {
                    Directory.Delete(directoryToDelete, true);
                    File.Delete(directoryToDelete + ".meta");
                }
            }

            m_PathsToDelete.Clear();
            m_DirectoriesToDelete.Clear();

            yield return new RecompileScripts(false);
        }

        [OneTimeTearDown]
        public void Dispose()
        {
            AssetDatabase.Refresh();
        }

        void CreateDll(string pluginName, string path)
        {
            var dllPath = Path.Combine(path, pluginName);
            m_PathsToDelete.Add(dllPath);
            File.Copy("Assets/Dummy.dll", dllPath, true);
        }

        void CreateFileWithContent(string fileName, string fileContent, string path)
        {
            var responseFile = Path.Combine(path, fileName);
            m_PathsToDelete.Add(responseFile);
            File.WriteAllText(responseFile, fileContent);
        }

        [UnityTest]
        public IEnumerator AddedDefinesToResponseFile_IsFoundInCSProjectFile()
        {
            CreateFileWithContent("csc.rsp", "-define:DEF1;DEF2 /reference:Assets/MyPlugin.dll", Application.dataPath);
            CreateDll("MyPlugin.dll", Application.dataPath);
            CreateFileWithContent("test.cs", " ", Application.dataPath);

            yield return new RecompileScripts(false);

            var csprojFileContents = SetupProjectGenerationAndReturnCSProjFilesWithContent().First(x => x.Key.Contains("Assembly-CSharp.csproj")).Value;

            Assert.IsTrue(ContainsRegex(csprojFileContents, "<DefineConstants>.*;DEF1.*</DefineConstants>"));
            Assert.IsTrue(ContainsRegex(csprojFileContents, "<DefineConstants>.*;DEF2.*</DefineConstants>"));

            Assert.IsTrue(ContainsRegex(csprojFileContents, "<Reference Include=\"MyPlugin\">\\W*<HintPath>.*Assets/MyPlugin.dll</HintPath>\\W*</Reference>"));
        }

        [UnityTest]
        public IEnumerator PathWithSpaces_IsParsedCorrectlyAndLinkedFromResponseFile()
        {
            var pathWithSpace = Path.Combine(Application.dataPath, "Path With Space");
            m_DirectoriesToDelete.Add(pathWithSpace);
            Directory.CreateDirectory(pathWithSpace);

            CreateDll("Goodbye.dll", pathWithSpace);
            CreateFileWithContent("csc.rsp", "-r:\"Assets/Path With Space/Goodbye.dll\"", Application.dataPath);

            yield return new RecompileScripts();

            var csprojFileContents = SetupProjectGenerationAndReturnCSProjFileContent();

            Assert.IsTrue(ContainsRegex(csprojFileContents, "<Reference Include=\"Goodbye\">\\W*<HintPath>.*Assets/Path With Space/Goodbye.dll\\W*</HintPath>\\W*</Reference>"));
        }

        [UnityTest]
        public IEnumerator ResponseFileDefines_OverrideRootResponseFile()
        {
            var directory = Path.Combine(Application.dataPath, "AsmdefResponse");
            CreateFileWithContent("csc.rsp", "/d:ASMDEF_DEFINE", directory);
            CreateFileWithContent("csc.rsp", "/d:RootedDefine", Application.dataPath);

            yield return new RecompileScripts(false);
            var setupProjectGenerationAndReturnCsProjFilesWithContent = SetupProjectGenerationAndReturnCSProjFilesWithContent();

            Assert.IsTrue(ContainsRegex(setupProjectGenerationAndReturnCsProjFilesWithContent.Single(x => x.Key.Contains("AsmdefResponse.csproj")).Value, "<DefineConstants>.*;ASMDEF_DEFINE.*</DefineConstants>"));
            Assert.IsFalse(ContainsRegex(setupProjectGenerationAndReturnCsProjFilesWithContent.Single(x => x.Key.Contains("AsmdefResponse.csproj")).Value, "<DefineConstants>.*;RootedDefine.*</DefineConstants>"));
            Assert.IsFalse(ContainsRegex(setupProjectGenerationAndReturnCsProjFilesWithContent.Single(x => x.Key.Contains("Assembly-CSharp-Editor.csproj")).Value, "<DefineConstants>.*;ASMDEF_DEFINE.*</DefineConstants>"));
            Assert.IsTrue(ContainsRegex(setupProjectGenerationAndReturnCsProjFilesWithContent.Single(x => x.Key.Contains("Assembly-CSharp-Editor.csproj")).Value, "<DefineConstants>.*;RootedDefine.*</DefineConstants>"));
        }

        [UnityTest]
        public IEnumerator MultipleFilesAdded_CanBeFoundInCSProjectFile()
        {
            CreateDll("MyPlugin.dll", Application.dataPath);
            CreateDll("Hello.dll", Application.dataPath);
            CreateFileWithContent("csc.rsp", "/reference:Assets/MyPlugin.dll -r:Assets/Hello.dll", Application.dataPath);

            yield return new RecompileScripts();

            var csprojFileContents = SetupProjectGenerationAndReturnCSProjFileContent();

            Assert.IsTrue(ContainsRegex(csprojFileContents, "<Reference Include=\"Hello\">\\W*<HintPath>.*Assets/Hello.dll</HintPath>\\W*</Reference>"));
            Assert.IsTrue(ContainsRegex(csprojFileContents, "<Reference Include=\"MyPlugin\">\\W*<HintPath>.*Assets/MyPlugin.dll</HintPath>\\W*</Reference>"));
        }

        [UnityTest]
        public IEnumerator AddingDefinesForMultipleFilesTest()
        {
            CreateDll("MyPlugin.dll", Application.dataPath);
            CreateDll("Hello.dll", Application.dataPath);
            CreateFileWithContent("csc.rsp", "-define:DEF1;DEF2 /reference:Assets/MyPlugin.dll -define:DEF3,DEF4;DEFFFF \n /d:DEF5\n -r:Assets/Hello.dll", Application.dataPath);

            yield return new RecompileScripts();

            var csprojFileContents = SetupProjectGenerationAndReturnCSProjFileContent();

            Assert.IsTrue(ContainsRegex(csprojFileContents, "<DefineConstants>.*;DEF1.*</DefineConstants>"));
            Assert.IsTrue(ContainsRegex(csprojFileContents, "<DefineConstants>.*;DEF2.*</DefineConstants>"));
            Assert.IsTrue(ContainsRegex(csprojFileContents, "<DefineConstants>.*;DEF3.*</DefineConstants>"));
            Assert.IsTrue(ContainsRegex(csprojFileContents, "<DefineConstants>.*;DEF4.*</DefineConstants>"));
            Assert.IsTrue(ContainsRegex(csprojFileContents, "<DefineConstants>.*;DEF5.*</DefineConstants>"));
            Assert.IsTrue(ContainsRegex(csprojFileContents, "<DefineConstants>.*;DEFFFF.*</DefineConstants>"));
        }

        [UnityTest]
        public IEnumerator AllowUnsafeBlockTest()
        {
            CreateDll("MyPlugin.dll", Application.dataPath);
            CreateFileWithContent("csc.rsp", "-unsafe /reference:Assets/MyPlugin.dll", Application.dataPath);

            yield return new RecompileScripts();

            var csprojFileContents = SetupProjectGenerationAndReturnCSProjFileContent();

            Assert.IsTrue(ContainsRegex(csprojFileContents, "<AllowUnsafeBlocks>True</AllowUnsafeBlocks>"));
        }

        [Test]
        public void ResponseFileReference_ResolvesToAbsolutePath()
        {
            CreateFileWithContent("csc.rsp", "-r:System.Data.dll", Application.dataPath);

            var csprojFileContents = SetupProjectGenerationAndReturnCSProjFileContent();

            var xmlDocument = new XmlDocument();
            xmlDocument.LoadXml(csprojFileContents);

            var references = SelectInnerTextOfNodes(xmlDocument, "/msb:Project/msb:ItemGroup/msb:Reference/msb:HintPath", GetModifiedXmlNamespaceManager(xmlDocument)).ToArray();
            Assert.True(references.Any(reference => reference.Contains("System.Data.dll")), "System.Data.dll was not found.\n Make sure that this is in the specific API reference files.");
            Assert.That(references, Is.All.Matches<string>(Path.IsPathRooted));
        }

        static IEnumerable<string> SelectInnerTextOfNodes(XmlDocument xmlDocument, string xpathQuery, XmlNamespaceManager xmlNamespaceManager)
        {
            return xmlDocument.SelectNodes(xpathQuery, xmlNamespaceManager).Cast<XmlElement>().Select(e => e.InnerText);
        }

        static XmlNamespaceManager GetModifiedXmlNamespaceManager(XmlDocument projectXml)
        {
            var xmlNamespaces = new XmlNamespaceManager(projectXml.NameTable);
            xmlNamespaces.AddNamespace("msb", ProjectGeneration.MSBuildNamespaceUri);
            return xmlNamespaces;
        }

        static string SetupProjectGenerationAndReturnCSProjFileContent()
        {
            return SetupProjectGenerationAndReturnCSProjFilesWithContent().First().Value;
        }

        static Dictionary<string, string> SetupProjectGenerationAndReturnCSProjFilesWithContent()
        {
            var projectDirectory = Directory.GetParent(Application.dataPath).FullName;
            var synchronizer = new ProjectGeneration(projectDirectory);
            synchronizer.GenerateAndWriteSolutionAndProjects();

            var files = Directory.GetFiles(projectDirectory);
            return files.Where(f => Path.GetExtension(f) == ".csproj").ToDictionary(x => x, x => File.ReadAllText(Path.Combine(projectDirectory, x)));
        }

        static bool ContainsRegex(string input, string pattern)
        {
            return Regex.Match(input, pattern).Success;
        }
    }
}