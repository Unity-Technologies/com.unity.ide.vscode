using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using UnityEditor.Compilation;
using UnityEngine.Profiling;

namespace VSCodeEditor
{
    public class NewCSharpAmazementsBallz : IGenerator
    {
        const string k_WindowsNewline = "\r\n";
        string m_SolutionProjectEntryTemplate = string.Join(k_WindowsNewline,
            @"Project(""{{{0}}}"") = ""{1}"", ""{2}"", ""{{{3}}}""",
            @"EndProject");
        string m_SolutionProjectConfigurationTemplate = string.Join(k_WindowsNewline,
            @"        {{{0}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
            @"        {{{0}}}.Debug|Any CPU.Build.0 = Debug|Any CPU",
            @"        {{{0}}}.Release|Any CPU.ActiveCfg = Release|Any CPU",
            @"        {{{0}}}.Release|Any CPU.Build.0 = Release|Any CPU").Replace("    ", "\t");
        const string m_ExcludeGlobs = "<Compile Remove=\"{0}\\**\" /> <None Remove=\"{0}\\**\" />";
        const string m_ProjectReference = "<ProjectReference Include=\"{0}\" />";
        const string m_CsProjectTemplate = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net471</TargetFramework>
    <LangVersion>7.3</LangVersion>
    <AssemblyName>{0}</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <None Remove=""**\*.meta"" />
    <None Remove=""Library\*"" />
{1}
  </ItemGroup>
</Project>
";

        public string ProjectDirectory { get; }
        readonly string m_ProjectName;
        readonly IAssemblyNameProvider m_AssemblyNameProvider;
        readonly IFileIO m_FileIOProvider;
        readonly IGUIDGenerator m_GUIDProvider;

        public NewCSharpAmazementsBallz(string tempDirectory)
            : this(tempDirectory, new AssemblyNameProvider(), new FileIOProvider(), new GUIDProvider()) { }

        public NewCSharpAmazementsBallz(string tempDirectory, IAssemblyNameProvider assemblyNameProvider, IFileIO fileIO, IGUIDGenerator guidGenerator)
        {
            ProjectDirectory = tempDirectory.Replace('\\', '/');
            m_ProjectName = Path.GetFileName(ProjectDirectory);
            m_AssemblyNameProvider = assemblyNameProvider;
            m_FileIOProvider = fileIO;
            m_GUIDProvider = guidGenerator;
        }

        public bool SyncIfNeeded(IEnumerable<string> affectedFiles, IEnumerable<string> reimportedFiles)
        {
            Profiler.BeginSample("SolutionSynchronizerSync");
            //SetupProjectSupportedExtensions();

            // Don't sync if we haven't synced before
            if (SolutionExists() && HasFilesBeenModified(affectedFiles, reimportedFiles))
            {
                Sync();

                Profiler.EndSample();
                return true;
            }

            Profiler.EndSample();
            return false;
        }

        bool HasFilesBeenModified(IEnumerable<string> affectedFiles, IEnumerable<string> reimportedFiles)
        {
            return affectedFiles.Any(m_AssemblyNameProvider.ShouldFileBePartOfSolution)
                || reimportedFiles.Any(m_AssemblyNameProvider.ShouldSyncOnReimportedAsset);
        }

        public void Sync()
        {
            var assemblies = m_AssemblyNameProvider.GetAssemblies().ToList();
            GenerateAndWriteSolution(assemblies);
            GenerateAndWriteProjects(assemblies);
        }

        void GenerateAndWriteSolution(IEnumerable<Assembly> assemblies)
        {
            SyncSolution(assemblies);
        }

        void GenerateAndWriteProjects(IEnumerable<Assembly> assemblies)
        {
            var allProjectIslands = m_AssemblyNameProvider.RelevantAssemblies(assemblies).ToList();
            var allAssetProjectParts = GenerateAllAssetProjectParts();
            var asmdefProjectPaths = GenerateAllAsmdefProjectPaths(allProjectIslands);
            foreach (Assembly assembly in allProjectIslands)
            {
                var responseFileData = ParseResponseFileData(assembly);
                SyncProject(assembly, allAssetProjectParts, responseFileData, allProjectIslands, asmdefProjectPaths);
            }
        }

        static Dictionary<string, string> GenerateAllAsmdefProjectPaths(List<Assembly> allProjectAssemblies)
        {
            var res = new Dictionary<string, string>();
            foreach(var assembly in allProjectAssemblies)
            {
                var asmdefpath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly.name);
                if (asmdefpath == null)
                {
                    continue;
                }
                res.Add(assembly.name, asmdefpath);
            }
            return res;
        }

        void SyncProject(Assembly island,
            Dictionary<string, string> allAssetsProjectParts,
            IEnumerable<ResponseFileData> responseFilesData,
            List<Assembly> allProjectIslands, Dictionary<string, string> asmdefProjectPaths)
        {
            SyncFileIfNotChanged(ProjectFile(island), ProjectText(island, allAssetsProjectParts, responseFilesData, allProjectIslands, asmdefProjectPaths));
        }

