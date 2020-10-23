using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;

namespace VSCodeEditor
{
    public struct GenerationJob
    {
        const string k_WindowsNewline = "\r\n";
        const string k_ToolsVersion = "4.0";
        const string k_ProductVersion = "10.0.20506";
        const string k_BaseDirectory = ".";
        const string k_TargetFrameworkVersion = "v4.7.1";
        const string k_TargetLanguageVersion = "latest";
        const string k_MSBuildNamespaceUri = "http://schemas.microsoft.com/developer/msbuild/2003";

        enum ScriptingLanguage
        {
            None,
            CSharp
        }

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

        public Assembly Assembly;
        public string ProjectDirectory;
        public string ProjectName;
        public string AdditionalAssetsForProject;
        public Dictionary<string, PackageInfo> AssetToPackageInfo;
        public Dictionary<string, bool> IsInternalizedPackagePath;
        public List<ResponseFileData> ResponseFilesData;
        public IEnumerable<string> RoslynAnalyzerPaths;
        public string[] ProjectSupportedExtensions;
        public string[] ActiveScriptCompilationDefines;
        public string ProjectGenerationRootNamespace;

        public IGUIDGenerator GuidProvider;
        public IFileIO FileIOProvider;

        public void Execute()
        {
            var projectFile = ProjectUtility.ProjectFile(Assembly.name, ProjectDirectory);
            var projectText = ProjectText();
            SyncProjectFileIfNotChanged(projectFile, projectText);
        }

