using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace VeganAdda.Web.Observability;

public static class BetterStackLogging
{
    public static void Configure(LoggerConfiguration logger, IConfiguration configuration)
    {
        var token = configuration["Monitoring:BetterStack:SourceToken"];
        var endpoint = configuration["Monitoring:BetterStack:Endpoint"];
        if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(endpoint)) return;
        if (string.IsNullOrWhiteSpace(token) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || !uri.Host.EndsWith(".betterstackdata.com", StringComparison.OrdinalIgnoreCase) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/")
            throw new InvalidOperationException("Better Stack requires a source token and its HTTPS betterstackdata.com ingestion origin.");

        var destination = new LoggerConfiguration().MinimumLevel.Warning()
            .WriteTo.BetterStack(sourceToken: token, betterStackEndpoint: uri.GetLeftPart(UriPartial.Authority),
                queueLimitBytes: 5 * 1024 * 1024, retries: 3, maxFlushTime: TimeSpan.FromSeconds(5))
            .CreateLogger();
        logger.WriteTo.Sink(new SafeOperationalLogSink(destination), LogEventLevel.Warning);
    }
}

// Keep raw framework logs local. External operational events use a property
// allowlist and never include exception messages, data, or request scope values.
public sealed class SafeOperationalLogSink(Logger destination) : ILogEventSink, IDisposable
{
    private static readonly HashSet<string> Allowed =
    ["Application", "Environment", "SourceContext", "RequestId", "TraceId", "ExceptionType", "StackTrace", "Type",
     "StatusCode", "Elapsed", "RequestMethod"];

    public static LogEvent? Sanitize(LogEvent source)
    {
        var category = source.Properties.GetValueOrDefault("SourceContext") is ScalarValue { Value: string value } ? value : "";
        var request = category == "Serilog.AspNetCore.RequestLoggingMiddleware";
        if (source.Level < LogEventLevel.Warning ||
            !(category.StartsWith("VeganAdda.", StringComparison.Ordinal) || request || source.Level == LogEventLevel.Fatal)) return null;
        var properties = source.Properties
            .Where(p => Allowed.Contains(p.Key) || (request && p.Key == "RequestPath"))
            .Select(p => new LogEventProperty(p.Key, p.Value)).ToList();
        if (source.Exception is not null && !source.Properties.ContainsKey("ExceptionType"))
            properties.Add(new("ExceptionType", new ScalarValue(source.Exception.GetType().Name)));
        return new LogEvent(source.Timestamp, source.Level, null, source.MessageTemplate, properties,
            source.TraceId ?? default, source.SpanId ?? default);
    }

    public void Emit(LogEvent logEvent)
    {
        if (Sanitize(logEvent) is { } safe) destination.Write(safe);
    }

    public void Dispose() => destination.Dispose();
}
