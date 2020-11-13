using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;

namespace VSCodeEditor
{
    public static class ProjectUtility
    {
        public static string ProjectFile(this string assemblyName, string projectDirectory)
        {
            var fileBuilder = new StringBuilder(assemblyName);
            fileBuilder.Append(".csproj");
            return Path.Combine(projectDirectory, fileBuilder.ToString());
        }

        public static string NormalizePath(this string path)
        {
            if (Path.DirectorySeparatorChar == '\\')
                return path.Replace('/', Path.DirectorySeparatorChar);
            return path.Replace('\\', Path.DirectorySeparatorChar);
        }
    }
}