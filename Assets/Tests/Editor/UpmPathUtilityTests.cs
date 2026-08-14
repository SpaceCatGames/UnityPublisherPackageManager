using System.IO;
using NUnit.Framework;
using SCG.UPPM.Upm;

namespace SCG.UPPM.Tests
{
    /// <summary>
    /// Verifies path normalization and file remapping used by the UPM workflow.
    /// </summary>
    public sealed class UpmPathUtilityTests
    {
        /// <summary>
        /// Verifies that whitespace input resolves to the default package folder name.
        /// </summary>
        [Test]
        public void GetSafeFolderName_UsesFallbackForWhitespaceInput()
        {
            var result = UpmPathUtility.GetSafeFolderName("   ");

            Assert.That(result, Is.EqualTo("SCG"));
        }

        /// <summary>
        /// Verifies that a nested file keeps its relative path after a root folder move.
        /// </summary>
        [Test]
        public void MapMovedFileToTemp_PreservesRelativePathForNestedFile()
        {
            var sourceRoot = Path.Combine(Path.GetTempPath(), "source-root");
            var stagingRoot = Path.Combine(Path.GetTempPath(), "staging-root");
            var sourceFile = Path.Combine(sourceRoot, "Nested", "package.json");

            var result = UpmPathUtility.MapMovedFileToTemp(sourceRoot, stagingRoot, sourceFile);

            Assert.That(result, Is.EqualTo(Path.Combine(stagingRoot, "Nested", "package.json")));
        }

        /// <summary>
        /// Verifies that an external file maps to the staging root by file name.
        /// </summary>
        [Test]
        public void MapMovedFileToTemp_UsesFileNameForExternalFile()
        {
            var sourceRoot = Path.Combine(Path.GetTempPath(), "source-root");
            var stagingRoot = Path.Combine(Path.GetTempPath(), "staging-root");
            var externalFile = Path.Combine(Path.GetTempPath(), "external", "package.json");

            var result = UpmPathUtility.MapMovedFileToTemp(sourceRoot, stagingRoot, externalFile);

            Assert.That(result, Is.EqualTo(Path.Combine(stagingRoot, "package.json")));
        }

        /// <summary>
        /// Verifies Windows path comparison ignores case and trailing separators.
        /// </summary>
        [Test]
        public void PathsEqual_IgnoresTrailingSeparatorsAndCharacterCase()
        {
            var path = Path.Combine(Path.GetTempPath(), "SCG-Path-Test");

            var result = UpmPathUtility.PathsEqual(path.ToUpperInvariant() + Path.DirectorySeparatorChar, path.ToLowerInvariant());

            Assert.That(result, Is.True);
        }
    }
}
