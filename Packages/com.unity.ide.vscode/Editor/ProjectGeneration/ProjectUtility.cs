using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;

namespace VSCodeEditor
{
    public class ProjectUtility
    {
        public static string ProjectFile(string assemblyName, string projectDirectory)
        {
            var fileBuilder = new StringBuilder(assemblyName);
            fileBuilder.Append(".csproj");
            return Path.Combine(projectDirectory, fileBuilder.ToString());
        }
    }
}