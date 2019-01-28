using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VSCodeEditor;

namespace VSCodeEditor.Runtime_spec.CSProject
{
    [TestFixture]
    [Serializable]
    public abstract class CleanupTest
    {
        [SerializeField]
        protected IGenerator m_ProjectGeneration;
        [SerializeField]
        protected List<string> m_GeneratedFiles = new List<string>();
        [SerializeField]
        List<string> m_DirectoriesToDelete = new List<string>();
        [SerializeField]
        protected string m_CsProjPath;
        [SerializeField]
        protected DateTime m_LastWritten;
        
        protected const string emptyCSharpScript = @"
using UnityEngine;
public class SimpleCSharpScript : MonoBehaviour
{
  void Start()
  {
  }
}";

        [SetUp]
        public void SetUp() {
            var projectDirectory = Directory.GetParent(Application.dataPath).FullName;
            m_ProjectGeneration = new ProjectGeneration(projectDirectory);
        }

        [UnityTearDown]
        public virtual IEnumerator TearDown()
        {
            foreach (var pathToDelete in m_GeneratedFiles)
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

            m_GeneratedFiles.Clear();
            m_DirectoriesToDelete.Clear();

            yield return new RecompileScripts(false);
        }

        protected void CopyScriptToAssetsFolder(string assetPath, string fileName, string content)
        {
            var targetFile = Path.Combine(assetPath, fileName);
            m_GeneratedFiles.Add(targetFile);
            File.WriteAllText(targetFile, content);
        }

        protected void CreateFolder(string path)
        {
            m_DirectoriesToDelete.Add(path);
            Directory.CreateDirectory(path);
        }
    }

    public class Synchronize : CleanupTest
    {
        [UnityTest]
        public IEnumerator Not_WhenNoChanged()
        {
            CopyScriptToAssetsFolder(Application.dataPath, "SimpleCSharpScript.cs", emptyCSharpScript);
            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp.csproj");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            DateTime lastWritten = File.GetLastWriteTime(m_CsProjPath);

            string csprojContents = File.ReadAllText(m_CsProjPath);
            StringAssert.Contains(@"Assets\SimpleCSharpScript.cs", csprojContents);
            Assert.AreEqual(lastWritten, File.GetLastWriteTime(m_CsProjPath));
        }

        [UnityTest]
        public IEnumerator IncludesScripts()
        {
            CopyScriptToAssetsFolder(Application.dataPath, "SimpleCSharpScript.cs", emptyCSharpScript);
            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp.csproj");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            string csprojContents = File.ReadAllText(m_CsProjPath);
            StringAssert.Contains(@"Assets\SimpleCSharpScript.cs", csprojContents);
        }

        [UnityTest]
        public IEnumerator DoesNotIncludeNonCSharpFiles()
        {
            CopyScriptToAssetsFolder(Application.dataPath, "SimpleCSharpScript.cs", emptyCSharpScript);
            CopyScriptToAssetsFolder(Application.dataPath, "ClassDiagram1.cd", " ");
            CopyScriptToAssetsFolder(Application.dataPath, "text.txt", " ");
            CopyScriptToAssetsFolder(Application.dataPath, "Test.shader", " ");
            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp.csproj");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            var csProj = XMLUtilities.FromFile(m_CsProjPath);

            XMLUtilities.AssertCompileItemsMatchExactly(csProj, new [] { "SimpleCSharpScript.cs" });
            XMLUtilities.AssertNonCompileItemsMatchExactly(csProj, new [] { "text.txt", "ClassDiagram1.cd", "Test.shader" });
        }

        [UnityTest]
        public IEnumerator ReferencesPlugins()
        {
            CreateFolder($"{Application.dataPath}/Plugins");
            CopyScriptToAssetsFolder(Application.dataPath, "SimpleCSharpScript.cs", emptyCSharpScript);
            CopyScriptToAssetsFolder($"{Application.dataPath}/Plugins", "Plugin.cs", emptyCSharpScript);
            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp.csproj");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            var projectReferences = new[]
            {
                "Assembly-CSharp-firstpass",
                "Unity.VSCode.Editor",
                "AsmdefResponse"
            };
            var projectXml = XMLUtilities.FromFile(m_CsProjPath);

            XMLUtilities.AssertProjectReferencesMatchExactly(projectXml, projectReferences);
        }

