using VeganAdda.Contracts;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Verification;

public static class VerificationEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/verification", (VerificationService service) => Json(service.GetVerification())).WithName("Verification_Status").RequireAuthorization();
        api.MapPost("/verification", (HttpContext ctx, VerificationService service) => Json(service.SubmitVerification(Body<VerificationSubmission>(ctx)))).WithName("Verification_Submit").RequireAuthorization();
        api.MapPost("/verification/withdraw", (HttpContext ctx, VerificationService service) => Json(service.WithdrawVerification(Body<VerificationRequestAction>(ctx).Id))).WithName("Verification_Withdraw").RequireAuthorization();
        api.MapGet("/verification/referrers", (HttpContext ctx, VerificationService service) => Json(service.SearchReferrers(Q(ctx, "search")))).WithName("Verification_Referrers").RequireAuthorization();
        api.MapGet("/verification/referrals/{personId:guid}", (Guid personId, VerificationService service) => Json(service.GetReferralCards(personId))).WithName("Verification_ReferralCards").RequireAuthorization();
        api.MapPost("/verification/referrals/{id:guid}/respond", (Guid id, HttpContext ctx, VerificationService service) => Json(service.RespondToReferral(id, Body<VerificationResponse>(ctx)))).WithName("Verification_Respond").RequireAuthorization();
    }
}
