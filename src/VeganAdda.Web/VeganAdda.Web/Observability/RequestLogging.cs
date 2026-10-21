using Microsoft.AspNetCore.Routing;
using Serilog.AspNetCore;
using Serilog.Events;

namespace VeganAdda.Web.Observability;

public static class RequestLogging
{
    public static void Configure(RequestLoggingOptions options)
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = (context, _, exception) => Level(context, exception);
        options.GetMessageTemplateProperties = (context, _, elapsed, statusCode) =>
        [
            new("RequestMethod", new ScalarValue(context.Request.Method)),
            new("RequestPath", new ScalarValue(SafePath(context))),
            new("StatusCode", new ScalarValue(statusCode)),
            new("Elapsed", new ScalarValue(elapsed))
        ];
        options.EnrichDiagnosticContext = (diagnostics, context) =>
        {
            diagnostics.Set("RequestId", context.TraceIdentifier);
        };
    }

    // Keep matched route values and all query strings private. For unmatched requests,
    // retain the diagnostic path, redacting known private paths and account IDs.
    public static string SafePath(HttpContext context)
    {
        if ((context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText is { } route) return route;
        var path = context.Request.Path.Value ?? "/";
        var segments = path.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = Uri.UnescapeDataString(segments[i]);
            if (segment.Equals("referrals", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("referral", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("media", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < segments.Length) return LimitPath(string.Join('/', segments.Take(i + 1)) + "/[redacted]");
            }
            if (Guid.TryParse(segment, out _)) segments[i] = "[id]";
        }
        return LimitPath(string.Join('/', segments));
    }

    private static string LimitPath(string path) =>
        new(path.Take(256).Select(c => char.IsControl(c) ? '_' : c).ToArray());

    public static LogEventLevel Level(HttpContext context, Exception? exception) =>
        exception != null || context.Response.StatusCode >= 500 ? LogEventLevel.Error :
        context.Response.StatusCode == 404 && IsWordPressManifestProbe(context.Request.Path) ? LogEventLevel.Debug :
        context.Response.StatusCode >= 400 ? LogEventLevel.Warning :
        context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Debug : LogEventLevel.Information;

    private static bool IsWordPressManifestProbe(PathString path) =>
        path.Value?.EndsWith("/wp-includes/wlwmanifest.xml", StringComparison.OrdinalIgnoreCase) == true;
}
