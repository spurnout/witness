using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionInstallAssistService
{
    private const int DefaultRemoteDebuggingPort = 58617;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<BrowserExtensionInstallAssistResult> CreateAsync(
        BrowserExtensionInstallAssistRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = string.IsNullOrWhiteSpace(request.ExtensionSourceDirectory)
            ? Path.GetFullPath("browser-extension")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.ExtensionSourceDirectory));
        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.GetFullPath(Path.Combine("artifacts", "browser-extension-install-assist"))
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));
        var browser = NormalizeBrowser(request.Browser);
        var remoteDebuggingPort = NormalizeRemoteDebuggingPort(request.RemoteDebuggingPort);
        var startUrl = string.IsNullOrWhiteSpace(request.StartUrl)
            ? "about:blank"
            : request.StartUrl.Trim();
        var supportsAutomatedLaunch = browser is "chrome" or "edge";
        var profileDirectory = supportsAutomatedLaunch
            ? Path.Combine(outputRoot, $"{browser}-install-assist-profile")
            : string.Empty;

        var result = new BrowserExtensionInstallAssistResult
        {
            Browser = browser,
            SourceDirectory = source,
            OutputPath = outputRoot,
            PolicyAllowed = request.PolicyAllowed,
            PolicyReason = SensitiveTextDetector.Redact(request.PolicyReason ?? string.Empty),
            SourceExists = Directory.Exists(source) && File.Exists(Path.Combine(source, "manifest.json")),
            SupportsAutomatedLaunch = supportsAutomatedLaunch,
            Mode = supportsAutomatedLaunch ? "isolated-profile-load-extension" : "manual-temporary-load",
            StartUrl = startUrl,
            RemoteDebuggingPort = remoteDebuggingPort,
            RemoteDebuggingUrl = $"http://127.0.0.1:{remoteDebuggingPort}/json/version",
            ProfileDirectory = profileDirectory,
            LaunchScriptPath = supportsAutomatedLaunch ? Path.Combine(outputRoot, "launch-browser-extension.ps1") : string.Empty,
            LaunchPlanPath = Path.Combine(outputRoot, "browser-launch-plan.json"),
            AssistGuidePath = Path.Combine(outputRoot, "install-assist.md"),
            AssistJsonPath = Path.Combine(outputRoot, "install-assist.json"),
            Command = supportsAutomatedLaunch ? ".\\launch-browser-extension.ps1" : string.Empty,
            MutationFlags = new BrowserExtensionInstallAssistMutationFlags
            {
                StartsBrowser = request.StartBrowser
            }
        };

        result.NonGoals.AddRange(BuildNonGoals());

        if (string.IsNullOrWhiteSpace(browser))
        {
            result.Issues.Add("Install-assist browser must be one of: chrome, edge, or firefox.");
        }

        if (!request.PolicyAllowed)
        {
            result.Issues.Add(string.IsNullOrWhiteSpace(result.PolicyReason)
                ? "Browser extension handoff is disabled by managed policy."
                : result.PolicyReason);
        }

        if (!result.SourceExists)
        {
            result.Issues.Add($"Extension source with manifest.json was not found: {source}");
        }

        if (request.RemoteDebuggingPort is not null && request.RemoteDebuggingPort != remoteDebuggingPort)
        {
            result.Issues.Add("Remote debugging port must be between 1 and 65535.");
        }

        if (!string.IsNullOrWhiteSpace(browser) && !supportsAutomatedLaunch)
        {
            result.Warnings.Add("Firefox temporary add-on loading is still a manual browser action through about:debugging; this helper writes the checklist but does not start Firefox or install an add-on.");
        }

        if (supportsAutomatedLaunch)
        {
            result.BrowserArguments.AddRange(BuildBrowserArguments(profileDirectory, source, remoteDebuggingPort, startUrl));
            result.Commands.Add(".\\launch-browser-extension.ps1 -NoStart");
            result.Commands.Add(".\\launch-browser-extension.ps1");
            result.Commands.Add("receipts browser-extension native-host status --json");
        }
        else if (browser == "firefox")
        {
            result.Commands.Add("Open about:debugging#/runtime/this-firefox and choose Load Temporary Add-on.");
            result.Commands.Add($"Select the manifest.json file under {Quote(source)}.");
            result.Commands.Add("receipts browser-extension native-host status --json");
        }

        Directory.CreateDirectory(outputRoot);
        if (supportsAutomatedLaunch)
        {
            await File.WriteAllTextAsync(result.LaunchScriptPath, BuildLaunchScript(browser, source, startUrl, remoteDebuggingPort), cancellationToken);
            result.GeneratedFiles.Add(result.LaunchScriptPath);
        }

        await File.WriteAllTextAsync(result.LaunchPlanPath, JsonSerializer.Serialize(BuildLaunchPlan(result), JsonOptions) + Environment.NewLine, cancellationToken);
        result.GeneratedFiles.Add(result.LaunchPlanPath);

        await File.WriteAllTextAsync(result.AssistGuidePath, BuildMarkdown(result), cancellationToken);
        result.GeneratedFiles.Add(result.AssistGuidePath);

        if (request.StartBrowser && supportsAutomatedLaunch && result.Issues.Count == 0)
        {
            StartBrowser(result, request.BrowserExecutablePath);
        }
        else if (request.StartBrowser && !supportsAutomatedLaunch)
        {
            result.Issues.Add("Automatic browser start is only supported for Chrome or Edge.");
        }

        result.Succeeded = result.Issues.Count == 0;
        result.Message = result.Succeeded
            ? $"Browser extension install assist created: {outputRoot}"
            : $"Browser extension install assist created with blockers: {outputRoot}";

        await File.WriteAllTextAsync(result.AssistJsonPath, JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine, cancellationToken);
        result.GeneratedFiles.Add(result.AssistJsonPath);

        return result;
    }

    private static string NormalizeBrowser(string? browser)
    {
        if (string.IsNullOrWhiteSpace(browser))
        {
            return "edge";
        }

        return browser.Trim().ToLowerInvariant() switch
        {
            "chrome" or "chromium" => "chrome",
            "edge" or "msedge" => "edge",
            "firefox" => "firefox",
            _ => string.Empty
        };
    }

    private static int NormalizeRemoteDebuggingPort(int? port)
    {
        return port is >= 1 and <= 65535 ? port.Value : DefaultRemoteDebuggingPort;
    }

    private static List<string> BuildBrowserArguments(
        string profileDirectory,
        string source,
        int remoteDebuggingPort,
        string startUrl)
    {
        return new List<string>
        {
            $"--user-data-dir={profileDirectory}",
            $"--disable-extensions-except={source}",
            $"--load-extension={source}",
            "--no-first-run",
            "--no-default-browser-check",
            $"--remote-debugging-port={remoteDebuggingPort}",
            startUrl
        };
    }

    private static BrowserExtensionInstallAssistLaunchPlan BuildLaunchPlan(BrowserExtensionInstallAssistResult result)
    {
        return new BrowserExtensionInstallAssistLaunchPlan
        {
            Browser = result.Browser,
            SupportsAutomatedLaunch = result.SupportsAutomatedLaunch,
            Mode = result.Mode,
            LaunchScriptPath = result.LaunchScriptPath,
            ProfileDirectory = result.ProfileDirectory,
            ExtensionSourceDirectory = result.SourceDirectory,
            StartUrl = result.StartUrl,
            RemoteDebuggingPort = result.RemoteDebuggingPort,
            RemoteDebuggingUrl = result.RemoteDebuggingUrl,
            Command = result.Command,
            BrowserArguments = result.BrowserArguments.ToList(),
            MutationFlags = result.MutationFlags,
            ProofBoundary = result.SupportsAutomatedLaunch
                ? "Starts Chrome/Edge with an isolated local profile and unpacked extension source. This is temporary local loading, not browser-store publication, signing, review, availability, permanent profile mutation, or enterprise force-install proof."
                : "Writes manual temporary-loading guidance only. Browser add-on installation remains controlled by the browser, browser store, or enterprise policy."
        };
    }

    private static void StartBrowser(BrowserExtensionInstallAssistResult result, string? browserExecutablePath)
    {
        var executable = ResolveBrowserExecutable(result.Browser, browserExecutablePath, result.Issues);
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(result.ProfileDirectory);
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false
            };

            foreach (var argument in result.BrowserArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = Process.Start(startInfo);
            if (process is null)
            {
                result.Issues.Add("Browser process did not start.");
                return;
            }

            result.Started = true;
            result.StartedProcessId = process.Id;
            result.BrowserExecutablePath = executable;
        }
        catch (Exception ex)
        {
            result.Issues.Add($"Browser process could not be started: {ex.Message}");
        }
    }

    private static string ResolveBrowserExecutable(string browser, string? requested, List<string> issues)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var expanded = Environment.ExpandEnvironmentVariables(requested);
            if (File.Exists(expanded))
            {
                return Path.GetFullPath(expanded);
            }

            issues.Add($"Browser executable was supplied but not found: {requested}");
            return string.Empty;
        }

        var commandName = browser == "edge" ? "msedge.exe" : "chrome.exe";
        var fromPath = FindOnPath(commandName);
        if (!string.IsNullOrWhiteSpace(fromPath))
        {
            return fromPath;
        }

        foreach (var candidate in BrowserExecutableCandidates(browser))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        issues.Add($"Could not find {browser}. Re-run the launch script with -BrowserExe pointing at chrome.exe or msedge.exe.");
        return string.Empty;
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(part.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static IEnumerable<string> BrowserExecutableCandidates(string browser)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return browser == "edge"
            ? new[]
            {
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe")
            }
            : new[]
            {
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFiles, "Chromium", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Chromium", "Application", "chrome.exe")
            };
    }

    private static string BuildLaunchScript(
        string browser,
        string source,
        string startUrl,
        int remoteDebuggingPort)
    {
        return string.Join(
            Environment.NewLine,
            [
                "param(",
                "  [string]$BrowserExe = '',",
                $"  [string]$ProfileDir = (Join-Path $PSScriptRoot '{browser}-install-assist-profile'),",
                $"  [int]$RemoteDebuggingPort = {remoteDebuggingPort},",
                $"  [string]$StartUrl = {PowerShellSingleQuote(startUrl)},",
                "  [switch]$NoStart",
                ")",
                "$ErrorActionPreference = 'Stop'",
                $"$Browser = {PowerShellSingleQuote(browser)}",
                $"$ExtensionSource = {PowerShellSingleQuote(source)}",
                "$RunManifestPath = Join-Path $PSScriptRoot 'browser-launch-run.json'",
                "function Resolve-BrowserExecutable {",
                "  param([string]$BrowserName, [string]$Requested)",
                "  if (-not [string]::IsNullOrWhiteSpace($Requested)) {",
                "    $expanded = [Environment]::ExpandEnvironmentVariables($Requested)",
                "    if (Test-Path -LiteralPath $expanded) { return (Resolve-Path -LiteralPath $expanded).Path }",
                "    throw \"Browser executable was supplied but not found: $Requested\"",
                "  }",
                "  $commandName = if ($BrowserName -eq 'edge') { 'msedge.exe' } else { 'chrome.exe' }",
                "  $fromPath = Get-Command $commandName -ErrorAction SilentlyContinue",
                "  if ($fromPath) { return $fromPath.Source }",
                "  if ($BrowserName -eq 'edge') {",
                "    $candidates = @(",
                "      \"$env:ProgramFiles\\Microsoft\\Edge\\Application\\msedge.exe\",",
                "      \"${env:ProgramFiles(x86)}\\Microsoft\\Edge\\Application\\msedge.exe\",",
                "      \"$env:LOCALAPPDATA\\Microsoft\\Edge\\Application\\msedge.exe\"",
                "    )",
                "  } else {",
                "    $candidates = @(",
                "      \"$env:ProgramFiles\\Google\\Chrome\\Application\\chrome.exe\",",
                "      \"${env:ProgramFiles(x86)}\\Google\\Chrome\\Application\\chrome.exe\",",
                "      \"$env:LOCALAPPDATA\\Google\\Chrome\\Application\\chrome.exe\",",
                "      \"$env:ProgramFiles\\Chromium\\Application\\chrome.exe\",",
                "      \"${env:ProgramFiles(x86)}\\Chromium\\Application\\chrome.exe\"",
                "    )",
                "  }",
                "  foreach ($candidate in $candidates) {",
                "    if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) { return (Resolve-Path -LiteralPath $candidate).Path }",
                "  }",
                "  throw \"Could not find $BrowserName. Re-run with -BrowserExe pointing at chrome.exe or msedge.exe.\"",
                "}",
                "if ($Browser -notin @('chrome', 'edge')) { throw 'This launch helper supports Chrome or Edge only.' }",
                "if (-not (Test-Path -LiteralPath $ExtensionSource)) { throw \"Extension source not found: $ExtensionSource\" }",
                "if (-not (Test-Path -LiteralPath (Join-Path $ExtensionSource 'manifest.json'))) { throw \"Extension manifest not found under: $ExtensionSource\" }",
                "$ResolvedBrowserExe = Resolve-BrowserExecutable -BrowserName $Browser -Requested $BrowserExe",
                "$ProfileDirFull = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($ProfileDir))",
                "New-Item -ItemType Directory -Force -Path $ProfileDirFull | Out-Null",
                "$BrowserArgs = @(",
                "  \"--user-data-dir=`\"$ProfileDirFull`\"\",",
                "  \"--disable-extensions-except=`\"$ExtensionSource`\"\",",
                "  \"--load-extension=`\"$ExtensionSource`\"\",",
                "  '--no-first-run',",
                "  '--no-default-browser-check',",
                "  \"--remote-debugging-port=$RemoteDebuggingPort\",",
                "  $StartUrl",
                ")",
                "$RunManifest = [ordered]@{",
                "  generatedAt = (Get-Date).ToUniversalTime().ToString('o')",
                "  browser = $Browser",
                "  browserExe = $ResolvedBrowserExe",
                "  profileDir = $ProfileDirFull",
                "  extensionSource = $ExtensionSource",
                "  startUrl = $StartUrl",
                "  remoteDebuggingPort = $RemoteDebuggingPort",
                "  remoteDebuggingUrl = \"http://127.0.0.1:$RemoteDebuggingPort/json/version\"",
                "  arguments = $BrowserArgs",
                "  proofBoundary = 'Temporary isolated-profile extension loading only; no browser-store publication, signing, review, availability, permanent profile mutation, or enterprise force-install proof.'",
                "}",
                "$RunManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $RunManifestPath -Encoding UTF8",
                "Write-Host \"Browser launch manifest written to $RunManifestPath\"",
                "Write-Host \"Profile: $ProfileDirFull\"",
                "Write-Host \"Start URL: $StartUrl\"",
                "if ($NoStart) { return }",
                "Start-Process -FilePath $ResolvedBrowserExe -ArgumentList $BrowserArgs | Out-Null"
            ]);
    }

    private static string BuildMarkdown(BrowserExtensionInstallAssistResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Receipts Browser Extension Install Assist");
        builder.AppendLine();
        builder.AppendLine($"Browser: `{(string.IsNullOrWhiteSpace(result.Browser) ? "invalid" : result.Browser)}`");
        builder.AppendLine($"Status: `{(result.Issues.Count == 0 ? "ready" : "blocked")}`");
        builder.AppendLine($"Mode: `{result.Mode}`");
        builder.AppendLine($"Source: `{result.SourceDirectory}`");
        builder.AppendLine($"Source exists: `{result.SourceExists}`");
        builder.AppendLine($"Output: `{result.OutputPath}`");
        builder.AppendLine($"Supports automated launch: `{result.SupportsAutomatedLaunch}`");
        builder.AppendLine($"Profile directory: `{(string.IsNullOrWhiteSpace(result.ProfileDirectory) ? "not applicable" : result.ProfileDirectory)}`");
        builder.AppendLine($"Start URL: `{result.StartUrl}`");
        builder.AppendLine($"Remote debugging URL: `{result.RemoteDebuggingUrl}`");
        builder.AppendLine();
        AppendList(builder, "Issues", result.Issues);
        AppendList(builder, "Warnings", result.Warnings);
        AppendList(builder, "Commands", result.Commands);
        AppendList(builder, "Browser Arguments", result.BrowserArguments);
        AppendList(builder, "Non-Goals", result.NonGoals);
        builder.AppendLine("## Mutation Flags");
        builder.AppendLine();
        builder.AppendLine($"- Starts browser: `{result.MutationFlags.StartsBrowser}`");
        builder.AppendLine($"- Mutates existing browser profile: `{result.MutationFlags.MutatesExistingBrowserProfile}`");
        builder.AppendLine($"- Installs extension permanently: `{result.MutationFlags.InstallsExtensionPermanently}`");
        builder.AppendLine($"- Contacts browser store account: `{result.MutationFlags.ContactsBrowserStoreAccount}`");
        builder.AppendLine($"- Writes registry or enterprise policy: `{result.MutationFlags.WritesRegistryOrEnterprisePolicy}`");
        builder.AppendLine();
        builder.AppendLine("## Boundary");
        builder.AppendLine();
        builder.AppendLine(result.SupportsAutomatedLaunch
            ? "This helper can load the unpacked extension into a temporary Chrome/Edge profile with command-line flags. It does not prove browser-store review, signing, listing availability, permanent installation, enterprise force-install, or native-host reachability."
            : "This helper writes manual browser steps only. Browser add-on installation remains controlled by the browser, browser store, or enterprise policy.");
        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        if (values.Count == 0)
        {
            builder.AppendLine("- None.");
            builder.AppendLine();
            return;
        }

        foreach (var value in values)
        {
            builder.AppendLine($"- {value}");
        }

        builder.AppendLine();
    }

    private static IReadOnlyList<string> BuildNonGoals() => new[]
    {
        "Does not publish, upload, sign, review, or make the extension available in any browser store.",
        "Does not install the extension permanently into an existing browser profile.",
        "Does not write enterprise force-install policy, registry keys, native-host registrations, or browser preferences.",
        "Does not prove browser-to-native Host Status; collect that separately from the extension popup/options UI."
    };

    private static string Quote(string value) => $"\"{value}\"";

    private static string PowerShellSingleQuote(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }
}