        [UnityTest]
        public IEnumerator EditorPluginsReferencePlugin()
        {
            CreateFolder($"{Application.dataPath}/Plugins");
            CreateFolder($"{Application.dataPath}/Plugins/Editor");
            CopyScriptToAssetsFolder($"{Application.dataPath}/Plugins", "Plugin.cs", emptyCSharpScript);
            CopyScriptToAssetsFolder($"{Application.dataPath}/Plugins/Editor", "EditorPlugin.cs", emptyCSharpScript);
            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp-Editor-firstpass.csproj");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            var projectReferences = new[]
            {
                "Assembly-CSharp-firstpass",
                "Unity.VSCode.Editor",
                "AsmdefResponse"
            };
            var expectedReferencesEditor = new[]
            {
                "System",
                "System.Xml",
                "System.Core",
                "UnityEngine",
                "UnityEditor",
                "UnityEditor.Graphs"
            };
            var projectXml = XMLUtilities.FromFile(m_CsProjPath);

            XMLUtilities.AssertReferencesContainAll(projectXml, expectedReferencesEditor);
            XMLUtilities.AssertProjectReferencesMatchExactly(projectXml, projectReferences);
        }

        [UnityTest]
        public IEnumerator EditorReferenceAllOtherScriptProjects()
        {
            CreateFolder($"{Application.dataPath}/Plugins");
            CreateFolder($"{Application.dataPath}/Plugins/Editor");
            CopyScriptToAssetsFolder(Application.dataPath, "Bar.cs", emptyCSharpScript);
            CopyScriptToAssetsFolder($"{Application.dataPath}/Editor", "Foo.cs", emptyCSharpScript);
            CopyScriptToAssetsFolder($"{Application.dataPath}/Plugins", "Plugin.cs", emptyCSharpScript);
            CopyScriptToAssetsFolder($"{Application.dataPath}/Plugins/Editor", "EditorPlugin.cs", emptyCSharpScript);
            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp-Editor.csproj");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            var projectReferences = new[]
            {
                "Assembly-CSharp",
                "Assembly-CSharp-firstpass",
                "Assembly-CSharp-Editor-firstpass",
                "Unity.VSCode.Editor",
                "AsmdefResponse"
            };
            var expectedReferencesEditor = new[]
            {
                "System",
                "System.Xml",
                "System.Core",
                "UnityEngine",
                "UnityEditor",
                "UnityEditor.Graphs"
            };
            var projectXml = XMLUtilities.FromFile(m_CsProjPath);

            XMLUtilities.AssertReferencesContainAll(projectXml, expectedReferencesEditor);
            XMLUtilities.AssertProjectReferencesMatchExactly(projectXml, projectReferences);
        }

        [UnityTest]
        public IEnumerator IncludesScriptsInPlugins()
        {
            CreateFolder($"{Application.dataPath}/Test");
            CreateFolder($"{Application.dataPath}/Test/Hello");
            CopyScriptToAssetsFolder($"{Application.dataPath}/Test", "Test.asmdef", @"{ ""name"" : ""Test"" }");
            CopyScriptToAssetsFolder($"{Application.dataPath}/Test", "Script.cs", " ");
            CopyScriptToAssetsFolder($"{Application.dataPath}/Test", "Doc.txt", " ");
            CopyScriptToAssetsFolder($"{Application.dataPath}/Test/Hello", "Hello.txt", " ");
            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Test.csproj");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            var sourceFiles = new string[]
            {
                "Test/Script.cs"
            };

            var textFiles = new string[]
            {
                "Test/Test.asmdef",
                "Test/Doc.txt",
                "Test/Hello/Hello.txt"
            };

            var projectXml = XMLUtilities.FromFile(m_CsProjPath);
            XMLUtilities.AssertCompileItemsMatchExactly(projectXml, sourceFiles);
            XMLUtilities.AssertNonCompileItemsMatchExactly(projectXml, textFiles);
        }

