using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace com.unity.ide.vscode.tests
{
    namespace SolutionGeneration
    {
        class Synchronization : SolutionGenerationTestBase
        {
            [Test]
            public void EmptyProject_WhenSynced_ShouldNotGenerateSolutionFile()
            {
                var synchronizer = m_Builder.WithAssemblies(new Assembly[0]).Build();

                synchronizer.Sync();

                Assert.False(m_Builder.ReadFile(synchronizer.SolutionFile()).Contains("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\")"), "Should not create project entry with no assemblies.");
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

                Assert.False(synchronizer.SolutionExists(), "Synchronizer should sync state with file system, after file has been deleted.");
            }

            [Test]
            public void ContentWithoutChanges_WhenSynced_DoesNotReSync()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();
                Assert.AreEqual(3, m_Builder.WriteTimes); // Once for csproj, once for solution, and once for vscode settings

                synchronizer.Sync();
                Assert.AreEqual(3, m_Builder.WriteTimes, "When content doesn't change we shouldn't re-sync");
            }

            [Test]
            public void AssemblyChanged_AfterSync_PerformsReSync()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();
                Assert.AreEqual(3, m_Builder.WriteTimes); // Once for csproj, once for solution, and once vscode settings

                m_Builder.WithAssemblies(new[] { new Assembly("Another", "path/to/Assembly.dll", new[] { "file.cs" }, new string[0], new Assembly[0], new string[0], AssemblyFlags.None) });

                synchronizer.Sync();
                Assert.AreEqual(5, m_Builder.WriteTimes, "Should re-sync the solution file and the csproj");
            }

            [Test]
            public void EmptySolutionFile_WhenSynced_OverwritesTheFile()
            {
                var synchronizer = m_Builder.Build();

                // Pre-seed solution file with empty property section
                var solutionText = "Microsoft Visual Studio Solution File, Format Version 11.00\n# Visual Studio 2010\nGlobal\nEndGlobal";
                m_Builder.WithSolutionText(solutionText);

                synchronizer.Sync();

                Assert.AreNotEqual(solutionText, m_Builder.ReadFile(synchronizer.SolutionFile()), "Should rewrite solution text");
            }

            [TestCase("dll")]
            [TestCase("asmdef")]
            public void AfterSync_WillResync_WhenReimportWithSpecialFileExtensions(string reimportedFile)
            {
                var synchronizer = m_Builder.Build();

                Assert.IsFalse(synchronizer.SyncIfNeeded(new List<string>(), new[] { $"reimport.{reimportedFile}" }), "Before sync has been called, we should not allow SyncIfNeeded");

                synchronizer.Sync();

                Assert.IsTrue(synchronizer.SyncIfNeeded(new List<string>(), new[] { $"reimport.{reimportedFile}" }));
            }

            [Test]
            public void AfterSync_WontResync_WhenReimportWithoutSpecialFileExtensions()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();

                Assert.IsFalse(synchronizer.SyncIfNeeded(new List<string>(), new[] { "ShouldNotSync.txt" }));
            }

            [Test]
            public void AfterSync_WontReimport_WithoutSpeciifcAffectedFileExtension()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();

                Assert.IsFalse(synchronizer.SyncIfNeeded(new List<string> { " reimport.random" }, new string[0]));
            }

            [Test, TestCaseSource(nameof(s_ExtensionsRequireReSync))]
            public void AfterSync_WillResync_WhenAffectedFileTypes(string fileExtension)
            {
                var synchronizer = m_Builder.Build();

                Assert.IsFalse(synchronizer.SyncIfNeeded(new List<string> { $"reimport.{fileExtension}" }, new string[0]), "Before sync has been called, we should not allow SyncIfNeeded");

                synchronizer.Sync();

                Assert.IsTrue(synchronizer.SyncIfNeeded(new List<string> { $"reimport.{fileExtension}" }, new string[0]));
            }

            static string[] s_ExtensionsRequireReSync =
            {
                "dll", "asmdef", "cs", "uxml", "uss", "shader", "compute", "cginc", "hlsl", "glslinc", "template", "raytrace"
            };
        }

        class Format : SolutionGenerationTestBase
        {
            [Test]
            public void SyncSettings_WhenSynced_HeaderMatchesVSVersion()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();

                string[] syncedSolutionText = m_Builder.ReadFile(synchronizer.SolutionFile()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                Assert.IsTrue(syncedSolutionText.Length >= 4);
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
                    @"        Unity|Any CPU = Unity|Any CPU",
                    @"    EndGlobalSection",
                    @"    GlobalSection(ProjectConfigurationPlatforms) = postSolution",
                    @"        {{{1}}}.Unity|Any CPU.ActiveCfg = Unity|Any CPU",
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
    }
}
