using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Profiling;

namespace com.unity.ide.vscode
{
    interface IGenerator
    {
        bool SyncIfNeeded(List<string> affectedFiles, string[] reimportedFiles);
        void Sync();
        string SolutionFile();
        string ProjectDirectory { get; }
        IAssemblyNameProvider AssemblyNameProvider { get; }
        void GenerateAll(bool generateAll);
        bool SolutionExists();
    }

    class ProjectGeneration : IGenerator
    {
        enum ScriptingLanguage
        {
            None,
            CSharp
        }

        const string k_SolutionFileFormat = "\r\n" +
            "Microsoft Visual Studio Solution File, Format Version {0}\r\n" +
            "# Visual Studio {1}\r\n" +
            "{2}\r\n" +
            "Global\r\n" +
            "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n" +
            "\t\tUnity|Any CPU = Unity|Any CPU\r\n" +
            "\tEndGlobalSection\r\n" +
            "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n" +
            "{3}\r\n" +
            "\tEndGlobalSection\r\n" +
            "\tGlobalSection(SolutionProperties) = preSolution\r\n" +
            "\t\tHideSolutionNode = FALSE\r\n" +
            "\tEndGlobalSection\r\n" +
            "EndGlobal\r\n";

        const string k_ProjectFooter =
            "  </ItemGroup>\r\n" +
            "  <Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" />\r\n" +
            "  <!-- To modify your build process, add your task inside one of the targets below and uncomment it.\r\n" +
            "       Other similar extension points exist, see Microsoft.Common.targets.\r\n" +
            "  <Target Name=\"BeforeBuild\">\r\n" +
            "  </Target>\r\n" +
            "  <Target Name=\"AfterBuild\">\r\n" +
            "  </Target>\r\n" +
            "  -->\r\n" +
            "</Project>\r\n";

        const string k_MSBuildNamespaceUri = "http://schemas.microsoft.com/developer/msbuild/2003";

        const string k_WindowsNewline = "\r\n";

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

        /// <summary>
        /// Map source extensions to ScriptingLanguages
        /// </summary>
        static readonly Dictionary<string, ScriptingLanguage> k_BuiltinSupportedExtensions = new Dictionary<string, ScriptingLanguage>
        {
            { "cs", ScriptingLanguage.CSharp },
            { "uxml", ScriptingLanguage.None },
            { "uss", ScriptingLanguage.None },
            { "shader", ScriptingLanguage.None },
            { "compute", ScriptingLanguage.None },
            { "cginc", ScriptingLanguage.None },
            { "hlsl", ScriptingLanguage.None },
            { "glslinc", ScriptingLanguage.None },
            { "template", ScriptingLanguage.None },
            { "raytrace", ScriptingLanguage.None }
        };

        const string k_SolutionProjectEntryTemplate = "Project(\"{{{0}}}\") = \"{1}\", \"{2}\", \"{{{3}}}\"\r\nEndProject";

        const string k_SolutionProjectConfigurationTemplate =
            "\t\t{{{0}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU";

        static readonly string[] k_ReimportSyncExtensions = { ".dll", ".asmdef" };

        string[] m_ProjectSupportedExtensions = new string[0];
        public string ProjectDirectory { get; }
        IAssemblyNameProvider IGenerator.AssemblyNameProvider => m_AssemblyNameProvider;

        public void GenerateAll(bool generateAll)
        {
            m_AssemblyNameProvider.ToggleProjectGeneration(
                ProjectGenerationFlag.BuiltIn
                | ProjectGenerationFlag.Embedded
                | ProjectGenerationFlag.Git
                | ProjectGenerationFlag.Local
#if UNITY_2019_3_OR_NEWER
                | ProjectGenerationFlag.LocalTarBall
#endif
                | ProjectGenerationFlag.PlayerAssemblies
                | ProjectGenerationFlag.Registry
                | ProjectGenerationFlag.Unknown);
        }

        readonly string m_ProjectName;
        readonly IAssemblyNameProvider m_AssemblyNameProvider;
        readonly IFileIO m_FileIOProvider;
        readonly IGUIDGenerator m_GUIDProvider;

        const string k_CsProjectExtension = ".csproj";
        const string k_ToolsVersion = "4.0";
        const string k_ProductVersion = "10.0.20506";
        const string k_BaseDirectory = ".";
        const string k_TargetFrameworkVersion = "v4.7.1";
        const string k_TargetLanguageVersion = "latest";

