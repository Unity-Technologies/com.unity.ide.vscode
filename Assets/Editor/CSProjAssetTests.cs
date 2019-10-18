using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace VSCodeEditor.Runtime_spec.CSProject
{
    [TestFixture]
    [Serializable]
    public abstract class CleanupTest
    {
        [SerializeField]
        protected IGenerator m_ProjectGeneration;
        [SerializeField]
        List<string> m_GeneratedFiles = new List<string>();
        [SerializeField]
        List<string> m_DirectoriesToDelete = new List<string>();
        [SerializeField]
        protected string m_CsProjPath;
        [SerializeField]
        protected DateTime m_LastWritten;

        protected const string k_EmptyCSharpScript = @"
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
        protected virtual IEnumerator TearDown()
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

    public class BuildTarget : CleanupTest
    {
        [SerializeField]
        UnityEditor.BuildTarget m_Original;

        protected override IEnumerator TearDown()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, m_Original);
            return base.TearDown();
        }

        [UnityPlatform(RuntimePlatform.WindowsEditor)]
        [UnityTest]
        public IEnumerator WhenActiveBuildTargetChanges_Windows()
        {
            m_Original = UnityEditor.BuildTarget.StandaloneWindows64;
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
            m_Original = UnityEditor.BuildTarget.StandaloneOSX;
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
            m_Original = UnityEditor.BuildTarget.StandaloneLinux64;
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
            m_Original = UnityEditor.BuildTarget.StandaloneWindows64;
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
            m_Original = UnityEditor.BuildTarget.StandaloneOSX;
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
            m_Original = UnityEditor.BuildTarget.StandaloneLinux64;
            return AssertSynchronizedWhenActiveBuildTargetChanges(
                UnityEditor.BuildTarget.StandaloneLinux64,
                "PLATFORM_STANDALONE_LINUX",
                UnityEditor.BuildTarget.StandaloneOSX,
                "PLATFORM_STANDALONE_OSX",
                () => { CopyScriptToAssetsFolder(Application.dataPath, "SimpleCSharpScript2.cs", " "); });
        }

        IEnumerator AssertSynchronizedWhenActiveBuildTargetChanges(
            UnityEditor.BuildTarget platformTarget,
            string platformDefine,
            UnityEditor.BuildTarget changeTarget,
            string changeDefine,
            Action action)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, platformTarget);
            CopyScriptToAssetsFolder(Application.dataPath, "SimpleCSharpScript.cs", k_EmptyCSharpScript);

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

            WaitForCondition(() => File.GetLastWriteTime(m_CsProjPath) > m_LastWritten);

            AssertProjectContainsDefine(m_CsProjPath, changeDefine);
            yield return null;
        }

        static void AssertProjectContainsDefine(string csProjPath, string expectedDefine)
        {
            var content = File.ReadAllText(csProjPath);
            Assert.IsTrue(Regex.IsMatch(content, $"<DefineConstants>.*;{expectedDefine}.*</DefineConstants>"));
        }

        delegate bool Condition();

        static void WaitForCondition(Condition condition)
        {
            var started = DateTime.Now;
            while (!condition())
            {
                if (DateTime.Now - started > k_Timeout)
                    throw new TimeoutException($"Timeout while waiting for c# project to be rewritten for {k_Timeout.TotalSeconds} seconds");
                Thread.Sleep(10);
            }
        }

        static readonly TimeSpan k_Timeout = TimeSpan.FromSeconds(5);
    }
}