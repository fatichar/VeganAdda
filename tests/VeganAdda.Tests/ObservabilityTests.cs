using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using VeganAdda.Web.Observability;

namespace VeganAdda.Tests;

public class ObservabilityTests
{
    [Fact]
    public void Lifecycle_information_reaches_external_sink_with_account_correlation_only()
    {
        var accountId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var source = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null,
            new MessageTemplateParser().Parse("Lifecycle {EventName} for account {AccountId} by actor {ActorId}"),
            [new("SourceContext", new ScalarValue(LifecycleEvents.Category)),
             new("EventName", new ScalarValue("ProfileApproved")),
             new("AccountId", new ScalarValue(accountId)),
             new("ActorId", new ScalarValue(actorId)),
             new("Name", new ScalarValue("private-name")),
             new("RequestPath", new ScalarValue("/referrals/private-code"))]);
        var safe = Assert.IsType<LogEvent>(SafeOperationalLogSink.Sanitize(source));
        Assert.Equal(accountId, ((ScalarValue)safe.Properties["AccountId"]).Value);
        Assert.Equal(actorId, ((ScalarValue)safe.Properties["ActorId"]).Value);
        Assert.Contains("ProfileApproved", safe.RenderMessage());
        Assert.DoesNotContain("private", string.Join(" ", safe.Properties));
        foreach (var category in new[] { "VeganAdda.Web.Features.Profiles.ProfilesService", "Serilog.AspNetCore.RequestLoggingMiddleware", "Microsoft.Hosting.Lifetime" })
        {
            source.AddOrUpdateProperty(new("SourceContext", new ScalarValue(category)));
            Assert.Null(SafeOperationalLogSink.Sanitize(source));
        }
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void Analytics_does_not_load_outside_production(string environment) =>
        Assert.Null(AnalyticsSettings.FromConfiguration(Config(("Analytics:Umami:ScriptUrl", "https://analytics.example/script.js"),
            ("Analytics:Umami:WebsiteId", Guid.NewGuid().ToString())), new TestEnvironment(environment)));

    [Fact]
    public void Analytics_requires_explicit_configuration_and_validates_csp_origin()
    {
        var environment = new TestEnvironment("Production");
        Assert.Null(AnalyticsSettings.FromConfiguration(Config(), environment));
        var website = Guid.NewGuid().ToString();
        var settings = AnalyticsSettings.FromConfiguration(Config(("Analytics:Umami:ScriptUrl", "https://analytics.example/script.js"),
            ("Analytics:Umami:WebsiteId", website)), environment);
        Assert.Equal("https://analytics.example", settings!.Origin);
        foreach (var url in new[] { "http://analytics.example/script.js", "https://user:pass@analytics.example/script.js", "https://analytics.example/script.js?token=private", "https://analytics.example/script.js#value" })
            Assert.Throws<InvalidOperationException>(() => AnalyticsSettings.FromConfiguration(Config(
                ("Analytics:Umami:ScriptUrl", url), ("Analytics:Umami:WebsiteId", website)), environment));
    }

    [Fact]
    public void External_logs_drop_framework_events_exception_messages_and_unknown_properties()
    {
        var source = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Error, new Exception("private-token"),
            new MessageTemplateParser().Parse("Failure {ExceptionType}"),
            [new("SourceContext", new ScalarValue("VeganAdda.Web.Services.Example")),
             new("ExceptionType", new ScalarValue("Exception")),
             new("RequestPath", new ScalarValue("/referrals/private-code")),
             new("FirebaseError", new ScalarValue("private-token")),
             new("UserId", new ScalarValue("member-id"))]);
        var safe = SafeOperationalLogSink.Sanitize(source)!;
        Assert.NotNull(safe);
        Assert.Null(safe.Exception);
        Assert.DoesNotContain("private", string.Join(" ", safe.Properties));
        Assert.False(safe.Properties.ContainsKey("UserId"));
        source.AddOrUpdateProperty(new("SourceContext", new ScalarValue("Microsoft.EntityFrameworkCore.Database.Command")));
        Assert.Null(SafeOperationalLogSink.Sanitize(source));
    }

    [Fact]
    public void Better_stack_is_optional_and_rejects_wrong_or_partial_ingestion_settings()
    {
        BetterStackLogging.Configure(new LoggerConfiguration(), Config());
        Assert.Throws<InvalidOperationException>(() => BetterStackLogging.Configure(new LoggerConfiguration(),
            Config(("Monitoring:BetterStack:SourceToken", "test-token"))));
        Assert.Throws<InvalidOperationException>(() => BetterStackLogging.Configure(new LoggerConfiguration(),
            Config(("Monitoring:BetterStack:SourceToken", "test-token"), ("Monitoring:BetterStack:Endpoint", "https://unrelated.example"))));
    }

    private sealed class TestEnvironment(string environment) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environment;
        public string ApplicationName { get; set; } = "VeganAdda.Web";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
