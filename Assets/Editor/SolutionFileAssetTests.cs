using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using System.IO;
using System;

namespace VSCodeEditor.Runtime_spec
{
    [TestFixture]
    [Serializable]
    public class SolutionProject
    {
        const string k_EmptyCSharpScript = @"
using UnityEngine;
public class SimpleCSharpScript : MonoBehaviour
{
  void Start()
  {
  }
}";
        [SerializeField]
        IGenerator m_ProjectGeneration;
        [SerializeField]
        List<string> m_GeneratedFiles = new List<string>();
        [SerializeField]
        string m_SolutionPath;

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

        [SetUp]
        public void SetUp() {
            var projectDirectory = Directory.GetParent(Application.dataPath).FullName;
            m_ProjectGeneration = new ProjectGeneration(projectDirectory);
        }

        [TearDown]
        public void Dispose()
        {
            m_GeneratedFiles.ForEach(File.Delete);
            m_GeneratedFiles.Clear();
            AssetDatabase.Refresh();
        }

        [SerializeField]
        string m_SolutionText;

        [UnityTest]
        public IEnumerator ResyncDoesNotChangeSolution()
        {
            CopyScriptToAssetsFolder(Application.dataPath, "foo.cs", " ");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            m_SolutionText = File.ReadAllText(s_SolutionFile);

            yield return new RecompileScripts(false);
            m_ProjectGeneration.Sync();

            string secondSyncSolutionTest = File.ReadAllText(s_SolutionFile);
            Assert.AreEqual(m_SolutionText, secondSyncSolutionTest, "Solution changed after second sync");

            yield return new RecompileScripts(false);
            m_ProjectGeneration.Sync();

            string thirdSyncSolutionText = File.ReadAllText(s_SolutionFile);
            Assert.AreEqual(m_SolutionText, thirdSyncSolutionText, "Solution changed after third sync");
        }

        [UnityTest]
        public IEnumerator EmptySolutionSettingsSucceeds()
        {
           string originalText = @"Microsoft Visual Studio Solution File, Format Version 10.00
# Visual Studio 2008
Global
EndGlobal";
            // Pre-seed solution file with empty property section
            File.WriteAllText(s_SolutionFile, originalText);

            CopyScriptToAssetsFolder(Application.dataPath, "foo.cs", " ");

            yield return new RecompileScripts(true);
            m_ProjectGeneration.Sync();

            string syncedSolutionText = File.ReadAllText(s_SolutionFile);
            Assert.True(syncedSolutionText.Length != 0);
        }

        void CopyScriptToAssetsFolder(string assetPath, string fileName, string content)
        {
            var targetFile = Path.Combine(assetPath, fileName);
            m_GeneratedFiles.Add(targetFile);
            File.WriteAllText(targetFile, content);
        }
    }
}