        public ProjectGeneration(string tempDirectory)
            : this(tempDirectory, new AssemblyNameProvider(), new FileIO(), new GUIDGenerator()) { }

        public ProjectGeneration(string tempDirectory, IAssemblyNameProvider assemblyNameProvider, IFileIO fileIO, IGUIDGenerator guidGenerator)
        {
            ProjectDirectory = tempDirectory.Replace('\\', '/');
            m_ProjectName = Path.GetFileName(ProjectDirectory);
            m_AssemblyNameProvider = assemblyNameProvider;
            m_FileIOProvider = fileIO;
            m_GUIDProvider = guidGenerator;
        }

        /// <summary>
        /// Syncs the scripting solution if any affected files are relevant.
        /// </summary>
        /// <returns>
        /// Whether the solution was synced.
        /// </returns>
        /// <param name='affectedFiles'>
        /// A set of files whose status has changed
        /// </param>
        /// <param name="reimportedFiles">
        /// A set of files that got reimported
        /// </param>
        public bool SyncIfNeeded(List<string> affectedFiles, string[] reimportedFiles)
        {
            Profiler.BeginSample("SolutionSynchronizerSync");
            SetupProjectSupportedExtensions();

            // Don't sync if we haven't synced before
            if (SolutionExists() && HasFilesBeenModified(affectedFiles, reimportedFiles))
            {
                var assemblies = m_AssemblyNameProvider.GetAssemblies(ShouldFileBePartOfSolution);
                var allProjectAssemblies = RelevantAssembliesForMode(assemblies).ToList();
                var allAssetProjectParts = GenerateAllAssetProjectParts();

                var affectedNames = affectedFiles.Select(asset => m_AssemblyNameProvider.GetAssemblyNameFromScriptPath(asset)).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Split(new [] {".dll"}, StringSplitOptions.RemoveEmptyEntries)[0]);
                var reimportedNames = reimportedFiles.Select(asset => m_AssemblyNameProvider.GetAssemblyNameFromScriptPath(asset)).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Split(new [] {".dll"}, StringSplitOptions.RemoveEmptyEntries)[0]);
                var affectedAndReimported = new HashSet<string>(affectedNames.Concat(reimportedNames));

                foreach (var assembly in allProjectAssemblies)
                {
                    if (!affectedAndReimported.Contains(assembly.name))
                        continue;

                    SyncProject(assembly, allAssetProjectParts, ParseResponseFileData(assembly));
                }

                Profiler.EndSample();
                return true;
            }

