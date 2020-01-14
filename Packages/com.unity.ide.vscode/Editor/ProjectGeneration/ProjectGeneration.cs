using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Profiling;

namespace VSCodeEditor
{
    public interface IGenerator
    {
        bool SyncIfNeeded(List<string> affectedFiles, string[] reimportedFiles);
        void Sync();
        string SolutionFile();
        string ProjectDirectory { get; }
        void GenerateAll(bool generateAll);
        bool SolutionExists();
    }

    public class ProjectGeneration : IGenerator
    {
        enum ScriptingLanguage
        {
            None,
            CSharp
        }

        public static readonly string MSBuildNamespaceUri = "http://schemas.microsoft.com/developer/msbuild/2003";

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

        string m_SolutionProjectEntryTemplate = string.Join("\r\n", @"Project(""{{{0}}}"") = ""{1}"", ""{2}"", ""{{{3}}}""", @"EndProject").Replace("    ", "\t");

        string m_SolutionProjectConfigurationTemplate = string.Join("\r\n", @"        {{{0}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU", @"        {{{0}}}.Debug|Any CPU.Build.0 = Debug|Any CPU", @"        {{{0}}}.Release|Any CPU.ActiveCfg = Release|Any CPU", @"        {{{0}}}.Release|Any CPU.Build.0 = Release|Any CPU").Replace("    ", "\t");

        static readonly string[] k_ReimportSyncExtensions = { ".dll", ".asmdef" };

        string[] m_ProjectSupportedExtensions = new string[0];
        string m_ProjectRootNamespace;
        public string ProjectDirectory { get; }
        bool m_ShouldGenerateAll;

        public void GenerateAll(bool generateAll)
        {
            m_ShouldGenerateAll = generateAll;
        }

        readonly string m_ProjectName;
        readonly IAssemblyNameProvider m_AssemblyNameProvider;
        readonly IFileIO m_FileIOProvider;
        readonly IGUIDGenerator m_GUIDProvider;
        readonly HashSet<string> m_FileThatShouldBeUsed;
        readonly HashSet<string> m_FileThatShouldNotBeUsed;
        readonly Dictionary<string, UnityEditor.PackageManager.PackageInfo> m_PackageAssets;

        const string k_ToolsVersion = "4.0";
        const string k_ProductVersion = "10.0.20506";
        const string k_BaseDirectory = ".";
        const string k_TargetFrameworkVersion = "v4.7.1";
        const string k_TargetLanguageVersion = "latest";

        public ProjectGeneration(string tempDirectory)
            : this(tempDirectory, new AssemblyNameProvider(), new FileIOProvider(), new GUIDProvider()) { }

