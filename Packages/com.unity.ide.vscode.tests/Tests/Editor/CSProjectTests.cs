using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEditor;

namespace VSCodeEditor.Tests
{
    namespace CSProjectGeneration
    {
        class Formatting : ProjectGenerationTestBase
        {
            [Test]
            public void AbsoluteSourceFilePaths_WillBeMadeRelativeToProjectDirectory()
            {
                var absoluteFilePath = MakeAbsolutePathTestImplementation("dimmer.cs");
                var synchronizer = m_Builder.WithAssemblyData(files: new[] { absoluteFilePath }).Build();

                synchronizer.Sync();

                var csprojContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                XmlDocument scriptProject = XMLUtilities.FromText(csprojContent);
                XMLUtilities.AssertCompileItemsMatchExactly(scriptProject, new[] { "dimmer.cs" });
            }

            [Test]
            public void DefaultSettings_WhenSynced_CreateWorkspaceFile()
            {
                var synchronizer = m_Builder.Build();
                synchronizer.Sync();

                var projectName = Path.GetFileName(synchronizer.ProjectDirectory); // This could be a public API
                var workspaceFile = Path.Combine(synchronizer.ProjectDirectory, $"{projectName}.code-workspace");
                var workspaceFileContent = m_Builder.ReadFile(workspaceFile);
                var content = @"{
	""folders"": [
		{
			""path"": "".""
		}
	]
}";
                Assert.That(workspaceFileContent, Is.EqualTo(content), "Workspace file content was not expected");
            }

            [Test]
            public void DefaultSyncSettings_WhenSynced_CreatesProjectFileFromDefaultTemplate()
            {
                var projectGuid = "ProjectGuid";
                var synchronizer = m_Builder.WithProjectGuid(projectGuid, m_Builder.Assembly).Build();

                synchronizer.Sync();

                var csprojContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                var defines = string.Join(";", new[] { "DEBUG", "TRACE" }.Concat(EditorUserBuildSettings.activeScriptCompilationDefines).Concat(m_Builder.Assembly.defines).Distinct().ToArray());
                var content = new[]
                {
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                    "<Project ToolsVersion=\"4.0\" DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
                    "  <PropertyGroup>",
                    $"    <LangVersion>{Helper.GetLangVersion()}</LangVersion>",
                    "  </PropertyGroup>",
                    "  <PropertyGroup>",
                    "    <Configuration Condition=\" '$(Configuration)' == '' \">Debug</Configuration>",
                    "    <Platform Condition=\" '$(Platform)' == '' \">AnyCPU</Platform>",
                    "    <ProductVersion>10.0.20506</ProductVersion>",
                    "    <SchemaVersion>2.0</SchemaVersion>",
                    "    <RootNamespace></RootNamespace>",
                    $"    <ProjectGuid>{{{projectGuid}}}</ProjectGuid>",
                    "    <OutputType>Library</OutputType>",
                    "    <AppDesignerFolder>Properties</AppDesignerFolder>",
                    $"    <AssemblyName>{m_Builder.Assembly.name}</AssemblyName>",
                    "    <TargetFrameworkVersion>v4.7.1</TargetFrameworkVersion>",
                    "    <FileAlignment>512</FileAlignment>",
                    "    <BaseDirectory>.</BaseDirectory>",
                    "  </PropertyGroup>",
                    "  <PropertyGroup Condition=\" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' \">",
                    "    <DebugSymbols>true</DebugSymbols>",
                    "    <DebugType>full</DebugType>",
                    "    <Optimize>false</Optimize>",
                    "    <OutputPath>Temp\\bin\\Debug\\</OutputPath>",
                    $"    <DefineConstants>{defines}</DefineConstants>",
                    "    <ErrorReport>prompt</ErrorReport>",
                    "    <WarningLevel>4</WarningLevel>",
                    "    <NoWarn>0169</NoWarn>",
                    "    <AllowUnsafeBlocks>False</AllowUnsafeBlocks>",
                    "  </PropertyGroup>",
                    "  <PropertyGroup>",
                    "    <NoConfig>true</NoConfig>",
                    "    <NoStdLib>true</NoStdLib>",
                    "    <AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences>",
                    "    <ImplicitlyExpandNETStandardFacades>false</ImplicitlyExpandNETStandardFacades>",
                    "    <ImplicitlyExpandDesignTimeFacades>false</ImplicitlyExpandDesignTimeFacades>",
                    "  </PropertyGroup>",
                    "  <ItemGroup>",
                    "     <Compile Include=\"test.cs\" />",
                    "  </ItemGroup>",
                    "  <Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" />",
                    "  <!-- To modify your build process, add your task inside one of the targets below and uncomment it.",
                    "       Other similar extension points exist, see Microsoft.Common.targets.",
                    "  <Target Name=\"BeforeBuild\">",
                    "  </Target>",
                    "  <Target Name=\"AfterBuild\">",
                    "  </Target>",
                    "  -->",
                    "</Project>",
                    ""
                };

                StringAssert.AreEqualIgnoringCase(string.Join("\r\n", content), csprojContent);
            }
        }

