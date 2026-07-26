using System;
using System.IO;

namespace SCG.UnityAssetPublisherTools.Tests
{
    /// <summary>
    /// Provides temporary filesystem locations for UPM editor tests.
    /// </summary>
    internal static class UpmTestPaths
    {
        /// <summary>
        /// Creates an isolated temporary test directory under the Unity project Temp folder.
        /// </summary>
        /// <returns>Absolute path to the created directory.</returns>
        public static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(
                Upm.UpmPathUtility.ProjectRootAbs,
                Upm.UpmConstants.TempFolderName,
                "SCG-UnityAssetPublisherTools-Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Deletes a temporary test directory when it still exists.
        /// </summary>
        /// <param name="path">Absolute temporary directory path.</param>
        public static void DeleteDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }
}
