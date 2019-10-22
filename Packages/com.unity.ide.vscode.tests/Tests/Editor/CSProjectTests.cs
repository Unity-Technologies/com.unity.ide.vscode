using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace VSCodeEditor.Tests
{
    namespace CSProjectGeneration
    {
        static class Util
        {
            internal static bool MatchesRegex(this string input, string pattern)
            {
                return Regex.Match(input, pattern).Success;
            }
        }

        class Formatting : SolutionGenerationTestBase
        {
            [TestCase(@"x & y.cs", @"x &amp; y.cs")]
            [TestCase(@"x ' y.cs", @"x &apos; y.cs")]
            [TestCase(@"Dimmer&\foo.cs", @"Dimmer&amp;\foo.cs")]
            [TestCase(@"C:\Dimmer/foo.cs", @"C:\Dimmer\foo.cs")]
            public void Escape_SpecialCharsInFileName(string illegalFormattedFileName, string expectedFileName)
            {
                var synchronizer = m_Builder.WithAssemblyData(files: new[] { illegalFormattedFileName }).Build();

                synchronizer.Sync();

                var csprojContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                StringAssert.DoesNotContain(illegalFormattedFileName, csprojContent);
                StringAssert.Contains(expectedFileName, csprojContent);
            }

            [Test]
            public void NoExtension_IsNotValid()
            {
                var validFile = "dimmer.cs";
                var invalidFile = "foo";
                var file = new[] { validFile, invalidFile };
                var synchronizer = m_Builder.WithAssemblyData(files: file).Build();

                synchronizer.Sync();

                var csprojContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                XmlDocument scriptProject = XMLUtilities.FromText(csprojContent);
                XMLUtilities.AssertCompileItemsMatchExactly(scriptProject, new[] { validFile });
            }

            [Test]
            public void AbsoluteSourceFilePaths_WillBeMadeRelativeToProjectDirectory()
            {
                var absoluteFilePath = Path.Combine(SynchronizerBuilder.projectDirectory, "dimmer.cs");
                var synchronizer = m_Builder.WithAssemblyData(files: new[] { absoluteFilePath }).Build();

                synchronizer.Sync();

                var csprojContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                XmlDocument scriptProject = XMLUtilities.FromText(csprojContent);
                XMLUtilities.AssertCompileItemsMatchExactly(scriptProject, new[] { "dimmer.cs" });
            }

            [Test]
            public void DefaultSyncSettings_WhenSynced_CreatesProjectFileFromDefaultTemplate()
            {
                var projectGuid = "ProjectGuid";
                var synchronizer = m_Builder.WithProjectGuid(projectGuid, m_Builder.Assembly).Build();

                synchronizer.Sync();

                var csprojContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                var content = new[]
                {
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                    "<Project ToolsVersion=\"4.0\" DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
                    "  <PropertyGroup>",
                    "    <LangVersion>latest</LangVersion>",
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
                    "    <DefineConstants>DEBUG;TRACE</DefineConstants>",
                    "    <ErrorReport>prompt</ErrorReport>",
                    "    <WarningLevel>4</WarningLevel>",
                    "    <NoWarn>0169</NoWarn>",
                    "    <AllowUnsafeBlocks>False</AllowUnsafeBlocks>",
                    "  </PropertyGroup>",
                    "  <PropertyGroup Condition=\" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' \">",
                    "    <DebugType>pdbonly</DebugType>",
                    "    <Optimize>true</Optimize>",
                    "    <OutputPath>Temp\\bin\\Release\\</OutputPath>",
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

        class GUID : SolutionGenerationTestBase
        {
            [Test]
            public void ProjectReference_MatchAssemblyGUID()
            {
                string[] files = { "test.cs" };
                var assemblyB = new Assembly("Test", "Temp/Test.dll", files, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
                var assemblyA = new Assembly("Test2", "some/path/file.dll", files, new string[0], new[] { assemblyB }, new[] { "Library.ScriptAssemblies.Test.dll" }, AssemblyFlags.None);
                var synchronizer = m_Builder.WithAssemblies(new[] { assemblyA, assemblyB }).Build();

                synchronizer.Sync();

                var assemblyACSproject = Path.Combine(SynchronizerBuilder.projectDirectory, $"{assemblyA.name}.csproj");
                var assemblyBCSproject = Path.Combine(SynchronizerBuilder.projectDirectory, $"{assemblyB.name}.csproj");

                Assert.True(m_Builder.FileExists(assemblyACSproject));
                Assert.True(m_Builder.FileExists(assemblyBCSproject));

                XmlDocument scriptProject = XMLUtilities.FromText(m_Builder.ReadFile(assemblyACSproject));
                XmlDocument scriptPluginProject = XMLUtilities.FromText(m_Builder.ReadFile(assemblyBCSproject));

                var a = XMLUtilities.GetInnerText(scriptPluginProject, "/msb:Project/msb:PropertyGroup/msb:ProjectGuid");
                var b = XMLUtilities.GetInnerText(scriptProject, "/msb:Project/msb:ItemGroup/msb:ProjectReference/msb:Project");
                Assert.AreEqual(a, b);
            }
        }

        class Synchronization : SolutionGenerationTestBase
        {
            [Test]
            public void WontSynchronize_WhenNoFilesChanged()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();
                Assert.AreEqual(3, m_Builder.WriteTimes, "One write for solution, one write for csproj, and one for vscode settings");

                synchronizer.Sync();
                Assert.AreEqual(3, m_Builder.WriteTimes, "No more files should be written");
            }

            [Test]
            public void WhenSynchronized_WillCreateCSProjectForAssembly()
            {
                var synchronizer = m_Builder.Build();

                Assert.IsFalse(m_Builder.FileExists(m_Builder.ProjectFilePath(m_Builder.Assembly)));

                synchronizer.Sync();

                Assert.IsTrue(m_Builder.FileExists(m_Builder.ProjectFilePath(m_Builder.Assembly)));
            }

            [Test]
            public void WhenSynchronized_WithTwoAssemblies_TwoProjectFilesAreGenerated()
            {
                var assemblyA = new Assembly("assemblyA", "path/to/a.dll", new[] { "file.cs" }, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
                var assemblyB = new Assembly("assemblyB", "path/to/b.dll", new[] { "file.cs" }, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
                var synchronizer = m_Builder.WithAssemblies(new[] { assemblyA, assemblyB }).Build();

                synchronizer.Sync();

                Assert.IsTrue(m_Builder.FileExists(m_Builder.ProjectFilePath(assemblyA)));
                Assert.IsTrue(m_Builder.FileExists(m_Builder.ProjectFilePath(assemblyB)));
            }
        }

        class SourceFiles : SolutionGenerationTestBase
        {
            [Test]
            public void NoCSFile_CreatesNoProjectFile()
            {
                var synchronizer = m_Builder.WithAssemblyData(files: new string[0]).Build();

                synchronizer.Sync();

                Assert.False(
                    m_Builder.FileExists(Path.Combine(SynchronizerBuilder.projectDirectory, $"{m_Builder.Assembly.name}.csproj")),
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
                var assetPath = "Assets/Asset.cs";
                var synchronizer = m_Builder
                    .WithAssemblyData(files: new[] { Path.Combine(SynchronizerBuilder.projectDirectory, assetPath) })
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

                StringAssert.Contains(assetPath.Replace('/', '\\'), m_Builder.ReadProjectFile(assembly));
            }

            [Test]
            public void CSharpFiles_WillBeIncluded()
            {
                var synchronizer = m_Builder.Build();

                synchronizer.Sync();

                var assembly = m_Builder.Assembly;
                StringAssert.Contains(assembly.sourceFiles[0].Replace('/', '\\'), m_Builder.ReadProjectFile(assembly));
            }

            [Test]
            public void NonCSharpFiles_AddedToNonCompileItems()
            {
                var nonCompileItems = new[]
                {
                    "ClassDiagram1.cd",
                    "text.txt",
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

            [TestCase(@"path\com.unity.cs")]
            [TestCase(@"..\path\file.cs")]
            public void IsValidFileName(string filePath)
            {
                var synchronizer = m_Builder
                    .WithAssemblyData(files: new[] { filePath })
                    .Build();

                synchronizer.Sync();

                var csprojContent = m_Builder.ReadProjectFile(m_Builder.Assembly);
                StringAssert.Contains(filePath, csprojContent);
            }

            [Test]
            public void AddedAfterSync_WillBeSynced()
            {
                var synchronizer = m_Builder.Build();
                synchronizer.Sync();
                const string newFile = "Newfile.cs";
                var newFileArray = new[] { newFile };
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
                var newFileArray = new[] { newFile };
                m_Builder.WithAssemblyData(files: newFileArray);

                Assert.True(synchronizer.SyncIfNeeded(newFileArray, new string[0]), "Should sync when file in assembly changes");

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

                Assert.True(synchronizer.SyncIfNeeded(filesAfter, new string[0]), "Should sync when file in assembly changes");

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
                "asmdef", "uxml", "uss", "shader", "compute", "cginc", "hlsl", "glslinc", "template", "raytrace"
            };
        }

        class CompilerOptions : SolutionGenerationTestBase
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

        class References : SolutionGenerationTestBase
        {
            [Test]
            public void DllInSourceFiles_WillBeAddedAsReference()
            {
                var referenceDll = "reference.dll";
                var synchronizer = m_Builder
                    .WithAssemblyData(files: new[] { "file.cs", referenceDll })
                    .Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                XmlDocument scriptProject = XMLUtilities.FromText(csprojFileContents);
                XMLUtilities.AssertCompileItemsMatchExactly(scriptProject, new[] { "file.cs" });
                XMLUtilities.AssertNonCompileItemsMatchExactly(scriptProject, new string[0]);
                Assert.IsTrue(csprojFileContents.MatchesRegex($"<Reference Include=\"reference\">\\W*<HintPath>{SynchronizerBuilder.projectDirectory}/{referenceDll}\\W*</HintPath>\\W*</Reference>"));
            }

            [Test]
            public void Containing_PathWithSpaces_IsParsedCorrectly()
            {
                const string responseFile = "csc.rsp";
                var synchronizer = m_Builder
                    .WithResponseFileData(m_Builder.Assembly, responseFile, fullPathReferences: new[] { "Folder/Path With Space/Goodbye.dll" })
                    .Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                Assert.IsTrue(csprojFileContents.MatchesRegex("<Reference Include=\"Goodbye\">\\W*<HintPath>Folder/Path With Space/Goodbye.dll\\W*</HintPath>\\W*</Reference>"));
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
                Assert.IsTrue(csprojFileContents.MatchesRegex($"<Reference Include=\"assembly\">\\W*<HintPath>{assembly.outputPath}\\W*</HintPath>\\W*</Reference>"));
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

                Assert.IsTrue(csprojFileContents.MatchesRegex("<Reference Include=\"Hello\">\\W*<HintPath>Hello.dll</HintPath>\\W*</Reference>"));
                Assert.IsTrue(csprojFileContents.MatchesRegex("<Reference Include=\"MyPlugin\">\\W*<HintPath>MyPlugin.dll</HintPath>\\W*</Reference>"));
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
                Assert.IsTrue(csprojFileContents.MatchesRegex($"<Reference Include=\"{assemblyReferences[0].name}\">\\W*<HintPath>{assemblyReferences[0].outputPath}</HintPath>\\W*</Reference>"));
                Assert.IsTrue(csprojFileContents.MatchesRegex($"<Reference Include=\"{assemblyReferences[1].name}\">\\W*<HintPath>{assemblyReferences[1].outputPath}</HintPath>\\W*</Reference>"));
            }

            [Test]
            public void CompiledAssemblyReference_IsAdded()
            {
                var compiledAssemblyReferences = new[]
                {
                    "/some/path/MyPlugin.dll",
                    "/some/other/path/Hello.dll",
                };
                var synchronizer = m_Builder.WithAssemblyData(compiledAssemblyReferences: compiledAssemblyReferences).Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                Assert.IsTrue(csprojFileContents.MatchesRegex("<Reference Include=\"Hello\">\\W*<HintPath>/some/other/path/Hello.dll</HintPath>\\W*</Reference>"));
                Assert.IsTrue(csprojFileContents.MatchesRegex("<Reference Include=\"MyPlugin\">\\W*<HintPath>/some/path/MyPlugin.dll</HintPath>\\W*</Reference>"));
            }

            [Test]
            public void ProjectReference_FromLibraryReferences_IsAdded()
            {
                var projectAssembly = new Assembly("ProjectAssembly", "/path/to/project.dll", new[] { "test.cs" }, new string[0], new Assembly[0], new string[0], AssemblyFlags.None);
                var synchronizer = m_Builder.WithAssemblyData(assemblyReferences: new[] { projectAssembly }).Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                Assert.IsFalse(csprojFileContents.MatchesRegex($"<Reference Include=\"{projectAssembly.name}\">\\W*<HintPath>{projectAssembly.outputPath}</HintPath>\\W*</Reference>"));
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

        class Defines : SolutionGenerationTestBase
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
                Assert.IsTrue(csprojFileContents.MatchesRegex("<DefineConstants>.*;DEF1.*</DefineConstants>"));
                Assert.IsTrue(csprojFileContents.MatchesRegex("<DefineConstants>.*;DEF2.*</DefineConstants>"));
            }

            [Test]
            public void Assembly_CanAddDefines()
            {
                var synchronizer = m_Builder.WithAssemblyData(defines: new[] { "DEF1", "DEF2" }).Build();

                synchronizer.Sync();

                var csprojFileContents = m_Builder.ReadProjectFile(m_Builder.Assembly);
                Assert.IsTrue(csprojFileContents.MatchesRegex("<DefineConstants>.*;DEF1.*</DefineConstants>"));
                Assert.IsTrue(csprojFileContents.MatchesRegex("<DefineConstants>.*;DEF2.*</DefineConstants>"));
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
                Assert.IsTrue(bCsprojContent.MatchesRegex("<DefineConstants>.*;CHILD_DEFINE.*</DefineConstants>"));
                Assert.IsFalse(bCsprojContent.MatchesRegex("<DefineConstants>.*;RootedDefine.*</DefineConstants>"));
                Assert.IsFalse(aCsprojContent.MatchesRegex("<DefineConstants>.*;CHILD_DEFINE.*</DefineConstants>"));
                Assert.IsTrue(aCsprojContent.MatchesRegex("<DefineConstants>.*;RootedDefine.*</DefineConstants>"));
            }
        }
    }
}
