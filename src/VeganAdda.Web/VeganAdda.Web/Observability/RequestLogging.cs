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

    // Route values (including referral codes) and query strings stay out of logs.
    public static string SafePath(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "[unmatched]";

    public static LogEventLevel Level(HttpContext context, Exception? exception) =>
        exception != null || context.Response.StatusCode >= 500 ? LogEventLevel.Error :
        context.Response.StatusCode >= 400 ? LogEventLevel.Warning :
        context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Debug : LogEventLevel.Information;
}