            Profiler.EndSample();
            return false;
        }

        bool HasFilesBeenModified(List<string> affectedFiles, string[] reimportedFiles)
        {
            return affectedFiles.Any(ShouldFileBePartOfSolution) || reimportedFiles.Any(ShouldSyncOnReimportedAsset);
        }

        static bool ShouldSyncOnReimportedAsset(string asset)
        {
            return k_ReimportSyncExtensions.Contains(new FileInfo(asset).Extension);
        }

        public void Sync()
        {
            SetupProjectSupportedExtensions();
            GenerateAndWriteSolutionAndProjects();
        }

        public bool SolutionExists()
        {
            return m_FileIOProvider.Exists(SolutionFile());
        }

        void SetupProjectSupportedExtensions()
        {
            m_ProjectSupportedExtensions = m_AssemblyNameProvider.ProjectSupportedExtensions;
        }

        bool ShouldFileBePartOfSolution(string file)
        {
            // Exclude files coming from packages except if they are internalized.
            if (m_AssemblyNameProvider.IsInternalizedPackagePath(file))
            {
                return false;
            }

            return HasValidExtension(file);
        }

        bool HasValidExtension(string file)
        {
            string extension = Path.GetExtension(file);

            // Dll's are not scripts but still need to be included..
            if (extension == ".dll")
                return true;

            if (file.ToLower().EndsWith(".asmdef"))
                return true;

            return IsSupportedExtension(extension);
        }

        bool IsSupportedExtension(string extension)
        {
            extension = extension.TrimStart('.');
            if (k_BuiltinSupportedExtensions.ContainsKey(extension))
                return true;
            if (m_ProjectSupportedExtensions.Contains(extension))
                return true;
            return false;
        }

        static ScriptingLanguage ScriptingLanguageFor(Assembly assembly)
        {
            return ScriptingLanguageFor(GetExtensionOfSourceFiles(assembly.sourceFiles));
        }

        static string GetExtensionOfSourceFiles(string[] files)
        {
            return files.Length > 0 ? GetExtensionOfSourceFile(files[0]) : "NA";
        }

        static string GetExtensionOfSourceFile(string file)
        {
            var ext = Path.GetExtension(file).ToLower();
            ext = ext.Substring(1); //strip dot
            return ext;
        }

        static ScriptingLanguage ScriptingLanguageFor(string extension)
        {
            return k_BuiltinSupportedExtensions.TryGetValue(extension.TrimStart('.'), out var result)
                ? result
                : ScriptingLanguage.None;
        }

        public void GenerateAndWriteSolutionAndProjects()
        {
            // Only synchronize assemblies that have associated source files and ones that we actually want in the project.
            // This also filters out DLLs coming from .asmdef files in packages.
            var assemblies = m_AssemblyNameProvider.GetAssemblies(ShouldFileBePartOfSolution);

            var allAssetProjectParts = GenerateAllAssetProjectParts();

            SyncSolution(assemblies);
            var allProjectAssemblies = RelevantAssembliesForMode(assemblies).ToList();
            foreach (Assembly assembly in allProjectAssemblies)
            {
                var responseFileData = ParseResponseFileData(assembly);
                SyncProject(assembly, allAssetProjectParts, responseFileData);
            }

            WriteVSCodeSettingsFiles();
        }

        List<ResponseFileData> ParseResponseFileData(Assembly assembly)
        {
            var systemReferenceDirectories = CompilationPipeline.GetSystemAssemblyDirectories(assembly.compilerOptions.ApiCompatibilityLevel);

            Dictionary<string, ResponseFileData> responseFilesData = assembly.compilerOptions.ResponseFiles.ToDictionary(x => x, x => m_AssemblyNameProvider.ParseResponseFile(
                x,
                ProjectDirectory,
                systemReferenceDirectories
            ));

            Dictionary<string, ResponseFileData> responseFilesWithErrors = responseFilesData.Where(x => x.Value.Errors.Any())
                .ToDictionary(x => x.Key, x => x.Value);

            if (responseFilesWithErrors.Any())
            {
                foreach (var error in responseFilesWithErrors)
                foreach (var valueError in error.Value.Errors)
                {
                    Debug.LogError($"{error.Key} Parse Error : {valueError}");
                }
            }

            return responseFilesData.Select(x => x.Value).ToList();
        }

        Dictionary<string, string> GenerateAllAssetProjectParts()
        {
            Dictionary<string, StringBuilder> stringBuilders = new Dictionary<string, StringBuilder>();

            foreach (string asset in m_AssemblyNameProvider.GetAllAssetPaths())
            {
                // Exclude files coming from packages except if they are internalized.
                // TODO: We need assets from the assembly API
                if (m_AssemblyNameProvider.IsInternalizedPackagePath(asset))
                {
                    continue;
                }

                string extension = Path.GetExtension(asset);
                if (IsSupportedExtension(extension) && ScriptingLanguage.None == ScriptingLanguageFor(extension))
                {
                    // Find assembly the asset belongs to by adding script extension and using compilation pipeline.
                    var assemblyName = m_AssemblyNameProvider.GetAssemblyNameFromScriptPath(asset);

                    if (string.IsNullOrEmpty(assemblyName))
                    {
                        continue;
                    }

                    Utility.GetFileNameWithoutExtension(assemblyName, out var start, out var end);
                    assemblyName = assemblyName.Substring(start, end - start);

                    if (!stringBuilders.TryGetValue(assemblyName, out var projectBuilder))
                    {
                        projectBuilder = new StringBuilder();
                        stringBuilders[assemblyName] = projectBuilder;
                    }

                    projectBuilder.Append("     <None Include=\"").Append(EscapedRelativePathFor(asset)).Append("\" />\r\n");
                }
            }

            var result = new Dictionary<string, string>();

            foreach (var entry in stringBuilders)
                result[entry.Key] = entry.Value.ToString();

            return result;
        }

        void SyncProject(
            Assembly assembly,
            Dictionary<string, string> allAssetsProjectParts,
            List<ResponseFileData> responseFilesData)
        {
            SyncProjectFileIfNotChanged(ProjectFile(assembly), ProjectText(assembly, allAssetsProjectParts, responseFilesData, GetAllRoslynAnalyzerPaths().ToArray()));
        }

        private IEnumerable<string> GetAllRoslynAnalyzerPaths()
        {
            return m_AssemblyNameProvider.GetRoslynAnalyzerPaths();
        }

        void SyncProjectFileIfNotChanged(string path, string newContents)
        {
            SyncFileIfNotChanged(path, newContents);
        }

        void SyncSolutionFileIfNotChanged(string path, string newContents)
        {
            SyncFileIfNotChanged(path, newContents);
        }

        void SyncFileIfNotChanged(string filename, string newContents)
        {
            m_FileIOProvider.WriteAllText(filename, newContents);
        }

        string ProjectText(
            Assembly assembly,
            Dictionary<string, string> allAssetsProjectParts,
            List<ResponseFileData> responseFilesData,
            string[] roslynAnalyzerDllPaths)
        {
            var projectBuilder = new StringBuilder();
            ProjectHeader(assembly, responseFilesData, roslynAnalyzerDllPaths, projectBuilder);
            var references = new List<string>();

            foreach (string file in assembly.sourceFiles)
            {
                if (!HasValidExtension(file))
                    continue;

                var fullFile = EscapedRelativePathFor(file);
                if (!Utility.HasFileExtension(file, ".dll"))
                {
                    projectBuilder.Append("     <Compile Include=\"").Append(fullFile).Append("\" />\r\n");
                }
                else
                {
                    references.Add(fullFile);
                }
            }

            // Append additional non-script files that should be included in project generation.
            if (allAssetsProjectParts.TryGetValue(assembly.name, out var additionalAssetsForProject))
                projectBuilder.Append(additionalAssetsForProject);

            var responseRefs = responseFilesData.SelectMany(x => x.FullPathReferences.Select(r => r));
            var internalAssemblyReferences = assembly.assemblyReferences
              .Where(i => !i.sourceFiles.Any(ShouldFileBePartOfSolution)).Select(i => i.outputPath);
            var allReferences =
              assembly.compiledAssemblyReferences
                .Union(responseRefs)
                .Union(references)
                .Union(internalAssemblyReferences)
                .Except(roslynAnalyzerDllPaths);

            foreach (var reference in allReferences)
            {
                var fullReference = Utility.IsPathRooted(reference) ? reference : Path.Combine(ProjectDirectory, reference);
                AppendReference(fullReference, projectBuilder);
            }

            if (0 < assembly.assemblyReferences.Length)
            {
                projectBuilder.Append("  </ItemGroup>\r\n");
                projectBuilder.Append("  <ItemGroup>\r\n");
                foreach (Assembly reference in assembly.assemblyReferences.Where(i => i.sourceFiles.Any(ShouldFileBePartOfSolution)))
                {
                    projectBuilder.Append("    <ProjectReference Include=\"").Append(reference.name).Append(k_CsProjectExtension + "\">\r\n");
                    projectBuilder.Append("      <Project>{").Append(ProjectGuid(reference.name)).Append("}</Project>\r\n");
                    projectBuilder.Append("      <Name>").Append(reference.name).Append("</Name>\r\n");
                    projectBuilder.Append("      <ReferenceOutputAssembly>false</ReferenceOutputAssembly>\r\n");
                    projectBuilder.Append("    </ProjectReference>\r\n");
                }
            }

            projectBuilder.Append(k_ProjectFooter);
            return projectBuilder.ToString();
        }

        static void AppendReference(string fullReference, StringBuilder projectBuilder)
        {
            //replace \ with / and \\ with /
            var escapedFullPath = SecurityElement.Escape(fullReference);
            escapedFullPath = escapedFullPath.Replace("\\\\", "/");
            escapedFullPath = escapedFullPath.Replace("\\", "/");
            Utility.GetFileNameWithoutExtension(escapedFullPath, out var start, out var end);
            projectBuilder.Append(" <Reference Include=\"").Append(escapedFullPath, start, end - start).Append("\">\r\n" +
                " <HintPath>").Append(escapedFullPath).Append("</HintPath>\r\n" +
                " </Reference>\r\n");
        }

        public string ProjectFile(Assembly assembly)
        {
            var fileBuilder = new StringBuilder(assembly.name);
            fileBuilder.Append(".csproj");
            return Path.Combine(ProjectDirectory, fileBuilder.ToString());
        }

        public string SolutionFile()
        {
            return Path.Combine(ProjectDirectory, $"{m_ProjectName}.sln");
        }

        void ProjectHeader(
            Assembly assembly,
            List<ResponseFileData> responseFilesData,
            string[] roslynAnalyzerDllPaths,
            StringBuilder sb
        )
        {
            var otherArguments = GetOtherArgumentsFromResponseFilesData(responseFilesData);
            bool allowUnsafe = assembly.compilerOptions.AllowUnsafeCode;
            var defines = new HashSet<string>(StringComparer.Ordinal);
            defines.Add("DEBUG");
            defines.Add("TRACE");
            foreach (var def in assembly.defines)
                defines.Add(def);
            foreach (var rfd in responseFilesData)
            {
                allowUnsafe |= rfd.Unsafe;
                foreach (var def in rfd.Defines)
                    defines.Add(def);
            }
            GetProjectHeaderTemplate(
                builder,
                ProjectGuid(assembly.name),
                assembly.name,
                defines,
                assembly.compilerOptions.AllowUnsafeCode | responseFilesData.Any(x => x.Unsafe),
                GenerateAnalyserItemGroup(otherArguments["analyzer"].Concat(otherArguments["a"])
                    .SelectMany(x => x.Split(';'))
                    .Concat(roslynAnalyzerDllPaths)
                    .Distinct()
                    .ToArray()));
        }

        private static ILookup<string, string> GetOtherArgumentsFromResponseFilesData(List<ResponseFileData> responseFilesData)
        {
            var paths = responseFilesData.SelectMany(x =>
                {
                    return x.OtherArguments.Where(a => a.StartsWith("/") || a.StartsWith("-"))
                                           .Select(b =>
                    {
                        var index = b.IndexOf(":", StringComparison.Ordinal);
                        if (index > 0 && b.Length > index)
                        {
                            var key = b.Substring(1, index - 1);
                            return new KeyValuePair<string, string>(key, b.Substring(index + 1));
                        }

                        const string warnaserror = "warnaserror";
                        if (b.Substring(1).StartsWith(warnaserror))
                        {
                            return new KeyValuePair<string, string>(warnaserror, b.Substring(warnaserror.Length + 1));
                        }

                        return default;
                    });
                })
              .Distinct()
              .ToLookup(o => o.Key, pair => pair.Value);
            return paths;
        }

        private static string GenerateAnalyserItemGroup(string[] paths)
        {
            //    <ItemGroup>
            //        <Analyzer Include="..\packages\Comments_analyser.1.0.6626.21356\analyzers\dotnet\cs\Comments_analyser.dll" />
            //        <Analyzer Include="..\packages\UnityEngineAnalyzer.1.0.0.0\analyzers\dotnet\cs\UnityEngineAnalyzer.dll" />
            //    </ItemGroup>
            if (!paths.Any())
                return string.Empty;

            var analyserBuilder = new StringBuilder();
            analyserBuilder.Append("  <ItemGroup>").Append(k_WindowsNewline);
            foreach (var path in paths)
            {
                analyserBuilder.Append($"    <Analyzer Include=\"{path}\" />").Append(k_WindowsNewline);
            }
            analyserBuilder.Append("  </ItemGroup>").Append(k_WindowsNewline);
            return analyserBuilder.ToString();
        }

        static string GetSolutionText()
        {
            return string.Join("\r\n", @"", @"Microsoft Visual Studio Solution File, Format Version {0}", @"# Visual Studio {1}", @"{2}", @"Global", @"    GlobalSection(SolutionConfigurationPlatforms) = preSolution", @"        Debug|Any CPU = Debug|Any CPU", @"    EndGlobalSection", @"    GlobalSection(ProjectConfigurationPlatforms) = postSolution", @"{3}", @"    EndGlobalSection", @"    GlobalSection(SolutionProperties) = preSolution", @"        HideSolutionNode = FALSE", @"    EndGlobalSection", @"EndGlobal", @"").Replace("    ", "\t");
        }

        static string GetProjectFooterTemplate()
        {
            return string.Join("\r\n", @"  </ItemGroup>", @"  <Import Project=""$(MSBuildToolsPath)\Microsoft.CSharp.targets"" />", @"  <!-- To modify your build process, add your task inside one of the targets below and uncomment it.", @"       Other similar extension points exist, see Microsoft.Common.targets.", @"  <Target Name=""BeforeBuild"">", @"  </Target>", @"  <Target Name=""AfterBuild"">", @"  </Target>", @"  -->", @"</Project>", @"");
        }

        static void GetProjectHeaderTemplate(
            StringBuilder sb,
            string assemblyGUID,
            string assemblyName,
            string defines,
            bool allowUnsafe,
            string analyzerBlock
        )
        {
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n");
            sb.Append("<Project ToolsVersion=\"" + k_ToolsVersion + "\" DefaultTargets=\"Build\" xmlns=\"" + k_MSBuildNamespaceUri + "\">\r\n");
            sb.Append("  <PropertyGroup>\r\n");
            sb.Append("    <LangVersion>" + k_TargetLanguageVersion + "</LangVersion>\r\n");
            sb.Append("  </PropertyGroup>\r\n");
            sb.Append("  <PropertyGroup>\r\n");
            sb.Append("    <Configuration Condition=\" '$(Configuration)' == '' \">Debug</Configuration>\r\n");
            sb.Append("    <Platform Condition=\" '$(Platform)' == '' \">AnyCPU</Platform>\r\n");
            sb.Append("    <ProductVersion>" + k_ProductVersion + "</ProductVersion>\r\n");
            sb.Append("    <SchemaVersion>2.0</SchemaVersion>\r\n");
            sb.Append("    <RootNamespace>").Append(EditorSettings.projectGenerationRootNamespace).Append("</RootNamespace>\r\n");
            sb.Append("    <ProjectGuid>{").Append(assemblyGUID).Append("}</ProjectGuid>\r\n");
            sb.Append("    <OutputType>Library</OutputType>\r\n");
            sb.Append("    <AppDesignerFolder>Properties</AppDesignerFolder>\r\n");
            sb.Append("    <AssemblyName>").Append(assemblyName).Append("</AssemblyName>\r\n");
            sb.Append("    <TargetFrameworkVersion>" + k_TargetFrameworkVersion + "</TargetFrameworkVersion>\r\n");
            sb.Append("    <FileAlignment>512</FileAlignment>\r\n");
            sb.Append("    <BaseDirectory>" + k_BaseDirectory + "</BaseDirectory>\r\n");
            sb.Append("  </PropertyGroup>\r\n");
            sb.Append("  <PropertyGroup Condition=\" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' \">\r\n");
            sb.Append("    <DebugSymbols>true</DebugSymbols>\r\n");
            sb.Append("    <DebugType>full</DebugType>\r\n");
            sb.Append("    <Optimize>false</Optimize>\r\n");
            sb.Append("    <OutputPath>Temp\\bin\\Debug\\</OutputPath>\r\n");

            sb.Append("    <DefineConstants>");
            foreach (var def in defines)
            {
                sb.Append(def);
                sb.Append(';');
            }
            sb.Length -= 1; // remove final ';' (we know there's always at least 2 defines)
            sb.Append("</DefineConstants>\r\n");

            sb.Append("    <ErrorReport>prompt</ErrorReport>\r\n");
            sb.Append("    <WarningLevel>4</WarningLevel>\r\n");
            sb.Append("    <NoWarn>0169</NoWarn>\r\n");
            sb.Append("    <AllowUnsafeBlocks>").Append(allowUnsafe).Append("</AllowUnsafeBlocks>\r\n");
            sb.Append("  </PropertyGroup>\r\n");
            sb.Append("  <PropertyGroup>\r\n");
            sb.Append("    <NoConfig>true</NoConfig>\r\n");
            sb.Append("    <NoStdLib>true</NoStdLib>\r\n");
            sb.Append("    <AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences>\r\n");
            sb.Append("    <ImplicitlyExpandNETStandardFacades>false</ImplicitlyExpandNETStandardFacades>\r\n");
            sb.Append("    <ImplicitlyExpandDesignTimeFacades>false</ImplicitlyExpandDesignTimeFacades>\r\n");
            sb.Append("  </PropertyGroup>\r\n");
            sb.Append(analyzerBlock).Append("\r\n");
            sb.Append("  <ItemGroup>\r\n");
        }

        void SyncSolution(IEnumerable<Assembly> assemblies)
        {
            SyncSolutionFileIfNotChanged(SolutionFile(), SolutionText(assemblies));
        }

        string SolutionText(IEnumerable<Assembly> assemblies)
        {
            var fileversion = "11.00";
            var vsversion = "2010";

            var relevantAssemblies = RelevantAssembliesForMode(assemblies);
            string projectEntries = GetProjectEntries(relevantAssemblies);
            string projectConfigurations = string.Join(k_WindowsNewline, relevantAssemblies.Select(i => GetProjectActiveConfigurations(ProjectGuid(i.name))).ToArray());
            return string.Format(k_SolutionFileFormat, fileversion, vsversion, projectEntries, projectConfigurations);
        }

        static IEnumerable<Assembly> RelevantAssembliesForMode(IEnumerable<Assembly> assemblies)
        {
            return assemblies.Where(i => ScriptingLanguage.CSharp == ScriptingLanguageFor(i));
        }

        /// <summary>
        /// Get a Project("{guid}") = "MyProject", "MyProject.csproj", "{projectguid}"
        /// entry for each relevant language
        /// </summary>
        string GetProjectEntries(IEnumerable<Assembly> assemblies)
        {
            var projectEntries = assemblies.Select(i => string.Format(
                k_SolutionProjectEntryTemplate,
                SolutionGuid(i),
                i.name,
                Path.GetFileName(ProjectFile(i)),
                ProjectGuid(i.name)
            ));

            return string.Join(k_WindowsNewline, projectEntries.ToArray());
        }

        /// <summary>
        /// Generate the active configuration string for a given project guid
        /// </summary>
        string GetProjectActiveConfigurations(string projectGuid)
        {
            return string.Format(
                k_SolutionProjectConfigurationTemplate,
                projectGuid);
        }

        string EscapedRelativePathFor(string file)
        {
            var projectDir = ProjectDirectory.Replace('/', '\\');
            file = file.Replace('/', '\\');
            var path = SkipPathPrefix(file, projectDir);

            var packageInfo = m_AssemblyNameProvider.FindForAssetPath(path.Replace('\\', '/'));
            if (packageInfo != null)
            {
                // We have to normalize the path, because the PackageManagerRemapper assumes
                // dir seperators will be os specific.
                var absolutePath = Path.GetFullPath(NormalizePath(path)).Replace('/', '\\');
                path = SkipPathPrefix(absolutePath, projectDir);
            }

            return SecurityElement.Escape(path);
        }

        static string SkipPathPrefix(string path, string prefix)
        {
            var prefixLength = prefix.Length;
            if (path.Length > prefixLength && path[prefixLength] == '\\' && string.CompareOrdinal(path, 0, prefix, 0, prefixLength) == 0)
                return path.Substring(prefixLength + 1);
            return path;
        }

        static string NormalizePath(string path)
        {
            if (Path.DirectorySeparatorChar == '\\')
                return path.Replace('/', Path.DirectorySeparatorChar);
            return path.Replace('\\', Path.DirectorySeparatorChar);
        }

        string ProjectGuid(string assembly)
        {
            return m_GUIDProvider.ProjectGuid(m_ProjectName, assembly);
        }

        string SolutionGuid(Assembly assembly)
        {
            return m_GUIDProvider.SolutionGuid(m_ProjectName, GetExtensionOfSourceFiles(assembly.sourceFiles));
        }

        void WriteVSCodeSettingsFiles()
        {
            var vsCodeDirectory = Path.Combine(ProjectDirectory, ".vscode");

            if (!m_FileIOProvider.Exists(vsCodeDirectory))
                m_FileIOProvider.CreateDirectory(vsCodeDirectory);

            var vsCodeSettingsJson = Path.Combine(vsCodeDirectory, "settings.json");

            if (!m_FileIOProvider.Exists(vsCodeSettingsJson))
                m_FileIOProvider.WriteAllText(vsCodeSettingsJson, k_SettingsJson);
        }
    }

    public static class SolutionGuidGenerator
    {
        static MD5 mD5 = MD5CryptoServiceProvider.Create();

        public static string GuidForProject(string projectName)
        {
            return ComputeGuidHashFor(projectName + "salt");
        }

        public static string GuidForSolution(string projectName, string sourceFileExtension)
        {
            if (sourceFileExtension.ToLower() == "cs")

                // GUID for a C# class library: http://www.codeproject.com/Reference/720512/List-of-Visual-Studio-Project-Type-GUIDs
                return "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";

            return ComputeGuidHashFor(projectName);
        }

        static string ComputeGuidHashFor(string input)
        {
            var hash = mD5.ComputeHash(Encoding.Default.GetBytes(input));
            return new Guid(hash).ToString();
        }
    }
}
