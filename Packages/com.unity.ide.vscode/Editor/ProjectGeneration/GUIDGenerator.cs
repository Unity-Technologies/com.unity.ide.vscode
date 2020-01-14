namespace com.unity.ide.vscode
{
    interface IGUIDGenerator
    {
        string ProjectGuid(string projectName, string assemblyName);
        string SolutionGuid(string projectName, string extension);
    }

    class GUIDGenerator : IGUIDGenerator
    {
        public string ProjectGuid(string projectName, string assemblyName)
        {
            return SolutionGuidGenerator.GuidForProject(projectName + assemblyName);
        }

        public string SolutionGuid(string projectName, string extension)
        {
            return SolutionGuidGenerator.GuidForSolution(projectName, extension); // GetExtensionOfSourceFiles(assembly.sourceFiles)
        }
    }
}