        class GUID : ProjectGenerationTestBase
        {
            [Test]
            public void ProjectReference_MatchAssemblyGUID()
            {
                string[] files = { "test.cs" };
                var assemblyB = new Assembly("Test", "Temp/Test.dll", files, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
                var assemblyA = new Assembly("Test2", "some/path/file.dll", files, new string[0], new[] { assemblyB }, new[] { "Library.ScriptAssemblies.Test.dll" }, AssemblyFlags.None);
                var synchronizer = m_Builder.WithAssemblies(new[] { assemblyA, assemblyB }).Build();

                synchronizer.Sync();

                var assemblyACSproject = SynchronizerBuilder.ProjectFilePath(assemblyA);
                var assemblyBCSproject = SynchronizerBuilder.ProjectFilePath(assemblyB);

                Assert.True(m_Builder.FileExists(assemblyACSproject));
                Assert.True(m_Builder.FileExists(assemblyBCSproject));

                XmlDocument scriptProject = XMLUtilities.FromText(m_Builder.ReadFile(assemblyACSproject));
                XmlDocument scriptPluginProject = XMLUtilities.FromText(m_Builder.ReadFile(assemblyBCSproject));

                var a = XMLUtilities.GetInnerText(scriptPluginProject, "/msb:Project/msb:PropertyGroup/msb:ProjectGuid");
                var b = XMLUtilities.GetInnerText(scriptProject, "/msb:Project/msb:ItemGroup/msb:ProjectReference/msb:Project");
                Assert.AreEqual(a, b);
            }
        }

        class Synchronization : ProjectGenerationTestBase
        {
            [Test]
            public void WontSynchronize_WhenNoFilesChanged()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();
                Assert.AreEqual(4, m_Builder.WriteTimes, "One write for solution, one write for csproj, one for workspace file, and one for vscode settings");

                synchronizer.Sync();
                Assert.AreEqual(4, m_Builder.WriteTimes, "No more files should be written");
            }

            [Test]
            public void WhenSynchronized_WillCreateCSProjectForAssembly()
            {
                var synchronizer = m_Builder.Build();

                Assert.IsFalse(m_Builder.FileExists(SynchronizerBuilder.ProjectFilePath(m_Builder.Assembly)));

                synchronizer.Sync();

                Assert.IsTrue(m_Builder.FileExists(SynchronizerBuilder.ProjectFilePath(m_Builder.Assembly)));
            }

