using System.Text;

namespace VSCodeEditor
{
    public static class Utility
    {
        public static string FileNameWithoutExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }

            var indexOfDot = -1;
            var indexOfSlash = 0;
            for (var i = path.Length - 1; i >= 0; i--)
            {
                if (indexOfDot == -1 && path[i] == '.')
                {
                    indexOfDot = i;
                }

                if (indexOfSlash == 0 && path[i] == '/' || path[i] == '\\')
                {
                    indexOfSlash = i + 1;
                    break;
                }
            }

            if (indexOfDot == -1)
            {
                indexOfDot = path.Length;
            }

            return path.Substring(indexOfSlash, indexOfDot - indexOfSlash);
        }

        public static void GetFileNameWithoutExtension(string path, out int start, out int end)
        {
            int extsep = path.Length;
            int dirsep = extsep - 1;
            while (dirsep >= 0) {
                char c = path[dirsep];
                if (c == '/' || c == '\\') { // TODO: not guaranteed to be normalized?
                    break;
                }
                if (c == '.') {
                    extsep = dirsep;
                    while (dirsep >= 0) {
                        c = path[dirsep];
                        if (c == '/' || c == '\\') { // TODO: not guaranteed to be normalized?
                            break;
                        }
                        --dirsep;
                    }
                    break;
                }
                --dirsep;
            }
            start = dirsep + 1;
            end = extsep;
        }

        public static void AppendFileNameWithoutExtension(StringBuilder dest, string path)
        {
            if (path == null) return; // TODO: not guaranteed to be non-null?

            GetFileNameWithoutExtension(path, out var start, out var end);
            dest.Append(path, start, end - start);
        }
    }
}