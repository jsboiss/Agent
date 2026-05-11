using System.Text.RegularExpressions;

namespace Agent.Memory;

public static partial class PromptMemorySafety
{
    public static bool IsSafeForPrompt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Any(x => char.IsControl(x) && x is not '\r' and not '\n' and not '\t'))
        {
            return false;
        }

        return !PromptInjectionPattern().IsMatch(text)
            && !CredentialPattern().IsMatch(text)
            && !SecretPattern().IsMatch(text);
    }

    [GeneratedRegex("\\b(ignore|override|forget|bypass)\\b.{0,48}\\b(previous|system|developer|instruction|prompt|policy)\\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PromptInjectionPattern();

    [GeneratedRegex("\\b(exfiltrate|leak|reveal|print|send)\\b.{0,48}\\b(secret|token|password|credential|api key|private key)\\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CredentialPattern();

    [GeneratedRegex("\\b(sk-[A-Za-z0-9_-]{20,}|ghp_[A-Za-z0-9_]{20,}|xox[baprs]-[A-Za-z0-9-]{20,}|-----BEGIN [A-Z ]*PRIVATE KEY-----)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex SecretPattern();
}
