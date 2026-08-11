using System.Windows;
using System.Windows.Threading;
using System.Text.Json;
using System.Diagnostics;
using GoatShot.App.Models;
using GoatShot.App.Services;
using GoatShot.App.Windows;

namespace GoatShot.App;

public partial class App : System.Windows.Application
{
    public AppServices Services { get; private set; } = null!;
    private SingleInstanceCoordinator? _singleInstance;
    private PersonalInstallService? _personalInstall;
    private ReplayWindowsLifecycleController? _replayWindowsLifecycle;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartupTrace.Write($"OnStartup args={string.Join('|', e.Args)}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var startupArguments = BrowserNativeHostLaunchDetector.Resolve(
            e.Args,
            Console.IsInputRedirected);
        var startupOptions = AppStartupOptions.Parse(
            startupArguments,
            ResolveStartupEnvironment("OPEN_SETTINGS"),
            ResolveStartupEnvironment("SETTINGS_SECTION"),
            ResolveStartupEnvironment("RENDER_MAIN"),
            ResolveStartupEnvironment("RENDER_MAIN_OUTPUT"),
            ResolveStartupEnvironment("RENDER_SETTINGS_SECTION"),
            ResolveStartupEnvironment("RENDER_SETTINGS_OUTPUT"),
            ResolveStartupEnvironment("AUDIT_SETTINGS_SECTION"),
            ResolveStartupEnvironment("AUDIT_SETTINGS_OUTPUT"),
            ResolveStartupEnvironment("RENDER_EDITOR_IMAGE"),
            ResolveStartupEnvironment("RENDER_EDITOR_OUTPUT"),
            ResolveStartupEnvironment("RENDER_TRAY_MENU"),
            ResolveStartupEnvironment("RENDER_TRAY_MENU_OUTPUT"),
            ResolveStartupEnvironment("RENDER_CAPTURE_OVERLAY"),
            ResolveStartupEnvironment("RENDER_CAPTURE_OVERLAY_OUTPUT"),
            ResolveStartupEnvironment("RENDER_UPLOAD_TASK_SURFACE"),
            ResolveStartupEnvironment("RENDER_UPLOAD_TASK_OUTPUT"),
            ResolveStartupEnvironment("RENDER_SHARE_HISTORY"),
            ResolveStartupEnvironment("RENDER_SHARE_HISTORY_OUTPUT"),
            ResolveStartupEnvironment("RENDER_AI_HISTORY"),
            ResolveStartupEnvironment("RENDER_AI_HISTORY_OUTPUT"),
            ResolveStartupEnvironment("RENDER_CAPTURE_TASK"),
            ResolveStartupEnvironment("RENDER_CAPTURE_TASK_OUTPUT"),
            ResolveStartupEnvironment("PROOF_SCENE"),
            ResolveStartupEnvironment("RENDER_PROOF_SCENE"),
            ResolveStartupEnvironment("RENDER_PROOF_SCENE_OUTPUT"),
            ResolveStartupEnvironment("RECORD_PROOF_SCENE"),
            ResolveStartupEnvironment("RECORD_PROOF_SCENE_OUTPUT"),
            ResolveStartupEnvironment("RECORD_PROOF_SCENE_DURATION"),
            ResolveStartupEnvironment("AUDIT_WPF_SURFACE"),
            ResolveStartupEnvironment("AUDIT_WPF_OUTPUT"));
        StartupTrace.Write($"Parsed mode={startupOptions.Mode} verb={startupOptions.RuntimeVerb}");

