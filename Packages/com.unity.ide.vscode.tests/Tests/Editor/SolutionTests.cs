using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEditor;


namespace VSCodeEditor.Tests
{
    namespace SolutionGeneration
    {
        class Synchronization : ProjectGenerationTestBase
        {
            [Test]
            public void EmptyProject_WhenSynced_ShouldNotGenerateSolutionFile()
            {
                var synchronizer = m_Builder.WithAssemblies(new Assembly[0]).Build();

                synchronizer.Sync();

                Assert.False(
                    m_Builder.ReadFile(synchronizer.SolutionFile()).Contains("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\")"),
                    "Should not create project entry with no assemblies.");
            }

            [Test]
            public void NoSolution_WhenSynced_CreatesSolutionFile()
            {
                var synchronizer = m_Builder.Build();

                Assert.False(synchronizer.SolutionExists(), "Should not create solution file before we call sync.");

                synchronizer.Sync();

                Assert.True(synchronizer.SolutionExists(), "Should create solution file.");
            }

            [Test]
            public void WhenSynced_ThenDeleted_SolutionFileDoesNotExist()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();
                m_Builder.DeleteFile(synchronizer.SolutionFile());

                Assert.False(
                    synchronizer.SolutionExists(),
                    "Synchronizer should sync state with file system, after file has been deleted.");
            }

            [Test]
            public void ContentWithoutChanges_WhenSynced_DoesNotReSync()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();
                Assert.AreEqual(4, m_Builder.WriteTimes, "Once for csproj, once for solution, once for workspace, and once for vscode settings");

                synchronizer.Sync();
                Assert.AreEqual(
                    4,
                    m_Builder.WriteTimes,
                    "When content doesn't change we shouldn't re-sync");
            }

            [Test]
            public void AssemblyChanged_AfterSync_PerformsReSync()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();
                Assert.AreEqual(4, m_Builder.WriteTimes, "Once for csproj, once for solution, once for workspace file, and once vscode settings");

                m_Builder.WithAssemblies(new[] { new Assembly("Another", "path/to/Assembly.dll", new[] { "file.cs" }, new string[0], new Assembly[0], new string[0], AssemblyFlags.None) });

