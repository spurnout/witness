using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionLiveFixtureProofService
{
    public const int DefaultFixturePort = 58615;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;
    private readonly BrowserExtensionNativeBridgeService _bridge;

    public BrowserExtensionLiveFixtureProofService(
        AppPaths paths,
        BrowserExtensionNativeBridgeService bridge)
    {
        _paths = paths;
        _bridge = bridge;
    }

    public async Task<BrowserExtensionLiveFixtureProofResult> CreateAsync(
        BrowserExtensionLiveFixtureProofRequest request,
        BrowserNativeHostStatus nativeHostStatus,
        CancellationToken cancellationToken = default)
    {
        var generatedAt = DateTimeOffset.Now;
        var date = request.Date ?? DateOnly.FromDateTime(generatedAt.LocalDateTime);
        var root = ResolveRoot(request.OutputPath, date);
        Directory.CreateDirectory(root);

        var source = ResolveExtensionSource(request.ExtensionSourceDirectory);
        var browser = NormalizeBrowser(request.Browser);
        var fixtureUrl = string.IsNullOrWhiteSpace(request.FixtureUrl)
            ? $"http://127.0.0.1:{DefaultFixturePort}/safe-fixture.html"
            : request.FixtureUrl.Trim();
        var hostExecutable = string.IsNullOrWhiteSpace(request.HostExecutablePath)
            ? Path.Combine(AppContext.BaseDirectory, "GoatShot.Cli.exe")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.HostExecutablePath));
        var nativeHostInstallCommand = BuildNativeHostInstallCommand(browser, request.ExtensionId, hostExecutable);
        var diagnostics = new BrowserExtensionOperatorDiagnosticsService(_paths).Build(new BrowserExtensionOperatorDiagnosticsRequest
        {
            ExtensionSourceDirectory = source,
            NativeHostStatus = nativeHostStatus
        });

        var warnings = diagnostics.Warnings.ToList();
        var issues = diagnostics.ExtensionSourceExists
            ? new List<string>()
            : diagnostics.Issues.ToList();
        if (string.IsNullOrWhiteSpace(request.ExtensionId) &&
            (browser.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
             browser.Equals("edge", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add($"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(browser)} native-host install command needs the browser-generated extension id after Load unpacked succeeds.");
        }

        var notesPath = Path.Combine(root, "browser-extension-live-fixture.md");
        var commandsPath = Path.Combine(root, "commands.md");
        var serverScriptPath = Path.Combine(root, "serve-safe-fixture.ps1");
        var browserLaunchScriptPath = Path.Combine(root, "launch-browser-fixture.ps1");
        var browserLaunchPlanPath = Path.Combine(root, "browser-launch-plan.json");
        var browserLaunchPlan = BuildBrowserLaunchPlan(root, source, browser, fixtureUrl, browserLaunchScriptPath, browserLaunchPlanPath);
        WriteText(notesPath, BuildNotes(date, generatedAt, source, browser, fixtureUrl, nativeHostInstallCommand, diagnostics, request, browserLaunchPlan), request.Force);
        WriteText(commandsPath, BuildCommands(source, browser, fixtureUrl, nativeHostInstallCommand, request, browserLaunchPlan), request.Force);
        WriteText(serverScriptPath, BuildServerScript(source), request.Force);
        WriteText(browserLaunchScriptPath, BuildBrowserLaunchScript(source, browser, fixtureUrl), request.Force);
        WriteText(browserLaunchPlanPath, JsonSerializer.Serialize(browserLaunchPlan, JsonOptions), request.Force);

        BrowserExtensionLiveFixtureVerificationResult? verification = null;
        if (!string.IsNullOrWhiteSpace(request.PayloadPath) ||
            !string.IsNullOrWhiteSpace(request.StitchPackagePath))
        {
            verification = await VerifyAsync(root, request, cancellationToken);
            if (!verification.Succeeded)
            {
                issues.Add(verification.Message);
                issues.AddRange(verification.Issues);
            }

            warnings.AddRange(verification.Warnings);
        }
        else
        {
            warnings.Add("No payload/stitch-package paths were supplied; live fixture package import verification was not run.");
        }

        var proofValidation = await new BrowserExtensionProofManifestService().ValidateAsync(
            new BrowserExtensionProofValidationRequest
            {
                ProofRootPath = root,
                IgnoreExistingManifest = true,
                ExtensionSourceDirectory = source,
                Browser = browser,
                ExtensionId = request.ExtensionId,
                FixtureUrl = fixtureUrl,
                PayloadPath = request.PayloadPath,
                RedactedPayloadPath = verification?.RedactedPayloadPath,
                StitchPackagePath = request.StitchPackagePath,
                ImportResultPath = verification?.ImportResultPath
            },
            nativeHostStatus,
            cancellationToken);
        if (!proofValidation.Succeeded)
        {
            warnings.Add($"Browser proof manifest is incomplete; see {proofValidation.ValidationPath}.");
        }

        var diagnosticsPath = Path.Combine(root, "browser-extension-diagnostics.json");
        var result = new BrowserExtensionLiveFixtureProofResult
        {
            Succeeded = issues.Count == 0,
            RootPath = root,
            NotesPath = notesPath,
            CommandsPath = commandsPath,
            ServerScriptPath = serverScriptPath,
            BrowserLaunchScriptPath = browserLaunchScriptPath,
            BrowserLaunchPlanPath = browserLaunchPlanPath,
            DiagnosticsPath = diagnosticsPath,
            ExtensionSourceDirectory = source,
            SafeFixturePath = diagnostics.SafeFixturePath,
            SafeFixtureUrl = fixtureUrl,
            Browser = browser,
            BrowserLaunch = browserLaunchPlan,
            NativeHostInstallCommand = nativeHostInstallCommand,
            Verification = verification,
            ProofValidation = proofValidation,
            ProofManifestPath = proofValidation.ManifestPath,
            ProofValidationPath = proofValidation.ValidationPath,
            Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
        result.Message = result.Succeeded
            ? $"Browser extension live-fixture proof folder ready: {root}"
            : $"Browser extension live-fixture proof folder created with issue(s): {root}";

        await File.WriteAllTextAsync(
            diagnosticsPath,
            JsonSerializer.Serialize(new
            {
                generatedAt,
                result.ExtensionSourceDirectory,
                result.SafeFixturePath,
                result.SafeFixtureUrl,
                result.Browser,
                browserLaunch = result.BrowserLaunch,
                result.NativeHostInstallCommand,
                nativeHostStatus,
                diagnostics,
                verification = result.Verification,
                proofValidation = result.ProofValidation,
                result.Issues,
                result.Warnings
            }, JsonOptions),
            cancellationToken);

        return result;
    }

    private async Task<BrowserExtensionLiveFixtureVerificationResult> VerifyAsync(
        string root,
        BrowserExtensionLiveFixtureProofRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PayloadPath) ||
            string.IsNullOrWhiteSpace(request.StitchPackagePath))
        {
            return new BrowserExtensionLiveFixtureVerificationResult
            {
                Succeeded = false,
                Message = "Both --payload and --stitch-package are required to verify a live fixture package import.",
                Issues = new List<string>
                {
                    "Missing payload or stitch-package path."
                }
            };
        }

        var importResultPath = Path.Combine(root, "import-result.json");
        var redactedPayloadPath = Path.Combine(root, "redacted-payload.json");
        var bridgeResult = await _bridge.AcceptAsync(new BrowserExtensionBridgeRequest
        {
            PayloadPath = request.PayloadPath,
            StitchPackagePath = request.StitchPackagePath,
            RedactedOutputPath = redactedPayloadPath
        }, cancellationToken);

        var summary = new BrowserExtensionLiveFixtureVerificationResult
        {
            Succeeded = bridgeResult.Succeeded,
            Message = bridgeResult.Message,
            ImportResultPath = importResultPath,
            RedactedPayloadPath = bridgeResult.RedactedPayloadPath,
            StitchPackagePath = bridgeResult.StitchPackagePath,
            WorkspaceFilePath = bridgeResult.Item?.FilePath ?? string.Empty,
            Issues = bridgeResult.Issues,
            Warnings = bridgeResult.Warnings
        };

        await File.WriteAllTextAsync(
            importResultPath,
            JsonSerializer.Serialize(summary, JsonOptions),
            cancellationToken);

        return summary;
    }

    private static string ResolveRoot(string? outputPath, DateOnly date)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        }

        return Path.GetFullPath(Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            "manual-validation",
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "browser-extension-live-fixture"));
    }

    private static string ResolveExtensionSource(string? requestedSource)
    {
        if (!string.IsNullOrWhiteSpace(requestedSource))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedSource));
        }

        var packaged = Path.Combine(AppContext.BaseDirectory, "browser-extension");
        if (Directory.Exists(packaged))
        {
            return packaged;
        }

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "browser-extension"));
    }

    private static string NormalizeBrowser(string? browser)
    {
        browser = (browser ?? "chrome").Trim().ToLowerInvariant();
        browser = browser.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        return browser switch
        {
            "chrome" or "chromium" => "chrome",
            "edge" or "msedge" => "edge",
            "firefox" or "mozilla" => "firefox",
            _ => "chrome"
        };
    }

    private static string BuildNativeHostInstallCommand(
        string browser,
        string? extensionId,
        string hostExecutable)
    {
        var idFlag = browser switch
        {
            "edge" => "--edge-extension-id",
            "firefox" => "--firefox-extension-id",
            _ => "--chrome-extension-id"
        };
        var idValue = string.IsNullOrWhiteSpace(extensionId)
            ? "<extension-id>"
            : extensionId.Trim();
        return $"GoatShot.Cli.exe browser-extension native-host install --browser {browser} {idFlag} {Quote(idValue)} --host-exe {Quote(hostExecutable)} --json";
    }

    private static string BuildNotes(
        DateOnly date,
        DateTimeOffset generatedAt,
        string source,
        string browser,
        string fixtureUrl,
        string nativeHostInstallCommand,
        BrowserExtensionOperatorDiagnostics diagnostics,
        BrowserExtensionLiveFixtureProofRequest request,
        BrowserExtensionLiveFixtureLaunchPlan browserLaunchPlan)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Browser Extension Live Fixture Proof");
        builder.AppendLine();
        builder.AppendLine($"Date: {date:yyyy-MM-dd}");
        builder.AppendLine($"Generated at: {generatedAt:O}");
        builder.AppendLine();
        builder.AppendLine("## Scope");
        builder.AppendLine();
        builder.AppendLine("Use only safe local browser content. This lane proves unpacked extension loading, consent UI, native-host reachability, browser-download stitch-package export, and native package import for one tested browser. It does not prove browser-store publication or automatic installation.");
        builder.AppendLine();
        builder.AppendLine("## Paths");
        builder.AppendLine();
        builder.AppendLine($"- Browser: `{browser}`");
        builder.AppendLine($"- Extension source: `{source}`");
        builder.AppendLine($"- Safe fixture file: `{diagnostics.SafeFixturePath}`");
        builder.AppendLine($"- Safe fixture URL: `{fixtureUrl}`");
        builder.AppendLine($"- Browser launch script: `{browserLaunchPlan.LaunchScriptPath}`");
        builder.AppendLine($"- Isolated browser profile: `{browserLaunchPlan.ProfileDirectory}`");
        builder.AppendLine();
        builder.AppendLine("## Checklist");
        builder.AppendLine();
        builder.AppendLine("- [ ] Run `serve-safe-fixture.ps1` or another local static server for `browser-extension/samples/`.");
        if (browserLaunchPlan.SupportsAutomatedLaunch)
        {
            builder.AppendLine("- [ ] Run `launch-browser-fixture.ps1` to open an isolated Chrome/Edge profile with the extension source preloaded against the safe fixture.");
        }
        else
        {
            builder.AppendLine("- [ ] Open the target browser manually. This helper's automated launch script is designed for Chrome/Edge command-line extension loading.");
        }

        builder.AppendLine($"- [ ] Confirm `{fixtureUrl}` opens in the target browser.");
        builder.AppendLine($"- [ ] If browser policy blocks command-line extension loading, open `{browser}` extension management and load the unpacked extension folder manually.");
        builder.AppendLine($"- [ ] Chrome fallback: open `chrome://extensions`, enable Developer mode, choose Load unpacked, and select `{source}`.");
        builder.AppendLine($"- [ ] Edge fallback: open `edge://extensions`, enable Developer mode, choose Load unpacked, and select `{source}`.");
        builder.AppendLine("- [ ] Save a screenshot of the loaded extension details page.");
        builder.AppendLine("- [ ] Copy the browser-generated extension id.");
        builder.AppendLine("- [ ] Install the native host with the command below, replacing `<extension-id>` if needed.");
        builder.AppendLine("- [ ] Save popup consent defaults, options consent defaults, Host Status, selected-element mode, package-export toggle, and last handoff screenshots.");
        builder.AppendLine("- [ ] Run a consented fixture capture with screenshot consent and package export enabled.");
        builder.AppendLine("- [ ] Run the package import verifier command in `commands.md` with the downloaded package folder.");
        builder.AppendLine();
        builder.AppendLine("## Native Host Command");
        builder.AppendLine();
        builder.AppendLine("```powershell");
        builder.AppendLine(nativeHostInstallCommand);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Current Diagnostic Snapshot");
        builder.AppendLine();
        foreach (var entry in diagnostics.Entries)
        {
            var browserText = string.IsNullOrWhiteSpace(entry.Browser) ? string.Empty : $" ({entry.Browser})";
            builder.AppendLine($"- `{entry.Code}`{browserText}: {entry.Status} - {entry.Message}");
        }

        builder.AppendLine();
        builder.AppendLine("## Verifier Inputs");
        builder.AppendLine();
        builder.AppendLine($"- Payload: `{request.PayloadPath ?? "not supplied"}`");
        builder.AppendLine($"- Stitch package: `{request.StitchPackagePath ?? "not supplied"}`");
        builder.AppendLine();
        builder.AppendLine("## Result");
        builder.AppendLine();
        builder.AppendLine("- [ ] Pending");
        builder.AppendLine("- [ ] Passed");
        builder.AppendLine("- [ ] Failed");
        builder.AppendLine("- [ ] Blocked");
        builder.AppendLine();
        builder.AppendLine("## Evidence");
        builder.AppendLine();
        builder.AppendLine("- Extension details screenshot:");
        builder.AppendLine("- Browser launch run manifest:");
        builder.AppendLine("- Popup/options screenshots:");
        builder.AppendLine("- Host Status screenshot:");
        builder.AppendLine("- Downloaded package folder:");
        builder.AppendLine("- Import result JSON:");
        builder.AppendLine("- Limitations:");
        return builder.ToString();
    }

    private static string BuildCommands(
        string source,
        string browser,
        string fixtureUrl,
        string nativeHostInstallCommand,
        BrowserExtensionLiveFixtureProofRequest request,
        BrowserExtensionLiveFixtureLaunchPlan browserLaunchPlan)
    {
        var payload = string.IsNullOrWhiteSpace(request.PayloadPath)
            ? "<downloaded-or-exported-payload.json>"
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.PayloadPath));
        var package = string.IsNullOrWhiteSpace(request.StitchPackagePath)
            ? "<downloaded-GoatShot-correlationId-folder>"
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.StitchPackagePath));
        return string.Join(
            Environment.NewLine,
            [
                "# Browser Extension Live Fixture Commands",
                string.Empty,
                "Run from the proof folder unless a command says otherwise.",
                string.Empty,
                "```powershell",
                ".\\serve-safe-fixture.ps1",
                "```",
                string.Empty,
                "Launch an isolated browser profile with the extension source preloaded:",
                string.Empty,
                "```powershell",
                ".\\launch-browser-fixture.ps1",
                "```",
                string.Empty,
                $"This opens `{fixtureUrl}` in {browser} with a temporary profile under `{browserLaunchPlan.ProfileDirectory}`.",
                "If browser policy blocks command-line extension loading, use the manual Load unpacked fallback below and record the blocker in the notes.",
                string.Empty,
                "The launch helper writes `browser-launch-run.json` with the resolved browser executable, profile path, remote debugging URL, and command-line arguments used.",
                string.Empty,
                "Load unpacked extension folder:",
                string.Empty,
                "```text",
                source,
                "```",
                string.Empty,
                "Manual Chrome fallback:",
                string.Empty,
                "```text",
                "chrome://extensions -> Developer mode -> Load unpacked -> choose the extension source folder above",
                "```",
                string.Empty,
                "Manual Edge fallback:",
                string.Empty,
                "```text",
                "edge://extensions -> Developer mode -> Load unpacked -> choose the extension source folder above",
                "```",
                string.Empty,
                "Install native host after copying the browser-generated extension id:",
                string.Empty,
                "```powershell",
                nativeHostInstallCommand,
                "```",
                string.Empty,
                "Save diagnostics:",
                string.Empty,
                "```powershell",
                $"GoatShot.Cli.exe browser-extension diagnostics --source {Quote(source)} --json *> .\\browser-extension-diagnostics.json",
                "GoatShot.Cli.exe browser-extension native-host status --json *> .\\native-host-status.json",
                "```",
                string.Empty,
                "Verify/import the downloaded stitch package:",
                string.Empty,
                "```powershell",
                $"GoatShot.Cli.exe browser-extension live-fixture --browser {browser} --source {Quote(source)} --payload {Quote(payload)} --stitch-package {Quote(package)} --output . --force --json *> .\\live-fixture-verify.json",
                "```",
                string.Empty,
                "Validate the completed proof folder after screenshots and browser version are recorded:",
                string.Empty,
                "```powershell",
                $"GoatShot.Cli.exe browser-extension proof validate --folder . --source {Quote(source)} --browser {browser} --payload {Quote(payload)} --redacted-payload .\\redacted-payload.json --stitch-package {Quote(package)} --import-result .\\import-result.json --json *> .\\browser-proof-validation-cli.json",
                "```"
            ]);
    }

    private static BrowserExtensionLiveFixtureLaunchPlan BuildBrowserLaunchPlan(
        string root,
        string source,
        string browser,
        string fixtureUrl,
        string launchScriptPath,
        string launchPlanPath)
    {
        var profileDirectory = Path.Combine(root, $"{browser}-live-fixture-profile");
        var remoteDebuggingPort = DefaultFixturePort + 1;
        var supportsAutomatedLaunch = browser is "chrome" or "edge";
        var arguments = supportsAutomatedLaunch
            ? new List<string>
            {
                $"--user-data-dir={profileDirectory}",
                $"--disable-extensions-except={source}",
                $"--load-extension={source}",
                "--no-first-run",
                "--no-default-browser-check",
                $"--remote-debugging-port={remoteDebuggingPort}",
                fixtureUrl
            }
            : new List<string>();

        return new BrowserExtensionLiveFixtureLaunchPlan
        {
            Browser = browser,
            SupportsAutomatedLaunch = supportsAutomatedLaunch,
            LaunchScriptPath = launchScriptPath,
            LaunchPlanPath = launchPlanPath,
            ProfileDirectory = profileDirectory,
            ExtensionSourceDirectory = source,
            FixtureUrl = fixtureUrl,
            RemoteDebuggingPort = remoteDebuggingPort,
            RemoteDebuggingUrl = $"http://127.0.0.1:{remoteDebuggingPort}/json/version",
            Command = ".\\launch-browser-fixture.ps1",
            BrowserArguments = arguments,
            ProofBoundary = supportsAutomatedLaunch
                ? "Starts Chrome/Edge with an isolated local profile and unpacked extension source. This does not install the extension, publish to a store, or prove browser-store review."
                : "Automated launch is not available for this browser in the helper. Use manual browser loading and record evidence."
        };
    }

    private static string BuildServerScript(string source)
    {
        var sampleRoot = Path.Combine(source, "samples");
        var escapedRoot = sampleRoot.Replace("'", "''", StringComparison.Ordinal);
        return string.Join(
            Environment.NewLine,
            [
                "param([int]$Port = 58615)",
                "$ErrorActionPreference = 'Stop'",
                $"$Root = '{escapedRoot}'",
                "if (-not (Test-Path -LiteralPath $Root)) { throw \"Safe fixture folder not found: $Root\" }",
                "$Listener = [System.Net.HttpListener]::new()",
                "$Prefix = \"http://127.0.0.1:$Port/\"",
                "$Listener.Prefixes.Add($Prefix)",
                "$Listener.Start()",
                "Write-Host \"Serving $Root at ${Prefix}safe-fixture.html. Press Ctrl+C to stop.\"",
                "try {",
                "  while ($Listener.IsListening) {",
                "    $Context = $Listener.GetContext()",
                "    $Name = [System.Uri]::UnescapeDataString($Context.Request.Url.AbsolutePath.TrimStart('/'))",
                "    if ([string]::IsNullOrWhiteSpace($Name)) { $Name = 'safe-fixture.html' }",
                "    $Candidate = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Root, $Name))",
                "    $RootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\\', '/') + [System.IO.Path]::DirectorySeparatorChar",
                "    if (-not $Candidate.StartsWith($RootFull, [System.StringComparison]::OrdinalIgnoreCase) -or -not [System.IO.File]::Exists($Candidate)) {",
                "      $Context.Response.StatusCode = 404",
                "      $Bytes = [System.Text.Encoding]::UTF8.GetBytes('Not found')",
                "    } else {",
                "      $Context.Response.StatusCode = 200",
                "      $Context.Response.ContentType = if ($Candidate.EndsWith('.html')) { 'text/html; charset=utf-8' } elseif ($Candidate.EndsWith('.js')) { 'text/javascript; charset=utf-8' } else { 'application/octet-stream' }",
                "      $Bytes = [System.IO.File]::ReadAllBytes($Candidate)",
                "    }",
                "    $Context.Response.ContentLength64 = $Bytes.Length",
                "    $Context.Response.OutputStream.Write($Bytes, 0, $Bytes.Length)",
                "    $Context.Response.Close()",
                "  }",
                "} finally {",
                "  $Listener.Stop()",
                "  $Listener.Close()",
                "}"
            ]);
    }

    private static string BuildBrowserLaunchScript(
        string source,
        string browser,
        string fixtureUrl)
    {
        return string.Join(
            Environment.NewLine,
            [
                "param(",
                "  [string]$BrowserExe = '',",
                $"  [string]$ProfileDir = (Join-Path $PSScriptRoot '{browser}-live-fixture-profile'),",
                $"  [int]$RemoteDebuggingPort = {DefaultFixturePort + 1},",
                "  [switch]$NoStart",
                ")",
                "$ErrorActionPreference = 'Stop'",
                $"$Browser = {PowerShellSingleQuote(browser)}",
                $"$ExtensionSource = {PowerShellSingleQuote(source)}",
                $"$FixtureUrl = {PowerShellSingleQuote(fixtureUrl)}",
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
                "if ($Browser -notin @('chrome', 'edge')) {",
                "  throw 'This launch helper uses Chromium --load-extension flags and supports Chrome or Edge. Use commands.md for manual browser loading.'",
                "}",
                "if (-not (Test-Path -LiteralPath $ExtensionSource)) { throw \"Extension source not found: $ExtensionSource\" }",
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
                "  $FixtureUrl",
                ")",
                "$RunManifest = [ordered]@{",
                "  generatedAt = (Get-Date).ToUniversalTime().ToString('o')",
                "  browser = $Browser",
                "  browserExe = $ResolvedBrowserExe",
                "  profileDir = $ProfileDirFull",
                "  extensionSource = $ExtensionSource",
                "  fixtureUrl = $FixtureUrl",
                "  remoteDebuggingPort = $RemoteDebuggingPort",
                "  remoteDebuggingUrl = \"http://127.0.0.1:$RemoteDebuggingPort/json/version\"",
                "  arguments = $BrowserArgs",
                "  proofBoundary = 'Isolated-profile command-line extension loading only; no browser-store publication, signing, review, or automatic installation proof.'",
                "}",
                "$RunManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $RunManifestPath -Encoding UTF8",
                "Write-Host \"Browser launch manifest written to $RunManifestPath\"",
                "Write-Host \"Fixture URL: $FixtureUrl\"",
                "Write-Host \"Profile: $ProfileDirFull\"",
                "if ($NoStart) { return }",
                "Start-Process -FilePath $ResolvedBrowserExe -ArgumentList $BrowserArgs | Out-Null"
            ]);
    }

    private static void WriteText(string path, string content, bool force)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path) || force)
        {
            File.WriteAllText(path, content, Encoding.UTF8);
        }
    }

    private static string Quote(string value)
    {
        return value.Contains(" ", StringComparison.Ordinal) ||
               value.Contains("<", StringComparison.Ordinal) ||
               value.Contains(">", StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static string PowerShellSingleQuote(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }
}

public sealed class BrowserExtensionLiveFixtureProofRequest
{
    public string? OutputPath { get; set; }
    public DateOnly? Date { get; set; }
    public string? ExtensionSourceDirectory { get; set; }
    public string? Browser { get; set; }
    public string? ExtensionId { get; set; }
    public string? HostExecutablePath { get; set; }
    public string? FixtureUrl { get; set; }
    public string? PayloadPath { get; set; }
    public string? StitchPackagePath { get; set; }
    public bool Force { get; set; }
}

public sealed class BrowserExtensionLiveFixtureProofResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string NotesPath { get; set; } = string.Empty;
    public string CommandsPath { get; set; } = string.Empty;
    public string ServerScriptPath { get; set; } = string.Empty;
    public string BrowserLaunchScriptPath { get; set; } = string.Empty;
    public string BrowserLaunchPlanPath { get; set; } = string.Empty;
    public string DiagnosticsPath { get; set; } = string.Empty;
    public string ExtensionSourceDirectory { get; set; } = string.Empty;
    public string SafeFixturePath { get; set; } = string.Empty;
    public string SafeFixtureUrl { get; set; } = string.Empty;
    public string Browser { get; set; } = string.Empty;
    public BrowserExtensionLiveFixtureLaunchPlan BrowserLaunch { get; set; } = new();
    public string NativeHostInstallCommand { get; set; } = string.Empty;
    public BrowserExtensionLiveFixtureVerificationResult? Verification { get; set; }
    public BrowserExtensionProofValidationResult? ProofValidation { get; set; }
    public string ProofManifestPath { get; set; } = string.Empty;
    public string ProofValidationPath { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class BrowserExtensionLiveFixtureLaunchPlan
{
    public string Browser { get; set; } = string.Empty;
    public bool SupportsAutomatedLaunch { get; set; }
    public string LaunchScriptPath { get; set; } = string.Empty;
    public string LaunchPlanPath { get; set; } = string.Empty;
    public string ProfileDirectory { get; set; } = string.Empty;
    public string ExtensionSourceDirectory { get; set; } = string.Empty;
    public string FixtureUrl { get; set; } = string.Empty;
    public int RemoteDebuggingPort { get; set; }
    public string RemoteDebuggingUrl { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public List<string> BrowserArguments { get; set; } = new();
    public string ProofBoundary { get; set; } = string.Empty;
}

public sealed class BrowserExtensionLiveFixtureVerificationResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ImportResultPath { get; set; } = string.Empty;
    public string RedactedPayloadPath { get; set; } = string.Empty;
    public string StitchPackagePath { get; set; } = string.Empty;
    public string WorkspaceFilePath { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
