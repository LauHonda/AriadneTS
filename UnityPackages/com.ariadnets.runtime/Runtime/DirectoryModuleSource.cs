using System;
using System.IO;

namespace AriadneTS.Runtime
{
    public sealed class DirectoryModuleSource
    {
        private readonly string rootPath;
        private readonly StringComparison pathComparison;

        public DirectoryModuleSource(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("Module root path is required.", nameof(rootPath));
            }

            this.rootPath = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
            pathComparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        public string Load(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                return null;
            }

            var relativePath = moduleName.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
            if (!fullPath.StartsWith(rootPath, pathComparison) || !File.Exists(fullPath))
            {
                return null;
            }

            return File.ReadAllText(fullPath);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }
}

