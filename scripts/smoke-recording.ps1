param(
    [string[]] $Targets = @("fixed"),
    [double] $DurationSeconds = 2,
    [int] $Fps = 5,
    [ValidateSet("mp4", "gif")]
    [string] $Format = "mp4",
    [string] $OutputRoot = "",
    [string] $Configuration = "Release",
    [string] $CliPath = "",
    [switch] $NoBuild,
    [switch] $AllowLiveDesktopCapture,
    [ValidateSet("none", "mic-muted", "system-audio-muted", "both-muted", "mic", "system-audio", "both")]
    [string] $Audio = "none",
    [switch] $AllowLiveAudioCapture,
    [double] $MaxAudioVideoDeltaSeconds = 0.75,
    [switch] $PlanOnly,
    [switch] $RequireFfprobe
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\tranche-recording-smoke"
}
elseif ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
}
else {
    $OutputRoot = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputRoot))
}

$DurationSeconds = [Math]::Max(0.25, [Math]::Min(180, $DurationSeconds))
$Fps = [Math]::Max(1, [Math]::Min(60, $Fps))
$MaxAudioVideoDeltaSeconds = [Math]::Max(0.05, $MaxAudioVideoDeltaSeconds)

$localRoot = Join-Path $OutputRoot "local"
$libraryRoot = Join-Path $OutputRoot "library"
$recordingRoot = Join-Path $OutputRoot "recordings"
$logRoot = Join-Path $OutputRoot "logs"
$diagnosticsRoot = Join-Path $OutputRoot "diagnostics"
New-Item -ItemType Directory -Force -Path $OutputRoot, $localRoot, $libraryRoot, $recordingRoot, $logRoot, $diagnosticsRoot | Out-Null

$useDefaultCliPath = [string]::IsNullOrWhiteSpace($CliPath)
$cliBuildRoot = Join-Path $repoRoot "src\GoatShot.Cli\bin\$Configuration\net10.0-windows10.0.19041.0"
$receiptsCliPath = Join-Path $cliBuildRoot "Receipts.Cli.exe"
$legacyCliPath = Join-Path $cliBuildRoot "GoatShot.Cli.exe"
if ($useDefaultCliPath) {
    $CliPath = if (Test-Path -LiteralPath $receiptsCliPath -PathType Leaf) {
        $receiptsCliPath
    }
    elseif (Test-Path -LiteralPath $legacyCliPath -PathType Leaf) {
        $legacyCliPath
    }
    else {
        $receiptsCliPath
    }
}

if (-not $NoBuild) {
    dotnet build (Join-Path $repoRoot "GoatShot.slnx") -c $Configuration | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE; refusing to smoke-test a stale CLI binary."
    }
}

if ($useDefaultCliPath) {
    $CliPath = if (Test-Path -LiteralPath $receiptsCliPath -PathType Leaf) {
        $receiptsCliPath
    }
    elseif (Test-Path -LiteralPath $legacyCliPath -PathType Leaf) {
        $legacyCliPath
    }
    else {
        $receiptsCliPath
    }
}

if (-not (Test-Path $CliPath)) {
    throw "Receipts CLI was not found at $CliPath. Build first or pass -CliPath (legacy GoatShot.Cli.exe inputs remain accepted)."
}

$normalizedTargets = @()
foreach ($target in $Targets) {
    if ($target.Equals("all", [StringComparison]::OrdinalIgnoreCase)) {
        $normalizedTargets += @("fixed", "monitor", "window", "all-monitors")
        continue
    }

    $normalizedTargets += $target.ToLowerInvariant().Replace("allmonitors", "all-monitors")
}

$supportedTargets = @("fixed", "monitor", "window", "all-monitors")
foreach ($target in $normalizedTargets) {
    if ($supportedTargets -notcontains $target) {
        throw "Unsupported recording smoke target '$target'. Use fixed, monitor, window, all-monitors, or all."
    }

    if (($target -eq "fixed" -or $target -eq "monitor" -or $target -eq "window" -or $target -eq "all-monitors") -and
        -not $AllowLiveDesktopCapture -and
        -not $PlanOnly) {
        throw "Recording smoke target '$target' captures live desktop content. Prepare a safe desktop/foreground window and pass -AllowLiveDesktopCapture to run it."
    }
}

