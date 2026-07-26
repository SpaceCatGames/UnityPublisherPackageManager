using System.IO;
using NUnit.Framework;
using SCG.UnityAssetPublisherTools.Upm;

namespace SCG.UnityAssetPublisherTools.Tests
{
    /// <summary>
    /// Verifies resumable folder and meta file moves used by the UPM workflow.
    /// </summary>
    public sealed class UpmFileOperationsTests
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
        /// Verifies that a folder and its root meta file move together.
        /// </summary>
        [Test]
        public void EnsureFolderMovedWithMeta_MovesFolderAndMeta()
        {
            var source = CreateSourceFolder();
            var destination = Path.Combine(_testDirectory, "Destination");

            var moved = UpmFileOperations.EnsureFolderMovedWithMeta(source, destination);

            Assert.That(moved, Is.True);
            Assert.That(Directory.Exists(source), Is.False);
            Assert.That(Directory.Exists(destination), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(destination, "payload.txt")), Is.EqualTo("payload"));
            Assert.That(File.Exists(source + ".meta"), Is.False);
            Assert.That(File.Exists(destination + ".meta"), Is.True);
        }

        /// <summary>
        /// Verifies that a retry completes a root meta move left by an interrupted folder move.
        /// </summary>
        [Test]
        public void EnsureFolderMovedWithMeta_CompletesPendingMetaMove()
        {
            var source = CreateSourceFolder();
            var destination = Path.Combine(_testDirectory, "Destination");
            Directory.Move(source, destination);

            var moved = UpmFileOperations.EnsureFolderMovedWithMeta(source, destination);

            Assert.That(moved, Is.False);
            Assert.That(File.Exists(source + ".meta"), Is.False);
            Assert.That(File.ReadAllText(destination + ".meta"), Is.EqualTo("meta"));
        }

        /// <summary>
        /// Verifies that an existing destination meta file prevents a source folder move.
        /// </summary>
        [Test]
        public void EnsureFolderMovedWithMeta_RejectsExistingDestinationMetaBeforeMovingFolder()
        {
            var source = CreateSourceFolder();
            var destination = Path.Combine(_testDirectory, "Destination");
            File.WriteAllText(destination + ".meta", "conflict");

            Assert.That(
                () => UpmFileOperations.EnsureFolderMovedWithMeta(source, destination),
                Throws.TypeOf<IOException>());

            Assert.That(Directory.Exists(source), Is.True);
            Assert.That(File.Exists(source + ".meta"), Is.True);
            Assert.That(Directory.Exists(destination), Is.False);
        }

        /// <summary>
        /// Verifies that retries reject a state where both source and destination folders exist.
        /// </summary>
        [Test]
        public void EnsureFolderMovedWithMeta_RejectsAmbiguousFolderLocations()
        {
            var source = CreateSourceFolder();
            var destination = Path.Combine(_testDirectory, "Destination");
            Directory.CreateDirectory(destination);

            Assert.That(
                () => UpmFileOperations.EnsureFolderMovedWithMeta(source, destination),
                Throws.TypeOf<IOException>());
        }

        /// <summary>
        /// Creates a source folder with a payload file and root meta file.
        /// </summary>
        /// <returns>Absolute source folder path.</returns>
        private string CreateSourceFolder()
        {
            var source = Path.Combine(_testDirectory, "Source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "payload.txt"), "payload");
            File.WriteAllText(source + ".meta", "meta");
            return source;
        }
    }
}
