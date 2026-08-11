using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class AppRuntimeVerbExecutor
{
    private readonly AppServices _services;
    private readonly PersonalInstallService _install;

    public AppRuntimeVerbExecutor(AppServices services, PersonalInstallService install)
    {
        _services = services;
        _install = install;
    }

    public async Task<int> ExecuteAsync(AppStartupOptions options, CancellationToken cancellationToken = default)
    {
        return options.RuntimeVerb.ToLowerInvariant() switch
        {
            "--install" => Print(_install.InstallOrUpdate()),
            "--repair" => await RepairAsync(options.RuntimeArguments, cancellationToken),
            "--uninstall" => BeginUninstall(),
            "--browser-native-host" => await RunBrowserNativeHostAsync(cancellationToken),
            "--plugin-background-update" => await RunPluginBackgroundUpdateAsync(options.RuntimeArguments, cancellationToken),
            "--runtime-diagnostics" => await PrintDiagnosticsAsync(options.RuntimeArguments, cancellationToken),
            _ => 2
        };
    }

    private async Task<int> RepairAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var runtime = await _services.BundledTools.RepairAsync(cancellationToken);
        if (arguments.Any(argument => argument.Equals("--runtime-only", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { runtime }, JsonOptions));
            return runtime.Succeeded ? 0 : 1;
        }

        var install = _install.Repair();
        Console.WriteLine(JsonSerializer.Serialize(new { install, runtime }, JsonOptions));
        return install.Succeeded && runtime.Succeeded ? 0 : 1;
    }

    private int BeginUninstall()
    {
        _services.BrowserNativeHosts.Uninstall();
        return Print(_install.BeginUninstall());
    }

    private async Task<int> RunBrowserNativeHostAsync(CancellationToken cancellationToken)
    {
        var runner = new BrowserNativeMessagingHostRunner(_services.BrowserExtensionBridge);
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        return await runner.RunOnceAsync(input, output, cancellationToken);
    }

    private async Task<int> RunPluginBackgroundUpdateAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var registry = ReadArgument(args, "--registry");
        if (string.IsNullOrWhiteSpace(registry))
        {
            Console.Error.WriteLine("--plugin-background-update requires --registry <path-or-url>.");
            return 2;
        }

        var service = new PluginBackgroundUpdateService(_services.Paths, _services.RemotePlugins);
        var result = await service.RunAsync(new PluginBackgroundUpdateRunRequest
        {
            RegistryLocation = registry,
            Mode = ReadArgument(args, "--mode") ?? PluginBackgroundUpdateService.CheckOnlyMode,
            StatePath = ReadArgument(args, "--state"),
            IntervalHours = double.TryParse(ReadArgument(args, "--interval-hours"), out var hours) ? hours : 24d,
            Force = args.Any(arg => arg.Equals("--force", StringComparison.OrdinalIgnoreCase))
        }, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return result.Succeeded ? 0 : 1;
    }

    private async Task<int> PrintDiagnosticsAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        PersonSegmentationInferenceResult? segmentationSmoke = null;
        var segmentationInput = ReadArgument(arguments, "--segmentation-input");
        var segmentationOutput = ReadArgument(arguments, "--segmentation-output");
        if (!string.IsNullOrWhiteSpace(segmentationInput) || !string.IsNullOrWhiteSpace(segmentationOutput))
        {
            if (string.IsNullOrWhiteSpace(segmentationInput) || string.IsNullOrWhiteSpace(segmentationOutput))
            {
                Console.Error.WriteLine("Segmentation smoke requires both --segmentation-input and --segmentation-output.");
                return 2;
            }

            using var segmentation = new PersonSegmentationInferenceService(_services.BundledTools);
            segmentationSmoke = await segmentation.GenerateMaskAsync(segmentationInput, segmentationOutput, cancellationToken);
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            product = "GoatShot",
            version = PersonalInstallService.ProductVersion,
            buildId = BuildIdentity.Current,
            install = _install.GetState(),
            startup = _services.Startup.GetState(_install.InstalledExecutablePath),
            browserNativeHost = _services.BrowserNativeHosts.GetStatus(),
            bundledRuntime = _services.BundledTools.ValidateExisting(),
            segmentationSmoke
        }, JsonOptions));
        return segmentationSmoke is null || segmentationSmoke.Succeeded ? 0 : 1;
    }

    private static int Print(PersonalInstallResult result)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return result.Succeeded ? 0 : 1;
    }

    private static string? ReadArgument(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index + 1 < args.Count; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

}