            [Test]
            public void WhenSynchronized_WithTwoAssemblies_TwoProjectFilesAreGenerated()
            {
                var assemblyA = new Assembly("assemblyA", "path/to/a.dll", new[] { "file.cs" }, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
                var assemblyB = new Assembly("assemblyB", "path/to/b.dll", new[] { "file.cs" }, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
                var synchronizer = m_Builder.WithAssemblies(new[] { assemblyA, assemblyB }).Build();

                synchronizer.Sync();

                Assert.IsTrue(m_Builder.FileExists(SynchronizerBuilder.ProjectFilePath(assemblyA)));
                Assert.IsTrue(m_Builder.FileExists(SynchronizerBuilder.ProjectFilePath(assemblyB)));
            }

            [Test]
            public void NotInInternalizedPackage_WillResync()
            {
                var synchronizer = m_Builder.Build();
                synchronizer.Sync();
                var packageAsset = "packageAsset.cs";
                m_Builder.WithPackageAsset(packageAsset, false);
                Assert.IsTrue(synchronizer.SyncIfNeeded(new List<string>() { packageAsset }, new string[0]));
            }
        }

        class SourceFiles : ProjectGenerationTestBase
        {
            [Test]
            public void NoCSFile_CreatesNoProjectFile()
            {
                var synchronizer = m_Builder.WithAssemblyData(files: new string[0]).Build();

                synchronizer.Sync();

                Assert.False(
                    m_Builder.FileExists(SynchronizerBuilder.ProjectFilePath(m_Builder.Assembly)),
                    "Should not create csproj file for assembly with no cs file");
            }

            [Test]
            public void NotContributedAnAssembly_WillNotGetAdded()
            {
                var synchronizer = m_Builder.WithAssetFiles(new[] { "Assembly.hlsl" }).Build();

                synchronizer.Sync();

                var csprojContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                StringAssert.DoesNotContain("Assembly.hlsl", csprojContent);
            }

            [Test]
            public void MultipleSourceFiles_WillAllBeAdded()
            {
                var files = new[] { "fileA.cs", "fileB.cs", "fileC.cs" };
                var synchronizer = m_Builder
                    .WithAssemblyData(files: files)
                    .Build();

                synchronizer.Sync();

                var csprojectContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                var xmlDocument = XMLUtilities.FromText(csprojectContent);
                XMLUtilities.AssertCompileItemsMatchExactly(xmlDocument, files);
            }

            [Test]
            public void FullPathAsset_WillBeConvertedToRelativeFromProjectDirectory()
            {
                var assetPath = Path.Combine("Assets", "Asset.cs");
                var synchronizer = m_Builder
                    .WithAssemblyData(files: new[] { MakeAbsolutePathTestImplementation(assetPath) })
                    .Build();

                synchronizer.Sync();

                var csprojectContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                var xmlDocument = XMLUtilities.FromText(csprojectContent);
                XMLUtilities.AssertCompileItemsMatchExactly(xmlDocument, new[] { assetPath });
            }

            [Test]
            public void InRelativePackages_GetsPathResolvedCorrectly()
            {
                var assetPath = "/FullPath/ExamplePackage/Packages/Asset.cs";
                var assembly = new Assembly("ExamplePackage", "/FullPath/Example/ExamplePackage/ExamplePackage.dll", new[] { assetPath }, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
                var synchronizer = m_Builder
                    .WithAssemblies(new[] { assembly })
                    .WithPackageInfo(assetPath)
                    .Build();

                synchronizer.Sync();

                StringAssert.Contains(assetPath.NormalizePath(), m_Builder.ReadProjectFile(assembly));
            }

            [Test]
            public void InInternalizedPackage_WillBeAddedToCompileInclude()
            {
                var synchronizer = m_Builder.WithPackageAsset(m_Builder.Assembly.sourceFiles[0], true).Build();
                synchronizer.Sync();
                StringAssert.Contains(m_Builder.Assembly.sourceFiles[0], m_Builder.ReadProjectFile(m_Builder.Assembly));
            }

            [Test]
            public void NotInInternalizedPackage_WillBeAddedToCompileInclude()
            {
                var synchronizer = m_Builder.WithPackageAsset(m_Builder.Assembly.sourceFiles[0], false).Build();
                synchronizer.Sync();
                StringAssert.Contains(m_Builder.Assembly.sourceFiles[0], m_Builder.ReadProjectFile(m_Builder.Assembly));
            }

            [Test]
            public void CSharpFiles_WillBeIncluded()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();

                var assembly = m_Builder.Assembly;
                StringAssert.Contains(assembly.sourceFiles[0].NormalizePath(), m_Builder.ReadProjectFile(assembly));
            }

            [Test]
            public void NonCSharpFiles_AddedToNonCompileItems()
            {
                var nonCompileItems = new[]
                {
                    "UnityShader.uss",
                    "ComputerGraphic.cginc",
                    "Test.shader",
                };
                var synchronizer = m_Builder
                    .WithAssetFiles(nonCompileItems)
                    .AssignFilesToAssembly(nonCompileItems, m_Builder.Assembly)
                    .Build();

                synchronizer.Sync();

                var csprojectContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                var xmlDocument = XMLUtilities.FromText(csprojectContent);
                XMLUtilities.AssertCompileItemsMatchExactly(xmlDocument, m_Builder.Assembly.sourceFiles);
                XMLUtilities.AssertNonCompileItemsMatchExactly(xmlDocument, nonCompileItems);
            }

            [Test]
            public void UnsupportedExtensions_WillNotBeAdded()
            {
                var unsupported = new[] { "file.unsupported" };
                var synchronizer = m_Builder
                    .WithAssetFiles(unsupported)
                    .AssignFilesToAssembly(unsupported, m_Builder.Assembly)
                    .Build();

                synchronizer.Sync();

                var csprojectContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                var xmlDocument = XMLUtilities.FromText(csprojectContent);
                XMLUtilities.AssertCompileItemsMatchExactly(xmlDocument, m_Builder.Assembly.sourceFiles);
                XMLUtilities.AssertNonCompileItemsMatchExactly(xmlDocument, new string[0]);
            }

            [Test]
            public void UnsupportedExtension_IsOverWrittenBy_UserSupportedExtensions()
            {
                var unsupported = new[] { "file.unsupported" };
                var synchronizer = m_Builder
                    .WithAssetFiles(unsupported)
                    .AssignFilesToAssembly(unsupported, m_Builder.Assembly)
                    .WithUserSupportedExtensions(new[] { "unsupported" })
                    .Build();
                synchronizer.Sync();
                var xmlDocument = XMLUtilities.FromText(m_Builder.ReadProjectFile(m_Builder.Assembly));
                XMLUtilities.AssertNonCompileItemsMatchExactly(xmlDocument, unsupported);

            }

            [TestCase(@"path/com.unity.cs")]
            [TestCase(@"../path/file.cs")]
            public void IsValidFileName(string filePath)
            {
                var synchronizer = m_Builder
                    .WithAssemblyData(files: new[] { filePath })
                    .Build();

                synchronizer.Sync();

                var csprojContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                StringAssert.Contains(filePath.NormalizePath(), csprojContent);
            }

            [Test]
            public void AddedAfterSync_WillBeSynced()
            {
                var synchronizer = m_Builder.Build();
                synchronizer.Sync();
                const string newFile = "Newfile.cs";
                var newFileArray = new List<string> { newFile };
                m_Builder.WithAssemblyData(files: m_Builder.Assembly.sourceFiles.Concat(newFileArray).ToArray());

                Assert.True(synchronizer.SyncIfNeeded(newFileArray, new string[0]), "Should sync when file in assembly changes");

                var csprojContentAfter = m_Builder.ReadProjectFile(m_Builder.Assembly);
                StringAssert.Contains(newFile, csprojContentAfter);
            }

            [Test]
            public void Moved_WillBeResynced()
            {
                var synchronizer = m_Builder.Build();
                synchronizer.Sync();
                var filesBefore = m_Builder.Assembly.sourceFiles;
                const string newFile = "Newfile.cs";
                var newFiles = new List<string> { newFile };
                m_Builder.WithAssemblyData(files: newFiles.ToArray());

                Assert.True(synchronizer.SyncIfNeeded(newFiles, new string[0]), "Should sync when file in assembly changes");

                var csprojContentAfter = m_Builder.ReadProjectFile(m_Builder.Assembly);
                StringAssert.Contains(newFile, csprojContentAfter);
                foreach (var file in filesBefore)
                {
                    StringAssert.DoesNotContain(file, csprojContentAfter);
                }
            }

            [Test]
            public void Deleted_WillBeRemoved()
            {
                var filesBefore = new[]
                {
                    "WillBeDeletedScript.cs",
                    "Script.cs",
                };
                var synchronizer = m_Builder.WithAssemblyData(files: filesBefore).Build();

                synchronizer.Sync();

                var csprojContentBefore = m_Builder.ReadProjectFile(m_Builder.Assembly);
                StringAssert.Contains(filesBefore[0], csprojContentBefore);
                StringAssert.Contains(filesBefore[1], csprojContentBefore);

                var filesAfter = filesBefore.Skip(1).ToArray();
                m_Builder.WithAssemblyData(files: filesAfter);

                Assert.True(synchronizer.SyncIfNeeded(filesAfter.ToList(), new string[0]), "Should sync when file in assembly changes");

                var csprojContentAfter = m_Builder.ReadProjectFile(m_Builder.Assembly);
                StringAssert.Contains(filesAfter[0], csprojContentAfter);
                StringAssert.DoesNotContain(filesBefore[0], csprojContentAfter);
            }

            [Test, TestCaseSource(nameof(s_BuiltinSupportedExtensionsForSourceFiles))]
            public void BuiltinSupportedExtensions_InsideAssemblySourceFiles_WillBeAddedToCompileItems(string fileExtension)
            {
                var compileItem = new[] { "file.cs", $"anotherFile.{fileExtension}" };
                var synchronizer = m_Builder.WithAssemblyData(files: compileItem).Build();

                synchronizer.Sync();

                var csprojContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                XmlDocument scriptProject = XMLUtilities.FromText(csprojContent);
                XMLUtilities.AssertCompileItemsMatchExactly(scriptProject, compileItem);
            }

            static string[] s_BuiltinSupportedExtensionsForSourceFiles =
            {
                "asmdef", "cs", "uxml", "uss", "shader", "compute", "cginc", "hlsl", "glslinc", "template", "raytrace"
            };

            [Test, TestCaseSource(nameof(s_BuiltinSupportedExtensionsForAssets))]
            public void BuiltinSupportedExtensions_InsideAssetFolder_WillBeAddedToNonCompileItems(string fileExtension)
            {
                var nonCompileItem = new[] { $"anotherFile.{fileExtension}" };
                var synchronizer = m_Builder
                    .WithAssetFiles(files: nonCompileItem)
                    .AssignFilesToAssembly(nonCompileItem, m_Builder.Assembly)
                    .Build();

                synchronizer.Sync();

                var csprojContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                XmlDocument scriptProject = XMLUtilities.FromText(csprojContent);
                XMLUtilities.AssertCompileItemsMatchExactly(scriptProject, m_Builder.Assembly.sourceFiles);
                XMLUtilities.AssertNonCompileItemsMatchExactly(scriptProject, nonCompileItem);
            }

            static string[] s_BuiltinSupportedExtensionsForAssets =
            {
                "uxml", "uss", "shader", "compute", "cginc", "hlsl", "glslinc", "template", "raytrace"
            };
        }

        class CompilerOptions : ProjectGenerationTestBase
        {
            [Test]
            public void AllowUnsafeFromResponseFile_AddBlockToCsproj()
            {
                const string responseFile = "csc.rsp";
                var synchronizer = m_Builder
                    .WithResponseFileData(m_Builder.Assembly, responseFile, _unsafe: true)
                    .Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                StringAssert.Contains("<AllowUnsafeBlocks>True</AllowUnsafeBlocks>", csprojFileContents);
            }

            [TestCase(new object[] { "C:/Analyzer.dll" })]
            [TestCase(new object[] { "C:/Analyzer.dll", "C:/Analyzer2.dll" })]
            [TestCase(new object[] { "../Analyzer.dll" })]
            [TestCase(new object[] { "../Analyzer.dll", "C:/Analyzer2.dll" })]
            public void AddAnalyzers(params string[] paths)
            {
                var combined = string.Join(";", paths);
                const string additionalFileTemplate = @"    <Analyzer Include=""{0}"" />";
                var expectedOutput = paths.Select(x => string.Format(additionalFileTemplate, MakeAbsolutePath(x).NormalizePath())).ToArray();

                CheckOtherArgument(new[] { $"-a:{combined}" }, expectedOutput);
                CheckOtherArgument(new[] { $"-analyzer:{combined}" }, expectedOutput);
                CheckOtherArgument(new[] { $"/a:{combined}" }, expectedOutput);
                CheckOtherArgument(new[] { $"/analyzer:{combined}" }, expectedOutput);
            }

            [Test]
            public void CheckDefaultWarningLevel()
            {
                CheckOtherArgument(Array.Empty<string>(), "<WarningLevel>4</WarningLevel>");
            }

            [Test]
            public void CheckLangVersion()
            {
                CheckOtherArgument(new[] { "-langversion:7.2" }, "<LangVersion>7.2</LangVersion>");
            }

            [Test]
            public void CheckDefaultLangVersion()
            {
                CheckOtherArgument(Array.Empty<string>(), $"<LangVersion>{Helper.GetLangVersion()}</LangVersion>");
            }

            public void CheckOtherArgument(string[] argumentString, params string[] expectedContents)
            {
                const string responseFile = "csc.rsp";
                var synchronizer = m_Builder
                    .WithResponseFileData(m_Builder.Assembly, responseFile, otherArguments: argumentString)
                    .Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                foreach (string expectedContent in expectedContents)
                {
                    StringAssert.Contains(
                        expectedContent,
                        csprojFileContents,
                        $"Arguments: {string.Join(";", argumentString)} {Environment.NewLine}"
                        + Environment.NewLine
                        + $"Expected: {expectedContent.Replace("\r", "\\r").Replace("\n", "\\n")}"
                        + Environment.NewLine
                        + $"Actual: {csprojFileContents.Replace("\r", "\\r").Replace("\n", "\\n")}");
                }
            }

            [Test]
            public void AllowUnsafeFromAssemblySettings_AddBlockToCsproj()
            {
                var synchronizer = m_Builder
                    .WithAssemblyData(unsafeSettings: true)
                    .Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                StringAssert.Contains("<AllowUnsafeBlocks>True</AllowUnsafeBlocks>", csprojFileContents);
            }
        }

        class References : ProjectGenerationTestBase
        {
#if UNITY_2020_2_OR_NEWER
            [Test]
            public void RoslynAnalyzerDlls_WillBeIncluded()
            {
                var roslynAnalyzerDllPath = "Assets/RoslynAnalyzer.dll";
                var synchronizer = m_Builder.WithRoslynAnalyzers(new[] { roslynAnalyzerDllPath }).Build();

                synchronizer.Sync();

                string projectFile = m_Builder.ReadProjectFile(m_Builder.Assembly);
                XmlDocument projectFileXml = XMLUtilities.FromText(projectFile);
                XMLUtilities.AssertAnalyzerItemsMatchExactly(projectFileXml, new[] { MakeAbsolutePath(roslynAnalyzerDllPath).NormalizePath() });
            }

            [Test]
            public void RoslynAnalyzerRulesetFiles_WillBeIncluded()
            {
                var roslynAnalyzerRuleSetPath = "Assets/RoslynRuleSet.ruleset";

                m_Builder.WithAssemblyData(files: new[] {"file.cs"}).WithRoslynAnalyzerRulesetPath(roslynAnalyzerRuleSetPath).Build().Sync();
                var csProjectFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                XmlDocument csProjectXmlFile = XMLUtilities.FromText(csProjectFileContents);
                XMLUtilities.AssertAnalyzerRuleSetsMatchExactly(csProjectXmlFile, MakeAbsolutePath(roslynAnalyzerRuleSetPath).NormalizePath());
            }
#endif

            [Test]
            public void Containing_PathWithSpaces_IsParsedCorrectly()
            {
                const string responseFile = "csc.rsp";
                var synchronizer = m_Builder
                    .WithResponseFileData(m_Builder.Assembly, responseFile, fullPathReferences: new[] { "Folder/Path With Space/Goodbye.dll" })
                    .Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                Assert.That(csprojFileContents, Does.Match($"<Reference Include=\"Goodbye\">\\W*<HintPath>{Regex.Escape(MakeAbsolutePathTestImplementation("Folder/Path With Space/Goodbye.dll").NormalizePath())}\\W*</HintPath>\\W*</Reference>"));
            }

            [Test]
            public void Containing_PathWithDotCS_IsParsedCorrectly()
            {
                var assembly = new Assembly("name", "/path/with.cs/assembly.dll", new[] { "file.cs" }, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
                var synchronizer = m_Builder
                    .WithAssemblyData(assemblyReferences: new[] { assembly })
                    .Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                Assert.That(csprojFileContents, Does.Match($@"<ProjectReference Include=""{assembly.name}\.csproj"">[\S\s]*?</ProjectReference>"));
            }

            [Test]
            public void Multiple_AreAdded()
            {
                const string responseFile = "csc.rsp";
                var synchronizer = m_Builder
                    .WithResponseFileData(m_Builder.Assembly, responseFile, fullPathReferences: new[] { "MyPlugin.dll", "Hello.dll" })
                    .Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);

                Assert.That(csprojFileContents, Does.Match($@"<Reference Include=""Hello"">\W*<HintPath>{Regex.Escape(MakeAbsolutePathTestImplementation("Hello.dll").NormalizePath())}</HintPath>\W*</Reference>"));
                Assert.That(csprojFileContents, Does.Match($@"<Reference Include=""MyPlugin"">\W*<HintPath>{Regex.Escape(MakeAbsolutePathTestImplementation("MyPlugin.dll").NormalizePath())}</HintPath>\W*</Reference>"));
            }

            [Test]
            public void AssemblyReference_IsAdded()
            {
                string[] files = { "test.cs" };
                var assemblyReferences = new[]
                {
                    new Assembly("MyPlugin", "/some/path/MyPlugin.dll", files, new string[0], new Assembly[0], new string[0], AssemblyFlags.None),
                    new Assembly("Hello", "/some/path/Hello.dll", files, new string[0], new Assembly[0], new string[0], AssemblyFlags.None),
                };
                var synchronizer = m_Builder.WithAssemblyData(assemblyReferences: assemblyReferences).Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                Assert.That(csprojFileContents, Does.Match($@"<ProjectReference Include=""{assemblyReferences[0].name}\.csproj"">[\S\s]*?</ProjectReference>"));
                Assert.That(csprojFileContents, Does.Match($@"<ProjectReference Include=""{assemblyReferences[1].name}\.csproj"">[\S\s]*?</ProjectReference>"));
            }

            [Test]
            public void AssemblyReferenceFromInternalizedPackage_IsAddedAsReference()
            {
                string[] files = { "test.cs" };
                var assemblyReferences = new[]
                {
                    new Assembly("MyPlugin", "/some/path/MyPlugin.dll".NormalizePath(), files, new string[0], new Assembly[0], new string[0], AssemblyFlags.None),
                    new Assembly("Hello", "/some/path/Hello.dll".NormalizePath(), files, new string[0], new Assembly[0], new string[0], AssemblyFlags.None),
                };
                var synchronizer = m_Builder.WithPackageAsset(files[0], true).WithAssemblyData(assemblyReferences: assemblyReferences).Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                Assert.That(csprojFileContents, Does.Not.Match($@"<ProjectReference Include=""{assemblyReferences[0].name}\.csproj"">[\S\s]*?</ProjectReference>"));
                Assert.That(csprojFileContents, Does.Not.Match($@"<ProjectReference Include=""{assemblyReferences[1].name}\.csproj"">[\S\s]*?</ProjectReference>"));
                Assert.That(csprojFileContents, Does.Match($"<Reference Include=\"{assemblyReferences[0].name}\">\\W*<HintPath>{Regex.Escape(assemblyReferences[0].outputPath)}</HintPath>\\W*</Reference>"));
                Assert.That(csprojFileContents, Does.Match($"<Reference Include=\"{assemblyReferences[1].name}\">\\W*<HintPath>{Regex.Escape(assemblyReferences[1].outputPath)}</HintPath>\\W*</Reference>"));
            }

            [Test]
            public void CompiledAssemblyReference_IsAdded()
            {
                var compiledAssemblyReferences = new[]
                {
                    "/some/path/MyPlugin.dll".NormalizePath(),
                    "/some/other/path/Hello.dll".NormalizePath(),
                };
                var synchronizer = m_Builder.WithAssemblyData(compiledAssemblyReferences: compiledAssemblyReferences).Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                Assert.That(csprojFileContents, Does.Match($"<Reference Include=\"Hello\">\\W*<HintPath>{Regex.Escape(compiledAssemblyReferences[1])}</HintPath>\\W*</Reference>"));
                Assert.That(csprojFileContents, Does.Match($"<Reference Include=\"MyPlugin\">\\W*<HintPath>{Regex.Escape(compiledAssemblyReferences[0])}</HintPath>\\W*</Reference>"));
            }

            [Test]
            public void ProjectReference_FromLibraryReferences_IsAdded()
            {
                var projectAssembly = new Assembly("ProjectAssembly", "/path/to/project.dll", new[] { "test.cs" }, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
                var synchronizer = m_Builder.WithAssemblyData(assemblyReferences: new[] { projectAssembly }).Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                Assert.That(csprojFileContents, Does.Not.Match($"<Reference Include=\"{projectAssembly.name}\">\\W*<HintPath>{projectAssembly.outputPath}</HintPath>\\W*</Reference>"));
            }

            [Test]
            public void NotInAssembly_WontBeAdded()
            {
                var fileOutsideAssembly = "some.dll";
                var fileArray = new[] { fileOutsideAssembly };
                var synchronizer = m_Builder.WithAssetFiles(fileArray).Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                StringAssert.DoesNotContain("some.dll", csprojFileContents);
            }
        }

        class Defines : ProjectGenerationTestBase
        {
            [Test]
            public void ResponseFiles_CanAddDefines()
            {
                const string responseFile = "csc.rsp";
                var synchronizer = m_Builder
                    .WithResponseFileData(m_Builder.Assembly, responseFile, defines: new[] { "DEF1", "DEF2" })
                    .Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                Assert.That(csprojFileContents, Does.Match("<DefineConstants>.*;DEF1.*</DefineConstants>"));
                Assert.That(csprojFileContents, Does.Match("<DefineConstants>.*;DEF2.*</DefineConstants>"));
            }

            [Test]
            public void Assembly_CanAddDefines()
            {
                var synchronizer = m_Builder.WithAssemblyData(defines: new[] { "DEF1", "DEF2" }).Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                Assert.That(csprojFileContents, Does.Match("<DefineConstants>.*;DEF1.*</DefineConstants>"));
                Assert.That(csprojFileContents, Does.Match("<DefineConstants>.*;DEF2.*</DefineConstants>"));
            }

            [Test]
            public void ResponseFileDefines_OverrideRootResponseFile()
            {
                string[] files = { "test.cs" };
                var assemblyA = new Assembly("A", "some/root/file.dll", files, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
                var assemblyB = new Assembly("B", "some/root/child/anotherfile.dll", files, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
                var synchronizer = m_Builder
                    .WithAssemblies(new[] { assemblyA, assemblyB })
                    .WithResponseFileData(assemblyA, "A.rsp", defines: new[] { "RootedDefine" })
                    .WithResponseFileData(assemblyB, "B.rsp", defines: new[] { "CHILD_DEFINE" })
                    .Build();

                synchronizer.Sync();

                var aCsprojContent = m_Builder.ReadProjectFile(assemblyA);
                var bCsprojContent = m_Builder.ReadProjectFile(assemblyB);
                Assert.That(bCsprojContent, Does.Match("<DefineConstants>.*;CHILD_DEFINE.*</DefineConstants>"));
                Assert.That(bCsprojContent, Does.Not.Match("<DefineConstants>.*;RootedDefine.*</DefineConstants>"));
                Assert.That(aCsprojContent, Does.Not.Match("<DefineConstants>.*;CHILD_DEFINE.*</DefineConstants>"));
                Assert.That(aCsprojContent, Does.Match("<DefineConstants>.*;RootedDefine.*</DefineConstants>"));
            }
        }

        class OnGenerationProject : ProjectGenerationTestBase 
        {
            static bool m_HasCalledOnGeneratedCSProject = false;

            static bool m_isRunningThisTest = false;

            public class OnGenerationCallbacks : AssetPostprocessor 
            {
                public static string OnGeneratedCSProject(string path, string content) 
                {
                    if(!m_isRunningThisTest) return content;

                    m_HasCalledOnGeneratedCSProject = true;
                    return content.Replace("fileA", "fileD");
                }
            }

            [Test]
            public void OnGenerationProject_Called()
            {
                m_isRunningThisTest = true;
                
                var synchronizer = m_Builder.Build();
                synchronizer.Sync();

                Assert.True(m_HasCalledOnGeneratedCSProject);

                m_isRunningThisTest = false;
            }

            [Test]
            public void OnGenerationProject_Modifed()
            {
                m_isRunningThisTest = true;

                var files = new[] { "fileA.cs", "fileB.cs", "fileC.cs" };
                var synchronizer = m_Builder
                    .WithAssemblyData(files: files)
                    .Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                StringAssert.DoesNotContain("fileA.cs", csprojFileContents);
                StringAssert.Contains("fileD.cs", csprojFileContents);
                Assert.True(m_HasCalledOnGeneratedCSProject);

                m_isRunningThisTest = false;

            }
        }
    }
}
