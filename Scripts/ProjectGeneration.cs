using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.VisualStudioIntegration;
using UnityEditorInternal;
using UnityEngine;

namespace VSCodePackage
{
    public class ProjectGeneration
    {
        internal enum ScriptingLanguage
        {
            None,
            CSharp
        }

        enum Mode
        {
            UnityScriptAsUnityProj,
            UnityScriptAsPrecompiledAssembly
        }

        const string k_SettingsJson = @"{
    ""files.exclude"":
    {
        ""**/.DS_Store"":true,
        ""**/.git"":true,
        ""**/.gitignore"":true,
        ""**/.gitmodules"":true,
        ""**/*.booproj"":true,
        ""**/*.pidb"":true,
        ""**/*.suo"":true,
        ""**/*.user"":true,
        ""**/*.userprefs"":true,
        ""**/*.unityproj"":true,
        ""**/*.dll"":true,
        ""**/*.exe"":true,
        ""**/*.pdf"":true,
        ""**/*.mid"":true,
        ""**/*.midi"":true,
        ""**/*.wav"":true,
        ""**/*.gif"":true,
        ""**/*.ico"":true,
        ""**/*.jpg"":true,
        ""**/*.jpeg"":true,
        ""**/*.png"":true,
        ""**/*.psd"":true,
        ""**/*.tga"":true,
        ""**/*.tif"":true,
        ""**/*.tiff"":true,
        ""**/*.3ds"":true,
        ""**/*.3DS"":true,
        ""**/*.fbx"":true,
        ""**/*.FBX"":true,
        ""**/*.lxo"":true,
        ""**/*.LXO"":true,
        ""**/*.ma"":true,
        ""**/*.MA"":true,
        ""**/*.obj"":true,
        ""**/*.OBJ"":true,
        ""**/*.asset"":true,
        ""**/*.cubemap"":true,
        ""**/*.flare"":true,
        ""**/*.mat"":true,
        ""**/*.meta"":true,
        ""**/*.prefab"":true,
        ""**/*.unity"":true,
        ""build/"":true,
        ""Build/"":true,
        ""Library/"":true,
        ""library/"":true,
        ""obj/"":true,
        ""Obj/"":true,
        ""ProjectSettings/"":true,
        ""temp/"":true,
        ""Temp/"":true
    }
}";

