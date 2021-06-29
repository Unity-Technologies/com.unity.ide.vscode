using System.IO;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.VSCode.EditorTests")]

namespace VSCodeEditor.Tests
{
    internal static class StringUtils
    {
        public static string NormalizePath(this string path)
        {
            return path.Replace(Path.DirectorySeparatorChar == '\\' ? '/' : '\\', Path.DirectorySeparatorChar);
        }
    }
}