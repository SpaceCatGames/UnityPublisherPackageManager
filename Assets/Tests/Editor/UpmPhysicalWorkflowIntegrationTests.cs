using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SCG.UPPM.Upm;
using UnityEditor;
using UnityEngine;

namespace SCG.UPPM.Tests
{
    /// <summary>
    /// Exercises physical project and package moves against real Assets and Packages directories.
    /// </summary>
    public sealed class UpmPhysicalWorkflowIntegrationTests
    {
        private const string AssetRoot = "Assets/TestPackage";
        private const string PackageRoot = "Packages/scg.uppm.integration-test";
        private const string LogPrefix = "[UPPM Physical Test]";

        /// <summary>
        /// Physically verifies Samples and Documentation visibility plus a package move and return without Unity warnings.
        /// </summary>
        [Test]
        [Explicit("Creates and imports a physical embedded package under Assets/TestPackage and Packages.")]
        public void PhysicalPackageMoveAndReturn_PreservesFilesAndProducesNoWarnings()
        {
            LogStage("Started. Unity may pause briefly during synchronous asset imports.");
            var assetRootAbs = UpmPathUtility.ToAbsolute(AssetRoot);
            var packageRootAbs = UpmPathUtility.ToAbsolute(PackageRoot);
            Assert.That(Directory.Exists(assetRootAbs), Is.False, $"Reserved integration path already exists: {assetRootAbs}");
            Assert.That(Directory.Exists(packageRootAbs), Is.False, $"Reserved integration path already exists: {packageRootAbs}");

            var storageRoot = UpmTestPaths.CreateTemporaryDirectory();
            var metaStorage = new SamplesFolderMetaStorage(storageRoot);
            var warnings = new List<string>();
            Application.logMessageReceived += CaptureWarning;

            try
            {
                LogStage("Creating Assets/TestPackage and importing it.");
                CreateVisibleTestPackage(assetRootAbs);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                var samplesMeta = File.ReadAllBytes(Path.Combine(assetRootAbs, "Samples.meta"));
                var documentationMeta = File.ReadAllBytes(Path.Combine(assetRootAbs, "Documentation.meta"));
                LogStage("Hiding Samples and Documentation.");
                SetSamplesVisibility(assetRootAbs, visible: false, metaStorage);

                Assert.That(Directory.Exists(Path.Combine(assetRootAbs, "Samples~")), Is.True);
                Assert.That(Directory.Exists(Path.Combine(assetRootAbs, "Documentation~")), Is.True);
                Assert.That(File.Exists(Path.Combine(assetRootAbs, "Samples~.meta")), Is.False);
                Assert.That(File.Exists(Path.Combine(assetRootAbs, "Documentation~.meta")), Is.False);

                AssetDatabase.StartAssetEditing();
                try
                {
                    LogStage("Moving the physical package from Assets to Packages.");
                    UpmFileOperations.MoveFolderWithMeta(assetRootAbs, packageRootAbs);
                    Assert.That(Directory.Exists(packageRootAbs), Is.True);
                    Assert.That(File.Exists(Path.Combine(packageRootAbs, "package.json")), Is.True);
                    LogStage("Returning the physical package from Packages to Assets.");
                    UpmFileOperations.MoveFolderWithMeta(packageRootAbs, assetRootAbs);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                LogStage("Importing the returned package.");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Assert.That(Directory.Exists(assetRootAbs), Is.True);
                Assert.That(Directory.Exists(packageRootAbs), Is.False);

                LogStage("Restoring Samples and Documentation and verifying their GUIDs.");
                SetSamplesVisibility(assetRootAbs, visible: true, metaStorage);
                Assert.That(File.ReadAllBytes(Path.Combine(assetRootAbs, "Samples.meta")), Is.EqualTo(samplesMeta));
                Assert.That(File.ReadAllBytes(Path.Combine(assetRootAbs, "Documentation.meta")), Is.EqualTo(documentationMeta));
            }
            finally
            {
                LogStage("Cleaning up physical test files.");
                Application.logMessageReceived -= CaptureWarning;
                DeleteTestPath(assetRootAbs);
                DeleteTestPath(packageRootAbs);
                UpmTestPaths.DeleteDirectory(storageRoot);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            Assert.That(warnings, Is.Empty, string.Join(Environment.NewLine, warnings));
            LogStage("Completed successfully.");
            return;

            void CaptureWarning(string message, string _, LogType type)
            {
                if (type == LogType.Warning)
                    warnings.Add(message);
            }
        }

        private static void LogStage(string message) => Debug.Log($"{LogPrefix} {message}");

        private static void CreateVisibleTestPackage(string rootPath)
        {
            Directory.CreateDirectory(Path.Combine(rootPath, "Samples"));
            Directory.CreateDirectory(Path.Combine(rootPath, "Documentation"));
            File.WriteAllText(Path.Combine(rootPath, "Samples", "sample.txt"), "sample");
            File.WriteAllText(Path.Combine(rootPath, "Documentation", "documentation.md"), "documentation");
            File.WriteAllText(
                Path.Combine(rootPath, "package.json"),
                "{\"name\":\"scg.uppm.integration-test\",\"version\":\"1.0.0\"}");
        }

        private static void SetSamplesVisibility(string rootPath, bool visible, SamplesFolderMetaStorage storage)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                MovePair(rootPath, Constants.SamplesBase, Constants.SamplesRenamed, visible, storage);
                MovePair(rootPath, Constants.DocumentationBase, Constants.DocumentationRenamed, visible, storage);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            SamplesRenamer.RemoveHiddenFolderMetaFiles(rootPath, storage);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void MovePair(
            string rootPath,
            string hiddenName,
            string visibleName,
            bool visible,
            SamplesFolderMetaStorage storage)
        {
            var source = Path.Combine(rootPath, visible ? hiddenName : visibleName);
            var destination = Path.Combine(rootPath, visible ? visibleName : hiddenName);
            SamplesRenamer.MoveFolder(source, destination, rootPath, hiddenName, visible, storage);
        }

        private static void DeleteTestPath(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            if (File.Exists(path + ".meta"))
                File.Delete(path + ".meta");
        }
    }
}
