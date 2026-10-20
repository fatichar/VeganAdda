using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using VeganAdda.Contracts;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class HttpTests : IDisposable
{
    private readonly DatabaseFixture f;
    private readonly WebApplicationFactory<Program> factory;
    private readonly string storage = Path.Combine(Path.GetTempPath(), "veganadda-http-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> oldFirebaseEnvironment = new();
    public HttpTests(DatabaseFixture database)
    {
        f = database;
        SetFirebaseEnvironment("Authentication__Firebase__ProjectId", "test-firebase");
        SetFirebaseEnvironment("Authentication__Firebase__ApiKey", "public-test-key");
        SetFirebaseEnvironment("Authentication__Firebase__AuthDomain", "test-firebase.firebaseapp.com");
        SetFirebaseEnvironment("Authentication__Firebase__AppId", "1:test:web:test");
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Database"] = f.Connection, ["Development:Enabled"] = "false", ["Development:Seed"] = "false", ["Storage:MediaPath"] = storage, ["Storage:KeysPath"] = Path.Combine(storage, "keys"), ["Authentication:Firebase:ProjectId"] = "test-firebase", ["Authentication:Firebase:ApiKey"] = "public-test-key", ["Authentication:Firebase:AuthDomain"] = "test-firebase.firebaseapp.com", ["Authentication:Firebase:AppId"] = "1:test:web:test" }));
            b.ConfigureTestServices(s =>
            {
                var pushWorker = s.FirstOrDefault(d => d.ImplementationType == typeof(VeganAdda.Web.Services.ChatPushWorker));
                if (pushWorker != null) s.Remove(pushWorker);
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; });
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<VeganAdda.Web.Services.IFirebaseIdentityVerifier>(s);
                s.AddSingleton<VeganAdda.Web.Services.IFirebaseIdentityVerifier, TestFirebaseVerifier>();
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<Microsoft.EntityFrameworkCore.DbContextOptions<VeganAdda.Web.Data.CommunityDb>>(s);
                s.AddDbContext<VeganAdda.Web.Data.CommunityDb>(o => Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions.UseNpgsql(o, f.Connection));
            });
        });
    }
    void SetFirebaseEnvironment(string key, string value)
    {
        oldFirebaseEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }
    HttpClient Client(Guid? id = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (id.HasValue) client.DefaultRequestHeaders.Add("X-Test-User", id.ToString()); return client;
    }
    async Task<HttpResponseMessage> Post(HttpClient c, string path, object body)
    {
        var token = await c.GetFromJsonAsync<Csrf>("/api/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token!.Token); var response = await c.SendAsync(request); if (!response.IsSuccessStatusCode) throw new Exception($"HTTP {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"); return response;
    }
    [Fact] public async Task Http_requires_csrf_and_member_identity_and_public_html_prerenders()
    {
        using var anon = Client(); using var c = Client(await f.Member());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/connections")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/connections", new ConnectionInput(await f.Member()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(c, "/api/connections", new ConnectionInput(await f.Member()))).StatusCode);
        var html = await c.GetStringAsync("/"); Assert.Contains("Find your vegan people.", html); Assert.DoesNotContain("This screen couldn&#x27;t be loaded", html);
        Assert.Contains("no-store", (await c.GetAsync("/settings")).Headers.CacheControl!.ToString());
    }
    [Fact] public async Task Location_lookup_policy_allows_the_providers_redirect_host()
    {
        using var client = Client();
        using var response = await client.GetAsync("/settings/profile");
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        var connect = policy.Split(';', StringSplitOptions.TrimEntries).Single(d => d.StartsWith("connect-src "));
        var origins = connect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("https://api.bigdatacloud.net", origins);
        Assert.Contains("https://api-bdc.io", origins);
        Assert.DoesNotContain("*", connect);
    }
    [Fact] public async Task Production_analytics_scripts_and_csp_are_enabled_only_with_configuration()
    {
        SetFirebaseEnvironment("GOOGLE_APPLICATION_CREDENTIALS", "unused-in-this-render-test.json");
        // Program validates these before WebApplicationFactory applies its late
        // ConfigureAppConfiguration callback. Supply startup inputs explicitly.
        SetFirebaseEnvironment("ConnectionStrings__Database", f.Connection);
        SetFirebaseEnvironment("Storage__KeysPath", Path.Combine(storage, "keys"));
        SetFirebaseEnvironment("Analytics__Umami__ScriptUrl", "https://analytics.greenfit.in/script.js");
        SetFirebaseEnvironment("Analytics__Umami__WebsiteId", "29d3948e-23fe-4394-b433-799a76a4b09d");
        using var production = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Analytics:Umami:ScriptUrl"] = "https://analytics.greenfit.in/script.js",
                ["Analytics:Umami:WebsiteId"] = "29d3948e-23fe-4394-b433-799a76a4b09d"
            }));
        });
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/privacy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-before-send=\"veganAddaAnalyticsBeforeSend\"", html);
        Assert.Contains("data-exclude-search=\"true\"", html);
        Assert.True(html.IndexOf("js/analytics", StringComparison.Ordinal) < html.IndexOf("https://analytics.greenfit.in/script.js", StringComparison.Ordinal));
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("https://analytics.greenfit.in", policy.Split(';').Single(d => d.TrimStart().StartsWith("script-src ")));
        Assert.Contains("https://analytics.greenfit.in", policy.Split(';').Single(d => d.TrimStart().StartsWith("connect-src ")));
        using var development = Client();
        var developmentHtml = await development.GetStringAsync("/privacy");
        Assert.DoesNotContain("data-website-id", developmentHtml);
    }
    [Fact] public async Task Push_registration_requires_identity_csrf_and_moves_token_to_one_owner()
    {
        SetFirebaseEnvironment("Push__SenderId", "test-sender");
        SetFirebaseEnvironment("Push__VapidKey", "public-test-vapid");
        var a = await f.Member(); var b = await f.Member();
        using var anon = Client(); using var first = Client(a); using var second = Client(b);
        var registration = new { token = "test-token-" + Guid.NewGuid().ToString("N") };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/push/device")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/push/device", registration)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/push/device", registration)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(first, "/api/push/device", registration)).StatusCode);
        Assert.True((await first.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/push/device")).GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await Post(second, "/api/push/device", registration)).StatusCode);
        Assert.False((await first.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/push/device")).GetProperty("enabled").GetBoolean());
        Assert.True((await second.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/push/device")).GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await second.DeleteAsync("/api/push/device")).StatusCode);
        var csrf = await second.GetFromJsonAsync<Csrf>("/api/csrf");
        using var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/push/device"); delete.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        Assert.Equal(HttpStatusCode.OK, (await second.SendAsync(delete)).StatusCode);
        Assert.False((await second.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/push/device")).GetProperty("enabled").GetBoolean());
        await Post(second, "/api/push/device", registration);
        await Post(second, "/auth/signout", new { });
        Assert.False((await second.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/push/device")).GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAllowFailure(first, "/api/push/device", new { token = new string('a', 2001) })).StatusCode);
        var sw = await anon.GetAsync("/firebase-messaging-sw.js");
        Assert.Equal("application/javascript", sw.Content.Headers.ContentType!.MediaType);
        Assert.Contains("/js/push-worker.js", await sw.Content.ReadAsStringAsync());
    }
    [Fact] public async Task Firebase_login_requires_csrf_rejects_invalid_tokens_and_creates_one_account_per_uid()
    {
        using var c = Client();
        var authConfig = await c.GetFromJsonAsync<FirebaseClientConfig>("/api/auth/config");
        Assert.True(authConfig?.Configured, $"Firebase test config was {System.Text.Json.JsonSerializer.Serialize(authConfig)}");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/auth/firebase", new FirebaseLoginInput("good-google"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAllowFailure(c, "/auth/firebase", new FirebaseLoginInput("bad-token"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAllowFailure(c, "/auth/firebase", new FirebaseLoginInput("other-provider"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(c, "/auth/firebase", new FirebaseLoginInput("good-google"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(c, "/auth/firebase", new FirebaseLoginInput("good-google"))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VeganAdda.Web.Data.CommunityDb>();
            Assert.Equal(1, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.Accounts.Where(a => a.Subject == "firebase:test-firebase:test-google-uid")));
        }
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await c.PostAsJsonAsync("/auth/dev/aarav", new { })).StatusCode);
    }
    async Task<HttpResponseMessage> PostAllowFailure(HttpClient c, string path, object body)
    {
        var token = await c.GetFromJsonAsync<Csrf>("/api/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token!.Token); return await c.SendAsync(request);
    }
    [Fact] public async Task Static_downloads_do_not_exhaust_api_limits_but_connection_requests_are_limited()
    {
        using var c=Client(await f.Member());
        for(int i=0;i<250;i++) Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/api/session")).StatusCode);
        var recipient=await f.Member();
        for(int i=0;i<15;i++) await Post(c,"/api/connections",new ConnectionInput(recipient));
        var token=await c.GetFromJsonAsync<Csrf>("/api/csrf");
        using var req=new HttpRequestMessage(HttpMethod.Post,"/api/connections"){Content=JsonContent.Create(new ConnectionInput(recipient))}; req.Headers.Add("X-CSRF-TOKEN",token!.Token);
        Assert.Equal(HttpStatusCode.TooManyRequests,(await c.SendAsync(req)).StatusCode);
    }
    [Fact] public async Task Upload_rejects_fake_images_and_normalizes_valid_images_to_owned_jpeg()
    {
        var user = await f.Member(); using var c = Client(user);
        async Task<HttpResponseMessage> Upload(byte[] bytes)
        {
            var token = await c.GetFromJsonAsync<Csrf>("/api/csrf"); using var form = new MultipartFormDataContent();
            var content = new ByteArrayContent(bytes); content.Headers.ContentType = new("image/png"); form.Add(content, "file", "../../fake.png");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/upload") { Content = form }; req.Headers.Add("X-CSRF-TOKEN", token!.Token); return await c.SendAsync(req);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload("not an image"u8.ToArray())).StatusCode);
        using var bitmap = new SKBitmap(8, 8); bitmap.Erase(SKColors.Green); using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        var result = await Upload(encoded.ToArray()); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var path = (await result.Content.ReadFromJsonAsync<UploadResult>())!.Path;
        Assert.Matches("^/media/[a-f0-9]{32}\\.jpg$", path);
        Assert.Equal("image/jpeg", (await c.GetAsync(path)).Content.Headers.ContentType!.MediaType);
        using var other = Client(await f.Member()); Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path)).StatusCode);
        var profile = await c.GetFromJsonAsync<ProfileInput>("/api/profile"); profile!.Photo = path;
        Assert.Equal(HttpStatusCode.OK,(await Post(c,"/api/profile",profile)).StatusCode);
        using var anon = Client(); Assert.Equal(HttpStatusCode.NotFound,(await anon.GetAsync(path)).StatusCode);
        using var admin = Client(await f.Member(admin: true));
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, "/api/admin/profile-review", new ProfileReviewInput(user, true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await anon.GetAsync(path)).StatusCode);
    }
    [Fact] public async Task SignalR_authenticates_origin_and_delivers_invalidation_after_persisted_message()
    {
        var a = await f.Member(); var b = await f.Member(); using var ca = Client(a); using var cb = Client(b);
        await Post(ca,"/api/connections",new ConnectionInput(b)); await Post(cb,"/api/connections/accept",new ConnectionInput(a));
        using var anon = Client(); Assert.Equal(HttpStatusCode.Unauthorized,(await anon.PostAsync("/hubs/community/negotiate?negotiateVersion=1",null)).StatusCode);
        ca.DefaultRequestHeaders.Add("Origin","https://evil.test"); Assert.Equal(HttpStatusCode.Forbidden,(await ca.PostAsync("/hubs/community/negotiate?negotiateVersion=1",null)).StatusCode); ca.DefaultRequestHeaders.Remove("Origin");
        await using var hub = new HubConnectionBuilder().WithUrl("http://localhost/hubs/community", o => { o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler(); o.Transports = HttpTransportType.LongPolling; o.Headers["X-Test-User"] = b.ToString(); }).Build();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); hub.On<string>("Changed", area => { Assert.Equal("Messaging", area); changed.TrySetResult(); }); await hub.StartAsync();
        var key = CommunityServicePair(a,b);
        var response = await Post(ca,$"/api/conversations/{key}/messages",new MessageInput(Guid.NewGuid(),"Realtime integration test")); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var history = await cb.GetFromJsonAsync<MessagePage>($"/api/conversations/{key}/messages"); Assert.Single(history!.Items);
        await hub.StopAsync();
    }
    static string CommunityServicePair(Guid a, Guid b) => VeganAdda.Web.Services.CommunityService.Pair(a,b);
    public void Dispose() { factory.Dispose(); foreach (var item in oldFirebaseEnvironment) Environment.SetEnvironmentVariable(item.Key, item.Value); if (Directory.Exists(storage)) Directory.Delete(storage, true); }
    record Csrf(string Token);
    record UploadResult(string Path);
    private sealed class TestFirebaseVerifier : VeganAdda.Web.Services.IFirebaseIdentityVerifier
    {
        public Task<VeganAdda.Web.Services.VerifiedFirebaseIdentity?> Verify(string idToken) => Task.FromResult<VeganAdda.Web.Services.VerifiedFirebaseIdentity?>(idToken switch
        {
            "good-google" => new("test-google-uid", "Test Member", "google.com"),
            "other-provider" => new("test-other-uid", "Other Provider", "password"),
            _ => null
        });
    }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Test-User"],out var id)) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,id.ToString())],"Test")),"Test")));
        }
    }
}

