using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Serilog.AspNetCore;
using Serilog.Events;
using VeganAdda.Web.Observability;

namespace VeganAdda.Tests;

public class RequestLoggingTests
{
    [Fact]
    public void Completion_properties_exclude_private_path_values_and_query_strings()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/referrals/private-code";
        context.Request.QueryString = new("?token=secret&city=private-city");
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("/referrals/{code}"), 0, EndpointMetadataCollection.Empty, "Referral"));
        var options = new RequestLoggingOptions();
        RequestLogging.Configure(options);
        var properties = options.GetMessageTemplateProperties(context, context.Request.Path, 25, 500).ToDictionary(p => p.Name);
        Assert.Equal("/referrals/{code}", ((ScalarValue)properties["RequestPath"].Value).Value);
        Assert.DoesNotContain("private-code", string.Join(" ", properties.Values));
        Assert.DoesNotContain("secret", string.Join(" ", properties.Values));
        context.SetEndpoint(null);
        Assert.Equal("[unmatched]", RequestLogging.SafePath(context));
    }

    [Theory]
    [InlineData("/health/ready", 200, LogEventLevel.Debug)]
    [InlineData("/health/ready", 503, LogEventLevel.Error)]
    [InlineData("/api/profiles", 500, LogEventLevel.Error)]
    [InlineData("/api/profiles", 429, LogEventLevel.Warning)]
    [InlineData("/api/profiles", 200, LogEventLevel.Information)]
    public void Failures_remain_visible_while_successful_health_checks_are_quiet(string path, int status, LogEventLevel expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;
        Assert.Equal(expected, RequestLogging.Level(context, null));
        Assert.Equal(LogEventLevel.Error, RequestLogging.Level(context, new InvalidOperationException()));
    }
}
