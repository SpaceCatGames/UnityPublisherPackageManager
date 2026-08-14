using System;
using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using SCG.UPPM.Upm;

namespace SCG.UPPM.Tests
{
    /// <summary>
    /// Verifies unified durable notifications and failure cleanup behavior.
    /// </summary>
    public sealed class CompletionNotificationTests
    {
        private string _testDirectory;

        [SetUp]
        public void SetUp() => _testDirectory = UpmTestPaths.CreateTemporaryDirectory();

        [TearDown]
        public void TearDown() => UpmTestPaths.DeleteDirectory(_testDirectory);

        [Test]
        public void UnifiedState_StoresUpmAndSamplesNotificationsWithOneDto()
        {
            var path = Path.Combine(_testDirectory, "notifications.json");
            var state = new CompletionNotificationState();
            state.Notifications.Add(CreateNotification(CompletionNotificationKind.UpmReturn));
            state.Notifications.Add(CreateNotification(CompletionNotificationKind.SamplesHidden));

            CompletionNotificationStorage.SaveOrClear(state, path);
            var loaded = CompletionNotificationStorage.LoadOrCreate(path, null);

            Assert.That(loaded.Notifications, Has.Count.EqualTo(2));
            Assert.That(loaded.Notifications[0].RootPath, Is.EqualTo("C:/Project/Assets/TestPackage"));
            Assert.That(loaded.Notifications[1].RootPath, Is.EqualTo("C:/Project/Assets/TestPackage"));
        }

        [Test]
        public void LoadOrCreate_InvalidJsonDeletesMarker()
        {
            var path = Path.Combine(_testDirectory, "notifications.json");
            File.WriteAllText(path, "not-json");
            Exception loggedException = null;

            var state = CompletionNotificationStorage.LoadOrCreate(path, exception => loggedException = exception);

            Assert.That(state.Notifications, Is.Empty);
            Assert.That(File.Exists(path), Is.False);
            Assert.That(loggedException, Is.Not.Null);
        }

        [Test]
        public void BuildState_InvalidJsonDeletesMarker()
        {
            var path = Path.Combine(_testDirectory, "build-state.json");
            File.WriteAllText(path, "not-json");
            Exception loggedException = null;

            var state = UpmBuildStateStorage.LoadOrCreate(path, exception => loggedException = exception);

            Assert.That(state.Stage, Is.EqualTo(default(UpmStage)));
            Assert.That(File.Exists(path), Is.False);
            Assert.That(loggedException, Is.Not.Null);
        }

        [Test]
        public void ExecuteResumableStep_FailureClearsMarkersAndAllowsRetry()
        {
            var clearCount = 0;

            Assert.That(
                () => UpmBuildFlow.ExecuteResumableStep(
                    () => throw new InvalidOperationException("Move failed."),
                    () => clearCount++),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(clearCount, Is.EqualTo(1));
            Assert.That(
                () => UpmBuildFlow.ExecuteResumableStep(() => { }, () => clearCount++),
                Throws.Nothing);
            Assert.That(clearCount, Is.EqualTo(1));
        }

        [TestCase(UpmPackageAction.Build)]
        [TestCase(UpmPackageAction.Return)]
        public void RequestedInitialization_FailureClearsRequestAndAllowsRetry(UpmPackageAction _)
        {
            var clearCount = 0;

            Assert.That(
                () => UpmBuildFlow.ExecuteRequestedInitialization(
                    () => throw new InvalidOperationException("Initialization failed."),
                    () => clearCount++),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(clearCount, Is.EqualTo(1));
            Assert.That(
                () => UpmBuildFlow.ExecuteRequestedInitialization(() => { }, () => clearCount++),
                Throws.Nothing);
            Assert.That(clearCount, Is.EqualTo(1));
        }

        [Test]
        public void VisibilityRequest_QueuedBehindActiveRequestDoesNotApplyEarly()
        {
            var state = new CompletionNotificationState();
            state.Notifications.Add(CreateNotification(CompletionNotificationKind.SamplesVisible));
            var applyCount = 0;

            var started = SamplesVisibilityNotificationCoordinator.TryEnqueueCore(
                state,
                CreateNotification(CompletionNotificationKind.SamplesHidden),
                _ => { },
                () => applyCount++,
                () => { });

            Assert.That(started, Is.False);
            Assert.That(applyCount, Is.Zero);
            Assert.That(state.PendingVisibility, Is.EqualTo(new[] { SamplesVisibility.Hidden }));
        }

        [Test]
        public void QueuedVisibility_ApplyFailureRemovesFailedRequestWithoutRetryLoop()
        {
            var state = new CompletionNotificationState
            {
                PendingVisibility = new List<SamplesVisibility> { SamplesVisibility.Hidden }
            };
            var saveCount = 0;
            var retryCount = 0;
            Exception loggedException = null;

            var started = SamplesVisibilityNotificationCoordinator.TryStartNextPendingCore(
                state,
                _ => throw new IOException("Visibility move failed."),
                () => state,
                _ => saveCount++,
                () => retryCount++,
                exception => loggedException = exception);

            Assert.That(started, Is.False);
            Assert.That(state.PendingVisibility, Is.Empty);
            Assert.That(saveCount, Is.EqualTo(1));
            Assert.That(retryCount, Is.Zero);
            Assert.That(loggedException, Is.TypeOf<IOException>());
        }

        private static CompletionNotification CreateNotification(CompletionNotificationKind kind) => new()
        {
            Kind = kind,
            RootPath = "C:/Project/Assets/TestPackage",
            OriginatingDomainId = "domain",
            RequiresDomainReload = true
        };
    }
}