public sealed class BrowserExtensionInstallAssistRequest
{
    public string? Browser { get; set; }
    public string? ExtensionSourceDirectory { get; set; }
    public string? OutputPath { get; set; }
    public string? StartUrl { get; set; }
    public int? RemoteDebuggingPort { get; set; }
    public bool StartBrowser { get; set; }
    public string? BrowserExecutablePath { get; set; }
    public bool PolicyAllowed { get; set; } = true;
    public string? PolicyReason { get; set; }
}

public sealed class BrowserExtensionInstallAssistResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Browser { get; set; } = string.Empty;
    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public bool PolicyAllowed { get; set; }
    public string PolicyReason { get; set; } = string.Empty;
    public bool SourceExists { get; set; }
    public bool SupportsAutomatedLaunch { get; set; }
    public string Mode { get; set; } = string.Empty;
    public bool Started { get; set; }
    public int? StartedProcessId { get; set; }
    public string StartUrl { get; set; } = string.Empty;
    public int RemoteDebuggingPort { get; set; }
    public string RemoteDebuggingUrl { get; set; } = string.Empty;
    public string ProfileDirectory { get; set; } = string.Empty;
    public string BrowserExecutablePath { get; set; } = string.Empty;
    public string LaunchScriptPath { get; set; } = string.Empty;
    public string LaunchPlanPath { get; set; } = string.Empty;
    public string AssistGuidePath { get; set; } = string.Empty;
    public string AssistJsonPath { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public BrowserExtensionInstallAssistMutationFlags MutationFlags { get; set; } = new();
    public List<string> BrowserArguments { get; set; } = new();
    public List<string> Commands { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> NonGoals { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class BrowserExtensionInstallAssistLaunchPlan
{
    public string Browser { get; set; } = string.Empty;
    public bool SupportsAutomatedLaunch { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string LaunchScriptPath { get; set; } = string.Empty;
    public string ProfileDirectory { get; set; } = string.Empty;
    public string ExtensionSourceDirectory { get; set; } = string.Empty;
    public string StartUrl { get; set; } = string.Empty;
    public int RemoteDebuggingPort { get; set; }
    public string RemoteDebuggingUrl { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public List<string> BrowserArguments { get; set; } = new();
    public BrowserExtensionInstallAssistMutationFlags MutationFlags { get; set; } = new();
    public string ProofBoundary { get; set; } = string.Empty;
}

public sealed class BrowserExtensionInstallAssistMutationFlags
{
    public bool StartsBrowser { get; set; }
    public bool MutatesExistingBrowserProfile { get; set; }
    public bool InstallsExtensionPermanently { get; set; }
    public bool ContactsBrowserStoreAccount { get; set; }
    public bool WritesRegistryOrEnterprisePolicy { get; set; }
}
