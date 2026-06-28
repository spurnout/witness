using System.Text.RegularExpressions;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static partial class SensitiveTextDetector
{
    public static IReadOnlyList<string> DetectKinds(string text)
    {
        return Find(text)
            .Select(finding => finding.Kind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static SensitiveScanResult Scan(string? text)
    {
        text ??= string.Empty;
        var findings = Find(text).ToList();
        return new SensitiveScanResult
        {
            Findings = findings,
            RedactedText = Redact(text, findings),
            Summary = Summarize(findings)
        };
    }

    public static IReadOnlyList<SensitiveFinding> Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<SensitiveFinding>();
        }

        var findings = new List<SensitiveFinding>();
        AddMatches(findings, text, ConnectionStringRegex(), "connection string");
        AddMatches(findings, text, UrlWithTokenRegex(), "URL with token");
        AddMatches(findings, text, BearerTokenRegex(), "bearer token");
        AddMatches(findings, text, JwtRegex(), "JWT");
        AddMatches(findings, text, GithubTokenRegex(), "GitHub token");
        AddMatches(findings, text, AwsKeyRegex(), "AWS access key");
        AddMatches(findings, text, SecretAssignmentRegex(), "API key or password field");
        AddCreditCardMatches(findings, text);
        AddMatches(findings, text, EmailRegex(), "email address");
        AddMatches(findings, text, PhoneRegex(), "phone number");
        AddMatches(findings, text, IpRegex(), "IP address");

        return findings
            .OrderBy(finding => finding.StartIndex)
            .ThenByDescending(finding => finding.Length)
            .ToList();
    }

    public static string Redact(string? text)
    {
        text ??= string.Empty;
        return Redact(text, Find(text));
    }

    public static string Summarize(IReadOnlyCollection<SensitiveFinding> findings)
    {
        if (findings.Count == 0)
        {
            return "No sensitive data detected.";
        }

        var groups = findings
            .GroupBy(finding => finding.Kind)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Count() == 1 ? group.Key : $"{group.Key} x{group.Count()}");

        return $"Detected {findings.Count} sensitive value(s): {string.Join(", ", groups)}.";
    }

    private static string Redact(string text, IReadOnlyList<SensitiveFinding> findings)
    {
        var redacted = text;
        foreach (var finding in findings.OrderByDescending(finding => finding.StartIndex))
        {
            var replacement = $"[REDACTED:{Slug(finding.Kind)}]";
            redacted = redacted.Remove(finding.StartIndex, finding.Length).Insert(finding.StartIndex, replacement);
        }

        return redacted;
    }

    private static void AddMatches(List<SensitiveFinding> findings, string text, Regex regex, string kind)
    {
        foreach (Match match in regex.Matches(text))
        {
            AddFindingIfNew(findings, kind, match.Value, match.Index, match.Length);
        }
    }

    private static void AddCreditCardMatches(List<SensitiveFinding> findings, string text)
    {
        foreach (Match match in CreditCardLikeRegex().Matches(text))
        {
            var digits = new string(match.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is < 13 or > 19 || !PassesLuhn(digits))
            {
                continue;
            }

            AddFindingIfNew(findings, "credit-card-like value", match.Value, match.Index, match.Length);
        }
    }

    private static void AddFindingIfNew(List<SensitiveFinding> findings, string kind, string value, int startIndex, int length)
    {
        if (length == 0 || findings.Any(existing => Overlaps(existing, startIndex, length)))
        {
            return;
        }

        findings.Add(new SensitiveFinding
        {
            Kind = kind,
            Value = value,
            StartIndex = startIndex,
            Length = length,
            Preview = Preview(value)
        });
    }

    private static bool Overlaps(SensitiveFinding existing, int startIndex, int length)
    {
        var endIndex = startIndex + length;
        var existingEndIndex = existing.StartIndex + existing.Length;
        return startIndex < existingEndIndex && endIndex > existing.StartIndex;
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var digit = digits[i] - '0';
            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    private static string Preview(string value)
    {
        value = value.ReplaceLineEndings(" ");
        if (value.Length <= 8)
        {
            return new string('*', value.Length);
        }

        return $"{value[..3]}...{value[^3..]}";
    }

    private static string Slug(string kind)
    {
        return kind
            .ToLowerInvariant()
            .Replace(" ", "-", StringComparison.Ordinal)
            .Replace("/", "-", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:\+?1[\s.-]?)?(?:\(?\d{3}\)?[\s.-]?)\d{3}[\s.-]?\d{4}(?![A-Za-z0-9])")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b")]
    private static partial Regex IpRegex();

    [GeneratedRegex(@"\b(?:\d[ -]*?){13,19}\b")]
    private static partial Regex CreditCardLikeRegex();

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")]
    private static partial Regex AwsKeyRegex();

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9_]{30,}|github_pat_[A-Za-z0-9_]{20,}_[A-Za-z0-9_]{20,})\b")]
    private static partial Regex GithubTokenRegex();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/=-]{20,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b")]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"\b(?:Server|Host|Data Source|Initial Catalog|Database|User ID|Uid|Password|Pwd|AccountKey|SharedAccessKey)=[^;\r\n]+(?:;[A-Za-z ][A-Za-z0-9 ]*=[^;\r\n]*)+;?", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringRegex();

    [GeneratedRegex(@"\b(?:api[_-]?key|access[_-]?key|secret|password|passwd|pwd|token)\s*[:=]\s*['""]?[A-Za-z0-9._~+/=-]{12,}['""]?", RegexOptions.IgnoreCase)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(@"https?://[^\s""'<>)]+[?&](?:access_token|token|api_key|apikey|key|secret|sig|signature|auth|code)=[^\s""'<>)]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlWithTokenRegex();
}