        static readonly Regex _MonoDevelopPropertyHeader = new Regex(@"^\s*GlobalSection\(MonoDevelopProperties.*\)");
        static readonly Regex scriptReferenceExpression = new Regex(
            @"^Library.ScriptAssemblies.(?<dllname>(?<project>.*)\.dll$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly string MSBuildNamespaceUri = "http://schemas.microsoft.com/developer/msbuild/2003";

        public string ProjectDirectory { get; }

        string footerTemplate = string.Join("\r\n",
            @"  </ItemGroup>",
            @"  <Import Project=""$(MSBuildToolsPath)\Microsoft.CSharp.targets"" />",
            @"  <!-- To modify your build process, add your task inside one of the targets below and uncomment it. ",
            @"       Other similar extension points exist, see Microsoft.Common.targets.",
            @"  <Target Name=""BeforeBuild"">",
            @"  </Target>",
            @"  <Target Name=""AfterBuild"">",
            @"  </Target>",
            @"  -->",
            @"  {0}",
            @"</Project>",
            @"");

        string solutionProjectEntryTemplate = string.Join("\r\n", new[]
        {
            @"Project(""{{{0}}}"") = ""{1}"", ""{2}"", ""{{{3}}}""",
            @"EndProject"
        }).Replace("    ", "\t");

        string solutionProjectConfigurationTemplate = string.Join("\r\n", new[]
        {
            @"        {{{0}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
            @"        {{{0}}}.Debug|Any CPU.Build.0 = Debug|Any CPU",
            @"        {{{0}}}.Release|Any CPU.ActiveCfg = Release|Any CPU",
            @"        {{{0}}}.Release|Any CPU.Build.0 = Release|Any CPU"
        }).Replace("    ", "\t");

        private static readonly string DefaultMonoDevelopSolutionProperties = string.Join("\r\n", new[]
        {
            "    GlobalSection(MonoDevelopProperties) = preSolution",
            "        StartupItem = Assembly-CSharp.csproj",
            "    EndGlobalSection",
        }).Replace("    ", "\t");

        const string WindowsNewline = "\r\n";

        readonly string _projectName;

        static internal readonly Dictionary<string, ScriptingLanguage> BuiltinSupportedExtensions = new Dictionary<string, ScriptingLanguage>
        {
            { "cs", ScriptingLanguage.CSharp },
            { "js", ScriptingLanguage.None },
            { "boo", ScriptingLanguage.None },
            { "uxml", ScriptingLanguage.None },
            { "uss", ScriptingLanguage.None },
            { "shader", ScriptingLanguage.None },
            { "compute", ScriptingLanguage.None },
            { "cginc", ScriptingLanguage.None },
            { "hlsl", ScriptingLanguage.None },
            { "glslinc", ScriptingLanguage.None },
        };

        /// <summary>
        /// Map ScriptingLanguages to project extensions
        /// </summary>
        static readonly Dictionary<ScriptingLanguage, string> ProjectExtensions = new Dictionary<ScriptingLanguage, string>
        {
            { ScriptingLanguage.CSharp, ".csproj" },
            { ScriptingLanguage.None, ".csproj" },
        };

        public ProjectGeneration()
        {
            ProjectDirectory = Directory.GetParent(Application.dataPath).FullName;
            _projectName = Path.GetFileName(ProjectDirectory);
        }

        public void GenerateSolutionAndProjectFiles()
        {
            var islands = CompilationPipeline.GetAssemblies()
                .Where(assem => assem.sourceFiles.Length > 0 && assem.sourceFiles.Any(ShouldFileBePartOfSolution));

            var allAssetProjectParts = GenerateAllAssetProjectParts();

            var responseFilePath = Path.Combine("Assets", "mcs.rsp");

            // TODO: Talk about this..
            /*var responseFileData = ScriptCompilerBase.ParseResponseFileFromFile(Path.Combine(_projectDirectory, responseFilePath));

            if (responseFileData.Errors.Length > 0)
            {
                foreach (var error in responseFileData.Errors)
                    UnityEngine.Debug.LogErrorFormat("{0} Parse Error : {1}", responseFilePath, error);
            }*/

            SyncSolution(islands);
            var allProjectIslands = RelevantIslandsForMode(islands, Mode.UnityScriptAsPrecompiledAssembly).ToList();
            foreach (Assembly island in allProjectIslands)
                SyncProject(island, allAssetProjectParts, allProjectIslands);

            WriteVSCodeSettingsFiles();
        }

        string ProjectText(Assembly island,
                           Dictionary<string, string> allAssetsProjectParts,
                           List<Assembly> allProjectIslands)
        {
            var projectBuilder = new StringBuilder(ProjectHeader(island));
            var references = new List<string>();
            var projectReferences = new List<Match>();
            Match match;
            bool isBuildingEditorProject = island.outputPath.EndsWith("-Editor.dll");

            foreach (string file in island.sourceFiles)
            {
                if (!ShouldFileBePartOfSolution(file))
                    continue;

                var extension = Path.GetExtension(file).ToLower();
                var fullFile = EscapedRelativePathFor(file);
                if (".dll" != extension)
                {
                    var tagName = "Compile";
                    projectBuilder.AppendFormat("     <{0} Include=\"{1}\" />{2}", tagName, fullFile, WindowsNewline);
                }
                else
                {
                    references.Add(fullFile);
                }
            }

            string additionalAssetsForProject;
            var assemblyName = Path.GetFileNameWithoutExtension(island.outputPath);

            // Append additional non-script files that should be included in project generation.
            if (allAssetsProjectParts.TryGetValue(assemblyName, out additionalAssetsForProject))
                projectBuilder.Append(additionalAssetsForProject);

            foreach (string reference in references.Union(island.allReferences))
            {
                if (reference.EndsWith("/UnityEditor.dll") || reference.EndsWith("/UnityEngine.dll") || reference.EndsWith("\\UnityEditor.dll") || reference.EndsWith("\\UnityEngine.dll"))
                    continue;

                match = scriptReferenceExpression.Match(reference);
                if (match.Success)
                {
                    // Add a reference to a project except if it's a reference to a script assembly
                    // that we are not generating a project for. This will be the case for assemblies
                    // coming from .assembly.json files in non-internalized packages.
                    var dllName = match.Groups["dllname"].Value;
                    if (allProjectIslands.Any(i => Path.GetFileName(i.outputPath) == dllName))
                    {
                        projectReferences.Add(match);
                        continue;
                    }
                }

                string fullReference = Path.IsPathRooted(reference) ? reference : Path.Combine(ProjectDirectory, reference);

                //if (!AssemblyHelper.IsManagedAssembly(fullReference))
//                    continue;
//                if (AssemblyHelper.IsInternalAssembly(fullReference))
//                {
//                    if (!IsAdditionalInternalAssemblyReference(isBuildingEditorProject, fullReference))
//                        continue;
//                    var referenceName = Path.GetFileName(fullReference);
//                    if (allAdditionalReferenceFilenames.Contains(referenceName))
//                        continue;
//                    allAdditionalReferenceFilenames.Add(referenceName);
//                }

                //replace \ with / and \\ with /
                fullReference = fullReference.Replace("\\", "/");
                fullReference = fullReference.Replace("\\\\", "/");
                projectBuilder.AppendFormat(" <Reference Include=\"{0}\">{1}", Path.GetFileNameWithoutExtension(fullReference), WindowsNewline);
                projectBuilder.AppendFormat(" <HintPath>{0}</HintPath>{1}", fullReference, WindowsNewline);
                projectBuilder.AppendFormat(" </Reference>{0}", WindowsNewline);
            }

            if (0 < projectReferences.Count)
            {
                string referencedProject;
                projectBuilder.AppendLine("  </ItemGroup>");
                projectBuilder.AppendLine("  <ItemGroup>");
                foreach (Match reference in projectReferences)
                {
                    referencedProject = reference.Groups["project"].Value;
                    projectBuilder.AppendFormat("    <ProjectReference Include=\"{0}{1}\">{2}", referencedProject, ".csproj", WindowsNewline);
                    projectBuilder.AppendFormat("      <Project>{{{0}}}</Project>{1}", ProjectGuid(Path.Combine("Temp", reference.Groups["project"].Value + ".dll")), WindowsNewline);
                    projectBuilder.AppendFormat("      <Name>{0}</Name>{1}", referencedProject, WindowsNewline);
                    projectBuilder.AppendLine("    </ProjectReference>");
                }
            }

            projectBuilder.Append(ProjectFooter(island));
            return projectBuilder.ToString();
        }

        string ProjectFooter(Assembly island)
        {
            return string.Format(GetProjectFooterTemplate(), ReadExistingMonoDevelopProjectProperties(island));
        }

        string ReadExistingMonoDevelopProjectProperties(Assembly island)
        {
            if (!ProjectExists(island)) return string.Empty;
            XmlDocument doc = new XmlDocument();
            XmlNamespaceManager manager;
            try
            {
                doc.Load(ProjectFile(island));
                manager = new XmlNamespaceManager(doc.NameTable);
                manager.AddNamespace("msb", MSBuildNamespaceUri);
            }
            catch (Exception ex)
            {
                if (ex is IOException ||
                    ex is XmlException)
                    return string.Empty;
                throw;
            }

            XmlNodeList nodes = doc.SelectNodes("/msb:Project/msb:ProjectExtensions", manager);
            if (0 == nodes.Count) return string.Empty;

            StringBuilder sb = new StringBuilder();
            foreach (XmlNode node in nodes)
            {
                sb.AppendLine(node.OuterXml);
            }

            return sb.ToString();
        }

        public bool IsNonInternalizedPackage(string file)
        {
            if (file.StartsWith("Packages/"))
            {
                //bool rootFolder, readOnly;
                //bool validPath = AssetDatabase.GetAssetFolderInfo(file, out rootFolder, out readOnly);
                return (true);
            }

            return false;
        }

        public bool ShouldFileBePartOfSolution(string file)
        {
            string extension = Path.GetExtension(file);

#if ENABLE_PACKMAN
            if (IsNonInternalizedPackage(file))
            {
                return false;
            }
#endif

//#if ENABLE_PACKMAN
//		// Exclude files coming from packages except if they are internalized.
//		if (IsNonInternalizedPackagePath(file))
//		{
//			return false;
//		}
//#endif

            // Dll's are not scripts but still need to be included..
            if (extension == ".dll")
                return true;

            if (file.ToLower().EndsWith(".asmdef"))
                return true;

            return IsSupportedExtension(extension);
        }

        private string ProjectHeader(Assembly island)
        {
            string targetframeworkversion = "v3.5";
            string targetLanguageVersion = "4";
            string toolsversion = "4.0";
            string productversion = "10.0.20506";
#if ENABLE_PACKMAN
            string baseDirectory = ".";
#else
            string baseDirectory = "Assets";
#endif
            ScriptingLanguage language = ScriptingLanguageFor(GetExtensionOfSourceFiles(island.sourceFiles));

            targetframeworkversion = "v4.7.1";
            targetLanguageVersion = "6";

            var arguments = new object[]
            {
                toolsversion, productversion, ProjectGuid(island.outputPath),
                InternalEditorUtility.GetEngineAssemblyPath(),
                InternalEditorUtility.GetEditorAssemblyPath(),
                string.Join(";", new[] { "DEBUG", "TRACE" }.Concat(island.defines).Distinct().ToArray()),
                MSBuildNamespaceUri,
                Path.GetFileNameWithoutExtension(island.outputPath),
                EditorSettings.projectGenerationRootNamespace,
                targetframeworkversion,
                targetLanguageVersion,
                baseDirectory,
                island.compilerOptions.AllowUnsafeCode // | responseFileData.Unsafe
            };

            try
            {
                return string.Format(GetProjectHeaderTemplate(), arguments);
            }
            catch (Exception)
            {
                throw new System.NotSupportedException("Failed creating c# project because the c# project header did not have the correct amount of arguments, which is " + arguments.Length);
            }
        }

        public string GetProjectHeaderTemplate()
        {
            return EditorPrefs.GetString("VSProjectHeader", GetProjectHeaderTemplateInternal());
        }

        public virtual string GetProjectHeaderTemplateInternal()
        {
            var header = new[]
            {
                @"<?xml version=""1.0"" encoding=""utf-8""?>",
                @"<Project ToolsVersion=""{0}"" DefaultTargets=""Build"" xmlns=""{6}"">",
                @"  <PropertyGroup>",
                @"    <LangVersion>{10}</LangVersion>",
                @"  </PropertyGroup>",
                @"  <PropertyGroup>",
                @"    <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>",
                @"    <Platform Condition="" '$(Platform)' == '' "">AnyCPU</Platform>",
                @"    <ProductVersion>{1}</ProductVersion>",
                @"    <SchemaVersion>2.0</SchemaVersion>",
                @"    <RootNamespace>{8}</RootNamespace>",
                @"    <ProjectGuid>{{{2}}}</ProjectGuid>",
                @"    <OutputType>Library</OutputType>",
                @"    <AppDesignerFolder>Properties</AppDesignerFolder>",
                @"    <AssemblyName>{7}</AssemblyName>",
                @"    <TargetFrameworkVersion>{9}</TargetFrameworkVersion>",
                @"    <FileAlignment>512</FileAlignment>",
                @"    <BaseDirectory>{11}</BaseDirectory>",
                @"  </PropertyGroup>",
                @"  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' "">",
                @"    <DebugSymbols>true</DebugSymbols>",
                @"    <DebugType>full</DebugType>",
                @"    <Optimize>false</Optimize>",
                @"    <OutputPath>Temp\bin\Debug\</OutputPath>",
                @"    <DefineConstants>{5}</DefineConstants>",
                @"    <ErrorReport>prompt</ErrorReport>",
                @"    <WarningLevel>4</WarningLevel>",
                @"    <NoWarn>0169</NoWarn>",
                @"    <AllowUnsafeBlocks>{12}</AllowUnsafeBlocks>",
                @"  </PropertyGroup>",
                @"  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' "">",
                @"    <DebugType>pdbonly</DebugType>",
                @"    <Optimize>true</Optimize>",
                @"    <OutputPath>Temp\bin\Release\</OutputPath>",
                @"    <ErrorReport>prompt</ErrorReport>",
                @"    <WarningLevel>4</WarningLevel>",
                @"    <NoWarn>0169</NoWarn>",
                @"    <AllowUnsafeBlocks>{12}</AllowUnsafeBlocks>",
                @"  </PropertyGroup>",
            };

            var forceExplicitReferences = new[]
            {
                @"  <PropertyGroup>",
                @"    <NoConfig>true</NoConfig>",
                @"    <NoStdLib>true</NoStdLib>",
                @"    <AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences>",
                @"  </PropertyGroup>",
            };

            var itemGroupStart = new[]
            {
                @"  <ItemGroup>",
            };

            var systemReferences = new[]
            {
                @"    <Reference Include=""System"" />",
                @"    <Reference Include=""System.Xml"" />",
                @"    <Reference Include=""System.Core"" />",
                @"    <Reference Include=""System.Runtime.Serialization"" />",
                @"    <Reference Include=""System.Xml.Linq"" />",
            };

            var footer = new[]
            {
                @"    <Reference Include=""UnityEngine"">",
                @"      <HintPath>{3}</HintPath>",
                @"    </Reference>",
                @"    <Reference Include=""UnityEditor"">",
                @"      <HintPath>{4}</HintPath>",
                @"    </Reference>",
                @"  </ItemGroup>",
                @"  <ItemGroup>",
                @""
            };

            string[] text = header.Concat(forceExplicitReferences).Concat(itemGroupStart).Concat(footer).ToArray();
            return string.Join("\r\n", text);
        }

        void SyncProject(Assembly island,
            Dictionary<string, string> allAssetsProjectParts,
            List<Assembly> allProjectIslands)
        {
            SyncProjectFileIfNotChanged(ProjectFile(island), ProjectText(island, allAssetsProjectParts, allProjectIslands));
        }

        static void SyncProjectFileIfNotChanged(string path, string newContents)
        {
            //if (Path.GetExtension(path) == ".csproj")
            //{
//			newContents = AssetPostprocessingInternal.CallOnGeneratedCSProject(path, newContents);
            //}

            SyncFileIfNotChanged(path, newContents);
        }

        private void SyncSolution(IEnumerable<Assembly> islands)
        {
            SyncSolutionFileIfNotChanged(SolutionFile(), SolutionText(islands, Mode.UnityScriptAsPrecompiledAssembly));
        }

        static void SyncSolutionFileIfNotChanged(string path, string newContents)
        {
            //newContents = AssetPostprocessingInternal.CallOnGeneratedSlnSolution(path, newContents);

            SyncFileIfNotChanged(path, newContents);
        }

        private static void SyncFileIfNotChanged(string filename, string newContents)
        {
            if (File.Exists(filename) &&
                newContents == File.ReadAllText(filename))
            {
                return;
            }

            File.WriteAllText(filename, newContents, Encoding.UTF8);
        }

        public string SolutionTemplate => EditorPrefs.GetString("VSSolutionText", m_SolutionTemplate);

        string m_SolutionTemplate = string.Join("\r\n",
            @"",
            @"Microsoft Visual Studio Solution File, Format Version {0}",
            @"# Visual Studio {1}",
            @"{2}",
            @"Global",
            @"    GlobalSection(SolutionConfigurationPlatforms) = preSolution",
            @"        Debug|Any CPU = Debug|Any CPU",
            @"        Release|Any CPU = Release|Any CPU",
            @"    EndGlobalSection",
            @"    GlobalSection(ProjectConfigurationPlatforms) = postSolution",
            @"{3}",
            @"    EndGlobalSection",
            @"    GlobalSection(SolutionProperties) = preSolution",
            @"        HideSolutionNode = FALSE",
            @"    EndGlobalSection",
            @"{4}",
            @"EndGlobal",
            @"").Replace("    ", "\t");

        string SolutionText(IEnumerable<Assembly> islands, Mode mode)
        {
            const string fileversion = "11.00";
            const string vsversion = "2010";
            /*if (_settings.VisualStudioVersion == 9)
            {
                fileversion = "10.00";
                vsversion = "2008";
            }*/
            var relevantIslands = RelevantIslandsForMode(islands, mode);
            string projectEntries = GetProjectEntries(relevantIslands);
            string projectConfigurations = string.Join(WindowsNewline, relevantIslands.Select(i => GetProjectActiveConfigurations(ProjectGuid(i.outputPath))).ToArray());
            return string.Format(SolutionTemplate, fileversion, vsversion, projectEntries, projectConfigurations, ReadExistingMonoDevelopSolutionProperties());
        }

        bool ProjectExists(Assembly island)
        {
            return File.Exists(ProjectFile(island));
        }

        bool SolutionExists()
        {
            return File.Exists(SolutionFile());
        }

        string ReadExistingMonoDevelopSolutionProperties()
        {
            if (!SolutionExists()) return DefaultMonoDevelopSolutionProperties;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(SolutionFile());
            }
            catch (IOException)
            {
                return DefaultMonoDevelopSolutionProperties;
            }

            StringBuilder existingOptions = new StringBuilder();
            bool collecting = false;

            foreach (string line in lines)
            {
                if (_MonoDevelopPropertyHeader.IsMatch(line))
                {
                    collecting = true;
                }

                if (collecting)
                {
                    if (line.Contains("EndGlobalSection"))
                    {
                        existingOptions.Append(line);
                        collecting = false;
                    }
                    else
                        existingOptions.AppendFormat("{0}{1}", line, WindowsNewline);
                }
            }

            if (0 < existingOptions.Length)
            {
                return existingOptions.ToString();
            }

            return DefaultMonoDevelopSolutionProperties;
        }

