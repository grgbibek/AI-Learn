using System.Text.RegularExpressions;

namespace TaskFlow.Api.Data;

public sealed record SanitizationResult(
    string SanitizedText,
    bool WasSanitized,
    IReadOnlyList<string> DetectedTypes);

public sealed class DataSanitizationService
{
    private static readonly Regex PrivateKeyRegex = new(
        "-----BEGIN [A-Z ]*PRIVATE KEY-----.*?-----END [A-Z ]*PRIVATE KEY-----",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex ConnectionStringRegex = new(
        @"\b(?:Server|Data Source)\s*=\s*[^\r\n;]+(?:;\s*[^=;\r\n]+\s*=\s*[^\r\n;]+){2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BearerTokenRegex = new(
        @"\bBearer\s+[A-Za-z0-9._~+/=-]{20,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex JwtRegex = new(
        @"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ApiKeyRegex = new(
        @"\b(?:sk|pk)-(?:live|test|proj-)?[A-Za-z0-9_-]{16,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SecretAssignmentRegex = new(
        @"\b(?<name>api[-_ ]?key|client[-_ ]?secret|secret|token|password|pwd)\s*[:=]\s*[""']?(?<value>[^\s;""'\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex EmailRegex = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex PhoneRegex = new(
        @"(?<!\d)(?:\+?\d{1,3}[-.\s]?)?(?:\(?\d{3}\)?[-.\s]?)\d{3}[-.\s]?\d{4}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CreditCardCandidateRegex = new(
        @"(?<!\d)(?:\d[ -]*?){13,19}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public SanitizationResult Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return new SanitizationResult(input ?? string.Empty, false, []);
        }

        var detectedTypes = new List<string>();
        var sanitized = input;

        sanitized = Redact(sanitized, PrivateKeyRegex, "[REDACTED_PRIVATE_KEY]", "PrivateKey", detectedTypes);
        sanitized = Redact(sanitized, ConnectionStringRegex, "[REDACTED_CONNECTION_STRING]", "ConnectionString", detectedTypes);
        sanitized = Redact(sanitized, BearerTokenRegex, "Bearer [REDACTED_TOKEN]", "BearerToken", detectedTypes);
        sanitized = Redact(sanitized, JwtRegex, "[REDACTED_JWT]", "JwtToken", detectedTypes);
        sanitized = Redact(sanitized, ApiKeyRegex, "[REDACTED_API_KEY]", "ApiKey", detectedTypes);
        sanitized = RedactSecretAssignments(sanitized, detectedTypes);
        sanitized = RedactCreditCards(sanitized, detectedTypes);
        sanitized = Redact(sanitized, EmailRegex, "[REDACTED_EMAIL]", "EmailAddress", detectedTypes);
        sanitized = Redact(sanitized, PhoneRegex, "[REDACTED_PHONE]", "PhoneNumber", detectedTypes);

        return new SanitizationResult(sanitized, detectedTypes.Count > 0, detectedTypes);
    }

    public bool ContainsSensitiveData(string? input) => Sanitize(input).WasSanitized;

    private static string Redact(string input, Regex regex, string replacement, string detectedType, List<string> detectedTypes)
    {
        var found = false;
        var sanitized = regex.Replace(input, _ =>
        {
            found = true;
            return replacement;
        });

        if (found)
        {
            AddDetectedType(detectedTypes, detectedType);
        }

        return sanitized;
    }

    private static string RedactSecretAssignments(string input, List<string> detectedTypes)
    {
        var found = false;
        var sanitized = SecretAssignmentRegex.Replace(input, match =>
        {
            found = true;
            return $"{match.Groups["name"].Value}=[REDACTED_SECRET]";
        });

        if (found)
        {
            AddDetectedType(detectedTypes, "SecretAssignment");
        }

        return sanitized;
    }

    private static string RedactCreditCards(string input, List<string> detectedTypes)
    {
        var found = false;
        var sanitized = CreditCardCandidateRegex.Replace(input, match =>
        {
            var digits = new string(match.Value.Where(char.IsDigit).ToArray());
            if (!PassesLuhnCheck(digits))
            {
                return match.Value;
            }

            found = true;
            return "[REDACTED_CREDIT_CARD]";
        });

        if (found)
        {
            AddDetectedType(detectedTypes, "CreditCardNumber");
        }

        return sanitized;
    }

    private static bool PassesLuhnCheck(string digits)
    {
        var sum = 0;
        var doubleDigit = false;

        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var digit = digits[index] - '0';
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }

    private static void AddDetectedType(List<string> detectedTypes, string detectedType)
    {
        if (!detectedTypes.Contains(detectedType, StringComparer.OrdinalIgnoreCase))
        {
            detectedTypes.Add(detectedType);
        }
    }
}