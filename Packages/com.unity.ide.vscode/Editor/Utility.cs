using System;
using System.Runtime.CompilerServices;

namespace com.unity.ide.vscode
{
    static class Utility
    {
        public static bool HasFileExtension(string path, string extension)
        {
            GetExtension(path, out var start);
            var extLen = extension.Length;
            if (path.Length - start != extLen) return false;
            return string.Compare(path, start, extension, 0, extLen, StringComparison.OrdinalIgnoreCase) == 0;
        }

        /// <summary>
        /// Given a path, determine the start index of the file extension ('.'
        /// included) if any. If the path has no file extension, <c>start</c>
        /// will be equal to the length of <c>path</c> (the empty string).
        /// </summary>
        public static void GetExtension(string path, out int start)
        {
            var len = path.Length;
            int dot = len - 1;
            while (dot >= 0)
            {
                char c = path[dot];
                if (c == '/' || c == '\\')
                {
                    start = len;
                    return;
                }

                if (c == '.')
                {
                    start = dot;
                    return;
                }

                --dot;
            }

            start = len;
        }

        /// <summary>
        /// Given a path, determine the start and end indices of the filename
        /// without path nor file extension. The base filename may be retrieved
        /// using <c>path.Substring(start, end - start)</c> (but code should of
        /// course avoid allocating new strings like this when possible).
        /// </summary>
        public static void GetFileNameWithoutExtension(string path, out int start, out int end)
        {
            int dot = path.Length;
            int slash = dot - 1;
            while (slash >= 0)
            {
                char c = path[slash];
                if (c == '/' || c == '\\')
                {
                    break;
                }

                if (c == '.')
                {
                    dot = slash;
                    while (--slash >= 0)
                    {
                        c = path[slash];
                        if (c == '/' || c == '\\') break;
                    }

                    break;
                }

                --slash;
            }

            start = slash + 1;
            end = dot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPathRooted(string path)
        {
            var len = path.Length;
            if (len == 0) return false;
            var c = path[0];
            return c == '/' || c == '\\' || (len > 1 && path[1] == ':');
        }
    }
}
