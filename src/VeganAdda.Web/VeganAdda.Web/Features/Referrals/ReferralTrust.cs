using Microsoft.EntityFrameworkCore;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Features.Referrals;

public sealed class ReferralTrust(FeatureContext context) : FeatureService(context)
{
    public Task<List<Guid>> Ancestors(Guid id) => db.Database.SqlQuery<Guid>($"""
        WITH RECURSIVE ancestors AS (
            SELECT "ReferrerId" AS id FROM "Referrals" WHERE "ReferredId" = {id}
            UNION
            SELECT r."ReferrerId" FROM "Referrals" r JOIN ancestors a ON r."ReferredId" = a.id
        ) SELECT id AS "Value" FROM ancestors
        """).ToListAsync();

    public Task<bool> CanCreateReferral(Guid id) => db.Profiles.Where(BasicProfile.Complete)
        .AnyAsync(p => p.Id == id && p.IsVegan == true && p.VeganVerified && !p.TestProfile
            && db.Accounts.Any(a => a.Id == id && !a.Suspended));
    public static string ReferralHash(string code) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));
    public static bool ValidReferralCode(string? code) => code is { Length: 20 } && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
