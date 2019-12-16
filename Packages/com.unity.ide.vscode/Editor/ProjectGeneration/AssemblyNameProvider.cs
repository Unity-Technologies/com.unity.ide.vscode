using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;

namespace VSCodeEditor
{
    public interface IAssemblyNameProvider
    {
        string GetAssemblyNameFromScriptPath(string path);
        IEnumerable<Assembly> GetAssemblies();
        IEnumerable<string> GetAllAssetPaths();
        IEnumerable<Assembly> RelevantAssemblies(IEnumerable<Assembly> assemblies);
        UnityEditor.PackageManager.PackageInfo FindForAssetPath(string assetPath);
        ResponseFileData ParseResponseFile(string responseFilePath, string projectDirectory, string[] systemReferenceDirectories);
        bool ShouldFileBePartOfSolution(string file);
        bool ShouldSyncOnReimportedAsset(string file);
        string GetExtensionOfSourceFiles(string[] islandSourceFiles);
    }

    internal class AssemblyNameProvider : IAssemblyNameProvider
    {
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

        static readonly string[] k_ReimportSyncExtensions = { ".dll", ".asmdef" };

        bool m_ShouldGenerateAll;
        string[] m_ProjectSupportedExtensions = new string[0];

        public string GetAssemblyNameFromScriptPath(string path)
        {
            return CompilationPipeline.GetAssemblyNameFromScriptPath(path);
        }

        public IEnumerable<Assembly> GetAssemblies()
        {
            // Only synchronize assemblies that have associated source files and ones that we actually want in the project.
            // This also filters out DLLs coming from .asmdef files in packages.
            return CompilationPipeline.GetAssemblies()
                .Where(i => 0 < i.sourceFiles.Length && i.sourceFiles.Any(ShouldFileBePartOfSolution));
        }

        public IEnumerable<string> GetAllAssetPaths()
        {
            return AssetDatabase.GetAllAssetPaths().Where(asset => ShouldFileBePartOfSolution(asset) && ScriptingLanguageFor(GetExtensionOfSourceFile(asset)) == ScriptingLanguage.None);
        }

        public UnityEditor.PackageManager.PackageInfo FindForAssetPath(string assetPath)
        {
            return UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
        }

        public ResponseFileData ParseResponseFile(string responseFilePath, string projectDirectory, string[] systemReferenceDirectories)
        {
            return CompilationPipeline.ParseResponseFile(
                responseFilePath,
                projectDirectory,
                systemReferenceDirectories
            );
        }

        public IEnumerable<Assembly> RelevantAssemblies(IEnumerable<Assembly> assemblies)
        {
            IEnumerable<Assembly> relevantIslands = assemblies.Where(i => ScriptingLanguage.CSharp == ScriptingLanguageFor(i));
            return relevantIslands;
        }

        ScriptingLanguage ScriptingLanguageFor(Assembly island)
        {
            return ScriptingLanguageFor(GetExtensionOfSourceFiles(island.sourceFiles));
        }

        public string GetExtensionOfSourceFiles(string[] files)
        {
            return files.Length > 0 ? GetExtensionOfSourceFile(files[0]) : "NA";
        }

        static ScriptingLanguage ScriptingLanguageFor(string extension)
        {
            return k_BuiltinSupportedExtensions.TryGetValue(extension.TrimStart('.'), out var result)
                ? result
                : ScriptingLanguage.None;
        }

        public bool ShouldSyncOnReimportedAsset(string asset)
        {
            return k_ReimportSyncExtensions.Contains(new FileInfo(asset).Extension);
        }

        static string GetExtensionOfSourceFile(string file)
        {
            var ext = Path.GetExtension(file).ToLower();
            ext = ext.Substring(1); //strip dot
            return ext;
        }

        public bool ShouldFileBePartOfSolution(string file)
        {
            string extension = Path.GetExtension(file);

            // Exclude files coming from packages except if they are internalized.
            if (!m_ShouldGenerateAll && IsInternalizedPackagePath(file))
            {
                return false;
            }

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

        bool IsInternalizedPackagePath(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                return false;
            }

            var packageInfo = FindForAssetPath(file);
            if (packageInfo == null)
            {
                return false;
            }

            var packageSource = packageInfo.source;
            return packageSource != PackageSource.Embedded && packageSource != PackageSource.Local;
        }

        public void GenerateAll(bool generateAll)
        {
            m_ShouldGenerateAll = generateAll;
        }
    }
}
