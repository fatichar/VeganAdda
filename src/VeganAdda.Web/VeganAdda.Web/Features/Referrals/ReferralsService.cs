using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Features.Referrals;

public sealed class ReferralsService(FeatureContext context, ReferralTrust trust) : FeatureService(context)
{
    public async Task<ReferralInviteResult> CreateReferralInvite()
    {
        var me = await User();
        if (!await trust.CanCreateReferral(me.Id)) throw new ApiException(403, "Referrals require a complete profile and verified vegan status.");
        var now = DateTimeOffset.UtcNow;
        if (await db.ReferralInvites.CountAsync(i => i.ReferrerId == me.Id && !i.Revoked && !i.ReferredId.HasValue && i.Expires > now) >= 5)
            throw new ApiException(429, "Use or let an earlier referral link expire before creating another.");
        var raw = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(15)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = now.AddDays(30);
        db.ReferralInvites.Add(new() { TokenHash = ReferralTrust.ReferralHash(raw), ReferrerId = me.Id, Expires = expires });
        await db.SaveChangesAsync();
        return new ReferralInviteResult(raw, expires);
    }
}
