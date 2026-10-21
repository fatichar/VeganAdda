using VeganAdda.Contracts;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Features.Referrals;

public sealed class ReferralsService(FeatureContext context) : FeatureService(context)
{
    public async Task<ReferralInviteResult> CreateReferralInvite()
    {
        await User();
        throw new ApiException(410, "Referral links have been replaced. Connections can confirm vegan status through verification requests.");
    }
}