        Dictionary<string, string> GenerateAllAssetProjectParts()
        {
            Dictionary<string, StringBuilder> stringBuilders = new Dictionary<string, StringBuilder>();

            foreach (string asset in m_AssemblyNameProvider.GetAllAssetPaths())
            {
                // Exclude files coming from packages except if they are internalized.
                // Find assembly the asset belongs to by adding script extension and using compilation pipeline.
                var assemblyName = m_AssemblyNameProvider.GetAssemblyNameFromScriptPath(asset + ".cs");

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

            var result = new Dictionary<string, string>();

            foreach (var entry in stringBuilders)
                result[entry.Key] = entry.Value.ToString();

            return result;
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
                    UnityEngine.Debug.LogError($"{error.Key} Parse Error : {valueError}");
                }
            }

            return responseFilesData.Select(x => x.Value);
        }

        string ProjectText(Assembly assembly,
            Dictionary<string, string> allAssetsProjectParts,
            IEnumerable<ResponseFileData> responseFilesData,
            List<Assembly> allProjectIslands,
            Dictionary<string, string> asmdefProjectPaths)
        {
            var builder = new StringBuilder();

            ExcludeAsmdefs(assembly, asmdefProjectPaths, ref builder);
            AddProjectReferences(assembly.assemblyReferences, ref builder);

            var responseFileData = ParseResponseFileData(assembly);
            AddReferences(assembly.compiledAssemblyReferences
                .Union(responseFileData.SelectMany(reference => reference.FullPathReferences)), ref builder);

            return string.Format(m_CsProjectTemplate, builder, assembly.name);
        }

        static void AddReferences(IEnumerable<string> references, ref StringBuilder builder)
        {
            foreach (var reference in references)
            {
                //replace \ with / and \\ with /
                var escapedFullPath = SecurityElement.Escape(reference);
                escapedFullPath = escapedFullPath.Replace("\\", "/");
                escapedFullPath = escapedFullPath.Replace("\\\\", "/");
                builder.Append(" <Reference Include=\"").Append(Path.GetFileNameWithoutExtension(escapedFullPath)).Append("\">").Append(k_WindowsNewline);
                builder.Append(" <HintPath>").Append(escapedFullPath).Append("</HintPath>").Append(k_WindowsNewline);
                builder.Append(" </Reference>").Append(k_WindowsNewline);
            }
        }

        void AddProjectReferences(Assembly[] assemblyReferences, ref StringBuilder builder)
        {
            if (0 < assemblyReferences.Length)
            {
                foreach (Assembly reference in assemblyReferences)
                {
                    var referencedProject = reference.outputPath;

                    builder.Append("    <ProjectReference Include=\"").Append(reference.name).Append(".csproj").Append("\" />").Append(k_WindowsNewline);
                }
            }
        }

        void ExcludeAsmdefs(Assembly assembly, Dictionary<string, string> asmdefProjectPaths, ref StringBuilder stringBuilder)
        {
            var asmdefPath = asmdefProjectPaths.ContainsKey(assembly.name) ? asmdefProjectPaths[assembly.name] : null;
            var excludedPaths = GetExcludePaths(asmdefPath, asmdefProjectPaths.Values);
            stringBuilder.Append(string.Join(k_WindowsNewline, excludedPaths.Select(path => string.Format(m_ExcludeGlobs, path))));
        }

        void SyncSolution(IEnumerable<Assembly> islands)
        {
            SyncFileIfNotChanged(SolutionFile(), SolutionText(islands));
        }

        string SolutionText(IEnumerable<Assembly> islands)
        {
            var fileversion = "11.00";
            var vsversion = "2010";

            var relevantIslands = m_AssemblyNameProvider.RelevantAssemblies(islands);
            string projectEntries = GetProjectEntries(relevantIslands);
            string projectConfigurations = string.Join(k_WindowsNewline, relevantIslands.Select(i => GetProjectActiveConfigurations(ProjectGuid(i.name))).ToArray());
            return string.Format(GetSolutionText(), fileversion, vsversion, projectEntries, projectConfigurations);
        }

        static string GetSolutionText()
        {
            return string.Join(k_WindowsNewline,
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
                @"EndGlobal",
                @"").Replace("    ", "\t");
        }

        /// <summary>
        /// Get a Project("{guid}") = "MyProject", "MyProject.csproj", "{projectguid}"
        /// entry for each relevant language
        /// </summary>
        string GetProjectEntries(IEnumerable<Assembly> islands)
        {
            var projectEntries = islands.Select(i => string.Format(
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

        string ProjectFile(Assembly assembly)
        {
            var fileName = "." + assembly.name + ".csproj";
            return Path.Combine(ProjectDirectory, fileName);
        }

        public string SolutionFile()
        {
            return Path.Combine(ProjectDirectory, $"{m_ProjectName}.sln");
        }

        public void GenerateAll(bool generateAll)
        {
        }

        public bool SolutionExists()
        {
            return m_FileIOProvider.Exists(SolutionFile());
        }

        string ProjectGuid(string assembly)
        {
            return m_GUIDProvider.ProjectGuid(m_ProjectName, assembly);
        }

        string SolutionGuid(Assembly island)
        {
            return m_GUIDProvider.SolutionGuid(m_ProjectName, m_AssemblyNameProvider.GetExtensionOfSourceFiles(island.sourceFiles));
        }

        public List<string> GetExcludePaths(string currentAsmdefPath, IEnumerable<string> allAsmdefPaths) // TODO: Throw if not absolute?? maybe some relative checks
        {
            var res = new List<string>();
            foreach (string path in allAsmdefPaths)
            {
                if (path == currentAsmdefPath)
                {
                    continue;
                }

                if (currentAsmdefPath == null || path.StartsWith(currentAsmdefPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    res.Add(path);
                }
            }
            return res;
        }
    }
}
