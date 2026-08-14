using System.IO;
using NUnit.Framework;

namespace SCG.UPPM.Tests
{
    /// <summary>
    /// Verifies persistent folder metadata isolation and restoration behavior.
    /// </summary>
    public sealed class SamplesFolderMetaStorageTests
    {
        private string _testDirectory;
        private SamplesFolderMetaStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = UpmTestPaths.CreateTemporaryDirectory();
            _storage = new SamplesFolderMetaStorage(Path.Combine(_testDirectory, "Storage"));
        }

        [TearDown]
        public void TearDown() => UpmTestPaths.DeleteDirectory(_testDirectory);

        [Test]
        public void Save_DifferentPackageRootsRemainIsolated()
        {
            var firstRoot = Path.Combine(_testDirectory, "FirstPackage");
            var secondRoot = Path.Combine(_testDirectory, "SecondPackage");
            var firstMeta = new byte[] { 1, 2, 3 };
            var secondMeta = new byte[] { 4, 5, 6 };

            _storage.Save(firstRoot, Constants.SamplesBase, firstMeta);
            _storage.Save(secondRoot, Constants.SamplesBase, secondMeta);

            Assert.That(_storage.Load(firstRoot, Constants.SamplesBase), Is.EqualTo(firstMeta));
            Assert.That(_storage.Load(secondRoot, Constants.SamplesBase), Is.EqualTo(secondMeta));
            Assert.That(_storage.GetPackageStoragePath(firstRoot), Is.Not.EqualTo(_storage.GetPackageStoragePath(secondRoot)));
        }

        [Test]
        public void MoveFolder_HideUsesCurrentVisibleMetaAndRevealRestoresIt()
        {
            var root = Path.Combine(_testDirectory, "Package");
            var visible = Path.Combine(root, Constants.SamplesRenamed);
            var hidden = Path.Combine(root, Constants.SamplesBase);
            Directory.CreateDirectory(visible);
            File.WriteAllText(visible + ".meta", "guid: current");
            _storage.Save(root, Constants.SamplesBase, System.Text.Encoding.UTF8.GetBytes("guid: stale"));

            SamplesRenamer.MoveFolder(visible, hidden, root, Constants.SamplesBase, false, _storage);
            Assert.That(File.Exists(hidden + ".meta"), Is.False);

            SamplesRenamer.MoveFolder(hidden, visible, root, Constants.SamplesBase, true, _storage);
            Assert.That(File.ReadAllText(visible + ".meta"), Is.EqualTo("guid: current"));
            Assert.That(_storage.Load(root, Constants.SamplesBase), Is.Null);
        }

        [Test]
        public void RemoveHiddenFolderMetaFiles_AlreadyHiddenFolderPreservesLegacyGuid()
        {
            var root = Path.Combine(_testDirectory, "Package");
            var hidden = Path.Combine(root, Constants.SamplesBase);
            var visible = Path.Combine(root, Constants.SamplesRenamed);
            Directory.CreateDirectory(hidden);
            File.WriteAllText(hidden + ".meta", "guid: legacy");

            var changed = SamplesRenamer.RemoveHiddenFolderMetaFiles(root, _storage);

            Assert.That(changed, Is.True);
            Assert.That(File.Exists(hidden + ".meta"), Is.False);
            Assert.That(_storage.Load(root, Constants.SamplesBase), Is.Not.Null);

            SamplesRenamer.MoveFolder(hidden, visible, root, Constants.SamplesBase, true, _storage);

            Assert.That(File.ReadAllText(visible + ".meta"), Is.EqualTo("guid: legacy"));
            Assert.That(_storage.Load(root, Constants.SamplesBase), Is.Null);
        }
    }
}
