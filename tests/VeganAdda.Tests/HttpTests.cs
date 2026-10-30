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
using VeganAdda.Web.Observability;
using Serilog.Core;
using Serilog.Events;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class HttpTests : IDisposable
{
    private readonly DatabaseFixture f;
    private readonly WebApplicationFactory<Program> factory;
    private readonly string storage = Path.Combine(Path.GetTempPath(), "veganadda-http-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> oldFirebaseEnvironment = new();
    private readonly CapturedLogs logs = new();
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
                s.AddSingleton<ILogEventSink>(logs);
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
    [Fact]
    public async Task Verification_routes_enforce_csrf_and_keep_evidence_out_of_public_and_chat_projections()
    {
        var applicant = await f.Member(); var referrer = await f.Member(); var admin = await f.Member(admin: true);
        await using (var db = f.Db())
        {
            (await db.Profiles.FindAsync(applicant))!.VeganVerified = false;
            db.Relationships.Add(new() { Key = VeganAdda.Web.Features.Shared.FeatureService.Pair(applicant, referrer), A = applicant, B = referrer, State = "accepted", RequestedBy = applicant });
            await db.SaveChangesAsync();
        }
        using var member = Client(applicant); using var connection = Client(referrer); using var reviewer = Client(admin); using var anon = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/verification")).StatusCode);
        var input = new VerificationSubmission("Private verification explanation", ["https://example.org/private-evidence"], referrer, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync("/api/verification", input)).StatusCode);
        var initial = await member.GetFromJsonAsync<VerificationStatus>("/api/verification");
        Assert.Equal(VerificationState.NotRequested, initial!.Status);
        await Post(member, "/api/verification", input);
        var owner = (await member.GetFromJsonAsync<VerificationStatus>("/api/verification"))!;
        var html = await member.GetStringAsync($"/people/{applicant}");
        Assert.Contains("Under review", html);
        Assert.DoesNotContain("This screen couldn&#x27;t be loaded", html);
        var cards = await connection.GetStringAsync($"/api/verification/referrals/{applicant}");
        Assert.DoesNotContain(input.Explanation, cards); Assert.DoesNotContain(input.EvidenceUrls[0], cards);
        var chat = await connection.GetStringAsync("/messages/" + VeganAdda.Web.Features.Shared.FeatureService.Pair(applicant, referrer));
        Assert.Contains("I confirm this person is vegan", chat);
        Assert.DoesNotContain(input.Explanation, chat);
        var publicPerson = await anon.GetStringAsync($"/api/people/{applicant}");
        Assert.DoesNotContain(input.Explanation, publicPerson); Assert.DoesNotContain(input.EvidenceUrls[0], publicPerson);
        var review = await reviewer.GetFromJsonAsync<ProfileReview>($"/api/admin/members/{applicant}");
        Assert.Equal(input.Explanation, review!.Verification!.Explanation);
        await Post(connection, $"/api/verification/referrals/{owner.Request!.Id}/respond", new VerificationResponse(true));
        Assert.False((await member.GetFromJsonAsync<ProfileInput>("/api/profile"))!.VeganVerified);
        await Post(reviewer, "/api/admin/profile-review", new VeganVerificationInput(applicant, true, RequestId: owner.Request.Id));
        Assert.Equal(VerificationState.Verified, (await member.GetFromJsonAsync<VerificationStatus>("/api/verification"))!.Status);
    }

    [Fact]
    public async Task Edit_profile_keeps_social_links_optional_and_gender_required_below_name()
    {
        var member = await f.Member();
        await using (var db = f.Db()) { (await db.Profiles.FindAsync(member))!.Gender = ""; await db.SaveChangesAsync(); }
        using var client = Client(member);
        var html = await client.GetStringAsync("/settings/profile");
        Assert.Contains("gender is now required", html);
        var basicStart = html.IndexOf("id=\"profile-panel-0\"", StringComparison.Ordinal);
        var optionalStart = html.IndexOf("id=\"profile-panel-1\"", StringComparison.Ordinal);
        var preferencesStart = html.IndexOf("id=\"profile-panel-2\"", StringComparison.Ordinal);
        var basic = html[basicStart..optionalStart];
        var optional = html[optionalStart..preferencesStart];
        Assert.True(basic.IndexOf("id=\"name\"", StringComparison.Ordinal) < basic.IndexOf("id=\"gender\"", StringComparison.Ordinal));
        Assert.Contains("id=\"gender\" required", basic);
        Assert.Contains("Fields marked", basic);
        Assert.DoesNotContain("id=\"instagram\"", basic);
        Assert.Contains("Social Links", optional);
        Assert.Contains("id=\"instagram\"", optional);
        Assert.Contains("id=\"linkedin\"", optional);
        Assert.Contains("id=\"website\"", optional);
        Assert.DoesNotContain("Fields marked", optional);
        Assert.DoesNotContain("(optional)", optional);
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
    [Fact] public async Task Profile_edits_keep_verification_and_moderation_actions_change_only_vegan_status()
    {
        var member = await f.Member();
        using var client = Client(member);
        var html = await client.GetStringAsync("/settings/profile");
        Assert.DoesNotContain("Profile changes may need another review.", html);
        Assert.DoesNotContain("Allow my public profile to be featured", html);
        var profile = (await client.GetFromJsonAsync<ProfileInput>("/api/profile"))!;
        profile.Bio = "Updated profile without losing verification";
        await Post(client, "/api/profile", profile);
        Assert.True((await client.GetFromJsonAsync<ProfileInput>("/api/profile"))!.VeganVerified);
        AssertLifecycle("ProfileUpdated", member, member);
        var admin = await f.Member(admin: true);
        using var reviewer = Client(admin);
        await Post(reviewer, "/api/admin/moderate", new ModerateInput(member, "remove-vegan-verification"));
        Assert.False((await client.GetFromJsonAsync<ProfileInput>("/api/profile"))!.VeganVerified);
        AssertLifecycle("VeganVerificationRemoved", member, admin);
        await Post(reviewer, "/api/admin/moderate", new ModerateInput(member, "confirm-vegan"));
        Assert.True((await client.GetFromJsonAsync<ProfileInput>("/api/profile"))!.VeganVerified);
        AssertLifecycle("VeganConfirmed", member, admin);
    }
    [Fact] public async Task Incomplete_profiles_show_a_prominent_gate_and_api_access_resumes_after_basic_setup()
    {
        var id = await f.Member();
        await using (var db = f.Db())
        {
            db.Profiles.Remove((await db.Profiles.FindAsync(id))!);
            await db.SaveChangesAsync();
        }
        using var client = Client(id);
        foreach (var route in new[] { "/", "/events", "/messages", "/saved" })
        {
            var html = await client.GetStringAsync(route);
            Assert.Contains("Complete your basic profile to use VeganAdda", html);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/people")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/events")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/privacy")).StatusCode);
        var onboarding = await client.GetStringAsync("/onboarding");
        Assert.Contains("Basic info", onboarding);
        Assert.Contains("Optional details", onboarding);
        Assert.Contains("Preferences", onboarding);
        Assert.DoesNotContain("I confirm I am at least 18 years old", onboarding);
        Assert.DoesNotContain("Allow my public profile to be featured", onboarding);
        // Old clients may still send removed fields. Neither consent nor verification is client-controlled.
        await Post(client, "/api/profile", new
        {
            Name = "Basic HTTP member", Gender = "Man", IsVegan = true, BirthYear = 1990, BirthMonth = 1,
            Country = "India", State = "Maharashtra", City = "Pune",
            FeatureConsent = false, VeganVerified = true
        });
        var session = (await client.GetFromJsonAsync<Session>("/api/session"))!;
        Assert.True(session.BasicProfileComplete);
        Assert.False(session.VeganVerified);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/people")).StatusCode);
        await using var check = f.Db();
        var profile = (await check.Profiles.FindAsync(id))!;
        Assert.True(profile.FeatureConsent);
        Assert.False(profile.Featured);
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
            var account = db.Accounts.Single(a => a.Subject == "firebase:test-firebase:test-google-uid");
            AssertLifecycle("AccountRegistered", account.Id, account.Id);
            Assert.Equal(2, logs.Events.Count(e => e.Properties.GetValueOrDefault("EventName") is ScalarValue { Value: "AccountSignedIn" }));
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
        Assert.True((await c.GetFromJsonAsync<ProfileInput>("/api/profile"))!.VeganVerified);
        using var anon = Client(); Assert.Equal(HttpStatusCode.OK,(await anon.GetAsync(path)).StatusCode);
        using var admin = Client(await f.Member(admin: true));
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, "/api/admin/profile-review", new VeganVerificationInput(user, true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await anon.GetAsync(path)).StatusCode);
    }
    [Fact] public async Task Reports_validate_reasons_and_keep_screenshots_private_and_owned()
    {
        var reporter = await f.Member(); var subject = await f.Member(); var stranger = await f.Member();
        using var member = Client(reporter); using var target = Client(subject); using var other = Client(stranger);
        using var admin = Client(await f.Member(admin: true)); using var anonymous = Client();
        using var bitmap = new SKBitmap(8, 8); bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        var token = await member.GetFromJsonAsync<Csrf>("/api/csrf");
        using var form = new MultipartFormDataContent(); var content = new ByteArrayContent(encoded.ToArray());
        content.Headers.ContentType = new("image/png"); form.Add(content, "file", "screenshot.png");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/upload?purpose=report") { Content = form };
        upload.Headers.Add("X-CSRF-TOKEN", token!.Token);
        var response = await member.SendAsync(upload); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var path = (await response.Content.ReadFromJsonAsync<UploadResult>())!.Path;
        Assert.Matches("^/report-media/[a-f0-9]{32}\\.jpg$", path);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(path)).StatusCode);
        foreach (var viewer in new[] { anonymous, target, other, admin }) Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync(path)).StatusCode);

        async Task<HttpResponseMessage> Report(HttpClient client, ReportInput input)
        {
            var csrf = await client.GetFromJsonAsync<Csrf>("/api/csrf");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/people/{subject}/report") { Content = JsonContent.Create(input) };
            request.Headers.Add("X-CSRF-TOKEN", csrf!.Token); return await client.SendAsync(request);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(member, new(subject, ""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(member, new(subject, "Unsupported"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(member, new(subject, "Other", new string('a', 1001)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(member, new(subject, "Other", Screenshot: "/media/" + path[14..]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Report(other, new(subject, "Spam", Screenshot: path))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Report(member, new(subject, "Harassment", "  Private details  ", path))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Report(member, new(subject, "Not a vegan"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(path)).StatusCode);
        var cache = (await admin.GetAsync(path)).Headers.CacheControl!;
        Assert.True(cache.Private); Assert.True(cache.NoStore);
        foreach (var viewer in new[] { anonymous, target, other }) Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync(path)).StatusCode);
        var reports = await admin.GetFromJsonAsync<AdminData>("/api/admin?section=Reports");
        var report = Assert.Single(reports!.Reports, r => r.Screenshot == path);
        Assert.Equal("Harassment", report.Reason); Assert.Equal("Private details", report.Details);
        Assert.DoesNotContain(path, await (await target.GetAsync($"/api/people/{reporter}")).Content.ReadAsStringAsync());
        var profile = await member.GetFromJsonAsync<ProfileInput>("/api/profile"); profile!.Photo = path;
        var csrfToken = await member.GetFromJsonAsync<Csrf>("/api/csrf");
        using var save = new HttpRequestMessage(HttpMethod.Post, "/api/profile") { Content = JsonContent.Create(profile) };
        save.Headers.Add("X-CSRF-TOKEN", csrfToken!.Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.SendAsync(save)).StatusCode);
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
    static string CommunityServicePair(Guid a, Guid b) => VeganAdda.Web.Features.Shared.FeatureService.Pair(a,b);
    public void Dispose() { factory.Dispose(); foreach (var item in oldFirebaseEnvironment) Environment.SetEnvironmentVariable(item.Key, item.Value); if (Directory.Exists(storage)) Directory.Delete(storage, true); }
    record Csrf(string Token);
    record UploadResult(string Path);
    void AssertLifecycle(string eventName, Guid accountId, Guid actorId)
    {
        var entry = Assert.Single(logs.Events, e => e.Properties.GetValueOrDefault("EventName") is ScalarValue { Value: string name } && name == eventName);
        var safe = Assert.IsType<LogEvent>(SafeOperationalLogSink.Sanitize(entry));
        Assert.Equal(accountId, ((ScalarValue)safe.Properties["AccountId"]).Value);
        Assert.Equal(actorId, ((ScalarValue)safe.Properties["ActorId"]).Value);
        Assert.DoesNotContain("Updated profile for review", safe.RenderMessage());
    }
    private sealed class CapturedLogs : ILogEventSink
    {
        public System.Collections.Concurrent.ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
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