        bool IsSupportedExtension(string extension)
        {
            extension = extension.TrimStart('.');
            if (k_BuiltinSupportedExtensions.ContainsKey(extension))
                return true;
            if (ProjectSupportedExtensions.Contains(extension))
                return true;
            return false;
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

        static string NormalizePath(string path)
        {
            if (Path.DirectorySeparatorChar == '\\')
                return path.Replace('/', Path.DirectorySeparatorChar);
            return path.Replace('\\', Path.DirectorySeparatorChar);
        }

        static string SkipPathPrefix(string path, string prefix)
        {
            if (path.StartsWith($@"{prefix}\"))
                return path.Substring(prefix.Length + 1);
            return path;
        }

        string EscapedRelativePathFor(string file)
        {
            var projectDir = ProjectDirectory.Replace('/', '\\');
            file = file.Replace('/', '\\');
            var path = SkipPathPrefix(file, projectDir);

            var packageInfo = AssetToPackageInfo.ContainsKey(path.Replace('\\', '/'));
            if (packageInfo)
            {
                // We have to normalize the path, because the PackageManagerRemapper assumes
                // dir seperators will be os specific.
                var absolutePath = Path.GetFullPath(NormalizePath(path)).Replace('/', '\\');
                path = SkipPathPrefix(absolutePath, projectDir);
            }

            return SecurityElement.Escape(path);
        }

        bool ShouldFileBePartOfSolution(string file)
        {
            // Exclude files coming from packages except if they are internalized.
            if (IsInternalizedPackagePath[file])
            {
                return false;
            }

            return HasValidExtension(file);
        }

        static void AppendReference(string fullReference, StringBuilder projectBuilder)
        {
            //replace \ with / and \\ with /
            var escapedFullPath = SecurityElement.Escape(fullReference);
            escapedFullPath = escapedFullPath.Replace("\\\\", "/");
            escapedFullPath = escapedFullPath.Replace("\\", "/");
            projectBuilder.Append("    <Reference Include=\"").Append(Path.GetFileNameWithoutExtension(escapedFullPath)).Append("\">").Append(k_WindowsNewline);
            projectBuilder.Append("        <HintPath>").Append(escapedFullPath).Append("</HintPath>").Append(k_WindowsNewline);
            projectBuilder.Append("    </Reference>").Append(k_WindowsNewline);
        }

        string ProjectGuid(string assembly)
        {
            return GuidProvider.ProjectGuid(ProjectName, assembly);
        }

        string ProjectText()
        {
            var projectBuilder = new StringBuilder();
            ProjectHeader(projectBuilder);
            var references = new List<string>();

            foreach (string file in Assembly.sourceFiles)
            {
                if (!HasValidExtension(file))
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
            projectBuilder.Append(AdditionalAssetsForProject);

            var responseRefs = ResponseFilesData.SelectMany(x => x.FullPathReferences.Select(r => r));
            GenerationJob tmpThis = this;
            var internalAssemblyReferences = tmpThis.Assembly.assemblyReferences
              .Where(i => !i.sourceFiles.Any(tmpThis.ShouldFileBePartOfSolution)).Select(i => i.outputPath);
            var allReferences =
                Assembly.compiledAssemblyReferences
                .Union(responseRefs)
                .Union(references)
                .Union(internalAssemblyReferences)
                .Except(RoslynAnalyzerPaths);

            foreach (var reference in allReferences)
            {
                string fullReference = Path.IsPathRooted(reference) ? reference : Path.Combine(ProjectDirectory, reference);
                AppendReference(fullReference, projectBuilder);
            }

            if (0 < Assembly.assemblyReferences.Length)
            {
                projectBuilder.Append("  </ItemGroup>").Append(k_WindowsNewline);
                projectBuilder.Append("  <ItemGroup>").Append(k_WindowsNewline);
                foreach (Assembly reference in Assembly.assemblyReferences.Where(i => i.sourceFiles.Any(tmpThis.ShouldFileBePartOfSolution)))
                {
                    projectBuilder.Append("    <ProjectReference Include=\"").Append(reference.name).Append(".csproj\">").Append(k_WindowsNewline);
                    projectBuilder.Append("      <Project>{").Append(ProjectGuid(reference.name)).Append("}</Project>").Append(k_WindowsNewline);
                    projectBuilder.Append("      <Name>").Append(reference.name).Append("</Name>").Append(k_WindowsNewline);
                    projectBuilder.Append("      <ReferenceOutputAssembly>false</ReferenceOutputAssembly>").Append(k_WindowsNewline);
                    projectBuilder.Append("    </ProjectReference>").Append(k_WindowsNewline);
                }
            }

            projectBuilder.Append(ProjectFooter());
            return projectBuilder.ToString();
        }

        static ILookup<string, string> GetOtherArgumentsFromResponseFilesData(List<ResponseFileData> responseFilesData)
        {
            var paths = responseFilesData.SelectMany(x =>
                {
                    return x.OtherArguments.Where(a =>
                            a.StartsWith("/", StringComparison.Ordinal)
                            || a.StartsWith("-", StringComparison.Ordinal))
                        .Select(b =>
                        {
                            var index = b.IndexOf(':');
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

        static string GenerateAnalyserItemGroup(string[] paths)
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

        void ProjectHeader(StringBuilder builder)
        {
            var otherArguments = GetOtherArgumentsFromResponseFilesData(ResponseFilesData);
            GetProjectHeaderTemplate(
                builder,
                ProjectGuid(Assembly.name),
                Assembly.name,
                string.Join(";", new[] { "DEBUG", "TRACE" }.Concat(Assembly.defines).Concat(ResponseFilesData.SelectMany(x => x.Defines)).Concat(ActiveScriptCompilationDefines).Distinct().ToArray()),
                Assembly.compilerOptions.AllowUnsafeCode | ResponseFilesData.Any(x => x.Unsafe),
                GenerateAnalyserItemGroup(otherArguments["analyzer"].Concat(otherArguments["a"])
                    .SelectMany(x => x.Split(';'))
                    .Concat(RoslynAnalyzerPaths)
                    .Distinct()
                    .ToArray()));
        }

        void GetProjectHeaderTemplate(
            StringBuilder builder,
            string assemblyGUID,
            string assemblyName,
            string defines,
            bool allowUnsafe,
            string analyzerBlock
        )
        {
            builder.Append(@"<?xml version=""1.0"" encoding=""utf-8""?>").Append(k_WindowsNewline);
            builder.Append(@"<Project ToolsVersion=""").Append(k_ToolsVersion).Append(@""" DefaultTargets=""Build"" xmlns=""").Append(k_MSBuildNamespaceUri).Append(@""">").Append(k_WindowsNewline);
            builder.Append(@"  <PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"    <LangVersion>").Append(k_TargetLanguageVersion).Append("</LangVersion>").Append(k_WindowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"  <PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"    <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>").Append(k_WindowsNewline);
            builder.Append(@"    <Platform Condition="" '$(Platform)' == '' "">AnyCPU</Platform>").Append(k_WindowsNewline);
            builder.Append(@"    <ProductVersion>").Append(k_ProductVersion).Append("</ProductVersion>").Append(k_WindowsNewline);
            builder.Append(@"    <SchemaVersion>2.0</SchemaVersion>").Append(k_WindowsNewline);
            builder.Append(@"    <RootNamespace>").Append(ProjectGenerationRootNamespace).Append("</RootNamespace>").Append(k_WindowsNewline);
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
            builder.Append(@"  <PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"    <NoConfig>true</NoConfig>").Append(k_WindowsNewline);
            builder.Append(@"    <NoStdLib>true</NoStdLib>").Append(k_WindowsNewline);
            builder.Append(@"    <AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences>").Append(k_WindowsNewline);
            builder.Append(@"    <ImplicitlyExpandNETStandardFacades>false</ImplicitlyExpandNETStandardFacades>").Append(k_WindowsNewline);
            builder.Append(@"    <ImplicitlyExpandDesignTimeFacades>false</ImplicitlyExpandDesignTimeFacades>").Append(k_WindowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(analyzerBlock);
            builder.Append(@"  <ItemGroup>").Append(k_WindowsNewline);
        }

        static string GetProjectFooterTemplate()
        {
            return string.Join("\r\n", @"  </ItemGroup>", @"  <Import Project=""$(MSBuildToolsPath)\Microsoft.CSharp.targets"" />", @"  <!-- To modify your build process, add your task inside one of the targets below and uncomment it.", @"       Other similar extension points exist, see Microsoft.Common.targets.", @"  <Target Name=""BeforeBuild"">", @"  </Target>", @"  <Target Name=""AfterBuild"">", @"  </Target>", @"  -->", @"</Project>", @"");
        }

        static string ProjectFooter()
        {
            return GetProjectFooterTemplate();
        }

        void SyncProjectFileIfNotChanged(string path, string newContents)
        {
            SyncFileIfNotChanged(path, newContents);
        }

        void SyncFileIfNotChanged(string filename, string newContents)
        {
            if (FileIOProvider.Exists(filename))
            {
                var currentContents = FileIOProvider.ReadAllText(filename);

                if (currentContents == newContents)
                {
                    return;
                }
            }

            FileIOProvider.WriteAllText(filename, newContents);
        }
    }
}