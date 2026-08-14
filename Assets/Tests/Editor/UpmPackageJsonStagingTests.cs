using System.IO;
using NUnit.Framework;
using SCG.UPPM.Upm;

namespace SCG.UPPM.Tests
{
    /// <summary>
    /// Verifies package manifest selection and staging behavior.
    /// </summary>
    public sealed class UpmPackageJsonStagingTests
    {
        private string _testDirectory;

        /// <summary>
        /// Creates an isolated temporary directory before every test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _testDirectory = UpmTestPaths.CreateTemporaryDirectory();
        }

        /// <summary>
        /// Deletes the temporary directory created for the current test.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            UpmTestPaths.DeleteDirectory(_testDirectory);
        }

        /// <summary>
        /// Verifies that a free package manifest takes precedence over package.json.
        /// </summary>
        [Test]
        public void GetEffectivePackageId_PrefersFreePackageManifest()
        {
            File.WriteAllText(Path.Combine(_testDirectory, UpmConstants.PackageJsonFileName), "{\"name\":\"normal.package\"}");
            File.WriteAllText(Path.Combine(_testDirectory, UpmConstants.FreePackageJsonFileName), "{\"name\":\"free.package\"}");

            var packageId = UpmPackageJsonStaging.GetEffectivePackageId(null, _testDirectory);

            Assert.That(packageId, Is.EqualTo("free.package"));
        }

        /// <summary>
        /// Verifies that package.json supplies the package id when no free manifest exists.
        /// </summary>
        [Test]
        public void GetEffectivePackageId_UsesPackageManifestWhenFreeManifestIsMissing()
        {
            File.WriteAllText(Path.Combine(_testDirectory, UpmConstants.PackageJsonFileName), "{\"name\":\"normal.package\"}");

            var packageId = UpmPackageJsonStaging.GetEffectivePackageId(null, _testDirectory);

            Assert.That(packageId, Is.EqualTo("normal.package"));
        }

        /// <summary>
        /// Verifies that the selected free manifest is copied to the staged package.json path.
        /// </summary>
        [Test]
        public void EnsureEffectivePackageJson_CopiesFreeManifestToPackageJson()
        {
            var freeManifest = "{\"name\":\"free.package\",\"version\":\"1.0.0\"}";
            File.WriteAllText(Path.Combine(_testDirectory, UpmConstants.FreePackageJsonFileName), freeManifest);
            var stagingDirectory = Path.Combine(_testDirectory, "Staging");
            Directory.CreateDirectory(stagingDirectory);
            File.Copy(
                Path.Combine(_testDirectory, UpmConstants.FreePackageJsonFileName),
                Path.Combine(stagingDirectory, UpmConstants.FreePackageJsonFileName));

            var result = UpmPackageJsonStaging.EnsureEffectivePackageJson(null, _testDirectory, stagingDirectory);

            Assert.That(result, Is.EqualTo(Path.Combine(stagingDirectory, UpmConstants.PackageJsonFileName)));
            Assert.That(File.ReadAllText(result), Is.EqualTo(freeManifest));
        }

        /// <summary>
        /// Verifies that package id resolution returns an empty value when no manifest exists.
        /// </summary>
        [Test]
        public void GetEffectivePackageId_ReturnsEmptyWhenNoManifestExists()
        {
            var packageId = UpmPackageJsonStaging.GetEffectivePackageId(null, _testDirectory);

            Assert.That(packageId, Is.Empty);
        }
    }
}