        if (startupOptions.RuntimeVerb.Equals("--complete-uninstall", StringComparison.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(async () =>
            {
                var exitCode = await PersonalInstallService.CompleteUninstallAsync(startupOptions.RuntimeArguments);
                Shutdown(exitCode);
            });
            return;
        }

        StartupTrace.Write("Creating services");
        Services = AppServices.Create();
        StartupTrace.Write("Services created");
        _personalInstall = Services.PersonalInstall;

        if (startupOptions.Mode == AppStartupMode.RuntimeVerb)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                StartupTrace.Write("Runtime verb dispatched");
                var exitCode = await new AppRuntimeVerbExecutor(Services, _personalInstall)
                    .ExecuteAsync(startupOptions);
                StartupTrace.Write($"Runtime verb completed exit={exitCode}");
                Shutdown(exitCode);
            });
            return;
        }
        if (startupOptions.RenderMain)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await MainWindowRenderer.RenderAsync(
                        Services,
                        startupOptions.RenderMainOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.RenderMainOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.RenderFrameExplorer)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await FrameExplorerWindowRenderer.RenderAsync(
                        Services,
                        startupOptions.RenderFrameExplorerOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.RenderFrameExplorerOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.RenderSettings)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await SettingsWindowRenderer.RenderAsync(
                        Services,
                        startupOptions.RenderSettingsSection,
                        startupOptions.RenderSettingsOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.RenderSettingsOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.AuditSettings)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await SettingsWindowAccessibilityAuditor.AuditAsync(
                        Services,
                        startupOptions.AuditSettingsSection,
                        startupOptions.AuditSettingsOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.AuditSettingsOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.RenderEditor)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await EditorWindowRenderer.RenderAsync(
                        Services,
                        startupOptions.RenderEditorImagePath,
                        startupOptions.RenderEditorOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.RenderEditorOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.RenderTrayMenu)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await TrayMenuPreviewRenderer.RenderAsync(startupOptions.RenderTrayMenuOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.RenderTrayMenuOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.RenderCaptureOverlay)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await CaptureOverlayPreviewRenderer.RenderAsync(startupOptions.RenderCaptureOverlayOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.RenderCaptureOverlayOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.RenderUploadTask)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await UploadTaskWindowRenderer.RenderAsync(
                        Services,
                        startupOptions.RenderUploadTaskSurface,
                        startupOptions.RenderUploadTaskOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.RenderUploadTaskOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.RenderShareHistory)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await ShareHistoryWindowRenderer.RenderAsync(
                        Services,
                        startupOptions.RenderShareHistoryOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.RenderShareHistoryOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.RenderAiHistory)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await AiHistoryWindowRenderer.RenderAsync(
                        Services,
                        startupOptions.RenderAiHistoryOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.RenderAiHistoryOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.RenderCaptureTask)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await CaptureTaskWindowRenderer.RenderAsync(
                        Services,
                        startupOptions.RenderCaptureTaskOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.RenderCaptureTaskOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.RenderProofScene)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await ProofSceneWindowRenderer.RenderAsync(
                        startupOptions.RenderProofSceneOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.RenderProofSceneOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.RecordProofScene)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await RecordProofSceneAsync(startupOptions);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.RecordProofSceneOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.AuditWpf)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await WpfSurfaceAccessibilityAuditor.AuditAsync(
                        Services,
                        startupOptions.AuditWpfSurface,
                        startupOptions.AuditWpfOutputPath);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    WriteProofFailure(startupOptions.AuditWpfOutputPath, exception);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (startupOptions.ShowProofScene)
        {
            var proofWindow = new ProofSceneWindow();
            MainWindow = proofWindow;
            proofWindow.Show();
            return;
        }

        if (startupOptions.Mode == AppStartupMode.Interactive &&
            _personalInstall.IsDistributionBuild &&
            !_personalInstall.IsRunningInstalledCopy)
        {
            if (_personalInstall.IsNewerThanInstalled())
            {
                if (OfferPersonalInstall(_personalInstall))
                {
                    return;
                }
            }
            else if (File.Exists(_personalInstall.InstalledExecutablePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _personalInstall.InstalledExecutablePath,
                    UseShellExecute = true
                });
                Shutdown(0);
                return;
            }
        }

        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.IsPrimary)
        {
            _singleInstance.SendAsync(SingleInstanceMessage.Activate).GetAwaiter().GetResult();
            Shutdown(0);
            return;
        }

        var replayStartupCleanup = AppServices.CleanupReplayBufferAtStartup(
            new FileReplayBufferStorage(Services.Paths.ReplayBufferRoot),
            DateTimeOffset.UtcNow);
        StartupTrace.Write(
            $"Receipts replay startup cleanup deleted={replayStartupCleanup.DeletedPaths.Count} " +
            $"retained={replayStartupCleanup.RetainedPaths.Count} failures={replayStartupCleanup.Failures.Count}");
        SubscribeReplaySystemEvents();

        var mainWindow = new MainWindow(
            Services,
            startHidden: startupOptions.Mode == AppStartupMode.Background);
        MainWindow = mainWindow;
        _singleInstance.StartServer(message => Dispatcher.InvokeAsync(() =>
        {
            if (message == SingleInstanceMessage.Activate)
            {
                mainWindow.ShowWorkspaceCommand();
            }
            else
            {
                mainWindow.ExitCommand();
            }
        }).Task);
        mainWindow.Show();
        _personalInstall.MarkStartupSuccessful();
    }

    private static string? ResolveStartupEnvironment(string suffix)
    {
        var resolution = BrandEnvironment.Resolve(suffix);
        if (resolution.UsedLegacyFallback)
        {
            StartupTrace.Write($"Legacy environment alias in use: {resolution.SourceVariable}; prefer {BrandIdentity.EnvironmentVariable(suffix)}.");
        }

        return resolution.Value;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        UnsubscribeReplaySystemEvents();
        _singleInstance?.Dispose();
        if (Services is not null)
        {
            Services.Dispose();
        }
        base.OnExit(e);
    }

    private void SubscribeReplaySystemEvents()
    {
        if (_replayWindowsLifecycle is not null)
        {
            return;
        }

        var lifecycle = new ReplayWindowsLifecycleController(
            () => Services.Replay,
            new WindowsReplaySystemEventSource(),
            StartupTrace.Write);
        _replayWindowsLifecycle = lifecycle;
        Services.ReplayServiceChanged += Services_ReplayServiceChanged;
        try
        {
            lifecycle.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Services.ReplayServiceChanged -= Services_ReplayServiceChanged;
            lifecycle.Dispose();
            _replayWindowsLifecycle = null;
            var failClosed = Services.Replay.SuspendForSystemEvent();
            StartupTrace.Write(
                $"Replay Windows lifecycle subscription failed; capture remains suspended " +
                $"until restart ({ex.GetType().Name}: {ex.Message}). {failClosed.Message}");
        }
    }

    private void UnsubscribeReplaySystemEvents()
    {
        var lifecycle = _replayWindowsLifecycle;
        if (lifecycle is null)
        {
            return;
        }

        Services.ReplayServiceChanged -= Services_ReplayServiceChanged;
        _replayWindowsLifecycle = null;
        lifecycle.Dispose();
    }

    private void Services_ReplayServiceChanged(object? sender, EventArgs e) =>
        _replayWindowsLifecycle?.RefreshCurrentReplay();

    private bool OfferPersonalInstall(PersonalInstallService install)
    {
        SingleInstanceCoordinator? coordinator = new();
        try
        {
            var state = install.GetState();
            var isUpdate = !string.IsNullOrWhiteSpace(state.InstalledVersion);

            // A first-run prompt temporarily owns the user-scoped instance lock. Do not
            // let a second distribution launch create another prompt or compete to copy.
            if (!isUpdate && !coordinator.IsPrimary)
            {
                coordinator.SendAsync(SingleInstanceMessage.Activate).GetAwaiter().GetResult();
                Shutdown(0);
                return true;
            }

            if (coordinator.IsPrimary)
            {
                coordinator.StartServer(_ => Task.CompletedTask);
            }

            var response = System.Windows.MessageBox.Show(
                isUpdate
                    ? $"Update the installed Receipts {state.InstalledVersion} copy to {state.CurrentVersion}?"
                    : "Install Receipts for this Windows user and start it in the tray when you sign in?",
                isUpdate ? "Update Receipts" : "Install Receipts",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (response != MessageBoxResult.Yes)
            {
                return false;
            }

            if (!coordinator.IsPrimary)
            {
                coordinator.SendAsync(SingleInstanceMessage.PrepareForUpdate).GetAwaiter().GetResult();
                Thread.Sleep(750);
            }

            var result = install.InstallOrUpdate();
            if (!result.Succeeded)
            {
                System.Windows.MessageBox.Show(result.Message, "Receipts install", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // Release the temporary first-run instance lock before starting the installed
            // copy. Otherwise the new process treats the installer as the primary app and exits.
            coordinator.Dispose();
            coordinator = null;

            Process.Start(new ProcessStartInfo
            {
                FileName = result.InstalledPath,
                UseShellExecute = true
            });
            Shutdown(0);
            return true;
        }
        finally
        {
            coordinator?.Dispose();
        }
    }

    private async Task RecordProofSceneAsync(AppStartupOptions startupOptions)
    {
        if (string.IsNullOrWhiteSpace(startupOptions.RecordProofSceneOutputPath))
        {
            throw new ArgumentException("A proof-scene recording output path is required.");
        }

        var outputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(startupOptions.RecordProofSceneOutputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        ProofSceneWindow? window = null;
        try
        {
            window = new ProofSceneWindow
            {
                Topmost = true
            };
            MainWindow = window;
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.Activate();
            window.Focus();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var captureBounds = GetWindowCaptureBounds(window);
            var duration = TimeSpan.FromSeconds(Math.Clamp(startupOptions.RecordProofSceneDurationSeconds, 1d, 180d));
            var settings = new RecordingSettings
            {
                PreferProductionCaptureEngine = true,
                PreferHevcEncoding = false,
                QualityProfile = "Small",
                FramesPerSecond = 10,
                TargetWidth = 0,
                TargetHeight = 0,
                BitrateKbps = 0,
                UseVariableBitrate = true,
                IncludeMicrophone = false,
                IncludeSystemAudio = false,
                EnableWebcamOverlay = false,
                ShowCountdown = false,
                ShowRecordingBorder = true,
                ShowRecordingTimer = true,
                ShowKeystrokeOverlay = false,
                RecordingTimerPosition = "TopLeft",
                RecordingOverlayStyle = "HighContrast"
            };

            var result = await Services.Recording.RecordMp4Async(
                duration,
                outputPath,
                settings,
                RecordingCaptureTarget.Region(captureBounds),
                addToWorkspace: false);

            var sidecar = Path.ChangeExtension(outputPath, ".proof.json");
            await File.WriteAllTextAsync(sidecar, JsonSerializer.Serialize(new
            {
                result.Succeeded,
                result.OutputPath,
                RequestedDurationSeconds = duration.TotalSeconds,
                result.Message,
                SafeContent = "Receipts Proof Scene app-owned WPF window",
                AudioRequested = false,
                WebcamRequested = false,
                Target = "explicit proof-scene window bounds",
                Bounds = captureBounds.Display
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Message);
            }
        }
        finally
        {
            window?.Close();
        }
    }

    private static CaptureBounds GetWindowCaptureBounds(Window window)
    {
        var topLeft = window.PointToScreen(new System.Windows.Point(0, 0));
        var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
        var width = Math.Max(1, (int)Math.Round(window.ActualWidth * transform.M11));
        var height = Math.Max(1, (int)Math.Round(window.ActualHeight * transform.M22));
        return new CaptureBounds
        {
            X = (int)Math.Round(topLeft.X),
            Y = (int)Math.Round(topLeft.Y),
            Width = width,
            Height = height
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Windows.MessageBox.Show(
            e.Exception.Message,
            "Receipts error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteProofFailure(string outputPath, Exception exception)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(outputPath + ".error.txt");
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                fullPath,
                $"{exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
        }
        catch
        {
            // Proof helpers must not mask the original exit code.
        }
    }
}
