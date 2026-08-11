using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class PersonalInstallServiceTests
{
    [TestMethod]
    public void StartupCommand_IsQuotedAndStartsInBackground()
    {
        var command = StartupRegistrationService.BuildStartupCommand(@"C:\Program Files\GoatShot\GoatShot.exe");

        Assert.AreEqual("\"C:\\Program Files\\GoatShot\\GoatShot.exe\" --background", command);
    }

    [TestMethod]
    public void InstallPaths_ArePerUserAndVersionedRuntimeIsOutsideProgramDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"goatshot-install-test-{Guid.NewGuid():N}");
        var current = Path.Combine(root, "download", "GoatShot.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        File.WriteAllBytes(current, [0x47, 0x53]);

        try
        {
            var service = new PersonalInstallService(localAppData: root, currentExecutable: current);

            Assert.AreEqual(Path.Combine(root, "Programs", "GoatShot", "GoatShot.exe"), service.InstalledExecutablePath);
            Assert.AreEqual(Path.Combine(root, "GoatShot", "runtime"), service.RuntimeRoot);
            Assert.IsFalse(service.IsRunningInstalledCopy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SingleInstanceCoordinator_ForwardsMessageToPrimaryInstance()
    {
        var identity = $"test-{Guid.NewGuid():N}";
        using var primary = new SingleInstanceCoordinator(identity);
        using var secondary = new SingleInstanceCoordinator(identity);
        var received = new TaskCompletionSource<SingleInstanceMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartServer(message =>
        {
            received.TrySetResult(message);
            return Task.CompletedTask;
        });

        Assert.IsTrue(primary.IsPrimary);
        Assert.IsFalse(secondary.IsPrimary);
        Assert.IsTrue(await secondary.SendAsync(SingleInstanceMessage.Activate));
        Assert.AreEqual(SingleInstanceMessage.Activate, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    [Timeout(5000)]
    public void SingleInstanceCoordinator_SynchronousClientDoesNotDeadlockOnDispatcherStyleContext()
    {
        var identity = $"test-{Guid.NewGuid():N}";
        using var primary = new SingleInstanceCoordinator(identity);
        using var secondary = new SingleInstanceCoordinator(identity);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
        try
        {
            var sent = secondary
                .SendAsync(SingleInstanceMessage.Activate, TimeSpan.FromMilliseconds(100))
                .GetAwaiter()
                .GetResult();

            Assert.IsFalse(sent);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [TestMethod]
    public void SingleInstanceCoordinator_DisposeReleasesLockForRelaunch()
    {
        var identity = $"test-{Guid.NewGuid():N}";
        var installer = new SingleInstanceCoordinator(identity);
        Assert.IsTrue(installer.IsPrimary);

        installer.Dispose();

        using var installedCopy = new SingleInstanceCoordinator(identity);
        Assert.IsTrue(installedCopy.IsPrimary);
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            // Deliberately do not run posted work. SendAsync must not capture this context.
        }
    }
}