        public ProjectGeneration(string tempDirectory, IAssemblyNameProvider assemblyNameProvider, IFileIO fileIO, IGUIDGenerator guidGenerator)
        {
            ProjectDirectory = tempDirectory.Replace('\\', '/');
            m_ProjectName = Path.GetFileName(ProjectDirectory);
            m_AssemblyNameProvider = assemblyNameProvider;
            m_FileIOProvider = fileIO;
            m_GUIDProvider = guidGenerator;
            m_FileThatShouldBeUsed = new HashSet<string>();
            m_FileThatShouldNotBeUsed = new HashSet<string>();
            m_PackageAssets = new Dictionary<string, UnityEditor.PackageManager.PackageInfo>();
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

                var affectedNames = affectedFiles.Select(asset => m_AssemblyNameProvider.GetAssemblyNameFromScriptPath(asset)?.Split(new [] {".dll"}, StringSplitOptions.RemoveEmptyEntries)[0]);
                var reimportedNames = reimportedFiles.Select(asset => m_AssemblyNameProvider.GetAssemblyNameFromScriptPath(asset)?.Split(new [] {".dll"}, StringSplitOptions.RemoveEmptyEntries)[0]);
                var affectedAndReimported = new HashSet<string>(affectedNames.Concat(reimportedNames));
                var assemblyNames = new HashSet<string>(allProjectAssemblies.Select(assembly => Path.GetFileName(assembly.outputPath)));

                //System.Threading.Tasks.Parallel.For(0, allProjectAssemblies.Count, index =>
                foreach (var assembly in allProjectAssemblies)
                {
                    //var assembly = allProjectAssemblies[index];
                    if (!affectedAndReimported.Contains(assembly.name))
                    {
                        continue;
                        //return;
                    }

                    SyncProject(assembly, allAssetProjectParts, ParseResponseFileData(assembly), assemblyNames);
                //});
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
            m_ProjectSupportedExtensions = EditorSettings.projectGenerationUserExtensions;
            m_ProjectRootNamespace = EditorSettings.projectGenerationRootNamespace;
        }

        bool ShouldFileBePartOfSolution(string file)
        {
            if (m_FileThatShouldBeUsed.Contains(file))
            {
                return true;
            }
            if (m_FileThatShouldNotBeUsed.Contains(file))
            {
                return false;
            }

            // Exclude files coming from packages except if they are internalized.
            if (!m_ShouldGenerateAll && IsInternalizedPackagePath(file))
            {
                m_FileThatShouldNotBeUsed.Add(file);
                return false;
            }

            string extension = Path.GetExtension(file);
            // Dll's are not scripts but still need to be included..
            if (extension == ".dll")
            {
                m_FileThatShouldBeUsed.Add(file);
                return true;
            }

            if (file.ToLower().EndsWith(".asmdef"))
            {
                m_FileThatShouldBeUsed.Add(file);
                return true;
            }

            var supportExtension = IsSupportedExtension(extension);
            if (supportExtension)
            {
                m_FileThatShouldBeUsed.Add(file);
            }
            else
            {
                m_FileThatShouldNotBeUsed.Add(file);
            }
            return supportExtension;
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
            var assemblyNames = new HashSet<string>(allProjectAssemblies.Select(assembly => Path.GetFileName(assembly.outputPath)));
            //System.Threading.Tasks.Parallel.For(0, allProjectAssemblies.Count, index =>
            foreach (var assembly in allProjectAssemblies)
            {
                //var assembly = allProjectAssemblies[index];
                var responseFileData = ParseResponseFileData(assembly);
                SyncProject(assembly, allAssetProjectParts, responseFileData, assemblyNames);
            //});
            }

            WriteVSCodeSettingsFiles();
        }

        IEnumerable<ResponseFileData> ParseResponseFileData(Assembly assembly)
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

            return responseFilesData.Select(x => x.Value);
        }

        Dictionary<string, string> GenerateAllAssetProjectParts()
        {
            Dictionary<string, StringBuilder> stringBuilders = new Dictionary<string, StringBuilder>();

            foreach (string asset in m_AssemblyNameProvider.GetAllAssetPaths())
            {
                // Exclude files coming from packages except if they are internalized.
                // TODO: We need assets from the assembly API
                if (!m_ShouldGenerateAll && IsInternalizedPackagePath(asset))
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

                    assemblyName = Utility.FileNameWithoutExtension(assemblyName);

                    if (!stringBuilders.TryGetValue(assemblyName, out var projectBuilder))
                    {
                        projectBuilder = new StringBuilder();
                        stringBuilders[assemblyName] = projectBuilder;
                    }

                    projectBuilder.Append("     <None Include=\"").Append(EscapedRelativePathFor(asset)).Append("\" />").Append(k_WindowsNewline);
                }
            }

            var result = new Dictionary<string, string>();

            foreach (var entry in stringBuilders)
                result[entry.Key] = entry.Value.ToString();

            return result;
        }

        bool IsInternalizedPackagePath(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                return false;
            }

            UnityEditor.PackageManager.PackageInfo packageInfo;
            if (!m_PackageAssets.ContainsKey(file))
            {
                packageInfo = m_AssemblyNameProvider.FindForAssetPath(file);
                if (packageInfo == null)
                {
                    return false;
                }
                m_PackageAssets.Add(file, packageInfo);
            }
            else
            {
                packageInfo = m_PackageAssets[file];
            }