        [UnityTest]
        public IEnumerator IncludesFilesAddedToAssemblyDefinitions()
        {
            CopyScriptToAssetsFolder(Application.dataPath, "SimpleCSharpScript.cs", emptyCSharpScript);
            string uglyDefineString = "THISDEFINEISEXTREMELYUNLIKELYTOEXISTBYDEFAULT; ALSOTHISONE, FINALLYTHISONE";
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, uglyDefineString);

            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp.csproj");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            XMLUtilities.AssertDefinesContain(m_CsProjPath, "THISDEFINEISEXTREMELYUNLIKELYTOEXISTBYDEFAULT");
            XMLUtilities.AssertDefinesContain(m_CsProjPath, "ALSOTHISONE");
            XMLUtilities.AssertDefinesContain(m_CsProjPath, "FINALLYTHISONE");
        }

        [UnityTest]
        public IEnumerator CorrectGuid()
        {
            CreateFolder($"{Application.dataPath}/Plugins");
            CopyScriptToAssetsFolder(Application.dataPath, "SimpleCSharpScript.cs", emptyCSharpScript);
            CopyScriptToAssetsFolder($"{Application.dataPath}/Plugins", "Plugin.cs", emptyCSharpScript);

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            var dir = Directory.GetParent(Application.dataPath).FullName;
            var scriptProjectFile = Path.Combine(dir, "Assembly-CSharp.csproj");
            var scriptPluginProjectFile = Path.Combine(dir, "Assembly-CSharp-firstpass.csproj");

            Assert.IsTrue(File.Exists(scriptProjectFile));
            Assert.IsTrue(File.Exists(scriptPluginProjectFile));

            XmlDocument scriptProject = XMLUtilities.FromFile(scriptProjectFile);
            XmlDocument scriptPluginProject = XMLUtilities.FromFile(scriptPluginProjectFile);

            var xmlNamespaces = new XmlNamespaceManager(scriptProject.NameTable);
            xmlNamespaces.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003");

            Assert.AreEqual(scriptPluginProject.SelectSingleNode("/msb:Project/msb:PropertyGroup/msb:ProjectGuid", xmlNamespaces).InnerText,
                scriptProject.SelectSingleNode("/msb:Project/msb:ItemGroup/msb:ProjectReference/msb:Project", xmlNamespaces).InnerText);
        }

        [UnityTest]
        public IEnumerator DoesntAddInvalidAssemblyReference()
        {
            CopyScriptToAssetsFolder(Application.dataPath, "Native.dll", " ");
            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp.csproj");

            yield return new RecompileScripts(false);
            m_ProjectGeneration.Sync();

            string csprojContents = File.ReadAllText(m_CsProjPath);
            StringAssert.DoesNotContain("Assets/Native.dll", csprojContents);
        }

        [UnityTest]
        public IEnumerator OnAssetImport()
        {
            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp.csproj");

            if (File.Exists(m_CsProjPath))
                File.Delete(m_CsProjPath);

            m_ProjectGeneration.Sync();

            Assert.IsFalse(File.Exists(m_CsProjPath));

            CopyScriptToAssetsFolder(Application.dataPath, "imported.cs", " ");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            string csprojContents = File.ReadAllText(m_CsProjPath);
            StringAssert.Contains("Assets\\imported.cs", csprojContents);
        }

        [UnityTest]
        public IEnumerator OnAssetMove()
        {
            var dir = Directory.GetParent(Application.dataPath).FullName;
            CopyScriptToAssetsFolder(Application.dataPath, "old.cs", " ");
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp.csproj");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            var oldScriptFile = "Assets\\old.cs";
            var newScriptFile = "Assets\\new.cs";
            string csprojContents = File.ReadAllText(m_CsProjPath);

            StringAssert.Contains(oldScriptFile, csprojContents);
            StringAssert.DoesNotContain(newScriptFile, csprojContents);

            File.Delete(Path.Combine(Application.dataPath, "old.cs"));
            CopyScriptToAssetsFolder(Application.dataPath, "new.cs", " ");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            oldScriptFile = "Assets\\old.cs";
            newScriptFile = "Assets\\new.cs";
            csprojContents = File.ReadAllText(m_CsProjPath);
            StringAssert.DoesNotContain(oldScriptFile, csprojContents);
            StringAssert.Contains(newScriptFile, csprojContents);
        }

