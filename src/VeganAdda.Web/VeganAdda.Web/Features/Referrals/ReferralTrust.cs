using Microsoft.EntityFrameworkCore;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Features.Referrals;

public sealed class ReferralTrust(FeatureContext context) : FeatureService(context)
{
    // Walk only this member's lineage. Stop at a trust anchor or invalid ancestor;
    // an unanchored cycle must never grant trust.
    public Task<bool> IsVerified(Guid id) => db.Database.SqlQuery<bool>($"""
        WITH RECURSIVE lineage AS (
            SELECT a."Id", a."Admin", a."Verified", a."Suspended", a."TrustRevoked",
                COALESCE(p."TestProfile", FALSE) AS "TestProfile", ARRAY[a."Id"] AS path, FALSE AS cycle
            FROM "Accounts" a LEFT JOIN "Profiles" p ON p."Id" = a."Id" WHERE a."Id" = {id}
            UNION ALL
            SELECT a."Id", a."Admin", a."Verified", a."Suspended", a."TrustRevoked",
                COALESCE(p."TestProfile", FALSE), l.path || a."Id", a."Id" = ANY(l.path)
            FROM lineage l JOIN "Referrals" r ON r."ReferredId" = l."Id"
            JOIN "Accounts" a ON a."Id" = r."ReferrerId" LEFT JOIN "Profiles" p ON p."Id" = a."Id"
            WHERE NOT l.cycle AND NOT (l."Admin" OR l."Verified" OR l."Suspended" OR l."TrustRevoked" OR l."TestProfile")
        )
        SELECT EXISTS(SELECT 1 FROM lineage WHERE ("Admin" OR "Verified")
            AND NOT ("Suspended" OR "TrustRevoked" OR "TestProfile" OR cycle)) AS "Value"
        """).SingleAsync();

    public Task<List<Guid>> Ancestors(Guid id) => db.Database.SqlQuery<Guid>($"""
        WITH RECURSIVE ancestors AS (
            SELECT "ReferrerId" AS id FROM "Referrals" WHERE "ReferredId" = {id}
            UNION
            SELECT r."ReferrerId" FROM "Referrals" r JOIN ancestors a ON r."ReferredId" = a.id
        ) SELECT id AS "Value" FROM ancestors
        """).ToListAsync();

    public Task<List<Guid>> Descendants(Guid id) => db.Database.SqlQuery<Guid>($"""
        WITH RECURSIVE descendants AS (
            SELECT "ReferredId" AS id FROM "Referrals" WHERE "ReferrerId" = {id}
            UNION
            SELECT r."ReferredId" FROM "Referrals" r JOIN descendants d ON r."ReferrerId" = d.id
        ) SELECT id AS "Value" FROM descendants
        """).ToListAsync();

    public async Task<bool> CanCreateReferral(Guid id) => await IsVerified(id)
        && await db.Profiles.AnyAsync(p => p.Id == id && p.Approved && !p.TestProfile);
    public static string ReferralHash(string code) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));
    public static bool ValidReferralCode(string? code) => code is { Length: 20 } && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
