using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine.TestTools;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace SCG.UnityAssetPublisherTools.Tests
{
    /// <summary>
    /// Verifies Unity Package Manager discovers embedded packages without manifest self-references.
    /// </summary>
    public sealed class EmbeddedPackageDiscoveryTests
    {
        private const string PackageId = "com.scg.publishertools.embedded-test";
        private const double ResolveTimeoutSeconds = 30;

        private string _packageDirectory;
        private string _manifestBeforeTest;

        /// <summary>
        /// Removes any prior temporary package and captures the current project manifest.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _packageDirectory = Path.Combine(
                Upm.UpmPathUtility.ProjectRootAbs,
                Upm.UpmConstants.PackagesFolderName,
                PackageId);
            DeleteTestPackage();
            _manifestBeforeTest = File.ReadAllText(Path.Combine(
                Upm.UpmPathUtility.ProjectRootAbs,
                Upm.UpmConstants.PackagesFolderName,
                Upm.UpmConstants.ManifestFileName));
        }

        /// <summary>
        /// Removes the temporary package and requests Package Manager resolution.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            DeleteTestPackage();
            Client.Resolve();
        }

        /// <summary>
        /// Verifies a package placed under Packages is registered without modifying manifest.json.
        /// </summary>
        [UnityTest]
        public IEnumerator PackageUnderPackagesDirectory_IsRegisteredWithoutManifestDependency()
        {
            Directory.CreateDirectory(_packageDirectory);
            File.WriteAllText(
                Path.Combine(_packageDirectory, "package.json"),
                "{\"name\":\"com.scg.publishertools.embedded-test\",\"version\":\"1.0.0\",\"displayName\":\"Embedded Test\"}");

            Client.Resolve();
            yield return WaitForRegistration(true);

            Assert.That(File.ReadAllText(Path.Combine(
                Upm.UpmPathUtility.ProjectRootAbs,
                Upm.UpmConstants.PackagesFolderName,
                Upm.UpmConstants.ManifestFileName)), Is.EqualTo(_manifestBeforeTest));
        }

        /// <summary>
        /// Waits until the temporary package registration matches the requested state.
        /// </summary>
        /// <param name="expectedRegistration">Expected Package Manager registration state.</param>
        /// <returns>Enumerator that completes when the registration state matches.</returns>
        private IEnumerator WaitForRegistration(bool expectedRegistration)
        {
            var deadline = EditorApplication.timeSinceStartup + ResolveTimeoutSeconds;
            while (IsRegistered() != expectedRegistration)
            {
                if (EditorApplication.timeSinceStartup >= deadline)
                    Assert.Fail($"Package registration did not become {expectedRegistration} within {ResolveTimeoutSeconds} seconds.");

                yield return null;
            }
        }

        /// <summary>
        /// Checks whether Package Manager currently registers the temporary embedded package.
        /// </summary>
        /// <returns>True when the temporary package is registered.</returns>
        private static bool IsRegistered()
        {
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                if (string.Equals(package.name, PackageId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Deletes the temporary embedded package directory when it exists.
        /// </summary>
        private void DeleteTestPackage()
        {
            if (Directory.Exists(_packageDirectory))
                Directory.Delete(_packageDirectory, true);
        }
    }
}
