using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Naninovel
{
    public static class PathUtils
    {
        /// <summary>
        /// Replaces back slashes with forward slashes.
        /// </summary>
        public static string FormatPath (string path)
        {
            return path.Replace('\\', '/');
        }

        /// <summary>
        /// Modifies the collection replacing back slashes with forward slashes.
        /// </summary>
        public static T FormatPaths<T> (T paths) where T : IList<string>
        {
            for (int i = 0; i < paths.Count; i++)
                paths[i] = FormatPath(paths[i]);
            return paths;
        }

        /// <summary>
        /// Given an absolute path inside current Unity project (eg, 'C:\UnityProject\Assets\FooAsset.asset'),
        /// transforms it to a relative project asset path (eg, 'Assets/FooAsset.asset'); returns null when
        /// specified path is not inside current Unity project (doesn't start with <see cref="Application.dataPath"/>).
        /// </summary>
        public static string AbsoluteToAssetPath (string absolutePath)
        {
            absolutePath = FormatPath(absolutePath);
            if (!absolutePath.StartsWithFast(Application.dataPath)) return null;
            return absolutePath.Replace(Application.dataPath, "Assets");
        }

        /// <summary>
        /// Invokes <see cref="Path.Combine(string[])"/> and replaces back slashes with forward slashes on the result.
        /// </summary>
        public static string Combine (params string[] paths)
        {
            return FormatPath(Path.Combine(paths));
        }
    }
}