        string GetProjectActiveConfigurations(string projectGuid)
        {
            return string.Format(
                solutionProjectConfigurationTemplate,
                projectGuid);
        }

        static IEnumerable<Assembly> RelevantIslandsForMode(IEnumerable<Assembly> islands, Mode mode)
        {
            return islands.Where(i =>
                ScriptingLanguage.CSharp == ScriptingLanguageFor(GetExtensionOfSourceFiles(i.sourceFiles)));
        }

        /// <summary>
        /// Get a Project("{guid}") = "MyProject", "MyProject.unityproj", "{projectguid}"
        /// entry for each relevant language
        /// </summary>
        string GetProjectEntries(IEnumerable<Assembly> islands)
        {
            var projectEntries = islands.Select(i => string.Format(
                solutionProjectEntryTemplate,
                SolutionGuid(i), _projectName, Path.GetFileName(ProjectFile(i)), ProjectGuid(i.outputPath)
            ));

            return string.Join(WindowsNewline, projectEntries.ToArray());
        }

        string ProjectGuid(string assembly)
        {
            return SolutionGuidGenerator.GuidForProject(_projectName + Path.GetFileNameWithoutExtension(assembly));
        }

        string SolutionGuid(Assembly island)
        {
            return SolutionGuidGenerator.GuidForSolution(_projectName, GetExtensionOfSourceFiles(island.sourceFiles));
        }