        [UnityTest]
        public IEnumerator OnAssetDelete()
        {
            CopyScriptToAssetsFolder(Application.dataPath, "deleted.cs", " ");
            CopyScriptToAssetsFolder(Application.dataPath, "another.cs", " ");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp.csproj");

            var scriptAsset = "Assets\\deleted.cs";
            var csprojContents = File.ReadAllText(m_CsProjPath);
            StringAssert.Contains(scriptAsset, csprojContents);

            File.Delete("Assets/deleted.cs");
            
            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            scriptAsset = "Assets\\deleted.cs";
            csprojContents = File.ReadAllText(m_CsProjPath);
            StringAssert.DoesNotContain(scriptAsset, csprojContents);
        }
    }

    public class Formatting : CleanupTest
    {
        const string kMsBuildNamespaceUri = "http://schemas.microsoft.com/developer/msbuild/2003";

        [UnityTest]
        public IEnumerator Escape_SpecialCharsInFileName()
        {
            Dictionary<string, string> awesomeFilenames() {
                return new Dictionary<string, string>
                {
                    { @"x & y", @"x &amp; y" },
                    { @"x ' y", @"x &apos; y" },
                    // <, > and " are illegal
                };
            }
            
            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp.csproj");

            var uniqueId = 0;
            foreach (var filename in awesomeFilenames().Keys)
            {
                var dummyScript = "class Test" + (uniqueId++) + "{}";
                CopyScriptToAssetsFolder(Application.dataPath, filename + ".cs", dummyScript);
            }

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            string csprojContents = File.ReadAllText(m_CsProjPath);
            foreach (var awesomePair in awesomeFilenames())
            {
                StringAssert.DoesNotContain(awesomePair.Key, csprojContents);
                StringAssert.Contains(awesomePair.Value, csprojContents);
            }
        }

        [UnityTest]
        public IEnumerator Escape_SpecialCharsInPathName()
        {            
            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp.csproj");

            var dummyScript = "class Test{}";
            CreateFolder($"{Application.dataPath}/Dimmer&");
            CopyScriptToAssetsFolder($"{Application.dataPath}/Dimmer&", "foo.cs", dummyScript);

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            string csprojContents = File.ReadAllText(m_CsProjPath);
            StringAssert.DoesNotContain("Dimmer&\\foo.cs", csprojContents);
            StringAssert.Contains("Dimmer&amp;\\foo.cs", csprojContents);
        }
    }

    public class BuildTarget : CleanupTest
    {
        [SerializeField]
        UnityEditor.BuildTarget m_original;

        public override IEnumerator TearDown()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, m_original);
            return base.TearDown();
        }

        [UnityPlatform(RuntimePlatform.WindowsEditor)]
        [UnityTest]
        public IEnumerator WhenActiveBuildTargetChanges_Windows()
        {
            m_original = UnityEditor.BuildTarget.StandaloneWindows64;
            return AssertSynchronizedWhenActiveBuildTargetChanges(
                UnityEditor.BuildTarget.StandaloneWindows64,
                "PLATFORM_STANDALONE_WIN",
                UnityEditor.BuildTarget.StandaloneLinux64,
                "PLATFORM_STANDALONE_LINUX",
                () => {});
        }

        [UnityPlatform(RuntimePlatform.OSXEditor)]
        [UnityTest]
        public IEnumerator WhenActiveBuildTargetChanges_MacOSX()
        {
            m_original = UnityEditor.BuildTarget.StandaloneOSX;
            return AssertSynchronizedWhenActiveBuildTargetChanges(
                UnityEditor.BuildTarget.StandaloneOSX,
                "PLATFORM_STANDALONE_OSX",
                UnityEditor.BuildTarget.StandaloneWindows64,
                "PLATFORM_STANDALONE_WIN",
                () => {});
        }

        [UnityPlatform(RuntimePlatform.LinuxEditor)]
        [UnityTest]
        public IEnumerator WhenActiveBuildTargetChanges_Linux()
        {
            m_original = UnityEditor.BuildTarget.StandaloneLinux64;
            return AssertSynchronizedWhenActiveBuildTargetChanges(
                UnityEditor.BuildTarget.StandaloneLinux64,
                "PLATFORM_STANDALONE_LINUX",
                UnityEditor.BuildTarget.StandaloneOSX,
                "PLATFORM_STANDALONE_OSX",
                () => {});
        }

        [UnityPlatform(RuntimePlatform.WindowsEditor)]
        [UnityTest]
        public IEnumerator WhenActiveBuildTargetChangesAfterScriptReload_Windows()
        {
            m_original = UnityEditor.BuildTarget.StandaloneWindows64;
            return AssertSynchronizedWhenActiveBuildTargetChanges(
                UnityEditor.BuildTarget.StandaloneWindows64,
                "PLATFORM_STANDALONE_WIN",
                UnityEditor.BuildTarget.StandaloneLinux64,
                "PLATFORM_STANDALONE_LINUX",
                () => { CopyScriptToAssetsFolder(Application.dataPath, "SimpleCSharpScript2.cs", " "); });
        }

        [UnityPlatform(RuntimePlatform.OSXEditor)]
        [UnityTest]
        public IEnumerator WhenActiveBuildTargetChangesAfterScriptReload_MacOSX()
        {
            m_original = UnityEditor.BuildTarget.StandaloneOSX;
            return AssertSynchronizedWhenActiveBuildTargetChanges(
                UnityEditor.BuildTarget.StandaloneOSX,
                "PLATFORM_STANDALONE_OSX",
                UnityEditor.BuildTarget.StandaloneWindows64,
                "PLATFORM_STANDALONE_WIN",
                () => { CopyScriptToAssetsFolder(Application.dataPath, "SimpleCSharpScript2.cs", " "); });
        }

        [UnityPlatform(RuntimePlatform.LinuxEditor)]
        [UnityTest]
        public IEnumerator WhenActiveBuildTargetChangesAfterScriptReload_Linux()
        {
            m_original = UnityEditor.BuildTarget.StandaloneLinux64;
            return AssertSynchronizedWhenActiveBuildTargetChanges(
                UnityEditor.BuildTarget.StandaloneLinux64,
                "PLATFORM_STANDALONE_LINUX",
                UnityEditor.BuildTarget.StandaloneOSX,
                "PLATFORM_STANDALONE_OSX",
                () => { CopyScriptToAssetsFolder(Application.dataPath, "SimpleCSharpScript2.cs", " "); });
        }

        private IEnumerator AssertSynchronizedWhenActiveBuildTargetChanges(
            UnityEditor.BuildTarget platformTarget,
            string platformDefine,
            UnityEditor.BuildTarget changeTarget,
            string changeDefine,
            Action action)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, platformTarget);
            CopyScriptToAssetsFolder(Application.dataPath, "SimpleCSharpScript.cs", emptyCSharpScript);

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            var dir = Directory.GetParent(Application.dataPath).FullName;
            m_CsProjPath = Path.Combine(dir, "Assembly-CSharp.csproj");

            AssertProjectContainsDefine(m_CsProjPath, platformDefine);

            action();

            m_LastWritten = DateTime.Now.AddSeconds(-1);
            File.SetLastWriteTime(m_CsProjPath, m_LastWritten);

            //switch target to another one than the standalone target for the current platform
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, changeTarget);

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            WaitForCondition(() => (File.GetLastWriteTime(m_CsProjPath) > m_LastWritten));

            AssertProjectContainsDefine(m_CsProjPath, changeDefine);
            yield return null;
        }

        private void AssertProjectContainsDefine(string csProjPath, string expectedDefine)
        {
            var content = File.ReadAllText(csProjPath);
            Assert.IsTrue(Regex.IsMatch(content, $"<DefineConstants>.*;{expectedDefine}.*</DefineConstants>"));
        }

        delegate bool Condition();

        private static void WaitForCondition(Condition condition)
        {
            var started = DateTime.Now;
            while (!condition())
            {
                if (DateTime.Now - started > s_Timeout)
                    throw new TimeoutException(string.Format("Timeout while waiting for c# project to be rewritten for {0} seconds", s_Timeout.TotalSeconds));
                Thread.Sleep(10);
            }
        }

        static readonly TimeSpan s_Timeout = TimeSpan.FromSeconds(5);
    }
}