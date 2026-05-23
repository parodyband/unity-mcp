using System;
using System.IO;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class UnityMcpPathUtility
    {
        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string PackageRoot => Path.Combine(ProjectRoot, "Packages");

        public static string NormalizeProjectRelativePath(string path)
        {
            path = (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
            if (path.StartsWith("./", StringComparison.Ordinal))
            {
                path = path.Substring(2);
            }

            return path;
        }

        public static string ResolveReadableProjectPath(string path)
        {
            var relative = ResolveAssetOrPackageRelativePath(path);
            var fullPath = Path.GetFullPath(Path.Combine(ProjectRoot, relative));
            if (Directory.Exists(fullPath))
            {
                throw new ArgumentException("Path points to a directory.");
            }

            return fullPath;
        }

        public static string ResolveWritableProjectPath(string path, bool allowDirectory)
        {
            var relative = ResolveAssetOrPackageRelativePath(path);
            var fullPath = Path.GetFullPath(Path.Combine(ProjectRoot, relative));
            if (!allowDirectory && Directory.Exists(fullPath))
            {
                throw new ArgumentException("Path points to a directory.");
            }

            return fullPath;
        }

        public static string ResolveAssetOrPackageRelativePath(string path)
        {
            var relative = NormalizeProjectRelativePath(path);
            var fullPath = Path.GetFullPath(Path.Combine(ProjectRoot, relative));
            var assetsRoot = Path.GetFullPath(Application.dataPath);
            var packagesRoot = Path.GetFullPath(PackageRoot);

            if (!IsSameOrChildPath(fullPath, assetsRoot) && !IsSameOrChildPath(fullPath, packagesRoot))
            {
                throw new ArgumentException("Path must resolve under Assets or Packages.");
            }

            return MakeProjectRelative(fullPath);
        }

        public static bool IsSameOrChildPath(string path, string root)
        {
            path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static string MakeProjectRelative(string fullPath)
        {
            var root = Path.GetFullPath(ProjectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalized = Path.GetFullPath(fullPath);
            if (!IsSameOrChildPath(normalized, root))
            {
                throw new ArgumentException("Path escapes the Unity project root.");
            }

            return normalized.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
        }
    }
}

