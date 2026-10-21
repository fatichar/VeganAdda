using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace VeganAdda.Web.Observability;

public static class AspireDashboardLogging
{
    public static void Configure(LoggerConfiguration logger, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var endpoint = configuration["Monitoring:Aspire:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        if (!environment.IsDevelopment() ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/")
            throw new InvalidOperationException("Aspire dashboard export is supported only from Development to a loopback HTTP endpoint.");

        var destination = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.OpenTelemetry(
                endpoint: uri.ToString(),
                protocol: OtlpProtocol.Grpc,
                resourceAttributes: new Dictionary<string, object> { ["service.name"] = "VeganAdda.Web" })
            .CreateLogger();
        logger.WriteTo.Sink(new SafeOperationalLogSink(destination), LogEventLevel.Information);
    }
}
