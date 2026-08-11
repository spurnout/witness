using System.Text;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionPublicationPlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<BrowserExtensionPublicationPlanResult> CreateAsync(
        BrowserExtensionPublicationPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = string.IsNullOrWhiteSpace(request.ExtensionSourceDirectory)
            ? Path.GetFullPath("browser-extension")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.ExtensionSourceDirectory));
        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.GetFullPath(Path.Combine("artifacts", "browser-extension-publication-plan"))
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));
        var storePackageRoot = string.IsNullOrWhiteSpace(request.StorePackageRoot)
            ? Path.GetFullPath(Path.Combine("artifacts", "browser-extension-store-package"))
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.StorePackageRoot));

        var result = new BrowserExtensionPublicationPlanResult
        {
            SourceDirectory = source,
            OutputPath = outputRoot,
            StorePackageRoot = storePackageRoot,
            SupportUrl = SensitiveTextDetector.Redact(request.SupportUrl ?? string.Empty),
            PrivacyUrl = SensitiveTextDetector.Redact(request.PrivacyUrl ?? string.Empty),
            WouldPublish = false,
            WouldContactStoreAccount = false,
            WouldUploadPackage = false,
            WouldInstallExtension = false,
            WouldMutateBrowserProfile = false
        };

        Directory.CreateDirectory(outputRoot);
        var targets = NormalizeTargets(request.Target);
        if (targets.Count == 0)
        {
            result.Issues.Add("Publication-plan target must be one of: all, chrome, edge, firefox.");
        }

        var readinessOutput = Path.Combine(outputRoot, "readiness");
        var readiness = await new BrowserExtensionStoreReadinessService().CreateAsync(new BrowserExtensionStoreReadinessRequest
        {
            ExtensionSourceDirectory = source,
            OutputPath = readinessOutput,
            Target = request.Target,
            SupportUrl = request.SupportUrl,
            PrivacyUrl = request.PrivacyUrl
        }, cancellationToken);
        result.ReadinessOutputPath = readiness.OutputPath;
        result.Issues.AddRange(readiness.Issues);
        result.Warnings.AddRange(readiness.Warnings);

        foreach (var target in targets)
        {
            result.Targets.Add(BuildTarget(target, readiness, storePackageRoot));
        }

        result.ManualGates.AddRange(BuildManualGates());
        result.PublicationNonGoals.AddRange(BuildPublicationNonGoals());
        result.AuthorityBoundaries.AddRange(BuildAuthorityBoundaries());
        result.SourceReferences.AddRange(BuildSourceReferences());

        result.Succeeded = result.Issues.Count == 0 && result.Targets.Count > 0 && result.Targets.All(target => target.Issues.Count == 0);
        result.Message = result.Succeeded
            ? $"Browser extension publication plan created: {outputRoot}. Manual store submission is still required."
            : $"Browser extension publication plan created with blockers: {outputRoot}.";

        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "publication-plan.md",
            BuildMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "publication-plan.json",
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken));

        return result;
    }

    private static BrowserExtensionPublicationTargetPlan BuildTarget(
        BrowserExtensionPublicationTarget target,
        BrowserExtensionStoreReadinessResult readiness,
        string storePackageRoot)
    {
        var targetName = target.ToString().ToLowerInvariant();
        var readinessTarget = readiness.Targets.FirstOrDefault(entry =>
            entry.Target.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        var package = FindStorePackage(storePackageRoot, targetName);
        var plan = new BrowserExtensionPublicationTargetPlan
        {
            Target = targetName,
            ReadinessStatus = readinessTarget?.Status ?? "missing-readiness",
            ExtensionPackagePath = package.ExtensionPackagePath,
            StoreSubmissionBundlePath = package.SubmissionBundlePath,
            StorePackageAvailable = !string.IsNullOrWhiteSpace(package.ExtensionPackagePath),
            StoreSubmissionBundleAvailable = !string.IsNullOrWhiteSpace(package.SubmissionBundlePath),
            WouldPublish = false,
            WouldContactStoreAccount = false,
            WouldUploadPackage = false,
            WouldInstallExtension = false,
            RequiresDeveloperAccount = true,
            RequiresManualReview = true,
            RequiresLiveBrowserEvidence = true
        };

        if (readinessTarget is null)
        {
            plan.Issues.Add("Store-readiness output did not include this target.");
        }
        else
        {
            plan.Issues.AddRange(readinessTarget.Issues);
            plan.Warnings.AddRange(readinessTarget.Warnings);
        }

        plan.Issues.AddRange(package.Issues);
        if (!plan.StorePackageAvailable)
        {
            plan.Issues.Add($"No {targetName} extension package was found under {storePackageRoot}.");
        }

        if (!plan.StoreSubmissionBundleAvailable)
        {
            plan.Issues.Add($"No {targetName} store submission bundle was found under {storePackageRoot}.");
        }

        plan.RequiredEvidence.AddRange(BuildRequiredEvidence(target));
        plan.ManualSteps.AddRange(BuildManualSteps(target, package));
        plan.Commands.Add($"receipts browser-extension store-readiness --source browser-extension --target {targetName} --json");
        plan.Commands.Add($"receipts browser-extension store-package --source browser-extension --target {targetName} --support-url <reviewed-support-url> --privacy-url <reviewed-privacy-url> --release-notes <reviewed-notes> --json");
        plan.Commands.Add($"receipts browser-extension install-plan --browser {targetName} --source browser-extension --store-package-root {Quote(storePackageRoot)} --json");

        plan.Status = plan.Issues.Count == 0
            ? "manual-publication-plan-ready"
            : "blocked-before-manual-publication";
        plan.Message = plan.Status == "manual-publication-plan-ready"
            ? $"{targetName} package/readiness artifacts are ready for a human browser-store submission flow. Receipts did not publish or upload anything."
            : $"{targetName} publication is blocked until local package/readiness artifacts are complete.";
        return plan;
    }

    private static BrowserExtensionPublicationPackageInfo FindStorePackage(string storePackageRoot, string target)
    {
        var result = new BrowserExtensionPublicationPackageInfo();
        if (!Directory.Exists(storePackageRoot))
        {
            result.Issues.Add($"Store-package root was not found: {storePackageRoot}");
            return result;
        }

        var targetRoot = Path.Combine(storePackageRoot, target);
        if (Directory.Exists(targetRoot))
        {
            result.ExtensionPackagePath = FindPreferredArtifact(
                targetRoot,
                $"receipts-browser-extension-{target}-v*.zip",
                $"goatshot-browser-extension-{target}-v*.zip");
        }

        result.SubmissionBundlePath = FindPreferredArtifact(
            storePackageRoot,
            $"receipts-browser-extension-{target}-store-submission.zip",
            $"goatshot-browser-extension-{target}-store-submission.zip");
        return result;
    }

    private static string FindPreferredArtifact(string root, string currentPattern, string legacyPattern)
    {
        foreach (var pattern in new[] { currentPattern, legacyPattern })
        {
            var match = Directory.GetFiles(root, pattern)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> BuildRequiredEvidence(BrowserExtensionPublicationTarget target)
    {
        var browserName = target.ToString();
        return new[]
        {
            $"{browserName} developer/store account screenshot or submission receipt.",
            $"{browserName} upload/validation result for the reviewed package.",
            $"{browserName} listing metadata screenshot with reviewed support and privacy URLs.",
            $"{browserName} review/signing/availability status after store processing.",
            $"{browserName} live browser screenshots for popup consent defaults, options defaults, Host Status, safe fixture capture, and native import result.",
            "Proof that the extension's native-host dependency and data-use disclosures match the submitted package."
        };
    }

    private static IReadOnlyList<string> BuildManualSteps(
        BrowserExtensionPublicationTarget target,
        BrowserExtensionPublicationPackageInfo package)
    {
        var targetName = target.ToString().ToLowerInvariant();
        var steps = new List<string>
        {
            "Review the generated store-readiness copy, permission rationale, privacy/data-use notes, and screenshots checklist.",
            "Review the generated extension package and store submission bundle before uploading anything.",
            "Use a browser-store developer account controlled by the product owner.",
            "Upload the reviewed package through the store's current human submission flow.",
            "Save validation/review/submission screenshots in a manual-validation folder.",
            "After approval or rejection, rerun `browser-extension proof validate` for live browser proof instead of treating store submission as functional proof."
        };

        if (!string.IsNullOrWhiteSpace(package.ExtensionPackagePath))
        {
            steps.Insert(1, $"Current {targetName} extension package candidate: `{package.ExtensionPackagePath}`.");
        }

        if (!string.IsNullOrWhiteSpace(package.SubmissionBundlePath))
        {
            steps.Insert(2, $"Current {targetName} submission bundle candidate: `{package.SubmissionBundlePath}`.");
        }

        return steps;
    }

    private static IReadOnlyList<string> BuildManualGates() => new[]
    {
        "Developer/store account ownership and credentials.",
        "Current store policy acceptance and reviewer-facing metadata.",
        "Manual package upload, validator output, signing/review status, and publication availability.",
        "Live browser screenshots using safe content after the store or unpacked package is loaded.",
        "Native-host registration proof using the browser-generated extension id.",
        "Privacy/data-use review against the submitted package and support/privacy URLs."
    };

    private static IReadOnlyList<string> BuildPublicationNonGoals() => new[]
    {
        "This plan does not publish to Chrome Web Store, Microsoft Edge Add-ons, or Firefox AMO.",
        "This plan does not contact browser-store accounts or APIs.",
        "This plan does not upload extension packages.",
        "This plan does not sign, approve, list, or make an extension available.",
        "This plan does not install extensions or mutate browser profiles.",
        "This plan does not prove live browser Host Status or capture/import behavior."
    };

    private static IReadOnlyList<string> BuildAuthorityBoundaries() => new[]
    {
        "Browser-store submission, review, signing, and availability are owned by each browser vendor's current store flow.",
        "Receipts can generate local packages, listing copy, checklists, native-host manifests, and proof commands.",
        "Receipts should not claim publication until a human supplies store-side evidence.",
        "Receipts should not claim automatic installation unless a later enterprise/browser-policy tranche is explicitly implemented and proven."
    };

    private static IReadOnlyList<string> BuildSourceReferences() => new[]
    {
        "Chrome Web Store developer documentation: https://developer.chrome.com/docs/webstore",
        "Chrome Web Store privacy best practices: https://developer.chrome.com/docs/webstore/best-practices",
        "Microsoft Edge extension publishing documentation: https://learn.microsoft.com/en-us/microsoft-edge/extensions/publish/publish-extension",
        "Microsoft Edge extension hosting/updating documentation: https://learn.microsoft.com/en-us/microsoft-edge/extensions/publish/hosting-and-updating",
        "Firefox AMO submission documentation: https://extensionworkshop.com/documentation/publish/submitting-an-add-on/",
        "Firefox signing/distribution overview: https://extensionworkshop.com/documentation/publish/signing-and-distribution-overview/"
    };

    private static string BuildMarkdown(BrowserExtensionPublicationPlanResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Receipts Browser Extension Publication Plan");
        builder.AppendLine();
        builder.AppendLine($"Source: `{result.SourceDirectory}`");
        builder.AppendLine($"Store package root: `{result.StorePackageRoot}`");
        builder.AppendLine($"Readiness output: `{result.ReadinessOutputPath}`");
        builder.AppendLine($"Support URL: `{EmptyIfMissing(result.SupportUrl)}`");
        builder.AppendLine($"Privacy URL: `{EmptyIfMissing(result.PrivacyUrl)}`");
        builder.AppendLine();
        builder.AppendLine("## Boundary");
        builder.AppendLine();
        builder.AppendLine($"- Would publish: `{result.WouldPublish}`");
        builder.AppendLine($"- Would contact store account: `{result.WouldContactStoreAccount}`");
        builder.AppendLine($"- Would upload package: `{result.WouldUploadPackage}`");
        builder.AppendLine($"- Would install extension: `{result.WouldInstallExtension}`");
        builder.AppendLine($"- Would mutate browser profile: `{result.WouldMutateBrowserProfile}`");
        builder.AppendLine();
        AppendList(builder, "Publication Non-Goals", result.PublicationNonGoals);
        AppendList(builder, "Manual Gates", result.ManualGates);
        builder.AppendLine("## Targets");
        builder.AppendLine();
        foreach (var target in result.Targets)
        {
            builder.AppendLine($"### {target.Target}");
            builder.AppendLine();
            builder.AppendLine($"Status: `{target.Status}`");
            builder.AppendLine($"Message: {target.Message}");
            builder.AppendLine($"Readiness status: `{target.ReadinessStatus}`");
            builder.AppendLine($"Extension package: `{EmptyIfMissing(target.ExtensionPackagePath)}`");
            builder.AppendLine($"Store submission bundle: `{EmptyIfMissing(target.StoreSubmissionBundlePath)}`");
            builder.AppendLine($"Would publish: `{target.WouldPublish}`");
            builder.AppendLine($"Would contact store account: `{target.WouldContactStoreAccount}`");
            builder.AppendLine($"Would upload package: `{target.WouldUploadPackage}`");
            builder.AppendLine($"Would install extension: `{target.WouldInstallExtension}`");
            AppendList(builder, "Issues", target.Issues, 4);
            AppendList(builder, "Warnings", target.Warnings, 4);
            AppendList(builder, "Commands", target.Commands, 4);
            AppendList(builder, "Manual Steps", target.ManualSteps, 4);
            AppendList(builder, "Required Evidence", target.RequiredEvidence, 4);
        }

        AppendList(builder, "Authority Boundaries", result.AuthorityBoundaries);
        AppendList(builder, "Source References", result.SourceReferences);
        return builder.ToString();
    }

    private static List<BrowserExtensionPublicationTarget> NormalizeTargets(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return new List<BrowserExtensionPublicationTarget>
            {
                BrowserExtensionPublicationTarget.Chrome,
                BrowserExtensionPublicationTarget.Edge,
                BrowserExtensionPublicationTarget.Firefox
            };
        }

        var targets = new List<BrowserExtensionPublicationTarget>();
        foreach (var part in target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<BrowserExtensionPublicationTarget>(part, ignoreCase: true, out var parsed))
            {
                targets.Add(parsed);
            }
        }

        return targets.Distinct().ToList();
    }

    private static async Task<string> WriteFileAsync(
        string root,
        string fileName,
        string contents,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents, cancellationToken);
        return path;
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values, int headingLevel = 2)
    {
        builder.AppendLine($"{new string('#', headingLevel)} {title}");
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

    private static string EmptyIfMissing(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "missing" : value;

    private static string Quote(string value) => $"\"{value}\"";

    private sealed class BrowserExtensionPublicationPackageInfo
    {
        public string ExtensionPackagePath { get; set; } = string.Empty;
        public string SubmissionBundlePath { get; set; } = string.Empty;
        public List<string> Issues { get; set; } = new();
    }
}

public enum BrowserExtensionPublicationTarget
{
    Chrome,
    Edge,
    Firefox
}

public sealed class BrowserExtensionPublicationPlanRequest
{
    public string? ExtensionSourceDirectory { get; set; }
    public string? OutputPath { get; set; }
    public string? Target { get; set; }
    public string? StorePackageRoot { get; set; }
    public string? SupportUrl { get; set; }
    public string? PrivacyUrl { get; set; }
}

public sealed class BrowserExtensionPublicationPlanResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string StorePackageRoot { get; set; } = string.Empty;
    public string ReadinessOutputPath { get; set; } = string.Empty;
    public string SupportUrl { get; set; } = string.Empty;
    public string PrivacyUrl { get; set; } = string.Empty;
    public bool WouldPublish { get; set; }
    public bool WouldContactStoreAccount { get; set; }
    public bool WouldUploadPackage { get; set; }
    public bool WouldInstallExtension { get; set; }
    public bool WouldMutateBrowserProfile { get; set; }
    public List<BrowserExtensionPublicationTargetPlan> Targets { get; set; } = new();
    public List<string> ManualGates { get; set; } = new();
    public List<string> PublicationNonGoals { get; set; } = new();
    public List<string> AuthorityBoundaries { get; set; } = new();
    public List<string> SourceReferences { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class BrowserExtensionPublicationTargetPlan
{
    public string Target { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ReadinessStatus { get; set; } = string.Empty;
    public string ExtensionPackagePath { get; set; } = string.Empty;
    public string StoreSubmissionBundlePath { get; set; } = string.Empty;
    public bool StorePackageAvailable { get; set; }
    public bool StoreSubmissionBundleAvailable { get; set; }
    public bool WouldPublish { get; set; }
    public bool WouldContactStoreAccount { get; set; }
    public bool WouldUploadPackage { get; set; }
    public bool WouldInstallExtension { get; set; }
    public bool RequiresDeveloperAccount { get; set; }
    public bool RequiresManualReview { get; set; }
    public bool RequiresLiveBrowserEvidence { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Commands { get; set; } = new();
    public List<string> ManualSteps { get; set; } = new();
    public List<string> RequiredEvidence { get; set; } = new();
}