$audioRequested = -not $Audio.Equals("none", [StringComparison]::OrdinalIgnoreCase)
if ($audioRequested -and -not $Format.Equals("mp4", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Recording smoke audio validation is only supported for MP4 output."
}

if ($audioRequested -and -not $AllowLiveAudioCapture) {
    throw "Recording smoke audio mode '$Audio' can access microphone or system-audio devices. Pass -AllowLiveAudioCapture after preparing a safe audio environment."
}

function Find-Executable {
    param([string] $Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

function Find-Ffprobe {
    $ffprobe = Find-Executable "ffprobe.exe"
    if ($ffprobe) {
        return $ffprobe
    }

    $ffmpeg = $env:RECEIPTS_FFMPEG_PATH
    if ([string]::IsNullOrWhiteSpace($ffmpeg)) {
        $ffmpeg = $env:GOATSHOT_FFMPEG_PATH
    }
    if ([string]::IsNullOrWhiteSpace($ffmpeg)) {
        $ffmpeg = Find-Executable "ffmpeg.exe"
    }

    if (-not [string]::IsNullOrWhiteSpace($ffmpeg)) {
        $sibling = Join-Path (Split-Path -Parent $ffmpeg) "ffprobe.exe"
        if (Test-Path $sibling) {
            return $sibling
        }
    }

    return $null
}

function Invoke-CliArtifact {
    param(
        [string[]] $Arguments,
        [string] $OutputPath,
        [string] $Label
    )

    $processInfo = [Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = $CliPath
    $processInfo.UseShellExecute = $false
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void]$processInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::Start($processInfo)
    # Drain stderr asynchronously: sequential ReadToEnd calls deadlock once the child
    # fills the ~4 KB stderr pipe while stdout is still being read.
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $stdout = $process.StandardOutput.ReadToEnd().Trim()
    $stderr = Redact-RecordingLogText -Text $stderrTask.GetAwaiter().GetResult().Trim()
    $process.WaitForExit()

    Set-Content -LiteralPath $OutputPath -Value $stdout -Encoding UTF8
    if ($process.ExitCode -ne 0) {
        $errorPath = [System.IO.Path]::ChangeExtension($OutputPath, ".stderr.log")
        Set-Content -LiteralPath $errorPath -Value $stderr -Encoding UTF8
        throw "$Label failed with exit code $($process.ExitCode). See $errorPath. $stderr"
    }

    [pscustomobject]@{
        Label = $Label
        Path = $OutputPath
        ExitCode = $process.ExitCode
    }
}

function Get-TargetSafetyClass {
    param([string] $Target)

    if ($Target -eq "fixed") {
        return "fixed-region-live-desktop-content"
    }

    return "live-desktop-content"
}

function Get-AudioSafetyClass {
    if ($Audio.Equals("none", [StringComparison]::OrdinalIgnoreCase)) {
        return "no-audio-device-access"
    }

    if ($Audio.EndsWith("-muted", [StringComparison]::OrdinalIgnoreCase)) {
        return "live-device-access-muted-track"
    }

    return "live-device-access-unmuted-track"
}

function Get-SmokeValidationPlan {
    param([string] $Target)

    $targetSafety = Get-TargetSafetyClass -Target $Target
    $requiresLiveDesktopConsent = $targetSafety -eq "live-desktop-content" -or
        $targetSafety -eq "fixed-region-live-desktop-content"
    [pscustomobject]@{
        Target = $Target
        Format = $Format
        TargetSafety = $targetSafety
        RequiresLiveDesktopConsent = $requiresLiveDesktopConsent
        LiveDesktopConsentProvided = [bool]$AllowLiveDesktopCapture
        AudioMode = $Audio
        AudioSafety = Get-AudioSafetyClass
        RequiresLiveAudioConsent = $script:audioRequested
        LiveAudioConsentProvided = [bool]$AllowLiveAudioCapture
        DurationSeconds = $DurationSeconds
        Fps = $Fps
        RequiresFfprobe = [bool]$RequireFfprobe
        Validations = @(
            "CLI exits zero",
            "output file exists",
            "output file length is greater than zero",
            "video stream dimensions and duration when ffprobe is available",
            "audio stream count and max audio/video duration delta when audio is requested and ffprobe is available"
        )
    }
}

function Write-SmokePlanArtifacts {
    param([object[]] $Plan)

    $planJsonPath = Join-Path $OutputRoot "proof-matrix.json"
    ConvertTo-Json -InputObject $Plan -Depth 8 | Set-Content -LiteralPath $planJsonPath -Encoding UTF8

    $planMarkdownPath = Join-Path $OutputRoot "proof-matrix.md"
    $lines = @(
        "# Recording Smoke Proof Matrix",
        "",
        "Date: $(Get-Date -Format o)",
        "",
        "- Format: $Format",
        "- Duration seconds: $DurationSeconds",
        "- FPS: $Fps",
        "- Audio: $Audio",
        "- ffprobe required: $([bool]$RequireFfprobe)",
        "- Plan only: $([bool]$PlanOnly)",
        "",
        "| Target | Target safety | Audio safety | Live desktop consent | Live audio consent | Validations |",
        "|---|---|---|---:|---:|---|"
    )

    foreach ($item in $Plan) {
        $validations = ($item.Validations -join "; ")
        $lines += ("| {0} | {1} | {2} | {3} | {4} | {5} |" -f
            $item.Target,
            $item.TargetSafety,
            $item.AudioSafety,
            "$($item.RequiresLiveDesktopConsent)/$($item.LiveDesktopConsentProvided)",
            "$($item.RequiresLiveAudioConsent)/$($item.LiveAudioConsentProvided)",
            $validations)
    }

    $lines | Set-Content -LiteralPath $planMarkdownPath -Encoding UTF8

    [pscustomobject]@{
        ProofMatrixJson = $planJsonPath
        ProofMatrixMarkdown = $planMarkdownPath
    }
}

function Read-VideoMetadata {
    param(
        [string] $Path,
        [string] $Ffprobe
    )

    if ([string]::IsNullOrWhiteSpace($Ffprobe) -or -not (Test-Path $Ffprobe)) {
        return [pscustomobject]@{
            Available = $false
            Message = "ffprobe unavailable; validated file existence and nonzero length only."
        }
    }

    $json = & $Ffprobe -v error -print_format json -show_format -show_streams $Path
    if ($LASTEXITCODE -ne 0) {
        throw "ffprobe failed for $Path."
    }

    $metadata = $json | ConvertFrom-Json
    $video = @($metadata.streams | Where-Object { $_.codec_type -eq "video" } | Select-Object -First 1)
    if (-not $video) {
        throw "No video stream was reported for $Path."
    }

    $duration = 0.0
    if ($metadata.format -and $metadata.format.duration) {
        $duration = [double]::Parse([string]$metadata.format.duration, [Globalization.CultureInfo]::InvariantCulture)
    }
    elseif ($video.duration) {
        $duration = [double]::Parse([string]$video.duration, [Globalization.CultureInfo]::InvariantCulture)
    }

    $videoDuration = $duration
    if ($video.duration) {
        $videoDuration = [double]::Parse([string]$video.duration, [Globalization.CultureInfo]::InvariantCulture)
    }

    $audioStreams = @($metadata.streams | Where-Object { $_.codec_type -eq "audio" })
    $audioDurations = @(
        foreach ($audio in $audioStreams) {
            if ($audio.duration) {
                [Math]::Round([double]::Parse([string]$audio.duration, [Globalization.CultureInfo]::InvariantCulture), 3)
            }
        }
    )
    $maxAudioVideoDelta = $null
    if ($audioDurations.Count -gt 0) {
        $deltas = @($audioDurations | ForEach-Object { [Math]::Abs($_ - $videoDuration) })
        $maxAudioVideoDelta = [Math]::Round(($deltas | Measure-Object -Maximum).Maximum, 3)
    }

    [pscustomobject]@{
        Available = $true
        Codec = [string]$video.codec_name
        Width = [int]$video.width
        Height = [int]$video.height
        DurationSeconds = [Math]::Round($duration, 3)
        VideoDurationSeconds = [Math]::Round($videoDuration, 3)
        Frames = if ($video.nb_frames) { [int]$video.nb_frames } else { $null }
        AudioStreams = $audioStreams.Count
        AudioDurationSeconds = $audioDurations
        MaxAudioVideoDeltaSeconds = $maxAudioVideoDelta
        Message = "ffprobe metadata validated."
    }
}

function Redact-RecordingLogText {
    param([string] $Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    return $Text -replace '(active window )"[^"]*"', '$1"[redacted]"'
}

function Invoke-RecordingSmoke {
    param([string] $Target)

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $outputPath = Join-Path $recordingRoot "$Target-$stamp.$Format"
    $stdoutPath = Join-Path $logRoot "$Target-$stamp.stdout.log"
    $stderrPath = Join-Path $logRoot "$Target-$stamp.stderr.log"

    $args = @("record", $Target, "--duration", $DurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture), "--format", $Format, "--fps", $Fps.ToString([Globalization.CultureInfo]::InvariantCulture), "--quality", "small", "--output", $outputPath)
    if ($Target -eq "fixed") {
        $args += @("--bounds", "0,0,320,180")
    }

    switch ($Audio) {
        "mic-muted" {
            $args += @("--mic", "--mute-mic")
        }
        "system-audio-muted" {
            $args += @("--system-audio", "--mute-system-audio")
        }
        "both-muted" {
            $args += @("--mic", "--mute-mic", "--system-audio", "--mute-system-audio")
        }
        "mic" {
            $args += @("--mic", "--unmute-mic")
        }
        "system-audio" {
            $args += @("--system-audio", "--unmute-system-audio")
        }
        "both" {
            $args += @("--mic", "--unmute-mic", "--system-audio", "--unmute-system-audio")
        }
    }

    $processInfo = [Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = $CliPath
    $processInfo.UseShellExecute = $false
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    foreach ($argument in $args) {
        [void]$processInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::Start($processInfo)
    # Drain stderr asynchronously: sequential ReadToEnd calls deadlock once the child
    # fills the ~4 KB stderr pipe while stdout is still being read.
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $stdout = $process.StandardOutput.ReadToEnd().Trim()
    $stderr = Redact-RecordingLogText -Text $stderrTask.GetAwaiter().GetResult().Trim()
    $process.WaitForExit()
    $exitCode = $process.ExitCode

    Set-Content -LiteralPath $stdoutPath -Value $stdout -Encoding UTF8
    Set-Content -LiteralPath $stderrPath -Value $stderr -Encoding UTF8

    if ($exitCode -ne 0) {
        throw "Recording smoke target '$Target' failed with exit code $exitCode. See $stderrPath. $stderr"
    }

    if (-not (Test-Path $outputPath)) {
        throw "Recording smoke target '$Target' did not create $outputPath. Stdout: $stdout"
    }

    $file = Get-Item $outputPath
    if ($file.Length -le 0) {
        throw "Recording smoke target '$Target' created an empty file at $outputPath."
    }

    $metadata = Read-VideoMetadata -Path $outputPath -Ffprobe $script:ffprobe
    $metadataValidated = [bool]$metadata.Available
    $durationValidated = $false
    $audioStreamValidated = -not $script:audioRequested
    $audioVideoDeltaValidated = -not $script:audioRequested
    if ($metadata.Available) {
        if ($metadata.Width -le 0 -or $metadata.Height -le 0) {
            throw "Recording smoke target '$Target' reported invalid video dimensions."
        }

        $minimumDuration = [Math]::Max(0.2, $DurationSeconds * 0.5)
        if ($metadata.DurationSeconds -lt $minimumDuration) {
            throw "Recording smoke target '$Target' duration was too short: $($metadata.DurationSeconds)s, expected at least $minimumDuration s."
        }
        $durationValidated = $true

        if ($script:audioRequested) {
            if ($metadata.AudioStreams -lt 1) {
                throw "Recording smoke target '$Target' requested audio mode '$Audio', but ffprobe reported no audio stream."
            }
            $audioStreamValidated = $true

            if ($metadata.MaxAudioVideoDeltaSeconds -ne $null -and
                $metadata.MaxAudioVideoDeltaSeconds -gt $MaxAudioVideoDeltaSeconds) {
                throw "Recording smoke target '$Target' audio/video duration delta was $($metadata.MaxAudioVideoDeltaSeconds)s, expected <= $MaxAudioVideoDeltaSeconds s."
            }
            $audioVideoDeltaValidated = $metadata.MaxAudioVideoDeltaSeconds -ne $null
        }
    }

    [pscustomobject]@{
        Target = $Target
        Format = $Format
        TargetSafety = Get-TargetSafetyClass -Target $Target
        DurationRequestedSeconds = $DurationSeconds
        FpsRequested = $Fps
        AudioMode = $Audio
        AudioSafety = Get-AudioSafetyClass
        OutputPath = $file.FullName
        Bytes = $file.Length
        ExitCode = $exitCode
        StdoutLog = $stdoutPath
        StderrLog = $stderrPath
        CliMessage = $stderr
        Validation = [pscustomobject]@{
            OutputFileExists = $true
            OutputFileNonEmpty = $file.Length -gt 0
            FfprobeRequired = [bool]$RequireFfprobe
            FfprobeAvailable = [bool]$metadata.Available
            MetadataValidated = $metadataValidated
            DurationValidated = $durationValidated
            AudioStreamRequired = [bool]$script:audioRequested
            AudioStreamValidated = $audioStreamValidated
            AudioVideoDeltaLimitSeconds = if ($script:audioRequested) { $MaxAudioVideoDeltaSeconds } else { $null }
            AudioVideoDeltaValidated = $audioVideoDeltaValidated
            LiveDesktopConsentProvided = [bool]$AllowLiveDesktopCapture
            LiveAudioConsentProvided = [bool]$AllowLiveAudioCapture
        }
        Metadata = $metadata
    }
}

$env:RECEIPTS_LOCAL_ROOT = $localRoot
$env:RECEIPTS_LIBRARY_ROOT = $libraryRoot
$env:GOATSHOT_LOCAL_ROOT = $localRoot
$env:GOATSHOT_LIBRARY_ROOT = $libraryRoot
$script:ffprobe = Find-Ffprobe
$script:audioRequested = $audioRequested

if ($RequireFfprobe -and -not $script:ffprobe) {
    throw "ffprobe.exe is required for this recording smoke run. Install ffprobe, put it on PATH, or set RECEIPTS_FFMPEG_PATH (legacy GOATSHOT_FFMPEG_PATH is also accepted) to an ffmpeg.exe with sibling ffprobe.exe."
}

$plan = @($normalizedTargets | ForEach-Object { Get-SmokeValidationPlan -Target $_ })
$planArtifacts = Write-SmokePlanArtifacts -Plan $plan

if ($PlanOnly) {
    [pscustomobject]@{
        OutputRoot = $OutputRoot
        ProofMatrixJson = $planArtifacts.ProofMatrixJson
        ProofMatrixMarkdown = $planArtifacts.ProofMatrixMarkdown
        Plan = $plan
    }
    return
}

$preflightArtifact = Invoke-CliArtifact `
    -Arguments @("record", "preflight", "--json") `
    -OutputPath (Join-Path $diagnosticsRoot "record-preflight.json") `
    -Label "record preflight"
$devicesArtifact = Invoke-CliArtifact `
    -Arguments @("record", "devices", "--json") `
    -OutputPath (Join-Path $diagnosticsRoot "record-devices.json") `
    -Label "record devices"

$results = @(foreach ($target in $normalizedTargets) {
    Invoke-RecordingSmoke -Target $target
})

$summaryPath = Join-Path $OutputRoot "summary.json"
ConvertTo-Json -InputObject $results -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

$manifestPath = Join-Path $OutputRoot "run-manifest.json"
$runManifest = [pscustomobject]@{
    GeneratedAt = Get-Date -Format o
    OutputRoot = $OutputRoot
    Configuration = $Configuration
    Format = $Format
    DurationSeconds = $DurationSeconds
    Fps = $Fps
    Audio = $Audio
    Ffprobe = if ($script:ffprobe) { $script:ffprobe } else { $null }
    FfprobeRequired = [bool]$RequireFfprobe
    LiveDesktopConsentProvided = [bool]$AllowLiveDesktopCapture
    LiveAudioConsentProvided = [bool]$AllowLiveAudioCapture
    Artifacts = [pscustomobject]@{
        SummaryJson = $summaryPath
        ProofMatrixJson = $planArtifacts.ProofMatrixJson
        ProofMatrixMarkdown = $planArtifacts.ProofMatrixMarkdown
        PreflightJson = $preflightArtifact.Path
        DevicesJson = $devicesArtifact.Path
    }
    Plan = $plan
    Results = $results
}
ConvertTo-Json -InputObject $runManifest -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$markdownPath = Join-Path $OutputRoot "summary.md"
$lines = @(
    "# Recording Smoke Summary",
    "",
    "Date: $(Get-Date -Format o)",
    "",
    "- Targets: $($normalizedTargets -join ', ')",
    "- Format: $Format",
    "- Duration seconds: $DurationSeconds",
    "- FPS: $Fps",
    "- Audio: $Audio",
    "- ffprobe: $(if ($script:ffprobe) { $script:ffprobe } else { 'unavailable' })",
    "- Proof matrix: ``$($planArtifacts.ProofMatrixMarkdown)``",
    "- Run manifest: ``$manifestPath``",
    "- Preflight JSON: ``$($preflightArtifact.Path)``",
    "- Devices JSON: ``$($devicesArtifact.Path)``",
    "",
    "| Target | Safety | Output | Bytes | Validation | Metadata |",
    "|---|---|---:|---:|---|---|"
)

foreach ($result in $results) {
    $metadataText = if ($result.Metadata.Available) {
        $syncText = if ($result.Metadata.MaxAudioVideoDeltaSeconds -ne $null) {
            ", av-delta=$($result.Metadata.MaxAudioVideoDeltaSeconds)s"
        }
        else {
            ""
        }
        "$($result.Metadata.Codec), $($result.Metadata.Width)x$($result.Metadata.Height), $($result.Metadata.DurationSeconds)s, audio-streams=$($result.Metadata.AudioStreams)$syncText"
    }
    else {
        $result.Metadata.Message
    }

    $validationText = "file=$($result.Validation.OutputFileNonEmpty), metadata=$($result.Validation.MetadataValidated), duration=$($result.Validation.DurationValidated), audio=$($result.Validation.AudioStreamValidated), av-delta=$($result.Validation.AudioVideoDeltaValidated)"
    $lines += ("| {0} | {1} / {2} | ``{3}`` | {4} | {5} | {6} |" -f
        $result.Target,
        $result.TargetSafety,
        $result.AudioSafety,
        $result.OutputPath,
        $result.Bytes,
        $validationText,
        $metadataText)
}

$lines | Set-Content -LiteralPath $markdownPath -Encoding UTF8

[pscustomobject]@{
    SummaryJson = $summaryPath
    SummaryMarkdown = $markdownPath
    RunManifestJson = $manifestPath
    ProofMatrixJson = $planArtifacts.ProofMatrixJson
    ProofMatrixMarkdown = $planArtifacts.ProofMatrixMarkdown
    PreflightJson = $preflightArtifact.Path
    DevicesJson = $devicesArtifact.Path
    OutputRoot = $OutputRoot
    Results = $results
}
