using System;
using System.IO;
using System.Linq;
using Moq;
using UnityEditor.Compilation;

namespace VSCodeEditor.Tests
{
    class SynchronizerBuilder
    {
        class BuilderError : Exception
        {
            public BuilderError(string message)
                : base(message) { }
        }

        IGenerator m_Generator;
        Mock<IAssemblyNameProvider> m_AssemblyProvider = new Mock<IAssemblyNameProvider>();
        public static readonly string projectDirectory = "/FullPath/Example".NormalizePath();

        MockFileIO m_FileIoMock = new MockFileIO();
        Mock<IGUIDGenerator> m_GUIDGenerator = new Mock<IGUIDGenerator>();

        public string ReadFile(string fileName) => m_FileIoMock.ReadAllText(fileName);
        public static string ProjectFilePath(Assembly assembly) => Path.Combine(projectDirectory, $"{assembly.name}.csproj");
        public string ReadProjectFile(Assembly assembly) => ReadFile(ProjectFilePath(assembly));
        public bool FileExists(string fileName) => m_FileIoMock.Exists(fileName);
        public void DeleteFile(string fileName) => m_FileIoMock.DeleteFile(fileName);
        public int WriteTimes => m_FileIoMock.WriteTimes;
        public int ReadTimes => m_FileIoMock.ReadTimes;

        public Assembly Assembly
        {
            get
            {
                if (m_Assemblies.Length > 0)
                {
                    return m_Assemblies[0];
                }

                throw new BuilderError("An empty list of assemblies has been populated, and then the first assembly was requested.");
            }
        }

        Assembly[] m_Assemblies;

        public SynchronizerBuilder()
        {
            WithAssemblyData();
        }

        public IGenerator Build()
        {
            return m_Generator = new ProjectGeneration(projectDirectory, m_AssemblyProvider.Object, m_FileIoMock, m_GUIDGenerator.Object);
        }

        public SynchronizerBuilder WithSolutionText(string solutionText)
        {
            if (m_Generator == null)
            {
                throw new BuilderError("You need to call Build() before calling this method.");
            }

            m_FileIoMock.WriteAllText(m_Generator.SolutionFile(), solutionText);
            return this;
        }

        public SynchronizerBuilder WithSolutionGuid(string solutionGuid)
        {
            m_GUIDGenerator.Setup(x => x.SolutionGuid(Path.GetFileName(projectDirectory), "cs")).Returns(solutionGuid);
            return this;
        }

        public SynchronizerBuilder WithProjectGuid(string projectGuid, Assembly assembly)
        {
            m_GUIDGenerator.Setup(x => x.ProjectGuid(Path.GetFileName(projectDirectory), assembly.name)).Returns(projectGuid);
            return this;
        }

        public SynchronizerBuilder WithAssemblies(Assembly[] assemblies)
        {
            m_Assemblies = assemblies;
            m_AssemblyProvider.Setup(x => x.GetAssemblies(It.IsAny<Func<string, bool>>())).Returns(m_Assemblies);
            return this;
        }

        public SynchronizerBuilder WithAssemblyData(string[] files = null, string[] defines = null, Assembly[] assemblyReferences = null, string[] compiledAssemblyReferences = null, bool unsafeSettings = false)
        {
            var assembly = new Assembly(
                "Test",
                "some/path/file.dll",
                files ?? new[] { "test.cs" },
                defines ?? new string[0],
                assemblyReferences ?? new Assembly[0],
                compiledAssemblyReferences ?? new string[0],
                AssemblyFlags.None);
            assembly.compilerOptions.AllowUnsafeCode = unsafeSettings;
            return WithAssembly(assembly);
        }

        public SynchronizerBuilder WithAssembly(Assembly assembly)
        {
            AssignFilesToAssembly(assembly.sourceFiles, assembly);
            return WithAssemblies(new[] { assembly });
        }

        public SynchronizerBuilder WithAssetFiles(string[] files)
        {
            m_AssemblyProvider.Setup(x => x.GetAllAssetPaths()).Returns(files);
            return this;
        }

        public SynchronizerBuilder AssignFilesToAssembly(string[] files, Assembly assembly)
        {
            m_AssemblyProvider.Setup(x => x.GetAssemblyNameFromScriptPath(It.Is<string>(file => files.Contains(file)))).Returns(assembly.name);
            return this;
        }

        public SynchronizerBuilder WithResponseFileData(Assembly assembly, string responseFile, string[] defines = null, string[] errors = null, string[] fullPathReferences = null, string[] otherArguments = null, bool _unsafe = false)
        {
            assembly.compilerOptions.ResponseFiles = new[] { responseFile };
            m_AssemblyProvider.Setup(x => x.ParseResponseFile(responseFile, projectDirectory, It.IsAny<string[]>())).Returns(new ResponseFileData
            {
                Defines = defines ?? new string[0],
                Errors = errors ?? new string[0],
                FullPathReferences = fullPathReferences ?? new string[0],
                OtherArguments = otherArguments ?? new string[0],
                Unsafe = _unsafe,
            });
            return this;
        }

        public SynchronizerBuilder WithPackageInfo(string assetPath)
        {
            m_AssemblyProvider.Setup(x => x.FindForAssetPath(assetPath)).Returns(default(UnityEditor.PackageManager.PackageInfo));
            return this;
        }

        public SynchronizerBuilder WithPackageAsset(string assetPath, bool isInternalPackageAsset)
        {
            m_AssemblyProvider.Setup(x => x.IsInternalizedPackagePath(assetPath)).Returns(isInternalPackageAsset);
            return this;
        }

        public SynchronizerBuilder WithUserSupportedExtensions(string[] extensions)
        {
            m_AssemblyProvider.Setup(x => x.ProjectSupportedExtensions).Returns(extensions);
            return this;
        }

#if UNITY_2020_2_OR_NEWER
        public SynchronizerBuilder WithRoslynAnalyzerRulesetPath(string roslynAnalyzerRuleSetPath)
        {
            foreach (var assembly in m_Assemblies)
            {
                assembly.compilerOptions.RoslynAnalyzerRulesetPath = roslynAnalyzerRuleSetPath;
            }
            return this;
        }

        public SynchronizerBuilder WithRoslynAnalyzers(string[] roslynAnalyzerDllPaths)
        {
#if !ROSLYN_ANALYZER_FIX
            m_AssemblyProvider.Setup(x => x.GetRoslynAnalyzerPaths()).Returns(roslynAnalyzerDllPaths);
#else
            foreach (var assembly in m_Assemblies)
            {
                assembly.compilerOptions.RoslynAnalyzerDllPaths = roslynAnalyzerDllPaths;
            }
#endif
            return this;
        }
#endif
    }
}
