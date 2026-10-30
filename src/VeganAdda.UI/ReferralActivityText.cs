using VeganAdda.Contracts;

namespace VeganAdda.UI;

public static class ReferralActivityText
{
    public static string Format(ReferralActivity activity, string applicantName, bool applicantIsViewer) => activity.Kind switch
    {
        ReferralActivityKind.Requested => applicantIsViewer
            ? "You asked this member to confirm you are vegan."
            : $"{applicantName} asked you to confirm they are vegan.",
        ReferralActivityKind.Confirmed => activity.ByViewer
            ? "You confirmed their vegan status for moderator review."
            : "This member confirmed your vegan status for moderator review.",
        ReferralActivityKind.Declined => activity.ByViewer
            ? "You declined to confirm their vegan status."
            : "This member declined to confirm your vegan status.",
        ReferralActivityKind.Verified => activity.ByViewer
            ? "You confirmed their vegan status."
            : applicantIsViewer ? "Your vegan status was verified." : $"{applicantName}'s vegan status was verified.",
        ReferralActivityKind.Withdrawn => applicantIsViewer
            ? "You withdrew your verification request."
            : $"{applicantName} withdrew their verification request.",
        ReferralActivityKind.ChangesRequested => applicantIsViewer
            ? "A reviewer requested more verification details."
            : "The verification request was returned for more details.",
        _ => throw new ArgumentOutOfRangeException(nameof(activity))
    };
}