        static string GetExtensionOfSourceFiles(string[] _files)
        {
            return _files.Length > 0 ? GetExtensionOfSourceFile(_files[0]) : "NA";
        }

        static string GetExtensionOfSourceFile(string file)
        {
            var ext = Path.GetExtension(file).ToLower();
            ext = ext.Substring(1); //strip dot
            return ext;
        }

        string ProjectFile(Assembly island)
        {
            var language = ScriptingLanguageFor(GetExtensionOfSourceFiles(island.sourceFiles));
            return Path.Combine(ProjectDirectory, $"{Path.GetFileNameWithoutExtension(island.outputPath)}{ProjectExtensions[language]}");
        }

        string SolutionFile()
        {
            return Path.Combine(ProjectDirectory, $"{_projectName}.sln");
        }

        Dictionary<string, string> GenerateAllAssetProjectParts()
        {
            var stringBuilders = new Dictionary<string, StringBuilder>();

            foreach (var asset in AssetDatabase.GetAllAssetPaths())
            {
                /*if (IsNonInternalizedPackagePath(asset))
                {
                    continue;
                }*/

                var extension = Path.GetExtension(asset);
                if (IsSupportedExtension(extension) && ScriptingLanguage.None == ScriptingLanguageFor(extension))
                {
                    // Find assembly the asset belongs to by adding script extension and using compilation pipeline.
                    var assemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(asset + ".cs");
                    assemblyName = assemblyName ?? CompilationPipeline.GetAssemblyNameFromScriptPath(asset + ".js");
                    assemblyName = assemblyName ?? CompilationPipeline.GetAssemblyNameFromScriptPath(asset + ".boo");

                    assemblyName = Path.GetFileNameWithoutExtension(assemblyName);

                    StringBuilder projectBuilder;

                    if (!stringBuilders.TryGetValue(assemblyName, out projectBuilder))
                    {
                        projectBuilder = new StringBuilder();
                        stringBuilders[assemblyName] = projectBuilder;
                    }

                    projectBuilder.AppendFormat("     <None Include=\"{0}\" />{1}", EscapedRelativePathFor(asset), WindowsNewline);
                }
            }

            var result = new Dictionary<string, string>();

            foreach (var entry in stringBuilders)
                result[entry.Key] = entry.Value.ToString();

            return result;
        }

