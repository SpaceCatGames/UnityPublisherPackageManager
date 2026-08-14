using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SCG.UPPM
{
    /// <summary>
    /// Ensures that every asset under Samples~ has a .meta file with a stable GUID.
    /// The baker temporarily imports assets into Assets/__SamplesMetaBake__ to force Unity
    /// to generate .meta files, then copies those .meta files back next to the originals
    /// under Samples~ so Package Manager can preserve GUIDs on "Import" action.
    /// </summary>
    public static class SamplesMetaBaker
    {
        #region Constants
        private const string SamplesFolder = Constants.SamplesBase;
        private const string ImportRootFolder = "Assets/__SamplesMetaBake__";
        #endregion

        #region Public API

        /// <summary>
        /// Generates .meta files for all assets under "Samples~" if they are missing.
        /// Safe to call repeatedly; existing .meta files are kept intact.
        /// </summary>
        /// <param name="packageRootAbs">
        /// Absolute package root path that contains the "Samples~" folder.
        /// </param>
        public static void Bake(string packageRootAbs)
        {
            if (string.IsNullOrEmpty(packageRootAbs) || !Directory.Exists(packageRootAbs))
                return;

            var samples = Path.Combine(packageRootAbs, SamplesFolder);
            if (!Directory.Exists(samples))
                return;

            var tempImportRootAbs = ToAbsolute(ImportRootFolder);

            EnsureCleanDirectoryAbs(tempImportRootAbs);

            try
            {
                // Copy candidates without .meta into a temp Assets folder to force meta creation.
                var anyCopied = CopyAssetsMissingMetaToTemp(samples, ImportRootFolder);

                if (!anyCopied)
                {
                    // Nothing to bake → remove the empty folder anyway.
                    CleanupTempImportRoot(ImportRootFolder, tempImportRootAbs);
                    return;
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                // Copy generated .meta back to Samples~.
                CopyGeneratedMetasBack(samples, ImportRootFolder);

                // Clean temp (both as Asset and as raw folder).
                CleanupTempImportRoot(ImportRootFolder, tempImportRootAbs);
            }
            finally
            {
                // Best-effort safety net (in case of exceptions/locks).
                if (Directory.Exists(tempImportRootAbs) || AssetDatabase.IsValidFolder(ImportRootFolder))
                    CleanupTempImportRoot(ImportRootFolder, tempImportRootAbs);
            }
        }

        #endregion

        #region Core

        /// <summary>
        /// Copies files from Samples~ which lack a .meta into a temp Assets folder,
        /// preserving relative layout. Skips .meta, .cs, .asmdef etc. – only assets that
        /// actually need stable GUIDs (prefabs, materials, textures, scenes, uxml, etc.).
        /// </summary>
        private static bool CopyAssetsMissingMetaToTemp(string samplesAbs, string tempImportRootRel)
        {
            var any = false;
            foreach (var src in Directory.GetFiles(samplesAbs, "*", SearchOption.AllDirectories))
            {
                if (IsMeta(src) || ShouldSkipByExtension(src)) continue;

                var meta = src + ".meta";
                if (File.Exists(meta)) continue; // already has stable GUID

                var rel = MakeRelative(src, samplesAbs);
                var dstRel = Path.Combine(tempImportRootRel, rel).Replace('\\', '/');
                var dstDirRel = Path.GetDirectoryName(dstRel)?.Replace('\\', '/');

                if (!string.IsNullOrEmpty(dstDirRel) && !AssetDatabase.IsValidFolder(dstDirRel))
                    CreateFoldersRecursively(dstDirRel);

                FileUtil.CopyFileOrDirectory(src, dstRel);
                any = true;
            }
            return any;
        }

        /// <summary>
        /// Copies generated .meta from temp Assets folder back next to the originals under Samples~.
        /// </summary>
        private static void CopyGeneratedMetasBack(string samplesAbs, string tempImportRootRel)
        {
            var tempAbs = ToAbsolute(tempImportRootRel);
            if (!Directory.Exists(tempAbs)) return;

            foreach (var baked in Directory.GetFiles(tempAbs, "*", SearchOption.AllDirectories))
            {
                if (IsMeta(baked)) continue; // we want .meta for each actual asset

                var rel = MakeRelative(baked, tempAbs);
                var dstAssetAbs = Path.Combine(samplesAbs, rel);
                var bakedMeta = baked + ".meta";
                var dstMeta = dstAssetAbs + ".meta";

                if (!File.Exists(bakedMeta)) continue;
                var dstDir = Path.GetDirectoryName(dstMeta);
                if (!string.IsNullOrEmpty(dstDir) && !Directory.Exists(dstDir))
                    Directory.CreateDirectory(dstDir);

                // Do not overwrite an existing meta – it already pins a GUID.
                if (!File.Exists(dstMeta))
                    FileUtil.CopyFileOrDirectory(bakedMeta, dstMeta);
            }
        }

        #endregion

        #region Utils

        /// <summary>
        /// Creates nested folders under Assets/ path (AssetDatabase API requires step-by-step creation).
        /// </summary>
        private static void CreateFoldersRecursively(string assetsRelPath)
        {
            var parts = assetsRelPath.Split('/');
            var path = parts[0]; // should start with "Assets"
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{path}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(path, parts[i]);
                path = next;
            }
        }

        private static bool IsMeta(string path) => path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);

        private static bool ShouldSkipByExtension(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            // Scripts and assembly defs do not need pinned metas for sample import
            return ext is ".cs" or ".asmdef" or ".asmref";
        }

        private static string MakeRelative(string fullPath, string rootAbs)
        {
            fullPath = Path.GetFullPath(fullPath).Replace('\\', '/');
            rootAbs = Path.GetFullPath(rootAbs).Replace('\\', '/').TrimEnd('/');
            return fullPath.StartsWith(rootAbs + "/") ? fullPath[(rootAbs.Length + 1)..] : Path.GetFileName(fullPath);
        }

        private static string ToAbsolute(string projectRelative)
        {
            if (Path.IsPathRooted(projectRelative)) return projectRelative;
            var proj = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.GetFullPath(Path.Combine(proj, projectRelative));
        }

        private static void EnsureCleanDirectoryAbs(string abs)
        {
            if (Directory.Exists(abs)) Directory.Delete(abs, true);
            Directory.CreateDirectory(abs);
        }

        #endregion

        #region Cleanup Helper

        /// <summary>
        /// Removes the temporary import root both from the AssetDatabase and from the filesystem.
        /// Tries immediate deletion; if still present (e.g., file locks on Windows), retries in delayCall.
        /// </summary>
        /// <param name="tempImportRootRel">Project-relative path like "Assets/__SamplesMetaBake__".</param>
        /// <param name="tempImportRootAbs">Absolute path to the same folder.</param>
        private static void CleanupTempImportRoot(string tempImportRootRel, string tempImportRootAbs)
        {
            // Try as an Asset first (works when Unity created a .meta for the folder).
            if (AssetDatabase.IsValidFolder(tempImportRootRel))
                AssetDatabase.DeleteAsset(tempImportRootRel);

            // Always attempt raw filesystem removal (covers cases with no .meta).
            TryDeleteDirectory(tempImportRootAbs);

            // Delete leftover .meta by absolute path.
            var metaAbs = tempImportRootAbs.TrimEnd('\\', '/') + ".meta";
            if (File.Exists(metaAbs))
            {
                TryClearReadOnly(metaAbs);
                File.Delete(metaAbs);
            }

            AssetDatabase.Refresh();

            // If still exists (Windows locks), schedule one more attempt.
            if (Directory.Exists(tempImportRootAbs) || AssetDatabase.IsValidFolder(tempImportRootRel))
            {
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        if (AssetDatabase.IsValidFolder(tempImportRootRel))
                            AssetDatabase.DeleteAsset(tempImportRootRel);

                        TryDeleteDirectory(tempImportRootAbs);

                        var meta = tempImportRootAbs.TrimEnd('\\', '/') + ".meta";
                        if (File.Exists(meta))
                        {
                            TryClearReadOnly(meta);
                            File.Delete(meta);
                        }

                        AssetDatabase.Refresh();
                    }
                    catch (Exception) { /* ignore */ }
                };
            }

            return;

            static void TryDeleteDirectory(string abs)
            {
                if (!Directory.Exists(abs)) return;
                try
                {
                    // Clear read-only flags just in case (Windows).
                    foreach (var f in Directory.GetFiles(abs, "*", SearchOption.AllDirectories))
                        TryClearReadOnly(f);
                    Directory.Delete(abs, true);
                }
                catch (IOException) { /* will retry in delayCall */ }
                catch (UnauthorizedAccessException) { /* will retry in delayCall */ }
            }

            static void TryClearReadOnly(string filePath)
            {
                try
                {
                    var attr = File.GetAttributes(filePath);
                    if ((attr & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(filePath, attr & ~FileAttributes.ReadOnly);
                }
                catch { /* ignore */ }
            }
        }

        #endregion
    }
}
