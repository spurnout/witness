using System.Text.RegularExpressions;

namespace GoatShot.App.Services;

internal static partial class WorkflowTextRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var redacted = SensitiveTextDetector.Scan(value).RedactedText;
        redacted = BearerRegex().Replace(redacted, "$1 [REDACTED]");
        redacted = AssignmentSecretRegex().Replace(redacted, "$1$2[REDACTED]");
        redacted = QuerySecretRegex().Replace(redacted, "$1$2=[REDACTED]");
        return redacted;
    }

    [GeneratedRegex(@"(?i)\b(authorization:\s*(?:bearer|basic)|bearer|basic)\s+([A-Za-z0-9._~+/=-]{8,})")]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"(?i)\b(access[_-]?token|refresh[_-]?token|client[_-]?secret|api[_-]?key|secret|signature|sig|password|code|temp[_-]?auth|tempauth|auth[_-]?key|session[_-]?url|upload[_-]?url)(\s*[:=]\s*)[^\s;&,]+")]
    private static partial Regex AssignmentSecretRegex();

    [GeneratedRegex(@"(?i)([?&])(access_token|refresh_token|client_secret|api_key|token|sig|signature|password|code|tempauth|temp_auth|authkey|auth_key|session|session_url|upload_url)=[^&#\s]+")]
    private static partial Regex QuerySecretRegex();
}
