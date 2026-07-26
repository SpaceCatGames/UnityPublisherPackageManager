using System.IO;
using System.Text;
using NUnit.Framework;
using SCG.UnityAssetPublisherTools.Helpers;

namespace SCG.UnityAssetPublisherTools.Tests
{
    /// <summary>
    /// Verifies manifest dependency text updates and UTF-8 encoding behavior.
    /// </summary>
    public sealed class ManifestJsonUtilityTests
    {
        private string _testDirectory;
        private string _manifestPath;

        /// <summary>
        /// Creates a temporary manifest with dependencies before every test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _testDirectory = UpmTestPaths.CreateTemporaryDirectory();
            _manifestPath = Path.Combine(_testDirectory, "manifest.json");
            File.WriteAllText(
                _manifestPath,
                "{\n  \"dependencies\": {\n    \"com.example.existing\": \"1.0.0\"\n  },\n  \"scopedRegistries\": []\n}\n",
                new UTF8Encoding(false));
        }

        /// <summary>
        /// Deletes the temporary manifest directory created for the current test.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            UpmTestPaths.DeleteDirectory(_testDirectory);
        }

        /// <summary>
        /// Verifies that adding a dependency preserves unrelated manifest sections and writes no UTF-8 byte order mark.
        /// </summary>
        [Test]
        public void SetDependency_AddsDependencyWithoutChangingOtherManifestSections()
        {
            ManifestJsonUtility.SetDependency(_manifestPath, "com.example.added", "file:../Packages/com.example.added");

            var json = File.ReadAllText(_manifestPath);
            Assert.That(json, Does.Contain("\"com.example.existing\": \"1.0.0\""));
            Assert.That(json, Does.Contain("\"com.example.added\": \"file:../Packages/com.example.added\""));
            Assert.That(json, Does.Contain("\"scopedRegistries\": []"));
            Assert.That(HasUtf8Bom(File.ReadAllBytes(_manifestPath)), Is.False);
        }

        /// <summary>
        /// Verifies that writing an unchanged dependency does not rewrite the manifest file.
        /// </summary>
        [Test]
        public void SetDependency_DoesNotRewriteUnchangedManifest()
        {
            var initialBytes = File.ReadAllBytes(_manifestPath);

            ManifestJsonUtility.SetDependency(_manifestPath, "com.example.existing", "1.0.0");

            Assert.That(File.ReadAllBytes(_manifestPath), Is.EqualTo(initialBytes));
        }

        /// <summary>
        /// Verifies that removing a dependency leaves unrelated dependencies intact.
        /// </summary>
        [Test]
        public void RemoveDependency_RemovesOnlyRequestedDependency()
        {
            ManifestJsonUtility.SetDependency(_manifestPath, "com.example.added", "2.0.0");

            ManifestJsonUtility.RemoveDependency(_manifestPath, "com.example.existing");

            var json = File.ReadAllText(_manifestPath);
            Assert.That(json, Does.Not.Contain("com.example.existing"));
            Assert.That(json, Does.Contain("\"com.example.added\": \"2.0.0\""));
        }

        /// <summary>
        /// Checks whether byte content begins with a UTF-8 byte order mark.
        /// </summary>
        /// <param name="bytes">File content bytes.</param>
        /// <returns>True when the content begins with a UTF-8 byte order mark.</returns>
        private static bool HasUtf8Bom(byte[] bytes) =>
            bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
    }
}
