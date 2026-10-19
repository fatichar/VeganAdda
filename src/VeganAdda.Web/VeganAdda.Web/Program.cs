using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Components;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 3 * 1024 * 1024);
var dev = builder.Environment.IsDevelopment();
if (dev)
    Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", Environment.GetEnvironmentVariable("FIREBASE_ADMIN_CREDENTIALS_VEGANADDA"));
if (!dev && (builder.Configuration.GetValue<bool>("Development:Enabled") || builder.Configuration.GetValue<bool>("Development:Seed")))
    throw new InvalidOperationException("Development authentication and fixtures are forbidden outside Development.");
var connection = builder.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("ConnectionStrings__Database is required.");
var firebaseProjectId = builder.Configuration["Authentication:Firebase:ProjectId"];
var firebaseApiKey = builder.Configuration["Authentication:Firebase:ApiKey"];
var firebaseAuthDomain = builder.Configuration["Authentication:Firebase:AuthDomain"];
var firebaseAppId = builder.Configuration["Authentication:Firebase:AppId"];
var firebaseConfigured = new[] { firebaseProjectId, firebaseApiKey, firebaseAuthDomain, firebaseAppId }.All(x => !string.IsNullOrWhiteSpace(x));
var migrationOnly = args.Contains("--migrate", StringComparer.Ordinal);
if (!dev && !migrationOnly && !firebaseConfigured) throw new InvalidOperationException("Authentication:Firebase project ID, API key, auth domain and app ID are required.");
if (!dev && !migrationOnly && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")))
    throw new InvalidOperationException("GOOGLE_APPLICATION_CREDENTIALS must point to a mounted Firebase Admin service-account file.");
builder.Services.AddDbContext<CommunityDb>(o => o.UseNpgsql(connection));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IActor, HttpActor>(); builder.Services.AddScoped<CommunityService>();
builder.Services.AddScoped<ICommunityApi, ServerApi>(); builder.Services.AddScoped<IPlatform, ServerPlatform>();
builder.Services.AddScoped<IChangeBus, ChangeBus>();
builder.Services.AddSingleton<IFirebaseIdentityVerifier, FirebaseIdentityVerifier>();
builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
builder.Services.AddSignalR(o => { o.EnableDetailedErrors = false; o.MaximumReceiveMessageSize = 4096; });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.SecurePolicy = dev ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
var keys = builder.Configuration["Storage:KeysPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
Directory.CreateDirectory(keys);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keys)).SetApplicationName("VeganAdda");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = dev ? "VeganAdda.Dev.Session" : "__Host-VeganAdda.Session";
    o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = dev ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromDays(7); o.SlidingExpiration = true; o.LoginPath = "/signin";
    o.Events.OnRedirectToLogin = c => { if (c.Request.Path.StartsWithSegments("/api") || c.Request.Path.StartsWithSegments("/hubs")) c.Response.StatusCode = 401; else c.Response.Redirect(c.RedirectUri); return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = async c =>
    {
        var db = c.HttpContext.RequestServices.GetRequiredService<CommunityDb>();
        if (!Guid.TryParse(c.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || !await db.Accounts.AnyAsync(a => a.Id == id && !a.Suspended))
        { c.RejectPrincipal(); await c.HttpContext.SignOutAsync(); }
    };
});
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    var proxy = builder.Configuration["Hosting:TrustedProxy"];
    if (!string.IsNullOrEmpty(proxy)) o.KnownProxies.Add(IPAddress.Parse(proxy));
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        if (!ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/auth") && !ctx.Request.Path.StartsWithSegments("/hubs"))
            return RateLimitPartition.GetNoLimiter("static-and-pages");
        var sensitive = HttpMethods.IsPost(ctx.Request.Method) || HttpMethods.IsDelete(ctx.Request.Method);
        var message = sensitive && ctx.Request.Path.Value?.EndsWith("/messages", StringComparison.Ordinal) == true;
        var transport = ctx.Request.Path.StartsWithSegments("/hubs");
        var readmark = sensitive && ctx.Request.Path.StartsWithSegments("/api/conversations") && ctx.Request.Path.Value!.EndsWith("/read", StringComparison.Ordinal);
        var request = sensitive && ctx.Request.Path == "/api/connections";
        var bucket = transport ? "transport" : readmark ? "readmark" : message ? "message" : request ? "request" : sensitive ? "write" : "read";
        var identity = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(identity + ":" + bucket, _ => new() { PermitLimit = transport ? 300 : readmark ? 240 : message ? 60 : request ? 15 : sensitive ? 30 : 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
    o.OnRejected = async (c, ct) => await c.HttpContext.Response.WriteAsJsonAsync(new { error = "Please slow down and try again in a minute." }, ct);
});
var app = builder.Build();
app.UseForwardedHeaders();
if (dev) app.UseWebAssemblyDebugging(); else { app.UseHsts(); app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpsRedirection()); }
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Headers.Origin.Count > 0 && ctx.Request.Headers.Origin.ToString() != $"{ctx.Request.Scheme}://{ctx.Request.Host}")
    { ctx.Response.StatusCode = 403; return; }
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    var cspNonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    ctx.Items["VeganAdda.CspNonce"] = cspNonce;
    // The free reverse-geocoding endpoint can redirect to BigDataCloud's api-bdc.io host.
    ctx.Response.Headers["Content-Security-Policy"] = $"default-src 'self'; script-src 'self' 'wasm-unsafe-eval' 'nonce-{cspNonce}' https://apis.google.com; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self' https://api.bigdatacloud.net https://api-bdc.io https://identitytoolkit.googleapis.com https://securetoken.googleapis.com https://www.googleapis.com ws: wss:; frame-src 'self' https://accounts.google.com https://*.firebaseapp.com; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (!ctx.Request.Path.StartsWithSegments("/_framework") && !ctx.Request.Path.StartsWithSegments("/_content")) ctx.Response.Headers.CacheControl = "no-cache, no-store";
    try { await next(); }
    catch (ApiException ex) when (!ctx.Response.HasStarted) { ctx.Response.StatusCode = ex.Status; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (AntiforgeryValidationException) when (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = "Your session changed. Refresh this page and try again." }); }
    catch (Exception ex) when (!ctx.Response.HasStarted)
    {
        app.Logger.LogError("Request failed. Type={ExceptionType} Trace={TraceId}", ex.GetType().Name, ctx.TraceIdentifier);
        ctx.Response.StatusCode = 500; await ctx.Response.WriteAsJsonAsync(new { error = "Something went wrong. Please try again.", traceId = ctx.TraceIdentifier });
    }
});
app.UseStaticFiles();
app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.UseAntiforgery();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (CommunityDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/api/csrf", (HttpContext ctx, IAntiforgery csrf) => Results.Ok(new { token = csrf.GetAndStoreTokens(ctx).RequestToken }));
app.MapGet("/api/auth/config", () => Results.Json(new FirebaseClientConfig(firebaseApiKey ?? "", firebaseAuthDomain ?? "", firebaseProjectId ?? "", firebaseAppId ?? "", firebaseConfigured), Wire.Json));
app.MapPost("/auth/firebase", async (HttpContext ctx, CommunityDb db, IAntiforgery csrf, IFirebaseIdentityVerifier verifier, FirebaseLoginInput? input) =>
{
    await csrf.ValidateRequestAsync(ctx);
    if (!firebaseConfigured) return Results.Json(new { error = "Firebase Authentication is not configured for this environment." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    if (input?.IdToken is not { Length: > 0 and <= 16000 } idToken) throw new ApiException(400, "Google sign-in couldn't be completed. Please try again.");
    var identity = await verifier.Verify(idToken);
    if (identity is null || identity.Provider is not ("google.com" or "phone")) return Results.Unauthorized();
    var subject = $"firebase:{firebaseProjectId}:{identity.Uid}";
    await using var tx = await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({subject}, 0))");
    var account = await db.Accounts.SingleOrDefaultAsync(a => a.Subject == subject);
    if (account == null)
    {
        var name = CommunityService.Text(identity.Name, 80);
        account = new() { Subject = subject, Name = name.Length == 0 ? "New member" : name, Admin = builder.Configuration["Authentication:AdminFirebaseUid"] == identity.Uid };
        db.Accounts.Add(account); await db.SaveChangesAsync();
    }
    if (account.Suspended) return Results.StatusCode(StatusCodes.Status403Forbidden);
    await tx.CommitAsync();
    await ctx.SignInAsync(Principal(account));
    return Results.Ok(new Ok());
});
app.MapPost("/auth/signout", async (HttpContext ctx, IAntiforgery csrf) => { await csrf.ValidateRequestAsync(ctx); await ctx.SignOutAsync(); return Results.Ok(new Ok()); });
if (dev && builder.Configuration.GetValue<bool>("Development:Enabled"))
{
    app.MapPost("/auth/dev/{persona}", async (string persona, HttpContext ctx, CommunityDb db, IAntiforgery csrf) =>
    {
        await csrf.ValidateRequestAsync(ctx);
        if (!DevelopmentSeed.Subjects.Contains(persona)) return Results.NotFound();
        var account = await db.Accounts.SingleAsync(a => a.Subject == "dev:" + persona);
        if (account.Suspended) return Results.Forbid();
        await ctx.SignInAsync(Principal(account)); return Results.Ok(new Ok());
    });
}
app.MapMethods("/api/{**resource}", ["GET", "POST", "DELETE"], async (string resource, HttpContext ctx, CommunityService service, IAntiforgery csrf) =>
{
    System.Text.Json.JsonElement? body = null;
    if (ctx.Request.Method != "GET")
    {
        await csrf.ValidateRequestAsync(ctx);
        if (ctx.Request.ContentLength > 32000) throw new ApiException(413, "This request is too large.");
        // JsonContent and browser requests may be chunked with no Content-Length.
        var bytes = new byte[32001]; var length = 0;
        while (length < bytes.Length)
        { var read = await ctx.Request.Body.ReadAsync(bytes.AsMemory(length)); if (read == 0) break; length += read; }
        if (length > 32000) throw new ApiException(413, "This request is too large.");
        if (length > 0)
        { try { body = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(bytes.AsSpan(0, length)); } catch (System.Text.Json.JsonException) { throw new ApiException(400, "Please check your input."); } }
    }
    try
    {
        var result = await service.Execute(ctx.Request.Method, resource + ctx.Request.QueryString, body);
        if (resource == "account" && ctx.Request.Method == "DELETE") await ctx.SignOutAsync();
        return Results.Json(result, Wire.Json);
    }
    catch (FormatException) { throw new ApiException(400, "This identifier is invalid."); }
});
app.MapMedia();
app.MapHub<CommunityHub>("/hubs/community").RequireAuthorization();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveWebAssemblyRenderMode().AddAdditionalAssemblies(typeof(VeganAdda.Web.Client._Imports).Assembly, typeof(VeganAdda.UI._Imports).Assembly);
if (args.Contains("--migrate"))
{
    await using var scope = app.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<CommunityDb>().Database.MigrateAsync(); return;
}
if (dev)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CommunityDb>();
    await db.Database.MigrateAsync();
    if (builder.Configuration.GetValue<bool>("Development:Seed"))
        await DevelopmentSeed.Run(db, builder.Configuration, builder.Environment.ContentRootPath);
}
else
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CommunityDb>();
    if (await db.Accounts.AnyAsync(a => a.Demo) || await db.Events.AnyAsync(e => e.Demo)) throw new InvalidOperationException("Production cannot use a database containing development fixtures.");
}
app.Run();

static ClaimsPrincipal Principal(Account account) => new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, account.Id.ToString()), new(ClaimTypes.Name, account.Name)], CookieAuthenticationDefaults.AuthenticationScheme));
public partial class Program;
