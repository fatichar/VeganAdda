namespace VeganAdda.Web.Observability;

public sealed record AnalyticsSettings(string ScriptUrl, string WebsiteId, string Origin)
{
    public static AnalyticsSettings? FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsProduction()) return null;
        var script = configuration["Analytics:Umami:ScriptUrl"];
        var website = configuration["Analytics:Umami:WebsiteId"];
        if (string.IsNullOrWhiteSpace(script) || string.IsNullOrWhiteSpace(website)) return null;
        if (!Uri.TryCreate(script, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || !Guid.TryParse(website, out _))
            throw new InvalidOperationException("Umami requires an HTTPS script URL without credentials, query or fragment, and a website UUID.");
        return new(uri.AbsoluteUri, website, uri.GetLeftPart(UriPartial.Authority));
    }
}
