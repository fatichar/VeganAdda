using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Referrals;
using VeganAdda.Web.Features.Events;
using static VeganAdda.Web.Features.Profiles.BirthDetails;

namespace VeganAdda.Web.Features.Referrals;

public sealed class ReferralTrust(FeatureContext context) : FeatureService(context)
{
    public async Task<HashSet<Guid>> VerifiedAccounts()
    {
        var accounts = await db.Accounts.Select(a => new { a.Id, a.Admin, a.Verified, a.Suspended, a.TrustRevoked }).ToDictionaryAsync(a => a.Id);
        var testProfiles = await db.Profiles.Where(p => p.TestProfile).Select(p => p.Id).ToHashSetAsync();
        var parent = await db.Referrals.ToDictionaryAsync(r => r.ReferredId, r => r.ReferrerId);
        var resolved = new Dictionary<Guid, bool>();
        foreach (var start in accounts.Keys)
        {
            if (resolved.ContainsKey(start)) continue;
            var path = new List<Guid>(); var seen = new HashSet<Guid>(); var current = start; var value = false;
            while (seen.Add(current))
            {
                if (resolved.TryGetValue(current, out value)) break;
                if (!accounts.TryGetValue(current, out var account) || account.Suspended || account.TrustRevoked || testProfiles.Contains(current)) { value = false; break; }
                if (account.Verified || account.Admin) { value = true; break; }
                path.Add(current);
                if (!parent.TryGetValue(current, out current)) { value = false; break; }
            }
            foreach (var id in path) resolved[id] = value;
            resolved[start] = value;
        }
        return resolved.Where(x => x.Value).Select(x => x.Key).ToHashSet();
    }
    public async Task<bool> IsVerified(Guid id) => (await VerifiedAccounts()).Contains(id);
    public async Task<bool> CanCreateReferral(Guid id) => await IsVerified(id)
        && await db.Profiles.AnyAsync(p => p.Id == id && p.Approved && !p.TestProfile);
    public static string ReferralHash(string code) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));
    public static bool ValidReferralCode(string? code) => code is { Length: 20 } && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
