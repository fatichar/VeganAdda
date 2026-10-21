namespace VeganAdda.Contracts;

public static class ProfileSetup
{
    public static bool AllowsIncomplete(string path) => path is "/onboarding" or "/settings" or "/settings/profile" or "/signin" or "/help" or "/privacy" or "/terms";
    public static string SafeReturnUrl(string? value) => value is { Length: > 0 } && value.StartsWith('/')
        && !value.StartsWith("//") && !value.Contains('\\') && !value.Any(char.IsControl)
        && !value.Split('?', '#')[0].Equals("/onboarding", StringComparison.OrdinalIgnoreCase) ? value : "/";
    public static string SignInDestination(bool complete, string? target)
    {
        // Keep an invitation on the outer onboarding URL across authentication.
        if (!complete && target is { Length: > 0 } && target.StartsWith("/onboarding?")
            && !target.Contains('\\') && !target.Any(char.IsControl)) return target;
        return complete ? SafeReturnUrl(target) : OnboardingUrl(target);
    }
    public static string OnboardingUrl(string? target) => "/onboarding?returnUrl=" + Uri.EscapeDataString(SafeReturnUrl(target));
}
