namespace VSCodeEditor
{
    public static class Utility
    {
        public static string FileNameWithoutExtension(string path)
        {
            var indexOfDot = -1;
            var indexOfSlash = -1;
            for (var i = path.Length - 1; i >= 0; i--)
            {
                if (indexOfDot == -1 && path[i] == '.')
                {
                    indexOfDot = i;
                }
                if (indexOfSlash == -1 && path[i] == '/' || path[i] == '\\')
                {
                    indexOfSlash = i + 1;
                }
                if (indexOfDot != -1 && indexOfSlash != -1)
                {
                    break;
                }
            }
            if (indexOfDot == -1)
            {
                indexOfDot = path.Length - 1;
            }
            if (indexOfSlash == -1)
            {
                indexOfSlash = 0;
            }
            return path.Substring(indexOfSlash, indexOfDot - indexOfSlash);

        }
    }
}