        static string ConvertSeparatorsToWindows(string path)
        {
            return path.Replace('/', '\\');
        }

        static string SkipPathPrefix(string path, string prefix)
        {
            return path.StartsWith(prefix) ? path.Substring(prefix.Length + 1) : path;
        }

        string EscapedRelativePathFor(string file)
        {
            var projectDir = ConvertSeparatorsToWindows(ProjectDirectory);
            file = ConvertSeparatorsToWindows(file);
            var path = SkipPathPrefix(file, projectDir);
/*#if ENABLE_PACKMAN
		if (PackageManager.Folders.IsPackagedAssetPath(path.ConvertSeparatorsToUnity()))
		{
			// We have to normalize the path, because the PackageManagerRemapper assumes
			// dir seperators will be os specific.
			var absolutePath = Path.GetFullPath(path.NormalizePath()).ConvertSeparatorsToWindows();
			path = Paths.SkipPathPrefix(absolutePath, projectDir);
		}
#endif*/
            return SecurityElement.Escape(path);
        }

        static bool IsSupportedExtension(string extension)
        {
            extension = extension.TrimStart('.');
            return BuiltinSupportedExtensions.ContainsKey(extension);
        }

        static ScriptingLanguage ScriptingLanguageFor(string extension)
        {
            ScriptingLanguage result;
            return BuiltinSupportedExtensions.TryGetValue(extension.TrimStart('.'), out result)
                ? result
                : ScriptingLanguage.None;
        }

        void WriteVSCodeSettingsFiles()
        {
            var vsCodeDirectory = Path.Combine(ProjectDirectory, ".vscode");

            if (!Directory.Exists(vsCodeDirectory))
                Directory.CreateDirectory(vsCodeDirectory);

            var vsCodeSettingsJson = Path.Combine(vsCodeDirectory, "settings.json");

            if (!File.Exists(vsCodeSettingsJson))
                File.WriteAllText(vsCodeSettingsJson, k_SettingsJson);
        }

        string GetProjectFooterTemplate()
        {
            return EditorPrefs.GetString("VSProjectFooter", footerTemplate);
        }

        public void SyncIfNeeded(IEnumerable<string> affectedFiles, IEnumerable<string> reimportedFiles)
        {
        }
    }
}