                synchronizer.Sync();
                Assert.AreEqual(6, m_Builder.WriteTimes, "Should re-sync the solution file and the csproj");
            }

            [Test]
            public void EmptySolutionFile_WhenSynced_OverwritesTheFile()
            {
                var synchronizer = m_Builder.Build();

                // Pre-seed solution file with empty property section
                var solutionText = "Microsoft Visual Studio Solution File, Format Version 11.00\n# Visual Studio 2010\nGlobal\nEndGlobal";
                m_Builder.WithSolutionText(solutionText);

                synchronizer.Sync();

                Assert.AreNotEqual(
                    solutionText,
                    m_Builder.ReadFile(synchronizer.SolutionFile()),
                    "Should rewrite solution text");
            }

            [TestCase("dll")]
            [TestCase("asmdef")]
            public void AfterSync_WillResync_WhenReimportWithSpecialFileExtensions(string reimportedFile)
            {
                var synchronizer = m_Builder.Build();

                Assert.That(synchronizer.SyncIfNeeded(new List<string>(), new[] {$"reimport.{reimportedFile}"}));
            }

            [Test]
            public void AfterSync_WontResync_WhenReimportWithoutSpecialFileExtensions()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();

                Assert.IsFalse(synchronizer.SyncIfNeeded(new List<string>(), new[] {"ShouldNotSync.txt"}));
            }

            [Test]
            public void AfterSync_WontReimport_WithoutSpecificAffectedFileExtension()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();

                Assert.IsFalse(synchronizer.SyncIfNeeded(new List<string> {" reimport.random"}, new string[0]));
            }

            [Test]
            public void AfterSync_WillReimportSolutionFile_WhenNewAssemblyIsBeingAdded()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();

                var newAssembly = new Assembly(
                    "MyAssembly",
                    "myOutput/path",
                    new[] {"MyFile.cs"},
                    new string[0],
                    new Assembly[0],
                    new string[0],
                    AssemblyFlags.None);
                var newAssemblies = new[] {m_Builder.Assembly, newAssembly};
                m_Builder.WithAssemblies(newAssemblies);
                m_Builder.AssignFilesToAssembly(new[] {"MyFile.cs"}, newAssembly);

                synchronizer.SyncIfNeeded(new List<string> {"MyFile.cs"}, new string[0]);

                var solutionFileContent = m_Builder.ReadFile(synchronizer.SolutionFile());
                StringAssert.Contains(
                    "Project(\"{}\") = \"MyAssembly\", \"MyAssembly.csproj\"",
                    solutionFileContent,
                    "After synchronizing a new file from a new Assembly. The new assembly should be added to solution file.");
            }

            [Test]
            public void AssetNotBelongingToAssembly_WillSync_ButWontWriteFiles()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync(); // Generate solution and csproj

                Assert.AreEqual(4, m_Builder.WriteTimes, "Should have written csproj, sln, workspace, and vscode setting files");

                m_Builder.WithAssetFiles(new[] {"X.cs"});

                var res = synchronizer.SyncIfNeeded(new List<string> {"X.cs"}, new string[0]);

                Assert.IsTrue(res, "Should support file extension");

                Assert.AreEqual(
                    4,
                    m_Builder.WriteTimes,
                    "Should not have rewritten neither csproj, sln, workspace, nor vscode setting files");
            }

            [Test]
            public void AssetBelongingToAssemblyWithNoName_WillSync_ButWontWriteFiles()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync(); // Generate solution and csproj

                Assert.AreEqual(4, m_Builder.WriteTimes, "Should have written csproj, sln, workspace, and vscode setting files");

                string[] files = {"X.cs"};
                m_Builder
                    .WithAssetFiles(files)
                    .AssignFilesToAssembly(
                        files,
                        new Assembly("", "", files, new string[0], new Assembly[0], new string[0], AssemblyFlags.EditorAssembly));

                var res = synchronizer.SyncIfNeeded(new List<string> {"X.cs"}, new string[0]);

                Assert.IsTrue(res, "Should support file extension");

                Assert.AreEqual(
                    4,
                    m_Builder.WriteTimes,
                    "Should only rewrite sln file");
            }

            [Test, TestCaseSource(nameof(s_ExtensionsRequireReSync))]
            public void WillResync_WhenAffectedFileTypes(string fileExtension)
            {
                var synchronizer = m_Builder.Build();

                Assert.That(synchronizer.SyncIfNeeded(new List<string> {$"reimport.{fileExtension}"}, new string[0]));
            }

            static string[] s_ExtensionsRequireReSync =
            {
                "dll", "asmdef", "cs", "uxml", "uss", "shader", "compute", "cginc", "hlsl", "glslinc", "template",
                "raytrace"
            };
        }

        class Format : ProjectGenerationTestBase
        {
            [Test]
            public void SyncSettings_WhenSynced_HeaderMatchesVSVersion()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();

                string[] syncedSolutionText = m_Builder.ReadFile(synchronizer.SolutionFile()).Split(new[] {"\r\n"}, StringSplitOptions.None);
                Assert.That(syncedSolutionText.Length, Is.GreaterThanOrEqualTo(4));
                Assert.AreEqual("", syncedSolutionText[0]);
                Assert.AreEqual("Microsoft Visual Studio Solution File, Format Version 11.00", syncedSolutionText[1]);
                Assert.AreEqual("# Visual Studio 2010", syncedSolutionText[2]);
            }

            [Test]
            public void DefaultSyncSettings_WhenSynced_CreatesSolutionFileFromDefaultTemplate()
            {
                var solutionGUID = "SolutionGUID";
                var projectGUID = "ProjectGUID";
                var synchronizer = m_Builder
                    .WithSolutionGuid(solutionGUID)
                    .WithProjectGuid(projectGUID, m_Builder.Assembly)
                    .Build();

                // solutionguid, solutionname, projectguid
                var solutionExpected = string.Join("\r\n", new[]
                {
                    @"",
                    @"Microsoft Visual Studio Solution File, Format Version 11.00",
                    @"# Visual Studio 2010",
                    @"Project(""{{{0}}}"") = ""{2}"", ""{2}.csproj"", ""{{{1}}}""",
                    @"EndProject",
                    @"Global",
                    @"    GlobalSection(SolutionConfigurationPlatforms) = preSolution",
                    @"        Debug|Any CPU = Debug|Any CPU",
                    @"    EndGlobalSection",
                    @"    GlobalSection(ProjectConfigurationPlatforms) = postSolution",
                    @"        {{{1}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
                    @"        {{{1}}}.Debug|Any CPU.Build.0 = Debug|Any CPU",
                    @"    EndGlobalSection",
                    @"    GlobalSection(SolutionProperties) = preSolution",
                    @"        HideSolutionNode = FALSE",
                    @"    EndGlobalSection",
                    @"EndGlobal",
                    @""
                }).Replace("    ", "\t");

                var solutionTemplate = string.Format(
                    solutionExpected,
                    solutionGUID,
                    projectGUID,
                    m_Builder.Assembly.name);

                synchronizer.Sync();

                Assert.AreEqual(solutionTemplate, m_Builder.ReadFile(synchronizer.SolutionFile()));
            }
        }

        class OnGenerationSolution : ProjectGenerationTestBase 
        {
            static bool m_HasCalledOnGeneratedSlnSolution = false;

            const string solutionGUID = "SolutionGUID";
            const string newSolutionGUID = "1234567";

            // This is here because the fact this OnGenerationCallbacks is around 
            // will cause it to get executed.
            static bool m_isRunningThisTest = false;

            public class OnGenerationCallbacks : AssetPostprocessor 
            {
                public static string OnGeneratedSlnSolution(string path, string content)
                {
                    if(!m_isRunningThisTest) return content;

                    m_HasCalledOnGeneratedSlnSolution = true;
                    return content.Replace(solutionGUID, newSolutionGUID);
                }
            }

            [Test]
            public void OnGenerationSolution_Called()
            {
                m_isRunningThisTest = true;

                var synchronizer = m_Builder.Build();
                synchronizer.Sync();

                Assert.True(m_HasCalledOnGeneratedSlnSolution);

                m_isRunningThisTest = false;
            }

            [Test]
            public void OnGenerationSolution_Modifed()
            {
                m_isRunningThisTest = true;

                var synchronizer = m_Builder
                    .WithSolutionGuid(solutionGUID)
                    .Build();

                synchronizer.Sync();

                var slnFileContents = m_Builder.ReadFile(synchronizer.SolutionFile());
                StringAssert.DoesNotContain(solutionGUID, slnFileContents);
                StringAssert.Contains(newSolutionGUID, slnFileContents);
                Assert.True(m_HasCalledOnGeneratedSlnSolution);

                m_isRunningThisTest = false;
            }
        }
    }
}