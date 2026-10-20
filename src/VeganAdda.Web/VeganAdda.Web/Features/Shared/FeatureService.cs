using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;

namespace VeganAdda.Web.Features.Shared;

public sealed class FeatureContext(CommunityDb db, IActor actor, IChangeBus changes,
    IConfiguration config, IWebHostEnvironment environment)
{
    public CommunityDb Db { get; } = db;
    public IActor Actor { get; } = actor;
    public IChangeBus Changes { get; } = changes;
    public IConfiguration Configuration { get; } = config;
    public IWebHostEnvironment Environment { get; } = environment;
}

public abstract class FeatureService(FeatureContext context)
{
    protected readonly CommunityDb db = context.Db;
    protected readonly IChangeBus changes = context.Changes;
    protected readonly IConfiguration config = context.Configuration;
    protected readonly IWebHostEnvironment env = context.Environment;
    protected Guid? Viewer => context.Actor.Id;
    protected static ApiException Missing() => new(404, "This item is no longer available.");
    protected static ApiException Denied() => new(403, "You don't have access to this action.");
    protected static T Read<T>(JsonElement? body) where T : class => body?.Deserialize<T>(Wire.Json) ?? throw new ApiException(400, "Please check the form and try again.");
    protected async Task<Account> User(bool profile = false, bool admin = false, bool moderator = false)
    {
        var u = await db.Accounts.SingleOrDefaultAsync(x => x.Id == Viewer) ?? throw new ApiException(401, "Sign in to continue.");
        if (u.Suspended) throw new ApiException(403, "Your account is suspended. Contact the community team for help.");
        if (profile && !await db.Profiles.AnyAsync(x => x.Id == u.Id)) throw new ApiException(409, "Create your profile first.");
        if (admin && !u.Admin) throw Denied();
        if (moderator && !u.Admin && (!u.Moderator || !await db.Profiles.AnyAsync(p => p.Id == u.Id && p.IsVegan == true && !p.VeganVerificationPending))) throw Denied();
        return u;
    }
    protected async Task ActiveViewer() { if (Viewer.HasValue) await User(); }
    protected async Task<bool> Blocked(Guid a, Guid b) => await db.Blocks.AnyAsync(x => (x.Owner == a && x.Subject == b) || (x.Owner == b && x.Subject == a));
    public static string Pair(Guid a, Guid b) => string.CompareOrdinal(a.ToString(), b.ToString()) < 0 ? $"{a:N}_{b:N}" : $"{b:N}_{a:N}";
    protected Task PairLock(string key) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
    protected Task UserLock(Guid id) => PairLock("user:" + id);
    public static string Text(string? text, int max, bool required = false)
    {
        text = text?.Trim() ?? "";
        if ((required && text.Length == 0) || text.Length > max || text.Contains('\0')) throw new ApiException(400, $"Please enter {(required ? "1–" : "up to ")}{max} characters.");
        return text;
    }
    public static string? Url(string? value, string? host = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 500 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || (host != null && uri.Host != host && !uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))) throw new ApiException(400, "Use a valid HTTPS link for the selected service.");
        return uri.AbsoluteUri;
    }
    public static string Photo(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^/media/[a-f0-9]{32}\.(jpg|png|webp)$")) return value;
        throw new ApiException(400, "Please upload a supported photo.");
    }
    protected void Notice(Guid user, string kind, string text, string url) => db.Notifications.Add(new() { UserId = user, Kind = kind, Text = text, Url = url });
}