            var packageSource = packageInfo.source;
            return packageSource != PackageSource.Embedded && packageSource != PackageSource.Local;
        }

        void SyncProject(
            Assembly assembly,
            Dictionary<string, string> allAssetsProjectParts,
            IEnumerable<ResponseFileData> responseFilesData,
            HashSet<string> assemblyNames)
        {
            SyncProjectFileIfNotChanged(ProjectFile(assembly), ProjectText(assembly, allAssetsProjectParts, responseFilesData, assemblyNames));
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
            if (m_FileIOProvider.Exists(filename))
            {
                var currentContents = m_FileIOProvider.ReadAllText(filename);

                if (currentContents == newContents)
                {
                    return;
                }
            }

            m_FileIOProvider.WriteAllText(filename, newContents);
        }

        string ProjectText(
            Assembly assembly,
            Dictionary<string, string> allAssetsProjectParts,
            IEnumerable<ResponseFileData> responseFilesData,
            HashSet<string> assemblyNames)
        {
            var projectBuilder = new StringBuilder();
            ProjectHeader(assembly, responseFilesData, projectBuilder);
            var references = new List<string>();
            var projectReferences = new List<Assembly>();

            foreach (string file in assembly.sourceFiles)
            {
                if (!ShouldFileBePartOfSolution(file))
                    continue;

                var extension = Path.GetExtension(file).ToLower();
                var fullFile = EscapedRelativePathFor(file);
                if (".dll" != extension)
                {
                    projectBuilder.Append("     <Compile Include=\"").Append(fullFile).Append("\" />").Append(k_WindowsNewline);
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
            foreach (var reference in assembly.compiledAssemblyReferences.Union(responseRefs).Union(references))
            {
                string fullReference = Path.IsPathRooted(reference) ? reference : Path.Combine(ProjectDirectory, reference);
                AppendReference(fullReference, projectBuilder);
            }

            if (0 < assembly.assemblyReferences.Length)
            {
                projectBuilder.Append("  </ItemGroup>").Append(k_WindowsNewline);
                projectBuilder.Append("  <ItemGroup>").Append(k_WindowsNewline);
                foreach (Assembly reference in assembly.assemblyReferences)
                {
                    var referencedProject = reference.outputPath;

                    projectBuilder.Append("    <ProjectReference Include=\"").Append(reference.name).Append(GetProjectExtension()).Append("\">").Append(k_WindowsNewline);
                    projectBuilder.Append("      <Project>{").Append(ProjectGuid(reference.name)).Append("}</Project>").Append(k_WindowsNewline);
                    projectBuilder.Append("      <Name>").Append(reference.name).Append("</Name>").Append(k_WindowsNewline);
                    projectBuilder.Append("    </ProjectReference>").Append(k_WindowsNewline);
                }
            }

            projectBuilder.Append(ProjectFooter());
            return projectBuilder.ToString();
        }

        static readonly char[] toBeEscaped = new[] { '<', '>', '"', '&', '\\' };

        static void AppendPathEscaped(StringBuilder dest, string path, int start, int end) // Note: start:end, not start+length
        {
            while (start < end) {
                int j = path.IndexOfAny(toBeEscaped, start);
                if (j == -1) {
                    dest.Append(path, start, end - start);
                    return;
                }

                dest.Append(path, start, j - start);
                char c = path[j];
                start = j + 1;
                switch (c) {
                case '\\':
                    dest.Append('/');
                    if (start < end && path[start] == '\\') ++start;
                    continue;
                case '<': dest.Append("&lt;"); continue;
                case '>': dest.Append("&gt;"); continue;
                case '&': dest.Append("&amp;"); continue;
                case '"': dest.Append("&quot;"); continue;
                default:
                    throw new InvalidOperationException("should not happen: " + c);
                }
            }
        }

        static void AppendReference(string fullReference, StringBuilder projectBuilder)
        {
            //replace \ with / and \\ with /
            //var escapedFullPath = SecurityElement.Escape(fullReference);
            //escapedFullPath = escapedFullPath.Replace("\\\\", "/");
            //escapedFullPath = escapedFullPath.Replace("\\", "/");
            projectBuilder.Append(" <Reference Include=\"");
            Utility.GetFileNameWithoutExtension(fullReference, out var start, out var end);
            AppendPathEscaped(projectBuilder, fullReference, start, end);
            //Utility.AppendFileNameWithoutExtension(projectBuilder, escapedFullPath);
            projectBuilder.Append("\">").Append(k_WindowsNewline);
            projectBuilder.Append(" <HintPath>");
            AppendPathEscaped(projectBuilder, fullReference, 0, fullReference.Length);
            projectBuilder.Append("</HintPath>").Append(k_WindowsNewline);
            projectBuilder.Append(" </Reference>").Append(k_WindowsNewline);
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
            IEnumerable<ResponseFileData> responseFilesData,
            StringBuilder builder
        )
        {
            // TODO: .Concat(EditorUserBuildSettings.activeScriptCompilationDefines)
            GetProjectHeaderTemplate(
                builder,
                ProjectGuid(assembly.name),
                assembly.name,
                string.Join(";", new[] { "DEBUG", "TRACE" }.Concat(assembly.defines).Concat(responseFilesData.SelectMany(x => x.Defines)).Distinct().ToArray()),
                assembly.compilerOptions.AllowUnsafeCode | responseFilesData.Any(x => x.Unsafe),
                m_ProjectRootNamespace);
        }

        static string GetSolutionText()
        {
            return string.Join("\r\n", @"", @"Microsoft Visual Studio Solution File, Format Version {0}", @"# Visual Studio {1}", @"{2}", @"Global", @"    GlobalSection(SolutionConfigurationPlatforms) = preSolution", @"        Debug|Any CPU = Debug|Any CPU", @"        Release|Any CPU = Release|Any CPU", @"    EndGlobalSection", @"    GlobalSection(ProjectConfigurationPlatforms) = postSolution", @"{3}", @"    EndGlobalSection", @"    GlobalSection(SolutionProperties) = preSolution", @"        HideSolutionNode = FALSE", @"    EndGlobalSection", @"EndGlobal", @"").Replace("    ", "\t");
        }

        static string GetProjectFooterTemplate()
        {
            return string.Join("\r\n", @"  </ItemGroup>", @"  <Import Project=""$(MSBuildToolsPath)\Microsoft.CSharp.targets"" />", @"  <!-- To modify your build process, add your task inside one of the targets below and uncomment it.", @"       Other similar extension points exist, see Microsoft.Common.targets.", @"  <Target Name=""BeforeBuild"">", @"  </Target>", @"  <Target Name=""AfterBuild"">", @"  </Target>", @"  -->", @"</Project>", @"");
        }

        static void GetProjectHeaderTemplate(
            StringBuilder builder,
            string assemblyGUID,
            string assemblyName,
            string defines,
            bool allowUnsafe,
            string rootNamespace
        )
        {
            builder.Append(@"<?xml version=""1.0"" encoding=""utf-8""?>").Append(k_WindowsNewline);
            builder.Append(@"<Project ToolsVersion=""").Append(k_ToolsVersion).Append(@""" DefaultTargets=""Build"" xmlns=""").Append(MSBuildNamespaceUri).Append(@""">").Append(k_WindowsNewline);
            builder.Append(@"  <PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"    <LangVersion>").Append(k_TargetLanguageVersion).Append("</LangVersion>").Append(k_WindowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"  <PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"    <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>").Append(k_WindowsNewline);
            builder.Append(@"    <Platform Condition="" '$(Platform)' == '' "">AnyCPU</Platform>").Append(k_WindowsNewline);
            builder.Append(@"    <ProductVersion>").Append(k_ProductVersion).Append("</ProductVersion>").Append(k_WindowsNewline);
            builder.Append(@"    <SchemaVersion>2.0</SchemaVersion>").Append(k_WindowsNewline);
            builder.Append(@"    <RootNamespace>").Append(rootNamespace).Append("</RootNamespace>").Append(k_WindowsNewline);
            builder.Append(@"    <ProjectGuid>{").Append(assemblyGUID).Append("}</ProjectGuid>").Append(k_WindowsNewline);
            builder.Append(@"    <OutputType>Library</OutputType>").Append(k_WindowsNewline);
            builder.Append(@"    <AppDesignerFolder>Properties</AppDesignerFolder>").Append(k_WindowsNewline);
            builder.Append(@"    <AssemblyName>").Append(assemblyName).Append("</AssemblyName>").Append(k_WindowsNewline);
            builder.Append(@"    <TargetFrameworkVersion>").Append(k_TargetFrameworkVersion).Append("</TargetFrameworkVersion>").Append(k_WindowsNewline);
            builder.Append(@"    <FileAlignment>512</FileAlignment>").Append(k_WindowsNewline);
            builder.Append(@"    <BaseDirectory>").Append(k_BaseDirectory).Append("</BaseDirectory>").Append(k_WindowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' "">").Append(k_WindowsNewline);
            builder.Append(@"    <DebugSymbols>true</DebugSymbols>").Append(k_WindowsNewline);
            builder.Append(@"    <DebugType>full</DebugType>").Append(k_WindowsNewline);
            builder.Append(@"    <Optimize>false</Optimize>").Append(k_WindowsNewline);
            builder.Append(@"    <OutputPath>Temp\bin\Debug\</OutputPath>").Append(k_WindowsNewline);
            builder.Append(@"    <DefineConstants>").Append(defines).Append("</DefineConstants>").Append(k_WindowsNewline);
            builder.Append(@"    <ErrorReport>prompt</ErrorReport>").Append(k_WindowsNewline);
            builder.Append(@"    <WarningLevel>4</WarningLevel>").Append(k_WindowsNewline);
            builder.Append(@"    <NoWarn>0169</NoWarn>").Append(k_WindowsNewline);
            builder.Append(@"    <AllowUnsafeBlocks>").Append(allowUnsafe).Append("</AllowUnsafeBlocks>").Append(k_WindowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' "">").Append(k_WindowsNewline);
            builder.Append(@"    <DebugType>pdbonly</DebugType>").Append(k_WindowsNewline);
            builder.Append(@"    <Optimize>true</Optimize>").Append(k_WindowsNewline);
            builder.Append(@"    <OutputPath>Temp\bin\Release\</OutputPath>").Append(k_WindowsNewline);
            builder.Append(@"    <ErrorReport>prompt</ErrorReport>").Append(k_WindowsNewline);
            builder.Append(@"    <WarningLevel>4</WarningLevel>").Append(k_WindowsNewline);
            builder.Append(@"    <NoWarn>0169</NoWarn>").Append(k_WindowsNewline);
            builder.Append(@"    <AllowUnsafeBlocks>").Append(allowUnsafe).Append("</AllowUnsafeBlocks>").Append(k_WindowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"  <PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"    <NoConfig>true</NoConfig>").Append(k_WindowsNewline);
            builder.Append(@"    <NoStdLib>true</NoStdLib>").Append(k_WindowsNewline);
            builder.Append(@"    <AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences>").Append(k_WindowsNewline);
            builder.Append(@"    <ImplicitlyExpandNETStandardFacades>false</ImplicitlyExpandNETStandardFacades>").Append(k_WindowsNewline);
            builder.Append(@"    <ImplicitlyExpandDesignTimeFacades>false</ImplicitlyExpandDesignTimeFacades>").Append(k_WindowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"  <ItemGroup>").Append(k_WindowsNewline);
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
            return string.Format(GetSolutionText(), fileversion, vsversion, projectEntries, projectConfigurations);
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
                m_SolutionProjectEntryTemplate,
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
                m_SolutionProjectConfigurationTemplate,
                projectGuid);
        }

        string EscapedRelativePathFor(string file)
        {
            var projectDir = ProjectDirectory.Replace('/', '\\');
            file = file.Replace('/', '\\');
            var path = SkipPathPrefix(file, projectDir);

            if (m_PackageAssets.ContainsKey(file))
            {
                var packageInfo = m_PackageAssets[file];
                if (packageInfo != null)
                {
                    // We have to normalize the path, because the PackageManagerRemapper assumes
                    // dir seperators will be os specific.
                    var absolutePath = Path.GetFullPath(NormalizePath(path)).Replace('/', '\\');
                    path = SkipPathPrefix(absolutePath, projectDir);
                }
            }

            return SecurityElement.Escape(path);
        }

        static string SkipPathPrefix(string path, string prefix)
        {
            if (path.StartsWith($@"{prefix}\"))
                return path.Substring(prefix.Length + 1);
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

        static string ProjectFooter()
        {
            return GetProjectFooterTemplate();
        }

        static string GetProjectExtension()
        {
            return ".csproj";
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
        //static MD5 mD5 = MD5CryptoServiceProvider.Create();

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
            var hash = MD5.Create().ComputeHash(Encoding.Default.GetBytes(input));
            return new Guid(hash).ToString();
        }
    }
}
