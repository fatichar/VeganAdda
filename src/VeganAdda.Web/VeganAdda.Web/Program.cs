using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
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
if (!dev && (builder.Configuration.GetValue<bool>("Development:Enabled") || builder.Configuration.GetValue<bool>("Development:Seed")))
    throw new InvalidOperationException("Development authentication and fixtures are forbidden outside Development.");
var connection = builder.Configuration.GetConnectionString("Database") ?? (dev ? "Host=127.0.0.1;Port=55432;Database=veganadda;Username=veganadda" : throw new InvalidOperationException("ConnectionStrings__Database is required."));
var googleId = builder.Configuration["Authentication:Google:ClientId"];
var googleSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!dev && (string.IsNullOrWhiteSpace(googleId) || string.IsNullOrWhiteSpace(googleSecret))) throw new InvalidOperationException("Production Google OAuth credentials are required.");
builder.Services.AddDbContext<CommunityDb>(o => o.UseNpgsql(connection));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IActor, HttpActor>(); builder.Services.AddScoped<CommunityService>();
builder.Services.AddScoped<ICommunityApi, ServerApi>(); builder.Services.AddScoped<IPlatform, ServerPlatform>();
builder.Services.AddScoped<IChangeBus, ChangeBus>();
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
}).AddGoogle(o =>
{
    o.ClientId = googleId ?? "not-configured"; o.ClientSecret = googleSecret ?? "not-configured"; o.SaveTokens = false;
    o.Events.OnCreatingTicket = async c =>
    {
        var subject = c.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Google did not provide a subject.");
        var db = c.HttpContext.RequestServices.GetRequiredService<CommunityDb>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"google:" + subject}, 0))");
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Subject == "google:" + subject);
        if (account == null)
        {
            account = new() { Subject = "google:" + subject, Name = CommunityService.Text(c.Principal?.FindFirstValue(ClaimTypes.Name), 80), Admin = builder.Configuration["Authentication:AdminGoogleSubject"] == subject };
            db.Accounts.Add(account); await db.SaveChangesAsync();
        }
        if (account.Suspended) throw new ApiException(403, "This account is suspended.");
        await tx.CommitAsync(); c.Principal = Principal(account);
    };
    o.Events.OnRemoteFailure = c => { c.HandleResponse(); c.Response.Redirect("/signin?error=google"); return Task.CompletedTask; };
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
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
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
app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.UseAntiforgery();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (CommunityDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/api/csrf", (HttpContext ctx, IAntiforgery csrf) => Results.Ok(new { token = csrf.GetAndStoreTokens(ctx).RequestToken }));
app.MapGet("/auth/google", (string? returnUrl) => string.IsNullOrEmpty(googleId) ? Results.Redirect("/signin?error=configuration") : Results.Challenge(new AuthenticationProperties { RedirectUri = "/auth/complete?returnUrl=" + Uri.EscapeDataString(SafeReturn(returnUrl)) }, [GoogleDefaults.AuthenticationScheme]));
app.MapGet("/auth/complete", async (HttpContext ctx, CommunityDb db, string? returnUrl) =>
{
    if (!Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Results.Redirect("/signin");
    var target = SafeReturn(returnUrl);
    return Results.Redirect(await db.Profiles.AnyAsync(p => p.Id == id) ? target : "/onboarding?returnUrl=" + Uri.EscapeDataString(target));
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
    var db = scope.ServiceProvider.GetRequiredService<CommunityDb>(); await db.Database.MigrateAsync();
    if (builder.Configuration.GetValue<bool>("Development:Seed")) await DevelopmentSeed.Run(db, builder.Configuration, builder.Environment.ContentRootPath);
}
else
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CommunityDb>();
    if (await db.Accounts.AnyAsync(a => a.Demo) || await db.Events.AnyAsync(e => e.Demo)) throw new InvalidOperationException("Production cannot use a database containing development fixtures.");
}
app.Run();

static string SafeReturn(string? value) => value is { Length: > 0 } && value.StartsWith('/') && !value.StartsWith("//") && !value.Contains('\\') && !value.Any(char.IsControl) ? value : "/";
static ClaimsPrincipal Principal(Account account) => new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, account.Id.ToString()), new(ClaimTypes.Name, account.Name)], CookieAuthenticationDefaults.AuthenticationScheme));
public partial class Program;
