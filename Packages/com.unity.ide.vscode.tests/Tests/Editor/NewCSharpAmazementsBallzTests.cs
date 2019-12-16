using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;
using VSCodeEditor;
using VSCodeEditor.Tests;
using VSCodeEditor.Tests.CSProjectGeneration;

namespace NewCSharpAmazementsBallzTests
{
    class ExcludeAsmdefUnit
    {
        public class Folders
        {
            public string current;
            public List<string> paths;
            public List<string> expected;

            public override string ToString()
            {
                return $"current: {current}, paths: {string.Join(",", paths)}";
            }
        }

        [TestCaseSource("DivideCases")]
        public void Bla(Folders folders)
        {
            var actualExcluded = new NewCSharpAmazementsBallz(Directory.GetParent(Application.dataPath).FullName).GetExcludePaths(folders.current, folders.paths);
            CollectionAssert.AreEqual(folders.expected, actualExcluded);
        }

        static object[] DivideCases =
        {
            new Folders { current = "/folder", paths = new List<string> { "" }, expected = new List<string>() },
            new Folders { current = "/folder", paths = new List<string> { "/Hello" }, expected = new List<string>() },
            new Folders { current = "/folder", paths = new List<string> { "/folder" }, expected = new List<string>() },
            new Folders { current = "/folder", paths = new List<string> { "/Hello", "/folder/subfolder" }, expected = new List<string> {"/folder/subfolder"} },
        };
    }

    class ExcludeAsmdefFromSync : SolutionGenerationTestBase
    {
        [Test]
        public void Containing_PathWithDotCS_IsParsedCorrectly()
        {
            string[] files = { "test.cs" };
            var assemblyB = new Assembly("Test", "some/path/Test.dll", files, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
            var assemblyA = new Assembly("Test2", "some/path/subfolder/Test2.dll", files, new string[0], new[] { assemblyB }, new string[0], AssemblyFlags.None);
            // var synchronizer = m_Builder
            //     .WithAssemblyData(assemblyReferences: new[] { assembly })
            //     .Build();

            // synchronizer.Sync();

            var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
            Assert.IsTrue(csprojFileContents.MatchesRegex($"<Compile Remove=\"assembly\" />"));
        }
    }
}
